using System.Text.Json;

namespace Reporting;

/// <summary>
/// Persists run history and per-source cursors to disk. Reports accumulate - each
/// <see cref="AppendRunReport"/> call preserves every prior entry - while cursors are a
/// current-state snapshot: <see cref="SaveCursors"/> overwrites the file with whatever
/// set is passed in.
/// </summary>
public static class RunReportStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Appends one report to the JSON array at <paramref name="path"/>, keeping every previous entry.</summary>
    public static void AppendRunReport(string path, RunReport report)
    {
        var reports = LoadRunReports(path).ToList();
        reports.Add(report);
        File.WriteAllText(path, JsonSerializer.Serialize(reports, JsonOptions));
    }

    public static IReadOnlyList<RunReport> LoadRunReports(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<List<RunReport>>(File.ReadAllText(path)) ?? new List<RunReport>()
            : new List<RunReport>();

    /// <summary>Overwrites cursors.json with exactly the cursors passed in - one per source.</summary>
    public static void SaveCursors(string path, IReadOnlyCollection<SourceCursor> cursors) =>
        File.WriteAllText(path, JsonSerializer.Serialize(cursors, JsonOptions));

    public static IReadOnlyDictionary<string, SourceCursor> LoadCursors(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, SourceCursor>();

        var cursors = JsonSerializer.Deserialize<List<SourceCursor>>(File.ReadAllText(path)) ?? new List<SourceCursor>();
        return cursors.ToDictionary(c => c.SourceName);
    }
}
