using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Signet.Core.Misc;

namespace Signet.Core.Themes;

/// <summary>
/// The places a theme can set the font of (Rider's color scheme font, extended to every font of the "Fonts" pages): the
/// keys of <see cref="Theme.Fonts"/>. A <see cref="ThemeFont"/> means what the setting of its place means: an empty
/// family or a size of 0 is the default of the place (e.g. a tooltip without a typeface uses the interface font); a
/// place with only a typeface ignores the size, a place with only a size ignores the family.
/// </summary>
public static class ThemeFontKeys
{
    /// <summary>The interface font (family and size).</summary>
    public const string Interface = "ui";

    /// <summary>The size of the warnings in dialogs (size only).</summary>
    public const string Warnings = "warnings";

    /// <summary>The Code Editor font (family and size).</summary>
    public const string CodeEditorText = "codeEditor.editor";

    /// <summary>The opening tag hint of Code Editor (family and size).</summary>
    public const string OpenTagHint = "codeEditor.openTagHint";

    /// <summary>The line numbers of Code Editor (family and size; empty / 0 = the editor font).</summary>
    public const string CodeEditorLineNumbers = "codeEditor.lineNumbers";

    /// <summary>The standard font of the Preview (family only).</summary>
    public const string PreviewStandard = "preview.standard";

    /// <summary>The serif font of the Preview (family only).</summary>
    public const string PreviewSerif = "preview.serif";

    /// <summary>The sans-serif font of the Preview (family only).</summary>
    public const string PreviewSansSerif = "preview.sansSerif";

    /// <summary>The font size of the Preview (size only).</summary>
    public const string PreviewSize = "preview.size";

    /// <summary>The font of the "Insert Special Character" window (family and size).</summary>
    public const string SpecialCharacters = "specialCharacters";

    /// <summary>The names and the other cells of the Book Browser tree (family and size).</summary>
    public const string BookBrowserItems = "bookBrowser.items";

    /// <summary>The column headers of the Book Browser tree (family and size).</summary>
    public const string BookBrowserHeaders = "bookBrowser.headers";

    /// <summary>The tooltips of an area (family and size), e.g. <c>tooltip.bookBrowser</c>.</summary>
    public static string Tooltip(TooltipArea area)
    {
        string name = area.ToString();
        return "tooltip." + char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>Every place, in the order of the "Fonts" pages.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
        {
            Interface, Warnings, CodeEditorText, CodeEditorLineNumbers, OpenTagHint, PreviewStandard, PreviewSerif, PreviewSansSerif, PreviewSize,
            SpecialCharacters, BookBrowserItems, BookBrowserHeaders,
        }
        .Concat(Enum.GetValues<TooltipArea>().Select(Tooltip))
        .ToArray();

    /// <summary>
    /// The fonts the settings use now, by place — what a theme starts from when "use the theme fonts" is turned on (as
    /// Rider copies the default font into the scheme).
    /// </summary>
    public static ImmutableDictionary<string, ThemeFont> FromSettings(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ImmutableDictionary<string, ThemeFont>.Builder fonts = ImmutableDictionary.CreateBuilder<string, ThemeFont>(StringComparer.Ordinal);
        fonts[Interface] = new ThemeFont(settings.UiFont, settings.UiFontSize);
        fonts[Warnings] = new ThemeFont(string.Empty, settings.WarningAppearance.FontSize);
        CodeEditorAppearance code = settings.CodeEditorAppearance;
        fonts[CodeEditorText] = new ThemeFont(code.FontFamily, code.FontSize);
        LineNumberFontAppearance numbers = settings.LineNumberFontAppearance;
        fonts[CodeEditorLineNumbers] = new ThemeFont(numbers.FontFamily, numbers.FontSize);
        OpenTagHintAppearance hint = settings.OpenTagHintAppearance;
        fonts[OpenTagHint] = new ThemeFont(hint.FontFamily, hint.FontSize);
        PreviewAppearance preview = settings.PreviewAppearance;
        fonts[PreviewStandard] = new ThemeFont(preview.FontFamilyStandard, 0);
        fonts[PreviewSerif] = new ThemeFont(preview.FontFamilySerif, 0);
        fonts[PreviewSansSerif] = new ThemeFont(preview.FontFamilySansSerif, 0);
        fonts[PreviewSize] = new ThemeFont(string.Empty, preview.FontSize);
        SpecialCharacterAppearance special = settings.SpecialCharacterAppearance;
        fonts[SpecialCharacters] = new ThemeFont(special.FontFamily, special.FontSize);
        BookBrowserAppearance bookBrowser = settings.BookBrowserAppearance;
        fonts[BookBrowserItems] = new ThemeFont(bookBrowser.ItemsFontFamily, bookBrowser.ItemsFontSize);
        fonts[BookBrowserHeaders] = new ThemeFont(bookBrowser.HeaderFontFamily, bookBrowser.HeaderFontSize);
        foreach (TooltipArea area in Enum.GetValues<TooltipArea>())
        {
            TooltipFont tooltip = settings.GetEffectiveTooltipFont(area);
            fonts[Tooltip(area)] = new ThemeFont(tooltip.FontFamily, tooltip.FontSize);
        }

        return fonts.ToImmutable();
    }
}

/// <summary>
/// The fonts the application uses: those of the settings, or — when the theme turns its fonts on ("Use the theme
/// fonts", <see cref="Theme.UseThemeFonts"/>) — those the theme sets (with its parents). A place the theme does not set
/// keeps the font of the settings.
/// </summary>
public sealed class ThemeFonts
{
    private readonly Theme _theme;
    private readonly ThemeResolver _resolver;

