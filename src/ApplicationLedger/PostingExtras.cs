namespace ApplicationLedger;

/// <summary>
/// The presentation message proposed for a posting. Never sent by Jobbby: the user copies it.
/// </summary>
/// <param name="Edited">The user changed the text by hand after it was generated.</param>
/// <param name="BasedOnFullText">Written from the full text the user pasted, not from the source's excerpt.</param>
/// <param name="Matches">Up to three points found both in the CV and in the posting; empty = generic message.</param>
public sealed record PresentationMessage(string Text, DateTimeOffset GeneratedAt, bool Edited, bool BasedOnFullText, List<string> Matches);

/// <summary>An evaluation as it stood at some point, kept when a later one replaces it.</summary>
public sealed record EvaluationSnapshot(string Outcome, string? Reason, double? Confidence, string? Category, string? Reasoning);

/// <summary>
/// The posting's full text, pasted by the user (untrusted, like the source's own text). The record's
/// evaluation fields then describe this text; <see cref="ExcerptEvaluation"/> keeps the one made on the excerpt.
/// </summary>
public sealed record UserFullText(string Text, DateTimeOffset ProvidedAt, EvaluationSnapshot ExcerptEvaluation)
{
    public const int MaxLength = 20_000;
}
