using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Signet.Core.Themes;

/// <summary>
/// The registry of the theme keys: every color and look of text a theme sets, in the order of the trees of the theme
/// editor. The defaults are the colors of Signet Light (before themes they were the settings or fixed in the code).
/// </summary>
public static class ThemeKeys
{
    // ---- General: the editor ----

    /// <summary>The text and the background of the editor.</summary>
    public static readonly ThemeKey EditorText = Attributes("editor.text", ThemePage.General, "Editor",
        new TextAttributes { Foreground = Rgb(0x000000), Background = Rgb(0xFFFFFF) });

    /// <summary>The background of the line with the caret.</summary>
    public static readonly ThemeKey EditorCurrentLine = Color("editor.currentLine", ThemePage.General, "Editor", Rgb(0xFFFFBF));

    /// <summary>The line numbers.</summary>
    public static readonly ThemeKey EditorLineNumbers = Attributes("editor.lineNumbers", ThemePage.General, "Editor",
        new TextAttributes { Foreground = Rgb(0x808080) });

    /// <summary>The selected text.</summary>
    public static readonly ThemeKey EditorSelection = Attributes("editor.selection", ThemePage.General, "Editor",
        new TextAttributes { Background = Rgb(0xADD6FF) });

    /// <summary>The pair of tags around the caret.</summary>
    public static readonly ThemeKey EditorTagPair = Attributes("editor.tagPair", ThemePage.General, "Editor",
        new TextAttributes { Background = Argb(0x40808080) });

    /// <summary>The matches of Find &amp; Replace.</summary>
    public static readonly ThemeKey SearchMatch = Attributes("search.match", ThemePage.General, "Search",
        new TextAttributes { Background = Rgb(0xFFF0C4) });

    /// <summary>The search results marked in the text ("Mark").</summary>
    public static readonly ThemeKey SearchMarked = Attributes("search.marked", ThemePage.General, "Search",
        new TextAttributes { Background = Argb(0x184D9BFF), Effect = EffectType.Box, EffectColor = Argb(0x904D9BFF) });

    /// <summary>The place a document stops being well-formed.</summary>
    public static readonly ThemeKey ErrorsWellFormed = Attributes("errors.wellFormed", ThemePage.General, "Errors",
        new TextAttributes { Effect = EffectType.Wave, EffectColor = Rgb(0xE01B1B) });

    /// <summary>A warning about the well-formedness (e.g. a missing DOCTYPE).</summary>
    public static readonly ThemeKey ErrorsWellFormedWarning = Attributes("errors.wellFormedWarning", ThemePage.General, "Errors",
        new TextAttributes { Effect = EffectType.Wave, EffectColor = Rgb(0xE6A800) });

    /// <summary>A syntax error found by the highlighting (e.g. an unclosed comment, a broken link).</summary>
    public static readonly ThemeKey ErrorsSyntax = Attributes("errors.syntax", ThemePage.General, "Errors",
        new TextAttributes { Effect = EffectType.Wave, EffectColor = Rgb(0xFF0000) });

    /// <summary>A misspelled word.</summary>
    public static readonly ThemeKey ErrorsSpelling = Attributes("errors.spelling", ThemePage.General, "Errors",
        new TextAttributes { Effect = EffectType.Wave, EffectColor = Rgb(0xE01B1B) });

    /// <summary>The hint with the opening tag of the closing tag under the mouse.</summary>
    public static readonly ThemeKey HintsOpenTag = Attributes("hints.openTag", ThemePage.General, "Hints",
        new TextAttributes { Foreground = Rgb(0x000000), Background = Rgb(0xF2F2F2) });

    // ---- XHTML ----

    /// <summary>The DOCTYPE declaration.</summary>
    public static readonly ThemeKey XhtmlDoctype = Attributes("xhtml.doctype", ThemePage.Xhtml, "Markup", Fg(0x000080));

    /// <summary>A tag.</summary>
    public static readonly ThemeKey XhtmlTag = Attributes("xhtml.tag", ThemePage.Xhtml, "Markup", Fg(0x0000FF));

    /// <summary>The namespace prefix of a name (<c>svg:</c>, <c>epub:</c>).</summary>
    public static readonly ThemeKey XhtmlNamespacePrefix = Attributes("xhtml.namespacePrefix", ThemePage.Xhtml, "Markup",
        Fg(0x7B5694), baseId: "xhtml.tag");

    /// <summary>The name of an attribute.</summary>
    public static readonly ThemeKey XhtmlAttributeName = Attributes("xhtml.attribute.name", ThemePage.Xhtml, "Markup", Fg(0x800000));

