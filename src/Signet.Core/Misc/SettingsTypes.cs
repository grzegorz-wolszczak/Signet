using System;
using System.Collections.Generic;
using System.Linq;

namespace Signet.Core.Misc;

/// <summary>
/// When to run automatic source cleanup (<c>CleanSource</c>).
/// </summary>
[Flags]
public enum CleanOn
{
    /// <summary>Do not clean automatically.</summary>
    Never = 0,

    /// <summary>Clean when a publication is opened (<c>1 &lt;&lt; 0</c>).</summary>
    Open = 1 << 0,

    /// <summary>Clean when a publication is saved (<c>1 &lt;&lt; 1</c>).</summary>
    Save = 1 << 1,
}

/// <summary>
/// Window geometry remembered between runs (position, size and the
/// <c>maximized</c>/<c>fullscreen</c> flags).
/// </summary>
/// <param name="X">Left edge, physical screen pixels (<c>Window.Position</c>).</param>
/// <param name="Y">Top edge, physical screen pixels (<c>Window.Position</c>).</param>
/// <param name="Width">Width, logical units/DIP (<c>Window.Width</c>).</param>
/// <param name="Height">Height, logical units/DIP (<c>Window.Height</c>).</param>
/// <param name="Maximized">Whether the window was maximized.</param>
/// <param name="FullScreen">Whether the window was in full-screen mode.</param>
public readonly record struct WindowGeometry(
    int X,
    int Y,
    int Width,
    int Height,
    bool Maximized = false,
    bool FullScreen = false);

/// <summary>Preview font settings.</summary>
/// <param name="FontFamilyStandard">Default family.</param>
/// <param name="FontFamilySerif">Serif family.</param>
/// <param name="FontFamilySansSerif">Sans-serif family.</param>
/// <param name="FontSize">Base size (px).</param>
public readonly record struct PreviewAppearance(
    string FontFamilyStandard,
    string FontFamilySerif,
    string FontFamilySansSerif,
    int FontSize)
{
    /// <summary>Default values.</summary>
    public static PreviewAppearance Default => new("Arial", "Times New Roman", "Arial", 16);
}

/// <summary>How the preview marks the element corresponding to the Code Editor caret.</summary>
public enum PreviewHighlightStyle
{
    /// <summary>Semi-transparent background of the whole block (paragraph, heading, list item…).</summary>
    Background,

    /// <summary>An outline (CSS <c>outline</c> — does not shift the page layout) around the same block.</summary>
    Outline,
}

/// <summary>
/// How the Find &amp; Replace panel's action buttons (Find, Replace, Restart, Replace/Find, Replace All, Count
/// All) show their icon and text.
/// </summary>
public enum FindActionButtonDisplay
{
    /// <summary>Icon and text (the default).</summary>
    IconAndText,

    /// <summary>Icon only — narrower buttons, the label is still in the tooltip.</summary>
    IconOnly,

    /// <summary>Text only, no icon.</summary>
    TextOnly,
}

/// <summary>
/// Highlight of the Code Editor caret location in the preview (Code Editor → Preview synchronization).
/// </summary>
/// <param name="Style">Background or outline (the color comes from the theme: <c>preview.highlight</c>).</param>
/// <param name="OpacityPercent">Background opacity in percent (the outline always uses the full color).</param>
/// <param name="OutlineWidth">Outline width in pixels.</param>
/// <param name="AutoHide">Whether the highlight disappears on its own (otherwise it stays until the caret moves again).</param>
/// <param name="AutoHideDelayMs">After how many milliseconds it disappears when <paramref name="AutoHide"/> is set.</param>
public readonly record struct PreviewHighlight(
    PreviewHighlightStyle Style,
    int OpacityPercent,
    int OutlineWidth,
    bool AutoHide,
    int AutoHideDelayMs)
{
    /// <summary>Lower bound of <see cref="OpacityPercent"/>.</summary>
    public const int OpacityMin = 5;

    /// <summary>Upper bound of <see cref="OpacityPercent"/>.</summary>
    public const int OpacityMax = 100;

    /// <summary>Lower bound of <see cref="OutlineWidth"/> (px).</summary>
    public const int OutlineWidthMin = 1;

    /// <summary>Upper bound of <see cref="OutlineWidth"/> (px).</summary>
    public const int OutlineWidthMax = 20;

    /// <summary>Lower bound of <see cref="AutoHideDelayMs"/>.</summary>
    public const int AutoHideDelayMin = 500;

    /// <summary>Upper bound of <see cref="AutoHideDelayMs"/>.</summary>
    public const int AutoHideDelayMax = 30000;

    /// <summary>
    /// Default values: a 45% background, a 3 px outline, a persistent highlight; when auto-hide is enabled — 2 s.
    /// </summary>
    public static PreviewHighlight Default =>
        new(PreviewHighlightStyle.Background, 45, 3, false, 2000);

    /// <summary>A copy with values clamped to their ranges.</summary>
    public PreviewHighlight Normalized() => new(
        Enum.IsDefined(Style) ? Style : Default.Style,
        Math.Clamp(OpacityPercent, OpacityMin, OpacityMax),
        Math.Clamp(OutlineWidth, OutlineWidthMin, OutlineWidthMax),
        AutoHide,
        Math.Clamp(AutoHideDelayMs, AutoHideDelayMin, AutoHideDelayMax));
}

