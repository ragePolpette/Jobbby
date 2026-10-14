using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ApplicationLedger;

/// <summary>
/// The one definition of "the same job posting", used both inside a run (reposts under a new
/// link) and across runs (the ledger's dedupe). Case, punctuation and spacing are ignored,
/// and trailing legal-form suffixes from many countries are stripped from the company, so
/// "Acme S.r.l." and "ACME SRL", or "Foo GmbH & Co. KG" and "Foo", are one company. No
/// geographic or generic words are stripped ("Acme Italia" stays distinct from "Acme").
/// Every input must come from the source itself, never from an LLM: a model's guess must not
/// be able to merge a posting into another one or split it from its history.
/// </summary>
public static class PostingIdentity
{
    private const string Separator = "::";

    private static readonly Regex NonWord = new(@"[^\p{L}\p{N}#+]+", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Legal forms, already in normalized spelling (lower case, punctuation as spaces).</summary>
    private static readonly string[] LegalSuffixes =
    {
        "s r l s", "srls", "s r l", "srl", "s p a", "spa", "s a s", "sas", "s n c", "snc", "s a", "sa", "unipersonale",
        "gmbh", "ag", "kg", "co", "cie", "b v", "bv", "n v", "nv", "ltd", "plc", "llc", "inc", "corp",
        "oy", "ab", "a s", "as", "s l", "sl", "sp z o o", "kft",
    };

    /// <param name="company">The company as the source reported it; empty when the source has none.</param>
    /// <param name="applyUrl">
    /// Used only when the company is unknown: the title alone would merge unrelated employers'
    /// ads (and give them one PostingId), so such a posting is identified by its link as well.
    /// </param>
    /// <param name="sourceId">The source's own id of the ad; preferred over the link when the company is unknown.</param>
    public static string Key(string company, string title, IEnumerable<string>? extraCompanySuffixes = null, string? applyUrl = null, string? sourceId = null)
    {
        var normalizedCompany = NormalizeCompany(company, extraCompanySuffixes);
        var key = $"{normalizedCompany}{Separator}{NormalizeText(title)}";
        if (normalizedCompany.Length > 0)
            return key;
        if (!string.IsNullOrWhiteSpace(sourceId))
            return $"{key}{Separator}id:{sourceId.Trim()}";
        return string.IsNullOrWhiteSpace(applyUrl) ? key : $"{key}{Separator}url:{NormalizeUrl(applyUrl)}";
    }

    /// <summary>
    /// The link without fragment, with scheme and host in lower case and the trailing slash of
    /// the path removed. The query is kept: some sources identify the ad there.
    /// </summary>
    public static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return trimmed;

        var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : string.Empty;
        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{uri.Scheme}://{uri.Host.ToLowerInvariant()}{port}{path}{uri.Query}";
    }

    /// <summary>Opaque, URL-safe, stable id of a key: first 16 bytes of its SHA-256, in hex.</summary>
    public static string Id(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 16).ToLowerInvariant();

    public static string NormalizeCompany(string company, IEnumerable<string>? extraCompanySuffixes = null)
    {
        var normalized = NormalizeText(company);
        var suffixes = LegalSuffixes
            .Concat((extraCompanySuffixes ?? Array.Empty<string>()).Select(NormalizeText))
            .Where(suffix => suffix.Length > 0)
            .OrderByDescending(suffix => suffix.Length)
            .ToList();

        // Strip repeatedly ("gmbh co kg"), but never down to nothing: a name that is only a suffix stays.
        var stripped = true;
        while (stripped)
        {
            stripped = false;
            foreach (var suffix in suffixes)
            {
                if (normalized.Length > suffix.Length && normalized.EndsWith(" " + suffix, StringComparison.Ordinal))
                {
                    normalized = normalized[..^(suffix.Length + 1)].TrimEnd();
                    stripped = true;
                    break;
                }
            }
        }

        return normalized;
    }

    public static string NormalizeText(string value) =>
        Spaces.Replace(NonWord.Replace(value.ToLowerInvariant(), " "), " ").Trim();
}
