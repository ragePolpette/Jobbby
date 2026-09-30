using Config;
using CvExtraction;
using Discovery;
using GraphEngine;
using Host;
using Host.Nodes;
using JobPostings;
using Matching;
using Microsoft.Extensions.Configuration;
using Notifications;
using Reporting;

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();
SourceWhitelist.Configuration = configuration;

using var httpClient = new HttpClient();
var baseDirectory = AppContext.BaseDirectory;
var dryRunOptions = DryRunOptions.FromConfiguration(configuration, baseDirectory);
var dryRunLog = dryRunOptions.Enabled ? new DryRunLog() : null;
var sourcesPath = Path.Combine(baseDirectory, "sources.json");
var applicationsPath = Path.Combine(baseDirectory, "applications.json");
var cvPath = configuration["Jobbby:CvPath"] ?? Path.Combine(baseDirectory, "cv.json");
var searchesPath = configuration["Jobbby:SearchesConfig"] ?? Path.Combine(baseDirectory, "searches.json");
var runReportsPath = Path.Combine(baseDirectory, "run-reports.json");
var cursorsPath = Path.Combine(baseDirectory, "cursors.json");

var sources = SourceWhitelist.LoadFromFile(sourcesPath);
Console.WriteLine($"Loaded {sources.Count} source(s) from {sourcesPath}");

var cursorsBySource = dryRunOptions.Enabled
    ? new Dictionary<string, SourceCursor>()
    : RunReportStore.LoadCursors(cursorsPath);
Console.WriteLine($"Loaded {cursorsBySource.Count} source cursor(s) from {cursorsPath}");

var llmSelection = LlmClientFactory.Create(configuration, httpClient);
var llmClient = llmSelection.Client;
Console.WriteLine($"LLM provider: {llmSelection.Provider}");

var candidateCv = await CvLoader.LoadAsync(cvPath, llmClient);
Console.WriteLine($"Loaded candidate CV for {candidateCv.Name} ({candidateCv.YearsExperience}y, {candidateCv.Seniority})");

var searchPlan = await SearchQueryPlanner.PlanAsync(ConfigLoader.Load<SearchSettings>(searchesPath), candidateCv, llmClient);
if (searchPlan.DerivationError is not null)
    Console.Error.WriteLine($"Could not derive search queries from the CV, using configured ones only: {searchPlan.DerivationError}");
Console.WriteLine($"Search queries: {string.Join(", ", searchPlan.Queries)}");

ITelegramGateway? gateway = dryRunOptions.Enabled ? null : TelegramGateway.FromEnvironment(httpClient);
var registry = new PendingApprovalRegistry();
if (gateway is not null)
{
    gateway.ReplyReceived += registry.OnReply;
    gateway.Start();
}

if (dryRunOptions.Enabled)
{
    dryRunLog!.Add("dry_run_started", new
    {
        dryRunOptions.MaxPostingsPerSource,
        dryRunOptions.MaxDiscoveryCandidates,
        Sources = sources.Select(source => new { source.Name, source.BaseUrl }).ToList(),
        Searches = new { searchPlan.ConfiguredQueries, searchPlan.DerivedQueries, searchPlan.DerivationError },
        Candidate = new { candidateCv.Name, candidateCv.YearsExperience, candidateCv.Seniority, candidateCv.Skills },
        Llm = new { llmSelection.Provider, llmSelection.Endpoint, llmSelection.Model },
    });
    Console.WriteLine($"DRY RUN enabled: max {dryRunOptions.MaxPostingsPerSource} posting(s) per source and query, no Telegram or persistent state writes.");
}

var discoveryPath = configuration["Jobbby:DiscoveryConfig"] ?? Environment.GetEnvironmentVariable("JOBBBY_DISCOVERY_CONFIG");
if (!string.IsNullOrWhiteSpace(discoveryPath))
{
    var criteria = ConfigLoader.Load<DiscoveryCriteria>(discoveryPath);
    var discovery = new DiscoveryEngine(BraveSearchClient.FromEnvironment(httpClient), llmClient);

    if (dryRunOptions.Enabled)
    {
        var candidates = await discovery.DiscoverAsync(criteria, maxCandidates: dryRunOptions.MaxDiscoveryCandidates);
        foreach (var candidate in candidates)
        {
            dryRunLog!.Add("discovery_candidate_evaluated", new
            {
                candidate.Name,
                candidate.Url,
                candidate.ReliabilityScore,
                candidate.EvaluationSummary,
                Activated = false,
            });
        }
        Console.WriteLine($"[dry-run] Discovery evaluated {candidates.Count} candidate source(s); none activated.");
    }
    else
    {
        var reviewService = new DiscoveryReviewService(discovery, gateway!, registry);
        var review = await reviewService.DiscoverAndReviewAsync(criteria);
        var approvedPath = Path.Combine(baseDirectory, "approved-sources.json");
        ApprovedSourceStore.SaveApproved(approvedPath, review.ApprovedSources);
        Console.WriteLine($"Approved {review.ApprovedSources.Count} discovered source(s).");
    }
}