/// <summary>
/// Appearance of the warning texts in dialogs (e.g. the consequences of a risky span removal or Cleanup item); their
/// color comes from the theme (<c>ui.warningText</c>).
/// </summary>
/// <param name="FontSize">Font size (px) of the warning texts; 0 = the size of the interface text.</param>
public readonly record struct WarningAppearance(int FontSize)
{
    /// <summary>Lower bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMin = 8;

    /// <summary>Upper bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMax = 40;

    /// <summary>Default values: the interface text size.</summary>
    public static WarningAppearance Default => new(0);

    /// <summary>A copy with the size clamped (0 stays "as the interface text").</summary>
    public WarningAppearance Normalized() => new(FontSize <= 0 ? 0 : Math.Clamp(FontSize, FontSizeMin, FontSizeMax));
}

/// <summary>
/// A part of the application whose tooltips have their own font (Preferences: the "Fonts" page of the part's section,
/// or Appearance › General › Fonts for the windows without a section).
/// </summary>
public enum TooltipArea
{
    /// <summary>The main window: menus, toolbars, the status bar and the panels without an area of their own.</summary>
    MainWindow,

    /// <summary>The document tabs: their tab strips (e.g. closing a tab) and their contents (e.g. Code Editor).</summary>
    DocumentTabs,

    /// <summary>The Preferences window.</summary>
    PreferencesWindow,

    /// <summary>The windows without an area of their own (e.g. Select Files, View Image) — also the default.</summary>
    OtherWindows,

    /// <summary>The start screen (e.g. the path of a recent file).</summary>
    StartScreen,

    /// <summary>The Book Browser.</summary>
    BookBrowser,

    /// <summary>The Table of Contents panel, Edit Table of Contents and the Heading Selector.</summary>
    TableOfContents,

    /// <summary>The Preview panel.</summary>
    Preview,

    /// <summary>Find &amp; Replace: its view, its panel, the Search Editor and Find Usages.</summary>
    FindReplace,

    /// <summary>The "Insert Special Character" window.</summary>
    SpecialCharacters,

    /// <summary>The Checkpoints panel and the Diff window.</summary>
    Checkpoints,

    /// <summary>The validation results.</summary>
    Validation,

    /// <summary>The Metadata Editor.</summary>
    MetadataEditor,

    /// <summary>Cleanup and Standardization.</summary>
    Cleanup,

    /// <summary>The footnote windows (e.g. the footnote conversion).</summary>
    Footnotes,
}

/// <summary>The font of the tooltips of a <see cref="TooltipArea"/>.</summary>
/// <param name="FontFamily">Typeface; empty = the interface font.</param>
/// <param name="FontSize">Size (px).</param>
/// <param name="FontWeight">Weight of the font (<see cref="FontWeightScale"/>); 0 = not set.</param>
public readonly record struct TooltipFont(string FontFamily, int FontSize, int FontWeight = 0)
{
    /// <summary>Lower bound of <see cref="FontSize"/>.</summary>
    public const int FontSizeMin = 8;

    /// <summary>Upper bound of <see cref="FontSize"/>.</summary>
    public const int FontSizeMax = 40;

    /// <summary>Default values: the interface font at 13 px.</summary>
    public static TooltipFont Default => new(string.Empty, 13);

    /// <summary>A copy with the size clamped and the typeface trimmed.</summary>
    public TooltipFont Normalized() => new(
        (FontFamily ?? string.Empty).Trim(),
        Math.Clamp(FontSize, FontSizeMin, FontSizeMax),
        FontWeightScale.Normalize(FontWeight));
}

