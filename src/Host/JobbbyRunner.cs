using System.Text.Json;
using ApplicationLedger;
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
    /// <summary>Real searches and evaluations, no ledger or cursor writes.</summary>
    Dry,

    /// <summary>Records outcomes and cursors.</summary>
    Normal,
}

/// <param name="Kind">started, area, queries, fetched, fetch_failed, evaluated, posting_failed, warning, completed, cancelled.</param>
public sealed record RunEvent(string Kind, string Message, DateTimeOffset At);

public sealed record RunDependencies(IJobSource JobSource, ILlmClient Llm, IReadOnlyList<SourceDefinition> Sources);

/// <param name="Area">The area actually searched (settings plus the CV location when enabled).</param>
/// <param name="RunPath">The run's file in <c>DataDir/runs/</c>.</param>
public sealed record RunSummary(string RunId, RunReport Report, int AdzunaCalls, IReadOnlyList<string> Warnings, bool Cancelled, AreaSettings Area, string RunPath);

/// <summary>
/// One complete Jobbby run, driven only by <see cref="JobbbySettings"/> and the CV: plan the
/// queries, fetch every source (local and, when configured, remote sweep), skip postings already
/// decided, evaluate the others through <see cref="HostGraph"/> and give each an outcome. Every
/// run, dry or normal, is recorded in <c>DataDir/runs/</c>; normal runs also write the ledger and
/// the cursors. Cancellation stops LLM calls in flight, marks the run Interrupted and saves no
/// cursors. Discovery is not part of a run.
/// </summary>
public sealed class JobbbyRunner
{
    private static readonly JsonSerializerOptions CursorJsonOptions = new() { WriteIndented = true };

    private readonly DataDir _dataDir;
    private readonly RunDependencies _deps;
    private readonly bool _discoveryConfigured;
    private readonly RunStore _runs;

    public JobbbyRunner(DataDir dataDir, RunDependencies deps, bool discoveryConfigured = false)
    {
        _dataDir = dataDir;
        _deps = deps;
        _discoveryConfigured = discoveryConfigured;
        _runs = new RunStore(dataDir);
    }

