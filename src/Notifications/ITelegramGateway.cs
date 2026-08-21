namespace Notifications;

/// <summary>
/// Bidirectional Telegram messaging seam: send a message and get back the provider's
/// message id, and be told about replies to messages this gateway sent. Kept as an
/// interface so nodes and tests can depend on it without talking to the real Telegram
/// Bot API - see <see cref="MockTelegramGateway"/>.
/// </summary>
public interface ITelegramGateway
{
    /// <summary>Sends a message and returns the message_id assigned by the Telegram API.</summary>
    Task<int> SendAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when a reply to a previously sent message is observed. The first argument
    /// is the message_id being replied to (reply_to_message.message_id), the second is
    /// the reply's text.
    /// </summary>
    event Action<int, string>? ReplyReceived;

    /// <summary>Starts the background polling loop. Does nothing if already started.</summary>
    void Start();

    /// <summary>Stops the background polling loop. Does nothing if not started.</summary>
    void Stop();
}
