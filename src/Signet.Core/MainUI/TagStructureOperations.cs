using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Signet.Core.Localization;

namespace Signet.Core.MainUI;

/// <summary>
/// An (X)HTML/XML element enclosing a position: the ranges of the opening and closing tags
/// (<see cref="ClosePos"/> = <c>-1</c> for an empty or unclosed tag).
/// </summary>
/// <param name="Name">The element name as in the source (with a prefix).</param>
/// <param name="OpenPos">The start of the opening tag (<c>&lt;</c>).</param>
/// <param name="OpenLen">The length of the opening tag.</param>
/// <param name="ClosePos">The start of the closing tag or <c>-1</c>.</param>
/// <param name="CloseLen">The length of the closing tag or <c>-1</c>.</param>
/// <param name="SelfClosing">Whether this is an empty tag (<c>&lt;br/&gt;</c>).</param>
public readonly record struct EnclosingElement(string Name, int OpenPos, int OpenLen, int ClosePos, int CloseLen, bool SelfClosing)
{
    /// <summary>Whether the element has a closing tag.</summary>
    public bool HasClose => ClosePos >= 0;
}

/// <summary>
/// Editor operations based on the tag structure (finding the closest containing tag, jumping to the
/// enclosing tag, selecting tag contents, splitting a tag, auto-closing a tag), computed on
/// <see cref="TagLister"/> rather than on highlighter data.
/// </summary>
public static partial class TagStructureOperations
{
    // The names of block-level tags.
    private static readonly HashSet<string> BlockTagNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "center", "dir", "fieldset", "isindex", "menu",
        "noframes", "hgroup", "noscript", "pre", "section", "h1", "h2", "h3", "h4", "h5", "h6",
        "header", "p", "div", "dd", "dl", "ul", "ol", "li", "body", "td", "th",
    };

    /// <summary>
    /// The closest element enclosing the position <paramref name="caret"/>: when the position is inside
    /// a tag — that tag's element; otherwise the deepest element opened before the
    /// position and not closed before it. <c>null</c> when there is none.
    /// </summary>
    public static EnclosingElement? FindEnclosingElement(string text, int caret)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return null;
        }

        int pos = Math.Clamp(caret, 0, text.Length);
        var lister = new TagLister(text);

        int i = lister.FindFirstTagOnOrAfter(pos);
        TagLister.TagInfo ti = lister.At(i);
        if (ti.Len > 0 && pos > ti.Pos && pos < ti.Pos + ti.Len)
        {
            switch (ti.Kind)
            {
                case TagKind.Begin:
                    return Element(text, lister, i, lister.FindCloseTagForOpen(i));
                case TagKind.SelfClosing:
                    return Element(text, lister, i, -1);
                case TagKind.End:
                    int open = lister.FindOpenTagForClose(i);
                    return open >= 0 ? Element(text, lister, open, i) : null;
                default:
                    pos = ti.Pos; // a comment / DOCTYPE — counted from its start
                    break;
            }
        }

        for (int j = lister.FindFirstTagOnOrAfter(pos) - 1; j >= 0; j--)
        {
            TagLister.TagInfo candidate = lister.At(j);
            if (candidate.Kind != TagKind.Begin || candidate.Pos >= pos)
            {
                continue;
            }

            int close = lister.FindCloseTagForOpen(j);
            if (close < 0 || lister.At(close).Pos >= pos)
            {
                return Element(text, lister, j, close);
            }
        }

        return null;
    }

    private static EnclosingElement Element(string text, TagLister lister, int open, int close)
    {
        TagLister.TagInfo o = lister.At(open);
        string name = TagNameRegex().Match(text, o.Pos) is { Success: true } m ? m.Groups[1].Value : o.TagName;
        return close >= 0 && lister.At(close).Len > 0
            ? new EnclosingElement(name, o.Pos, o.Len, lister.At(close).Pos, lister.At(close).Len, false)
            : new EnclosingElement(name, o.Pos, o.Len, -1, -1, o.Kind == TagKind.SelfClosing);
    }

    /// <summary>The caret position after the <c>&lt;</c> of the enclosing element's opening tag (Ctrl+{).</summary>
    public static int? OpeningTagCaret(string text, int caret) =>
        FindEnclosingElement(text, caret) is { } e ? e.OpenPos + 1 : null;

    /// <summary>The caret position after the <c>&lt;/</c> of the enclosing element's closing tag (Ctrl+}).</summary>
    public static int? ClosingTagCaret(string text, int caret) =>
        FindEnclosingElement(text, caret) is { HasClose: true } e ? e.ClosePos + 2 : null;

    /// <summary>The range of the enclosing element's content — between the tags (select tag contents).</summary>
    public static (int Start, int End)? TagContentsRange(string text, int caret) =>
        FindEnclosingElement(text, caret) is { HasClose: true } e ? (e.OpenPos + e.OpenLen, e.ClosePos) : null;

    /// <summary>
    /// Renames the enclosing element in the opening and closing tags at once (attributes
    /// are kept). An invalid name or an unclosed element — unchanged, with a message.
    /// </summary>
    public static FormatEdit RenameTag(string text, int caret, string newName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(newName);
        int pos = Math.Clamp(caret, 0, text.Length);
        newName = newName.Trim();
        if (!ValidNameRegex().IsMatch(newName))
        {
            return FormatEdit.Unchanged(text, pos, pos) with { StatusMessage = CoreStrings.Format("Status_InvalidTagName", newName) };
        }

        if (FindEnclosingElement(text, pos) is not { } e)
        {
            return FormatEdit.Unchanged(text, pos, pos) with { StatusMessage = CoreStrings.Get("Status_NoTagToRename") };
        }

        if (!e.HasClose && !e.SelfClosing)
        {
            return FormatEdit.Unchanged(text, pos, pos) with
            {
                StatusMessage = CoreStrings.Format("Status_TagNotClosed", e.Name),
            };
        }

        string result = text;
        int newCaret = pos;
        if (e.HasClose)
        {
            Match close = CloseNameRegex().Match(result, e.ClosePos);
            result = Replace(result, close.Groups[1], newName, ref newCaret);
        }

        Match open = TagNameRegex().Match(result, e.OpenPos);
        result = Replace(result, open.Groups[1], newName, ref newCaret);
        return new FormatEdit(result, newCaret, newCaret, !string.Equals(result, text, StringComparison.Ordinal));
    }

    private static string Replace(string text, Group name, string newName, ref int caret)
    {
        if (caret > name.Index + name.Length)
        {
            caret += newName.Length - name.Length;
        }
        else if (caret > name.Index)
        {
            caret = name.Index + newName.Length;
        }

        return string.Concat(text.AsSpan(0, name.Index), newName, text.AsSpan(name.Index + name.Length));
    }

    /// <summary>
    /// Splits the enclosing element at the caret: inserts its closing tag
    /// and a copy of the opening one without the <c>id</c> attribute; for a block element the copy
    /// starts a new line with the same indentation.
    /// </summary>
    public static FormatEdit SplitTag(string text, int caret)
    {
        ArgumentNullException.ThrowIfNull(text);
        int pos = Math.Clamp(caret, 0, text.Length);
        var lister = new TagLister(text);
        TagLister.TagInfo ti = lister.At(lister.FindFirstTagOnOrAfter(pos));
        if (ti.Len > 0 && pos > ti.Pos && pos < ti.Pos + ti.Len)
        {
            return FormatEdit.Unchanged(text, pos, pos) with
            {
                StatusMessage = CoreStrings.Get("Status_CannotSplitInsideTag"),
            };
        }

        if (FindEnclosingElement(text, pos) is not { } e)
        {
            return FormatEdit.Unchanged(text, pos, pos) with { StatusMessage = CoreStrings.Get("Status_NoTagToSplit") };
        }

        if (!e.HasClose)
        {
            return FormatEdit.Unchanged(text, pos, pos) with
            {
                StatusMessage = CoreStrings.Format("Status_TagNotClosed", e.Name),
            };
        }

        string openText = text.Substring(e.OpenPos, e.OpenLen);
        openText = IdAttributeRegex().Replace(openText, string.Empty);
        openText = WhitespaceRegex().Replace(openText, " ");
        openText = SpaceBeforeEndRegex().Replace(openText, ">");
        string closeText = text.Substring(e.ClosePos, e.CloseLen);

        string prefix = string.Empty;
        if (BlockTagNames.Contains(LocalName(e.Name)))
        {
            int lineStart = e.OpenPos == 0 ? 0 : text.LastIndexOf('\n', e.OpenPos - 1) + 1;
            string indent = text[lineStart..e.OpenPos];
            if (indent.Length > 0 && string.IsNullOrWhiteSpace(indent))
            {
                prefix = "\n" + indent;
            }
        }

        string insert = closeText + prefix + openText;
        string result = text.Insert(pos, insert);
        int newCaret = pos + insert.Length;
        return new FormatEdit(result, newCaret, newCaret, true);
    }

    /// <summary>
    /// Auto-closing: when <c>&lt;/</c> was typed right before <paramref name="caret"/>, returns the name
    /// of the element to close (the closest element enclosing the place of
    /// <c>&lt;</c>). <c>null</c> when there is nothing to close or <c>&lt;/</c> is already part of a tag
    /// (there is a <c>&gt;</c> after the caret before the next <c>&lt;</c> in this or the next line).
    /// </summary>
    public static string? AutoCloseTagName(string text, int caret)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (caret < 2 || caret > text.Length || text[caret - 2] != '<' || text[caret - 1] != '/')
        {
            return null;
        }

        if (LooksInsideTag(text, caret))
        {
            return null;
        }

        string without = text.Remove(caret - 2, 2);
        return FindEnclosingElement(without, caret - 2) is { SelfClosing: false } e ? e.Name : null;
    }

    // Checks whether we are inside a tag: in the current and next line, a ">" before a "<".
    private static bool LooksInsideTag(string text, int caret)
    {
        int lineEnd = text.IndexOf('\n', caret);
        if (InsideTagOnLine(text, caret, lineEnd < 0 ? text.Length : lineEnd))
        {
            return true;
        }

        if (lineEnd < 0)
        {
            return false;
        }

        int nextEnd = text.IndexOf('\n', lineEnd + 1);
        return InsideTagOnLine(text, lineEnd + 1, nextEnd < 0 ? text.Length : nextEnd);
    }

    private static bool InsideTagOnLine(string text, int from, int to)
    {
        int close = text.IndexOf('>', from, to - from);
        int open = text.IndexOf('<', from, to - from);
        return close > -1 && (open == -1 || close < open);
    }

    private static string LocalName(string name) =>
        name.IndexOf(':', StringComparison.Ordinal) is var colon && colon >= 0 ? name[(colon + 1)..] : name;

    [GeneratedRegex(@"\G<\s*([^\s/>]+)")]
    private static partial Regex TagNameRegex();

    [GeneratedRegex(@"\G<\s*/\s*([^\s>]+)")]
    private static partial Regex CloseNameRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.:-]*$")]
    private static partial Regex ValidNameRegex();

    [GeneratedRegex(@"\s\bid\s*=\s*(?:""[^""]*""|'[^']*')")]
    private static partial Regex IdAttributeRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\s+>$")]
    private static partial Regex SpaceBeforeEndRegex();
}
