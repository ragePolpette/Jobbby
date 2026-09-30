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

        Assert.IsType<OpenAiCompatibleLlmClient>(selection.Client);
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

        Assert.IsType<ClaudeCliLlmClient>(selection.Client);
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

        Assert.IsType<OpenAiCompatibleLlmClient>(selection.Client);
        Assert.Equal("gpt-x", selection.Model);
    }

    [Fact]
    public void Create_FromSettings_ClaudeCliWithoutModel_UsesDefault()
    {
        using var httpClient = new HttpClient();

        var selection = LlmClientFactory.Create(new Config.LlmSettings { Provider = "claude-cli", Model = null }, Configuration(new()), httpClient);

        Assert.IsType<ClaudeCliLlmClient>(selection.Client);
        Assert.Equal("sonnet", selection.Model);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
