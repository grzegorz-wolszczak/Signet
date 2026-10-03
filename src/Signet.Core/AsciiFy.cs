using System;
using System.Text;

namespace Signet.Core;

/// <summary>
/// Transliterates Unicode text to a "close" plain-ASCII approximation (the data comes from
/// KBibTeX / the <c>unidecode</c> project).
/// </summary>
/// <remarks>
/// Used among others when generating XML identifiers from file names (<see cref="Resources.XmlResource.GetValidId"/>).
/// The table is deliberately simpler than full <c>unidecode</c> (e.g. <c>я</c> -&gt; <c>a</c>, <c>ж</c> -&gt; <c>z</c>).
/// </remarks>
public static class AsciiFy
{
    private const char EnDash = '–';
    private const char EmDash = '—';

    /// <summary>
    /// Converts text to an ASCII approximation:
    /// <c>–</c>&#160;&#8594;&#160;<c>--</c>, <c>—</c>&#160;&#8594;&#160;<c>---</c>, space&#160;&#8594;&#160;<c>_</c>,
    /// and every other character through the transliteration table (a character without a mapping
    /// is dropped). Surrogate pairs (characters outside the BMP) are copied unchanged.
    /// </summary>
    public static string ConvertToPlainAscii(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        string prepared = input
            .Replace(EnDash.ToString(), "--", StringComparison.Ordinal)
            .Replace(EmDash.ToString(), "---", StringComparison.Ordinal)
            .Replace(' ', '_');

        uint[] pos = AsciiFyData.Pos;
        string text = AsciiFyData.Text;
        StringBuilder result = new(prepared.Length + 16);

        foreach (char c in prepared)
        {
            if (char.IsSurrogate(c))
            {
                result.Append(c);
                continue;
            }

            int codePoint = c;
            int index = codePoint < 0xD800 ? codePoint : codePoint - 2048;
            uint packed = pos[index];
            int start = (int)(packed >> 5);
            int length = (int)(packed & 31);
            if (length > 0)
            {
                result.Append(text, start, length);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Whether the text, after NFC normalization, contains only ASCII characters (&lt;= 0x7F).
    /// </summary>
    public static bool ContainsOnlyAscii(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (char c in text.Normalize(NormalizationForm.FormC))
        {
            if (c > 0x7F)
            {
                return false;
            }
        }

        return true;
    }
}
