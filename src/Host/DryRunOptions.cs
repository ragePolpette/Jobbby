using Microsoft.Extensions.Configuration;

namespace Host;

public sealed record DryRunOptions(
    bool Enabled,
    int MaxPostingsPerSource,
    int MaxDiscoveryCandidates,
    string LogPath)
{
    public static DryRunOptions FromConfiguration(IConfiguration configuration, string baseDirectory)
    {
        var enabled = bool.TryParse(configuration["Jobbby:DryRun"], out var parsedEnabled) && parsedEnabled;
        var maxPostings = ParseLimit(configuration["Jobbby:DryRunMaxPostingsPerSource"], 3, 20);
        var maxDiscovery = ParseLimit(configuration["Jobbby:DryRunMaxDiscoveryCandidates"], 3, 10);
        var configuredPath = configuration["Jobbby:DryRunLogPath"];
        var defaultPath = Path.Combine(baseDirectory, $"dry-run-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
        var logPath = string.IsNullOrWhiteSpace(configuredPath) ? defaultPath : configuredPath;

        return new DryRunOptions(enabled, maxPostings, maxDiscovery, Path.GetFullPath(logPath, baseDirectory));
    }

    private static int ParseLimit(string? value, int defaultValue, int maximum) =>
        int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 1, maximum) : defaultValue;
}
