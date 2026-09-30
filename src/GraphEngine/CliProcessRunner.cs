using System.Diagnostics;

namespace GraphEngine;

public sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Seam between CLI-backed clients (see <see cref="ClaudeCliLlmClient"/>) and the OS
/// process API, so tests can script process results without spawning anything.
/// </summary>
public interface ICliProcessRunner
{
    Task<CliProcessResult> RunAsync(ProcessStartInfo startInfo, string standardInput, CancellationToken cancellationToken);
}

public sealed class CliProcessRunner : ICliProcessRunner
{
    public async Task<CliProcessResult> RunAsync(ProcessStartInfo startInfo, string standardInput, CancellationToken cancellationToken)
    {
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{startInfo.FileName}'.");

        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new CliProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
