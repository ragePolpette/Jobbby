using CvExtraction;
using GraphEngine;
using JobPostings;
using Matching;

namespace Host.Nodes;

/// <summary>
/// Two-stage match scoring for the normalized JobPosting against one fixed candidate CV.
/// MatchStageOneFilter runs first (no LLM, cheap): a fail is recorded but this node does
/// not decide routing on its own - HostGraph's edge sends stage-one failures straight to
/// END, so they never reach Telegram. A pass is handed to MatchStageTwoJudge (LLM), and
/// the judgment is mapped to the single numeric MatchConfidence the existing
/// threshold-based downstream routing already consumes, unchanged.
/// </summary>
public sealed class ScoreMatchNode : INode
{
    public const string MatchConfidenceStateKey = "MatchConfidence";
    public const string StageOnePassedStateKey = "StageOnePassed";
    public const string StageOneReasonStateKey = "StageOneReason";
    public const string MatchJudgmentReasoningStateKey = "MatchJudgmentReasoning";

    private readonly CvData _candidateCv;
    private readonly MatchStageTwoJudge _stageTwoJudge;

    public ScoreMatchNode(CvData candidateCv, MatchStageTwoJudge stageTwoJudge)
    {
        _candidateCv = candidateCv;
        _stageTwoJudge = stageTwoJudge;
    }

    public async Task<NodeResult> ExecuteAsync(GraphState state)
    {
        var jobPosting = state.Get<JobPosting>(NormalizeJobPostingNode.JobPostingStateKey)
            ?? throw new InvalidOperationException($"No JobPosting found in state under '{NormalizeJobPostingNode.JobPostingStateKey}'.");

        var (stageOnePassed, stageOneReason) = MatchStageOneFilter.Evaluate(jobPosting, _candidateCv);

        if (!stageOnePassed)
        {
            return NodeResult.From(new Dictionary<string, object>
            {
                [MatchConfidenceStateKey] = 0.0,
                [StageOnePassedStateKey] = false,
                [StageOneReasonStateKey] = stageOneReason,
            });
        }

        var judgment = await _stageTwoJudge.JudgeAsync(jobPosting, _candidateCv).ConfigureAwait(false);
        var matchConfidence = MatchConfidenceMapper.ToMatchConfidence(judgment);

        return NodeResult.From(new Dictionary<string, object>
        {
            [MatchConfidenceStateKey] = matchConfidence,
            [StageOnePassedStateKey] = true,
            [StageOneReasonStateKey] = stageOneReason,
            [MatchJudgmentReasoningStateKey] = judgment.Reasoning,
        });
    }
}
