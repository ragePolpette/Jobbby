using GraphEngine;
using Host.Nodes;
using JobPostings;
using Xunit;

namespace Host.Tests;

public class NormalizeJobPostingNodeTests
{
    private const string FixedExtraction = """
        {"company":"Acme Corp","seniorityLevel":"Senior","requiredStack":["C#",".NET"]}
        """;

    private static GraphState NewStateFor(RawPosting rawPosting, string sourceUrl = "https://apply.example") =>
        new(new Dictionary<string, object>
        {
            [NormalizeJobPostingNode.RawPostingStateKey] = rawPosting,
            [JobApplicationStateKeys.SourceUrl] = sourceUrl,
        });

    [Fact]
    public async Task ExecuteAsync_MailtoApplyUrl_ChannelIsEmail()
    {
        var rawPosting = new RawPosting("Backend Engineer", "Ottima opportunita", "mailto:hr@acme.example", "acme.example");
        var node = new NormalizeJobPostingNode(new MockLlmClient(FixedExtraction));

        var result = await node.ExecuteAsync(NewStateFor(rawPosting));
        var jobPosting = (JobPosting)result.Updates[NormalizeJobPostingNode.JobPostingStateKey];

        Assert.Equal(ApplyChannels.Email, jobPosting.ApplyChannel);
    }

    [Fact]
    public async Task ExecuteAsync_ApplyUrlOnSourceDomain_ChannelIsNativeForm()
    {
        var rawPosting = new RawPosting("Backend Engineer", "Ottima opportunita", "https://acme.example/apply/123", "acme.example");
        var node = new NormalizeJobPostingNode(new MockLlmClient(FixedExtraction));

        var result = await node.ExecuteAsync(NewStateFor(rawPosting));
        var jobPosting = (JobPosting)result.Updates[NormalizeJobPostingNode.JobPostingStateKey];

        Assert.Equal(ApplyChannels.NativeForm, jobPosting.ApplyChannel);
    }

    [Fact]
    public async Task ExecuteAsync_ApplyUrlOnDifferentDomain_ChannelIsExternalPlatform()
    {
        var rawPosting = new RawPosting("Backend Engineer", "Ottima opportunita", "https://jobs.example/apply/123", "acme.example");
        var node = new NormalizeJobPostingNode(new MockLlmClient(FixedExtraction));

        var result = await node.ExecuteAsync(NewStateFor(rawPosting));
        var jobPosting = (JobPosting)result.Updates[NormalizeJobPostingNode.JobPostingStateKey];

        Assert.Equal(ApplyChannels.ExternalPlatform, jobPosting.ApplyChannel);
    }

    [Fact]
    public async Task ExecuteAsync_ApplyUrlNotAbsolute_FallsBackToExternalPlatform()
    {
        var rawPosting = new RawPosting("Backend Engineer", "Ottima opportunita", "/apply/123", "acme.example");
        var node = new NormalizeJobPostingNode(new MockLlmClient(FixedExtraction));

        var result = await node.ExecuteAsync(NewStateFor(rawPosting));
        var jobPosting = (JobPosting)result.Updates[NormalizeJobPostingNode.JobPostingStateKey];

        Assert.Equal(ApplyChannels.ExternalPlatform, jobPosting.ApplyChannel);
    }

    [Fact]
    public async Task ExecuteAsync_WritesLlmExtractedFieldsAndFlatCompanyTitleKeys()
    {
        var rawPosting = new RawPosting("Backend Engineer", "Ottima opportunita in C#", "https://jobs.example/apply/123", "acme.example");
        var node = new NormalizeJobPostingNode(new MockLlmClient(FixedExtraction));

        var result = await node.ExecuteAsync(NewStateFor(rawPosting, "https://acme.example"));
        var jobPosting = (JobPosting)result.Updates[NormalizeJobPostingNode.JobPostingStateKey];

        Assert.Equal("Acme Corp", jobPosting.Company);
        Assert.Equal("Senior", jobPosting.SeniorityLevel);
        Assert.Equal(new[] { "C#", ".NET" }, jobPosting.RequiredStack);
        Assert.Equal("Backend Engineer", jobPosting.Title);
        Assert.Equal("Ottima opportunita in C#", jobPosting.Description);
        Assert.Equal("https://jobs.example/apply/123", jobPosting.ApplyUrl);
        Assert.Equal("https://acme.example", jobPosting.SourceUrl);

        Assert.Equal("Acme Corp", result.Updates[JobApplicationStateKeys.Company]);
        Assert.Equal("Backend Engineer", result.Updates[JobApplicationStateKeys.Title]);
    }

    [Fact]
    public async Task ExecuteAsync_NoRawPostingInState_Throws()
    {
        var node = new NormalizeJobPostingNode(new MockLlmClient(FixedExtraction));
        var state = new GraphState();

        await Assert.ThrowsAsync<InvalidOperationException>(() => node.ExecuteAsync(state));
    }
}
