namespace GraphEngine;

/// <summary>
/// Decorates any <see cref="ILlmClient"/> so every caller receives the content without a
/// wrapping markdown code fence, whichever provider produced it.
/// </summary>
public sealed class CodeFenceStrippingLlmClient(ILlmClient inner) : ILlmClient
{
    public ILlmClient Inner { get; } = inner;

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default) =>
        LlmResponse.StripCodeFence(await Inner.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false));
}
