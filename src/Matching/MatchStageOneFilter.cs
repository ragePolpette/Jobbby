using CvExtraction;
using JobPostings;

namespace Matching;

public sealed record MatchStageOneResult(bool Passes, string Reason)
{
    public List<string> MatchedRequirements { get; init; } = new();
    public List<string> MissingRequirements { get; init; } = new();
    public List<string> PreferenceWarnings { get; init; } = new();
}

public static class MatchStageOneFilter
{
    public static MatchStageOneResult Evaluate(JobPosting posting, CvData cv)
    {
        var candidateStack = new SkillMatcher(BuildCandidateStack(cv));
        var requiredStack = posting.RequiredStack ?? new List<string>();
        var mustHaveStack = posting.MustHaveStack ?? new List<string>();
        var preferredStack = posting.PreferredStack ?? new List<string>();
        var matched = requiredStack.Where(candidateStack.Covers).ToList();
        var missing = mustHaveStack.Where(skill => !candidateStack.Covers(skill)).ToList();
        var warnings = preferredStack.Where(skill => !candidateStack.Covers(skill)).Select(skill => $"Competenza preferenziale non presente: {skill}").ToList();

        if (requiredStack.Count > 0 && matched.Count == 0)
            missing.Add($"Nessuna sovrapposizione, serve almeno una competenza tra: {string.Join(", ", requiredStack)}");

        var candidateBand = BandFromYearsExperience(cv.YearsExperience);
        var requiredBand = BandFromLabel(posting.SeniorityLevel);
        if (candidateBand < requiredBand)
            missing.Add($"Seniority {requiredBand}");

        if (posting.RequiredLanguages is { Count: > 0 })
            missing.AddRange(posting.RequiredLanguages.Where(language => !cv.Languages.Contains(language, StringComparer.OrdinalIgnoreCase)).Select(language => $"Lingua: {language}"));

        // Sources report locations like "Milano, Lombardia": a desired "Milano" contained in it counts.
        if (posting.RemoteAvailable == false && cv.DesiredLocations.Count > 0 &&
            !cv.DesiredLocations.Any(desired => (posting.Location ?? string.Empty).Contains(desired, StringComparison.OrdinalIgnoreCase)))
            missing.Add($"Località: {posting.Location ?? "non specificata"}");

        if (posting.SalaryMaximum is not null && cv.MinimumSalary is not null && posting.SalaryMaximum < cv.MinimumSalary)
            missing.Add($"RAL massima {posting.SalaryMaximum} inferiore al minimo {cv.MinimumSalary}");

        if (posting.RemoteAvailable == true && !cv.AcceptsRemote)
            warnings.Add("Il ruolo è remoto ma il candidato preferisce lavoro in sede.");

        var passes = missing.Count == 0;
        var reason = passes
            ? $"Requisiti obbligatori soddisfatti. Competenze in comune: {(matched.Count == 0 ? "nessuna richiesta" : string.Join(", ", matched))}."
            : $"Requisiti mancanti: {string.Join("; ", missing)}.";

        return new MatchStageOneResult(passes, reason)
        {
            MatchedRequirements = matched,
            MissingRequirements = missing,
            PreferenceWarnings = warnings,
        };
    }

    private static IEnumerable<string> BuildCandidateStack(CvData cv) =>
        cv.Skills.Concat(cv.Roles.SelectMany(role => role.Stack));

    private enum SeniorityBand { Junior, Mid, Senior, Staff }

    private static SeniorityBand BandFromYearsExperience(double years) => years switch
    {
        < 2 => SeniorityBand.Junior,
        < 5 => SeniorityBand.Mid,
        < 9 => SeniorityBand.Senior,
        _ => SeniorityBand.Staff,
    };

    private static SeniorityBand BandFromLabel(string label)
    {
        var normalized = (label ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Contains("junior")) return SeniorityBand.Junior;
        if (normalized.Contains("staff") || normalized.Contains("lead") || normalized.Contains("principal")) return SeniorityBand.Staff;
        if (normalized.Contains("senior")) return SeniorityBand.Senior;
        return SeniorityBand.Mid;
    }
}
