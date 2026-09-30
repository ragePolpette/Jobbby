using System.Text.RegularExpressions;

namespace Matching;

/// <summary>
/// Decides whether a candidate's skills cover a skill named in a posting. CVs and
/// postings name the same technology differently (".NET Core" vs ".NET", "API REST" vs
/// "REST API", "Vue" vs "Vue.js"), so exact string equality rejects good matches. Skills
/// are normalized (case, spacing, trailing versions, known aliases) and two skills match
/// when they are equal or one is a more specific variant of the other (".NET Core" and
/// ".NET" share the ".net" root). This is a coarse stage-one check: it errs toward
/// matching, stage two's LLM judgment does the fine-grained comparison.
/// </summary>
public sealed class SkillMatcher
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["csharp"] = "c#",
        ["c sharp"] = "c#",
        ["dotnet"] = ".net",
        ["dot net"] = ".net",
        ["net"] = ".net",
        ["api rest"] = "rest api",
        ["rest"] = "rest api",
        ["restful"] = "rest api",
        ["restful api"] = "rest api",
        ["restful apis"] = "rest api",
        ["rest apis"] = "rest api",
        ["ef"] = "entity framework",
        ["ef core"] = "entity framework core",
        ["js"] = "javascript",
        ["ts"] = "typescript",
        ["vue"] = "vue.js",
        ["vuejs"] = "vue.js",
        ["react.js"] = "react",
        ["reactjs"] = "react",
        ["node"] = "node.js",
        ["nodejs"] = "node.js",
        ["mssql"] = "sql server",
        ["ms sql"] = "sql server",
        ["microsoft sql server"] = "sql server",
        ["postgres"] = "postgresql",
        ["k8s"] = "kubernetes",
        ["golang"] = "go",
    };

    /// <summary>Knowing the key implies knowing each value, beyond what the prefix rule covers.</summary>
    private static readonly Dictionary<string, string[]> Implications = new(StringComparer.Ordinal)
    {
        ["asp.net"] = new[] { ".net" },
        ["asp.net core"] = new[] { ".net", ".net core" },
        ["asp.net mvc"] = new[] { ".net" },
        ["entity framework"] = new[] { ".net" },
        ["entity framework core"] = new[] { ".net", ".net core" },
        ["wcf"] = new[] { ".net" },
        ["sql server"] = new[] { "sql" },
        ["postgresql"] = new[] { "sql" },
        ["mysql"] = new[] { "sql" },
        ["typescript"] = new[] { "javascript" },
    };

    private readonly HashSet<string> _candidateSkills;

    public SkillMatcher(IEnumerable<string> candidateSkills)
    {
        _candidateSkills = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in candidateSkills.Select(Normalize).Where(skill => skill.Length > 0))
        {
            _candidateSkills.Add(skill);
            if (Implications.TryGetValue(skill, out var implied))
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

    public static string Normalize(string skill)
    {
        var normalized = Regex.Replace(skill.Trim().ToLowerInvariant(), @"\s+", " ");
        // Trailing versions carry no matching signal: ".NET 8" -> ".net", "Vue 3" -> "vue".
        normalized = Regex.Replace(normalized, @"\s+v?\d+(\.\d+)*(\.x)?\+?$", string.Empty);
        return Aliases.TryGetValue(normalized, out var canonical) ? canonical : normalized;
    }
}
