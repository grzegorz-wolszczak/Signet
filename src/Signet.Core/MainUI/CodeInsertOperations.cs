using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Signet.Core.Localization;

namespace Signet.Core.MainUI;

/// <summary>
/// View-independent implementation of the "Insert" menu operations of the code editor:
/// inserting text (a special character / a clip), inserting a file (<c>&lt;img&gt;</c>/<c>&lt;audio&gt;</c>/<c>&lt;video&gt;</c>/
/// <c>&lt;a&gt;</c>), "Insert ID" (the <c>id</c> attribute on <c>&lt;a&gt;</c>) and "Insert Hyperlink"
/// (the <c>href</c> attribute on <c>&lt;a&gt;</c>).
/// </summary>
/// <remarks>
/// The same convention as <see cref="CodeFormatOperations"/>: a plain source string plus a selection
/// range <c>[selectionStart, selectionEnd)</c>, with the result as a <see cref="FormatEdit"/>. The positional
/// logic uses <see cref="TagLister"/> and the shared helpers from <see cref="CodeFormatOperations"/>.
/// </remarks>
public static class CodeInsertOperations
{
    // The tags that can serve as an anchor.
    private static readonly HashSet<string> AnchorTags = new(StringComparer.Ordinal) { "a" };

    // Matches an invalid id (used by Insert ID).
    private static readonly Regex InvalidIdChar =
        new(@"(^[^A-Za-z]|[^A-Za-z0-9_:.\-])", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Inserts <paramref name="insert"/> at the selection (replacing it). Used by "Insert Special Character"
    /// and (on the App side) by "Insert Clip".
    /// </summary>
    public static FormatEdit InsertText(string text, int selectionStart, int selectionEnd, string insert)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(insert);
        CodeFormatOperations.Normalize(text, ref selectionStart, ref selectionEnd);

        string newText = text[..selectionStart] + insert + text[selectionEnd..];
        int caret = selectionStart + insert.Length;
        return new FormatEdit(newText, caret, caret, !string.Equals(newText, text, StringComparison.Ordinal));
    }

    /// <summary>
    /// Pastes the content of a clip. With no selection the
    /// <c>\1</c> placeholder is removed and the rest of the text is inserted at the caret (which lets
    /// the same clip be used for both inserting and wrapping). With a selection, <c>\1</c> is
    /// replaced by the selected text and the result replaces the selection. Returns "unchanged" when
    /// <paramref name="clipText"/> is empty.
    /// </summary>
    public static FormatEdit PasteClipText(string text, int selectionStart, int selectionEnd, string clipText)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(clipText);
        CodeFormatOperations.Normalize(text, ref selectionStart, ref selectionEnd);

