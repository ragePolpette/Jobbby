using Config;
using Discovery;
using Xunit;

namespace Discovery.Tests;

public class DiscoveryCriteriaConfigLoaderTests
{
    [Fact]
    public void Load_Json_ParsesSearchIntentAndEvaluationCriteria()
    {
        var path = Path.Combine(Path.GetTempPath(), $"criteria-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "searchIntent": "mercati di antiquariato in Toscana",
              "evaluationCriteria": "autenticita dei pezzi secondo recensioni di esperti"
            }
            """);

        try
        {
            var criteria = ConfigLoader.Load<DiscoveryCriteria>(path);

            Assert.Equal("mercati di antiquariato in Toscana", criteria.SearchIntent);
            Assert.Equal("autenticita dei pezzi secondo recensioni di esperti", criteria.EvaluationCriteria);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_Yaml_ParsesSearchIntentAndEvaluationCriteria()
    {
        var path = Path.Combine(Path.GetTempPath(), $"criteria-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            searchIntent: mercati di antiquariato in Toscana
            evaluationCriteria: autenticita dei pezzi secondo recensioni di esperti
            """);

        try
        {
            var criteria = ConfigLoader.Load<DiscoveryCriteria>(path);

            Assert.Equal("mercati di antiquariato in Toscana", criteria.SearchIntent);
            Assert.Equal("autenticita dei pezzi secondo recensioni di esperti", criteria.EvaluationCriteria);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
