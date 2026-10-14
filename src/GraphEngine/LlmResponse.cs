using System.Text.Json;

namespace GraphEngine;

/// <summary>Turning raw LLM output into typed data: fences removed, malformed JSON reported as <see cref="LlmException"/>.</summary>
public static class LlmResponse
{
    /// <summary>Removes a single wrapping ```/```json fence, which models occasionally add despite the prompt.</summary>
    public static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstNewLine = trimmed.IndexOf('\n');
        var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine < 0 || end <= firstNewLine ? trimmed : trimmed[(firstNewLine + 1)..end].Trim();
    }

    /// <param name="what">What the response should have been, for the error message (e.g. "a match judgment").</param>
    public static T Deserialize<T>(string response, JsonSerializerOptions options, string what)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(StripCodeFence(response), options)
                ?? throw new LlmException($"The LLM response was empty instead of {what}.");
        }
        catch (JsonException ex)
        {
            throw new LlmException($"The LLM response was not valid JSON for {what}.", ex);
        }
    }

    /// <summary>The value trimmed and cut to <paramref name="maxLength"/> characters; empty for null.</summary>
    public static string Clamp(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }
}
