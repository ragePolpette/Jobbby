namespace ApplicationLedger;

/// <summary>
/// The ledger's dedup key; kept as an entry point for existing callers, the rules live in
/// <see cref="PostingIdentity"/>.
/// </summary>
public static class DedupeKey
{
    public static string Normalize(string company, string title) => PostingIdentity.Key(company, title);
}
