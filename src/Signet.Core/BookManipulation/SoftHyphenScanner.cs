using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Signet.Core.BookManipulation;

/// <summary>Where a soft hyphen that is not in the text of a language stands.</summary>
public enum SoftHyphenPlaceKind
{
    /// <summary>In the value of an attribute (<c>alt</c>, <c>title</c>, <c>href</c>, <c>id</c>…).</summary>
    Attribute,

    /// <summary>In <c>&lt;head&gt;</c> (or anywhere else outside <c>&lt;body&gt;</c>).</summary>
    Head,

    /// <summary>In an element whose text is not prose (<c>pre</c>, <c>code</c>, <c>script</c>, <c>style</c>…).</summary>
    Element,

    /// <summary>In a comment.</summary>
    Comment,
}

/// <summary>Where the language of a piece of text comes from.</summary>
public enum LanguageSourceKind
{
    /// <summary>A <c>lang</c> / <c>xml:lang</c> attribute of an element around the text.</summary>
    Element,

    /// <summary>The first <c>dc:language</c> of the OPF (no element around the text states one).</summary>
    Opf,

    /// <summary>Nothing: no element states it and the OPF has no <c>dc:language</c> — the language is unknown.</summary>
    None,
}

/// <summary>Where the language of a piece of text comes from: the kind, and for an element, its name.</summary>
/// <param name="Kind">The kind of the source.</param>
/// <param name="ElementName">The element that states the language (<c>html</c>, <c>body</c>, <c>span</c>…); empty otherwise.</param>
public readonly record struct LanguageSource(LanguageSourceKind Kind, string ElementName)
{
    /// <summary>The OPF's <c>dc:language</c>.</summary>
    public static LanguageSource Opf { get; } = new(LanguageSourceKind.Opf, string.Empty);

    /// <summary>No source: the language is unknown.</summary>
    public static LanguageSource None { get; } = new(LanguageSourceKind.None, string.Empty);
}

/// <summary>
/// A piece of an XHTML file as the soft hyphen operations see it: text in a language (where a soft hyphen may be added
/// and is counted under that language), or a place outside the text of any language (an attribute value, the head, a
/// <c>pre</c>, a comment…), where soft hyphens are never added and are listed one by one.
/// </summary>
internal readonly record struct SoftHyphenSegment(
    int Start, int End, bool IsText, string Language, LanguageSource Source, SoftHyphenPlaceKind Kind, string Name);

/// <summary>A run of letters of a text segment (soft hyphens inside it included).</summary>
internal readonly record struct SoftHyphenWord(int Start, int End, int Letters, bool HasSoftHyphen);

