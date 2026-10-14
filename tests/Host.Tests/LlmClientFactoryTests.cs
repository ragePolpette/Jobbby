using GraphEngine;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Host.Tests;

public class LlmClientFactoryTests
{
    [Fact]
    public void Create_NoProvider_DefaultsToOpenAiCompatible()
    {
        using var httpClient = new HttpClient();

        var selection = LlmClientFactory.Create(Configuration(new()
        {
            ["Llm:Endpoint"] = "https://llm.example/v1/chat/completions",
            ["Llm:ApiKey"] = "secret",
            ["Llm:Model"] = "model",
        }), httpClient);

        Assert.IsType<OpenAiCompatibleLlmClient>(Assert.IsType<CodeFenceStrippingLlmClient>(selection.Client).Inner);
        Assert.Equal("openai", selection.Provider);
    }

    [Fact]
    public void Create_OpenAiWithoutApiKey_Throws()
    {
        using var httpClient = new HttpClient();

        var error = Assert.Throws<InvalidOperationException>(() => LlmClientFactory.Create(Configuration(new()
        {
            ["Llm:Endpoint"] = "https://llm.example/v1/chat/completions",
            ["Llm:Model"] = "model",
        }), httpClient));

        Assert.Contains("Llm:ApiKey", error.Message);
    }

    [Fact]
    public void Create_ClaudeCli_NeedsNoEndpointOrApiKey()
    {
        using var httpClient = new HttpClient();

        var selection = LlmClientFactory.Create(Configuration(new() { ["Llm:Provider"] = "Claude-CLI" }), httpClient);

        Assert.IsType<ClaudeCliLlmClient>(Assert.IsType<CodeFenceStrippingLlmClient>(selection.Client).Inner);
        Assert.Equal("claude-cli", selection.Provider);
        Assert.Null(selection.Endpoint);
        Assert.Equal("sonnet", selection.Model);
    }

    [Fact]
    public void Create_ClaudeCliWithModel_UsesConfiguredModel()
    {
        using var httpClient = new HttpClient();

        var selection = LlmClientFactory.Create(Configuration(new()
        {
            ["Llm:Provider"] = "claude-cli",
            ["Llm:Model"] = "haiku",
        }), httpClient);

        Assert.Equal("haiku", selection.Model);
    }

    [Fact]
    public void Create_UnknownProvider_Throws()
    {
        using var httpClient = new HttpClient();

        var error = Assert.Throws<InvalidOperationException>(() =>
            LlmClientFactory.Create(Configuration(new() { ["Llm:Provider"] = "gemini" }), httpClient));

        Assert.Contains("gemini", error.Message);
    }

    [Fact]
    public void Create_FromSettings_ProviderAndModelComeFromSettings_SecretsFromConfiguration()
    {
        using var httpClient = new HttpClient();
        var secrets = Configuration(new()
        {
            ["Llm:Provider"] = "claude-cli",
            ["Llm:Model"] = "ignored",
            ["Llm:Endpoint"] = "https://llm.example/v1/chat/completions",
            ["Llm:ApiKey"] = "secret",
        });

        var selection = LlmClientFactory.Create(new Config.LlmSettings { Provider = "openai", Model = "gpt-x" }, secrets, httpClient);

        Assert.IsType<OpenAiCompatibleLlmClient>(Assert.IsType<CodeFenceStrippingLlmClient>(selection.Client).Inner);
        Assert.Equal("gpt-x", selection.Model);
    }

    [Fact]
    public void Create_FromSettings_ClaudeCliWithoutModel_UsesDefault()
    {
        using var httpClient = new HttpClient();

        var selection = LlmClientFactory.Create(new Config.LlmSettings { Provider = "claude-cli", Model = null }, Configuration(new()), httpClient);

        Assert.IsType<ClaudeCliLlmClient>(Assert.IsType<CodeFenceStrippingLlmClient>(selection.Client).Inner);
        Assert.Equal("sonnet", selection.Model);
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData("8", 8)]
    [InlineData("0", 4)]
    [InlineData("many", 4)]
    public void Create_OpenAi_CapsConcurrencyFromConfiguration(string? configured, int expected)
    {
        using var httpClient = new HttpClient();

        var selection = LlmClientFactory.Create(Configuration(new()
        {
            ["Llm:Endpoint"] = "https://llm.example/v1/chat/completions",
            ["Llm:ApiKey"] = "secret",
            ["Llm:Model"] = "model",
            ["Llm:MaxConcurrency"] = configured,
        }), httpClient);

        var client = Assert.IsType<OpenAiCompatibleLlmClient>(Assert.IsType<CodeFenceStrippingLlmClient>(selection.Client).Inner);
        Assert.Equal(expected, client.MaxConcurrency);
    }

    [Theory]
    [InlineData(null, PromptLanguage.English)]
    [InlineData("en", PromptLanguage.English)]
    [InlineData("IT", PromptLanguage.Italian)]
    public void Create_ReadsThePromptLanguage_EnglishByDefault(string? configured, PromptLanguage expected)
    {
        using var httpClient = new HttpClient();

        var selection = LlmClientFactory.Create(Configuration(new() { ["Llm:Provider"] = "claude-cli", ["Llm:Language"] = configured }), httpClient);

        Assert.Equal(expected, selection.Language);
    }

    [Fact]
    public void Create_UnknownPromptLanguage_Throws()
    {
        using var httpClient = new HttpClient();

        var error = Assert.Throws<InvalidOperationException>(() =>
            LlmClientFactory.Create(Configuration(new() { ["Llm:Provider"] = "claude-cli", ["Llm:Language"] = "klingon" }), httpClient));

        Assert.Contains("Llm:Language", error.Message);
    }

    [Fact]
    public async Task Create_StripsCodeFences_ForEveryProvider()
    {
        using var httpClient = new HttpClient();
        var runner = new FencedRunner();

        var selection = LlmClientFactory.Create(Configuration(new() { ["Llm:Provider"] = "claude-cli" }), httpClient, runner);

        Assert.Equal("{\"a\":1}", await selection.Client.CompleteAsync("prompt"));
    }

    private sealed class FencedRunner : ICliProcessRunner
    {
        public Task<CliProcessResult> RunAsync(System.Diagnostics.ProcessStartInfo startInfo, string standardInput, CancellationToken cancellationToken) =>
            Task.FromResult(new CliProcessResult(0, """{"is_error":false,"result":"```json\n{\"a\":1}\n```"}""", string.Empty));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
