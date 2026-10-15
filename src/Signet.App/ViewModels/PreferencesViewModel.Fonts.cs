using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.ViewModels;

/// <summary>
/// The Fonts tabs of the Preferences window (Appearance › &lt;section&gt; › Fonts), as in Rider's Editor › Font: every
/// font setting of the section with a typeface list, a size field, "Choose…" and a preview on the right. The settings
/// stay the properties of this view model; the pages are adapters over them (<see cref="FontSettingViewModel"/>).
/// </summary>
public sealed partial class PreferencesViewModel
{
    private const int FontSizeMin = FontPickerViewModel.MinSize;
    private const int FontSizeMax = FontPickerViewModel.MaxSize;

    /// <summary>
    /// Appearance › General › Fonts: the interface font, the font size of the warnings in dialogs and the tooltip fonts of the
    /// parts without a section of their own (the main window, the document tabs, this window, the other windows).
    /// </summary>
    public FontPageViewModel AppearanceFonts { get; private set; } = null!;

    /// <summary>Appearance › Code View › Fonts: the editor font and the opening tag hint font.</summary>
    public FontPageViewModel CodeViewFonts { get; private set; } = null!;

    /// <summary>Appearance › Preview › Fonts: the standard, serif and sans-serif fonts and their size.</summary>
    public FontPageViewModel PreviewFonts { get; private set; } = null!;

    /// <summary>Appearance › Special Characters › Fonts: the font of the "Insert Special Character" window.</summary>
    public FontPageViewModel SpecialCharacterFonts { get; private set; } = null!;

    /// <summary>Appearance › Start Screen › Fonts: the tooltip font.</summary>
    public FontPageViewModel StartScreenFonts { get; private set; } = null!;

    /// <summary>Appearance › Book Browser › Fonts: the tooltip font.</summary>
    public FontPageViewModel BookBrowserFonts { get; private set; } = null!;

    /// <summary>Appearance › Table of Contents › Fonts: the tooltip font.</summary>
    public FontPageViewModel TableOfContentsFonts { get; private set; } = null!;

    /// <summary>Appearance › Find &amp; Replace › Fonts: the tooltip font.</summary>
    public FontPageViewModel FindReplaceFonts { get; private set; } = null!;

    /// <summary>Appearance › Checkpoints › Fonts: the tooltip font.</summary>
    public FontPageViewModel CheckpointsFonts { get; private set; } = null!;

    /// <summary>Appearance › Validation › Fonts: the tooltip font.</summary>
    public FontPageViewModel ValidationFonts { get; private set; } = null!;

    /// <summary>Appearance › Metadata Editor › Fonts: the tooltip font.</summary>
    public FontPageViewModel MetadataEditorFonts { get; private set; } = null!;

    /// <summary>Appearance › Cleanup › Fonts: the tooltip font.</summary>
    public FontPageViewModel CleanupFonts { get; private set; } = null!;

    /// <summary>Appearance › Footnotes › Fonts: the tooltip font.</summary>
    public FontPageViewModel FootnotesFonts { get; private set; } = null!;

    /// <summary>
    /// Appearance › Color Scheme › Fonts: every font of the "Fonts" pages, grouped by their sections, as the selected
    /// theme sets them (used while the theme turns its fonts on, <see cref="ColorSchemeViewModel.UseThemeFonts"/>).
    /// </summary>
    public FontPageViewModel ThemeFonts { get; private set; } = null!;

    /// <summary>The font of the code previews of the Appearance › Color Scheme pages (a row of the Color Scheme page).</summary>
    public FontSettingViewModel ColorSchemePreviewFont { get; private set; } = null!;

    // Every "Fonts" page, for the refresh after a change made elsewhere.
    private readonly List<FontPageViewModel> _fontPages = new();

