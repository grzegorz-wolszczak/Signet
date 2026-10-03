using System;
using System.Collections.Generic;
using System.Globalization;

namespace Signet.Core.Semantics;

/// <summary>
/// Unicode code point names (for the "Insert Special Character" dialog).
/// </summary>
/// <remarks>
/// The names come from a bundled precomputed table (<c>EmbeddedData/codepoint-names.tsv.gz</c>)
/// generated from the Unicode Character Database.
/// </remarks>
public static class CodepointNames
{
    // Names of the control characters U+0000..U+001F.
    private static readonly string[] ControlNames =
    {
        "NULL", "START OF HEADING", "START OF TEXT", "END OF TEXT", "END OF TRANSMISSION",
        "ENQUIRY", "ACKNOWLEDGE", "BELL", "BACKSPACE", "TAB", "NEW LINE", "VERTICAL TAB",
        "FORM FEED", "CARRIAGE RETURN", "SHIFT OUT", "SHIFT IN", "DATA LINK ESCAPE",
        "DEVICE CONTROL ONE", "DEVICE CONTROL TWO", "DEVICE CONTROL THREE", "DEVICE CONTROL FOUR",
        "NEGATIVE ACKNOWLEDGE", "SYNCHRONOUS IDLE", "END OF TRANSMISSION BLOCK", "CANCEL",
        "END OF MEDIUM", "SUBSTITUTE", "ESCAPE", "FILE SEPARATOR (FS)", "GROUP SEPARATOR (GS)",
        "RECORD SEPARATOR (RS)", "UNIT SEPARATOR (US)",
    };

    private static readonly Lazy<Dictionary<int, string>> NameCache = new(LoadNames);

    /// <summary>
    /// The name of a code point. <c>-1</c> &#8594; <c>"EOF"</c>; negative values &#8594; an empty string;
    /// U+0000..U+001F &#8594; the control character name; an unassigned point &#8594; <c>"Unknown"</c>.
    /// </summary>
    public static string GetName(int cp)
    {
        if (cp == -1)
        {
            return "EOF";
        }

        if (cp < 0)
        {
            return string.Empty;
        }

        if (cp <= 0x1F)
        {
            return ControlNames[cp];
        }

        return NameCache.Value.GetValueOrDefault(cp, "Unknown");
    }

    /// <summary>
    /// Finds code points whose Unicode name contains all the words from <paramref name="query"/>
    /// (separated by whitespace, case-insensitive). It also matches the <c>U+XXXX</c> / hexadecimal
    /// notation. The result is sorted ascending by code point, truncated to
    /// <paramref name="maxResults"/>. Used by the "Insert Special Character" dialog.
    /// </summary>
    public static IReadOnlyList<(int Codepoint, string Name)> Search(string query, int maxResults = 200)
    {
        var results = new List<(int, string)>();
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
        {
            return results;
        }

        string[] terms = query.Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        int? exact = null;
        string trimmed = query.Trim();
        if (trimmed.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        if (int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp) && cp is >= 0 and <= 0x10FFFF)
        {
            exact = cp;
        }

        foreach (KeyValuePair<int, string> pair in NameCache.Value)
        {
            bool match = terms.Length > 0;
            foreach (string term in terms)
            {
                if (!pair.Value.Contains(term, StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match || pair.Key == exact)
            {
                results.Add((pair.Key, pair.Value));
            }
        }

        results.Sort(static (a, b) => a.Item1.CompareTo(b.Item1));
        if (results.Count > maxResults)
        {
            results.RemoveRange(maxResults, results.Count - maxResults);
        }

        return results;
    }

    private static Dictionary<int, string> LoadNames()
    {
        Dictionary<int, string> map = new();
        foreach (string[] fields in ReferenceData.LoadTsvGz("ReferenceData.codepoint-names", 2))
        {
            map[int.Parse(fields[0], CultureInfo.InvariantCulture)] = fields[1];
        }

        return map;
    }
}
