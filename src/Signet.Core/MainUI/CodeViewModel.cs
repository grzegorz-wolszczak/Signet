using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;
using Signet.Core.Resources;
using Signet.Core.Semantics;

namespace Signet.Core.MainUI;

/// <summary>
/// The view-independent model of the code editor (Code View), independent of AvaloniaEdit: a working copy of
/// the resource text, modification tracking, flushing to the resource, XML validity checking and
/// inserting the section split marker.
/// </summary>
/// <remarks>
/// The editor control (<c>AvaloniaEdit.TextEditor</c>) and syntax highlighting belong to the
/// App layer (<c>Signet.App.ViewModels.Tabs.CodeTabViewModel</c> + <c>Views/Tabs/CodeTabView</c>).
/// The App layer synchronizes <see cref="Text"/> with the editor document on every change,
/// and on save/tab switch calls <see cref="SaveToResource"/>.
/// </remarks>
public sealed partial class CodeViewModel
{
    /// <summary>
    /// The section split marker inserted by "Insert Split Marker". The actual
    /// file split is performed separately.
    /// </summary>
    public const string SectionMarker = "<hr class=\"signet_split_marker\" />";

    private readonly TextResource _resource;
    private string _text;
    private string _savedText;

    /// <summary>Creates a model for a text resource, loading its current content.</summary>
    public CodeViewModel(TextResource resource)
    {
        _resource = resource ?? throw new ArgumentNullException(nameof(resource));
        _resource.InitialLoad();
        _text = _savedText = _resource.GetText();
        Syntax = CodeViewSyntaxMap.ForResource(resource);
    }

    /// <summary>The resource edited in this model.</summary>
    public TextResource Resource => _resource;

    /// <summary>The syntax kind (selects the highlighting grammar in the App layer).</summary>
    public CodeViewSyntax Syntax { get; }

    /// <summary>Whether XML validity (well-formedness) is checked for this resource.</summary>
    public bool SupportsWellFormedCheck => CodeViewSyntaxMap.SupportsWellFormedCheck(Syntax);

    /// <summary>The working copy of the content. The App layer synchronizes it with the editor document.</summary>
    public string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    /// <summary>
    /// Whether the working copy differs from the content last flushed to the resource
    /// (a content comparison, not an editor flag).
    /// </summary>
    public bool IsModified => !string.Equals(_text, _savedText, StringComparison.Ordinal);

    /// <summary>
    /// Silently flushes the working copy to the resource (<c>TextResource.SetText</c>) if anything changed.
    /// Returns <c>true</c> when a write happened.
    /// </summary>
    public bool SaveToResource()
    {
        if (!IsModified)
        {
            return false;
        }

        _resource.SetText(_text);
        _savedText = _text;
        return true;
    }

    /// <summary>Reloads the content from the resource, discarding unsaved changes.</summary>
    public void ReloadFromResource()
    {
        _resource.InitialLoad();
        _text = _savedText = _resource.GetText();
    }

    /// <summary>
    /// Checks the syntactic validity of the current content. Returns <c>null</c> when the check does not
    /// apply to this kind of resource (<see cref="SupportsWellFormedCheck"/> is <c>false</c>).
    /// </summary>
    public WellFormedResult? CheckWellFormed()
    {
        if (!SupportsWellFormedCheck)
        {
            return null;
        }

        return Syntax switch
        {
            CodeViewSyntax.Html => WellFormedChecker.CheckXhtmlStructure(_text, _resource.EpubVersion),
            CodeViewSyntax.Css => CssWellFormedChecker.Check(_text),
            _ => WellFormedChecker.Check(_text, _resource.MediaType),
        };
    }

    /// <summary>
    /// The number of CSS rules in the current content (selector blocks plus <c>@font-face</c> / <c>@page</c>) —
    /// the "rule counter". Returns <c>0</c> for resources other than CSS.
    /// </summary>
    public int GetCssRuleCount() =>
        Syntax == CodeViewSyntax.Css ? new CssInfo(_text).Rules.Count : 0;

    /// <summary>
    /// Inserts <see cref="SectionMarker"/> at the given (0-based) position of the working copy and returns
    /// the new caret position (right after the inserted marker).
    /// </summary>
    public int InsertSectionMarkerAt(int offset)
    {
        int clamped = Math.Clamp(offset, 0, _text.Length);
        _text = _text.Insert(clamped, SectionMarker);
        return clamped + SectionMarker.Length;
    }

