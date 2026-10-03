using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using PCRE;

namespace Signet.Core.Search;

/// <summary>
/// The Signet regular expression object (a wrapper over the PCRE2 library).
/// Used to find matches in text and to build replacement texts.
/// </summary>
/// <remarks>
/// <para>
/// The pattern is always compiled with <c>PCRE2_UTF | PCRE2_MULTILINE</c>
/// (<see cref="PcreOptions.Utf"/> | <see cref="PcreOptions.MultiLine"/>).
/// The remaining options (case-insensitivity, dot-all, UCP, "plain text" mode) are added by the
/// Find &amp; Replace layer as inline prefixes in the pattern itself (<c>(?i)</c>, <c>(?s)</c>
/// etc.).
/// </para>
/// <para>
/// Offsets in <see cref="MatchInfo"/> are in UTF-16 code units, matching
/// <see cref="string"/> indexing, so no UTF-16 -&gt; UTF-32 conversion is needed.
/// </para>
/// </remarks>
public sealed class Spcre
{
    /// <summary>The maximum number of capture groups taken into account.</summary>
    public const int MaxCaptureGroups = 30;

    private const PcreOptions CompileOptions = PcreOptions.Utf | PcreOptions.MultiLine;

    // PCRE2_NOTEMPTY — empty matches are not counted as hits.
    private const PcreMatchOptions MatchOptions = PcreMatchOptions.NotEmpty;

    private static readonly Regex ErrorOffsetRegex =
        new(@"\s+at offset (\d+)\.?\s*$", RegexOptions.CultureInvariant);

    private readonly PcreRegex? _regex;

    /// <summary>
    /// Compiles the pattern. Does not throw — an invalid pattern leaves the object in the
    /// <see cref="IsValid"/> == <c>false</c> state with <see cref="Error"/> / <see cref="ErrorPosition"/> set.
    /// </summary>
    /// <param name="pattern">The search pattern.</param>
    public Spcre(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        Pattern = pattern;
        Error = string.Empty;
        ErrorPosition = -1;

        try
        {
            _regex = new PcreRegex(pattern, CompileOptions);
            IsValid = true;
            // pcre2_get_ovector_count == number of groups + 1 (for the whole match).
            CaptureSubpatternCount = _regex.PatternInfo.CaptureCount + 1;
        }
        catch (PcrePatternException ex)
        {
            IsValid = false;
            (Error, ErrorPosition) = ParseCompileError(ex.Message);
        }
    }

    /// <summary>Whether the pattern is valid.</summary>
    public bool IsValid { get; }

    /// <summary>The error message (empty when <see cref="IsValid"/>).</summary>
    public string Error { get; }

    /// <summary>The error position in the pattern (code units), or <c>-1</c>.</summary>
    public int ErrorPosition { get; }

    /// <summary>The string the expression was created from (the constructor argument).</summary>
    public string Pattern { get; }

    /// <summary>
    /// The total number of capturing subpatterns + 1 (the ovector size). <c>0</c> for
    /// an invalid pattern.
    /// </summary>
    public int CaptureSubpatternCount { get; }

    /// <summary>
    /// Converts the capture group number corresponding to a named group into its
    /// absolute number. <c>-1</c> if the named group does not exist.
    /// </summary>
    public int GetCaptureStringNumber(string name)
    {
        if (!IsValid || _regex is null || string.IsNullOrEmpty(name))
        {
            return -1;
        }

        IReadOnlyList<int>? indexes = _regex.PatternInfo.GetGroupIndexesByName(name);
        if (indexes is null || indexes.Count == 0)
        {
            return -1;
        }

        int number = indexes[0];
        return number == 0 ? -1 : number;
    }

    /// <summary>
    /// Returns information about all matches of the pattern in the given text.
    /// </summary>
    public IReadOnlyList<MatchInfo> GetEveryMatchInfo(string text)
    {
        var info = new List<MatchInfo>();
        if (!IsValid || _regex is null || string.IsNullOrEmpty(text))
        {
            return info;
        }

        int ovectorCount = Math.Min(CaptureSubpatternCount, MaxCaptureGroups);
        int position = 0;

        while (position <= text.Length)
        {
            PcreMatch match = _regex.Match(text, position, MatchOptions);
            if (!match.Success || match.Index >= match.EndIndex)
            {
                break;
            }

            info.Add(GenerateMatchInfo(match, ovectorCount));
            position = match.EndIndex;
        }

        return info;
    }

    /// <summary>Returns information about the first match of the pattern in the text.</summary>
    public MatchInfo GetFirstMatchInfo(string text) => GetFirstMatchInfo(text, 0);

