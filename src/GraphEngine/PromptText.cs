namespace GraphEngine;

/// <summary>
/// Helpers for putting untrusted text (postings, CVs) into prompts as data. The text is
/// wrapped in <c>&lt;tag&gt;…&lt;/tag&gt;</c> and every angle bracket inside it becomes a
/// look-alike (‹ ›), so no nesting, attribute or self-closing trick can close the block
/// early or open a fake one. The text stays readable for the model.
/// </summary>
public static class PromptText
{
    public static string Delimit(string tag, string text)
    {
        var clean = text.Replace('<', '‹').Replace('>', '›');
        return $"<{tag}>\n{clean}\n</{tag}>";
    }
}
