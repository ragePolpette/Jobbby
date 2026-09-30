using GraphEngine;
using Microsoft.Extensions.Configuration;

namespace Host;

public sealed record LlmSelection(ILlmClient Client, string Provider, string? Endpoint, string? Model);

/// <summary>
/// Picks the <see cref="ILlmClient"/> from <c>Llm:Provider</c>: <c>openai</c> (default,
/// any OpenAI-compatible chat completions endpoint) or <c>claude-cli</c> (local
/// <c>claude -p</c>, no endpoint or API key needed).
/// </summary>
public static class LlmClientFactory
{
    public const string OpenAiProvider = "openai";
    public const string ClaudeCliProvider = "claude-cli";
    public const string DefaultClaudeModel = "sonnet";

    /// <summary>
    /// Provider and model from <c>settings.json</c>; endpoint, API key and CLI options stay in
    /// configuration (user-secrets / environment), which is where secrets belong.
    /// </summary>
    public static LlmSelection Create(Config.LlmSettings llm, IConfiguration configuration, HttpClient httpClient, ICliProcessRunner? runner = null)
    {
        var overlay = new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = llm.Provider,
                ["Llm:Model"] = llm.Model,
            })
            .Build();
        return Create(overlay, httpClient, runner);
    }

    public static LlmSelection Create(IConfiguration configuration, HttpClient httpClient, ICliProcessRunner? runner = null)
    {
        var provider = (configuration["Llm:Provider"] ?? OpenAiProvider).Trim().ToLowerInvariant();
        return provider switch
        {
            OpenAiProvider => CreateOpenAi(configuration, httpClient),
            ClaudeCliProvider => CreateClaudeCli(configuration, runner),
            _ => throw new InvalidOperationException(
                $"Unknown Llm:Provider '{provider}'. Expected '{OpenAiProvider}' or '{ClaudeCliProvider}'."),
        };
    }

    private static LlmSelection CreateOpenAi(IConfiguration configuration, HttpClient httpClient)
    {
        var endpoint = configuration["Llm:Endpoint"] ?? throw new InvalidOperationException("Missing Llm:Endpoint configuration.");
        var apiKey = configuration["Llm:ApiKey"] ?? throw new InvalidOperationException("Missing Llm:ApiKey secret.");
        var model = configuration["Llm:Model"] ?? throw new InvalidOperationException("Missing Llm:Model configuration.");
        return new LlmSelection(new OpenAiCompatibleLlmClient(httpClient, endpoint, apiKey, model), OpenAiProvider, endpoint, model);
    }

    private static LlmSelection CreateClaudeCli(IConfiguration configuration, ICliProcessRunner? runner)
    {
        var executable = configuration["Llm:ClaudePath"] ?? "claude";
        var model = string.IsNullOrWhiteSpace(configuration["Llm:Model"]) ? DefaultClaudeModel : configuration["Llm:Model"]!;
        var timeout = int.TryParse(configuration["Llm:TimeoutSeconds"], out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : (TimeSpan?)null;
        var maxConcurrency = int.TryParse(configuration["Llm:MaxConcurrency"], out var concurrency) ? concurrency : 2;

        var client = new ClaudeCliLlmClient(executable, model, requestTimeout: timeout, maxConcurrency: maxConcurrency, runner: runner);
        return new LlmSelection(client, ClaudeCliProvider, null, model);
    }
}