var temporaryLedgerPath = dryRunOptions.Enabled
    ? Path.Combine(Path.GetTempPath(), $"jobbby-dry-run-{Guid.NewGuid():N}.json")
    : applicationsPath;
var ledger = new ApplicationLedger.ApplicationLedger(temporaryLedgerPath);
var statsCollector = new RunStatsCollector();
IJobSource jobSource = AdzunaJobSource.FromEnvironment(
    httpClient,
    configuration["Jobbby:Country"] ?? throw new InvalidOperationException("Missing Jobbby:Country (Adzuna country code)."),
    resultsPerPage: dryRunOptions.Enabled ? dryRunOptions.MaxPostingsPerSource : 10);
var definition = HostGraph.Build(
    gateway ?? new MockTelegramGateway(),
    registry,
    ledger,
    llmClient,
    candidateCv,
    statsCollector,
    dryRun: dryRunOptions.Enabled);

var runAt = DateTimeOffset.UtcNow;
var updatedCursors = await Task.WhenAll(
    sources.Select(source => RunForSourceAsync(
        definition,
        jobSource,
        source,
        searchPlan.Queries,
        cursorsBySource,
        statsCollector,
        dryRunOptions.Enabled ? dryRunOptions.MaxPostingsPerSource : null,
        runAt,
        dryRunLog)));

gateway?.Stop();

var mergedCursors = new Dictionary<string, SourceCursor>(cursorsBySource);
foreach (var cursor in updatedCursors.SelectMany(cursors => cursors))
    mergedCursors[cursor.SourceName] = cursor;

var report = statsCollector.BuildReport(runAt);
var summary = BuildSummaryMessage(report);
Console.WriteLine(summary);

if (dryRunOptions.Enabled)
{
    dryRunLog!.Add("dry_run_completed", new { Report = report, PersistentWrites = false, TelegramMessages = 0 });
    dryRunLog.Save(dryRunOptions.LogPath);
    if (File.Exists(temporaryLedgerPath))
        File.Delete(temporaryLedgerPath);
    Console.WriteLine($"Detailed dry-run log written to {dryRunOptions.LogPath}");
}
else
{
    RunReportStore.SaveCursors(cursorsPath, mergedCursors.Values.ToList());
    RunReportStore.AppendRunReport(runReportsPath, report);
    await gateway!.SendAsync(summary);
}

Console.WriteLine("All source runs completed.");

static async Task<IReadOnlyList<SourceCursor>> RunForSourceAsync(
    GraphDefinition definition,
    IJobSource jobSource,
    SourceDefinition source,
    IReadOnlyList<string> queries,
    IReadOnlyDictionary<string, SourceCursor> cursorsBySource,
    RunStatsCollector statsCollector,
    int? postingLimitPerQuery,
    DateTimeOffset runAt,
    DryRunLog? dryRunLog)
{
    var fetch = await MultiQueryFetcher.FetchAsync(jobSource, source, queries, cursorsBySource, postingLimitPerQuery, runAt);

    foreach (var query in fetch.Queries)
    {
        if (query.Error is not null)
        {
            statsCollector.IncrementError(source.Name);
            dryRunLog?.Add("source_fetch_failed", new { Source = source.Name, query.Query, query.Error });
            Console.Error.WriteLine($"[{source.Name}] fetch failed for '{query.Query}': {query.Error}");
        }
        else
        {
            dryRunLog?.Add("source_fetched", new { Source = source.Name, query.Query, query.Returned });
            Console.WriteLine($"[{source.Name}] '{query.Query}': {query.Returned} posting(s)");
        }
    }

    var rawPostings = fetch.Postings;
    statsCollector.IncrementTotalFetched(rawPostings.Count);
    dryRunLog?.Add("source_postings_merged", new { Source = source.Name, Processed = rawPostings.Count });
    Console.WriteLine($"[{source.Name}] processing {rawPostings.Count} distinct posting(s)");

    var postingRuns = rawPostings.Select(rawPosting => RunForPostingAsync(definition, source, rawPosting, statsCollector, dryRunLog));
    await Task.WhenAll(postingRuns);

    return fetch.Queries.Where(query => query.Cursor is not null).Select(query => query.Cursor!).ToList();
}

