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

/// <summary>How the preview marks the element corresponding to the Code View caret.</summary>
public enum PreviewHighlightStyle
{
    /// <summary>Semi-transparent background of the whole block (paragraph, heading, list item…).</summary>
    Background,

    /// <summary>An outline (CSS <c>outline</c> — does not shift the page layout) around the same block.</summary>
    Outline,
}

/// <summary>
/// Highlight of the Code View caret location in the preview (Code View → Preview synchronization).
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
/// or Appearance › Fonts for the windows without a section).
/// </summary>
public enum TooltipArea
{
    /// <summary>The main window: menus, toolbars, the status bar and the panels without an area of their own.</summary>
    MainWindow,

    /// <summary>The document tabs: their tab strips (e.g. closing a tab) and their contents (e.g. Code View).</summary>
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
public readonly record struct TooltipFont(string FontFamily, int FontSize)
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
        Math.Clamp(FontSize, FontSizeMin, FontSizeMax));
}

/// <summary>
/// Font of the Code View opening tag hint (the tooltip shown over a closing tag); its colors come from the theme
/// (<c>hints.openTag</c>).
/// </summary>
/// <param name="FontFamily">Font family; empty = the Code View editor font.</param>
/// <param name="FontSize">Font size (px); 0 = the Code View editor size (with its zoom).</param>
public readonly record struct OpenTagHintAppearance(string FontFamily, int FontSize)
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
        FontSize <= 0 ? 0 : Math.Clamp(FontSize, FontSizeMin, FontSizeMax));
}

/// <summary>Font settings of the "Insert Special Character" window.</summary>
/// <param name="FontFamily">Font family.</param>
/// <param name="FontSize">Size (px).</param>
public readonly record struct SpecialCharacterAppearance(string FontFamily, int FontSize)
{
    /// <summary>Default values.</summary>
    public static SpecialCharacterAppearance Default => new("Arial", 24);
}

/// <summary>
/// Code editor (Code View) font; the colors come from the theme (<see cref="Themes.ThemeKeys"/>).
/// </summary>
public sealed record CodeViewAppearance
{
    /// <summary>The default font.</summary>
    public static CodeViewAppearance Default { get; } = new();

    /// <summary>Editor font family.</summary>
    public string FontFamily { get; init; } = "Courier New";

    /// <summary>Editor font size (px).</summary>
    public int FontSize { get; init; } = 10;
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