/// <summary>
/// Font of the Code Editor opening tag hint (the tooltip shown over a closing tag); its colors come from the theme
/// (<c>hints.openTag</c>).
/// </summary>
/// <param name="FontFamily">Font family; empty = the Code Editor font.</param>
/// <param name="FontSize">Font size (px); 0 = the Code Editor size (with its zoom).</param>
/// <param name="FontWeight">Weight of the font (<see cref="FontWeightScale"/>); 0 = not set.</param>
public readonly record struct OpenTagHintAppearance(string FontFamily, int FontSize, int FontWeight = 0)
{
    /// <summary>Lower bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMin = 6;

    /// <summary>Upper bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMax = 72;

    /// <summary>Default values: the editor font.</summary>
    public static OpenTagHintAppearance Default => new(string.Empty, 0);

    /// <summary>A copy with the size clamped (0 stays "as the editor").</summary>
    public OpenTagHintAppearance Normalized() => new(
        FontFamily?.Trim() ?? string.Empty,
        FontSize <= 0 ? 0 : Math.Clamp(FontSize, FontSizeMin, FontSizeMax),
        FontWeightScale.Normalize(FontWeight));
}

/// <summary>
/// Font of the titles of the document tabs (also in the windows of the tabs dragged out); the colors come from the theme
/// (<c>ui.documentTab</c>, <c>ui.documentTab.selected</c>).
/// </summary>
/// <param name="FontFamily">Font family; empty = the interface font.</param>
/// <param name="FontSize">Font size (px); 0 = the default size of the tabs.</param>
/// <param name="FontWeight">Weight of the font (<see cref="FontWeightScale"/>); 0 = not set.</param>
public readonly record struct DocumentTabFontAppearance(string FontFamily, int FontSize, int FontWeight = 0)
{
    /// <summary>Lower bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMin = 6;

    /// <summary>Upper bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMax = 72;

    /// <summary>Default values: the interface font at the default size of the tabs.</summary>
    public static DocumentTabFontAppearance Default => new(string.Empty, 0);

    /// <summary>A copy with the size clamped (0 stays "the default").</summary>
    public DocumentTabFontAppearance Normalized() => new(
        FontFamily?.Trim() ?? string.Empty,
        FontSize <= 0 ? 0 : Math.Clamp(FontSize, FontSizeMin, FontSizeMax),
        FontWeightScale.Normalize(FontWeight));
}

/// <summary>
/// Font of the title bar of a tool panel (Book Browser, Preview, Validation Results, …) — every dockable panel that
/// is not a document tab, which has a font setting of its own (<see cref="DocumentTabFontAppearance"/>).
/// </summary>
/// <param name="FontFamily">Font family; empty = the interface font.</param>
/// <param name="FontSize">Font size (px); 0 = the default size of the panel title.</param>
/// <param name="FontWeight">Weight of the font (<see cref="FontWeightScale"/>); 0 = not set.</param>
public readonly record struct ToolHeaderFontAppearance(string FontFamily, int FontSize, int FontWeight = 0)
{
    /// <summary>Lower bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMin = 6;

    /// <summary>Upper bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMax = 72;

    /// <summary>Default values: the interface font at the default size of the panel title.</summary>
    public static ToolHeaderFontAppearance Default => new(string.Empty, 0);

    /// <summary>A copy with the size clamped (0 stays "the default").</summary>
    public ToolHeaderFontAppearance Normalized() => new(
        FontFamily?.Trim() ?? string.Empty,
        FontSize <= 0 ? 0 : Math.Clamp(FontSize, FontSizeMin, FontSizeMax),
        FontWeightScale.Normalize(FontWeight));
}