    /// <summary>The value of an attribute.</summary>
    public static readonly ThemeKey XhtmlAttributeValue = Attributes("xhtml.attribute.value", ThemePage.Xhtml, "Markup", Fg(0x008080));

    /// <summary>An entity or a character reference.</summary>
    public static readonly ThemeKey XhtmlEntity = Attributes("xhtml.entity", ThemePage.Xhtml, "Markup", Fg(0x800080));

    /// <summary>A special space (e.g. a no-break space) written as a character.</summary>
    public static readonly ThemeKey XhtmlSpecialSpace = Attributes("xhtml.specialSpace", ThemePage.Xhtml, "Markup",
        new TextAttributes { Effect = EffectType.DashedUnderline, EffectColor = Rgb(0x800080) });

    /// <summary>A comment.</summary>
    public static readonly ThemeKey XhtmlComment = Attributes("xhtml.comment", ThemePage.Xhtml, "Markup", Fg(0x008000));

    /// <summary>A link target (<c>href</c>, <c>src</c>, <c>url()</c>).</summary>
    public static readonly ThemeKey Link = Attributes("link", ThemePage.Xhtml, "Links", Fg(0x0645AD));

    /// <summary>A link target that does not exist in the book.</summary>
    public static readonly ThemeKey LinkBroken = Attributes("link.broken", ThemePage.Xhtml, "Links", Fg(0x0645AD), baseId: "link");

    /// <summary>The text of a <c>&lt;b&gt;</c> / <c>&lt;strong&gt;</c> element.</summary>
    public static readonly ThemeKey XhtmlTextBold = Attributes("xhtml.text.bold", ThemePage.Xhtml, "Text",
        new TextAttributes { Bold = true });

    /// <summary>The text of an <c>&lt;i&gt;</c> / <c>&lt;em&gt;</c> element.</summary>
    public static readonly ThemeKey XhtmlTextItalic = Attributes("xhtml.text.italic", ThemePage.Xhtml, "Text",
        new TextAttributes { Italic = true });

    /// <summary>The text that is both bold and italic.</summary>
    public static readonly ThemeKey XhtmlTextBoldItalic = Attributes("xhtml.text.boldItalic", ThemePage.Xhtml, "Text",
        new TextAttributes { Bold = true, Italic = true });

    /// <summary>CSS inside the document (a <c>&lt;style&gt;</c> block, a <c>style</c> attribute) without the extended highlighting.</summary>
    public static readonly ThemeKey XhtmlCss = Attributes("xhtml.css", ThemePage.Xhtml, "Embedded CSS", Fg(0x808000), baseId: "css.value");

    /// <summary>A CSS comment inside the document.</summary>
    public static readonly ThemeKey XhtmlCssComment = Attributes("xhtml.css.comment", ThemePage.Xhtml, "Embedded CSS",
        Fg(0x008000), baseId: "css.comment");

    // ---- CSS ----

    /// <summary>A selector.</summary>
    public static readonly ThemeKey CssSelector = Attributes("css.selector", ThemePage.Css, "Selectors", Fg(0x800000));

    /// <summary>A class, id or pseudo-class in a selector.</summary>
    public static readonly ThemeKey CssSpecialSelector = Attributes("css.specialSelector", ThemePage.Css, "Selectors",
        Fg(0xAA5500), baseId: "css.selector");

    /// <summary>A property name.</summary>
    public static readonly ThemeKey CssProperty = Attributes("css.property", ThemePage.Css, "Declarations", Fg(0x000080));

    /// <summary>A property value.</summary>
    public static readonly ThemeKey CssValue = Attributes("css.value", ThemePage.Css, "Declarations", Fg(0x000000));

    /// <summary>A constant: a number, a dimension, a color.</summary>
    public static readonly ThemeKey CssConstant = Attributes("css.constant", ThemePage.Css, "Declarations",
        Fg(0x098658), baseId: "css.value");

    /// <summary>A quoted string.</summary>
    public static readonly ThemeKey CssString = Attributes("css.string", ThemePage.Css, "Declarations", Fg(0x800080), baseId: "css.value");

    /// <summary>An <c>@</c> rule and <c>!important</c>.</summary>
    public static readonly ThemeKey CssAtRule = Attributes("css.atRule", ThemePage.Css, "Rules", Fg(0x8B008B));

    /// <summary>A comment.</summary>
    public static readonly ThemeKey CssComment = Attributes("css.comment", ThemePage.Css, "Rules", Fg(0x008000));

    // ---- Diff ----

