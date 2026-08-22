using Config;
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

var sources = SourceWhitelist.LoadFromFile(sourcesPath);
Console.WriteLine($"Loaded {sources.Count} source(s) from {sourcesPath}");

var gateway = TelegramGateway.FromEnvironment(httpClient);
gateway.Start();

var registry = new PendingApprovalRegistry();
gateway.ReplyReceived += registry.OnReply;

var ledger = new ApplicationLedger.ApplicationLedger(applicationsPath);

IJobSource jobSource = AdzunaJobSource.FromEnvironment(httpClient);

// Real extraction still awaits a wired provider; MockLlmClient keeps NormalizeJobPosting
// runnable end-to-end until then.
ILlmClient llmClient = new MockLlmClient("""
    {"company":"","seniorityLevel":"","requiredStack":[]}
    """);

// One GraphDefinition shared by every posting's run - it's immutable config, safe to
// reuse concurrently (see GraphDefinition/GraphRun).
var definition = HostGraph.Build(gateway, registry, ledger, llmClient);

var runs = sources.Select(source => RunForSourceAsync(definition, jobSource, source));
await Task.WhenAll(runs);

gateway.Stop();

Console.WriteLine("All source runs completed.");

static async Task RunForSourceAsync(GraphDefinition definition, IJobSource jobSource, SourceDefinition source)
{
    var rawPostings = await jobSource.FetchAsync(source);
    Console.WriteLine($"[{source.Name}] fetched {rawPostings.Count} posting(s)");

    var postingRuns = rawPostings.Select((rawPosting, index) => RunForPostingAsync(definition, source, rawPosting, index));
    await Task.WhenAll(postingRuns);
}

static async Task RunForPostingAsync(GraphDefinition definition, SourceDefinition source, RawPosting rawPosting, int index)
{
    // No real match scoring yet - alternate a high/low confidence per posting so a
    // single run exercises both the auto-approved path and the Telegram-approval path.
    var matchConfidence = index % 2 == 0 ? 0.9 : 0.3;

    var state = new GraphState(new Dictionary<string, object>
    {
        [NormalizeJobPostingNode.RawPostingStateKey] = rawPosting,
        [JobApplicationStateKeys.SourceUrl] = source.BaseUrl,
        [ScoreMatchNode.MatchConfidenceStateKey] = matchConfidence,
    });

    Console.WriteLine($"[{source.Name}] starting: {rawPosting.RawTitle} (MatchConfidence={matchConfidence:0.00})");

    await definition.CreateRun().RunAsync(HostGraph.NormalizeJobPostingNodeName, state);

    Console.WriteLine($"[{source.Name}] esito: {DescribeOutcome(state)}");
}

static string DescribeOutcome(GraphState state)
{
    if (state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey))
        return "scartato per dedupe";

    if (state.Get<bool>(RecordIfApprovedNode.AutoApprovedStateKey))
        return "auto-approvato (confidenza alta, nessuna richiesta Telegram)";

    if (state.Get<string>(AskApprovalNode.OutcomeStateKey) == HumanInputNode.SkippedNoResponseOutcome)
        return "in attesa di approvazione (nessuna risposta entro il timeout)";

    return state.Get<bool>(RecordIfApprovedNode.RecordedStateKey) ? "registrato" : "rifiutato";
}
