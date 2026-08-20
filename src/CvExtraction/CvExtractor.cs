using GraphEngine;

namespace CvExtraction;

/// <summary>
/// Prompts an <see cref="ILlmClient"/> to turn raw CV text into the CV JSON schema. The
/// LLM provider is injected, so this works unchanged whether it's wired to
/// <see cref="MockLlmClient"/> today or a real provider later.
/// </summary>
public sealed class CvExtractor
{
    private readonly ILlmClient _llmClient;

    public CvExtractor(ILlmClient llmClient)
    {
        _llmClient = llmClient;
    }

    public Task<string> ExtractCvJsonAsync(string rawText, CancellationToken cancellationToken = default)
    {
        var prompt = CvExtractionPrompt.Build(rawText);
        return _llmClient.CompleteAsync(prompt, cancellationToken);
    }
}
