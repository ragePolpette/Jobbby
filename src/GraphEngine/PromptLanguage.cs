namespace GraphEngine;

/// <summary>The language the prompts are written in (<c>Llm:Language</c>): English unless the CV and postings are Italian.</summary>
public enum PromptLanguage
{
    English,
    Italian,
}

public static class PromptLanguages
{
    public static PromptLanguage Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "en" or "english" => PromptLanguage.English,
        "it" or "italian" or "italiano" => PromptLanguage.Italian,
        _ => throw new InvalidOperationException($"Unknown Llm:Language '{value}'. Expected 'en' or 'it'."),
    };
}
