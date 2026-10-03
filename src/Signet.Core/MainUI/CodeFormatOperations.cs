using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Signet.Core.Parsers;
using Signet.Core.Localization;

namespace Signet.Core.MainUI;

/// <summary>
/// View-independent implementation of the "Format" menu operations of the code editor:
/// inline tag toggles, block replacement (headings), lists, indentation, alignment, text
/// direction, case changes, "Remove Formatting", "Remove Tag Pair", "Insert Closing Tag".
/// </summary>
/// <remarks>
/// All methods operate on a plain source string plus a selection range
/// <c>[selectionStart, selectionEnd)</c> (0-based, <c>selectionStart == selectionEnd</c> = no
/// selection). They return a <see cref="FormatEdit"/> with the new content and the new selection; the
/// App layer (<c>CodeTabViewModel</c>) applies the minimal difference to the AvaloniaEdit <c>TextDocument</c>,
/// so that the change lands on the undo stack as a single step.
/// The positional logic uses <see cref="TagLister"/>.
/// </remarks>
public static class CodeFormatOperations
{
    // The block-level tags.
    internal static readonly HashSet<string> BlockLevelTags = new(StringComparer.Ordinal)
    {
        "address", "blockquote", "center", "dir", "div", "dl", "fieldset", "form",
        "h1", "h2", "h3", "h4", "h5", "h6", "hr", "isindex", "menu", "noframes",
        "noscript", "ol", "p", "pre", "table", "ul", "body",
    };

    // The tags that can carry an id/dir (the block-level tags + inline/table tags + a).
    internal static readonly HashSet<string> IdTags = new(StringComparer.Ordinal)
    {
        "address", "blockquote", "center", "dir", "div", "dl", "fieldset", "form",
        "h1", "h2", "h3", "h4", "h5", "h6", "hr", "isindex", "menu", "noframes",
        "noscript", "ol", "p", "pre", "table", "ul", "body",
        "dd", "dt", "li", "tbody", "td", "tfoot", "th", "thead", "tr", "a", "abbr",
        "acronym", "b", "big", "caption", "cite", "code", "dfn", "em", "font", "i",
        "label", "mark", "small", "span", "strike", "strong", "sub", "sup", "u",
    };

