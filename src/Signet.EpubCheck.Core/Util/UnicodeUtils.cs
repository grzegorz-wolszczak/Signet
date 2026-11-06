using System;
using System.Collections.Frozen;
using System.Text;

namespace Signet.EpubCheck.Core.Util;

/// <summary>Unicode normalization and case folding (upstream's <c>UnicodeUtils</c>, which uses ICU4J).</summary>
public static class UnicodeUtils
{
    /// <summary>
    /// The canonical case fold of <paramref name="value"/>: its canonical decomposition (NFD), then full case folding
    /// (ICU4J's <c>CaseMap.fold()</c>, statuses C and F of <c>CaseFolding.txt</c>). Two OCF paths that fold to the same
    /// string name the same resource.
    /// </summary>
    public static string CanonicalCaseFold(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return CaseFolding.Fold(value.Normalize(NormalizationForm.FormD));
    }

    /// <summary>The Unicode canonical composition (NFC) of <paramref name="value"/>.</summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Normalize(NormalizationForm.FormC);
    }
}

/// <summary>Full case folding, from the table generated out of the Unicode Character Database.</summary>
internal static partial class CaseFolding
{
    private static readonly FrozenDictionary<int, string> s_folds = s_table.ToFrozenDictionary(entry => entry.CodePoint, entry => entry.Folded);

    /// <summary>The full case folding of <paramref name="value"/>.</summary>
    public static string Fold(string value)
    {
        StringBuilder result = new(value.Length);
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (s_folds.TryGetValue(rune.Value, out string? folded))
            {
                result.Append(folded);
            }
            else
            {
                result.Append(rune.ToString());
            }
        }

        return result.ToString();
    }
}