/// <summary>
/// Splits the source of an XHTML file into <see cref="SoftHyphenSegment"/>s, on the raw text (like
/// <see cref="TagLister"/>, not a parsed DOM), so the operations change nothing but the soft hyphens. The language of
/// text is the one of the nearest element around it with <c>lang</c> (or <c>xml:lang</c>, as spellcheck reads them),
/// otherwise the language the file starts with (the OPF's).
/// </summary>
internal static class SoftHyphenScanner
{
    /// <summary>Elements whose text is not prose: no soft hyphen is added in them.</summary>
    internal static readonly IReadOnlySet<string> NotProse = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "pre", "code", "script", "style", "textarea", "title",
    };

    /// <summary>Every form of a soft hyphen: the named, decimal and hexadecimal reference, and the character.</summary>
    internal static readonly Regex SoftHyphen = new(
        "&shy;|&#0*173;|&#[xX]0*[aA][dD];|\u00AD",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SoftHyphenAtPosition = new(
        "\\G(?:&shy;|&#0*173;|&#[xX]0*[aA][dD];|\u00AD)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const string HeadName = "head";

    /// <summary>The segments of <paramref name="html"/>, in order (attribute values come before the text after their tag).</summary>
    /// <param name="html">The source of the file.</param>
    /// <param name="defaultLanguage">The language of text no element states one for (the OPF's); empty = unknown.</param>
    public static List<SoftHyphenSegment> Scan(string html, string defaultLanguage)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(defaultLanguage);

        List<SoftHyphenSegment> segments = new();
        TagLister lister = TagLister.For(html);
        List<Frame> frames = new()
        {
            new Frame(string.Empty, defaultLanguage, defaultLanguage.Length > 0 ? LanguageSource.Opf : LanguageSource.None),
        };

        int textStart = 0;
        for (int i = 0; i < lister.Count; i++)
        {
            TagLister.TagInfo tag = lister.At(i);
            if (tag.Pos < 0)
            {
                continue;
            }

            AddText(lister, frames, textStart, tag.Pos, segments);
            textStart = tag.Pos + tag.Len;
            switch (tag.Kind)
            {
                case TagKind.Begin:
                    AddAttributes(html, tag, segments);
                    frames.Add(FrameOf(html, tag, frames[^1]));
                    break;
                case TagKind.SelfClosing:
                    AddAttributes(html, tag, segments);
                    break;
                case TagKind.End:
                    Close(frames, tag.TagName);
                    break;
                case TagKind.Comment:
                    segments.Add(Outside(tag.Pos, tag.Pos + tag.Len, SoftHyphenPlaceKind.Comment, string.Empty));
                    break;
                case TagKind.CData:
                    segments.Add(Outside(tag.Pos, tag.Pos + tag.Len, SoftHyphenPlaceKind.Element, "CDATA"));
                    break;
            }
        }

        AddText(lister, frames, textStart, html.Length, segments);
        return segments;
    }

    /// <summary>The runs of letters of a text segment (an entity reference other than a soft hyphen ends a word).</summary>
    public static IEnumerable<SoftHyphenWord> Words(string html, SoftHyphenSegment segment)
    {
        int start = -1;
        int letters = 0;
        bool hasSoftHyphen = false;
        int i = segment.Start;
        while (i <= segment.End)
        {
            if (i < segment.End)
            {
                int softHyphen = SoftHyphenLengthAt(html, i);
                if (softHyphen > 0)
                {
                    if (start < 0)
                    {
                        start = i;
                    }

                    hasSoftHyphen = true;
                    i += softHyphen;
                    continue;
                }

                if (IsLetter(html[i]))
                {
                    if (start < 0)
                    {
                        start = i;
                    }

                    letters++;
                    i++;
                    continue;
                }
            }

            if (start >= 0 && letters > 0)
            {
                yield return new SoftHyphenWord(start, i, letters, hasSoftHyphen);
            }

            start = -1;
            letters = 0;
            hasSoftHyphen = false;
            if (i < segment.End && html[i] == '&' && html.IndexOf(';', i, Math.Min(32, segment.End - i)) is var end and > 0)
            {
                i = end + 1;
                continue;
            }

            i++;
        }
    }

    /// <summary>The soft hyphens (any form) between <paramref name="start"/> and <paramref name="end"/>.</summary>
    public static IEnumerable<(int Offset, int Length)> SoftHyphens(string html, int start, int end)
    {
        for (Match match = SoftHyphen.Match(html, start, end - start); match.Success; match = match.NextMatch())
        {
            yield return (match.Index, match.Length);
        }
    }

    private static int SoftHyphenLengthAt(string html, int index)
    {
        if (html[index] is not ('&' or '\u00AD'))
        {
            return 0;
        }

        Match match = SoftHyphenAtPosition.Match(html, index);
        return match.Success ? match.Length : 0;
    }

    // A letter, or a mark that belongs to the letter before it (a combining accent does not end a word).
    private static bool IsLetter(char c) =>
        char.IsLetter(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    private static void AddText(TagLister lister, List<Frame> frames, int start, int end, List<SoftHyphenSegment> segments)
    {
        if (end <= start)
        {
            return;
        }

        if (!lister.IsPositionInBody(start) || frames.Exists(f => string.Equals(f.Name, HeadName, StringComparison.OrdinalIgnoreCase)))
        {
            segments.Add(Outside(start, end, SoftHyphenPlaceKind.Head, HeadName));
            return;
        }

        for (int i = frames.Count - 1; i > 0; i--)
        {
            if (NotProse.Contains(frames[i].Name))
            {
                segments.Add(Outside(start, end, SoftHyphenPlaceKind.Element, frames[i].Name.ToLowerInvariant()));
                return;
            }
        }

        Frame frame = frames[^1];
        segments.Add(new SoftHyphenSegment(start, end, true, frame.Language, frame.Source, default, string.Empty));
    }

    private static SoftHyphenSegment Outside(int start, int end, SoftHyphenPlaceKind kind, string name) =>
        new(start, end, false, string.Empty, LanguageSource.None, kind, name);

    // The language inside an element: its own lang (or xml:lang), or the one around it.
    private static Frame FrameOf(string html, TagLister.TagInfo tag, Frame around)
    {
        string? language = null;
        foreach ((string name, int valueStart, int valueLength) in Attributes(html, tag))
        {
            if (string.Equals(name, "lang", StringComparison.Ordinal) && valueLength > 0)
            {
                language = html.Substring(valueStart, valueLength).Trim();
                break;
            }

            if (string.Equals(name, "xml:lang", StringComparison.Ordinal) && valueLength > 0)
            {
                language ??= html.Substring(valueStart, valueLength).Trim();
            }
        }

        return string.IsNullOrEmpty(language)
            ? around with { Name = tag.TagName }
            : new Frame(tag.TagName, language, new LanguageSource(LanguageSourceKind.Element, tag.TagName.ToLowerInvariant()));
    }

    // A closing tag closes the nearest open element of its name (and the ones left open inside it); one that closes
    // nothing open is ignored.
    private static void Close(List<Frame> frames, string name)
    {
        for (int i = frames.Count - 1; i > 0; i--)
        {
            if (string.Equals(frames[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                frames.RemoveRange(i, frames.Count - i);
                return;
            }
        }
    }

    private static void AddAttributes(string html, TagLister.TagInfo tag, List<SoftHyphenSegment> segments)
    {
        foreach ((string name, int valueStart, int valueLength) in Attributes(html, tag))
        {
            if (valueLength > 0)
            {
                segments.Add(Outside(valueStart, valueStart + valueLength, SoftHyphenPlaceKind.Attribute, name));
            }
        }
    }

    // The attributes of an opening or empty tag: name, and the offset and length of the value in html.
    private static IEnumerable<(string Name, int ValueStart, int ValueLength)> Attributes(string html, TagLister.TagInfo tag)
    {
        int end = tag.Pos + tag.Len;
        int p = tag.Pos + 1;
        while (p < end && !char.IsWhiteSpace(html[p]) && html[p] is not ('>' or '/'))
        {
            p++;
        }

        while (p < end)
        {
            while (p < end && (char.IsWhiteSpace(html[p]) || html[p] == '/'))
            {
                p++;
            }

            int nameStart = p;
            while (p < end && !char.IsWhiteSpace(html[p]) && html[p] is not ('=' or '>' or '/'))
            {
                p++;
            }

            if (p == nameStart)
            {
                yield break;
            }

            string name = html[nameStart..p];
            while (p < end && char.IsWhiteSpace(html[p]))
            {
                p++;
            }

            if (p >= end || html[p] != '=')
            {
                continue;
            }

            p++;
            while (p < end && char.IsWhiteSpace(html[p]))
            {
                p++;
            }

            if (p < end && html[p] is '"' or '\'')
            {
                char quote = html[p];
                int valueStart = p + 1;
                int close = html.IndexOf(quote, valueStart, end - valueStart);
                int valueEnd = close < 0 ? end - 1 : close;
                yield return (name, valueStart, valueEnd - valueStart);
                p = valueEnd + 1;
            }
            else
            {
                int valueStart = p;
                while (p < end && !char.IsWhiteSpace(html[p]) && html[p] != '>')
                {
                    p++;
                }

                yield return (name, valueStart, p - valueStart);
            }
        }
    }

    private readonly record struct Frame(string Name, string Language, LanguageSource Source);
}
