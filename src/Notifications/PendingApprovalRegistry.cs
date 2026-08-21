using System.Collections.Concurrent;

namespace Notifications;

/// <summary>
/// Correlates outgoing Telegram messages with their eventual reply. A caller registers
/// interest in a message id via <see cref="WaitForReplyAsync"/>; when a
/// <see cref="ITelegramGateway.ReplyReceived"/> event fires for that id, wire it to
/// <see cref="OnReply"/> (e.g. <c>gateway.ReplyReceived += registry.OnReply;</c>) to
/// resolve the wait.
/// </summary>
public sealed class PendingApprovalRegistry
{
    /// <summary>Sentinel returned by <see cref="WaitForReplyAsync"/> when no reply arrives before the timeout.</summary>
    public const string TimeoutSentinel = "__TIMEOUT__";

    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pending = new();

    public async Task<string> WaitForReplyAsync(int messageId, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(messageId, tcs))
            throw new InvalidOperationException($"Already waiting for a reply to message {messageId}.");

        try
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var registration = timeoutCts.Token.Register(() => tcs.TrySetResult(TimeoutSentinel));

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(messageId, out _);
        }
    }

    /// <summary>Resolves the pending wait for <paramref name="replyToMessageId"/>, if any is registered.</summary>
    public void OnReply(int replyToMessageId, string text)
    {
        if (_pending.TryGetValue(replyToMessageId, out var tcs))
            tcs.TrySetResult(text);
    }
}
