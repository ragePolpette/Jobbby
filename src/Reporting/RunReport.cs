namespace Reporting;

/// <summary>
/// Human-facing summary of one Host run, built from a <see cref="RunStatsCollector"/>
/// once every source's postings have finished flowing through the graph.
/// StageTwoBreakdown is keyed by <c>Matching.MatchCategories</c> ("Strong"/"Borderline"/
/// "Weak"); ErrorsPerSource is keyed by source name.
/// </summary>
public sealed record RunReport(
    DateTimeOffset RunAt,
    int TotalFetched,
    int SkippedDuplicate,
    int RejectedStageOne,
    Dictionary<string, int> StageTwoBreakdown,
    int AutoApproved,
    int HumanApproved,
    int HumanRejected,
    int TimedOut,
    Dictionary<string, int> ErrorsPerSource);