    /// <summary>Creates the fonts of a theme.</summary>
    public ThemeFonts(Theme theme, ThemeResolver resolver)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>Whether the theme's fonts replace those of the settings.</summary>
    public bool IsActive => _theme.UseThemeFonts;

    /// <summary>The font of the place while the theme's fonts are on; <c>null</c> = the setting's.</summary>
    public ThemeFont? For(string key) => IsActive ? _resolver.Font(_theme, key) : null;

    /// <summary>The interface font.</summary>
    public (string Family, int Size) Interface(string family, int size) =>
        For(ThemeFontKeys.Interface) is { } f ? (f.Family, f.Size) : (family, size);

    /// <summary>The warnings in dialogs.</summary>
    public WarningAppearance Warnings(WarningAppearance setting) =>
        For(ThemeFontKeys.Warnings) is { } f ? new WarningAppearance(f.Size) : setting;

    /// <summary>The Code Editor font.</summary>
    public CodeEditorAppearance CodeEditor(CodeEditorAppearance setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return For(ThemeFontKeys.CodeEditorText) is { } f ? setting with { FontFamily = f.Family, FontSize = f.Size } : setting;
    }

    /// <summary>The line numbers of Code Editor.</summary>
    public LineNumberFontAppearance LineNumbers(LineNumberFontAppearance setting) =>
        For(ThemeFontKeys.CodeEditorLineNumbers) is { } f ? new LineNumberFontAppearance(f.Family, f.Size) : setting;

    /// <summary>The opening tag hint of Code Editor.</summary>
    public OpenTagHintAppearance OpenTagHint(OpenTagHintAppearance setting) =>
        For(ThemeFontKeys.OpenTagHint) is { } f ? new OpenTagHintAppearance(f.Family, f.Size) : setting;

    /// <summary>The Preview.</summary>
    public PreviewAppearance Preview(PreviewAppearance setting) => new(
        For(ThemeFontKeys.PreviewStandard)?.Family ?? setting.FontFamilyStandard,
        For(ThemeFontKeys.PreviewSerif)?.Family ?? setting.FontFamilySerif,
        For(ThemeFontKeys.PreviewSansSerif)?.Family ?? setting.FontFamilySansSerif,
        For(ThemeFontKeys.PreviewSize)?.Size ?? setting.FontSize);

    /// <summary>The "Insert Special Character" window.</summary>
    public SpecialCharacterAppearance SpecialCharacters(SpecialCharacterAppearance setting) =>
        For(ThemeFontKeys.SpecialCharacters) is { } f ? new SpecialCharacterAppearance(f.Family, f.Size) : setting;

    /// <summary>The Book Browser tree (the indentation and the row spacing are not fonts: the setting's).</summary>
    public BookBrowserAppearance BookBrowser(BookBrowserAppearance setting)
    {
        ThemeFont? items = For(ThemeFontKeys.BookBrowserItems);
        ThemeFont? headers = For(ThemeFontKeys.BookBrowserHeaders);
        return setting with
        {
            ItemsFontFamily = items?.Family ?? setting.ItemsFontFamily,
            ItemsFontSize = items?.Size ?? setting.ItemsFontSize,
            HeaderFontFamily = headers?.Family ?? setting.HeaderFontFamily,
            HeaderFontSize = headers?.Size ?? setting.HeaderFontSize,
        };
    }

    /// <summary>The tooltips of an area.</summary>
    public TooltipFont Tooltip(TooltipArea area, TooltipFont setting) =>
        For(ThemeFontKeys.Tooltip(area)) is { } f ? new TooltipFont(f.Family, f.Size) : setting;
}