    /// <summary>A deleted line.</summary>
    public static readonly ThemeKey DiffDeletedLine = Attributes("diff.deletedLine", ThemePage.Diff, "Lines",
        new TextAttributes { Background = Argb(0x38E53935) });

    /// <summary>An inserted line.</summary>
    public static readonly ThemeKey DiffInsertedLine = Attributes("diff.insertedLine", ThemePage.Diff, "Lines",
        new TextAttributes { Background = Argb(0x3843A047) });

    /// <summary>The place of a line missing on one side.</summary>
    public static readonly ThemeKey DiffEmptyCell = Attributes("diff.emptyCell", ThemePage.Diff, "Lines",
        new TextAttributes { Background = Argb(0x18808080) });

    /// <summary>A deleted word inside a changed line.</summary>
    public static readonly ThemeKey DiffDeletedWord = Attributes("diff.deletedWord", ThemePage.Diff, "Words",
        new TextAttributes { Background = Argb(0x80E53935) });

    /// <summary>An inserted word inside a changed line.</summary>
    public static readonly ThemeKey DiffInsertedWord = Attributes("diff.insertedWord", ThemePage.Diff, "Words",
        new TextAttributes { Background = Argb(0x8043A047) });

    // ---- Preview ----

    /// <summary>The highlight of the element clicked in Code View.</summary>
    public static readonly ThemeKey PreviewHighlight = Color("preview.highlight", ThemePage.Preview, string.Empty, Rgb(0xFFEB3B));

    // ---- Interface ----

    /// <summary>The accent of the controls; unset = the system accent.</summary>
    public static readonly ThemeKey UiAccent = Color("ui.accent", ThemePage.Interface, "Controls", null);

    /// <summary>The selected text in text boxes.</summary>
    public static readonly ThemeKey UiTextSelection = Attributes("ui.textSelection", ThemePage.Interface, "Controls",
        new TextAttributes { Foreground = Rgb(0x000000), Background = Rgb(0xADD6FF) });

    /// <summary>A button of a destructive action (e.g. deleting).</summary>
    public static readonly ThemeKey UiDangerButton = Color("ui.dangerButton", ThemePage.Interface, "Controls", Rgb(0xC42B1C));

    /// <summary>A button of a destructive action under the mouse.</summary>
    public static readonly ThemeKey UiDangerButtonHover = Color("ui.dangerButton.hover", ThemePage.Interface, "Controls", Rgb(0xA82315));

    /// <summary>A pressed button of a destructive action.</summary>
    public static readonly ThemeKey UiDangerButtonPressed = Color("ui.dangerButton.pressed", ThemePage.Interface, "Controls", Rgb(0x8E1D11));

    /// <summary>A frame around a list or a preview in a dialog.</summary>
    public static readonly ThemeKey UiFrame = Color("ui.frame", ThemePage.Interface, "Controls", Rgb(0x808080));

    /// <summary>Secondary text (e.g. the source of a rule in the Live CSS panel).</summary>
    public static readonly ThemeKey UiMutedText = Color("ui.mutedText", ThemePage.Interface, "Messages", Rgb(0x808080));

    /// <summary>The warnings in dialogs (e.g. what a risky change would alter).</summary>
    public static readonly ThemeKey UiWarningText = Color("ui.warningText", ThemePage.Interface, "Messages", Rgb(0xB35C00));

    /// <summary>The error notifications: their status bar message, their row in Notifications, the dot on the bell.</summary>
    public static readonly ThemeKey UiErrorNotification = Color("ui.errorNotification", ThemePage.Interface, "Messages", Rgb(0xD93030));

    /// <summary>The errors in dialogs (e.g. an invalid regular expression).</summary>
    public static readonly ThemeKey UiErrorText = Color("ui.errorText", ThemePage.Interface, "Messages", Rgb(0xCD5C5C));

    /// <summary>The "⚠" mark of a risky item (e.g. in the Book Browser).</summary>
    public static readonly ThemeKey UiRiskMarker = Color("ui.riskMarker", ThemePage.Interface, "Messages", Rgb(0xC0392B));

    /// <summary>The dot of an active filter (e.g. the shortcut filter of the keymap).</summary>
    public static readonly ThemeKey UiFilterActive = Color("ui.filterActive", ThemePage.Interface, "Messages", Rgb(0x59A869));

    /// <summary>An action icon that cannot be used now (e.g. "Save" without changes).</summary>
    public static readonly ThemeKey UiIconInactive = Color("ui.icon.inactive", ThemePage.Interface, "Icons", Rgb(0xA0A0A0));