/// <summary>
/// Font of the timestamp column and of the message column of the Notifications panel (independent of each other), and
/// the vertical space between rows; the colors come from the theme (<c>ui.notifications.*</c>, see <see cref="Signet.Core.Themes.ThemeKeys"/>).
/// </summary>
/// <param name="TimestampFontFamily">Font family of the timestamp column; empty = the interface font.</param>
/// <param name="TimestampFontSize">Font size (px) of the timestamp column; 0 = the interface size.</param>
/// <param name="TimestampFontWeight">Weight of the timestamp column (<see cref="FontWeightScale"/>); 0 = not set.</param>
/// <param name="MessageFontFamily">Font family of the message column; empty = the interface font.</param>
/// <param name="MessageFontSize">Font size (px) of the message column; 0 = the interface size.</param>
/// <param name="MessageFontWeight">Weight of the message column (<see cref="FontWeightScale"/>); 0 = not set.</param>
/// <param name="EntrySpacing">Vertical space (px) between two rows; 2 by default.</param>
public readonly record struct NotificationsAppearance(
    string TimestampFontFamily, int TimestampFontSize, int TimestampFontWeight,
    string MessageFontFamily, int MessageFontSize, int MessageFontWeight,
    int EntrySpacing = 2)
{
    /// <summary>Lower bound of an explicit font size.</summary>
    public const int FontSizeMin = 6;

    /// <summary>Upper bound of an explicit font size.</summary>
    public const int FontSizeMax = 72;

    /// <summary>Upper bound of <see cref="EntrySpacing"/>.</summary>
    public const int EntrySpacingMax = 20;

    /// <summary>Default values: the interface font at the default size, the default spacing between rows.</summary>
    public static NotificationsAppearance Default => new(string.Empty, 0, 0, string.Empty, 0, 0);

    /// <summary>A copy with the sizes and the spacing clamped (0 stays "the default").</summary>
    public NotificationsAppearance Normalized() => new(
        TimestampFontFamily?.Trim() ?? string.Empty,
        TimestampFontSize <= 0 ? 0 : Math.Clamp(TimestampFontSize, FontSizeMin, FontSizeMax),
        FontWeightScale.Normalize(TimestampFontWeight),
        MessageFontFamily?.Trim() ?? string.Empty,
        MessageFontSize <= 0 ? 0 : Math.Clamp(MessageFontSize, FontSizeMin, FontSizeMax),
        FontWeightScale.Normalize(MessageFontWeight),
        Math.Clamp(EntrySpacing, 0, EntrySpacingMax));
}

/// <summary>
/// Font of the line numbers of Code Editor; the colors come from the theme (<c>editor.lineNumbers</c>,
/// <c>editor.currentLineNumber</c>).
/// </summary>
/// <param name="FontFamily">Font family; empty = the Code Editor font.</param>
/// <param name="FontSize">Font size (px); 0 = the Code Editor size (with its zoom).</param>
/// <param name="FontWeight">Weight of the font (<see cref="FontWeightScale"/>); 0 = not set.</param>
public readonly record struct LineNumberFontAppearance(string FontFamily, int FontSize, int FontWeight = 0)
{
    /// <summary>Lower bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMin = 6;

    /// <summary>Upper bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMax = 72;

    /// <summary>Default values: the editor font.</summary>
    public static LineNumberFontAppearance Default => new(string.Empty, 0);

    /// <summary>Whether the line numbers use the editor font (no typeface, size and weight of their own).</summary>
    public bool IsInherited => string.IsNullOrEmpty(FontFamily) && FontSize <= 0 && FontWeight <= 0;

    /// <summary>A copy with the size clamped (0 stays "as the editor").</summary>
    public LineNumberFontAppearance Normalized() => new(
        FontFamily?.Trim() ?? string.Empty,
        FontSize <= 0 ? 0 : Math.Clamp(FontSize, FontSizeMin, FontSizeMax),
        FontWeightScale.Normalize(FontWeight));
}

