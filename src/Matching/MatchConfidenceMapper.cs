namespace Matching;

/// <summary>
/// Converts a stage-two <see cref="MatchJudgment"/> (category + confidence, deliberately
/// separate) into the single numeric MatchConfidence (0-1) the existing ScoreMatch
/// threshold gate consumes.
/// </summary>
public static class MatchConfidenceMapper
{
    public static double ToMatchConfidence(MatchJudgment judgment)
    {
        if (string.Equals(judgment.Category, MatchCategories.Weak, StringComparison.OrdinalIgnoreCase))
            return 0.0; // discarded regardless of how confident the judge was in calling it weak

        return Math.Clamp(judgment.Confidence, 0.0, 1.0);
    }
}
