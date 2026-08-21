using GraphEngine;
using Notifications;

namespace Host.Nodes;

/// <summary>
/// Asks a human to approve sending the application, via the existing
/// <see cref="HumanInputNode"/>/<see cref="ITelegramGateway"/> plumbing. Builds the
/// confirmation prompt from Company/Title in state, then delegates the actual
/// send-and-wait to an internally configured <see cref="HumanInputNode"/>.
/// </summary>
public sealed class AskApprovalNode : INode
{
    public const string PromptStateKey = "AskApprovalPrompt";
    public const string ResponseStateKey = "ApprovalResponse";
    public const string OutcomeStateKey = "ApprovalOutcome";

    private readonly HumanInputNode _humanInputNode;

    public AskApprovalNode(ITelegramGateway gateway, PendingApprovalRegistry registry, TimeSpan? timeout = null)
    {
        _humanInputNode = new HumanInputNode(
            gateway,
            registry,
            promptStateKey: PromptStateKey,
            responseStateKey: ResponseStateKey,
            outcomeStateKey: OutcomeStateKey,
            timeout: timeout);
    }

    public async Task<NodeResult> ExecuteAsync(GraphState state)
    {
        var company = state.Get<string>(JobApplicationStateKeys.Company) ?? string.Empty;
        var title = state.Get<string>(JobApplicationStateKeys.Title) ?? string.Empty;
        var prompt = $"Candidatura fittizia per {company} / {title} - confermi invio? (si/no)";

        // HumanInputNode reads its prompt straight out of state, so it has to be there
        // before we delegate to it.
        state.Set(PromptStateKey, prompt);

        var innerResult = await _humanInputNode.ExecuteAsync(state).ConfigureAwait(false);

        var updates = new Dictionary<string, object>(innerResult.Updates)
        {
            [PromptStateKey] = prompt,
        };

        return NodeResult.From(updates);
    }
}
