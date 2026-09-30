using CvExtraction;
using JobPostings;

namespace Matching;

public sealed record MatchStageOneResult(bool Passes, string Reason)
{
    public List<string> MatchedRequirements { get; init; } = new();
    public List<string> MissingRequirements { get; init; } = new();
    public List<string> PreferenceWarnings { get; init; } = new();
}

/// <summary>
/// Cheap, LLM-free first gate. Skills and languages come from the CV; area, remote and
/// salary from <see cref="StageOneCriteria"/>. Unknown information never excludes a posting:
/// only a stated requirement the candidate does not meet does.
/// </summary>
public static class MatchStageOneFilter
{
    public static MatchStageOneResult Evaluate(JobPosting posting, CvData cv, StageOneCriteria criteria)
    {
        var candidateSkillMatcher = new SkillMatcher(BuildCandidateSkills(cv), criteria.Aliases);
        var requiredSkills = posting.RequiredSkills ?? new List<string>();
        var mustHaveSkills = posting.MustHaveSkills ?? new List<string>();
        var preferredSkills = posting.PreferredSkills ?? new List<string>();
        var matched = requiredSkills.Where(candidateSkillMatcher.Covers).ToList();
        var missing = mustHaveSkills.Where(skill => !candidateSkillMatcher.Covers(skill)).ToList();
        var warnings = preferredSkills.Where(skill => !candidateSkillMatcher.Covers(skill)).Select(skill => $"Competenza preferenziale non presente: {skill}").ToList();

        if (requiredSkills.Count > 0 && matched.Count == 0)
            missing.Insert(0, $"Nessuna sovrapposizione, serve almeno una competenza tra: {string.Join(", ", requiredSkills)}");

        // Years only: seniority labels vary by profession and language, stage two weighs them.
        if (posting.MinYearsExperience is { } requiredYears && cv.YearsExperience < requiredYears)
            missing.Add($"Esperienza: richiesti almeno {requiredYears:0.#} anni, nel CV {cv.YearsExperience:0.#}");

        if (posting.RequiredLanguages is { Count: > 0 })
            missing.AddRange(posting.RequiredLanguages.Where(language => !cv.Languages.Contains(language, StringComparer.OrdinalIgnoreCase)).Select(language => $"Lingua: {language}"));

        CheckLocation(posting, criteria, missing, warnings);

        if (criteria.MinimumYearlySalary is { } minimum && posting.SalaryMaximum is { } salary && salary < minimum)
            missing.Add($"Retribuzione massima {salary:0.##} inferiore al minimo {minimum:0.##}");

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

    private static void CheckLocation(JobPosting posting, StageOneCriteria criteria, List<string> missing, List<string> warnings)
    {
        if (posting.Sweep == SearchSweep.Remote)
        {
            // The remote sweep ignored the area on purpose: only genuinely remote jobs belong in it.
            if (posting.WorkMode != WorkMode.Remote)
                missing.Add($"Località: {posting.Location ?? "non specificata"} (fuori zona e non interamente da remoto)");
            return;
        }

        if (posting.WorkMode == WorkMode.Remote && !criteria.AcceptsRemote)
            warnings.Add("Il ruolo è da remoto, ma nelle impostazioni il remoto non è tra le preferenze.");

        // The source already restricted the search to the area (e.g. "Milano +30 km" includes
        // Sesto San Giovanni): re-checking the text would wrongly exclude nearby places.
        if (criteria.GeoFilteredBySource || criteria.Where.Length == 0)
            return;

        // Hybrid counts as on-site: the candidate still has to get there.
        if (posting.WorkMode is WorkMode.Onsite or WorkMode.Hybrid &&
            !(posting.Location ?? string.Empty).Contains(criteria.Where, StringComparison.OrdinalIgnoreCase))
            missing.Add($"Località: {posting.Location ?? "non specificata"}");
    }

    private static IEnumerable<string> BuildCandidateSkills(CvData cv) =>
        cv.Skills.Concat(cv.Roles.SelectMany(role => role.Skills));
}
