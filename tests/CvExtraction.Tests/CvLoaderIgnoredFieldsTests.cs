using GraphEngine;
using Xunit;

namespace CvExtraction.Tests;

public class CvLoaderIgnoredFieldsTests
{
    [Fact]
    public async Task LoadWithNotes_JsonWithPreferenceFields_ReportsThemAsIgnored()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("cv.json"), """{"name":"A","minimumSalary":30000,"desiredLocations":["X"],"acceptsRemote":false}""");

        var result = await CvLoader.LoadWithNotesAsync(dir.Path("cv.json"), new MockLlmClient());

        Assert.Equal("A", result.Cv.Name);
        Assert.Equal(new[] { "acceptsRemote", "desiredLocations", "minimumSalary" }, result.IgnoredFields.OrderBy(f => f, StringComparer.Ordinal));
    }

    [Fact]
    public async Task LoadWithNotes_CleanJson_HasNoNotes()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("cv.json"), """{"name":"A","skills":["Triage"]}""");

        var result = await CvLoader.LoadWithNotesAsync(dir.Path("cv.json"), new MockLlmClient());

        Assert.Empty(result.IgnoredFields);
    }
}
