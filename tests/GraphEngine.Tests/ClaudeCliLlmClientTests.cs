using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace GraphEngine.Tests;

public class ClaudeCliLlmClientTests
{
    [Fact]
    public async Task CompleteAsync_SendsPromptOnStdinAndReturnsResult()
    {
        var runner = new ScriptedRunner(Success("{\"ok\":true}"));
        var client = new ClaudeCliLlmClient("claude", model: "sonnet", runner: runner);

        var result = await client.CompleteAsync("test prompt");

        Assert.Equal("{\"ok\":true}", result);
        Assert.Equal("test prompt", runner.LastInput);
        var arguments = runner.LastStartInfo!.ArgumentList.ToList();
        Assert.Equal("claude", runner.LastStartInfo.FileName);
        Assert.Contains("-p", arguments);
        Assert.Equal("json", arguments[arguments.IndexOf("--output-format") + 1]);
        Assert.Equal("", arguments[arguments.IndexOf("--tools") + 1]);
        Assert.Equal("sonnet", arguments[arguments.IndexOf("--model") + 1]);
        Assert.DoesNotContain("test prompt", arguments);
    }

    [Fact]
    public async Task CompleteAsync_NoModel_OmitsModelArgument()
    {
        var runner = new ScriptedRunner(Success("done"));

        await new ClaudeCliLlmClient(runner: runner).CompleteAsync("prompt");

        Assert.DoesNotContain("--model", runner.LastStartInfo!.ArgumentList);
    }

    [Fact]
    public async Task CompleteAsync_ResultWrappedInCodeFence_ReturnsInnerContent()
    {
        var runner = new ScriptedRunner(Success("```json\n{\"a\":1}\n```"));

        var result = await new ClaudeCliLlmClient(runner: runner).CompleteAsync("prompt");

        Assert.Equal("{\"a\":1}", result);
    }

    [Fact]
    public async Task CompleteAsync_CliFailure_RetriesThenSucceeds()
    {
        var runner = new ScriptedRunner(
            new CliProcessResult(1, "", "rate limited"),
            Success("done"));

        var result = await new ClaudeCliLlmClient(runner: runner).CompleteAsync("prompt");

        Assert.Equal("done", result);
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_CliReportsErrorOnEveryAttempt_ThrowsTypedException()
    {
        var error = JsonSerializer.Serialize(new { type = "result", is_error = true, result = "Invalid API key" });
        var runner = new ScriptedRunner(new CliProcessResult(1, error, ""), new CliProcessResult(1, error, ""));

        var exception = await Assert.ThrowsAsync<LlmException>(() => new ClaudeCliLlmClient(runner: runner).CompleteAsync("prompt"));

        Assert.Contains("Invalid API key", exception.Message);
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_InvalidJsonWithZeroExit_ThrowsWithoutRetrying()
    {
        var runner = new ScriptedRunner(new CliProcessResult(0, "not json", ""));

        var exception = await Assert.ThrowsAsync<LlmException>(() => new ClaudeCliLlmClient(runner: runner).CompleteAsync("prompt"));

        Assert.Contains("not valid JSON", exception.Message);
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_Timeout_RetriesAndFailsWithTypedException()
    {
        var runner = new ScriptedRunner(token => Task.Delay(Timeout.InfiniteTimeSpan, token));
        var client = new ClaudeCliLlmClient(runner: runner, requestTimeout: TimeSpan.FromMilliseconds(20));

        var exception = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync("prompt"));

        Assert.Contains("timed out", exception.Message);
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_LimitsConcurrentProcesses()
    {
        var runner = new ScriptedRunner(async token =>
        {
            await Task.Delay(30, token);
        });
        var client = new ClaudeCliLlmClient(runner: runner, maxConcurrency: 2);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.CompleteAsync("prompt")));

        Assert.Equal(2, runner.MaxObservedConcurrency);
    }

    private static CliProcessResult Success(string result) =>
        new(0, JsonSerializer.Serialize(new { type = "result", is_error = false, result }), "");

    private sealed class ScriptedRunner : ICliProcessRunner
    {
        private readonly Queue<CliProcessResult> _results;
        private readonly Func<CancellationToken, Task>? _behavior;
        private int _running;

        public ScriptedRunner(params CliProcessResult[] results) => _results = new Queue<CliProcessResult>(results);

        public ScriptedRunner(Func<CancellationToken, Task> behavior)
        {
            _results = new Queue<CliProcessResult>();
            _behavior = behavior;
        }

        public ProcessStartInfo? LastStartInfo { get; private set; }
        public string? LastInput { get; private set; }
        public int CallCount;
        public int MaxObservedConcurrency;

        public async Task<CliProcessResult> RunAsync(ProcessStartInfo startInfo, string standardInput, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            LastStartInfo = startInfo;
            LastInput = standardInput;
            var running = Interlocked.Increment(ref _running);
            lock (_results)
                MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, running);
            try
            {
                if (_behavior is not null)
                {
                    await _behavior(cancellationToken);
                    return Success("ok");
                }

                lock (_results)
                    return _results.Dequeue();
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }
}
