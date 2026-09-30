namespace Config;

/// <summary>Countries served by the Adzuna jobs API (lower-case codes as used in its URLs) and their currency.</summary>
public static class AdzunaCountries
{
    public static IReadOnlyDictionary<string, string> CurrencyByCode { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["at"] = "EUR",
        ["au"] = "AUD",
        ["be"] = "EUR",
        ["br"] = "BRL",
        ["ca"] = "CAD",
        ["ch"] = "CHF",
        ["de"] = "EUR",
        ["es"] = "EUR",
        ["fr"] = "EUR",
        ["gb"] = "GBP",
        ["in"] = "INR",
        ["it"] = "EUR",
        ["mx"] = "MXN",
        ["nl"] = "EUR",
        ["nz"] = "NZD",
        ["pl"] = "PLN",
        ["sg"] = "SGD",
        ["us"] = "USD",
        ["za"] = "ZAR",
    };

    public static bool IsSupported(string? code) => code is not null && CurrencyByCode.ContainsKey(code);
}