    /// <summary>An action icon that asks for attention (e.g. "Save" with changes).</summary>
    public static readonly ThemeKey UiIconAttention = Color("ui.icon.attention", ThemePage.Interface, "Icons", Rgb(0xD93030));

    /// <summary>The folder icon of the file lists.</summary>
    public static readonly ThemeKey UiIconFolder = Color("ui.icon.folder", ThemePage.Interface, "Icons", Rgb(0x3D7FD6));

    /// <summary>The inside of the folder icon.</summary>
    public static readonly ThemeKey UiIconFolderInner = Color("ui.icon.folderInner", ThemePage.Interface, "Icons", Rgb(0x8CC2F5));

    /// <summary>The page of the file icon of the file lists.</summary>
    public static readonly ThemeKey UiIconFile = Color("ui.icon.file", ThemePage.Interface, "Icons", Rgb(0xFFFFFF));

    /// <summary>The outline of the file icon.</summary>
    public static readonly ThemeKey UiIconFileOutline = Color("ui.icon.fileOutline", ThemePage.Interface, "Icons", Rgb(0x8A8A8A));

    /// <summary>The folded corner of the file icon.</summary>
    public static readonly ThemeKey UiIconFileFold = Color("ui.icon.fileFold", ThemePage.Interface, "Icons", Rgb(0xD8D8D8));

    /// <summary>The background of the sample of a font file (the tab of a font).</summary>
    public static readonly ThemeKey UiFontPreviewBackground = Color("ui.fontPreview.background", ThemePage.Interface, "Previews", Rgb(0xFFFFFF));

    private static readonly ImmutableArray<ThemeKey> AllKeys = Collect();
    private static readonly Dictionary<string, ThemeKey> ById = AllKeys.ToDictionary(k => k.Id, StringComparer.Ordinal);

    /// <summary>Every key, in the order of the pages and their trees.</summary>
    public static ImmutableArray<ThemeKey> All => AllKeys;

    /// <summary>The key with the identifier.</summary>
    /// <exception cref="KeyNotFoundException">There is no such key.</exception>
    public static ThemeKey Get(string id) =>
        ById.TryGetValue(id, out ThemeKey? key) ? key : throw new KeyNotFoundException($"No theme key '{id}'.");

    /// <summary>The key with the identifier, if there is one.</summary>
    public static bool TryGet(string id, [NotNullWhen(true)] out ThemeKey? key) => ById.TryGetValue(id, out key);

    /// <summary>The keys of a page, in the order of its tree.</summary>
    public static IEnumerable<ThemeKey> On(ThemePage page) => AllKeys.Where(k => k.Page == page);

    private static ThemeKey Color(string id, ThemePage page, string group, ThemeColor? value) =>
        new(id, page, group, ThemeKeyKind.Color, null, value, null);

    private static ThemeKey Attributes(string id, ThemePage page, string group, TextAttributes value, string? baseId = null) =>
        new(id, page, group, ThemeKeyKind.TextAttributes, baseId, null, value);

    private static TextAttributes Fg(int rgb) => new() { Foreground = Rgb(rgb) };

    private static ThemeColor Rgb(int rgb) => ThemeColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static ThemeColor Argb(uint argb) => new((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    // The public fields above, in the order they are declared in (grouped by page); checks that the identifiers are unique, the bases exist and have the
    // same kind, and that following the bases never comes back to a key.
    private static ImmutableArray<ThemeKey> Collect()
    {
        ImmutableArray<ThemeKey> keys = typeof(ThemeKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ThemeKey))
            .OrderBy(f => f.MetadataToken)
            .Select(f => (ThemeKey)f.GetValue(null)!)
            .OrderBy(k => k.Page)
            .ToImmutableArray();
        Dictionary<string, ThemeKey> byId = new(StringComparer.Ordinal);
        foreach (ThemeKey key in keys)
        {
            if (!byId.TryAdd(key.Id, key))
            {
                throw new InvalidOperationException($"Duplicate theme key '{key.Id}'.");
            }
        }

        foreach (ThemeKey key in keys)
        {
            HashSet<string> seen = new(StringComparer.Ordinal) { key.Id };
            for (string? baseId = key.BaseId; baseId is not null; baseId = byId[baseId].BaseId)
            {
                if (!byId.TryGetValue(baseId, out ThemeKey? baseKey) || baseKey.Kind != key.Kind || !seen.Add(baseId))
                {
                    throw new InvalidOperationException($"Invalid base '{baseId}' of the theme key '{key.Id}'.");
                }
            }
        }

        return keys;
    }
}
