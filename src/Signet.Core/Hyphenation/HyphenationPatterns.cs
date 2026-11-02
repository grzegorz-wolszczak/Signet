using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Signet.Core.Hyphenation;

/// <summary>
/// The hyphenation patterns of one language, read from a libhyphen file (<c>hyph_pl_PL.dic</c>, as LibreOffice ships
/// them next to its spelling dictionaries), and the places where a word may be broken by them (Liang's algorithm, as
/// TeX and libhyphen do it).
/// </summary>
/// <remarks>
/// <para>The file: its first line names its encoding (<c>UTF-8</c>, <c>ISO8859-2</c>…); <c>%</c> starts a comment;
/// <c>LEFTHYPHENMIN</c> / <c>RIGHTHYPHENMIN</c> say how many letters stay before the first break and after the last
/// one, <c>COMPOUNDLEFTHYPHENMIN</c> / <c>COMPOUNDRIGHTHYPHENMIN</c> the same around the inside edges of the parts of a
/// compound; <c>NEXTLEVEL</c> separates the patterns of the compound boundaries (first level) from the patterns of the
/// parts (second level). Every other line is a pattern: letters with digits between them (an odd digit allows a break
/// there, an even one forbids it, the greater wins) and <c>.</c> for an edge of the word.</para>
/// <para>Not supported, deliberately: libhyphen's non-standard patterns (<c>pattern/replacement,start,cut</c> — a
/// break that changes letters, e.g. German "Schiffahrt" → "Schiff-fahrt"); a soft hyphen cannot change letters, so
/// those lines are skipped. <c>NOHYPHEN</c> is read and ignored: it names the characters (hyphen, apostrophe) next to
/// which libhyphen does not break, and Signet only ever hyphenates runs of letters.</para>
/// </remarks>
public sealed class HyphenationPatterns
{
    /// <summary>What libhyphen takes when the file does not say: two letters before the first break and after the last.</summary>
    public const int DefaultHyphenMin = 2;

    private static readonly Regex IsoCodePage = new("^ISO-?8859-?(\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly Level[] _levels;

    private HyphenationPatterns(
        Level[] levels, int leftMin, int rightMin, int compoundLeftMin, int compoundRightMin, int skippedPatterns)
    {
        _levels = levels;
        LeftHyphenMin = leftMin;
        RightHyphenMin = rightMin;
        CompoundLeftHyphenMin = compoundLeftMin;
        CompoundRightHyphenMin = compoundRightMin;
        SkippedPatterns = skippedPatterns;
    }

    /// <summary>The fewest letters before the first break of a word (<c>LEFTHYPHENMIN</c>).</summary>
    public int LeftHyphenMin { get; }

    /// <summary>The fewest letters after the last break of a word (<c>RIGHTHYPHENMIN</c>).</summary>
    public int RightHyphenMin { get; }

    /// <summary>The fewest letters of a part of a compound next to its inside edge (<c>COMPOUNDLEFTHYPHENMIN</c>).</summary>
    public int CompoundLeftHyphenMin { get; }

    /// <summary>The fewest letters of a part of a compound next to its inside edge (<c>COMPOUNDRIGHTHYPHENMIN</c>).</summary>
    public int CompoundRightHyphenMin { get; }

    /// <summary>The non-standard patterns the file has, which are not used (see the remarks of the class).</summary>
    public int SkippedPatterns { get; }

    /// <summary>Reads the patterns of a libhyphen file.</summary>
    /// <exception cref="InvalidDataException">The file holds no patterns.</exception>
    public static HyphenationPatterns Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Parse(File.ReadAllBytes(path));
    }

