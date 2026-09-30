using CvExtraction;
using JobPostings;
using Xunit;

namespace Matching.Tests;

public class MatchStageOneFilterTests
{
    /// <summary>No area, remote accepted, no salary floor: only the skill rules apply.</summary>
    private static readonly StageOneCriteria Anything = new("", GeoFilteredBySource: false, AcceptsRemote: true, MinimumYearlySalary: null);

    private static readonly StageOneCriteria MilanoGeo = new("Milano", GeoFilteredBySource: true, AcceptsRemote: true, MinimumYearlySalary: 33000);

    private static JobPosting NewPosting(IEnumerable<string> requiredSkills, string seniorityLevel = "") =>
        new("Backend Engineer", "Acme", seniorityLevel, requiredSkills.ToList(), "desc",
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
    public void Evaluate_StackOverlap_Passes()
    {
        var (passes, reason) = MatchStageOneFilter.Evaluate(NewPosting(new[] { "C#", "SQL" }), NewCv(4, skills: new[] { "C#", "Docker" }), Anything);

        Assert.True(passes);
        Assert.Contains("C#", reason);
    }

    [Fact]
    public void Evaluate_StackMatchIsCaseInsensitive()
    {
        Assert.True(MatchStageOneFilter.Evaluate(NewPosting(new[] { "c#" }), NewCv(4, skills: new[] { "C#" }), Anything).Passes);
    }

    [Fact]
    public void Evaluate_StackFromRolesAlsoCounts()
    {
        var cv = NewCv(4, skills: new[] { "C#" }, roles: new[]
        {
            new CvRole { Title = "Backend Engineer", Company = "Prev Co", Skills = new List<string> { "Kubernetes" }, Highlights = new List<string>() },
        });

        Assert.True(MatchStageOneFilter.Evaluate(NewPosting(new[] { "Kubernetes" }), cv, Anything).Passes);
    }

    [Fact]
    public void Evaluate_GenericSkillCoveredBySpecificVariant_Passes()
    {
        // Regression: a ".NET" posting was rejected because the CV listed ".NET Core"/".NET Framework".
        var (passes, reason) = MatchStageOneFilter.Evaluate(NewPosting(new[] { ".NET" }), NewCv(7, skills: new[] { ".NET Core", ".NET Framework" }), Anything);

        Assert.True(passes);
        Assert.Contains(".NET", reason);
    }

    [Fact]
    public void Evaluate_NoStackOverlap_Fails()
    {
        var (passes, reason) = MatchStageOneFilter.Evaluate(NewPosting(new[] { "Rust" }), NewCv(4, skills: new[] { "C#", ".NET" }), Anything);

        Assert.False(passes);
        Assert.Contains("sovrapposizione", reason);
    }

    [Fact]
    public void Evaluate_EmptyRequiredSkills_StackCheckPasses()
    {
        Assert.True(MatchStageOneFilter.Evaluate(NewPosting(Array.Empty<string>()), NewCv(4, skills: new[] { "C#" }), Anything).Passes);
    }

    [Fact]
    public void Evaluate_MissingMustHaveSkill_FailsAndExplainsRequirement()
    {
        var posting = NewPosting(new[] { "C#" }) with { MustHaveSkills = new List<string> { "Kubernetes" } };

        var result = MatchStageOneFilter.Evaluate(posting, NewCv(4, skills: new[] { "C#" }), Anything);

        Assert.False(result.Passes);
        Assert.Contains("Kubernetes", result.MissingRequirements);
    }

    [Fact]
    public void Evaluate_MissingPreferredSkill_PassesWithWarning()
    {
        var posting = NewPosting(new[] { "C#" }) with { PreferredSkills = new List<string> { "Azure" } };

        var result = MatchStageOneFilter.Evaluate(posting, NewCv(4, skills: new[] { "C#" }), Anything);

        Assert.True(result.Passes);
        Assert.Contains(result.PreferenceWarnings, warning => warning.Contains("Azure"));
    }

    [Fact]
    public void Evaluate_MissingRequiredLanguage_Fails()
    {
        var posting = NewPosting(new[] { "C#" }) with { RequiredLanguages = new List<string> { "German" } };

        var result = MatchStageOneFilter.Evaluate(posting, NewCv(4, skills: new[] { "C#" }), Anything);

        Assert.False(result.Passes);
        Assert.Contains("Lingua: German", result.MissingRequirements);
    }

    // --- location: the source's geographic filter is authoritative

    [Fact]
    public void Evaluate_GeoFilteredLocalResult_OutsideTextualMatch_Passes()
    {
        var posting = NewPosting(new[] { "C#" }) with { Location = "Sesto San Giovanni", WorkMode = WorkMode.Onsite };

        Assert.True(MatchStageOneFilter.Evaluate(posting, NewCv(4, new[] { "C#" }), MilanoGeo).Passes);
    }

    [Theory]
    [InlineData(WorkMode.Remote, true)]
    [InlineData(WorkMode.Hybrid, false)]
    [InlineData(WorkMode.Onsite, false)]
    [InlineData(WorkMode.Unknown, false)]
    public void Evaluate_RemoteSweepResult_PassesOnlyWhenRemote(WorkMode mode, bool expected)
    {
        var posting = NewPosting(new[] { "C#" }) with { Location = "Roma", WorkMode = mode, Sweep = SearchSweep.Remote };

        Assert.Equal(expected, MatchStageOneFilter.Evaluate(posting, NewCv(4, new[] { "C#" }), MilanoGeo).Passes);
    }

    [Theory]
    [InlineData(WorkMode.Onsite, "Roma", false)]
    [InlineData(WorkMode.Hybrid, "Roma", false)]
    [InlineData(WorkMode.Hybrid, "Milano, Lombardia", true)]
    [InlineData(WorkMode.Onsite, "milano", true)]
    [InlineData(WorkMode.Unknown, "Roma", true)]
    [InlineData(WorkMode.Remote, "Roma", true)]
    public void Evaluate_NoSourceGeoFilter_TextualCheckOnlyForOnsiteAndHybrid(WorkMode mode, string location, bool expected)
    {
        var criteria = MilanoGeo with { GeoFilteredBySource = false };
        var posting = NewPosting(new[] { "C#" }) with { Location = location, WorkMode = mode };

        var result = MatchStageOneFilter.Evaluate(posting, NewCv(4, new[] { "C#" }), criteria);

        Assert.Equal(expected, result.Passes);
        if (!expected)
            Assert.Contains(result.MissingRequirements, requirement => requirement.Contains(location));
    }

    [Fact]
    public void Evaluate_NoWhereConfigured_NeverChecksLocation()
    {
        var posting = NewPosting(new[] { "C#" }) with { Location = "Anywhere", WorkMode = WorkMode.Onsite };

        Assert.True(MatchStageOneFilter.Evaluate(posting, NewCv(4, new[] { "C#" }), Anything).Passes);
    }

    [Fact]
    public void Evaluate_RemoteNotAccepted_PassesWithWarning()
    {
        var criteria = Anything with { AcceptsRemote = false };

        var result = MatchStageOneFilter.Evaluate(NewPosting(new[] { "C#" }) with { WorkMode = WorkMode.Remote }, NewCv(4, new[] { "C#" }), criteria);

        Assert.True(result.Passes);
        Assert.NotEmpty(result.PreferenceWarnings);
    }

    // --- salary: client-side only, unknown never excludes

    [Theory]
    [InlineData(30000.0, false)]
    [InlineData(40000.0, true)]
    [InlineData(null, true)]
    public void Evaluate_Salary_FiltersOnlyKnownLowerValues(double? salary, bool expected)
    {
        var posting = NewPosting(new[] { "C#" }) with { SalaryMaximum = (decimal?)salary };

        var result = MatchStageOneFilter.Evaluate(posting, NewCv(4, new[] { "C#" }), MilanoGeo);

        Assert.Equal(expected, result.Passes);
        if (!expected)
            Assert.Contains(result.MissingRequirements, requirement => requirement.Contains("Retribuzione"));
    }

    [Fact]
    public void Evaluate_NoSalaryFloor_IgnoresSalary()
    {
        var posting = NewPosting(new[] { "C#" }) with { SalaryMaximum = 1 };

        Assert.True(MatchStageOneFilter.Evaluate(posting, NewCv(4, new[] { "C#" }), Anything).Passes);
    }

    // --- experience: years only, labels never exclude

    [Theory]
    [InlineData(5.0, 4.0, false)]
    [InlineData(3.0, 4.0, true)]
    [InlineData(4.0, 4.0, true)]
    [InlineData(null, 0.5, true)]
    public void Evaluate_Experience_ComparesYearsOnly_NeverLabels(double? required, double candidateYears, bool expected)
    {
        var posting = NewPosting(new[] { "C#" }, seniorityLevel: "Principal Staff Lead") with { MinYearsExperience = required };

        var result = MatchStageOneFilter.Evaluate(posting, NewCv(candidateYears, new[] { "C#" }), Anything);

        Assert.Equal(expected, result.Passes);
        if (!expected)
            Assert.Contains(result.MissingRequirements, requirement => requirement.Contains("anni"));
    }

    [Fact]
    public void Evaluate_BothStackAndExperienceFail_ReportsStackReasonFirst()
    {
        var posting = NewPosting(new[] { "Rust" }) with { MinYearsExperience = 10 };

        var (passes, reason) = MatchStageOneFilter.Evaluate(posting, NewCv(1, skills: new[] { "C#" }), Anything);

        Assert.False(passes);
        Assert.StartsWith("Requisiti mancanti: Nessuna sovrapposizione", reason);
    }

    [Fact]
    public void FromSettings_GeoFilteredOnlyWithWhere()
    {
        var withWhere = StageOneCriteria.FromSettings(new Config.AreaSettings { Where = " Milano ", AcceptsRemote = true }, new Config.SalarySettings { MinimumYearly = 33000 });
        var without = StageOneCriteria.FromSettings(new Config.AreaSettings(), new Config.SalarySettings());

        Assert.Equal(new StageOneCriteria("Milano", true, true, 33000), withWhere);
        Assert.Equal(new StageOneCriteria("", false, false, null), without);
    }
}
