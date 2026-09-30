using ApplicationLedger;
using GraphEngine;
using JobPostings;

namespace Host.Nodes;

/// <summary>Builds the ledger record of one evaluated posting from the graph state.</summary>
public static class PostingRecordFactory
{
    public static ApplicationRecord Create(GraphState state, string outcome, string reason)
    {
        var posting = state.Get<JobPosting>(NormalizeJobPostingNode.JobPostingStateKey);
        var company = posting?.Company ?? state.Get<string>(JobApplicationStateKeys.Company) ?? string.Empty;
        var title = posting?.Title ?? state.Get<string>(JobApplicationStateKeys.Title) ?? string.Empty;
        var applyUrl = posting?.ApplyUrl ?? state.Get<string>(JobApplicationStateKeys.SourceUrl);
        var dedupeKey = state.Get<string>(DedupeCheckNode.DedupeKeyStateKey) ?? PostingIdentity.Key(company, title, applyUrl: applyUrl);
        var judged = state.TryGet<string>(ScoreMatchNode.MatchCategoryStateKey, out var category);

        return new ApplicationRecord(
            dedupeKey,
            company,
            title,
            applyUrl,
            DateTimeOffset.UtcNow,
            outcome,
            PostingId: PostingIdentity.Id(dedupeKey),
            Reason: reason)
        {
            RunId = state.Get<string>(JobApplicationStateKeys.RunId),
            SourceName = state.Get<string>(JobApplicationStateKeys.SourceName),
            ApplyUrl = applyUrl,
            Excerpt = posting?.Description,
            RequiredSkills = posting?.RequiredSkills,
            MustHaveSkills = posting?.MustHaveSkills,
            PreferredSkills = posting?.PreferredSkills,
            WorkMode = posting?.WorkMode.ToString(),
            Location = posting?.Location,
            SalaryMaximum = posting?.SalaryMaximum,
            MinYearsExperience = posting?.MinYearsExperience,
            Seniority = posting?.SeniorityLevel,
            Confidence = judged ? state.Get<double>(ScoreMatchNode.MatchConfidenceStateKey) : null,
            Category = judged ? category : null,
            Reasoning = state.Get<string>(ScoreMatchNode.MatchJudgmentReasoningStateKey),
            MissingRequirements = state.Get<List<string>>(ScoreMatchNode.MissingRequirementsStateKey),
            Warnings = state.Get<List<string>>(ScoreMatchNode.PreferenceWarningsStateKey),
        };
    }
}
