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
}
