namespace ApplicationLedger;

public static class ApplicationOutcomes
{
    /// <summary>Legacy: a Telegram approval that timed out. Still read, no longer produced.</summary>
    public const string Discovered = "Discovered";
    public const string Rejected = "Rejected";
    public const string Shortlisted = "Shortlisted";
    public const string Approved = "Approved";
    public const string Applied = "Applied";

    /// <summary>Waiting for the user's decision; no deadline.</summary>
    public const string Pending = "Pending";

    /// <summary>Discarded by stage one or a Weak judgment, as opposed to a human rejection.</summary>
    public const string AutoRejected = "AutoRejected";

    /// <summary>Every outcome that keeps a posting from being proposed again as new.</summary>
    public static bool IsTerminal(string outcome) =>
        outcome is Rejected or Shortlisted or Approved or Applied or Pending or AutoRejected;
}
