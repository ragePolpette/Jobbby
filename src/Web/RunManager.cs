using Config;
using CvExtraction;
using Host;

namespace Web;

/// <summary>One run in progress, as the UI sees it: status, events so far, and the way to stop it.</summary>
public sealed class ActiveRun
{
    private readonly List<RunEvent> _events = new();
    private readonly object _lock = new();

    public ActiveRun(string runId, RunMode mode)
    {
        RunId = runId;
        Mode = mode;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public string RunId { get; }
    public RunMode Mode { get; }
    public DateTimeOffset StartedAt { get; }
    public CancellationTokenSource Cancellation { get; } = new();

    /// <summary>running, completed, interrupted or failed.</summary>
    public string Status { get; private set; } = "running";
    public string? Error { get; private set; }
    public RunSummary? Summary { get; private set; }
    public bool Finished => Status != "running";

    public void Add(RunEvent runEvent)
    {
        lock (_lock)
            _events.Add(runEvent);
    }

    public (IReadOnlyList<RunEvent> Events, bool Finished) Read(int from)
    {
        lock (_lock)
            return (_events.Skip(from).ToList(), Finished);
    }

    public void Complete(RunSummary summary)
    {
        lock (_lock)
        {
            Summary = summary;
            Status = summary.Cancelled ? "interrupted" : "completed";
        }
    }

    public void Fail(string error)
    {
        lock (_lock)
        {
            Error = error;
            Status = Cancellation.IsCancellationRequested ? "interrupted" : "failed";
        }
    }
}

public abstract record StartResult
{
    public sealed record Started(ActiveRun Run) : StartResult;
    public sealed record AlreadyRunning(ActiveRun Run) : StartResult;
    public sealed record Invalid(IReadOnlyList<SettingsError> Errors) : StartResult;
}

/// <summary>
/// Starts runs in the background, one at a time, sharing the process's single ledger with the
/// decision APIs so neither overwrites the other.
/// </summary>
public sealed class RunManager(
    DataDir dataDir,
    SettingsService settingsService,
    LedgerHolder ledgers,
    IRunDependenciesFactory dependencies,
    IConfiguration configuration,
    ILogger<RunManager> logger)
{
    private readonly object _lock = new();
    private ActiveRun? _current;

    public ActiveRun? Current
    {
        get
        {
            lock (_lock)
                return _current;
        }
    }

    public StartResult Start(RunMode mode)
    {
        lock (_lock)
        {
            if (_current is { Finished: false } running)
                return new StartResult.AlreadyRunning(running);

            var settings = settingsService.Load();
            var errors = SettingsValidator.Validate(settings, forRun: true).ToList();
            var cvPath = CvLocator.Find(dataDir, configuration);
            if (cvPath is null)
                errors.Add(new SettingsError("cv", CvLocator.NotFoundMessage(dataDir)));
            if (errors.Count > 0)
                return new StartResult.Invalid(errors);

            var run = new ActiveRun(JobbbyRunner.NewRunId(mode), mode);
            _current = run;
            _ = Task.Run(() => ExecuteAsync(run, settings, cvPath!));
            return new StartResult.Started(run);
        }
    }

    public bool Cancel()
    {
        var run = Current;
        if (run is null || run.Finished)
            return false;
        run.Cancellation.Cancel();
        return true;
    }

    private async Task ExecuteAsync(ActiveRun run, JobbbySettings settings, string cvPath)
    {
        try
        {
            var deps = dependencies.Create(settings, run.Mode, ledgers.Get);
            var cv = await CvLoader.LoadAsync(cvPath, deps.Llm, run.Cancellation.Token);
            var runner = new JobbbyRunner(dataDir, deps, discoveryConfigured: !string.IsNullOrWhiteSpace(configuration["Jobbby:DiscoveryConfig"]));
            var summary = await runner.RunAsync(settings, cv, run.Mode, new ForwardingProgress(run), run.Cancellation.Token, run.RunId);
            run.Complete(summary);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} failed", run.RunId);
            run.Add(new RunEvent("failed", ex.Message, DateTimeOffset.UtcNow));
            run.Fail(ex.Message);
        }
    }

    /// <summary>Synchronous on purpose: events keep their order and are visible immediately.</summary>
    private sealed class ForwardingProgress(ActiveRun run) : IProgress<RunEvent>
    {
        public void Report(RunEvent value) => run.Add(value);
    }
}
