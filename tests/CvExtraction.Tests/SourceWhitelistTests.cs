using System.Linq;
using Config;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CvExtraction.Tests;

public class SourceWhitelistTests
{
    [Fact]
    public void Load_ParsesEntriesIncludingAuthSecretKey()
    {
        const string json = """
            [
              { "name": "Public", "baseUrl": "https://a.example", "type": "api", "requiresAuth": false, "authSecretKey": null },
              { "name": "Private", "baseUrl": "https://b.example", "type": "html", "requiresAuth": true, "authSecretKey": "Private:ApiKey" }
            ]
            """;

        var sources = SourceWhitelist.Load(json);

        Assert.Equal(2, sources.Count);

        var priv = sources.Single(s => s.Name == "Private");
        Assert.True(priv.RequiresAuth);
        Assert.Equal("Private:ApiKey", priv.AuthSecretKey);

        var pub = sources.Single(s => s.Name == "Public");
        Assert.False(pub.RequiresAuth);
        Assert.Null(pub.AuthSecretKey);
    }

    [Fact]
    public void LoadFromFile_Json_And_Yaml_ProduceEquivalentWhitelists()
    {
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "Config", "sources.json");
        var yamlPath = Path.Combine(AppContext.BaseDirectory, "Config", "sources.yaml");

        var fromJson = SourceWhitelist.LoadFromFile(jsonPath);
        var fromYaml = SourceWhitelist.LoadFromFile(yamlPath);

        Assert.Equal(3, fromYaml.Count);
        Assert.Equal(fromJson, fromYaml);

        var linkedIn = fromYaml.Single(s => s.Name == "LinkedInJobs");
        Assert.True(linkedIn.RequiresAuth);
        Assert.Equal("LinkedInJobs:ApiKey", linkedIn.AuthSecretKey);
    }

    [Fact]
    public void ResolveSecret_NoConfigurationSet_FallsBackToEnvironmentVariable()
    {
        const string key = "SOURCE_WHITELIST_TEST_SECRET";
        Environment.SetEnvironmentVariable(key, "shh");

        try
        {
            Assert.Equal("shh", SourceWhitelist.ResolveSecret(key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void ResolveSecret_PrefersConfigurationOverEnvironmentVariable()
    {
        const string key = "SOURCE_WHITELIST_TEST_SECRET_CONFIG";
        Environment.SetEnvironmentVariable(key, "from-env");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = "from-config" })
            .Build();

        var previousConfiguration = SourceWhitelist.Configuration;
        SourceWhitelist.Configuration = configuration;

        try
        {
            // Mirrors how Host/Program.cs actually resolves secrets: IConfiguration
            // (user-secrets in dev, env vars in prod) wins when it has the key.
            Assert.Equal("from-config", SourceWhitelist.ResolveSecret(key));
        }
        finally
        {
            SourceWhitelist.Configuration = previousConfiguration;
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void ResolveSecret_ConfigurationSetButMissingKey_FallsBackToEnvironmentVariable()
    {
        const string key = "SOURCE_WHITELIST_TEST_SECRET_FALLBACK";
        Environment.SetEnvironmentVariable(key, "from-env");

        var configuration = new ConfigurationBuilder().Build(); // empty - doesn't have the key

        var previousConfiguration = SourceWhitelist.Configuration;
        SourceWhitelist.Configuration = configuration;

        try
        {
            Assert.Equal("from-env", SourceWhitelist.ResolveSecret(key));
        }
        finally
        {
            SourceWhitelist.Configuration = previousConfiguration;
            Environment.SetEnvironmentVariable(key, null);
        }
    }
}
