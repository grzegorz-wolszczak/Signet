using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>
/// The "Fonts" pages of the Preferences window (Appearance › Fonts of the sections), as in Rider's Editor › Font: every
/// font setting of the section with a typeface list, a size field, "Choose…" and a preview on the right. The settings
/// stay the properties of this view model; the pages are adapters over them (<see cref="FontSettingViewModel"/>).
/// </summary>
public sealed partial class PreferencesViewModel
{
    private const int FontSizeMin = FontPickerViewModel.MinSize;
    private const int FontSizeMax = FontPickerViewModel.MaxSize;

    /// <summary>
    /// Appearance › Fonts: the interface font, the font size of the warnings in dialogs and the tooltip fonts of the
    /// parts without a section of their own (the main window, the document tabs, this window, the other windows).
    /// </summary>
    public FontPageViewModel AppearanceFonts { get; private set; } = null!;

    /// <summary>Code View › Appearance › Fonts: the editor font and the opening tag hint font.</summary>
    public FontPageViewModel CodeViewFonts { get; private set; } = null!;

    /// <summary>Preview › Appearance › Fonts: the standard, serif and sans-serif fonts and their size.</summary>
    public FontPageViewModel PreviewFonts { get; private set; } = null!;

    /// <summary>Special Characters › Appearance › Fonts: the font of the "Insert Special Character" window.</summary>
    public FontPageViewModel SpecialCharacterFonts { get; private set; } = null!;

    /// <summary>Start Screen › Appearance › Fonts: the tooltip font.</summary>
    public FontPageViewModel StartScreenFonts { get; private set; } = null!;

    /// <summary>Book Browser › Appearance › Fonts: the tooltip font.</summary>
    public FontPageViewModel BookBrowserFonts { get; private set; } = null!;

    /// <summary>Table of Contents › Appearance › Fonts: the tooltip font.</summary>
    public FontPageViewModel TableOfContentsFonts { get; private set; } = null!;

    /// <summary>Find &amp; Replace › Appearance › Fonts: the tooltip font.</summary>
    public FontPageViewModel FindReplaceFonts { get; private set; } = null!;

    /// <summary>Checkpoints › Appearance › Fonts: the tooltip font.</summary>
    public FontPageViewModel CheckpointsFonts { get; private set; } = null!;

    /// <summary>Validation › Appearance › Fonts: the tooltip font.</summary>
    public FontPageViewModel ValidationFonts { get; private set; } = null!;

    /// <summary>Metadata Editor › Appearance › Fonts: the tooltip font.</summary>
    public FontPageViewModel MetadataEditorFonts { get; private set; } = null!;

    /// <summary>Cleanup › Appearance › Fonts: the tooltip font.</summary>
    public FontPageViewModel CleanupFonts { get; private set; } = null!;

    /// <summary>Footnotes › Appearance › Fonts: the tooltip font.</summary>
    public FontPageViewModel FootnotesFonts { get; private set; } = null!;

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
