namespace Host;

/// <summary>Every message the CLI prints, in one place.</summary>
public static class CliText
{
    public const string Usage = "Usage: decide <postingId> approve|reject|applied";
    public const string InterruptRequested = "Interrupt requested: stopping the calls in flight...";
    public const string NothingPending = "No postings waiting for a decision.";

    public static string Error(string message) => $"Error: {message}";

    public static string UnknownCommand(string command) =>
        $"Unknown command: '{command}'. Commands: pending, decide <postingId> approve|reject|applied (no arguments: a run).";

    public static string RecoveredInterruptedRun(string runId) => $"Run {runId} was still marked as running: marked as interrupted.";

    public static string ImportedLegacyReports(int count, string directory) => $"Imported {count} summaries from run-reports.json into {directory}.";

    public static string CreatedSettings(string path) => $"Created {path} with the default settings.";

    public static string InvalidSettings(string path) => $"Invalid settings in {path}:";

    public static string Llm(string provider, string? model) => $"LLM: {provider} {model}";

    public static string IgnoredCvField(string field) => $"CV field ignored, it is now set in settings.json: {field}";

    public static string Cv(string name, double years, string path) => $"CV: {name} ({years} years) from {path}";

    public static string AdzunaCalls(int count) => $"Adzuna calls: {count}";

    public static string RunSaved(string path) => $"Run saved to {path}";

    public static string PendingCount(int count) => $"{count} waiting for a decision. Use: decide <postingId> approve|reject|applied";

    public static string UnknownAction(string action, IEnumerable<string> allowed) => $"Unknown action: '{action}'. Allowed values: {string.Join(", ", allowed)}.";

    public static string UnknownPosting(string postingId) => $"Unknown posting: '{postingId}'. Use 'pending' to see the ids.";
}