    // Called by the constructor once the settings the pages adapt are loaded.
    private void CreateFontPages()
    {
        AppearanceFonts = new FontPageViewModel(new[]
        {
            new FontSettingViewModel(
                "Interface",
                Strings.Get("PreferencesWindow_UiFont"),
                Strings.Get("FontPreview_Interface"),
                (() => UiFontFamily, v => SetUiFont(v, UiFontSize)),
                (() => EffectiveUiFontSize, v => SetUiFont(UiFontFamily, v), FontSizeMin, FontSizeMax),
                (() => EffectiveUiFontFamily, () => EffectiveUiFontSize),
                ResetUiFont,
                ChooseUiFontCommand,
                placeholder: () => UiFontDescription),
            new FontSettingViewModel(
                "Warnings",
                Strings.Get("PreferencesWindow_WarningFontSize"),
                Strings.Get("PreferencesWindow_WarningSample"),
                null,
                (() => WarningFontSize, v => WarningFontSize = v, 0, WarningAppearance.FontSizeMax),
                (() => EffectiveUiFontFamily, () => WarningFontSize > 0 ? WarningFontSize : EffectiveUiFontSize),
                () => WarningFontSize = WarningAppearance.Default.FontSize,
                null,
                previewForeground: () => AppTheme.Current.Color(Signet.Core.Themes.ThemeKeys.UiWarningText)?.ToString()),
            TooltipRow(TooltipArea.MainWindow, "PreferencesWindow_TooltipFontMainWindow"),
            TooltipRow(TooltipArea.DocumentTabs, "PreferencesWindow_TooltipFontDocumentTabs"),
            TooltipRow(TooltipArea.PreferencesWindow, "PreferencesWindow_TooltipFontPreferencesWindow"),
            TooltipRow(TooltipArea.OtherWindows, "PreferencesWindow_TooltipFontOtherWindows"),
        });

        CodeViewAppearance codeViewDefaults = CodeViewAppearance.Default;
        CodeViewFonts = new FontPageViewModel(
            new[]
            {
                new FontSettingViewModel(
                    "CodeViewEditor",
                    Strings.Get("PreferencesWindow_CodeViewEditorFont"),
                    Strings.Get("FontPreview_Code"),
                    (() => CodeView.FontFamily, v => CodeView.FontFamily = v),
                    (() => CodeView.FontSize, v => CodeView.FontSize = v, FontSizeMin, FontSizeMax),
                    (() => CodeView.FontFamily, () => CodeView.FontSize),
                    () =>
                    {
                        CodeView.FontFamily = codeViewDefaults.FontFamily;
                        CodeView.FontSize = codeViewDefaults.FontSize;
                    },
                    CodeView.ChooseFontCommand,
                    codePreview: () => ExtendedHighlighting),
                new FontSettingViewModel(
                    "OpenTagHint",
                    Strings.Get("PreferencesWindow_OpenTagHintFont"),
                    Strings.Get("FontPreview_OpenTagHint") + "\n" + Strings.Format("CodeView_OpenTagHintLine", 12),
                    (() => OpenTagHintFontFamily, v => OpenTagHintFontFamily = v),
                    (() => OpenTagHintFontSize, v => OpenTagHintFontSize = v, 0, OpenTagHintAppearance.FontSizeMax),
                    (() => OpenTagHintFontFamily.Length > 0 ? OpenTagHintFontFamily : CodeView.FontFamily,
                     () => OpenTagHintFontSize > 0 ? OpenTagHintFontSize : CodeView.FontSize),
                    ResetOpenTagHintFont,
                    ChooseOpenTagHintFontCommand,
                    placeholder: () => Strings.Get("PreferencesWindow_OpenTagHintFontDefault"),
                    previewForeground: () => AppTheme.Current.Foreground(Signet.Core.Themes.ThemeKeys.HintsOpenTag)?.ToString(),
                    previewBackground: () => AppTheme.Current.Background(Signet.Core.Themes.ThemeKeys.HintsOpenTag)?.ToString(),
                    sizeHint: Strings.Get("PreferencesWindow_FontSizeZeroAsEditor")),
            },
            Strings.Get("PreferencesWindow_FontColorRestartNotice"));

        PreviewAppearance previewDefaults = PreviewAppearance.Default;
        PreviewFonts = new FontPageViewModel(
            new[]
            {
                PreviewFamily("PreviewStandard", "PreferencesWindow_Standard", () => PreviewFontStandard, v => PreviewFontStandard = v,
                    previewDefaults.FontFamilyStandard, ChoosePreviewFontStandardCommand),
                PreviewFamily("PreviewSerif", "PreferencesWindow_Serif", () => PreviewFontSerif, v => PreviewFontSerif = v,
                    previewDefaults.FontFamilySerif, ChoosePreviewFontSerifCommand),
                PreviewFamily("PreviewSansSerif", "PreferencesWindow_SansSerif", () => PreviewFontSansSerif, v => PreviewFontSansSerif = v,
                    previewDefaults.FontFamilySansSerif, ChoosePreviewFontSansSerifCommand),
                new FontSettingViewModel(
                    "PreviewSize",
                    Strings.Get("PreferencesWindow_Size"),
                    Strings.Get("FontPreview_Book"),
                    null,
                    (() => PreviewFontSize, v => PreviewFontSize = v, FontSizeMin, FontSizeMax),
                    (() => PreviewFontStandard, () => PreviewFontSize),
                    () => PreviewFontSize = previewDefaults.FontSize,
                    null),
                TooltipRow(TooltipArea.Preview),
            });

        SpecialCharacterAppearance specialDefaults = SpecialCharacterAppearance.Default;
        SpecialCharacterFonts = new FontPageViewModel(new[]
        {
            new FontSettingViewModel(
                "SpecialCharacters",
                Strings.Get("PreferencesWindow_SpecialCharacterFont"),
                Strings.Get("FontPreview_SpecialCharacters"),
                (() => SpecialCharacterFontFamily, v => SpecialCharacterFontFamily = v),
                (() => SpecialCharacterFontSize, v => SpecialCharacterFontSize = v, FontSizeMin, FontSizeMax),
                (() => SpecialCharacterFontFamily, () => SpecialCharacterFontSize),
                () =>
                {
                    SpecialCharacterFontFamily = specialDefaults.FontFamily;
                    SpecialCharacterFontSize = specialDefaults.FontSize;
                },
                ChooseSpecialCharacterFontCommand),
            TooltipRow(TooltipArea.SpecialCharacters),
        });

        StartScreenFonts = TooltipPage(TooltipArea.StartScreen);
        BookBrowserFonts = TooltipPage(TooltipArea.BookBrowser);
        TableOfContentsFonts = TooltipPage(TooltipArea.TableOfContents);
        FindReplaceFonts = TooltipPage(TooltipArea.FindReplace);
        CheckpointsFonts = TooltipPage(TooltipArea.Checkpoints);
        ValidationFonts = TooltipPage(TooltipArea.Validation);
        MetadataEditorFonts = TooltipPage(TooltipArea.MetadataEditor);
        CleanupFonts = TooltipPage(TooltipArea.Cleanup);
        FootnotesFonts = TooltipPage(TooltipArea.Footnotes);
        _fontPages.AddRange(new[]
        {
            AppearanceFonts, StartScreenFonts, BookBrowserFonts, TableOfContentsFonts, CodeViewFonts, PreviewFonts,
            FindReplaceFonts, SpecialCharacterFonts, CheckpointsFonts, ValidationFonts, MetadataEditorFonts, CleanupFonts,
            FootnotesFonts,
        });

        // The pages show values that change elsewhere too ("Restore Appearance Defaults", the compact mode): they are
        // refreshed after every change. Their colors are the theme's.
        PropertyChanged += (_, _) => RefreshFontPages();
        CodeView.PropertyChanged += (_, _) => CodeViewFonts.Refresh();
    }

