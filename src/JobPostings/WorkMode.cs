namespace JobPostings;

/// <summary>Where the work happens, as stated by the posting. <see cref="Hybrid"/> counts as on-site for location checks.</summary>
public enum WorkMode
{
    Unknown,
    Onsite,
    Hybrid,
    Remote,
}