/// <summary>
/// Look of the Book Browser tree: the fonts of the names and of the column headers, the indentation of the files in a
/// folder and the space between the rows.
/// </summary>
/// <param name="ItemsFontFamily">
/// Typeface of the names of folders and files and of the cells of the other columns; empty = the interface font.
/// </param>
/// <param name="ItemsFontSize">Size (px) of those; 0 = the interface size.</param>
/// <param name="HeaderFontFamily">Typeface of the column headers; empty = the interface font.</param>
/// <param name="HeaderFontSize">Size (px) of the column headers; 0 = the interface size.</param>
/// <param name="Indent">Indentation (px) of one level of the tree.</param>
/// <param name="CellMinHeight">Minimum height (px) of a cell, so of a row; 0 = a row is as high as its content.</param>
/// <param name="CellPadding">Vertical padding (px) of a cell: the space above and below its content.</param>
/// <param name="ShowFolders">
/// Whether the names of the files show the folders they are in (<c>text/part0001.html</c>, the path from the folder of the
/// OPF), as in calibre; otherwise only the file name.
/// </param>
/// <param name="ItemsFontWeight">Weight of the names and of the cells of the other columns (<see cref="FontWeightScale"/>); 0 = not set.</param>
/// <param name="HeaderFontWeight">Weight of the column headers (<see cref="FontWeightScale"/>); 0 = not set.</param>
public readonly record struct BookBrowserAppearance(
    string ItemsFontFamily, int ItemsFontSize, string HeaderFontFamily, int HeaderFontSize, int Indent,
    int CellMinHeight = BookBrowserAppearance.DefaultCellMinHeight, int CellPadding = 0, bool ShowFolders = false,
    int ItemsFontWeight = 0, int HeaderFontWeight = 0)
{
    /// <summary>The default of <see cref="CellMinHeight"/>: the height of the icons of the tree.</summary>
    public const int DefaultCellMinHeight = 16;

    /// <summary>Upper bound of <see cref="CellMinHeight"/> (the lower bound is 0).</summary>
    public const int CellMinHeightMax = 60;

    /// <summary>Upper bound of <see cref="CellPadding"/> (the lower bound is 0).</summary>
    public const int CellPaddingMax = 20;

    /// <summary>Lower bound of an explicit font size.</summary>
    public const int FontSizeMin = 6;

    /// <summary>Upper bound of an explicit font size.</summary>
    public const int FontSizeMax = 72;

    /// <summary>Lower bound of <see cref="Indent"/>.</summary>
    public const int IndentMin = 0;

    /// <summary>Upper bound of <see cref="Indent"/>.</summary>
    public const int IndentMax = 60;

    /// <summary>
    /// Default values: the interface font, an indentation of 20 px, rows 16 px high (the icons) and only the file names.
    /// </summary>
    public static BookBrowserAppearance Default => new(string.Empty, 0, string.Empty, 0, 20);

    /// <summary>A copy with the sizes and distances clamped (a font size of 0 stays "as the interface").</summary>
    public BookBrowserAppearance Normalized() => new(
        ItemsFontFamily?.Trim() ?? string.Empty,
        NormalizedFontSize(ItemsFontSize),
        HeaderFontFamily?.Trim() ?? string.Empty,
        NormalizedFontSize(HeaderFontSize),
        Math.Clamp(Indent, IndentMin, IndentMax),
        Math.Clamp(CellMinHeight, 0, CellMinHeightMax),
        Math.Clamp(CellPadding, 0, CellPaddingMax),
        ShowFolders,
        FontWeightScale.Normalize(ItemsFontWeight),
        FontWeightScale.Normalize(HeaderFontWeight));

    private static int NormalizedFontSize(int size) => size <= 0 ? 0 : Math.Clamp(size, FontSizeMin, FontSizeMax);
}

