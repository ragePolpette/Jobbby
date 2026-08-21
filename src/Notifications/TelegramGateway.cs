using System.Text;
using System.Text.Json;
using Config;

namespace Notifications;

/// <summary>
/// Real <see cref="ITelegramGateway"/> backed by the Telegram Bot API: sendMessage to
/// send, and a background long-polling loop over getUpdates to observe replies. The
/// update offset is tracked in memory only - this gateway does not persist polling
/// progress across restarts.
/// </summary>
public sealed class TelegramGateway : ITelegramGateway, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _botToken;
    private readonly long _chatId;
    private readonly int _longPollSeconds;

    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private int _updateOffset;

    public event Action<int, string>? ReplyReceived;

    public TelegramGateway(HttpClient httpClient, string botToken, long chatId, int longPollSeconds = 30)
    {
        _httpClient = httpClient;
        _botToken = botToken;
        _chatId = chatId;
        _longPollSeconds = longPollSeconds;
    }

    /// <summary>
    /// Builds a gateway resolving the bot token and chat id the same way
    /// <see cref="SourceWhitelist.ResolveSecret"/> resolves any other secret: via
    /// `dotnet user-secrets set &lt;key&gt; &lt;value&gt;` in development, or an
    /// environment variable of the same name in production.
    /// </summary>
    public static TelegramGateway FromEnvironment(
        HttpClient httpClient,
        string botTokenSecretKey = "Telegram:BotToken",
        string chatIdSecretKey = "Telegram:ChatId")
    {
        var token = SourceWhitelist.ResolveSecret(botTokenSecretKey)
            ?? throw new InvalidOperationException(
                $"Missing secret '{botTokenSecretKey}'. Set it with `dotnet user-secrets set {botTokenSecretKey} <value>` " +
                "in development, or as an environment variable in production.");

        var chatIdRaw = SourceWhitelist.ResolveSecret(chatIdSecretKey)
            ?? throw new InvalidOperationException(
                $"Missing secret '{chatIdSecretKey}'. Set it with `dotnet user-secrets set {chatIdSecretKey} <value>` " +
                "in development, or as an environment variable in production.");

        return new TelegramGateway(httpClient, token, long.Parse(chatIdRaw));
    }

    public async Task<int> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { chat_id = _chatId, text = message });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient
            .PostAsync($"https://api.telegram.org/bot{_botToken}/sendMessage", content, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return document.RootElement.GetProperty("result").GetProperty("message_id").GetInt32();
        }
    }

    public void Start()
    {
        if (_pollingTask is not null)
            return;

        _pollingCts = new CancellationTokenSource();
        _pollingTask = PollAsync(_pollingCts.Token);
    }

    public void Stop()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
        _pollingTask = null;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var url = $"https://api.telegram.org/bot{_botToken}/getUpdates?offset={_updateOffset}&timeout={_longPollSeconds}";
                using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                    HandleUpdates(document.RootElement.GetProperty("result"));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TelegramGateway] Polling error: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void HandleUpdates(JsonElement updates)
    {
        foreach (var update in updates.EnumerateArray())
        {
            _updateOffset = update.GetProperty("update_id").GetInt32() + 1;

            if (!update.TryGetProperty("message", out var message))
                continue;

            if (!message.TryGetProperty("reply_to_message", out var replyToMessage))
                continue;

            var replyToMessageId = replyToMessage.GetProperty("message_id").GetInt32();
            var text = message.TryGetProperty("text", out var textProperty) ? textProperty.GetString() ?? string.Empty : string.Empty;

            ReplyReceived?.Invoke(replyToMessageId, text);
        }
    }

    public void Dispose() => Stop();
}