        if (clipText.Length == 0)
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
        }

        string insert = selectionStart == selectionEnd
            ? clipText.Replace("\\1", string.Empty, StringComparison.Ordinal)
            : clipText.Replace("\\1", text[selectionStart..selectionEnd], StringComparison.Ordinal);

        return InsertText(text, selectionStart, selectionEnd, insert);
    }

    /// <summary>Whether "Insert File" is allowed at the given position.</summary>
    public static bool InsertFileAllowed(string text, int caretPosition)
    {
        ArgumentNullException.ThrowIfNull(text);
        int pos = Math.Clamp(caretPosition, 0, text.Length);
        var lister = new TagLister(text);
        if (!lister.IsPositionInBody(pos))
        {
            return false;
        }

        if (CodeFormatOperations.IsPositionInTag(text, lister, pos))
        {
            return pos < text.Length && text[pos] == '<';
        }

        return true;
    }

    /// <summary>
    /// Inserts a ready HTML fragment (e.g. <c>&lt;img .../&gt;</c>) at the caret, provided the position
    /// allows it. When the position is not allowed, it returns "unchanged"
    /// with a message.
    /// </summary>
    public static FormatEdit InsertFileFragment(string text, int selectionStart, int selectionEnd, string html)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(html);
        CodeFormatOperations.Normalize(text, ref selectionStart, ref selectionEnd);

        if (!InsertFileAllowed(text, selectionStart))
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd) with
            {
                StatusMessage = CoreStrings.Get("Status_CannotInsertFileHere"),
            };
        }

        return InsertText(text, selectionStart, selectionEnd, html);
    }

    /// <summary>
    /// Builds the HTML fragment for the inserted resource:
    /// image/SVG &#8594; <c>&lt;img&gt;</c>, video &#8594; <c>&lt;video&gt;</c>, audio &#8594;
    /// <c>&lt;audio&gt;</c>, any other file &#8594; <c>&lt;a href&gt;</c>.
    /// </summary>
    /// <param name="type">The resource type.</param>
    /// <param name="encodedRelativePath">The relative path, already URL-encoded.</param>
    /// <param name="label">The label (the file name without the extension) — <c>alt</c> / the element content.</param>
    public static string BuildFileFragment(ResourceType type, string encodedRelativePath, string label)
    {
        ArgumentNullException.ThrowIfNull(encodedRelativePath);
        ArgumentNullException.ThrowIfNull(label);

        return type switch
        {
            ResourceType.Image or ResourceType.Svg =>
                $"<img alt=\"{label}\" src=\"{encodedRelativePath}\"/>",
            ResourceType.Video =>
                $"<video controls=\"controls\" src=\"{encodedRelativePath}\">{label}</video>",
            ResourceType.Audio =>
                $"<audio controls=\"controls\" src=\"{encodedRelativePath}\">{label}</audio>",
            _ =>
                $"<a href=\"{encodedRelativePath}\">{label}</a>",
        };
    }

    /// <summary>Whether "Insert ID" is allowed at the given position.</summary>
    public static bool InsertIdAllowed(string text, int caretPosition) =>
        AnchorAttributeAllowed(text, caretPosition, openTagList: CodeFormatOperations.IdTags);

    /// <summary>Whether "Insert Hyperlink" is allowed at the given position.</summary>
    public static bool InsertHyperlinkAllowed(string text, int caretPosition) =>
        AnchorAttributeAllowed(text, caretPosition, openTagList: AnchorTags);

    // The shared skeleton of IsInsertIdAllowed / IsInsertHyperlinkAllowed (they differ only in the list
    // of allowed opening tags).
    private static bool AnchorAttributeAllowed(string text, int caretPosition, HashSet<string> openTagList)
    {
        ArgumentNullException.ThrowIfNull(text);
        int pos = Math.Clamp(caretPosition, 0, text.Length);
        var lister = new TagLister(text);
        if (!lister.IsPositionInBody(pos))
        {
            return false;
        }

        string closingTagName = GetClosingTagName(lister, pos);
        if (closingTagName.Length > 0 && pos < text.Length && text[pos] == '<')
        {
            closingTagName = string.Empty;
        }

        if (closingTagName.Length > 0 && !AnchorTags.Contains(closingTagName))
        {
            return false;
        }

        string tagName = GetOpeningTagName(lister, pos);
        if (tagName.Length > 0 && !openTagList.Contains(tagName))
        {
            return false;
        }

        return true;
    }

    /// <summary>The current value of the <c>id</c> attribute under the caret (to prefill the dialog).</summary>
    public static string CurrentIdValue(string text, int caretPosition)
    {
        ArgumentNullException.ThrowIfNull(text);
        int pos = Math.Clamp(caretPosition, 0, text.Length);
        var lister = new TagLister(text);
        HashSet<string> tagList = GetOpeningTagName(lister, pos).Length == 0 ? AnchorTags : CodeFormatOperations.IdTags;
        return CodeFormatOperations.ProcessAttribute(
            text, lister, pos, pos, "id", tagList, null, setAttr: false, out _, out _) ?? string.Empty;
    }

    /// <summary>The current value of the <c>href</c> attribute under the caret (to prefill the dialog).</summary>
    public static string CurrentHrefValue(string text, int caretPosition)
    {
        ArgumentNullException.ThrowIfNull(text);
        int pos = Math.Clamp(caretPosition, 0, text.Length);
        var lister = new TagLister(text);
        return CodeFormatOperations.ProcessAttribute(
            text, lister, pos, pos, "href", AnchorTags, null, setAttr: false, out _, out _) ?? string.Empty;
    }

    /// <summary>Whether <paramref name="id"/> is a valid XML identifier.</summary>
    public static bool IsValidId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.Length > 0 && !InvalidIdChar.IsMatch(id);
    }

    /// <summary>
    /// Adds/updates the <c>id</c> attribute on an <c>&lt;a&gt;</c> element (or creates a new
    /// <c>&lt;a id="…"&gt;&lt;/a&gt;</c> / wraps the selection).
    /// </summary>
    public static FormatEdit InsertId(string text, int selectionStart, int selectionEnd, string idValue)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(idValue);
        CodeFormatOperations.Normalize(text, ref selectionStart, ref selectionEnd);

        var lister = new TagLister(text);
        HashSet<string> tagList =
            GetOpeningTagName(lister, selectionStart).Length == 0 ? AnchorTags : CodeFormatOperations.IdTags;

        return InsertTagAttribute(text, lister, selectionStart, selectionEnd, "a", "id", idValue, tagList);
    }

    /// <summary>
    /// Adds/updates the <c>href</c> attribute on an <c>&lt;a&gt;</c> element (or creates a new
    /// <c>&lt;a href="…"&gt;&lt;/a&gt;</c> / wraps the selection).
    /// The value is made "HTML-safe" (escaping <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>).
    /// </summary>
    public static FormatEdit InsertHyperlink(string text, int selectionStart, int selectionEnd, string hrefValue)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(hrefValue);
        CodeFormatOperations.Normalize(text, ref selectionStart, ref selectionEnd);

        var lister = new TagLister(text);
        return InsertTagAttribute(text, lister, selectionStart, selectionEnd, "a", "href", HtmlSafe(hrefValue), AnchorTags);
    }

    // Inserts/sets a tag attribute.
    private static FormatEdit InsertTagAttribute(
        string text,
        TagLister lister,
        int selectionStart,
        int selectionEnd,
        string elementName,
        string attributeName,
        string attributeValue,
        HashSet<string> tagList)
    {
        // 1. Try to set the attribute on an existing opening tag.
        string? applied = CodeFormatOperations.ProcessAttribute(
            text, lister, selectionStart, selectionEnd, attributeName, tagList, attributeValue,
            setAttr: true, out string newText, out int caret);

        if (applied is not null)
        {
            return new FormatEdit(newText, caret, caret, !string.Equals(newText, text, StringComparison.Ordinal));
        }

        // 2. Nothing was set — insert a new tag (unless we are in the middle of a tag).
        string attributes = attributeName + "=\"" + attributeValue + "\"";
        bool inTagNotBefore = CodeFormatOperations.IsPositionInTag(text, lister, selectionEnd)
            && selectionEnd < text.Length && text[selectionEnd] != '<';

        if (selectionStart == selectionEnd && !inTagNotBefore)
        {
            return InsertTagAroundCaret(text, selectionEnd, elementName, attributes);
        }

        if (CodeFormatOperations.RemoveFormattingAllowed(text, selectionStart, selectionEnd))
        {
            return CodeFormatOperations.InsertTagAroundSelection(
                text, selectionStart, selectionEnd, elementName, "/" + elementName, attributes);
        }

        return FormatEdit.Unchanged(text, selectionStart, selectionEnd);
    }

    // Inserts an HTML tag around text, with empty text between the tags.
    private static FormatEdit InsertTagAroundCaret(string text, int caret, string elementName, string attributes)
    {
        string a = string.IsNullOrEmpty(attributes) ? string.Empty : " " + attributes;
        string prefix = "<" + elementName + a + ">";
        string inserted = prefix + "</" + elementName + ">";
        string newText = text[..caret] + inserted + text[caret..];
        int selection = caret + prefix.Length;
        return new FormatEdit(newText, selection, selection, true);
    }

    // Gets the opening tag name.
    private static string GetOpeningTagName(TagLister lister, int pos)
    {
        int i = lister.FindFirstTagOnOrAfter(pos);
        TagLister.TagInfo ti = lister.At(i);
        if (pos >= ti.Pos && pos < ti.Pos + ti.Len && ti.Kind is TagKind.Begin or TagKind.SelfClosing)
        {
            return ti.TagName.ToLowerInvariant();
        }

        return string.Empty;
    }

    // Gets the closing tag name.
    private static string GetClosingTagName(TagLister lister, int pos)
    {
        int i = lister.FindFirstTagOnOrAfter(pos);
        TagLister.TagInfo ti = lister.At(i);
        if (pos >= ti.Pos && pos < ti.Pos + ti.Len && ti.Kind == TagKind.End)
        {
            return ti.TagName.ToLowerInvariant();
        }

        return string.Empty;
    }

    // The chain of replacements used by Insert Hyperlink (the order matters).
    private static string HtmlSafe(string value) => value
        .Replace("&amp;", "&", StringComparison.Ordinal)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
