using Config;

namespace Matching;

/// <summary>
/// The user's non-CV requirements stage one checks, taken from the settings.
/// <see cref="GeoFilteredBySource"/> means the source already restricted the local search
/// to <see cref="Where"/>, so a posting's location is not re-checked by text.
/// </summary>
/// <param name="Aliases">The user's skill equivalences (null = none).</param>
public sealed record StageOneCriteria(string Where, bool GeoFilteredBySource, bool AcceptsRemote, decimal? MinimumYearlySalary, SkillAliases? Aliases = null)
{
    /// <summary>No area, remote accepted, no salary floor: only CV-based rules apply.</summary>
    public static StageOneCriteria None { get; } = new(string.Empty, GeoFilteredBySource: false, AcceptsRemote: true, MinimumYearlySalary: null);

    public static StageOneCriteria FromSettings(AreaSettings area, SalarySettings salary, SkillAliases? aliases = null)
    {
        var where = area.Where.Trim();
        return new StageOneCriteria(where, GeoFilteredBySource: where.Length > 0, area.AcceptsRemote, salary.MinimumYearly, aliases);
    }
}