    // Appearance › Color Scheme › Fonts — after the "Fonts" pages (it mirrors their rows) and the Color Scheme page.
    private void CreateThemeFontsPage()
    {
        ThemeFonts = new FontPageViewModel(new (FontPageViewModel Page, string Group)[]
            {
                (AppearanceFonts, "PreferencesWindow_General"),
                (StartScreenFonts, "PreferencesWindow_StartScreen"),
                (BookBrowserFonts, "PreferencesWindow_BookBrowser"),
                (TableOfContentsFonts, "PreferencesWindow_TableOfContents"),
                (CodeViewFonts, "PreferencesWindow_CodeView"),
                (PreviewFonts, "PreferencesWindow_Preview"),
                (FindReplaceFonts, "PreferencesWindow_FindReplace"),
                (SpecialCharacterFonts, "PreferencesWindow_SpecialCharacters"),
                (CheckpointsFonts, "PreferencesWindow_Checkpoints"),
                (ValidationFonts, "PreferencesWindow_Validation"),
                (MetadataEditorFonts, "PreferencesWindow_MetadataEditor"),
                (CleanupFonts, "PreferencesWindow_Cleanup"),
                (FootnotesFonts, "PreferencesWindow_Footnotes"),
            }
            .SelectMany(p => p.Page.Fonts.Select(f => ThemeFontRow(f, Strings.Get(p.Group))))
            .ToArray());
        ColorScheme.ThemeEdited += (_, _) => ThemeFonts.Refresh();

        // Its rows show the fonts of the settings where the theme has none: refreshed with the other pages.
        _fontPages.Add(ThemeFonts);
    }

