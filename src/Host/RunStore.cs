using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Config;
using Reporting;

namespace Host;

/// <summary>Reads and writes <c>DataDir/runs/*.json</c> (atomically), and recovers or imports old runs.</summary>
public sealed class RunStore
{
    private static readonly Regex SafeId = new("^[A-Za-z0-9_-]{1,80}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DataDir _dataDir;

    public RunStore(DataDir dataDir) => _dataDir = dataDir;

    public string PathOf(string runId) => Path.Combine(_dataDir.RunsDirectory, runId + ".json");

    public void Save(RunRecord run)
    {
        if (!SafeId.IsMatch(run.RunId))
            throw new ArgumentException($"Invalid run id '{run.RunId}'.", nameof(run));
        AtomicFile.WriteAllText(PathOf(run.RunId), JsonSerializer.Serialize(run, JsonOptions));
    }

    /// <summary>The run, or null if the id is unknown or not a valid run id (never a path outside runs/).</summary>
    public RunRecord? Load(string runId)
    {
        if (!SafeId.IsMatch(runId) || !File.Exists(PathOf(runId)))
            return null;
        return JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(PathOf(runId)), JsonOptions);
    }

    /// <summary>Every readable run, newest first. Unreadable files are skipped, not fatal.</summary>
    public IReadOnlyList<RunRecord> List()
    {
        if (!Directory.Exists(_dataDir.RunsDirectory))
            return Array.Empty<RunRecord>();

        var runs = new List<RunRecord>();
        foreach (var file in Directory.EnumerateFiles(_dataDir.RunsDirectory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(file), JsonOptions) is { RunId.Length: > 0 } run)
                    runs.Add(run);
            }
            catch (JsonException)
            {
                // A foreign or truncated file in runs/ must not hide the other runs.
            }
        }

        return runs.OrderByDescending(run => run.StartedAt).ToList();
    }

    /// <summary>Runs still marked Running when a process starts were cut short: mark them Interrupted.</summary>
    public IReadOnlyList<string> RecoverInterrupted()
    {
        var recovered = new List<string>();
        foreach (var run in List().Where(run => run.Status == RunStatus.Running))
        {
            Save(run with { Status = RunStatus.Interrupted, EndedAt = run.EndedAt ?? DateTimeOffset.UtcNow });
            recovered.Add(run.RunId);
        }

        return recovered;
    }

    /// <summary>
    /// Turns the old <c>run-reports.json</c> into summary-only runs (mode "legacy"), once: the
    /// file is then renamed to <c>run-reports.json.imported</c>. Returns how many were imported.
    /// </summary>
    public int ImportLegacyReports()
    {
        if (!File.Exists(_dataDir.RunReportsPath))
            return 0;

        var reports = RunReportStore.LoadRunReports(_dataDir.RunReportsPath);
        var index = 0;
        foreach (var report in reports)
        {
            Save(new RunRecord
            {
                RunId = $"legacy-{report.RunAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{index++}",
                Mode = "legacy",
                Status = RunStatus.Completed,
                StartedAt = report.RunAt,
                EndedAt = report.RunAt,
                Report = report,
            });
        }

        File.Move(_dataDir.RunReportsPath, _dataDir.RunReportsPath + ".imported", overwrite: true);
        return reports.Count;
    }
}
