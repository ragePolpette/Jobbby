using Config;
using CvExtraction;
using GraphEngine;
using Host;
using Host.Nodes;
using JobPostings;
using Notifications;

using var httpClient = new HttpClient();

// Resolved against the assembly's own directory (not the process's working directory),
// so this works the same whether launched via `dotnet run`, `dotnet Host.dll`, or a
// published binary.
var sourcesPath = Path.Combine(AppContext.BaseDirectory, "sources.json");
var applicationsPath = Path.Combine(AppContext.BaseDirectory, "applications.json");
var cvPath = Path.Combine(AppContext.BaseDirectory, "cv.json");

var sources = SourceWhitelist.LoadFromFile(sourcesPath);
Console.WriteLine($"Loaded {sources.Count} source(s) from {sourcesPath}");

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

IJobSource jobSource = AdzunaJobSource.FromEnvironment(httpClient);

// One GraphDefinition shared by every posting's run - it's immutable config, safe to
// reuse concurrently (see GraphDefinition/GraphRun).
var definition = HostGraph.Build(gateway, registry, ledger, llmClient, candidateCv);

var runs = sources.Select(source => RunForSourceAsync(definition, jobSource, source));
await Task.WhenAll(runs);

gateway.Stop();

Console.WriteLine("All source runs completed.");

static async Task RunForSourceAsync(GraphDefinition definition, IJobSource jobSource, SourceDefinition source)
{
    var rawPostings = await jobSource.FetchAsync(source);
    Console.WriteLine($"[{source.Name}] fetched {rawPostings.Count} posting(s)");

    var postingRuns = rawPostings.Select(rawPosting => RunForPostingAsync(definition, source, rawPosting));
    await Task.WhenAll(postingRuns);
}

static async Task RunForPostingAsync(GraphDefinition definition, SourceDefinition source, RawPosting rawPosting)
{
    var state = new GraphState(new Dictionary<string, object>
    {
        [NormalizeJobPostingNode.RawPostingStateKey] = rawPosting,
        [JobApplicationStateKeys.SourceUrl] = source.BaseUrl,
    });

    Console.WriteLine($"[{source.Name}] starting: {rawPosting.RawTitle}");

    await definition.CreateRun().RunAsync(HostGraph.NormalizeJobPostingNodeName, state);

    Console.WriteLine($"[{source.Name}] esito: {DescribeOutcome(state)}");
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
