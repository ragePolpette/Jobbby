using ApplicationLedger;
using Config;
using CvExtraction;
using GraphEngine;
using Host.Nodes;
using JobPostings;
using Matching;
using Reporting;

namespace Host;

/// <summary>
/// Evaluates a posting again on the full text the user pasted: normalization, stage one and the judge,
/// with the same rules as a run but no dedupe and no write. The posting keeps its identity and source
/// data; its outcome follows the new evaluation unless the user has already decided on it.
/// </summary>
public sealed class PostingReevaluator(ILlmClient llm)
{
    private const string Normalize = "Normalize";
    private const string Score = "Score";

    public async Task<ApplicationRecord> ReevaluateAsync(
        ApplicationRecord record,
        string fullText,
        CvData cv,
        JobbbySettings settings,
        SkillAliases aliases,
        SearchSweep sweep = SearchSweep.Local,
        CancellationToken cancellationToken = default)
    {
        var text = fullText?.Trim() ?? string.Empty;
        if (text.Length == 0)
            throw new ArgumentException("Incolla il testo completo dell'annuncio.", nameof(fullText));
        if (text.Length > UserFullText.MaxLength)
            throw new ArgumentException($"Il testo supera i {UserFullText.MaxLength} caratteri.", nameof(fullText));

        var area = AreaResolver.Resolve(settings.Area, cv).Area;
        var definition = new GraphDefinition(maxSteps: 5);
        definition.RegisterNode(Normalize, new NormalizeJobPostingNode(llm));
        definition.RegisterNode(Score, new ScoreMatchNode(cv, new MatchStageTwoJudge(llm), new RunStatsCollector(), StageOneCriteria.FromSettings(area, settings.Salary, aliases)));
        definition.RegisterEdge(Normalize, _ => Score);
        definition.RegisterEdge(Score, _ => GraphDefinition.End);

        var applyUrl = record.ApplyUrl ?? record.SourceUrl ?? string.Empty;
        var raw = new RawPosting(record.Title, text, applyUrl, Uri.TryCreate(applyUrl, UriKind.Absolute, out var uri) ? uri.Host : string.Empty,
            record.Company, Location: record.Location, SalaryMaximum: record.SalaryMaximum, Sweep: sweep);
        var state = new GraphState(new Dictionary<string, object>
        {
            [NormalizeJobPostingNode.RawPostingStateKey] = raw,
            [JobApplicationStateKeys.SourceUrl] = applyUrl,
        });
        await definition.CreateRun().RunAsync(Normalize, state, cancellationToken).ConfigureAwait(false);

        var (outcome, reason) = RecordOutcomeNode.Decide(state, settings.Evaluation.AutoApproveThreshold);
        var fresh = PostingRecordFactory.Create(state, outcome, reason);
        var decided = ApplicationOutcomes.IsUserDecision(record.Outcome);
        var excerptEvaluation = record.FullText?.ExcerptEvaluation
            ?? new EvaluationSnapshot(record.Outcome, record.Reason, record.Confidence, record.Category, record.Reasoning);

        return fresh with
        {
            DedupeKey = record.DedupeKey,
            PostingId = record.PostingId,
            Company = record.Company,
            Title = record.Title,
            SourceUrl = record.SourceUrl,
            RunId = record.RunId,
            SourceName = record.SourceName,
            ApplyUrl = record.ApplyUrl,
            Excerpt = record.Excerpt,
            Location = record.Location ?? fresh.Location,
            SalaryMaximum = record.SalaryMaximum,
            Outcome = decided ? record.Outcome : outcome,
            Reason = decided ? record.Reason : reason,
            Presentation = record.Presentation,
            FullText = new UserFullText(text, DateTimeOffset.UtcNow, excerptEvaluation),
        };
    }
}
