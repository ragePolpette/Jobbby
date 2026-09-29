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
    }

    [Fact]
    public void Create_UnknownProvider_Throws()
    {
        using var httpClient = new HttpClient();

        var error = Assert.Throws<InvalidOperationException>(() =>
            LlmClientFactory.Create(Configuration(new() { ["Llm:Provider"] = "gemini" }), httpClient));

        Assert.Contains("gemini", error.Message);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