    /// <summary>
    /// For a caret position inside an (X)HTML/XML tag, returns the ranges of the opening tag
    /// and (for a begin/end pair) the closing tag to highlight. Returns <c>null</c> when the caret
    /// is not in a tag or the syntax is not markup.
    /// </summary>
    public TagPairHighlight? GetTagPairHighlight(int caretOffset)
    {
        if (Syntax is not (CodeViewSyntax.Html or CodeViewSyntax.Xml) || _text.Length == 0)
        {
            return null;
        }

        string text = _text;
        int pos = Math.Clamp(caretOffset, 0, text.Length);

        int pb = LastIndexOf(text, '<', pos);
        int prevGt = LastIndexOf(text, '>', pos - 1);
        int ne = IndexOf(text, '>', pos);
        int nb = IndexOf(text, '<', pos + 1);
        if (nb == -1)
        {
            nb = text.Length + 1;
        }

        // The caret is in a tag when "<" is closer than ">" looking backwards, ">" is at/after
        // the caret position, and the next "<" is farther away than that ">".
        if (!(pb > prevGt && ne >= pos && nb > ne))
        {
            return null;
        }

        var lister = new TagLister(text);
        int i = lister.FindFirstTagOnOrAfter(pos);
        TagLister.TagInfo ti = lister.At(i);
        if (pos < ti.Pos || pos >= ti.Pos + ti.Len)
        {
            return null;
        }

        (int Offset, int Length)? open;
        (int Offset, int Length)? close = null;
        if (ti.Kind == TagKind.End)
        {
            open = ti.OpenLen != -1 ? (ti.OpenPos, ti.OpenLen) : null;
            close = (ti.Pos, ti.Len);
        }
        else
        {
            open = (ti.Pos, ti.Len);
            if (ti.Kind == TagKind.Begin)
            {
                int j = lister.FindCloseTagForOpen(i);
                if (j >= 0 && lister.At(j).Len != -1)
                {
                    close = (lister.At(j).Pos, lister.At(j).Len);
                }
            }
        }

        return open is null && close is null ? null : new TagPairHighlight(open, close);
    }

    /// <summary>
    /// For a caret position inside the value of the <c>class="..."</c> attribute of an opening /
    /// empty tag, returns the single class name the caret is on (one of several separated by
    /// spaces) — the logic behind "Jump to CSS class definition" (Ctrl+click). Returns <c>null</c> when the syntax
    /// is not markup, the caret is not in a tag, the tag has no <c>class</c> attribute or the caret is outside its value.
    /// </summary>
    public string? GetClassNameAtCaret(int caretOffset)
    {
        if (Syntax is not (CodeViewSyntax.Html or CodeViewSyntax.Xml) || _text.Length == 0)
        {
            return null;
        }

        string text = _text;
        int pos = Math.Clamp(caretOffset, 0, text.Length);

        var lister = new TagLister(text);
        int i = lister.FindFirstTagOnOrAfter(pos);
        TagLister.TagInfo ti = lister.At(i);
        if (pos < ti.Pos || pos >= ti.Pos + ti.Len || ti.Kind is not (TagKind.Begin or TagKind.SelfClosing))
        {
            return null;
        }

        string tagText = text.Substring(ti.Pos, ti.Len);
        TagLister.AttInfo classAttribute = TagLister.ParseAttribute(tagText, "class");
        if (classAttribute.Pos == -1 || classAttribute.VLen <= 0)
        {
            return null;
        }

        int valueStart = ti.Pos + classAttribute.VPos;
        int relativeCaret = pos - valueStart;
        if (relativeCaret < 0 || relativeCaret > classAttribute.VLen)
        {
            return null;
        }

        string value = classAttribute.AValue;
        int idx = 0;
        while (idx < value.Length)
        {
            while (idx < value.Length && char.IsWhiteSpace(value[idx]))
            {
                idx++;
            }

            int tokenStart = idx;
            while (idx < value.Length && !char.IsWhiteSpace(value[idx]))
            {
                idx++;
            }

            if (tokenStart < idx && relativeCaret >= tokenStart && relativeCaret <= idx)
            {
                return value[tokenStart..idx];
            }
        }

        return null;
    }

