using Config;
using Host;
using JobPostings;

namespace Web;

/// <summary>Builds what a run needs from the settings; replaced by fakes in the tests.</summary>
public interface IRunDependenciesFactory
{
    RunDependencies Create(JobbbySettings settings, RunMode mode, Func<IReadOnlyList<string>, ApplicationLedger.ApplicationLedger> ledger);

    /// <summary>The configured LLM alone, for CV extraction and query previews outside a run.</summary>
    GraphEngine.ILlmClient CreateLlm(JobbbySettings settings);
}

/// <summary>Adzuna and the configured LLM, with secrets from user-secrets / environment.</summary>
public sealed class DefaultRunDependenciesFactory(IConfiguration configuration, IHttpClientFactory httpClients) : IRunDependenciesFactory
{
    public RunDependencies Create(JobbbySettings settings, RunMode mode, Func<IReadOnlyList<string>, ApplicationLedger.ApplicationLedger> ledger)
    {
        var http = httpClients.CreateClient("jobbby");
        var llm = LlmClientFactory.Create(settings.Llm, configuration, http);
        var source = AdzunaJobSource.FromEnvironment(
            http,
            settings.Area.Country!,
            resultsPerPage: mode == RunMode.Dry ? settings.DryRun.MaxPostingsPerQuery : 10,
            minimumPlausibleSalary: settings.Salary.MinimumPlausible);
        var sources = SourceWhitelist.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "sources.json"));
        return new RunDependencies(source, llm.Client, sources, ledger);
    }

    public GraphEngine.ILlmClient CreateLlm(JobbbySettings settings) =>
        LlmClientFactory.Create(settings.Llm, configuration, httpClients.CreateClient("jobbby")).Client;
}

/// <summary>
/// The one <see cref="ApplicationLedger.ApplicationLedger"/> of the process, shared by runs and
/// API decisions: two instances would each hold the file in memory and overwrite each other.
/// A change of the company-suffix settings rekeys that same instance, even mid-run.
/// </summary>
public sealed class LedgerHolder(DataDir dataDir)
{
    private readonly object _lock = new();
    private ApplicationLedger.ApplicationLedger? _ledger;
    private string _suffixKey = string.Empty;

    public ApplicationLedger.ApplicationLedger Get(IReadOnlyList<string> extraCompanySuffixes)
    {
        var key = string.Join('\u0001', extraCompanySuffixes.Select(ApplicationLedger.PostingIdentity.NormalizeText).Where(s => s.Length > 0).Distinct().Order());
        lock (_lock)
        {
            if (_ledger is null)
                _ledger = new ApplicationLedger.ApplicationLedger(dataDir.ApplicationsPath, extraCompanySuffixes);
            else if (key != _suffixKey)
                _ledger.Rekey(extraCompanySuffixes);
            _suffixKey = key;
            return _ledger;
        }
    }
}

/// <summary>settings.json behind a lock, so the UI and a starting run never read a half-saved state.</summary>
public sealed class SettingsService(DataDir dataDir)
{
    private readonly object _lock = new();

    public JobbbySettings Load()
    {
        lock (_lock)
            return SettingsStore.LoadOrCreate(dataDir.SettingsPath, legacySearchesPath: null).Settings;
    }

    public void Save(JobbbySettings settings)
    {
        lock (_lock)
            SettingsStore.Save(dataDir.SettingsPath, settings);
    }
}
