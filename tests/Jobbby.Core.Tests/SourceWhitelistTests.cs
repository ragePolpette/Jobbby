using System.Linq;
using Config;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Jobbby.Core.Tests;

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

        Assert.Single(fromYaml);
        Assert.Equal(fromJson, fromYaml);

        var adzuna = fromYaml.Single(s => s.Name == "Adzuna");
        Assert.True(adzuna.RequiresAuth);
        Assert.Equal("Adzuna:AppKey", adzuna.AuthSecretKey);
    }

    [Fact]
    public void ResolveSecret_NoConfiguration_FallsBackToEnvironmentVariable()
    {
        const string key = "SOURCE_WHITELIST_TEST_SECRET";
        Environment.SetEnvironmentVariable(key, "shh");

        try
        {
            Assert.Equal("shh", SourceWhitelist.ResolveSecret(null, key));
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

        try
        {
            Assert.Equal("from-config", SourceWhitelist.ResolveSecret(configuration, key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void ResolveSecret_ConfigurationWithoutTheKey_FallsBackToEnvironmentVariable()
    {
        const string key = "SOURCE_WHITELIST_TEST_SECRET_FALLBACK";
        Environment.SetEnvironmentVariable(key, "from-env");

        try
        {
            Assert.Equal("from-env", SourceWhitelist.ResolveSecret(new ConfigurationBuilder().Build(), key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void SourceWhitelist_HasNoMutableStaticState()
    {
        var mutableStatics = typeof(SourceWhitelist)
            .GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Where(property => property.CanWrite);

        Assert.Empty(mutableStatics);
    }
}
