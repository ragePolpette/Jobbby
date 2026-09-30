using System.Text.Json;
using Config;
using CvExtraction;
using GraphEngine;
using Host.Nodes;
using JobPostings;
using Matching;
using Notifications;
using Reporting;

namespace Host;

public enum RunMode
{
    /// <summary>Real searches and evaluations, no ledger, cursor or notification writes.</summary>
    Dry,

    /// <summary>Records outcomes and cursors.</summary>
    Normal,
}

/// <param name="Kind">started, queries, fetched, fetch_failed, evaluated, posting_failed, warning, completed, cancelled.</param>
public sealed record RunEvent(string Kind, string Message, DateTimeOffset At);

/// <param name="Gateway">Approval channel for normal runs; required until approvals move to the UI.</param>
public sealed record RunDependencies(IJobSource JobSource, ILlmClient Llm, ITelegramGateway? Gateway, IReadOnlyList<SourceDefinition> Sources);

/// <param name="Area">The area actually searched (settings plus the CV location when enabled).</param>
public sealed record RunSummary(RunReport Report, int AdzunaCalls, IReadOnlyList<string> Warnings, bool Cancelled, DryRunLog? DryRunLog, AreaSettings Area);

/// <summary>
/// One complete Jobbby run, driven only by <see cref="JobbbySettings"/> and the CV: plan the
/// queries, fetch every source (local and, when configured, remote sweep), evaluate every
/// distinct posting through <see cref="HostGraph"/>, then persist cursors and the report in
/// the DataDir (normal mode only). Cancellation stops LLM calls in flight and persists nothing.
/// Discovery is not part of a run.
/// </summary>
public sealed class JobbbyRunner
{
    private static readonly JsonSerializerOptions CursorJsonOptions = new() { WriteIndented = true };

    private readonly DataDir _dataDir;
    private readonly RunDependencies _deps;
    private readonly bool _discoveryConfigured;

    public JobbbyRunner(DataDir dataDir, RunDependencies deps, bool discoveryConfigured = false)
    {
        _dataDir = dataDir;
        _deps = deps;
        _discoveryConfigured = discoveryConfigured;
    }

