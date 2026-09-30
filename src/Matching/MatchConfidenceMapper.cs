namespace Matching;

/// <summary>
/// Converts a stage-two <see cref="MatchJudgment"/> (category + confidence, deliberately
/// separate) into the single numeric MatchConfidence (0-1) the existing ScoreMatch
/// threshold gate consumes.
/// </summary>
public static class MatchConfidenceMapper
{
    /// <summary>
    /// The judge's confidence, clamped to 0-1. It measures how sure the judge is, not how good
    /// the fit is: a Weak verdict keeps its confidence and is rejected by its category instead.
    /// </summary>
    public static double ToMatchConfidence(MatchJudgment judgment) => Math.Clamp(judgment.Confidence, 0.0, 1.0);
}
