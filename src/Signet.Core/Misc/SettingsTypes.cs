using System;
using System.Buffers;

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
/// Interface theme preference.
/// </summary>
public enum ThemePreference
{
    /// <summary>Follow the system setting.</summary>
    System = 0,

    /// <summary>Always light.</summary>
    Light = 1,

    /// <summary>Always dark.</summary>
    Dark = 2,
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
/// <param name="Style">Background or outline.</param>
/// <param name="LightColor">Color (<c>#rrggbb</c>) for the light application theme.</param>
/// <param name="DarkColor">Color (<c>#rrggbb</c>) for the dark application theme.</param>
/// <param name="OpacityPercent">Background opacity in percent (the outline always uses the full color).</param>
/// <param name="OutlineWidth">Outline width in pixels.</param>
/// <param name="AutoHide">Whether the highlight disappears on its own (otherwise it stays until the caret moves again).</param>
/// <param name="AutoHideDelayMs">After how many milliseconds it disappears when <paramref name="AutoHide"/> is set.</param>
public readonly record struct PreviewHighlight(
    PreviewHighlightStyle Style,
    string LightColor,
    string DarkColor,
    int OpacityPercent,
    int OutlineWidth,
    bool AutoHide,
    int AutoHideDelayMs)
{
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

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
    /// Default values: a 45% yellow background (<c>rgba(255,235,59,0.45)</c>) in both themes,
    /// a 3 px outline, a persistent highlight; when auto-hide is enabled — 2 s.
    /// </summary>
    public static PreviewHighlight Default =>
        new(PreviewHighlightStyle.Background, "#ffeb3b", "#ffeb3b", 45, 3, false, 2000);

    /// <summary>Color for the application theme.</summary>
    /// <param name="dark">Whether the application uses a dark theme.</param>
    public string ColorFor(bool dark) => dark ? DarkColor : LightColor;

    /// <summary>A copy with values clamped to their ranges and valid colors (invalid → default).</summary>
    public PreviewHighlight Normalized() => new(
        Enum.IsDefined(Style) ? Style : Default.Style,
        IsHexColor(LightColor) ? LightColor : Default.LightColor,
        IsHexColor(DarkColor) ? DarkColor : Default.DarkColor,
        Math.Clamp(OpacityPercent, OpacityMin, OpacityMax),
        Math.Clamp(OutlineWidth, OutlineWidthMin, OutlineWidthMax),
        AutoHide,
        Math.Clamp(AutoHideDelayMs, AutoHideDelayMin, AutoHideDelayMax));

    /// <summary>Whether the value is a <c>#rrggbb</c> color.</summary>
    internal static bool IsHexColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept(HexDigits) < 0;
}

/// <summary>
/// Appearance of the warning texts in dialogs (e.g. the consequences of a risky span removal or Cleanup item) and of
/// the warning color used across the application (status bar, Notifications panel, attention tabs).
/// </summary>
/// <param name="FontSize">Font size (px) of the warning texts; 0 = the size of the interface text.</param>
/// <param name="LightColor">Warning color (<c>#rrggbb</c>) for the light application theme.</param>
/// <param name="DarkColor">Warning color (<c>#rrggbb</c>) for the dark application theme.</param>
public readonly record struct WarningAppearance(int FontSize, string LightColor, string DarkColor)
{
    /// <summary>Lower bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMin = 8;

    /// <summary>Upper bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMax = 40;

    /// <summary>Default values: the interface text size, dark orange (light theme) and amber (dark theme).</summary>
    public static WarningAppearance Default => new(0, "#b35c00", "#ffb900");

    /// <summary>A copy with the size clamped (0 stays "as the interface text") and valid colors (invalid → default).</summary>
    public WarningAppearance Normalized() => new(
        FontSize <= 0 ? 0 : Math.Clamp(FontSize, FontSizeMin, FontSizeMax),
        PreviewHighlight.IsHexColor(LightColor) ? LightColor : Default.LightColor,
        PreviewHighlight.IsHexColor(DarkColor) ? DarkColor : Default.DarkColor);
}

/// <summary>
/// Look of the Code View opening tag hint (the tooltip shown over a closing tag): font and colors for the light and
/// dark application themes.
/// </summary>
/// <param name="FontFamily">Font family; empty = the Code View editor font.</param>
/// <param name="FontSize">Font size (px); 0 = the Code View editor size (with its zoom).</param>
/// <param name="LightBackground">Background color (<c>#rrggbb</c>) for the light theme.</param>
/// <param name="LightForeground">Text color (<c>#rrggbb</c>) for the light theme.</param>
/// <param name="DarkBackground">Background color (<c>#rrggbb</c>) for the dark theme.</param>
/// <param name="DarkForeground">Text color (<c>#rrggbb</c>) for the dark theme.</param>
public readonly record struct OpenTagHintAppearance(
    string FontFamily,
    int FontSize,
    string LightBackground,
    string LightForeground,
    string DarkBackground,
    string DarkForeground)
{
    /// <summary>Lower bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMin = 6;

    /// <summary>Upper bound of an explicit <see cref="FontSize"/>.</summary>
    public const int FontSizeMax = 72;

    /// <summary>Default values: the editor font and the tooltip colors of the Fluent theme.</summary>
    public static OpenTagHintAppearance Default => new(string.Empty, 0, "#f2f2f2", "#000000", "#2b2b2b", "#ffffff");

    /// <summary>Background color for the application theme.</summary>
    public string BackgroundFor(bool dark) => dark ? DarkBackground : LightBackground;

    /// <summary>Text color for the application theme.</summary>
    public string ForegroundFor(bool dark) => dark ? DarkForeground : LightForeground;

    /// <summary>A copy with the size clamped (0 stays "as the editor") and valid colors (invalid → default).</summary>
    public OpenTagHintAppearance Normalized() => new(
        FontFamily?.Trim() ?? string.Empty,
        FontSize <= 0 ? 0 : Math.Clamp(FontSize, FontSizeMin, FontSizeMax),
        PreviewHighlight.IsHexColor(LightBackground) ? LightBackground : Default.LightBackground,
        PreviewHighlight.IsHexColor(LightForeground) ? LightForeground : Default.LightForeground,
        PreviewHighlight.IsHexColor(DarkBackground) ? DarkBackground : Default.DarkBackground,
        PreviewHighlight.IsHexColor(DarkForeground) ? DarkForeground : Default.DarkForeground);
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
/// Code editor (Code View) appearance settings — font + highlighting colors.
/// Colors are <c>#RRGGBB</c> strings (the Core layer does not depend on Avalonia; the UI layer
/// converts them to a color type).
/// </summary>
public sealed record CodeViewAppearance
{
    /// <summary>Editor font family.</summary>
    public string FontFamily { get; init; } = "Courier New";

    /// <summary>Editor font size (px).</summary>
    public int FontSize { get; init; } = 10;

    /// <summary>CSS comment color.</summary>
    public string CssCommentColor { get; init; } = "#008000";

    /// <summary>CSS property color.</summary>
    public string CssPropertyColor { get; init; } = "#000080";

    /// <summary>Color of quoted strings in CSS.</summary>
    public string CssQuoteColor { get; init; } = "#800080";

    /// <summary>CSS selector color.</summary>
    public string CssSelectorColor { get; init; } = "#800000";

    /// <summary>CSS value color.</summary>
    public string CssValueColor { get; init; } = "#000000";

    /// <summary>Current line highlight color.</summary>
    public string LineHighlightColor { get; init; } = "#FFFFBF";

    /// <summary>Background color of the line number column.</summary>
    public string LineNumberBackgroundColor { get; init; } = "#E1E1E1";

    /// <summary>Digit color in the line number column.</summary>
    public string LineNumberForegroundColor { get; init; } = "#7D7D7D";

    /// <summary>Color of the spelling error squiggle.</summary>
    public string SpellingUnderlineColor { get; init; } = "#FF0000";

    /// <summary>XHTML attribute name color.</summary>
    public string XhtmlAttributeNameColor { get; init; } = "#800000";

    /// <summary>XHTML attribute value color.</summary>
    public string XhtmlAttributeValueColor { get; init; } = "#008080";

    /// <summary>Color of a CSS block inside XHTML.</summary>
    public string XhtmlCssColor { get; init; } = "#808000";

    /// <summary>Color of a CSS comment inside XHTML.</summary>
    public string XhtmlCssCommentColor { get; init; } = "#008000";

    /// <summary>DOCTYPE declaration color.</summary>
    public string XhtmlDoctypeColor { get; init; } = "#000080";

    /// <summary>XHTML entity color.</summary>
    public string XhtmlEntityColor { get; init; } = "#800080";

    /// <summary>HTML tag color.</summary>
    public string XhtmlHtmlColor { get; init; } = "#0000FF";

    /// <summary>HTML comment color.</summary>
    public string XhtmlHtmlCommentColor { get; init; } = "#008000";

    // ---- Extended highlighting ----

    /// <summary>Color of CSS class / id / pseudo-class selectors.</summary>
    public string CssSpecialSelectorColor { get; init; } = "#AA5500";

    /// <summary>Color of <c>@</c> rules and <c>!important</c> in CSS.</summary>
    public string CssAtRuleColor { get; init; } = "#8B008B";

    /// <summary>Color of CSS constants (number, dimension, color).</summary>
    public string CssConstantColor { get; init; } = "#098658";

    /// <summary>Namespace prefix color (<c>svg:</c>, <c>epub:</c>).</summary>
    public string XhtmlNamespacePrefixColor { get; init; } = "#7B5694";

    /// <summary>Link target color (<c>href</c>, <c>src</c>, <c>url()</c>).</summary>
    public string LinkColor { get; init; } = "#0645AD";

    /// <summary>Color of the squiggle for syntax errors and broken links.</summary>
    public string ErrorUnderlineColor { get; init; } = "#FF0000";

    // ---- Search colors ----

    /// <summary>Background of all Find &amp; Replace matches (opaque, text drawn on top).</summary>
    public string SearchMatchBackgroundColor { get; init; } = "#FFF0C4";

    /// <summary>Selection background in the editor — also of the current Find match, which is selected.</summary>
    public string SelectionBackgroundColor { get; init; } = "#ADD6FF";

    /// <summary>Default colors for the light theme.</summary>
    public static CodeViewAppearance LightDefault => new();

    /// <summary>Default colors for the dark theme.</summary>
    public static CodeViewAppearance DarkDefault => new()
    {
        FontFamily = "Courier New",
        FontSize = 10,
        CssCommentColor = "#706D5B",
        CssPropertyColor = "#9FC28A",
        CssQuoteColor = "#EB939A",
        CssSelectorColor = "#EFEF8F",
        CssValueColor = "#FCFFE0",
        LineHighlightColor = "#515151",
        LineNumberBackgroundColor = "#2D2D2D",
        LineNumberForegroundColor = "#E5E5E5",
        SpellingUnderlineColor = "#FF3737",
        XhtmlAttributeNameColor = "#9FC28A",
        XhtmlAttributeValueColor = "#E89198",
        XhtmlCssColor = "#808000",
        XhtmlCssCommentColor = "#706D5B",
        XhtmlDoctypeColor = "#7EFCFF",
        XhtmlEntityColor = "#EBFFC4",
        XhtmlHtmlColor = "#EFEF8F",
        XhtmlHtmlCommentColor = "#706D5B",
        CssSpecialSelectorColor = "#F0C674",
        CssAtRuleColor = "#C586C0",
        CssConstantColor = "#B5CEA8",
        XhtmlNamespacePrefixColor = "#D7BA7D",
        LinkColor = "#6CB6FF",
        ErrorUnderlineColor = "#FF5555",
        SearchMatchBackgroundColor = "#695A2E",
        SelectionBackgroundColor = "#264F78",
    };
}