/// <summary>
/// Look of the start screen's list of recent files: the fonts of the name and the folder of a file and of the heading of the
/// list, and the indentation of the entries under the heading.
/// </summary>
/// <param name="TitleFontFamily">Typeface of the name of a recent file; empty = the interface font.</param>
/// <param name="TitleFontSize">Size (px) of the name of a recent file; 0 = the interface size.</param>
/// <param name="LocationFontFamily">Typeface of the folder of a recent file; empty = the interface font.</param>
/// <param name="LocationFontSize">Size (px) of the folder of a recent file; 0 = the default (a little smaller than the interface).</param>
/// <param name="SectionFontFamily">Typeface of the heading of the list ("Recent files"); empty = the interface font.</param>
/// <param name="SectionFontSize">Size (px) of the heading; 0 = the interface size.</param>
/// <param name="Indent">Indentation (px) of the entries relative to the heading of the list.</param>
/// <param name="TitleFontWeight">Weight of the name of a recent file (<see cref="FontWeightScale"/>); 0 = not set.</param>
/// <param name="LocationFontWeight">Weight of the folder of a recent file (<see cref="FontWeightScale"/>); 0 = not set.</param>
/// <param name="SectionFontWeight">Weight of the heading (<see cref="FontWeightScale"/>); 0 = not set (semi-bold).</param>
public readonly record struct StartScreenAppearance(
    string TitleFontFamily, int TitleFontSize, string LocationFontFamily, int LocationFontSize,
    string SectionFontFamily, int SectionFontSize, int Indent = StartScreenAppearance.DefaultIndent,
    int TitleFontWeight = 0, int LocationFontWeight = 0, int SectionFontWeight = 0)
{
    /// <summary>The default of <see cref="Indent"/>: the padding of an entry.</summary>
    public const int DefaultIndent = 8;

    /// <summary>Lower bound of <see cref="Indent"/>.</summary>
    public const int IndentMin = 0;

    /// <summary>Upper bound of <see cref="Indent"/>.</summary>
    public const int IndentMax = 60;

    /// <summary>Lower bound of an explicit font size.</summary>
    public const int FontSizeMin = 6;

    /// <summary>Upper bound of an explicit font size.</summary>
    public const int FontSizeMax = 72;

    /// <summary>Default values: the interface font everywhere and an indentation of 8 px.</summary>
    public static StartScreenAppearance Default => new(string.Empty, 0, string.Empty, 0, string.Empty, 0);

    /// <summary>A copy with the sizes and the indentation clamped (a font size of 0 stays "as the default").</summary>
    public StartScreenAppearance Normalized() => new(
        TitleFontFamily?.Trim() ?? string.Empty,
        NormalizedFontSize(TitleFontSize),
        LocationFontFamily?.Trim() ?? string.Empty,
        NormalizedFontSize(LocationFontSize),
        SectionFontFamily?.Trim() ?? string.Empty,
        NormalizedFontSize(SectionFontSize),
        Math.Clamp(Indent, IndentMin, IndentMax),
        FontWeightScale.Normalize(TitleFontWeight),
        FontWeightScale.Normalize(LocationFontWeight),
        FontWeightScale.Normalize(SectionFontWeight));

    private static int NormalizedFontSize(int size) => size <= 0 ? 0 : Math.Clamp(size, FontSizeMin, FontSizeMax);
}

/// <summary>
/// The fonts of the "Find Usages" panel: the column headers, the "Context" column (e.g. a fixed-width font for the code)
/// and the rest of the panel (the other cells and the heading). An empty typeface and a size of 0 stay "as the interface".
/// </summary>
/// <param name="HeaderFontFamily">Typeface of the column headers.</param>
/// <param name="HeaderFontSize">Size (px) of the column headers.</param>
/// <param name="ContextFontFamily">Typeface of the "Context" column.</param>
/// <param name="ContextFontSize">Size (px) of the "Context" column.</param>
/// <param name="CellsFontFamily">Typeface of the other cells and of the heading above the tree.</param>
/// <param name="CellsFontSize">Size (px) of the other cells and of the heading.</param>
/// <param name="HeaderFontWeight">Weight of the column headers (<see cref="FontWeightScale"/>); 0 = not set.</param>
/// <param name="ContextFontWeight">Weight of the "Context" column (<see cref="FontWeightScale"/>); 0 = not set.</param>
/// <param name="CellsFontWeight">Weight of the other cells and of the heading (<see cref="FontWeightScale"/>); 0 = not set.</param>
public readonly record struct FindUsagesAppearance(
    string HeaderFontFamily, int HeaderFontSize, string ContextFontFamily, int ContextFontSize,
    string CellsFontFamily, int CellsFontSize, int HeaderFontWeight = 0, int ContextFontWeight = 0, int CellsFontWeight = 0)
{
    /// <summary>Lower bound of an explicit font size.</summary>
    public const int FontSizeMin = 6;

    /// <summary>Upper bound of an explicit font size.</summary>
    public const int FontSizeMax = 72;

    /// <summary>Default values: the interface font everywhere.</summary>
    public static FindUsagesAppearance Default => new(string.Empty, 0, string.Empty, 0, string.Empty, 0);

    /// <summary>A copy with the sizes clamped (a font size of 0 stays "as the interface") and the typefaces trimmed.</summary>
    public FindUsagesAppearance Normalized() => new(
        HeaderFontFamily?.Trim() ?? string.Empty,
        NormalizedFontSize(HeaderFontSize),
        ContextFontFamily?.Trim() ?? string.Empty,
        NormalizedFontSize(ContextFontSize),
        CellsFontFamily?.Trim() ?? string.Empty,
        NormalizedFontSize(CellsFontSize),
        FontWeightScale.Normalize(HeaderFontWeight),
        FontWeightScale.Normalize(ContextFontWeight),
        FontWeightScale.Normalize(CellsFontWeight));

    private static int NormalizedFontSize(int size) => size <= 0 ? 0 : Math.Clamp(size, FontSizeMin, FontSizeMax);
}

