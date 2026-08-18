namespace GraphEngine;

/// <summary>
/// Fixed-response test double for <see cref="ILlmClient"/>. Returns a constant string,
/// or dequeues from a scripted list of responses if one is supplied, so nodes can be
/// exercised without a real LLM provider.
/// </summary>
public sealed class MockLlmClient : ILlmClient
{
    private const string DefaultResponse = "mock response";

    private readonly string _fixedResponse;
    private readonly Queue<string>? _scriptedResponses;

    public MockLlmClient(string fixedResponse = DefaultResponse)
    {
        _fixedResponse = fixedResponse;
    }

    public MockLlmClient(IEnumerable<string> scriptedResponses)
    {
        _scriptedResponses = new Queue<string>(scriptedResponses);
        _fixedResponse = DefaultResponse;
    }

    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var response = _scriptedResponses is { Count: > 0 }
            ? _scriptedResponses.Dequeue()
            : _fixedResponse;

        return Task.FromResult(response);
    }
}