    public async Task<RunSummary> RunAsync(JobbbySettings settings, CvData cv, RunMode mode, IProgress<RunEvent> progress, CancellationToken cancellationToken)
    {
        var errors = SettingsValidator.Validate(settings, forRun: true);
        if (errors.Count > 0)
            throw new SettingsValidationException(errors);
        if (mode == RunMode.Normal && _deps.Gateway is null)
            throw new InvalidOperationException("Una run normale richiede un canale di approvazione (Telegram) finché le approvazioni non passano alla UI.");

        var country = settings.Area.Country!;
        var warnings = new List<string>();
        var dryRunLog = mode == RunMode.Dry ? new DryRunLog() : null;
        void Report(string kind, string message) => progress.Report(new RunEvent(kind, message, DateTimeOffset.UtcNow));
        void Warn(string message)
        {
            warnings.Add(message);
            Report("warning", message);
        }

        if (_discoveryConfigured)
            Warn("Discovery configurata ma non eseguita: non fa parte delle run.");

        var resolved = AreaResolver.Resolve(settings.Area, cv);
        var area = resolved.Area;
        foreach (var note in resolved.Notes)
            Report("area", note);
        foreach (var warning in resolved.Warnings)
            Warn(warning);

        var runAt = DateTimeOffset.UtcNow;
        Report("started", mode == RunMode.Dry ? "Dry run avviata" : "Run avviata");

        SearchPlan plan;
        try
        {
            plan = await SearchQueryPlanner.PlanAsync(
            new SearchSettings
            {
                Queries = settings.Searches.Queries,
                DeriveFromCv = settings.Searches.DeriveFromCv,
                MaxDerivedQueries = settings.Searches.MaxDerivedQueries,
            },
            cv,
            _deps.Llm,
            cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Report("cancelled", "Run interrotta durante la preparazione delle ricerche.");
            return new RunSummary(new RunStatsCollector().BuildReport(runAt), 0, warnings, Cancelled: true, dryRunLog, area);
        }

        if (plan.DerivationError is not null)
            Warn($"Ricerche dal CV non ricavate, uso solo quelle configurate: {plan.DerivationError}");
        Report("queries", string.Join(", ", plan.Queries));

        var cursors = mode == RunMode.Dry
            ? new Dictionary<string, SourceCursor>()
            : RunReportStore.LoadCursors(_dataDir.CursorsPath);
        var ledgerPath = mode == RunMode.Dry
            ? Path.Combine(Path.GetTempPath(), $"jobbby-dry-run-{Guid.NewGuid():N}.json")
            : _dataDir.ApplicationsPath;
        var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
        var stats = new RunStatsCollector();
        var registry = new PendingApprovalRegistry();
        var gateway = _deps.Gateway ?? new MockTelegramGateway();
        var definition = HostGraph.Build(
            gateway,
            registry,
            ledger,
            _deps.Llm,
            cv,
            stats,
            confidenceThreshold: settings.Evaluation.AutoApproveThreshold,
            dryRun: mode == RunMode.Dry,
            stageOneCriteria: StageOneCriteria.FromSettings(area, settings.Salary, SkillAliases.Load(_dataDir.SkillAliasesPath)));
        var remoteFilter = new RemoteKeywordFilter(settings.RemoteSweep.AllKeywords());
        if (area.AcceptsRemote && remoteFilter.IsEmpty && !string.IsNullOrWhiteSpace(area.Where))
            Warn("Remoto accettato ma nessuna parola chiave configurata: la ricerca remota fuori zona è disattivata.");

        if (mode == RunMode.Normal)
        {
            gateway.ReplyReceived += registry.OnReply;
            gateway.Start();
        }

        var adzunaCalls = 0;
        var newCursors = new List<SourceCursor>();
        var cancelled = false;
        try
        {
            foreach (var source in _deps.Sources)
            {
                var fetch = await MultiQueryFetcher.FetchAsync(
                    _deps.JobSource, source, plan.Queries, country, area, remoteFilter, cursors,
                    mode == RunMode.Dry ? settings.DryRun.MaxPostingsPerQuery : null, runAt, cancellationToken).ConfigureAwait(false);
                adzunaCalls += fetch.AdzunaCalls;

                foreach (var query in fetch.Queries)
                {
                    if (query.Error is not null)
                    {
                        stats.IncrementError(source.Name);
                        dryRunLog?.Add("source_fetch_failed", new { Source = source.Name, query.Query, Sweep = query.Sweep.ToString(), query.Error });
                        Report("fetch_failed", $"[{source.Name}] '{query.Query}' ({query.Sweep}): {query.Error}");
                    }
                    else
                    {
                        dryRunLog?.Add("source_fetched", new { Source = source.Name, query.Query, Sweep = query.Sweep.ToString(), query.Returned, query.DroppedByPrefilter });
                        Report("fetched", $"[{source.Name}] '{query.Query}' ({query.Sweep}): {query.Returned} annunci, {query.DroppedByPrefilter} scartati dal prefiltro");
                    }
                }

                stats.IncrementTotalFetched(fetch.Postings.Count);
                await Task.WhenAll(fetch.Postings.Select(posting =>
                    EvaluateAsync(definition, source, posting, stats, dryRunLog, Report, cancellationToken))).ConfigureAwait(false);
                newCursors.AddRange(fetch.Queries.Where(query => query.Cursor is not null).Select(query => query.Cursor!));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }
        finally
        {
            if (mode == RunMode.Normal)
                gateway.Stop();
            if (mode == RunMode.Dry && File.Exists(ledgerPath))
                File.Delete(ledgerPath);
        }

        var report = stats.BuildReport(runAt);
        if (cancelled)
        {
            Report("cancelled", "Run interrotta: cursori e riepilogo non salvati.");
            return new RunSummary(report, adzunaCalls, warnings, Cancelled: true, dryRunLog, area);
        }

        if (mode == RunMode.Normal)
        {
            var merged = new Dictionary<string, SourceCursor>(cursors);
            foreach (var cursor in newCursors)
                merged[cursor.SourceName] = cursor;
            AtomicFile.WriteAllText(_dataDir.CursorsPath, JsonSerializer.Serialize(merged.Values.ToList(), CursorJsonOptions));
            RunReportStore.AppendRunReport(_dataDir.RunReportsPath, report);
            await gateway.SendAsync(RunSummaryText.Build(report), CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            dryRunLog!.Add("dry_run_completed", new { Report = report, AdzunaCalls = adzunaCalls, Warnings = warnings, PersistentWrites = false });
        }

        Report("completed", RunSummaryText.Build(report));
        return new RunSummary(report, adzunaCalls, warnings, Cancelled: false, dryRunLog, area);
    }

    private static async Task EvaluateAsync(
        GraphDefinition definition,
        SourceDefinition source,
        RawPosting rawPosting,
        RunStatsCollector stats,
        DryRunLog? dryRunLog,
        Action<string, string> report,
        CancellationToken cancellationToken)
    {
        var state = new GraphState(new Dictionary<string, object>
        {
            [NormalizeJobPostingNode.RawPostingStateKey] = rawPosting,
            [JobApplicationStateKeys.SourceUrl] = source.BaseUrl,
        });

        try
        {
            await definition.CreateRun().RunAsync(HostGraph.NormalizeJobPostingNodeName, state, cancellationToken).ConfigureAwait(false);
            var outcome = RunSummaryText.DescribeOutcome(state);
            var posting = state.Get<JobPosting>(NormalizeJobPostingNode.JobPostingStateKey);
            dryRunLog?.Add("posting_evaluated", new
            {
                Source = source.Name,
                rawPosting.RawTitle,
                rawPosting.Company,
                rawPosting.ApplyUrl,
                Sweep = rawPosting.Sweep.ToString(),
                Normalized = posting is null ? null : new { posting.Title, posting.Company, posting.SeniorityLevel, posting.RequiredSkills, posting.Location, WorkMode = posting.WorkMode.ToString(), posting.MinYearsExperience, posting.SalaryMaximum },
                StageOnePassed = state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey),
                StageOneReason = state.Get<string>(ScoreMatchNode.StageOneReasonStateKey),
                MissingRequirements = state.Get<List<string>>(ScoreMatchNode.MissingRequirementsStateKey),
                PreferenceWarnings = state.Get<List<string>>(ScoreMatchNode.PreferenceWarningsStateKey),
                MatchConfidence = state.Get<double>(ScoreMatchNode.MatchConfidenceStateKey),
                MatchReasoning = state.Get<string>(ScoreMatchNode.MatchJudgmentReasoningStateKey),
                Outcome = outcome,
            });
            report("evaluated", $"[{source.Name}] {rawPosting.RawTitle}: {outcome}");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            stats.IncrementError(source.Name);
            dryRunLog?.Add("posting_failed", new { Source = source.Name, rawPosting.RawTitle, Error = ex.Message });
            report("posting_failed", $"[{source.Name}] {rawPosting.RawTitle}: {ex.Message}");
        }
    }
}