    private static readonly Regex OpenTagStartsSelection =
        new(@"^\s*(<\s*([a-zA-Z0-9]+)[^>]*>)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StartingIndentUsed =
        new(@"(^\s*)[^\s]", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TagNameSearch =
        new(@"<\s*([^\s>]+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The set of block-level tags — used to detect the heading state.</summary>
    public static IReadOnlyCollection<string> BlockTags => BlockLevelTags;

    // ---------------------------------------------------------------------
    // Toggling inline tags: Bold/Italic/Underline/Strikethrough/Sub/Sup
    // ---------------------------------------------------------------------

    /// <summary>
    /// Toggles wrapping the selection in the <paramref name="elementName"/> tag.
    /// When the caret is outside <c>&lt;body&gt;</c> and
    /// <paramref name="cssProperty"/>/<paramref name="cssValue"/> are given, it modifies the CSS rule on the line.
    /// </summary>
    public static FormatEdit ToggleInline(
        string text,
        int selectionStart,
        int selectionEnd,
        string elementName,
        string? cssProperty = null,
        string? cssValue = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(elementName);
        Normalize(text, ref selectionStart, ref selectionEnd);

        if (selectionStart == selectionEnd)
        {
            (selectionStart, selectionEnd) = WordUnder(text, selectionStart);
        }

        int pos = selectionStart;
        var lister = new TagLister(text);

        if (!lister.IsPositionInBody(pos))
        {
            if (!string.IsNullOrEmpty(cssProperty) && !string.IsNullOrEmpty(cssValue))
            {
                return FormatCssStyleOnLine(text, pos, cssProperty!, cssValue!);
            }

            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        if (IsPositionInTag(text, lister, selectionStart) ||
            IsPositionInTag(text, lister, selectionEnd - 1))
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        bool inExistingOccurrence = false;
        int i = lister.FindLastTagOnOrBefore(pos);
        while (i >= 0)
        {
            TagLister.TagInfo ti = lister.At(i);
            if (ti.Len == -1)
            {
                return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
            }

            if (string.Equals(elementName, ti.TagName, StringComparison.Ordinal))
            {
                if (ti.Kind != TagKind.End)
                {
                    inExistingOccurrence = true;
                }

                break;
            }

            if (BlockLevelTags.Contains(ti.TagName))
            {
                break;
            }

            i--;
        }

        if (i < 0)
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        return inExistingOccurrence
            ? FormatSelectionWithinElement(text, selectionStart, selectionEnd, elementName, i, lister)
            : InsertTagAroundSelection(text, selectionStart, selectionEnd, elementName, "/" + elementName, string.Empty);
    }

    // Formats a selection that lies within an existing element.
    private static FormatEdit FormatSelectionWithinElement(
        string text, int selectionStart, int selectionEnd, string elementName, int tagno, TagLister lister)
    {
        int j = lister.FindCloseTagForOpen(tagno);
        if (j < 0)
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        TagLister.TagInfo tb = lister.At(tagno);
        TagLister.TagInfo te = lister.At(j);

        bool adjacentToStart = tb.Pos + tb.Len == selectionStart;
        bool adjacentToEnd = te.Pos == selectionEnd;

        if (!adjacentToStart && !adjacentToEnd)
        {
            // "<b>XXX|sel|YYY</b>" => "<b>XXX</b>sel<b>YYY</b>" (the opening attributes are copied).
            string openingTagText = text.Substring(tb.Pos + 1, tb.Len - 2).Trim();
            return InsertTagAroundSelection(text, selectionStart, selectionEnd, "/" + elementName, openingTagText, string.Empty);
        }

        if (selectionEnd == selectionStart && (adjacentToStart || adjacentToEnd) && te.Pos > tb.Pos + tb.Len)
        {
            // The caret is right in the middle of a pair with content — do nothing.
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        string openingTag = text.Substring(tb.Pos, tb.Len);
        string closingTag = text.Substring(te.Pos, te.Len);
        string replacement = text.Substring(selectionStart, selectionEnd - selectionStart);

        int cutStart = selectionStart;
        int cutEnd = selectionEnd;
        int newSelStart = selectionStart;
        int newSelEnd = selectionEnd;

        if (adjacentToStart)
        {
            cutStart -= openingTag.Length;
            newSelStart -= openingTag.Length;
            newSelEnd -= openingTag.Length;
        }
        else
        {
            replacement = closingTag + replacement;
            newSelStart += closingTag.Length;
            newSelEnd += closingTag.Length;
        }

        if (adjacentToEnd)
        {
            cutEnd += closingTag.Length;
        }
        else
        {
            replacement += openingTag;
        }

        string newText = text[..cutStart] + replacement + text[cutEnd..];
        return new FormatEdit(newText, newSelStart, newSelEnd, !string.Equals(newText, text, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------
    // Headings / Normal — block formatting.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Replaces the block tag enclosing the caret with <paramref name="elementName"/>
    /// (<c>h1</c>–<c>h6</c> or <c>p</c>). When there is no selection,
    /// it covers the current line (trimming the outermost tags).
    /// </summary>
    public static FormatEdit FormatBlock(
        string text, int selectionStart, int selectionEnd, string elementName, bool preserveAttributes)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(elementName);
        Normalize(text, ref selectionStart, ref selectionEnd);

        if (selectionStart == selectionEnd)
        {
            (selectionStart, selectionEnd) = DefaultLineSelection(text, selectionStart);
        }

        int pos = selectionStart;
        var lister = new TagLister(text);
        if (!lister.IsPositionInBody(pos))
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        int i = lister.FindLastTagOnOrBefore(pos);
        while (i >= 0)
        {
            TagLister.TagInfo ti = lister.At(i);
            if (BlockLevelTags.Contains(ti.TagName))
            {
                if (ti.Kind == TagKind.End && pos >= ti.Pos && pos < ti.Pos + ti.Len)
                {
                    i--;
                    continue;
                }

                if (ti.TagName == "body" || ti.Kind == TagKind.End || ti.Kind == TagKind.SelfClosing)
                {
                    return InsertTagAroundSelection(text, selectionStart, selectionEnd, elementName, "/" + elementName, string.Empty);
                }

                string openingTagText = text.Substring(ti.Pos, ti.Len);
                string allAttributes = TagLister.ExtractAllAttributes(openingTagText);

                int k = i + 1;
                TagLister.TagInfo et = lister.At(k);
                while (et.Len != -1 && et.OpenPos != ti.Pos)
                {
                    k++;
                    et = lister.At(k);
                }

                if (et.Len == -1)
                {
                    return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
                }

                string newOpen = preserveAttributes && allAttributes.Length > 0
                    ? "<" + elementName + " " + allAttributes + ">"
                    : "<" + elementName + ">";
                string newClose = "</" + elementName + ">";

                // The closing tag (later in the text) is replaced first, so that positions do not shift.
                string newText = text[..et.Pos] + newClose + text[(et.Pos + et.Len)..];
                newText = newText[..ti.Pos] + newOpen + newText[(ti.Pos + ti.Len)..];

                int caret = ti.Pos + newOpen.Length;
                bool changed = !string.Equals(newText, text, StringComparison.Ordinal);
                return new FormatEdit(newText, caret, caret, changed);
            }

            i--;
        }

        return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
    }

    // ---------------------------------------------------------------------
    // Lists.
    // ---------------------------------------------------------------------

    /// <summary>Wraps/unwraps the selected paragraphs in <c>&lt;ol&gt;</c>/<c>&lt;ul&gt;</c>.</summary>
    public static FormatEdit ApplyList(string text, int selectionStart, int selectionEnd, string element)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(element);
        Normalize(text, ref selectionStart, ref selectionEnd);

        string selectedText = text.Substring(selectionStart, selectionEnd - selectionStart);
        if (selectedText.Length == 0)
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        string indent = LeadingIndent(selectedText);
        string tagname = OpeningTagNameAtStart(selectedText);
        string newText = selectedText;

        if ((tagname == "ol" && element == "ol") || (tagname == "ul" && element == "ul"))
        {
            string body = RemoveLastTag(RemoveFirstTag(newText, element), element).Trim();
            var result = new List<string>();
            foreach (string item in SplitNonEmptyLines(body))
            {
                result.Add(indent + RemoveLastTag(RemoveFirstTag(item, "li"), "li"));
            }

            result.Add(string.Empty);
            newText = string.Join("\n", result);
        }
        else if (tagname is "p" or "")
        {
            var result = new List<string> { indent + "<" + element + ">" };
            foreach (string item in SplitNonEmptyLines(newText))
            {
                result.Add(indent + "    <li>" + item.Trim() + "</li>");
            }

            result.Add(indent + "</" + element + ">\n");
            newText = string.Join("\n", result);
        }

        return ReplaceSelection(text, selectionStart, selectionEnd, selectedText, newText);
    }

    // ---------------------------------------------------------------------
    // Indentation — wrapping the selection in an element (blockquote).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Wraps (<paramref name="unwrap"/> = <c>false</c>) or unwraps (<c>true</c>) the selection
    /// in the element <paramref name="element"/> (Increase/Decrease Indent uses <c>blockquote</c>).
    /// </summary>
    public static FormatEdit WrapInElement(string text, int selectionStart, int selectionEnd, string element, bool unwrap)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(element);
        Normalize(text, ref selectionStart, ref selectionEnd);

        string selectedText = text.Substring(selectionStart, selectionEnd - selectionStart);
        if (selectedText.Length == 0 || !IsFragmentBalanced(selectedText))
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        string indent = LeadingIndent(selectedText);
        string tagname = OpeningTagNameAtStart(selectedText);
        string newText = selectedText;

        if (unwrap)
        {
            if (tagname == element)
            {
                newText = RemoveLastTag(RemoveFirstTag(newText, element), element).Trim();
                newText = indent + newText;
            }
        }
        else
        {
            string trimmed = newText.Trim();
            newText = string.Join(
                "\n",
                indent + "<" + element + ">",
                indent + "    " + trimmed,
                indent + "</" + element + ">\n");
        }

        return ReplaceSelection(text, selectionStart, selectionEnd, selectedText, newText);
    }

    // ---------------------------------------------------------------------
    // Alignment / text direction (EPUB 2) — style formatting.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Sets/toggles the CSS property <paramref name="property"/> = <paramref name="value"/>.
    /// In <c>&lt;body&gt;</c> it modifies the <c>style</c> attribute of the nearest block
    /// element; outside <c>&lt;body&gt;</c> (or in CSS) — the rule on the line.
    /// </summary>
    public static FormatEdit FormatStyle(string text, int selectionStart, int selectionEnd, string property, string value)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(property);
        ArgumentException.ThrowIfNullOrEmpty(value);
        Normalize(text, ref selectionStart, ref selectionEnd);

        var lister = new TagLister(text);
        int pos = selectionStart;
        if (!lister.IsPositionInBody(pos))
        {
            return FormatCssStyleOnLine(text, pos, property, value);
        }

        string? existing = ProcessAttribute(text, lister, pos, pos, "style", BlockLevelTags, null, setAttr: false, out _, out _);
        string newStyleValue;
        if (existing is null)
        {
            newStyleValue = $"{property}: {value};";
        }
        else
        {
            var props = new List<CssProperty>(HtmlStyleInfo.GetCssProperties(existing, 0, existing.Length));
            ApplyChangeToProperties(props, property, value);
            if (props.Count == 0)
            {
                newStyleValue = string.Empty;
            }
            else
            {
                var parts = new List<string>();
                foreach (CssProperty p in props)
                {
                    parts.Add(p.Value is null ? p.Name : $"{p.Name}: {p.Value}");
                }

                newStyleValue = string.Join("; ", parts) + ";";
            }
        }

        _ = ProcessAttribute(
            text, lister, pos, pos, "style", BlockLevelTags, newStyleValue, setAttr: true, out string newText, out int caret);
        bool changed = !string.Equals(newText, text, StringComparison.Ordinal);
        return new FormatEdit(newText, caret, caret, changed);
    }

    // ---------------------------------------------------------------------
    // Text direction (EPUB 3).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Sets the <c>dir</c> attribute (<c>"ltr"</c>/<c>"rtl"</c>/<c>""</c> = remove) of the nearest element
    /// from <c>IdTags</c> enclosing the caret (EPUB 3).
    /// </summary>
    public static FormatEdit SetTextDirection(string text, int selectionStart, int selectionEnd, string dirValue)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dirValue);
        Normalize(text, ref selectionStart, ref selectionEnd);

        var lister = new TagLister(text);
        int pos = selectionStart;
        if (!lister.IsPositionInBody(pos))
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        _ = ProcessAttribute(text, lister, pos, pos, "dir", IdTags, dirValue, setAttr: true, out string newText, out int caret);
        bool changed = !string.Equals(newText, text, StringComparison.Ordinal);
        return new FormatEdit(newText, caret, caret, changed);
    }

    // ---------------------------------------------------------------------
    // Case change.
    // ---------------------------------------------------------------------

    /// <summary>Changes the letter case in the selection.</summary>
    public static FormatEdit ChangeCase(string text, int selectionStart, int selectionEnd, Casing casing)
    {
        ArgumentNullException.ThrowIfNull(text);
        Normalize(text, ref selectionStart, ref selectionEnd);

        string selectedText = text.Substring(selectionStart, selectionEnd - selectionStart);
        if (selectedText.Length == 0 ||
            selectedText.Contains('<', StringComparison.Ordinal) ||
            selectedText.Contains('>', StringComparison.Ordinal))
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        string newText = Utility.ChangeCase(selectedText, casing);
        if (string.Equals(newText, selectedText, StringComparison.Ordinal))
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        string full = text[..selectionStart] + newText + text[selectionEnd..];
        return new FormatEdit(full, selectionStart, selectionStart + newText.Length, true);
    }

    // ---------------------------------------------------------------------
    // Toggling a comment (Ctrl+Shift+/).
    // ---------------------------------------------------------------------

    /// <summary>Whether the given syntax has a defined comment delimiter pair (supported: HTML/XML/CSS/JS).</summary>
    public static bool IsCommentSyntaxSupported(CodeViewSyntax syntax) =>
        syntax is CodeViewSyntax.Html or CodeViewSyntax.Xml or CodeViewSyntax.Css or CodeViewSyntax.JavaScript;

    /// <summary>
    /// Toggles a comment for the selection: when the selection (or the fragment
    /// around it) is already a comment — removes it, otherwise wraps
    /// the selection (or the current line when there is no selection) in the delimiter pair appropriate for
    /// <paramref name="syntax"/> (<c>/* */</c> for CSS/JS, <c>&lt;!-- --&gt;</c> for HTML/XML).
    /// For an unsupported syntax (<see cref="IsCommentSyntaxSupported"/> = <c>false</c>) it returns
    /// the result unchanged.
    /// </summary>
    public static FormatEdit ToggleComment(string text, int selectionStart, int selectionEnd, CodeViewSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(text);
        Normalize(text, ref selectionStart, ref selectionEnd);

        if (!IsCommentSyntaxSupported(syntax))
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        (string open, string close) = syntax is CodeViewSyntax.Css or CodeViewSyntax.JavaScript
            ? ("/*", "*/")
            : ("<!--", "-->");

        if (selectionStart == selectionEnd)
        {
            (selectionStart, selectionEnd) = LineBounds(text, selectionStart);
        }

        if (TryUnwrapExactComment(text, selectionStart, selectionEnd, open, close, out FormatEdit exact))
        {
            return exact;
        }

        if (TryUnwrapEnclosingComment(text, selectionStart, selectionEnd, open, close, out FormatEdit enclosing))
        {
            return enclosing;
        }

        string selected = text.Substring(selectionStart, selectionEnd - selectionStart);
        string core = selected.Trim();
        if (core.Length == 0)
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        int leadLen = selected.Length - selected.TrimStart().Length;
        int trailLen = selected.Length - selected.TrimEnd().Length;
        string leading = selected[..leadLen];
        string trailing = selected[(selected.Length - trailLen)..];
        string replacement = leading + open + core + close + trailing;

        string newText = text[..selectionStart] + replacement + text[selectionEnd..];
        int newSelStart = selectionStart + leading.Length + open.Length;
        int newSelEnd = newSelStart + core.Length;
        return new FormatEdit(newText, newSelStart, newSelEnd, true);
    }

    // The selection (after trimming whitespace) is itself an entire comment -> remove the markers.
    private static bool TryUnwrapExactComment(
        string text, int selectionStart, int selectionEnd, string open, string close, out FormatEdit edit)
    {
        string selected = text.Substring(selectionStart, selectionEnd - selectionStart);
        string trimmed = selected.Trim();
        if (trimmed.Length < open.Length + close.Length ||
            !trimmed.StartsWith(open, StringComparison.Ordinal) ||
            !trimmed.EndsWith(close, StringComparison.Ordinal))
        {
            edit = default;
            return false;
        }

        string inner = trimmed[open.Length..^close.Length];
        int leadLen = selected.Length - selected.TrimStart().Length;
        int trailLen = selected.Length - selected.TrimEnd().Length;
        string leading = selected[..leadLen];
        string trailing = selected[(selected.Length - trailLen)..];
        string replacement = leading + inner + trailing;

        string newText = text[..selectionStart] + replacement + text[selectionEnd..];
        edit = new FormatEdit(newText, selectionStart, selectionStart + replacement.Length, true);
        return true;
    }

    // The selection (e.g. a caret without a selection in the middle of a multi-line comment) lies entirely
    // inside an existing comment whose markers are outside the selection -> remove the whole comment.
    private static bool TryUnwrapEnclosingComment(
        string text, int selectionStart, int selectionEnd, string open, string close, out FormatEdit edit)
    {
        edit = default;
        if (selectionStart <= 0)
        {
            return false;
        }

        int openPos = text.LastIndexOf(open, Math.Min(selectionStart, text.Length) - 1, StringComparison.Ordinal);
        if (openPos < 0)
        {
            return false;
        }

        int closePos = text.IndexOf(close, openPos + open.Length, StringComparison.Ordinal);
        if (closePos < 0)
        {
            return false;
        }

        int commentEnd = closePos + close.Length;
        if (commentEnd < selectionEnd)
        {
            return false;
        }

        string inner = text[(openPos + open.Length)..closePos];
        string newText = text[..openPos] + inner + text[commentEnd..];
        int newSelStart = Math.Clamp(selectionStart - open.Length, openPos, newText.Length);
        int newSelEnd = Math.Clamp(selectionEnd - open.Length, newSelStart, newText.Length);
        edit = new FormatEdit(newText, newSelStart, newSelEnd, true);
        return true;
    }

    // ---------------------------------------------------------------------
    // Remove Formatting.
    // ---------------------------------------------------------------------

    /// <summary>Whether "Remove Formatting" is allowed: a selection exists and neither starts nor ends in the middle of a tag.</summary>
    public static bool RemoveFormattingAllowed(string text, int selectionStart, int selectionEnd)
    {
        ArgumentNullException.ThrowIfNull(text);
        Normalize(text, ref selectionStart, ref selectionEnd);
        if (selectionStart == selectionEnd)
        {
            return false;
        }

        var lister = new TagLister(text);
        int start = selectionStart;
        int end = selectionEnd - 1;
        if (start < text.Length && end >= 0 && text[start] == '<' && text[end] == '>')
        {
            return true;
        }

        return !IsPositionInTag(text, lister, start) && !IsPositionInTag(text, lister, end);
    }

    /// <summary>Removes all tags (<c>&lt;...&gt;</c>) from the selection, leaving the text.</summary>
    public static FormatEdit RemoveFormatting(string text, int selectionStart, int selectionEnd)
    {
        ArgumentNullException.ThrowIfNull(text);
        Normalize(text, ref selectionStart, ref selectionEnd);
        if (!RemoveFormattingAllowed(text, selectionStart, selectionEnd))
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        string selectedText = text.Substring(selectionStart, selectionEnd - selectionStart);
        string stripped = StripTags(selectedText);
        string full = text[..selectionStart] + stripped + text[selectionEnd..];
        return new FormatEdit(full, selectionStart, selectionStart + stripped.Length, !string.Equals(full, text, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------
    // Remove Tag Pair.
    // ---------------------------------------------------------------------

    /// <summary>Whether "Remove Tag Pair" is allowed: there is no selection and the caret is inside a tag.</summary>
    public static bool RemoveTagPairAllowed(string text, int caretPosition)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (caretPosition < 0 || caretPosition > text.Length)
        {
            return false;
        }

        return IsPositionInTag(text, new TagLister(text), caretPosition);
    }

    /// <summary>Removes the enclosing element (a begin/end pair or a single tag), leaving the content.</summary>
    public static FormatEdit RemoveTagPair(string text, int caretPosition)
    {
        ArgumentNullException.ThrowIfNull(text);
        int caret = Math.Clamp(caretPosition, 0, text.Length);
        var lister = new TagLister(text);
        if (!IsPositionInTag(text, lister, caret))
        {
            return FormatEdit.Unchanged(text, caret, caret);
        }

        int i = lister.FindFirstTagOnOrAfter(caret);
        TagLister.TagInfo ti = lister.At(i);
        if (caret < ti.Pos || caret >= ti.Pos + ti.Len)
        {
            return FormatEdit.Unchanged(text, caret, caret);
        }

        if (ti.TagName is "body" or "html")
        {
            return FormatEdit.Unchanged(text, caret, caret);
        }

        int newpos;
        int openPos;
        int openLen;
        int closePos = -1;
        int closeLen = -1;

        if (ti.Kind == TagKind.End)
        {
            newpos = ti.Pos - ti.OpenLen;
            openPos = ti.OpenPos;
            openLen = ti.OpenLen;
            closePos = ti.Pos;
            closeLen = ti.Len;
        }
        else
        {
            newpos = ti.Pos;
            openPos = ti.Pos;
            openLen = ti.Len;
        }

        if (ti.Kind == TagKind.Begin)
        {
            int j = lister.FindCloseTagForOpen(i);
            if (j >= 0 && lister.At(j).Len != -1)
            {
                closePos = lister.At(j).Pos;
                closeLen = lister.At(j).Len;
            }
        }

        if (openLen == -1 && closeLen == -1)
        {
            return FormatEdit.Unchanged(text, caret, caret);
        }

        string newText = text;
        if (closeLen != -1)
        {
            newText = newText[..closePos] + newText[(closePos + closeLen)..];
        }

        if (openLen != -1)
        {
            newText = newText[..openPos] + newText[(openPos + openLen)..];
        }

        int newCaret = Math.Clamp(newpos, 0, newText.Length);
        return new FormatEdit(newText, newCaret, newCaret, !string.Equals(newText, text, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------
    // Insert Closing Tag.
    // ---------------------------------------------------------------------

    /// <summary>Whether "Insert Closing Tag" is allowed.</summary>
    public static bool InsertClosingTagAllowed(string text, int caretPosition)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (caretPosition < 0 || caretPosition > text.Length)
        {
            return false;
        }

        var lister = new TagLister(text);
        if (IsPositionInTag(text, lister, caretPosition))
        {
            return caretPosition < text.Length && text[caretPosition] == '<';
        }

        return true;
    }

    /// <summary>
    /// Inserts a closing tag for the nearest unclosed opening tag
    /// in the current block. Returns a <see cref="FormatEdit"/> with the
    /// inserted text or unchanged (with a message in <see cref="FormatEdit.StatusMessage"/>).
    /// </summary>
    public static FormatEdit InsertClosingTag(string text, int caretPosition)
    {
        ArgumentNullException.ThrowIfNull(text);
        int caret = Math.Clamp(caretPosition, 0, text.Length);

        if (!InsertClosingTagAllowed(text, caret))
        {
            return FormatEdit.Unchanged(text, caret, caret) with
            {
                StatusMessage = CoreStrings.Get("Status_CannotInsertClosingTagHere"),
            };
        }

        var lister = new TagLister(text);
        List<string> unmatched = UnmatchedTagsForBlock(text, lister, caret);
        if (unmatched.Count == 0)
        {
            return FormatEdit.Unchanged(text, caret, caret) with
            {
                StatusMessage = CoreStrings.Get("Status_NoOpenTagsHere"),
            };
        }

        Match m = TagNameSearch.Match(unmatched[^1]);
        if (!m.Success)
        {
            return FormatEdit.Unchanged(text, caret, caret);
        }

        string closingTag = "</" + m.Groups[1].Value + ">";
        string newText = text[..caret] + closingTag + text[caret..];
        return new FormatEdit(newText, caret + closingTag.Length, caret + closingTag.Length, true);
    }

    // ---------------------------------------------------------------------
    // Heading state — which entry is selected on the heading toolbar.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The name of the deepest block element enclosing the caret (<c>h1</c>–<c>h6</c>/<c>p</c>/…)
    /// or an empty string. Drives which
    /// "Heading N" / "Normal" entry is selected in the Format menu.
    /// </summary>
    public static string CaretBlockElementName(string text, int caretPosition)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        int caret = Math.Clamp(caretPosition, 0, text.Length);
        var lister = new TagLister(text);
        string path = lister.GeneratePathToTag(caret);
        string name = string.Empty;
        foreach (string node in path.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string tag = node.Trim().Split(' ')[0];
            if (BlockLevelTags.Contains(tag))
            {
                name = tag;
            }
        }

        return name;
    }

    // ---------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------

    internal static FormatEdit InsertTagAroundSelection(
        string text, int selectionStart, int selectionEnd, string leftElementName, string rightElementName, string attributes)
    {
        string newAttributes = attributes.Length > 0 ? " " + attributes : string.Empty;
        string selectedText = text.Substring(selectionStart, selectionEnd - selectionStart);
        string prefix = "<" + leftElementName + newAttributes + ">";
        string replacement = prefix + selectedText + "<" + rightElementName + ">";
        string newText = text[..selectionStart] + replacement + text[selectionEnd..];
        int newSelStart = selectionStart + prefix.Length;
        int newSelEnd = newSelStart + selectedText.Length;
        return new FormatEdit(newText, newSelStart, newSelEnd, !string.Equals(newText, text, StringComparison.Ordinal));
    }

    private static FormatEdit ReplaceSelection(
        string text, int selectionStart, int selectionEnd, string selectedText, string newSelectedText)
    {
        if (string.Equals(newSelectedText, selectedText, StringComparison.Ordinal))
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        string newText = text[..selectionStart] + newSelectedText + text[selectionEnd..];
        return new FormatEdit(newText, selectionStart, selectionStart + newSelectedText.Length, true);
    }

    // Formats a CSS style (simplified: the {} block on/around the caret's line).
    private static FormatEdit FormatCssStyleOnLine(string text, int caretPosition, string property, string value)
    {
        int pos = Math.Clamp(caretPosition, 0, text.Length);
        int bracketEnd = text.IndexOf('}', pos);
        if (bracketEnd < 0)
        {
            return FormatEdit.Unchanged(text, pos, pos);
        }

        int bracketStart = LastIndexOf(text, '{', pos);
        int prevClose = LastIndexOf(text, '}', pos - 1);
        if (bracketStart < 0 || prevClose > bracketStart)
        {
            (int lineStart, int lineEnd) = LineBounds(text, pos);
            int onLine = text.IndexOf('{', lineStart);
            if (onLine >= 0 && onLine < lineEnd)
            {
                bracketStart = onLine;
            }
            else
            {
                return FormatEdit.Unchanged(text, pos, pos);
            }
        }

        if (bracketStart > bracketEnd)
        {
            return FormatEdit.Unchanged(text, pos, pos);
        }

        var props = new List<CssProperty>(HtmlStyleInfo.GetCssProperties(text, bracketStart + 1, bracketEnd));
        ApplyChangeToProperties(props, property, value);

        (int blockStart, int blockEnd) = LineBounds(text, pos);
        bool singleLine = blockStart < bracketStart && bracketEnd <= blockEnd;
        string styleText = HtmlStyleInfo.FormatCssProperties(props, !singleLine);

        string newText = text[..(bracketStart + 1)] + styleText + text[bracketEnd..];
        return new FormatEdit(newText, bracketStart, bracketStart, !string.Equals(newText, text, StringComparison.Ordinal));
    }

    // Applies a change to the property list.
    private static void ApplyChangeToProperties(List<CssProperty> cssProperties, string propertyName, string propertyValue)
    {
        bool hasProperty = false;
        for (int i = cssProperties.Count - 1; i >= 0; i--)
        {
            CssProperty p = cssProperties[i];
            if (string.Equals(p.Name.ToLower(CultureInfo.InvariantCulture), propertyName, StringComparison.Ordinal))
            {
                hasProperty = true;
                if (string.Equals((p.Value ?? string.Empty).ToLower(CultureInfo.InvariantCulture), propertyValue, StringComparison.Ordinal))
                {
                    cssProperties.RemoveAt(i);
                }
                else
                {
                    cssProperties[i] = p with { Value = propertyValue };
                }
            }
        }

        if (!hasProperty)
        {
            cssProperties.Add(new CssProperty(propertyName, propertyValue));
        }
    }

    // Gets/sets an attribute on the nearest element from the tag list.
    // Returns the existing attribute value (get) or null; on set it fills in newText/caret.
    internal static string? ProcessAttribute(
        string text,
        TagLister lister,
        int pos,
        int originalPosition,
        string attributeName,
        HashSet<string> tagList,
        string? attributeValue,
        bool setAttr,
        out string newText,
        out int caret)
    {
        newText = text;
        caret = originalPosition;

        if (pos < 0)
        {
            return null;
        }

        if (lister.IsPositionInCloseTag(pos))
        {
            while (pos > 0 && text[pos] != '<')
            {
                pos--;
            }

            if (pos > 0)
            {
                pos--;
            }
        }

        int i = lister.FindLastTagOnOrBefore(pos);
        TagLister.TagInfo ti = lister.At(i);
        var pairedTags = new List<int>();
        while (i >= 0 && lister.At(i).TagName != "body")
        {
            ti = lister.At(i);
            if (ti.Kind == TagKind.End)
            {
                if (!BlockLevelTags.Contains(ti.TagName))
                {
                    pairedTags.Add(ti.OpenPos);
                }

                if (tagList.Contains(ti.TagName) || BlockLevelTags.Contains(ti.TagName))
                {
                    return null;
                }
            }
            else if (ti.Kind is TagKind.Begin or TagKind.SelfClosing)
            {
                if (pairedTags.Contains(ti.Pos))
                {
                    pairedTags.Remove(ti.Pos);
                }
                else if (tagList.Contains(ti.TagName) || BlockLevelTags.Contains(ti.TagName))
                {
                    break;
                }
            }

            i--;
        }

        if (i < 0 || !tagList.Contains(ti.TagName) || ti.TagName == "body")
        {
            return null;
        }

        string openingTagText = text.Substring(ti.Pos, ti.Len);
        TagLister.AttInfo ainfo = TagLister.ParseAttribute(openingTagText, attributeName);

        int attributeStart = ti.Pos + ti.Len - 1;
        if (ti.Kind == TagKind.SelfClosing)
        {
            attributeStart--;
        }

        int attributeEnd = attributeStart;
        if (ainfo.Pos != -1)
        {
            attributeStart = ti.Pos + ainfo.Pos - 1;
            attributeEnd = attributeStart + 1 + ainfo.Len;
        }

        if (!setAttr)
        {
            return ainfo.Pos == -1 ? null : ainfo.AValue;
        }

        if (ainfo.Pos == -1 && string.IsNullOrEmpty(attributeValue))
        {
            return null;
        }

        string attributeText = string.IsNullOrEmpty(attributeValue)
            ? string.Empty
            : " " + TagLister.SerializeAttribute(attributeName, attributeValue!);

        newText = text[..attributeStart] + attributeText + text[attributeEnd..];
        caret = ti.Pos + ti.Len - (attributeEnd - attributeStart) + attributeText.Length;
        caret = Math.Clamp(caret, 0, newText.Length);
        return attributeValue;
    }

    // Finds the unmatched tags for a block.
    private static List<string> UnmatchedTagsForBlock(string text, TagLister lister, int pos)
    {
        var openingTags = new List<string>();
        var pairedTags = new List<int>();
        int i = lister.FindFirstTagOnOrAfter(pos) - 1;
        if (i < 0)
        {
            return openingTags;
        }

        while (i >= 0 && lister.At(i).TagName != "body")
        {
            TagLister.TagInfo ti = lister.At(i);
            if (ti.Kind == TagKind.End)
            {
                pairedTags.Add(ti.OpenPos);
            }
            else if (ti.Kind == TagKind.Begin)
            {
                if (pairedTags.Contains(ti.Pos))
                {
                    pairedTags.Remove(ti.Pos);
                }
                else
                {
                    openingTags.Insert(0, text.Substring(ti.Pos, ti.Len));
                }
            }

            i--;
        }

        return openingTags;
    }

    // Whether a position is inside a tag (a char-scan + TagLister version).
    internal static bool IsPositionInTag(string text, TagLister lister, int pos)
    {
        if (pos < 0 || pos > text.Length)
        {
            return false;
        }

        int pb = LastIndexOf(text, '<', pos);
        int prevGt = LastIndexOf(text, '>', pos - 1);
        int ne = IndexOfFrom(text, '>', pos);
        int nb = IndexOfFrom(text, '<', pos + 1);
        if (nb == -1)
        {
            nb = text.Length + 1;
        }

        if (pb > prevGt && ne >= pos && nb > ne)
        {
            return lister.IsPositionInTag(pos);
        }

        return false;
    }

    private static string StripTags(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool inTag = false;
        foreach (char c in text)
        {
            if (!inTag && c != '<')
            {
                sb.Append(c);
            }

            if (c == '<')
            {
                inTag = true;
            }

            if (inTag && c == '>')
            {
                inTag = false;
            }
        }

        return sb.ToString();
    }

    private static string RemoveFirstTag(string text, string tagname)
    {
        int p = text.IndexOf('>', StringComparison.Ordinal);
        if (p > -1)
        {
            string tag = text[..(p + 1)];
            if (tag.Contains(tagname, StringComparison.Ordinal))
            {
                return text[(p + 1)..];
            }
        }

        return text;
    }

    private static string RemoveLastTag(string text, string tagname)
    {
        int p = text.LastIndexOf('<');
        if (p > -1)
        {
            string tag = text[p..];
            if (tag.Contains(tagname, StringComparison.Ordinal))
            {
                return text[..p];
            }
        }

        return text;
    }

    private static string LeadingIndent(string text)
    {
        Match m = StartingIndentUsed.Match(text);
        return m.Success ? m.Groups[1].Value.Replace("\t", "    ", StringComparison.Ordinal) : string.Empty;
    }

    private static string OpeningTagNameAtStart(string text)
    {
        Match m = OpenTagStartsSelection.Match(text);
        return m.Success ? m.Groups[2].Value : string.Empty;
    }

    private static IEnumerable<string> SplitNonEmptyLines(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            if (line.Trim().Length > 0)
            {
                yield return line;
            }
        }
    }

    private static bool IsFragmentBalanced(string fragment)
    {
        int depth = 0;
        foreach (char c in fragment)
        {
            if (c == '<')
            {
                depth++;
            }
            else if (c == '>')
            {
                depth--;
                if (depth < 0)
                {
                    return false;
                }
            }
        }

        if (depth != 0)
        {
            return false;
        }

        return new TagLister("<x>" + fragment + "</x>").FindFirstNestingError() is null;
    }

    // The word under the cursor (letters/digits on both sides of the position).
    private static (int Start, int End) WordUnder(string text, int pos)
    {
        int clamped = Math.Clamp(pos, 0, text.Length);
        int start = clamped;
        while (start > 0 && IsWordChar(text[start - 1]))
        {
            start--;
        }

        int end = clamped;
        while (end < text.Length && IsWordChar(text[end]))
        {
            end++;
        }

        return (start, end);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // The first part of block formatting (no selection => the line, trimming the outermost tags).
    private static (int Start, int End) DefaultLineSelection(string text, int pos)
    {
        (int lineStart, int lineEnd) = LineBounds(text, pos);
        string lineText = text[lineStart..lineEnd];
        string clean = lineText.Trim();

        if (clean.StartsWith('<'))
        {
            int p = clean.IndexOf('>', StringComparison.Ordinal);
            if (p > -1)
            {
                clean = clean[(p + 1)..];
            }
        }

        if (clean.EndsWith('>'))
        {
            int p = clean.LastIndexOf('<');
            if (p > -1)
            {
                clean = clean[..p];
            }
        }

        if (clean.Length == 0)
        {
            return (lineStart, lineEnd);
        }

        int offset = lineText.IndexOf(clean, StringComparison.Ordinal);
        int start = offset > 0 ? lineStart + offset : lineStart;
        return (start, start + clean.Length);
    }

    private static (int Start, int End) LineBounds(string text, int pos)
    {
        int clamped = Math.Clamp(pos, 0, text.Length);
        int prevNewline = clamped > 0 ? text.LastIndexOf('\n', Math.Min(clamped - 1, text.Length - 1)) : -1;
        int start = prevNewline < 0 ? 0 : prevNewline + 1;
        int end = IndexOfFrom(text, '\n', clamped);
        if (end < 0)
        {
            end = text.Length;
        }

        if (start > end)
        {
            start = end;
        }

        return (start, end);
    }

    private static int LastIndexOf(string text, char value, int startIndex)
    {
        if (startIndex < 0 || text.Length == 0)
        {
            return -1;
        }

        return text.LastIndexOf(value, Math.Min(startIndex, text.Length - 1));
    }

    private static int IndexOfFrom(string text, char value, int startIndex)
    {
        if (startIndex < 0)
        {
            startIndex = 0;
        }

        return startIndex >= text.Length ? -1 : text.IndexOf(value, startIndex);
    }

    internal static void Normalize(string text, ref int start, ref int end)
    {
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        if (start > end)
        {
            (start, end) = (end, start);
        }
    }
}

/// <summary>
/// The result of a formatting operation from <see cref="CodeFormatOperations"/>: the new content, the new selection
/// (0-based, <c>SelectionStart == SelectionEnd</c> = just the caret), a changed flag and an optional
/// status bar message (e.g. for "Insert Closing Tag" with no open tags).
/// </summary>
/// <param name="Text">The new document content.</param>
/// <param name="SelectionStart">The start of the new selection / the caret position.</param>
/// <param name="SelectionEnd">The end of the new selection.</param>
/// <param name="Changed">Whether the content actually changed.</param>
/// <param name="StatusMessage">An optional message for the user.</param>
public readonly record struct FormatEdit(
    string Text,
    int SelectionStart,
    int SelectionEnd,
    bool Changed,
    string? StatusMessage = null)
{
    /// <summary>An "unchanged" result — keeps the content and the given selection.</summary>
    public static FormatEdit Unchanged(string text, int selectionStart, int selectionEnd) =>
        new(text, selectionStart, selectionEnd, false);
}
