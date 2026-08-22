using Config;
using GraphEngine;
using Host;
using Host.Nodes;
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

// One GraphDefinition shared by every source's run - it's immutable config, safe to
// reuse concurrently (see GraphDefinition/GraphRun).
var definition = HostGraph.Build(gateway, registry, ledger);

var runs = sources.Select((source, index) => RunForSourceAsync(definition, source, index));
await Task.WhenAll(runs);

gateway.Stop();

Console.WriteLine("All source runs completed.");

static async Task RunForSourceAsync(GraphDefinition definition, SourceDefinition source, int index)
{
    // No real source agent yet - placeholder Company/Title so the graph has something
    // to dedupe-check and ask approval for.
    var company = $"{source.Name} Corp";
    const string title = "Fake Role";

    // No real match scoring yet either - alternate a high/low confidence per source so a
    // single run exercises both the auto-approved path and the Telegram-approval path.
    var matchConfidence = index % 2 == 0 ? 0.9 : 0.3;

    var state = new GraphState(new Dictionary<string, object>
    {
        [JobApplicationStateKeys.Company] = company,
        [JobApplicationStateKeys.Title] = title,
        [JobApplicationStateKeys.SourceUrl] = source.BaseUrl,
        [ScoreMatchNode.MatchConfidenceStateKey] = matchConfidence,
    });

    Console.WriteLine($"[{source.Name}] starting: {company} / {title} (MatchConfidence={matchConfidence:0.00})");

    await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

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