    /// <summary>
    /// Returns information about the first match of the pattern in the text, starting at
    /// offset <paramref name="offset"/>.
    /// </summary>
    public MatchInfo GetFirstMatchInfo(string text, int offset)
    {
        if (!IsValid || _regex is null || string.IsNullOrEmpty(text))
        {
            return MatchInfo.None;
        }

        if (offset < 0)
        {
            offset = 0;
        }

        if (offset > text.Length)
        {
            return MatchInfo.None;
        }

        PcreMatch match = _regex.Match(text, offset, MatchOptions);
        if (!match.Success || match.Index >= match.EndIndex)
        {
            return MatchInfo.None;
        }

        return GenerateMatchInfo(match, Math.Min(CaptureSubpatternCount, MaxCaptureGroups));
    }

    /// <summary>Returns information about the last match of the pattern in the text.</summary>
    public MatchInfo GetLastMatchInfo(string text)
    {
        IReadOnlyList<MatchInfo> info = GetEveryMatchInfo(text);
        return info.Count > 0 ? info[info.Count - 1] : MatchInfo.None;
    }

    /// <summary>
    /// Builds the replacement text from <paramref name="replacementPattern"/> based on the matched
    /// text <paramref name="text"/> and the capture group offsets (relative to the start of
    /// the match — exactly as in <see cref="MatchInfo.CaptureGroupsOffsets"/>).
    /// </summary>
    /// <remarks>
    /// Function-based replacement (<c>\F&lt;name&gt;</c>) is not supported — such a pattern is treated
    /// literally by <see cref="ReplaceTextBuilder"/>.
    /// </remarks>
    public bool ReplaceText(
        string text,
        IReadOnlyList<SpcreCapture> captureGroupsOffsets,
        string replacementPattern,
        out string result)
    {
        var builder = new ReplaceTextBuilder();
        return builder.BuildReplacementText(this, text, captureGroupsOffsets, replacementPattern, out result);
    }

    private static MatchInfo GenerateMatchInfo(PcreMatch match, int capturePatternCount)
    {
        int matchStart = match.Index;
        int matchEnd = match.EndIndex;

        var groups = new List<SpcreCapture>(capturePatternCount)
        {
            // The first entry is always the whole match, normalized to zero.
            new SpcreCapture(0, matchEnd - matchStart),
        };

        for (int i = 1; i < capturePatternCount; i++)
        {
            PcreGroup group = match[i];
            if (group.Success)
            {
                groups.Add(new SpcreCapture(group.Index - matchStart, group.EndIndex - matchStart));
            }
            else
            {
                // An unset group — yields an empty substring on replacement.
                groups.Add(new SpcreCapture(-1, -1));
            }
        }

        return new MatchInfo(new SpcreCapture(matchStart, matchEnd), groups);
    }

    private static (string Error, int Position) ParseCompileError(string message)
    {
        string text = message ?? string.Empty;
        int position = -1;

        Match offsetMatch = ErrorOffsetRegex.Match(text);
        if (offsetMatch.Success)
        {
            position = int.Parse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            text = text.Substring(0, offsetMatch.Index);
        }

        // PCRE.NET formats the message as: Invalid pattern '<pattern>': <error> at offset N.
        const string prefixHead = "Invalid pattern '";
        if (text.StartsWith(prefixHead, StringComparison.Ordinal))
        {
            int closing = text.IndexOf("': ", prefixHead.Length, StringComparison.Ordinal);
            if (closing >= 0)
            {
                text = text.Substring(closing + 3);
            }
        }

        text = text.Trim();
        return (PcreErrors.Instance.GetError(text, text), position);
    }

    /// <summary>
    /// The match offset in the text and the capture group offsets relative to
    /// the start of the matched substring.
    /// </summary>
    public sealed class MatchInfo
    {
        /// <summary>The "no match" instance (<c>Offset == (-1, -1)</c>).</summary>
        public static readonly MatchInfo None =
            new(new SpcreCapture(-1, -1), Array.Empty<SpcreCapture>());

        /// <summary>Creates a new match description.</summary>
        public MatchInfo(SpcreCapture offset, IReadOnlyList<SpcreCapture> captureGroupsOffsets)
        {
            Offset = offset;
            CaptureGroupsOffsets = captureGroupsOffsets;
        }

        /// <summary>The <c>[Start, End)</c> offset of the match in the whole text.</summary>
        public SpcreCapture Offset { get; }

        /// <summary>
        /// The capture group offsets, normalized so that <c>0</c> is the start of
        /// the matched substring. Entry <c>[0]</c> is always the whole match.
        /// Unset groups have the offset <c>(-1, -1)</c>.
        /// </summary>
        public IReadOnlyList<SpcreCapture> CaptureGroupsOffsets { get; }

        /// <summary>Whether a match was found.</summary>
        public bool Success => Offset.Start >= 0;
    }
}
