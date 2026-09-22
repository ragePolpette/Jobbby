namespace ApplicationLedger;

public static class ApplicationOutcomes
{
    public const string Discovered = "Discovered";
    public const string Rejected = "Rejected";
    public const string Shortlisted = "Shortlisted";
    public const string Approved = "Approved";
    public const string Applied = "Applied";

    public static bool IsTerminal(string outcome) => outcome is Rejected or Shortlisted or Approved or Applied;
}
