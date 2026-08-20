using System.Text.RegularExpressions;

namespace ApplicationLedger;

/// <summary>
/// Produces the dedup key used to tell whether a job posting has already been applied
/// to. "Acme Corp" and "acme corp  " must normalize to the same key.
/// </summary>
public static class DedupeKey
{
    private const string Separator = "::";

    public static string Normalize(string company, string title) =>
        $"{NormalizePart(company)}{Separator}{NormalizePart(title)}";

    private static string NormalizePart(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ").ToLowerInvariant();
}
