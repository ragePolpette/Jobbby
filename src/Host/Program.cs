using Config;
using CvExtraction;
using Host;
using JobPostings;
using Microsoft.Extensions.Configuration;
using Notifications;

// Thin CLI over JobbbyRunner: everything a run needs comes from the DataDir (settings.json,
// CV) plus secrets from user-secrets / environment variables.
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();
SourceWhitelist.Configuration = configuration;

DataDir dataDir;
try
{
    dataDir = DataDir.FromConfiguration(configuration);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

DataDirLock dataDirLock;
try
{
    dataDirLock = DataDirLock.Acquire(dataDir);
}
catch (DataDirLockedException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

using (dataDirLock)
{
    try
    {
        return await RunCliAsync(configuration, dataDir, args);
    }
    catch (Exception ex) when (ex is SettingsFileException or SettingsValidationException or InvalidOperationException
                                   or NotSupportedException or FileNotFoundException or System.Text.Json.JsonException)
    {
        // Configuration and environment problems: the message says what to fix, a stack trace would bury it.
        Console.Error.WriteLine($"Errore: {ex.Message}");
        return 2;
    }
}

static async Task<int> RunCliAsync(IConfiguration configuration, DataDir dataDir, string[] args)
{
    var baseDirectory = AppContext.BaseDirectory;
    var legacyDirectories = new[] { configuration["Jobbby:LegacyDir"], baseDirectory }.OfType<string>();
    foreach (var note in LegacyMigration.Run(dataDir, legacyDirectories))
        Console.WriteLine(note);

    var runs = new RunStore(dataDir);
    foreach (var runId in runs.RecoverInterrupted())
        Console.WriteLine($"Run {runId} era rimasta in corso: segnata come interrotta.");
    var imported = runs.ImportLegacyReports();
    if (imported.Imported > 0)
        Console.WriteLine($"Importati {imported.Imported} riepiloghi da run-reports.json in {dataDir.RunsDirectory}.");
    if (imported.Warning is not null)
        Console.Error.WriteLine(imported.Warning);

    // Only an explicitly configured searches.json seeds a new settings.json: the repository ships none.
    var settingsResult = SettingsStore.LoadOrCreate(dataDir.SettingsPath, configuration["Jobbby:SearchesConfig"]);
    if (settingsResult.Created)
        Console.WriteLine($"Creato {dataDir.SettingsPath} con le impostazioni predefinite.");
    foreach (var note in settingsResult.Notes)
        Console.WriteLine(note);
    var settings = settingsResult.Settings;

    // Decision commands: no run, no LLM, no Adzuna.
    switch (args.FirstOrDefault()?.ToLowerInvariant())
    {
        case "pending":
            return CliCommands.Pending(dataDir, Console.Out, settings.Dedupe.ExtraCompanySuffixes);
        case "decide" when args.Length == 3:
            return CliCommands.Decide(dataDir, args[1], args[2], Console.Out, Console.Error, settings.Dedupe.ExtraCompanySuffixes);
        case "decide":
            Console.Error.WriteLine("Uso: decide <postingId> approve|reject|applied");
            return 2;
        case { } unknown:
            Console.Error.WriteLine($"Comando sconosciuto: '{unknown}'. Comandi: pending, decide <postingId> approve|reject|applied (senza argomenti: una run).");
            return 2;
    }

    var errors = SettingsValidator.Validate(settings, forRun: true);
    if (errors.Count > 0)
    {
        Console.Error.WriteLine($"Impostazioni non valide in {dataDir.SettingsPath}:");
        foreach (var error in errors)
            Console.Error.WriteLine($"  {error.Field}: {error.Message}");
        return 2;
    }

    var mode = DryRunOptions.FromConfiguration(configuration, baseDirectory).Enabled ? RunMode.Dry : RunMode.Normal;
    using var httpClient = new HttpClient();
    var llm = LlmClientFactory.Create(settings.Llm, configuration, httpClient);
    Console.WriteLine($"LLM: {llm.Provider} {llm.Model}");

    var cvPath = configuration["Jobbby:CvPath"]
        ?? new[] { dataDir.CvExtractedPath, dataDir.CvJsonPath, dataDir.CvPdfPath }.FirstOrDefault(File.Exists);
    if (cvPath is null)
    {
        Console.Error.WriteLine($"Nessun CV trovato: metti cv.pdf o cv.json in {dataDir.Root} (oppure imposta Jobbby:CvPath).");
        return 2;
    }

    var cvResult = await CvLoader.LoadWithNotesAsync(cvPath, llm.Client);
    foreach (var field in cvResult.IgnoredFields)
        Console.WriteLine($"Campo del CV ignorato, ora si imposta in settings.json: {field}");
    Console.WriteLine($"CV: {cvResult.Cv.Name} ({cvResult.Cv.YearsExperience} anni) da {cvPath}");

    var sources = SourceWhitelist.LoadFromFile(Path.Combine(baseDirectory, "sources.json"));
    var jobSource = AdzunaJobSource.FromEnvironment(
        httpClient,
        settings.Area.Country!,
        resultsPerPage: mode == RunMode.Dry ? settings.DryRun.MaxPostingsPerQuery : 10,
        minimumPlausibleSalary: settings.Salary.MinimumPlausible);
    var discoveryConfigured = !string.IsNullOrWhiteSpace(configuration["Jobbby:DiscoveryConfig"]);

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        Console.Error.WriteLine("Interruzione richiesta: fermo le chiamate in corso...");
        cancellation.Cancel();
    };

    var runner = new JobbbyRunner(dataDir, new RunDependencies(jobSource, llm.Client, sources), discoveryConfigured);
    var progress = new ConsoleProgress();
    var summary = await runner.RunAsync(settings, cvResult.Cv, mode, progress, cancellation.Token);
    Console.WriteLine($"Chiamate Adzuna: {summary.AdzunaCalls}");

    Console.WriteLine($"Run salvata in {summary.RunPath}");

    return summary.Cancelled ? 130 : 0;
}

/// <summary>Writes run events synchronously, so lines appear in order.</summary>
internal sealed class ConsoleProgress : IProgress<RunEvent>
{
    public void Report(RunEvent value)
    {
        var writer = value.Kind is "fetch_failed" or "posting_failed" or "warning" ? Console.Error : Console.Out;
        writer.WriteLine(value.Message);
    }
}