    // Appearance › Color Scheme: the font of the code previews of its pages — a setting of the program, not of the theme.
    private void CreateColorSchemePreviewFont()
    {
        ColorSchemePreviewAppearance Font() => ColorScheme.PreviewFont;
        ColorSchemePreviewFont = new FontSettingViewModel(
            "ColorSchemePreview",
            Strings.Get("ColorScheme_PreviewFont"),
            Strings.Get("FontPreview_Code"),
            (() => Font().FontFamily, v => ColorScheme.PreviewFont = Font() with { FontFamily = v }),
            (() => Font().FontSize, v => ColorScheme.PreviewFont = Font() with { FontSize = v }, FontSizeMin, FontSizeMax),
            (() => Font().FontFamily, () => Font().FontSize),
            () => ColorScheme.PreviewFont = ColorSchemePreviewAppearance.Default,
            new AsyncRelayCommand(ChooseColorSchemePreviewFont));
    }

    // "Choose…" of the font of the previews: only monospaced fonts, as for Code View.
    private async Task ChooseColorSchemePreviewFont()
    {
        ColorSchemePreviewAppearance font = ColorScheme.PreviewFont;
        if (await PickFontAsync(new FontPickRequest(font.FontFamily, font.FontSize, true)) is { } result)
        {
            ColorScheme.PreviewFont = new ColorSchemePreviewAppearance(result.Family, result.Size);
            ColorSchemePreviewFont.Refresh();
        }
    }

    // The row of a font of the theme: the row of the "Fonts" page it mirrors (label, sample, limits, preview), with the
    // font kept in the selected theme. An empty typeface or a size of 0 means what it means on the "Fonts" page (the
    // default of the place), shown in the preview as the place resolves it now.
    private FontSettingViewModel ThemeFontRow(FontSettingViewModel source, string group)
    {
        string key = ThemeFontKey(source.Key);
        ThemeFont Font() => ColorScheme.FontOf(key);
        return new FontSettingViewModel(
            source.Key,
            source.Label,
            source.PreviewText,
            source.HasFamily ? (() => Font().Family, v => ColorScheme.SetFont(key, Font() with { Family = v })) : null,
            source.HasSize ? (() => Font().Size, v => ColorScheme.SetFont(key, Font() with { Size = v }), source.MinSize, source.MaxSize) : null,
            (() => Font().Family is { Length: > 0 } family ? family : source.EffectiveFamily,
             () => Font().Size > 0 ? Font().Size : source.EffectiveSize),
            () => ColorScheme.RestoreFont(key),
            source.HasFamily ? new AsyncRelayCommand(() => ChooseThemeFont(key, source)) : null,
            placeholder: () => Font().Family is { Length: > 0 } family ? family : source.Placeholder,
            previewForeground: () => source.PreviewForeground,
            previewBackground: () => source.PreviewBackground,
            sizeHint: source.SizeHint,
            codePreview: source.IsCodePreview ? () => source.CodePreviewExtended : null)
        {
            Group = group,
        };
    }

    // "Choose…" of a font of the theme.
    private async Task ChooseThemeFont(string key, FontSettingViewModel source)
    {
        ThemeFont font = ColorScheme.FontOf(key);
        string family = font.Family.Length > 0 ? font.Family : source.EffectiveFamily;
        int? size = source.HasSize ? (font.Size > 0 ? font.Size : (int)source.EffectiveSize) : null;
        if (await PickFontAsync(new FontPickRequest(family, size, false)) is { } result)
        {
            ColorScheme.SetFont(key, new ThemeFont(result.Family, source.HasSize ? result.Size : font.Size));
            ThemeFonts.Refresh();
        }
    }

