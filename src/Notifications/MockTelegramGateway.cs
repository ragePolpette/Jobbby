namespace Notifications;

/// <summary>
/// Fixed-behavior test double for <see cref="ITelegramGateway"/>, mirroring how
/// <c>MockLlmClient</c> stands in for a real LLM provider. SendAsync hands out
/// incrementing fake message ids instead of calling Telegram; <see cref="SimulateReply"/>
/// lets a test trigger a reply as if it had arrived from the real polling loop.
/// </summary>
public sealed class MockTelegramGateway : ITelegramGateway
{
    private int _nextMessageId = 1;

    public List<string> SentMessages { get; } = new();

    public bool IsPolling { get; private set; }

    public event Action<int, string>? ReplyReceived;

    public Task<int> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        SentMessages.Add(message);
        return Task.FromResult(_nextMessageId++);
    }

    public void Start() => IsPolling = true;

    public void Stop() => IsPolling = false;

    /// <summary>Test hook: raises <see cref="ReplyReceived"/> as if this reply had just been polled.</summary>
    public void SimulateReply(int replyToMessageId, string text) => ReplyReceived?.Invoke(replyToMessageId, text);
}
