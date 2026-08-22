using CvExtraction;
using JobPostings;
using Xunit;

namespace Matching.Tests;

public class MatchStageOneFilterTests
{
    private static JobPosting NewPosting(IEnumerable<string> requiredStack, string seniorityLevel) =>
        new("Backend Engineer", "Acme", seniorityLevel, requiredStack.ToList(), "desc",
            "https://x.example", "https://x.example/apply", ApplyChannels.ExternalPlatform);

    private static CvData NewCv(double yearsExperience, IEnumerable<string>? skills = null, IEnumerable<CvRole>? roles = null) => new()
    {
        Name = "Candidate",
        YearsExperience = yearsExperience,
        Seniority = "n/a",
        Roles = roles?.ToList() ?? new List<CvRole>(),
        Skills = skills?.ToList() ?? new List<string>(),
        Languages = new List<string>(),
    };

    [Fact]
    public void Evaluate_StackOverlapAndAdequateSeniority_Passes()
    {
        var posting = NewPosting(new[] { "C#", "SQL" }, "Mid");
        var cv = NewCv(4, skills: new[] { "C#", "Docker" });

        var (passes, reason) = MatchStageOneFilter.Evaluate(posting, cv);

        Assert.True(passes);
        Assert.Contains("C#", reason);
    }

    [Fact]
    public void Evaluate_StackMatchIsCaseInsensitive()
    {
        var posting = NewPosting(new[] { "c#" }, "Mid");
        var cv = NewCv(4, skills: new[] { "C#" });

        var (passes, _) = MatchStageOneFilter.Evaluate(posting, cv);

        Assert.True(passes);
    }

    [Fact]
    public void Evaluate_StackFromRolesAlsoCounts()
    {
        var posting = NewPosting(new[] { "Kubernetes" }, "Mid");
        var cv = NewCv(4, skills: new[] { "C#" }, roles: new[]
        {
            new CvRole { Title = "Backend Engineer", Company = "Prev Co", Stack = new List<string> { "Kubernetes" }, Highlights = new List<string>() },
        });

        var (passes, _) = MatchStageOneFilter.Evaluate(posting, cv);

        Assert.True(passes);
    }

    [Fact]
    public void Evaluate_NoStackOverlap_Fails()
    {
        var posting = NewPosting(new[] { "Rust" }, "Mid");
        var cv = NewCv(4, skills: new[] { "C#", ".NET" });

        var (passes, reason) = MatchStageOneFilter.Evaluate(posting, cv);

        Assert.False(passes);
        Assert.Contains("sovrapposizione", reason);
    }

    [Fact]
    public void Evaluate_EmptyRequiredStack_StackCheckPasses()
    {
        var posting = NewPosting(Array.Empty<string>(), "Mid");
        var cv = NewCv(4, skills: new[] { "C#" });

        var (passes, _) = MatchStageOneFilter.Evaluate(posting, cv);

        Assert.True(passes);
    }

    [Fact]
    public void Evaluate_CandidateSeniorityTooLow_Fails()
    {
        var posting = NewPosting(new[] { "C#" }, "Senior");
        var cv = NewCv(1, skills: new[] { "C#" }); // ~Junior band

        var (passes, reason) = MatchStageOneFilter.Evaluate(posting, cv);

        Assert.False(passes);
        Assert.Contains("Seniority", reason);
    }

    [Fact]
    public void Evaluate_CandidateOverqualified_StillPasses()
    {
        var posting = NewPosting(new[] { "C#" }, "Mid");
        var cv = NewCv(10, skills: new[] { "C#" }); // Staff band, well above Mid

        var (passes, _) = MatchStageOneFilter.Evaluate(posting, cv);

        Assert.True(passes);
    }

    [Fact]
    public void Evaluate_CandidateExactlySeniorForSeniorRole_Passes()
    {
        var posting = NewPosting(new[] { "C#" }, "Senior");
        var cv = NewCv(6, skills: new[] { "C#" }); // Senior band

        var (passes, _) = MatchStageOneFilter.Evaluate(posting, cv);

        Assert.True(passes);
    }

    [Fact]
    public void Evaluate_BothStackAndSeniorityFail_ReportsStackReasonFirst()
    {
        var posting = NewPosting(new[] { "Rust" }, "Staff");
        var cv = NewCv(1, skills: new[] { "C#" });

        var (passes, reason) = MatchStageOneFilter.Evaluate(posting, cv);

        Assert.False(passes);
        Assert.Contains("sovrapposizione", reason);
    }
}
