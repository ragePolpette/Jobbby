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

var runs = sources.Select(source => RunForSourceAsync(definition, source));
await Task.WhenAll(runs);

gateway.Stop();

Console.WriteLine("All source runs completed.");

static async Task RunForSourceAsync(GraphDefinition definition, SourceDefinition source)
{
    // No real source agent yet - placeholder Company/Title so the graph has something
    // to dedupe-check and ask approval for.
    var company = $"{source.Name} Corp";
    const string title = "Fake Role";

    var state = new GraphState(new Dictionary<string, object>
    {
        [JobApplicationStateKeys.Company] = company,
        [JobApplicationStateKeys.Title] = title,
        [JobApplicationStateKeys.SourceUrl] = source.BaseUrl,
    });

    Console.WriteLine($"[{source.Name}] starting: {company} / {title}");

    await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

    Console.WriteLine($"[{source.Name}] esito: {DescribeOutcome(state)}");
}

static string DescribeOutcome(GraphState state)
{
    if (state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey))
        return "scartato per dedupe";

    if (state.Get<string>(AskApprovalNode.OutcomeStateKey) == HumanInputNode.SkippedNoResponseOutcome)
        return "in attesa di approvazione (nessuna risposta entro il timeout)";

    return state.Get<bool>(RecordIfApprovedNode.RecordedStateKey) ? "registrato" : "rifiutato";
}
