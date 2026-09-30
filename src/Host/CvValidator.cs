using Config;
using CvExtraction;

namespace Host;

/// <summary>Cleans up a structured CV (from the form, an upload or the LLM) and reports what cannot be fixed silently.</summary>
public static class CvValidator
{
    public const double MaxYearsExperience = 80;

    public static IReadOnlyList<SettingsError> Validate(CvData cv)
    {
        var errors = new List<SettingsError>();
        if (!double.IsFinite(cv.YearsExperience) || cv.YearsExperience < 0 || cv.YearsExperience > MaxYearsExperience)
            errors.Add(new SettingsError("yearsExperience", $"Gli anni di esperienza devono essere un numero tra 0 e {MaxYearsExperience:0}."));
        return errors;
    }

    /// <summary>Trims every text, drops empty entries and case-insensitive duplicates, and roles with neither title nor company.</summary>
    public static CvData Normalize(CvData cv) => cv with
    {
        Name = cv.Name?.Trim() ?? string.Empty,
        Seniority = cv.Seniority?.Trim() ?? string.Empty,
        Location = string.IsNullOrWhiteSpace(cv.Location) ? null : cv.Location.Trim(),
        Skills = Clean(cv.Skills),
        Languages = Clean(cv.Languages),
        Roles = cv.Roles
            .Select(role => new CvRole
            {
                Title = role.Title?.Trim() ?? string.Empty,
                Company = role.Company?.Trim() ?? string.Empty,
                Skills = Clean(role.Skills),
                Highlights = Clean(role.Highlights),
            })
            .Where(role => role.Title.Length > 0 || role.Company.Length > 0)
            .ToList(),
    };

    private static List<string> Clean(IEnumerable<string?> values) =>
        values
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
