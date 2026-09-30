using System.Diagnostics;
using System.Text.Json;

namespace GraphEngine;

/// <summary>
/// Runs prompts through the Claude Code CLI in non-interactive mode (<c>claude -p</c>),
/// reusing whatever authentication the CLI already has (subscription token or API key).
/// Meant for local testing: every call spawns a process, so it is slower than
/// <see cref="OpenAiCompatibleLlmClient"/>. Tools, MCP servers and user/project settings
/// are disabled so the CLI behaves as a plain completion endpoint.
/// </summary>
public sealed class ClaudeCliLlmClient : ILlmClient
{
    internal const string SystemPrompt =
        "Sei un motore di completamento usato da un programma. Rispondi solo con il contenuto richiesto dal prompt, " +
        "senza blocchi di codice markdown, senza premesse e senza commenti. Se il prompt chiede JSON, restituisci solo JSON valido.";

    private readonly ICliProcessRunner _runner;
    private readonly string _executable;
    private readonly string? _model;
    private readonly int _maxAttempts;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _concurrency;

    public ClaudeCliLlmClient(
        string executable = "claude",
        string? model = null,
        int maxAttempts = 2,
        TimeSpan? requestTimeout = null,
        int maxConcurrency = 2,
        ICliProcessRunner? runner = null)
    {
        _executable = executable;
        _model = string.IsNullOrWhiteSpace(model) ? null : model;
        _maxAttempts = Math.Max(1, maxAttempts);
        _requestTimeout = requestTimeout ?? TimeSpan.FromMinutes(2);
        _concurrency = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        _runner = runner ?? new CliProcessRunner();
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Exception? lastError = null;
            for (var attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                try
                {
                    return await RunOnceAsync(prompt, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsRetryable(ex, cancellationToken))
                {
                    lastError = ex;
                }
            }

            var reason = lastError is OperationCanceledException ? "timed out" : lastError?.Message;
            throw new LlmException($"The Claude CLI request failed after {_maxAttempts} attempts: {reason}", lastError);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    internal ProcessStartInfo BuildStartInfo()
    {
        var startInfo = new ProcessStartInfo(_executable)
        {
            // A neutral directory keeps the CLI from picking up the repository's CLAUDE.md.
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var argument in new[]
                 {
                     "-p",
                     "--output-format", "json",
                     "--no-session-persistence",
                     "--tools", "",
                     "--strict-mcp-config",
                     "--setting-sources", "",
                     "--system-prompt", SystemPrompt,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (_model is not null)
        {
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(_model);
        }

        return startInfo;
    }

    private async Task<string> RunOnceAsync(string prompt, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);

        CliProcessResult result;
        try
        {
            result = await _runner.RunAsync(BuildStartInfo(), prompt, timeout.Token).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new LlmException($"Could not run '{_executable}'. Is Claude Code installed and on PATH?", ex);
        }

        return ParseResult(result);
    }

    private static string ParseResult(CliProcessResult result)
    {
        string? content;
        bool isError;
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            isError = root.TryGetProperty("is_error", out var errorFlag) && errorFlag.ValueKind == JsonValueKind.True;
            content = root.TryGetProperty("result", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException ex)
        {
            if (result.ExitCode != 0)
                throw new CliFailureException($"Claude CLI exited with code {result.ExitCode}: {Truncate(result.StandardError)}");
            throw new LlmException("The Claude CLI output was not valid JSON.", ex);
        }

        if (isError || result.ExitCode != 0)
            throw new CliFailureException($"Claude CLI reported an error (exit code {result.ExitCode}): {Truncate(content ?? result.StandardError)}");

        return string.IsNullOrWhiteSpace(content)
            ? throw new LlmException("The Claude CLI response did not contain a result.")
            : StripCodeFence(content);
    }

    /// <summary>Removes a single wrapping ```/```json fence, which models occasionally add despite the prompt.</summary>
    internal static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("```", StringComparison.Ordinal) || trimmed.Length < 6)
            return trimmed;

        var firstNewLine = trimmed.IndexOf('\n');
        if (firstNewLine < 0)
            return trimmed;

        return trimmed[(firstNewLine + 1)..^3].Trim();
    }

    private static string Truncate(string value) =>
        value.Length <= 300 ? value.Trim() : value[..300].Trim() + "…";

    private static bool IsRetryable(Exception exception, CancellationToken callerToken) =>
        exception is CliFailureException ||
        exception is OperationCanceledException && !callerToken.IsCancellationRequested;

    /// <summary>Non-zero exit or CLI-reported error: possibly transient (rate limit, network), so retried.</summary>
    private sealed class CliFailureException(string message) : Exception(message);
}