    // The place of a theme font (ThemeFontKeys) for the key of a row of the "Fonts" pages.
    private static string ThemeFontKey(string rowKey) => rowKey switch
    {
        "Interface" => ThemeFontKeys.Interface,
        "Warnings" => ThemeFontKeys.Warnings,
        "CodeViewEditor" => ThemeFontKeys.CodeViewEditor,
        "OpenTagHint" => ThemeFontKeys.OpenTagHint,
        "PreviewStandard" => ThemeFontKeys.PreviewStandard,
        "PreviewSerif" => ThemeFontKeys.PreviewSerif,
        "PreviewSansSerif" => ThemeFontKeys.PreviewSansSerif,
        "PreviewSize" => ThemeFontKeys.PreviewSize,
        "SpecialCharacters" => ThemeFontKeys.SpecialCharacters,
        _ when rowKey.StartsWith("Tooltip", System.StringComparison.Ordinal)
            && System.Enum.TryParse(rowKey["Tooltip".Length..], out TooltipArea area) => ThemeFontKeys.Tooltip(area),
        _ => throw new System.ArgumentOutOfRangeException(nameof(rowKey), rowKey, "A font row without a place in the theme."),
    };

    // A typeface of the preview: the size is shared (a row of its own).
    private FontSettingViewModel PreviewFamily(
        string key, string labelKey, System.Func<string> get, System.Action<string> set, string defaultFamily,
        System.Windows.Input.ICommand choose) =>
        new(
            key,
            Strings.Get(labelKey),
            Strings.Get("FontPreview_Book"),
            (get, set),
            null,
            (get, () => PreviewFontSize),
            () => set(defaultFamily),
            choose);

    // The "Fonts" page of a section whose only font is the one of its tooltips.
    private FontPageViewModel TooltipPage(TooltipArea area) => new(new[] { TooltipRow(area) });

    // The tooltip font of an area: the typeface (empty = the interface font) and the size.
    private FontSettingViewModel TooltipRow(TooltipArea area, string labelKey = "PreferencesWindow_TooltipFont") =>
        new(
            "Tooltip" + area,
            Strings.Get(labelKey),
            Strings.Get("FontPreview_Tooltip" + area),
            (() => _settings.GetTooltipFont(area).FontFamily,
             v => _settings.SetTooltipFont(area, _settings.GetTooltipFont(area) with { FontFamily = v })),
            (() => _settings.GetTooltipFont(area).FontSize,
             v => _settings.SetTooltipFont(area, _settings.GetTooltipFont(area) with { FontSize = v }),
             TooltipFont.FontSizeMin, TooltipFont.FontSizeMax),
            (() => TooltipFamily(area), () => _settings.GetTooltipFont(area).FontSize),
            () => _settings.SetTooltipFont(area, TooltipFont.Default),
            new AsyncRelayCommand(() => ChooseTooltipFont(area)),
            placeholder: () => Strings.Get("PreferencesWindow_TooltipFontDefault"));

    private string TooltipFamily(TooltipArea area) =>
        _settings.GetTooltipFont(area).FontFamily is { Length: > 0 } family ? family : EffectiveUiFontFamily;

    // "Choose…" of a tooltip font: the font picker, starting from the font in use.
    private async Task ChooseTooltipFont(TooltipArea area)
    {
        TooltipFont font = _settings.GetTooltipFont(area);
        if (await PickFontAsync(new FontPickRequest(TooltipFamily(area), font.FontSize, false)) is { } result)
        {
            _settings.SetTooltipFont(area, new TooltipFont(result.Family, result.Size));
            RefreshFontPages();
        }
    }

    private void SetUiFont(string family, int size)
    {
        _settings.UiFont = family;
        _settings.UiFontSize = size;
        NotifyUiFontChanged();
    }

    private void RefreshFontPages()
    {
        foreach (FontPageViewModel page in _fontPages)
        {
            page.Refresh();
        }
    }
}
