using CvExtraction;
using JobPostings;

namespace Matching;

/// <summary>
/// Cheap, deterministic first-stage filter - zero LLM calls. Checks whether the
/// candidate's skills (from CV.Skills and every role's Stack) overlap at all with the
/// posting's RequiredStack, and whether the candidate's experience band meets the
/// posting's seniority requirement. Both checks are intentionally lenient (any overlap,
/// any band at or above the requirement) since this is only meant to weed out postings
/// with no realistic chance, leaving nuanced judgment to stage two.
/// </summary>
public static class MatchStageOneFilter
{
    public static (bool Passes, string Reason) Evaluate(JobPosting posting, CvData cv)
    {
        var candidateStack = BuildCandidateStack(cv);
        var requiredStack = posting.RequiredStack ?? new List<string>();

        var overlap = requiredStack
            .Where(required => candidateStack.Contains(required))
            .ToList();

        var stackPasses = requiredStack.Count == 0 || overlap.Count > 0;

        var candidateBand = BandFromYearsExperience(cv.YearsExperience);
        var requiredBand = BandFromLabel(posting.SeniorityLevel);
        var seniorityPasses = candidateBand >= requiredBand;

        if (!stackPasses)
        {
            return (false,
                $"Nessuna sovrapposizione tra lo stack richiesto ({string.Join(", ", requiredStack)}) e le competenze del candidato.");
        }

        if (!seniorityPasses)
        {
            return (false,
                $"Seniority insufficiente: candidato {candidateBand} ({cv.YearsExperience} anni), ruolo richiede {requiredBand}.");
        }

        var overlapText = overlap.Count > 0 ? string.Join(", ", overlap) : "nessun requisito di stack";
        return (true, $"Stack in comune: {overlapText}. Seniority {candidateBand} soddisfa il requisito {requiredBand}.");
    }

    private static HashSet<string> BuildCandidateStack(CvData cv)
    {
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in cv.Skills)
            stack.Add(skill);

        foreach (var role in cv.Roles)
            foreach (var tech in role.Stack)
                stack.Add(tech);

        return stack;
    }

    private enum SeniorityBand
    {
        Junior = 0,
        Mid = 1,
        Senior = 2,
        Staff = 3,
    }

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

        if (normalized.Contains("junior"))
            return SeniorityBand.Junior;

        if (normalized.Contains("staff") || normalized.Contains("lead") || normalized.Contains("principal"))
            return SeniorityBand.Staff;

        if (normalized.Contains("senior"))
            return SeniorityBand.Senior;

        return SeniorityBand.Mid; // "mid", unrecognized, or empty
    }
}
