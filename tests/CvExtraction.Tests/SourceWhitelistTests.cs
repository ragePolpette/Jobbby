using System.Linq;
using Config;
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
    public void ResolveSecret_ReadsFromEnvironmentVariable()
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
}