    /// <summary>
    /// The link reference the caret is on — the <c>href</c>/<c>src</c>/<c>poster</c>/
    /// <c>xlink:href</c> value of an (X)HTML/XML tag or the target of <c>url(...)</c> in CSS (also in
    /// <c>&lt;style&gt;</c>). Used by Ctrl+click and "Go To Link Or Style".
    /// <c>null</c> when the caret is not on a link.
    /// </summary>
    public string? GetLinkAtCaret(int caretOffset)
    {
        if (_text.Length == 0)
        {
            return null;
        }

        string text = _text;
        int pos = Math.Clamp(caretOffset, 0, text.Length);

        if (Syntax is CodeViewSyntax.Html or CodeViewSyntax.Xml)
        {
            var lister = new TagLister(text);
            TagLister.TagInfo ti = lister.At(lister.FindFirstTagOnOrAfter(pos));
            if (pos >= ti.Pos && pos < ti.Pos + ti.Len)
            {
                if (ti.Kind is not (TagKind.Begin or TagKind.SelfClosing))
                {
                    return null;
                }

                string tagText = text.Substring(ti.Pos, ti.Len);
                foreach (string name in LinkAttributeNames)
                {
                    TagLister.AttInfo attribute = TagLister.ParseAttribute(tagText, name);
                    int valueStart = ti.Pos + attribute.VPos;
                    if (attribute.Pos != -1 && attribute.VLen > 0 && pos >= valueStart && pos <= valueStart + attribute.VLen)
                    {
                        return attribute.AValue.Trim();
                    }
                }

                return null;
            }
        }

        if (Syntax is CodeViewSyntax.Css or CodeViewSyntax.Html)
        {
            int lineStart = pos == 0 ? 0 : text.LastIndexOf('\n', pos - 1) + 1;
            int lineEnd = text.IndexOf('\n', pos);
            string line = text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)];
            foreach (Match m in CssUrlRegex().Matches(line))
            {
                if (pos >= lineStart + m.Index && pos <= lineStart + m.Index + m.Length)
                {
                    string target = m.Groups["target"].Value.Trim();
                    return target.Length > 0 ? target : null;
                }
            }
        }

        return null;
    }

    private static readonly string[] LinkAttributeNames = { "href", "src", "poster", "xlink:href" };

    /// <summary>
    /// The reference of the image in whose tag the caret sits — the <c>src</c> of an <c>&lt;img&gt;</c> tag
    /// or the <c>xlink:href</c>/<c>href</c> of an <c>&lt;img&gt;</c>/<c>&lt;image&gt;</c> tag (SVG).
    /// <c>null</c> when the caret is not in such a tag. Used by the
    /// "View Image" / "Open Tab For Image" context menu entries.
    /// </summary>
    public string? GetImageSourceAtCaret(int caretOffset)
    {
        if (Syntax is not (CodeViewSyntax.Html or CodeViewSyntax.Xml) || _text.Length == 0)
        {
            return null;
        }

        string text = _text;
        int pos = Math.Clamp(caretOffset, 0, text.Length);
        var lister = new TagLister(text);
        TagLister.TagInfo ti = lister.At(lister.FindFirstTagOnOrAfter(pos));
        if (pos < ti.Pos || pos >= ti.Pos + ti.Len || ti.Kind is not (TagKind.Begin or TagKind.SelfClosing))
        {
            return null;
        }

        string name = ti.TagName;
        int colon = name.IndexOf(':', StringComparison.Ordinal);
        string localName = (colon >= 0 ? name[(colon + 1)..] : name).ToLowerInvariant();
        if (localName is not ("img" or "image"))
        {
            return null;
        }

        string tagText = text.Substring(ti.Pos, ti.Len);
        foreach (string attributeName in ImageAttributeNames)
        {
            TagLister.AttInfo attribute = TagLister.ParseAttribute(tagText, attributeName);
            if (attribute.Pos != -1 && attribute.VLen > 0)
            {
                return attribute.AValue.Trim();
            }
        }

        return null;
    }

    private static readonly string[] ImageAttributeNames = { "src", "xlink:href", "href" };

    [GeneratedRegex(@"url\(\s*(?<q>['""]?)(?<target>[^'"")]*?)\k<q>\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex CssUrlRegex();

    private static int LastIndexOf(string text, char value, int startIndex)
    {
        if (startIndex < 0 || text.Length == 0)
        {
            return -1;
        }

        return text.LastIndexOf(value, Math.Min(startIndex, text.Length - 1));
    }

    private static int IndexOf(string text, char value, int startIndex) =>
        startIndex > text.Length ? -1 : text.IndexOf(value, startIndex);

    /// <summary>
    /// Formats the caret position for the status bar: <c>"&lt;code point name&gt; (U+XXXX) —
    /// Linia N, kol. M"</c> (the line/column wording is Polish). When <paramref name="codepoint"/> is negative, the <c>(U+XXXX)</c> part
    /// is omitted (and the name for <c>-1</c> is <c>"EOF"</c>).
    /// </summary>
    public static string FormatCaretPosition(int line, int column, int codepoint = -1)
    {
        string name = CodepointNames.GetName(codepoint);
        string cp = codepoint >= 0
            ? " (U+" + codepoint.ToString("X4", CultureInfo.InvariantCulture) + ")"
            : string.Empty;
        return $"{name}{cp} — Linia {line}, kol. {column}";
    }
}

/// <summary>
/// The ranges to highlight for an opening / closing tag pair (offsets in the document).
/// Each member may be <c>null</c> (e.g. a single tag — only <see cref="Open"/>).
/// </summary>
/// <param name="Open">The range of the opening tag (or of a single tag / DOCTYPE / comment).</param>
/// <param name="Close">The range of the closing tag of a begin/end pair.</param>
public readonly record struct TagPairHighlight(
    (int Offset, int Length)? Open,
    (int Offset, int Length)? Close);
