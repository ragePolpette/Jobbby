using Config;
using GraphEngine;
using Host;
using JobPostings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Web;

namespace Web.Tests;

/// <summary>
/// The real web app over a throwaway DataDir, with a fake job source and LLM so runs are fast
/// and offline. <see cref="Source"/> and <see cref="Llm"/> can be swapped per test.
/// </summary>
public sealed class JobbbyWebFactory : WebApplicationFactory<Program>
{
    public JobbbyWebFactory()
    {
        Root = Path.Combine(Path.GetTempPath(), "jobbby-web-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        DataDir = new DataDir(Root);
        File.WriteAllText(DataDir.CvJsonPath, """{"name":"Candidata","yearsExperience":5,"skills":["Triage"],"location":"Köln"}""");
    }

    public string Root { get; }

    public DataDir DataDir { get; }

    public IJobSource Source { get; set; } = new MockJobSource(_ => Array.Empty<RawPosting>());

    public ILlmClient Llm { get; set; } = new PromptRoutedLlm();

    public void WriteSettings(JobbbySettings settings) => SettingsStore.Save(DataDir.SettingsPath, settings);

    public static JobbbySettings RunnableSettings()
    {
        var d = JobbbySettings.Default;
        return d with { Area = d.Area with { Country = "de" }, Searches = d.Searches with { Queries = new() { "nurse" }, DeriveFromCv = false } };
    }

    /// <summary>A client that behaves like the app's own pages: same origin, custom header.</summary>
    public HttpClient CreateAppClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Jobbby-Request", "1");
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jobbby:DataDir", Root);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRunDependenciesFactory>();
            services.AddSingleton<IRunDependenciesFactory>(new FakeDependencies(this));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    private sealed class FakeDependencies(JobbbyWebFactory factory) : IRunDependenciesFactory
    {
        public RunDependencies Create(JobbbySettings settings, RunMode mode, Func<IReadOnlyList<string>, ApplicationLedger.ApplicationLedger> ledger) =>
            new(factory.Source, factory.Llm, new[] { new SourceDefinition { Name = "Adzuna", BaseUrl = "https://api.adzuna.com" } }, ledger);

        public ILlmClient CreateLlm(JobbbySettings settings) => factory.Llm;
    }
}

/// <summary>An LLM answering every prompt with a function of it.</summary>
public sealed class FuncLlm(Func<string, string> respond) : ILlmClient
{
    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default) => Task.FromResult(respond(prompt));
}

/// <summary>Like <see cref="FuncLlm"/>, for answers that wait on something.</summary>
public sealed class AsyncFuncLlm(Func<string, Task<string>> respond) : ILlmClient
{
    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default) => respond(prompt);
}

/// <summary>Answers normalization with fixed fields and stage two with a Borderline judgment (Pending).</summary>
public sealed class PromptRoutedLlm(Func<CancellationToken, Task>? onExtraction = null) : ILlmClient
{
    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (prompt.Contains("Estrai le seguenti informazioni", StringComparison.Ordinal))
        {
            if (onExtraction is not null)
                await onExtraction(cancellationToken);
            return """{"seniorityLevel":"","requiredSkills":["Triage"],"workMode":"onsite"}""";
        }

        return """{"category":"Borderline","reasoning":"Esperienza in triage.","confidence":0.5}""";
    }
}
