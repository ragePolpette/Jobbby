namespace JobPostings;

/// <summary>
/// Normalized job posting, ready for matching/approval - the shape every raw source
/// format gets mapped into. <see cref="ApplyChannel"/> is only data at this point (see
/// <see cref="ApplyChannels"/>); no submission handler exists yet for any of the values.
/// </summary>
public sealed record JobPosting(
    string Title,
    string Company,
    string SeniorityLevel,
    List<string> RequiredStack,
    string Description,
    string SourceUrl,
    string ApplyUrl,
    string ApplyChannel);

/// <summary>Valid values for <see cref="JobPosting.ApplyChannel"/>.</summary>
public static class ApplyChannels
{
    public const string Email = "email";
    public const string NativeForm = "native_form";
    public const string ExternalPlatform = "external_platform";
}
