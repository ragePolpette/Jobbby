using System.Text.RegularExpressions;
using Config;
using CvExtraction;

namespace Host;

/// <param name="Area">The area a run actually searches: settings plus, when enabled, the CV location.</param>
/// <param name="FromCv">True when the place came from the CV (so the user can be warned if it finds nothing).</param>
public sealed record ResolvedArea(AreaSettings Area, bool FromCv, IReadOnlyList<string> Notes, IReadOnlyList<string> Warnings);

/// <summary>
/// Decides where a run searches: a configured <c>area.where</c> always wins; otherwise, with
/// <c>area.whereFromCv</c> on, the CV's location reduced to a place name; otherwise the whole
/// country. A radius without any resolved place is dropped with a warning, since Adzuna needs
/// a centre for it.
/// </summary>
public static class AreaResolver
{
    public static ResolvedArea Resolve(AreaSettings area, CvData cv)
    {
        var notes = new List<string>();
        var warnings = new List<string>();
        var where = CollapseWhitespace(area.Where);
        var fromCv = false;

        if (where.Length > 0)
        {
            notes.Add($"Zona di ricerca dalle impostazioni: {where}.");
        }
        else if (area.WhereFromCv && PlaceName(cv.Location) is { Length: > 0 } place)
        {
            where = place;
            fromCv = true;
            notes.Add(place == CollapseWhitespace(cv.Location)
                ? $"Zona di ricerca dal CV: {place}."
                : $"Zona di ricerca dal CV: {place} (da \"{CollapseWhitespace(cv.Location)}\").");
        }
        else
        {
            notes.Add("Nessuna zona: ricerca in tutto il paese.");
        }

        var distance = area.DistanceKm;
        if (where.Length == 0 && distance is not null)
        {
            warnings.Add("Raggio configurato ma nessuna località (né nelle impostazioni né nel CV): il raggio viene ignorato.");
            distance = null;
        }

        return new ResolvedArea(area with { Where = where, DistanceKm = distance }, fromCv, notes, warnings);
    }

    /// <summary>
    /// Reduces what a CV says about residence to something a job search understands:
    /// "Bologna (BO)", "Via Roma 1, 40100 Bologna", "Bologna, Emilia-Romagna" → "Bologna".
    /// The comma part carrying a postcode wins (the usual "postcode city" address line),
    /// otherwise the first part; parentheses and digits are dropped.
    /// </summary>
    internal static string PlaceName(string? location)
    {
        var text = Regex.Replace(CollapseWhitespace(location), @"\([^)]*\)", " ");
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return string.Empty;

        var chosen = parts.FirstOrDefault(part => Regex.IsMatch(part, @"\b\d{4,6}\b")) ?? parts[0];
        return CollapseWhitespace(Regex.Replace(chosen, @"\d+", " "));
    }

    private static string CollapseWhitespace(string? value) =>
        Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
}