/// <summary>
/// Font settings of the Saved Searches window (both its list view's tree and its Add/Edit view's Name field):
/// the "Name" column header, and the tree rows / the read-only Details preview / the Name field.
/// </summary>
/// <param name="HeaderFontFamily">Typeface of the "Name" column header; empty = the interface font.</param>
/// <param name="HeaderFontSize">Size (px) of the "Name" column header; 0 = the interface size.</param>
/// <param name="RowsFontFamily">Typeface of the tree rows, the Details preview and the Name field; empty = the interface font.</param>
/// <param name="RowsFontSize">Size (px) of the tree rows, the Details preview and the Name field; 0 = the interface size.</param>
/// <param name="HeaderFontWeight">Weight of the column header (<see cref="FontWeightScale"/>); 0 = not set.</param>
/// <param name="RowsFontWeight">Weight of the rows (<see cref="FontWeightScale"/>); 0 = not set.</param>
public readonly record struct SearchEditorAppearance(
    string HeaderFontFamily, int HeaderFontSize, string RowsFontFamily, int RowsFontSize,
    int HeaderFontWeight = 0, int RowsFontWeight = 0)
{
    /// <summary>Lower bound of an explicit font size.</summary>
    public const int FontSizeMin = 6;

    /// <summary>Upper bound of an explicit font size.</summary>
    public const int FontSizeMax = 72;

    /// <summary>Default values: the interface font everywhere.</summary>
    public static SearchEditorAppearance Default => new(string.Empty, 0, string.Empty, 0);

    /// <summary>A copy with the sizes clamped (a font size of 0 stays "as the interface") and the typefaces trimmed.</summary>
    public SearchEditorAppearance Normalized() => new(
        HeaderFontFamily?.Trim() ?? string.Empty,
        NormalizedFontSize(HeaderFontSize),
        RowsFontFamily?.Trim() ?? string.Empty,
        NormalizedFontSize(RowsFontSize),
        FontWeightScale.Normalize(HeaderFontWeight),
        FontWeightScale.Normalize(RowsFontWeight));

    private static int NormalizedFontSize(int size) => size <= 0 ? 0 : Math.Clamp(size, FontSizeMin, FontSizeMax);
}

/// <summary>Font settings of the "Insert Special Character" window.</summary>
/// <param name="FontFamily">Font family.</param>
/// <param name="FontSize">Size (px).</param>
/// <param name="FontWeight">Weight of the font (<see cref="FontWeightScale"/>); 0 = not set.</param>
public readonly record struct SpecialCharacterAppearance(string FontFamily, int FontSize, int FontWeight = 0)
{
    /// <summary>Default values.</summary>
    public static SpecialCharacterAppearance Default => new("Arial", 24);
}

/// <summary>
/// The font of the code previews of the Appearance › Color Scheme pages (the demo text in the colors of the theme) — a
/// monospaced font, as in an editor.
/// </summary>
/// <param name="FontFamily">Font family.</param>
/// <param name="FontSize">Size (px).</param>
/// <param name="FontWeight">Weight of the font (<see cref="FontWeightScale"/>); 0 = not set.</param>
public readonly record struct ColorSchemePreviewAppearance(string FontFamily, int FontSize, int FontWeight = 0)
{
    /// <summary>Default values.</summary>
    public static ColorSchemePreviewAppearance Default => new("Consolas", 13);
}

/// <summary>
/// Code editor (Code Editor) font; the colors come from the theme (<see cref="Themes.ThemeKeys"/>).
/// </summary>
public sealed record CodeEditorAppearance
{
    /// <summary>The default font.</summary>
    public static CodeEditorAppearance Default { get; } = new();

    /// <summary>Editor font family.</summary>
    public string FontFamily { get; init; } = "Courier New";

    /// <summary>Editor font size (px).</summary>
    public int FontSize { get; init; } = 10;