    public async Task<RunSummary> RunAsync(JobbbySettings settings, CvData cv, RunMode mode, IProgress<RunEvent> progress, CancellationToken cancellationToken)
    {
        var errors = SettingsValidator.Validate(settings, forRun: true);
        if (errors.Count > 0)
            throw new SettingsValidationException(errors);

        var runAt = DateTimeOffset.UtcNow;
        var runId = $"{runAt:yyyyMMdd-HHmmss}-{(mode == RunMode.Dry ? "dry" : "run")}-{Guid.NewGuid():N}"[..28];
        var run = new RunRecord
        {
            RunId = runId,
            Mode = mode == RunMode.Dry ? "dry" : "normal",
            Status = RunStatus.Running,
            StartedAt = runAt,
            Settings = settings,
        };
        _runs.Save(run);

        var progressWithRun = new RunProgress(progress);
        try
        {
            return await ExecuteAsync(settings, cv, mode, run, progressWithRun, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _runs.Save((progressWithRun.Snapshot ?? run) with { Status = RunStatus.Failed, EndedAt = DateTimeOffset.UtcNow, Error = ex.Message });
            throw;
        }
    }

    private async Task<RunSummary> ExecuteAsync(JobbbySettings settings, CvData cv, RunMode mode, RunRecord run, RunProgress progress, CancellationToken cancellationToken)
    {
        var country = settings.Area.Country!;
        var warnings = new List<string>();
        void Report(string kind, string message) => progress.Report(new RunEvent(kind, message, DateTimeOffset.UtcNow));
        void Warn(string message)
        {
            warnings.Add(message);
            Report("warning", message);
        }

        if (_discoveryConfigured)
            Warn("Discovery configurata ma non eseguita: non fa parte delle run.");

        var skillAliases = SkillAliases.Load(_dataDir.SkillAliasesPath);
        var resolved = AreaResolver.Resolve(settings.Area, cv);
        var area = resolved.Area;
        foreach (var note in resolved.Notes)
            Report("area", note);
        foreach (var warning in resolved.Warnings)
            Warn(warning);

        Report("started", mode == RunMode.Dry ? "Dry run avviata" : "Run avviata");
        var stats = new RunStatsCollector();
        var postings = new List<RunPosting>();
        var queryOutcomes = new List<QueryRecord>();
        var adzunaCalls = 0;
        var queries = new List<string>();

        RunRecord Snapshot(RunStatus status, RunReport? report = null) => run with
        {
            Status = status,
            EndedAt = status == RunStatus.Running ? null : DateTimeOffset.UtcNow,
            Area = area,
            Queries = queries.ToList(),
            AdzunaCalls = adzunaCalls,
            QueryOutcomes = queryOutcomes.ToList(),
            Postings = SnapshotPostings(postings),
            Report = report ?? stats.BuildReport(run.StartedAt),
            Warnings = warnings.ToList(),
        };
        progress.SnapshotFactory = () => Snapshot(RunStatus.Running);

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
            return Finish(Snapshot(RunStatus.Interrupted), cancelled: true, "Run interrotta durante la preparazione delle ricerche.");
        }

        if (plan.DerivationError is not null)
            Warn($"Ricerche dal CV non ricavate, uso solo quelle configurate: {plan.DerivationError}");
        queries.AddRange(plan.Queries);
        Report("queries", string.Join(", ", plan.Queries));

        var cursors = mode == RunMode.Dry
            ? new Dictionary<string, SourceCursor>()
            : RunReportStore.LoadCursors(_dataDir.CursorsPath);
        var suffixes = settings.Dedupe.ExtraCompanySuffixes;
        // Dry runs read the real history (dedupe) but never write it: RecordOutcomeNode skips the write.
        var ledger = new ApplicationLedger.ApplicationLedger(_dataDir.ApplicationsPath, suffixes);
        var history = ledger;
        // No approval channel: postings below the threshold are recorded as Pending and wait for the user.
        var definition = HostGraph.Build(
            null,
            new PendingApprovalRegistry(),
            ledger,
            _deps.Llm,
            cv,
            stats,
            confidenceThreshold: settings.Evaluation.AutoApproveThreshold,
            dryRun: mode == RunMode.Dry,
            stageOneCriteria: StageOneCriteria.FromSettings(area, settings.Salary, skillAliases),
            extraCompanySuffixes: suffixes);
        var remoteFilter = new RemoteKeywordFilter(settings.RemoteSweep.AllKeywords());
        if (area.AcceptsRemote && remoteFilter.IsEmpty && !string.IsNullOrWhiteSpace(area.Where))
            Warn("Remoto accettato ma nessuna parola chiave configurata: la ricerca remota fuori zona è disattivata.");

        var newCursors = new List<SourceCursor>();
        var cancelled = false;
        try
        {
            foreach (var source in _deps.Sources)
            {
                var fetch = await MultiQueryFetcher.FetchAsync(
                    _deps.JobSource, source, plan.Queries, country, area, remoteFilter, cursors,
                    mode == RunMode.Dry ? settings.DryRun.MaxPostingsPerQuery : null, run.StartedAt, cancellationToken,
                    suffixes).ConfigureAwait(false);
                adzunaCalls += fetch.AdzunaCalls;
                var localQueries = fetch.Queries.Where(query => query.Sweep == SearchSweep.Local).ToList();
                if (resolved.FromCv && localQueries.Count > 0 && localQueries.All(query => query.Error is null && query.Returned == 0))
                    Warn($"La zona presa dal CV (\"{area.Where}\") non ha dato risultati su {source.Name}: controlla la località del CV o imposta area.where.");

                foreach (var query in fetch.Queries)
                {
                    queryOutcomes.Add(new QueryRecord(source.Name, query.Query, query.Sweep.ToString(), query.Returned, query.DroppedByPrefilter, query.Error));
                    if (query.Error is not null)
                    {
                        stats.IncrementError(source.Name);
                        Report("fetch_failed", $"[{source.Name}] '{query.Query}' ({query.Sweep}): {query.Error}");
                    }
                    else
                    {
                        Report("fetched", $"[{source.Name}] '{query.Query}' ({query.Sweep}): {query.Returned} annunci, {query.DroppedByPrefilter} scartati dal prefiltro");
                    }
                }

                stats.IncrementTotalFetched(fetch.Postings.Count);
                var evaluations = new List<Task>();
                foreach (var posting in fetch.Postings)
                {
                    var key = PostingIdentity.Key(posting.Company, posting.RawTitle, suffixes, posting.ApplyUrl);
                    var slot = new RunPosting(PostingIdentity.Id(key), posting.RawTitle, posting.Company, posting.ApplyUrl, source.Name,
                        posting.Sweep.ToString(), PostingStatus.Interrupted, null, null, null);

                    // Already decided in an earlier run: skip before normalization, which costs an LLM call.
                    if (!string.IsNullOrWhiteSpace(posting.Company) && history.HasBeenProcessed(key))
                    {
                        stats.IncrementSkippedDuplicate();
                        Add(postings, slot with { Status = PostingStatus.Skipped, Summary = "già visto in una run precedente" });
                        Report("evaluated", $"[{source.Name}] {posting.RawTitle}: già visto in una run precedente");
                        continue;
                    }

                    var index = Add(postings, slot);
                    evaluations.Add(EvaluateAsync(definition, source, run.RunId, posting, stats, updated => Replace(postings, index, updated), slot, Report, cancellationToken));
                }

                await Task.WhenAll(evaluations).ConfigureAwait(false);
                newCursors.AddRange(fetch.Queries.Where(query => query.Cursor is not null).Select(query => query.Cursor!));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }

        if (cancelled)
            return Finish(Snapshot(RunStatus.Interrupted), cancelled: true, "Run interrotta: cursori non salvati.");

        if (mode == RunMode.Normal)
        {
            var merged = new Dictionary<string, SourceCursor>(cursors);
            foreach (var cursor in newCursors)
                merged[cursor.SourceName] = cursor;
            AtomicFile.WriteAllText(_dataDir.CursorsPath, JsonSerializer.Serialize(merged.Values.ToList(), CursorJsonOptions));
        }

        var completed = Snapshot(RunStatus.Completed);
        return Finish(completed, cancelled: false, RunSummaryText.Build(completed.Report!));

        RunSummary Finish(RunRecord record, bool cancelled, string message)
        {
            _runs.Save(record);
            Report(cancelled ? "cancelled" : "completed", message);
            return new RunSummary(record.RunId, record.Report!, record.AdzunaCalls, record.Warnings, cancelled, area, _runs.PathOf(record.RunId));
        }
    }

    private static async Task EvaluateAsync(
        GraphDefinition definition,
        SourceDefinition source,
        string runId,
        RawPosting rawPosting,
        RunStatsCollector stats,
        Action<RunPosting> update,
        RunPosting slot,
        Action<string, string> report,
        CancellationToken cancellationToken)
    {
        var state = new GraphState(new Dictionary<string, object>
        {
            [NormalizeJobPostingNode.RawPostingStateKey] = rawPosting,
            [JobApplicationStateKeys.SourceUrl] = rawPosting.ApplyUrl,
            [JobApplicationStateKeys.SourceName] = source.Name,
            [JobApplicationStateKeys.RunId] = runId,
        });

        try
        {
            await definition.CreateRun().RunAsync(HostGraph.NormalizeJobPostingNodeName, state, cancellationToken).ConfigureAwait(false);
            var summary = RunSummaryText.DescribeOutcome(state);
            var record = state.Get<ApplicationRecord>(RecordOutcomeNode.RecordStateKey);
            update(slot with
            {
                PostingId = record?.PostingId ?? slot.PostingId,
                Company = record?.Company ?? slot.Company,
                Status = PostingStatus.Evaluated,
                Summary = summary,
                Record = record,
            });
            report("evaluated", $"[{source.Name}] {rawPosting.RawTitle}: {summary}");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            stats.IncrementError(source.Name);
            update(slot with { Status = PostingStatus.Failed, Error = ex.Message });
            report("posting_failed", $"[{source.Name}] {rawPosting.RawTitle}: {ex.Message}");
        }
    }

    private static int Add(List<RunPosting> postings, RunPosting posting)
    {
        lock (postings)
        {
            postings.Add(posting);
            return postings.Count - 1;
        }
    }

    private static void Replace(List<RunPosting> postings, int index, RunPosting posting)
    {
        lock (postings)
            postings[index] = posting;
    }

    private static List<RunPosting> SnapshotPostings(List<RunPosting> postings)
    {
        lock (postings)
            return postings.ToList();
    }

    /// <summary>Forwards events and remembers how to snapshot the run, so a failure can still be recorded with what happened.</summary>
    private sealed class RunProgress(IProgress<RunEvent> inner) : IProgress<RunEvent>
    {
        public Func<RunRecord>? SnapshotFactory { get; set; }

        public RunRecord? Snapshot => SnapshotFactory?.Invoke();

        public void Report(RunEvent value) => inner.Report(value);
    }
}
