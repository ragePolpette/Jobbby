namespace Matching;

/// <summary>
/// Stage-two LLM judgment on a candidate/posting pair. Category and Confidence are
/// deliberately separate, not a single blended score: a judgment can be a confident
/// "Weak" just as easily as an unsure "Strong".
/// </summary>
public sealed record MatchJudgment(string Category, string Reasoning, double Confidence);

/// <summary>Valid values for <see cref="MatchJudgment.Category"/>.</summary>
public static class MatchCategories
{
    public const string Strong = "Strong";
    public const string Borderline = "Borderline";
    public const string Weak = "Weak";

    /// <summary>The canonical spelling of a category, ignoring case and surrounding spaces; null when it is none of them.</summary>
    public static string? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "strong" => Strong,
        "borderline" => Borderline,
        "weak" => Weak,
        _ => null,
    };
}
