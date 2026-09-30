using System.Text.Json.Serialization;
using Config;
using Host;
using Web.Api;

namespace Web;

/// <summary>
/// The web host: one process per DataDir (the lock is taken at startup and held until exit),
/// the same first-start migration as the CLI, only local hosts accepted, and the request guard
/// in front of every state-changing API.
/// </summary>
public static class JobbbyWeb
{
    public const string DefaultUrl = "http://0.0.0.0:5080";

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddUserSecrets(typeof(JobbbyWeb).Assembly, optional: true);
        if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]) && string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_URLS"]))
            builder.WebHost.UseUrls(DefaultUrl);
        builder.Configuration["AllowedHosts"] ??= "localhost;127.0.0.1";

        SourceWhitelist.Configuration = builder.Configuration;
        var dataDir = DataDir.FromConfiguration(builder.Configuration);
        var dataDirLock = DataDirLock.Acquire(dataDir);
        LegacyMigration.Run(dataDir, new[] { builder.Configuration["Jobbby:LegacyDir"], AppContext.BaseDirectory }.OfType<string>());
        var runs = new RunStore(dataDir);
        runs.RecoverInterrupted();
        runs.ImportLegacyReports();

        builder.Services.AddSingleton(dataDir);
        builder.Services.AddSingleton(dataDirLock);
        builder.Services.AddSingleton(runs);
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<LedgerHolder>();
        builder.Services.AddSingleton<RunManager>();
        builder.Services.AddSingleton<IRunDependenciesFactory, DefaultRunDependenciesFactory>();
        builder.Services.AddHttpClient("jobbby");
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        var app = builder.Build();
        app.Lifetime.ApplicationStopped.Register(dataDirLock.Dispose);

        app.UseRequestGuard();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapSettingsApi();
        app.MapRunsApi();
        app.MapPostingsApi();
        app.MapCvApi();
        return app;
    }
}
