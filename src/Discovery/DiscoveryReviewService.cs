using Config;
using Notifications;

namespace Discovery;

public sealed record DiscoveryReviewResult(
    IReadOnlyList<DiscoveryCandidate> Candidates,
    IReadOnlyList<SourceDefinition> ApprovedSources);

public sealed class DiscoveryReviewService
{
    private readonly DiscoveryEngine _engine;
    private readonly ITelegramGateway _gateway;
    private readonly PendingApprovalRegistry _registry;
    private readonly TimeSpan _approvalTimeout;
    private readonly int _minimumReliabilityScore;

    public DiscoveryReviewService(
        DiscoveryEngine engine,
        ITelegramGateway gateway,
        PendingApprovalRegistry registry,
        TimeSpan? approvalTimeout = null,
        int minimumReliabilityScore = 7)
    {
        _engine = engine;
        _gateway = gateway;
        _registry = registry;
        _approvalTimeout = approvalTimeout ?? TimeSpan.FromHours(6);
        _minimumReliabilityScore = minimumReliabilityScore;
    }

    public async Task<DiscoveryReviewResult> DiscoverAndReviewAsync(
        DiscoveryCriteria criteria,
        CancellationToken cancellationToken = default,
        int? maxCandidates = null)
    {
        var candidates = await _engine.DiscoverAsync(criteria, cancellationToken, maxCandidates).ConfigureAwait(false);
        var approved = new List<SourceDefinition>();

        foreach (var candidate in candidates.Where(candidate => candidate.ReliabilityScore >= _minimumReliabilityScore))
        {
            var message = $"Nuova fonte candidata: {candidate.Name}\n{candidate.Url}\nAffidabilità: {candidate.ReliabilityScore}/10\n{candidate.EvaluationSummary}\nRispondi si per approvarla.";
            var messageId = await _gateway.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var reply = await _registry.WaitForReplyAsync(messageId, _approvalTimeout).ConfigureAwait(false);

            if (reply.Trim().Equals("si", StringComparison.OrdinalIgnoreCase) ||
                reply.Trim().Equals("sì", StringComparison.OrdinalIgnoreCase))
            {
                approved.Add(new SourceDefinition
                {
                    Name = candidate.Name,
                    BaseUrl = candidate.Url,
                    Type = "discovered",
                    RequiresAuth = false,
                });
            }
        }

        return new DiscoveryReviewResult(candidates, approved);
    }
}
