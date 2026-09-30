using System.Diagnostics;
using Xunit;

namespace Host.Tests;

/// <summary>Runs the real CLI (Host.dll next to the test assembly) as a separate process.</summary>
public class CliTests
{
    [Theory]
    [InlineData("""{"area":{"distanceKm":"trenta"}}""", "distanceKm")]
    [InlineData("""{"area":{"country":"it","where":null}}""", "area.where")]
    [InlineData("""{"area": """, "settings.json")]
    public async Task BrokenSettings_PrintsReadableErrorAndExits2_WithoutStackTrace(string settings, string expected)
    {
        using var dataDir = new TempDir();
        File.WriteAllText(dataDir.Path("settings.json"), settings);

        var (exitCode, output) = await RunCliAsync(dataDir.Root);

        Assert.Equal(2, exitCode);
        Assert.Contains(expected, output);
        Assert.DoesNotContain("Unhandled exception", output);
        Assert.DoesNotContain("   at ", output);
    }

    private static async Task<(int ExitCode, string Output)> RunCliAsync(string dataDir)
    {
        var startInfo = new ProcessStartInfo(Environment.ProcessPath ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Host.dll"));
        startInfo.Environment["Jobbby__DataDir"] = dataDir;
        startInfo.Environment["Jobbby__DryRun"] = "true";

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }
}
