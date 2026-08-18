namespace GraphEngine;

/// <summary>
/// Provider-agnostic seam for nodes that need to call an LLM. The engine has no
/// dependency on any specific provider; nodes take an <see cref="ILlmClient"/> via
/// constructor injection and the caller decides which implementation to wire up.
/// </summary>
public interface ILlmClient
{
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default);
}