    /// <summary>Reads the patterns of a libhyphen file from its bytes, in the encoding its first line names.</summary>
    /// <exception cref="InvalidDataException">The file holds no patterns.</exception>
    public static HyphenationPatterns Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        int firstLineEnd = Array.IndexOf(data, (byte)'\n');
        string charset = Encoding.ASCII.GetString(data, 0, firstLineEnd < 0 ? data.Length : firstLineEnd).Trim();
        Encoding encoding = EncodingOf(charset);
        string text = encoding.GetString(data);
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        return Parse(new StringReader(text));
    }

    // Reads the lines after the first one (the encoding).
    private static HyphenationPatterns Parse(TextReader reader)
    {
        List<Dictionary<string, byte[]>> levels = new() { new Dictionary<string, byte[]>(StringComparer.Ordinal) };
        int leftMin = DefaultHyphenMin;
        int rightMin = DefaultHyphenMin;
        int compoundLeftMin = DefaultHyphenMin;
        int compoundRightMin = DefaultHyphenMin;
        int skipped = 0;

        _ = reader.ReadLine();
        while (reader.ReadLine() is { } raw)
        {
            string line = raw.Trim();
            int comment = line.IndexOf('%', StringComparison.Ordinal);
            if (comment >= 0)
            {
                line = line[..comment].Trim();
            }

            if (line.Length == 0)
            {
                continue;
            }

            string[] words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (words[0])
            {
                case "LEFTHYPHENMIN":
                    leftMin = Number(words, leftMin);
                    continue;
                case "RIGHTHYPHENMIN":
                    rightMin = Number(words, rightMin);
                    continue;
                case "COMPOUNDLEFTHYPHENMIN":
                    compoundLeftMin = Number(words, compoundLeftMin);
                    continue;
                case "COMPOUNDRIGHTHYPHENMIN":
                    compoundRightMin = Number(words, compoundRightMin);
                    continue;
                case "NOHYPHEN":
                    continue;
                case "NEXTLEVEL":
                    levels.Add(new Dictionary<string, byte[]>(StringComparer.Ordinal));
                    continue;
            }

            foreach (string pattern in words)
            {
                if (pattern.Contains('/', StringComparison.Ordinal))
                {
                    skipped++;
                    continue;
                }

                AddPattern(levels[^1], pattern);
            }
        }

        levels.RemoveAll(l => l.Count == 0);
        if (levels.Count == 0)
        {
            throw new InvalidDataException("The file holds no hyphenation patterns.");
        }

        Level[] built = new Level[levels.Count];
        for (int i = 0; i < built.Length; i++)
        {
            built[i] = new Level(levels[i]);
        }

        return new HyphenationPatterns(built, leftMin, rightMin, compoundLeftMin, compoundRightMin, skipped);
    }

    private static int Number(string[] words, int fallback) =>
        words.Length > 1 && int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0
            ? value
            : fallback;

    // A pattern such as ".ab1c" is kept as its letters ("." included) and the digits between them ([0,0,0,1,0]).
    private static void AddPattern(Dictionary<string, byte[]> level, string pattern)
    {
        StringBuilder letters = new(pattern.Length);
        List<byte> points = new(pattern.Length + 1) { 0 };
        foreach (char c in pattern)
        {
            if (c is >= '0' and <= '9')
            {
                points[^1] = (byte)(c - '0');
            }
            else
            {
                letters.Append(char.ToLowerInvariant(c));
                points.Add(0);
            }
        }

        if (letters.Length > 0)
        {
            level[letters.ToString()] = points.ToArray();
        }
    }

    // The encoding the first line names; libhyphen files use the names of the time ("ISO8859-2", "microsoft-cp1251").
    private static Encoding EncodingOf(string charset)
    {
        string name = charset;
        if (IsoCodePage.Match(charset) is { Success: true } iso)
        {
            name = "iso-8859-" + iso.Groups[1].Value;
        }
        else if (charset.StartsWith("microsoft-cp", StringComparison.OrdinalIgnoreCase))
        {
            name = "windows-" + charset["microsoft-cp".Length..];
        }

        return HtmlEncodingResolver.TryGetEncoding(name, out Encoding encoding) ? encoding : Utility.Utf8NoBom;
    }

    /// <summary>
    /// The places a word may be broken at: offsets into <paramref name="word"/> (a break at <c>i</c> goes between
    /// <c>word[i - 1]</c> and <c>word[i]</c>), in order, keeping <see cref="LeftHyphenMin"/> letters before the first
    /// and <see cref="RightHyphenMin"/> after the last.
    /// </summary>
    /// <param name="word">A run of letters (case does not matter).</param>
    public IReadOnlyList<int> BreakPoints(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        List<int> breaks = new();
        if (word.Length < LeftHyphenMin + RightHyphenMin)
        {
            return breaks;
        }

        string lower = Lower(word);
        if (_levels.Length == 1)
        {
            AddBreaks(_levels[0], lower, 0, lower.Length, LeftHyphenMin, RightHyphenMin, breaks);
            return breaks;
        }

        // Two levels: the first finds the parts of a compound, the second breaks each part; inside edges keep the
        // compound minimums, the edges of the word the word's.
        List<int> boundaries = new();
        AddBreaks(_levels[0], lower, 0, lower.Length, LeftHyphenMin, RightHyphenMin, boundaries);
        int start = 0;
        foreach (int boundary in boundaries.Append(lower.Length))
        {
            int left = start == 0 ? LeftHyphenMin : CompoundLeftHyphenMin;
            int right = boundary == lower.Length ? RightHyphenMin : CompoundRightHyphenMin;
            AddBreaks(_levels[^1], lower, start, boundary, left, right, breaks);
            if (boundary < lower.Length)
            {
                breaks.Add(boundary);
            }

            start = boundary;
        }

        breaks.Sort();
        return breaks;
    }

    // Lower case letter by letter, so that the offsets stay those of the word.
    private static string Lower(string word)
    {
        char[] chars = word.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    // Liang's algorithm over word[start..end): the odd points that keep the minimums are added as offsets into word.
    private static void AddBreaks(Level level, string word, int start, int end, int leftMin, int rightMin, List<int> breaks)
    {
        int length = end - start;
        if (length < leftMin + rightMin || length < 2)
        {
            return;
        }

        string dotted = string.Concat(".", word.AsSpan(start, length), ".");
        byte[] points = new byte[dotted.Length + 1];
        for (int i = 0; i < dotted.Length; i++)
        {
            int longest = Math.Min(level.MaxLength, dotted.Length - i);
            for (int len = 1; len <= longest; len++)
            {
                if (level.Patterns.TryGetValue(dotted.Substring(i, len), out byte[]? pattern))
                {
                    for (int p = 0; p < pattern.Length; p++)
                    {
                        if (pattern[p] > points[i + p])
                        {
                            points[i + p] = pattern[p];
                        }
                    }
                }
            }
        }

        // points[k] is the point before dotted[k]; the letter j of the part is dotted[j + 1], so a break before it is
        // points[j + 1].
        for (int j = Math.Max(leftMin, 1); j <= length - Math.Max(rightMin, 1); j++)
        {
            if (points[j + 1] % 2 == 1)
            {
                breaks.Add(start + j);
            }
        }
    }

    private sealed class Level
    {
        public Level(Dictionary<string, byte[]> patterns)
        {
            Patterns = patterns;
            foreach (string key in patterns.Keys)
            {
                MaxLength = Math.Max(MaxLength, key.Length);
            }
        }

        public Dictionary<string, byte[]> Patterns { get; }

        public int MaxLength { get; }
    }
}
