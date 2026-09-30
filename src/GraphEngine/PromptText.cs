using System.Text.RegularExpressions;

namespace GraphEngine;

/// <summary>
/// Helpers for putting untrusted text (postings, CVs) into prompts as data. The text is
/// wrapped in <c>&lt;tag&gt;…&lt;/tag&gt;</c> and any occurrence of that tag inside it is
/// removed, so the text can neither close its block early nor open a fake one.
/// </summary>
public static class PromptText
{
    public static string Delimit(string tag, string text)
    {
        var clean = Regex.Replace(text, $@"<\s*/?\s*{Regex.Escape(tag)}\s*>", " ", RegexOptions.IgnoreCase);
        return $"<{tag}>\n{clean}\n</{tag}>";
    }
}
