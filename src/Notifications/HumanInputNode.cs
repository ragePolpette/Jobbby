using GraphEngine;

namespace Notifications;

/// <summary>
/// Generic human-in-the-loop node: sends a message read from state (a draft, a
/// confirmation request, anything) over Telegram, and waits for a reply. Not tied to any
/// particular use case (cover letters, approvals, ...) - which state key holds the
/// prompt/response/outcome is configurable per instance, so a graph can use several of
/// these for different steps.
/// </summary>
public sealed class HumanInputNode : INode
{
    public const string PromptStateKey = "humanInputPrompt";
    public const string ResponseStateKey = "humanInputResponse";
    public const string OutcomeStateKey = "humanInputOutcome";

    public const string RespondedOutcome = "responded";
    public const string SkippedNoResponseOutcome = "skipped_no_response";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromHours(6);

    private readonly ITelegramGateway _gateway;
    private readonly PendingApprovalRegistry _registry;
    private readonly string _promptStateKey;
    private readonly string _responseStateKey;
    private readonly string _outcomeStateKey;
    private readonly TimeSpan _timeout;

    public HumanInputNode(
        ITelegramGateway gateway,
        PendingApprovalRegistry registry,
        string promptStateKey = PromptStateKey,
        string responseStateKey = ResponseStateKey,
        string outcomeStateKey = OutcomeStateKey,
        TimeSpan? timeout = null)
    {
        _gateway = gateway;
        _registry = registry;
        _promptStateKey = promptStateKey;
        _responseStateKey = responseStateKey;
        _outcomeStateKey = outcomeStateKey;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<NodeResult> ExecuteAsync(GraphState state)
    {
        var message = state.Get<string>(_promptStateKey) ?? string.Empty;

        var messageId = await _gateway.SendAsync(message).ConfigureAwait(false);
        var reply = await _registry.WaitForReplyAsync(messageId, _timeout).ConfigureAwait(false);

        if (reply == PendingApprovalRegistry.TimeoutSentinel)
        {
            Console.WriteLine($"[HumanInputNode] No reply to message {messageId} within {_timeout}; proceeding without one.");
            return NodeResult.From(_outcomeStateKey, SkippedNoResponseOutcome);
        }

        return NodeResult.From(new Dictionary<string, object>
        {
            [_outcomeStateKey] = RespondedOutcome,
            [_responseStateKey] = reply,
        });
    }
}
