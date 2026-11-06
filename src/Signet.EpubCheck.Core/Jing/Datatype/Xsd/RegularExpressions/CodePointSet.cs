using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Signet.EpubCheck.Core.Jing.Datatype.Xsd.RegularExpressions;

/// <summary>
/// A set of Unicode code points, as sorted, disjoint, non-adjacent ranges. Jing's translator builds a tree of
/// character classes and prints it as a <c>java.util.regex</c> class, which matches code points; .NET's regular
/// expressions match UTF-16 units, so the port computes the set itself and prints it as a .NET pattern in which a
/// character outside the BMP is its surrogate pair (as Jing's own output for JDKs without surrogate support does).
/// </summary>
internal sealed class CodePointSet
{
    public const int MaxCodePoint = 0x10FFFF;

    private const int MinSupplementary = 0x10000;
    private const int MinSurrogate = 0xD800;
    private const int MaxSurrogate = 0xDFFF;
    private const char MinLowSurrogate = '\uDC00';
    private const char MaxLowSurrogate = '\uDFFF';
    private const string MatchesNothing = "(?!)";

    private readonly int[] _ranges;

    private CodePointSet(int[] ranges)
    {
        _ranges = ranges;
    }

    public static CodePointSet Empty { get; } = new([]);

    public static CodePointSet All { get; } = new([0, MaxCodePoint]);

    /// <summary>The single code point the set has, or -1.</summary>
    public int SingleCodePoint => _ranges.Length == 2 && _ranges[0] == _ranges[1] ? _ranges[0] : -1;

    public bool IsEmpty => _ranges.Length == 0;

    public static CodePointSet Of(int codePoint) => new([codePoint, codePoint]);

    public static CodePointSet Range(int min, int max) => min > max ? Empty : new([min, max]);

    /// <summary>A set from (min, max) pairs in any order, possibly overlapping.</summary>
    public static CodePointSet FromRanges(IEnumerable<(int Min, int Max)> ranges)
    {
        List<(int Min, int Max)> sorted = [.. ranges];
        sorted.Sort((a, b) => a.Min.CompareTo(b.Min));
        List<int> merged = [];
        foreach ((int min, int max) in sorted)
        {
            if (merged.Count > 0 && min <= merged[^1] + 1)
            {
                merged[^1] = Math.Max(merged[^1], max);
            }
            else
            {
                merged.Add(min);
                merged.Add(max);
            }
        }

        return new CodePointSet([.. merged]);
    }

    public static CodePointSet Union(IEnumerable<CodePointSet> sets)
    {
        List<(int, int)> all = [];
        foreach (CodePointSet set in sets)
        {
            all.AddRange(set.Pairs());
        }

        return FromRanges(all);
    }

    public CodePointSet Union(CodePointSet other) => Union([this, other]);

    public CodePointSet Complement()
    {
        List<(int, int)> result = [];
        int next = 0;
        foreach ((int min, int max) in Pairs())
        {
            if (min > next)
            {
                result.Add((next, min - 1));
            }

            next = max + 1;
        }

        if (next <= MaxCodePoint)
        {
            result.Add((next, MaxCodePoint));
        }

        return FromRanges(result);
    }

    public CodePointSet Subtract(CodePointSet other) => Union([Complement(), other]).Complement();

    public bool Contains(int codePoint)
    {
        for (int i = 0; i < _ranges.Length; i += 2)
        {
            if (codePoint >= _ranges[i] && codePoint <= _ranges[i + 1])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The set as a .NET pattern: a class for the BMP part (surrogate code points left out: the input is well-formed
    /// UTF-16), and a surrogate pair pattern per range outside the BMP; a group when there is more than one part.
    /// </summary>
    public string ToPattern()
    {
        List<string> parts = [];
        StringBuilder bmp = new();
        List<(int Min, int Max)> supplementary = [];
        foreach ((int min, int max) in Pairs())
        {
            AddBmp(bmp, min, Math.Min(max, MinSurrogate - 1));
            AddBmp(bmp, Math.Max(min, MaxSurrogate + 1), Math.Min(max, MinSupplementary - 1));
            if (max >= MinSupplementary)
            {
                supplementary.Add((Math.Max(min, MinSupplementary), max));
            }
        }

        if (bmp.Length > 0)
        {
            parts.Add("[" + bmp + "]");
        }

        foreach ((int min, int max) in supplementary)
        {
            AddSupplementary(parts, min, max);
        }

        return parts.Count switch
        {
            0 => MatchesNothing,
            1 => parts[0],
            _ => "(?:" + string.Join('|', parts) + ")",
        };
    }

    private static void AddBmp(StringBuilder bmp, int min, int max)
    {
        if (min > max)
        {
            return;
        }

        bmp.Append(Escape((char)min));
        if (max > min)
        {
            bmp.Append('-').Append(Escape((char)max));
        }
    }

    // A range outside the BMP as surrogate pairs: the partial first and last high surrogates, and the full ones between.
    private static void AddSupplementary(List<string> parts, int min, int max)
    {
        string minPair = char.ConvertFromUtf32(min);
        string maxPair = char.ConvertFromUtf32(max);
        char high1 = minPair[0];
        char low1 = minPair[1];
        char high2 = maxPair[0];
        char low2 = maxPair[1];
        if (high1 == high2)
        {
            parts.Add(Escape(high1) + LowClass(low1, low2));
            return;
        }

        if (low1 != MinLowSurrogate)
        {
            parts.Add(Escape(high1) + LowClass(low1, MaxLowSurrogate));
            high1++;
        }

        bool lastPartial = low2 != MaxLowSurrogate;
        if (lastPartial)
        {
            high2--;
        }

        if (high1 <= high2)
        {
            string highClass = high1 == high2 ? Escape(high1) : "[" + Escape(high1) + "-" + Escape(high2) + "]";
            parts.Add(highClass + LowClass(MinLowSurrogate, MaxLowSurrogate));
        }

        if (lastPartial)
        {
            parts.Add(Escape(maxPair[0]) + LowClass(MinLowSurrogate, low2));
        }
    }

    private static string LowClass(char min, char max) =>
        min == max ? Escape(min) : "[" + Escape(min) + "-" + Escape(max) + "]";

    private static string Escape(char c) => "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture);

    private IEnumerable<(int Min, int Max)> Pairs()
    {
        for (int i = 0; i < _ranges.Length; i += 2)
        {
            yield return (_ranges[i], _ranges[i + 1]);
        }
    }
}