    /// <summary>Editor font weight (<see cref="FontWeightScale"/>); 0 = not set (Regular).</summary>
    public int FontWeight { get; init; }
}

/// <summary>Where in the reading order (spine) the edition page goes.</summary>
public enum EditionPagePosition
{
    /// <summary>The first file of the spine.</summary>
    First,

    /// <summary>The second file of the spine (after the cover, if the book has one).</summary>
    Second,

    /// <summary>The last-but-one file of the spine.</summary>
    Penultimate,

    /// <summary>The last file of the spine.</summary>
    Last,
}

/// <summary>A user field of the edition page table (e.g. "Edited by" — "GreatWorksPublishing").</summary>
/// <param name="Key">The label in the first column.</param>
/// <param name="Value">The value in the second column.</param>
/// <param name="Show">Whether the row is written to the page.</param>
public readonly record struct EditionPageField(string Key, string Value, bool Show);

/// <summary>
/// The edition page (Preferences → Edition page): a page with a table of fields that Signet adds to the book and
/// updates on the first save after the book was opened, so the copy on a reader can be told apart from an older one.
/// Global — not per book.
/// </summary>
/// <param name="Enabled">Whether the page is added/updated (on the first save after opening the book).</param>
/// <param name="Position">Where in the spine the page goes.</param>
/// <param name="Fields">The user fields, in table order (before the fixed rows).</param>
/// <param name="ShowProgram">Whether the program row ("Signet") is written.</param>
/// <param name="ShowVersion">Whether the version row (the version of Signet that wrote the page) is written.</param>
/// <param name="ShowRevision">Whether the revision row (a counter kept in the book) is written.</param>
/// <param name="ShowDate">Whether the save date row (UTC) is written.</param>
/// <param name="AddToToc">Whether the page gets an entry in the book's table of contents (NAV / NCX).</param>
/// <param name="Title">The page heading (and the TOC entry text); empty = by the book's language.</param>
/// <param name="HeadingLevel">The level of the page heading (1–6).</param>
public sealed record EditionPageSettings(
    bool Enabled,
    EditionPagePosition Position,
    IReadOnlyList<EditionPageField> Fields,
    bool ShowProgram,
    bool ShowVersion,
    bool ShowRevision,
    bool ShowDate,
    bool AddToToc,
    string Title,
    int HeadingLevel)
{
    /// <summary>Default values: disabled, the last page, no user fields, all fixed rows shown, in the TOC, h1.</summary>
    public static EditionPageSettings Default { get; } =
        new(false, EditionPagePosition.Last, Array.Empty<EditionPageField>(), true, true, true, true, true, string.Empty, 1);

    /// <inheritdoc />
    public bool Equals(EditionPageSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && Position == other.Position
        && Fields.SequenceEqual(other.Fields)
        && ShowProgram == other.ShowProgram
        && ShowVersion == other.ShowVersion
        && ShowRevision == other.ShowRevision
        && ShowDate == other.ShowDate
        && AddToToc == other.AddToToc
        && string.Equals(Title, other.Title, StringComparison.Ordinal)
        && HeadingLevel == other.HeadingLevel;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Enabled, Position, Fields.Count, HashCode.Combine(ShowProgram, ShowVersion, ShowRevision, ShowDate), AddToToc, Title, HeadingLevel);
}

/// <summary>
/// Snapshot of a single dock region (Left/Right/Bottom) to persist — order and
/// assignment of panels plus the region's proportion. Part of <see cref="DockLayoutState"/>.
/// </summary>
public sealed record DockRegionState(string DockId, double Proportion, IReadOnlyList<string> ToolIds, string? ActiveToolId);

/// <summary>
/// Snapshot of the main window's dock layout (dock layout persistence across restarts):
/// assignment of panels to regions, their order, the region's active tab and the proportions
/// (of the regions and of the document area). It does not cover document tabs (handled separately by
/// <c>TabManager.CaptureSession</c>), panel visibility (handled separately by
/// <c>MainDockFactory.CaptureToolVisibility</c>) or floating windows (a floating panel is saved in the region it
/// was taken from; its window is persisted separately by <c>MainDockFactory.CaptureFloatingPanels</c>).
/// </summary>
public sealed record DockLayoutState(IReadOnlyList<DockRegionState> Regions, double DocumentsProportion);
