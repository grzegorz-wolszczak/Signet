using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Localization;

namespace Signet.Core.Semantics;

/// <summary>
/// The mapping between language codes (ISO 639-1/639-2 + regional variants <c>xx-YY</c>) &#8596;
/// full English language names. Data: <c>EmbeddedData/languages.tsv</c>.
/// </summary>
/// <remarks>
/// Language names are localized through the catalog; the full list is always exposed.
/// </remarks>
public static class Language
{
    private static readonly IReadOnlyList<string[]> Data =
        ReferenceData.LoadTsv("ReferenceData.languages", 2);

    private static readonly Dictionary<string, string> CodeToName =
        BuildLastWins(Data, keyIndex: 0, valueIndex: 1);

    private static readonly Dictionary<string, string> NameToCode =
        BuildLastWins(Data, keyIndex: 1, valueIndex: 0);

    private static Dictionary<string, string> BuildLastWins(IReadOnlyList<string[]> data, int keyIndex, int valueIndex)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (string[] row in data)
        {
            map[row[keyIndex]] = row[valueIndex];
        }

        return map;
    }

    private const string Catalog = "Lang";

    /// <summary>
    /// The language name (in the UI language) for a code; <paramref name="ow"/> if the code is unknown.
    /// </summary>
    public static string GetLanguageName(string languageCode, string ow = "")
    {
        ArgumentNullException.ThrowIfNull(languageCode);
        ArgumentNullException.ThrowIfNull(ow);
        return CodeToName.TryGetValue(languageCode, out string? english)
            ? CatalogText.Name(Catalog, languageCode, english)
            : ow;
    }

    /// <summary>
    /// The language code for a name (English or translated); <paramref name="ow"/> if unknown.
    /// </summary>
    public static string GetLanguageCode(string languageName, string ow = "")
    {
        ArgumentNullException.ThrowIfNull(languageName);
        ArgumentNullException.ThrowIfNull(ow);
        if (NameToCode.TryGetValue(languageName, out string? code))
        {
            return code;
        }

        foreach (string candidate in CodeToName.Keys)
        {
            if (string.Equals(GetLanguageName(candidate), languageName, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return ow;
    }

    /// <summary>The language names sorted, in the UI language.</summary>
    public static IReadOnlyList<string> GetSortedPrimaryLanguageNames() =>
        CodeToName.Keys.Select(c => GetLanguageName(c)).OrderBy(n => n, StringComparer.CurrentCulture).ToList();

    /// <summary>A code -&gt; language name map (in the UI language), without a description.</summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> GetLangMap() =>
        CodeToName.Keys.ToDictionary(c => c, c => new DescriptiveInfo(GetLanguageName(c), string.Empty), StringComparer.Ordinal);
}
