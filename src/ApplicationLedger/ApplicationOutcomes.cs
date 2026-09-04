namespace ApplicationLedger;

/// <summary>Valid values for <see cref="ApplicationRecord.Outcome"/>.</summary>
public static class ApplicationOutcomes
{
    public const string Applied = "Applied";
    public const string Rejected = "Rejected";
    public const string TimedOut = "TimedOut";
}
