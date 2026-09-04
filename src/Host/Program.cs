using Config;
using CvExtraction;
using GraphEngine;
using Host;
using Host.Nodes;
using JobPostings;
using Matching;
using Microsoft.Extensions.Configuration;
using Notifications;
using Reporting;

// Secrets (Telegram bot token, Brave/Adzuna API keys, ...) resolve through this:
// AddUserSecrets<Program>() is where `dotnet user-secrets set <key> <value>` values
// actually surface in development; AddEnvironmentVariables() is what production sets
// instead. SourceWhitelist.ResolveSecret reads from whichever of the two has the key.
SourceWhitelist.Configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

using var httpClient = new HttpClient();

// Resolved against the assembly's own directory (not the process's working directory),
// so this works the same whether launched via `dotnet run`, `dotnet Host.dll`, or a
// published binary.
var sourcesPath = Path.Combine(AppContext.BaseDirectory, "sources.json");
var applicationsPath = Path.Combine(AppContext.BaseDirectory, "applications.json");
var cvPath = Path.Combine(AppContext.BaseDirectory, "cv.json");
var runReportsPath = Path.Combine(AppContext.BaseDirectory, "run-reports.json");
var cursorsPath = Path.Combine(AppContext.BaseDirectory, "cursors.json");

var sources = SourceWhitelist.LoadFromFile(sourcesPath);
Console.WriteLine($"Loaded {sources.Count} source(s) from {sourcesPath}");

var cursorsBySource = RunReportStore.LoadCursors(cursorsPath);
Console.WriteLine($"Loaded {cursorsBySource.Count} source cursor(s) from {cursorsPath}");

// Real extraction/judging still await a wired provider; MockLlmClient keeps
// NormalizeJobPosting and ScoreMatch's stage-two judge runnable end-to-end until then.
ILlmClient llmClient = new MockLlmClient("""
    {"company":"","seniorityLevel":"","requiredStack":[]}
    """);

var candidateCv = await CvLoader.LoadAsync(cvPath, llmClient);
Console.WriteLine($"Loaded candidate CV for {candidateCv.Name} ({candidateCv.YearsExperience}y, {candidateCv.Seniority})");

var gateway = TelegramGateway.FromEnvironment(httpClient);
gateway.Start();

var registry = new PendingApprovalRegistry();
gateway.ReplyReceived += registry.OnReply;

var ledger = new ApplicationLedger.ApplicationLedger(applicationsPath);
var statsCollector = new RunStatsCollector();

IJobSource jobSource = AdzunaJobSource.FromEnvironment(httpClient);

// One GraphDefinition shared by every posting's run - it's immutable config, safe to
// reuse concurrently (see GraphDefinition/GraphRun).
var definition = HostGraph.Build(gateway, registry, ledger, llmClient, candidateCv, statsCollector);

var runAt = DateTimeOffset.UtcNow;
var updatedCursors = await Task.WhenAll(
    sources.Select(source => RunForSourceAsync(definition, jobSource, source, cursorsBySource, statsCollector)));

gateway.Stop();

var mergedCursors = new Dictionary<string, SourceCursor>(cursorsBySource);
foreach (var cursor in updatedCursors)
{
    if (cursor is not null)
        mergedCursors[cursor.SourceName] = cursor;
}

RunReportStore.SaveCursors(cursorsPath, mergedCursors.Values.ToList());

var report = statsCollector.BuildReport(runAt);
RunReportStore.AppendRunReport(runReportsPath, report);

var summary = BuildSummaryMessage(report);
Console.WriteLine(summary);
await gateway.SendAsync(summary);

Console.WriteLine("All source runs completed.");

static async Task<SourceCursor?> RunForSourceAsync(
    GraphDefinition definition,
    IJobSource jobSource,
    SourceDefinition source,
    IReadOnlyDictionary<string, SourceCursor> cursorsBySource,
    RunStatsCollector statsCollector)
{
    cursorsBySource.TryGetValue(source.Name, out var cursor);

    IReadOnlyList<RawPosting> rawPostings;
    try
    {
        rawPostings = await jobSource.FetchAsync(source, cursor);
    }
    catch (Exception ex)
    {
        statsCollector.IncrementError(source.Name);
        Console.Error.WriteLine($"[{source.Name}] fetch failed: {ex.Message}");
        return cursor;
    }

    statsCollector.IncrementTotalFetched(rawPostings.Count);
    Console.WriteLine($"[{source.Name}] fetched {rawPostings.Count} posting(s)");

    if (rawPostings.Count == 0)
        return cursor;

    var postingRuns = rawPostings.Select(rawPosting => RunForPostingAsync(definition, source, rawPosting, statsCollector));
    await Task.WhenAll(postingRuns);

    // Best-effort "most recent" bookmark: Adzuna's default ordering isn't guaranteed
    // chronological, so this - combined with max_days_old on the next fetch - is a
    // reasonable approximation, not a precise cursor.
    return new SourceCursor(source.Name, rawPostings[0].ApplyUrl, DateTimeOffset.UtcNow);
}

static async Task RunForPostingAsync(GraphDefinition definition, SourceDefinition source, RawPosting rawPosting, RunStatsCollector statsCollector)
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
        Console.WriteLine($"[{source.Name}] esito: {DescribeOutcome(state)}");
    }
    catch (Exception ex)
    {
        statsCollector.IncrementError(source.Name);
        Console.Error.WriteLine($"[{source.Name}] run failed for '{rawPosting.RawTitle}': {ex.Message}");
    }
}

static string DescribeOutcome(GraphState state)
{
    if (state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey))
        return "scartato per dedupe";

    if (!state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey))
        return $"scartato dal filtro stage 1 ({state.Get<string>(ScoreMatchNode.StageOneReasonStateKey)})";

    if (state.Get<bool>(RecordIfApprovedNode.AutoApprovedStateKey))
        return "auto-approvato (confidenza alta, nessuna richiesta Telegram)";

    if (state.Get<string>(AskApprovalNode.OutcomeStateKey) == HumanInputNode.SkippedNoResponseOutcome)
        return "in attesa di approvazione (nessuna risposta entro il timeout)";

    return state.Get<bool>(RecordIfApprovedNode.RecordedStateKey) ? "registrato" : "rifiutato";
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
        Auto-approvati: {report.AutoApproved}
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
