using System.Text.RegularExpressions;
using Config;

namespace Matching;

/// <summary>
/// Decides whether a candidate's skills cover a skill named in a posting. CVs and postings
/// name the same skill differently, so exact string equality rejects good matches. Built-in
/// rules are profession-neutral: case, spacing and a trailing version number are ignored,
/// and a more specific variant covers the generic skill ("Triage infermieristico" covers
/// "Triage", ".NET Core" covers ".NET"). Anything profession-specific ("csharp" = "C#",
/// "ASP.NET Core" implies ".NET") comes from <see cref="SkillAliases"/>, i.e. from the
/// user's data. A coarse stage-one check: it errs toward matching, stage two's LLM
/// judgment does the fine-grained comparison.
/// </summary>
public sealed class SkillMatcher
{
    private readonly Dictionary<string, string> _aliases;
    private readonly HashSet<string> _candidateSkills;

    public SkillMatcher(IEnumerable<string> candidateSkills, SkillAliases? aliases = null)
    {
        aliases ??= SkillAliases.Empty;
        _aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (spelling, canonical) in aliases.Aliases)
            _aliases[Clean(spelling)] = Clean(canonical);

        var implies = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (skill, implied) in aliases.Implies)
            implies[Normalize(skill)] = implied.Select(Normalize).ToList();

        _candidateSkills = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in candidateSkills.Select(Normalize).Where(skill => skill.Length > 0))
        {
            _candidateSkills.Add(skill);
            if (implies.TryGetValue(skill, out var implied))
                _candidateSkills.UnionWith(implied);
        }
    }

    public bool Covers(string requiredSkill)
    {
        var required = Normalize(requiredSkill);
        if (required.Length == 0)
            return false;

        return _candidateSkills.Any(candidate =>
            candidate == required ||
            candidate.StartsWith(required + " ", StringComparison.Ordinal) ||
            required.StartsWith(candidate + " ", StringComparison.Ordinal));
    }

    private string Normalize(string skill)
    {
        var cleaned = Clean(skill);
        return _aliases.TryGetValue(cleaned, out var canonical) ? canonical : cleaned;
    }

    private static string Clean(string skill)
    {
        var normalized = Regex.Replace(skill.Trim().ToLowerInvariant(), @"\s+", " ");
        // A trailing version carries no matching signal: "Excel 2019" -> "excel", ".NET 8" -> ".net".
        return Regex.Replace(normalized, @"\s+v?\d+(\.\d+)*(\.x)?\+?$", string.Empty);
    }
}
