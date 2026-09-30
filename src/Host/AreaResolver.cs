using Config;
using CvExtraction;

namespace Host;

/// <param name="Area">The area a run actually searches: settings plus, when enabled, the CV location.</param>
public sealed record ResolvedArea(AreaSettings Area, IReadOnlyList<string> Notes, IReadOnlyList<string> Warnings);

/// <summary>
/// Decides where a run searches: a configured <c>area.where</c> always wins; otherwise, with
/// <c>area.whereFromCv</c> on, the CV's location; otherwise the whole country. A radius without
/// any resolved place is dropped with a warning, since Adzuna needs a centre for it.
/// </summary>
public static class AreaResolver
{
    public static ResolvedArea Resolve(AreaSettings area, CvData cv)
    {
        var notes = new List<string>();
        var warnings = new List<string>();
        var where = area.Where.Trim();

        if (where.Length > 0)
        {
            notes.Add($"Zona di ricerca dalle impostazioni: {where}.");
        }
        else if (area.WhereFromCv && !string.IsNullOrWhiteSpace(cv.Location))
        {
            where = cv.Location.Trim();
            notes.Add($"Zona di ricerca dal CV: {where}.");
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

        return new ResolvedArea(area with { Where = where, DistanceKm = distance }, notes, warnings);
    }
}