static async Task RunForPostingAsync(
    GraphDefinition definition,
    SourceDefinition source,
    RawPosting rawPosting,
    RunStatsCollector statsCollector,
    DryRunLog? dryRunLog)
{
    var state = new GraphState(new Dictionary<string, object>
    {
        [NormalizeJobPostingNode.RawPostingStateKey] = rawPosting,
        [JobApplicationStateKeys.SourceUrl] = source.BaseUrl,
    });

    Console.WriteLine($"[{source.Name}] starting: {rawPosting.RawTitle}");

    try
    {
        await definition.CreateRun().RunAsync(HostGraph.NormalizeJobPostingNodeName, state);
        var outcome = DescribeOutcome(state);
        var posting = state.Get<JobPosting>(NormalizeJobPostingNode.JobPostingStateKey);
        dryRunLog?.Add("posting_evaluated", new
        {
            Source = source.Name,
            rawPosting.RawTitle,
            rawPosting.Company,
            rawPosting.ApplyUrl,
            Normalized = posting is null ? null : new { posting.Title, posting.Company, posting.SeniorityLevel, posting.RequiredStack, posting.Location, posting.RemoteAvailable, posting.SalaryMaximum },
            StageOnePassed = state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey),
            StageOneReason = state.Get<string>(ScoreMatchNode.StageOneReasonStateKey),
            MissingRequirements = state.Get<List<string>>(ScoreMatchNode.MissingRequirementsStateKey),
            PreferenceWarnings = state.Get<List<string>>(ScoreMatchNode.PreferenceWarningsStateKey),
            MatchConfidence = state.Get<double>(ScoreMatchNode.MatchConfidenceStateKey),
            MatchReasoning = state.Get<string>(ScoreMatchNode.MatchJudgmentReasoningStateKey),
            Outcome = outcome,
            ActionTaken = "none",
        });
        Console.WriteLine($"[{source.Name}] esito: {outcome}");
    }
    catch (Exception ex)
    {
        statsCollector.IncrementError(source.Name);
        dryRunLog?.Add("posting_failed", new { Source = source.Name, rawPosting.RawTitle, Error = ex.Message });
        Console.Error.WriteLine($"[{source.Name}] run failed for '{rawPosting.RawTitle}': {ex.Message}");
    }
}

static string DescribeOutcome(GraphState state)
{
    if (state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey))
        return "scartato per dedupe";
    if (!state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey))
        return $"scartato dal filtro stage 1 ({state.Get<string>(ScoreMatchNode.StageOneReasonStateKey)})";
    if (!state.ContainsKey(RecordIfApprovedNode.OutcomeStateKey))
        return $"valutato senza azioni (confidenza {state.Get<double>(ScoreMatchNode.MatchConfidenceStateKey):0.00})";
    if (state.Get<bool>(RecordIfApprovedNode.AutoApprovedStateKey))
        return "selezionato automaticamente";
    if (state.Get<string>(AskApprovalNode.OutcomeStateKey) == HumanInputNode.SkippedNoResponseOutcome)
        return "in attesa di approvazione";
    return state.Get<bool>(RecordIfApprovedNode.RecordedStateKey) ? "approvato" : "rifiutato";
}

static string BuildSummaryMessage(RunReport report)
{
    var stageTwo = report.StageTwoBreakdown.Count == 0
        ? "nessuna"
        : string.Join(", ", report.StageTwoBreakdown.Select(kv => $"{TranslateCategory(kv.Key)}: {kv.Value}"));
    var errors = report.ErrorsPerSource.Count == 0
        ? "nessuno"
        : string.Join(", ", report.ErrorsPerSource.Select(kv => $"{kv.Key}: {kv.Value}"));

    return $"""
        Riepilogo run {report.RunAt:yyyy-MM-dd HH:mm} UTC
        Annunci trovati: {report.TotalFetched}
        Scartati per dedupe: {report.SkippedDuplicate}
        Scartati al filtro stage 1: {report.RejectedStageOne}
        Valutazioni stage 2: {stageTwo}
        Selezionati automaticamente: {report.AutoApproved}
        Approvati da un umano: {report.HumanApproved}
        Rifiutati da un umano: {report.HumanRejected}
        Scaduti senza risposta: {report.TimedOut}
        Errori per fonte: {errors}
        """;
}

static string TranslateCategory(string category) => category switch
{
    MatchCategories.Strong => "forte",
    MatchCategories.Borderline => "borderline",
    MatchCategories.Weak => "debole",
    _ => category,
};
