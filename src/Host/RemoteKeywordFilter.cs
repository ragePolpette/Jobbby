using System.Globalization;
using System.Text;
using JobPostings;

namespace Host;

/// <summary>
/// Cheap prefilter for the remote sweep: a result goes on to (paid) LLM normalization only if
/// its title or excerpt mentions one of the configured keywords. Case- and accent-insensitive,
/// substring match. It only saves calls: survivors are still kept only if normalization says
/// the job is remote.
/// </summary>
public sealed class RemoteKeywordFilter
{
    private readonly IReadOnlyList<string> _keywords;

    public RemoteKeywordFilter(IEnumerable<string> keywords)
    {
        _keywords = keywords.Select(Fold).Where(keyword => keyword.Length > 0).Distinct().ToList();
    }

    public bool IsEmpty => _keywords.Count == 0;

    public bool Matches(RawPosting posting)
    {
        var text = Fold(posting.RawTitle + " " + posting.RawDescription);
        return _keywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal));
    }

    private static string Fold(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                folded.Append(character);
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }
}
