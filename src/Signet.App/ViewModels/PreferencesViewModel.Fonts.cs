using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.Core.Misc;

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

    /// <summary>Appearance › Code Editor › Fonts: the editor font and the opening tag hint font.</summary>
    public FontPageViewModel CodeEditorFonts { get; private set; } = null!;

    /// <summary>Appearance › Preview › Fonts: the standard, serif and sans-serif fonts and their size.</summary>
    public FontPageViewModel PreviewFonts { get; private set; } = null!;

    /// <summary>Appearance › Special Characters › Fonts: the font of the "Insert Special Character" window.</summary>
    public FontPageViewModel SpecialCharacterFonts { get; private set; } = null!;

    /// <summary>Appearance › Start Screen › Fonts: the tooltip font.</summary>
    public FontPageViewModel StartScreenFonts { get; private set; } = null!;

    /// <summary>Appearance › Book Browser › Fonts: the font of the names and cells, of the column headers and the tooltip font.</summary>
    public FontPageViewModel BookBrowserFonts { get; private set; } = null!;

    /// <summary>Appearance › Find Usages Window › Fonts: the column headers, the "Context" column and the other cells.</summary>
    public FontPageViewModel FindUsagesFonts { get; private set; } = null!;

    /// <summary>
    /// Appearance › Saved Searches › Fonts: the "Name" column header, and the tree rows / Details preview / Name field.
    /// </summary>
    public FontPageViewModel SearchEditorFonts { get; private set; } = null!;

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

    /// <summary>Appearance › Notifications › Fonts: the font of the timestamp column and of the message column.</summary>
    public FontPageViewModel NotificationsFonts { get; private set; } = null!;

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
                placeholder: () => UiFontDescription,
                weight: (() => UiFontWeight, SetUiFontWeight)),
            ToolHeaderFontRow(),
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

        CodeEditorAppearance codeEditorDefaults = CodeEditorAppearance.Default;
        CodeEditorFonts = new FontPageViewModel(
            new[]
            {
                new FontSettingViewModel(
                    "CodeEditorText",
                    Strings.Get("PreferencesWindow_CodeEditorTextFont"),
                    Strings.Get("FontPreview_Code"),
                    (() => CodeEditor.FontFamily, v => CodeEditor.FontFamily = v),
                    (() => CodeEditor.FontSize, v => CodeEditor.FontSize = v, FontSizeMin, FontSizeMax),
                    (() => CodeEditor.FontFamily, () => CodeEditor.FontSize),
                    () =>
                    {
                        CodeEditor.FontFamily = codeEditorDefaults.FontFamily;
                        CodeEditor.FontSize = codeEditorDefaults.FontSize;
                        CodeEditor.FontWeight = codeEditorDefaults.FontWeight;
                    },
                    CodeEditor.ChooseFontCommand,
                    codePreview: () => ExtendedHighlighting,
                    weight: (() => CodeEditor.FontWeight, v => CodeEditor.FontWeight = v)),
                LineNumbersFontRow(),
                new FontSettingViewModel(
                    "OpenTagHint",
                    Strings.Get("PreferencesWindow_OpenTagHintFont"),
                    Strings.Get("FontPreview_OpenTagHint") + "\n" + Strings.Format("CodeEditor_OpenTagHintLine", 12),
                    (() => OpenTagHintFontFamily, v => OpenTagHintFontFamily = v),
                    (() => OpenTagHintFontSize, v => OpenTagHintFontSize = v, 0, OpenTagHintAppearance.FontSizeMax),
                    (() => OpenTagHintFontFamily.Length > 0 ? OpenTagHintFontFamily : CodeEditor.FontFamily,
                     () => OpenTagHintFontSize > 0 ? OpenTagHintFontSize : CodeEditor.FontSize),
                    ResetOpenTagHintFont,
                    ChooseOpenTagHintFontCommand,
                    weight: (() => OpenTagHintFontWeight, v => OpenTagHintFontWeight = v),
                    effectiveWeight: () => OpenTagHintFontWeight > 0 ? OpenTagHintFontWeight : CodeEditor.FontWeight,
                    placeholder: () => CodeEditor.FontFamily,
                    previewForeground: () => AppTheme.Current.Foreground(Signet.Core.Themes.ThemeKeys.HintsOpenTag)?.ToString(),
                    previewBackground: () => AppTheme.Current.Background(Signet.Core.Themes.ThemeKeys.HintsOpenTag)?.ToString(),
                    sizeHint: Strings.Get("PreferencesWindow_FontSizeZeroAsEditor")),
                DocumentTabFontRow(),
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
                    SpecialCharacterFontWeight = specialDefaults.FontWeight;
                },
                ChooseSpecialCharacterFontCommand,
                weight: (() => SpecialCharacterFontWeight, v => SpecialCharacterFontWeight = v)),
            TooltipRow(TooltipArea.SpecialCharacters),
        });

        StartScreenFonts = new FontPageViewModel(StartScreenFontRows().Append(TooltipRow(TooltipArea.StartScreen)).ToArray());
        BookBrowserFonts = new FontPageViewModel(new[]
        {
            InterfaceFontRow(
                "BookBrowserItems", "PreferencesWindow_BookBrowserItemsFont", "FontPreview_BookBrowserItems",
                () => BookBrowserItemsFontFamily, v => BookBrowserItemsFontFamily = v,
                () => BookBrowserItemsFontSize, v => BookBrowserItemsFontSize = v,
                () =>
                {
                    BookBrowserItemsFontFamily = string.Empty;
                    BookBrowserItemsFontSize = 0;
                    BookBrowserItemsFontWeight = 0;
                },
                ChooseBookBrowserItemsFontCommand,
                () => BookBrowserItemsFontWeight, v => BookBrowserItemsFontWeight = v),
            InterfaceFontRow(
                "BookBrowserHeaders", "PreferencesWindow_BookBrowserHeadersFont", "FontPreview_BookBrowserHeaders",
                () => BookBrowserHeaderFontFamily, v => BookBrowserHeaderFontFamily = v,
                () => BookBrowserHeaderFontSize, v => BookBrowserHeaderFontSize = v,
                () =>
                {
                    BookBrowserHeaderFontFamily = string.Empty;
                    BookBrowserHeaderFontSize = 0;
                    BookBrowserHeaderFontWeight = 0;
                },
                ChooseBookBrowserHeaderFontCommand,
                () => BookBrowserHeaderFontWeight, v => BookBrowserHeaderFontWeight = v),
            TooltipRow(TooltipArea.BookBrowser),
        });
        FindUsagesFonts = new FontPageViewModel(new[]
        {
            InterfaceFontRow(
                "FindUsagesHeaders", "PreferencesWindow_FindUsagesHeadersFont", "FontPreview_FindUsagesHeaders",
                () => FindUsagesHeaderFontFamily, v => FindUsagesHeaderFontFamily = v,
                () => FindUsagesHeaderFontSize, v => FindUsagesHeaderFontSize = v,
                () =>
                {
                    FindUsagesHeaderFontFamily = string.Empty;
                    FindUsagesHeaderFontSize = 0;
                    FindUsagesHeaderFontWeight = 0;
                },
                ChooseFindUsagesHeaderFontCommand,
                () => FindUsagesHeaderFontWeight, v => FindUsagesHeaderFontWeight = v),
            InterfaceFontRow(
                "FindUsagesContext", "PreferencesWindow_FindUsagesContextFont", "FontPreview_FindUsagesContext",
                () => FindUsagesContextFontFamily, v => FindUsagesContextFontFamily = v,
                () => FindUsagesContextFontSize, v => FindUsagesContextFontSize = v,
                () =>
                {
                    FindUsagesContextFontFamily = string.Empty;
                    FindUsagesContextFontSize = 0;
                    FindUsagesContextFontWeight = 0;
                },
                ChooseFindUsagesContextFontCommand,
                () => FindUsagesContextFontWeight, v => FindUsagesContextFontWeight = v),
            InterfaceFontRow(
                "FindUsagesCells", "PreferencesWindow_FindUsagesCellsFont", "FontPreview_FindUsagesCells",
                () => FindUsagesCellsFontFamily, v => FindUsagesCellsFontFamily = v,
                () => FindUsagesCellsFontSize, v => FindUsagesCellsFontSize = v,
                () =>
                {
                    FindUsagesCellsFontFamily = string.Empty;
                    FindUsagesCellsFontSize = 0;
                    FindUsagesCellsFontWeight = 0;
                },
                ChooseFindUsagesCellsFontCommand,
                () => FindUsagesCellsFontWeight, v => FindUsagesCellsFontWeight = v),
        });
        SearchEditorFonts = new FontPageViewModel(new[]
        {
            InterfaceFontRow(
                "SearchEditorHeader", "PreferencesWindow_SearchEditorHeaderFont", "FontPreview_SearchEditorHeader",
                () => SearchEditorHeaderFontFamily, v => SearchEditorHeaderFontFamily = v,
                () => SearchEditorHeaderFontSize, v => SearchEditorHeaderFontSize = v,
                () =>
                {
                    SearchEditorHeaderFontFamily = string.Empty;
                    SearchEditorHeaderFontSize = 0;
                    SearchEditorHeaderFontWeight = 0;
                },
                ChooseSearchEditorHeaderFontCommand,
                () => SearchEditorHeaderFontWeight, v => SearchEditorHeaderFontWeight = v),
            InterfaceFontRow(
                "SearchEditorRows", "PreferencesWindow_SearchEditorRowsFont", "FontPreview_SearchEditorRows",
                () => SearchEditorRowsFontFamily, v => SearchEditorRowsFontFamily = v,
                () => SearchEditorRowsFontSize, v => SearchEditorRowsFontSize = v,
                () =>
                {
                    SearchEditorRowsFontFamily = string.Empty;
                    SearchEditorRowsFontSize = 0;
                    SearchEditorRowsFontWeight = 0;
                },
                ChooseSearchEditorRowsFontCommand,
                () => SearchEditorRowsFontWeight, v => SearchEditorRowsFontWeight = v),
        });
        NotificationsFonts = new FontPageViewModel(new[] { NotificationsTimestampFontRow(), NotificationsMessageFontRow() });
        TableOfContentsFonts = TooltipPage(TooltipArea.TableOfContents);
        FindReplaceFonts = TooltipPage(TooltipArea.FindReplace);
        CheckpointsFonts = TooltipPage(TooltipArea.Checkpoints);
        ValidationFonts = TooltipPage(TooltipArea.Validation);
        MetadataEditorFonts = TooltipPage(TooltipArea.MetadataEditor);
        CleanupFonts = TooltipPage(TooltipArea.Cleanup);
        FootnotesFonts = TooltipPage(TooltipArea.Footnotes);
        _fontPages.AddRange(new[]
        {
            AppearanceFonts, StartScreenFonts, BookBrowserFonts, TableOfContentsFonts, CodeEditorFonts, PreviewFonts,
            FindReplaceFonts, FindUsagesFonts, SearchEditorFonts, SpecialCharacterFonts, CheckpointsFonts, ValidationFonts, MetadataEditorFonts,
            CleanupFonts, FootnotesFonts, NotificationsFonts,
        });

        // The pages show values that change elsewhere too ("Restore Appearance Defaults", the compact mode): they are
        // refreshed after every change. Their colors are the theme's.
        PropertyChanged += (_, _) => RefreshFontPages();
        CodeEditor.PropertyChanged += (_, _) => CodeEditorFonts.Refresh();
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
            new AsyncRelayCommand(ChooseColorSchemePreviewFont),
            codePreview: () => ExtendedHighlighting,
            weight: (() => Font().FontWeight, v => ColorScheme.PreviewFont = Font() with { FontWeight = v }));
    }

    // "Choose…" of the font of the previews: only monospaced fonts, as for Code Editor.
    private async Task ChooseColorSchemePreviewFont()
    {
        ColorSchemePreviewAppearance font = ColorScheme.PreviewFont;
        await PickFontAsync(
            new FontPickRequest(font.FontFamily, font.FontSize, true, font.FontWeight),
            new FontPickResult(font.FontFamily, font.FontSize, font.FontWeight),
            result =>
            {
                ColorScheme.PreviewFont = new ColorSchemePreviewAppearance(result.Family, result.Size, result.Weight);
                ColorSchemePreviewFont.Refresh();
            });
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
    // Every area but the other windows inherits the font of the other windows (checked by default).
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
            (() => TooltipFamily(area), () => _settings.GetEffectiveTooltipFont(area).FontSize),
            () =>
            {
                _settings.SetTooltipFont(area, TooltipFont.Default);
                _settings.SetTooltipInherit(area, true);
            },
            new AsyncRelayCommand(() => ChooseTooltipFont(area)),
            placeholder: () => EffectiveUiFontFamily,
            inheritance: area == TooltipArea.OtherWindows ? null : TooltipInheritance(area),
            weight: (() => _settings.GetTooltipFont(area).FontWeight,
                     v => _settings.SetTooltipFont(area, _settings.GetTooltipFont(area) with { FontWeight = v })),
            effectiveWeight: () => _settings.GetEffectiveTooltipFont(area).FontWeight);

    // The font the tooltips of an area use now: its own, or the one of the other windows while it inherits.
    private string TooltipFamily(TooltipArea area) =>
        _settings.GetEffectiveTooltipFont(area).FontFamily is { Length: > 0 } family ? family : EffectiveUiFontFamily;

    // The inheritance of an area from "Fonts › Tooltips: other windows" (the page of Appearance › General that holds them; the
    // path starts at "Fonts", the window has one place to set a font).
    private FontInheritance TooltipInheritance(TooltipArea area) => new(
        () => _settings.GetTooltipInherit(area),
        v => _settings.SetTooltipInherit(area, v),
        string.Join(" › ", Strings.Get("PreferencesWindow_Fonts"), Strings.Get("PreferencesWindow_TooltipFontOtherWindows")),
        () =>
        {
            TooltipFont other = _settings.GetTooltipFont(TooltipArea.OtherWindows);
            return (other.FontFamily.Length > 0 ? other.FontFamily : EffectiveUiFontFamily) + ", " + other.FontSize
                + (other.FontWeight > 0 ? ", " + FontWeightScale.NameOf(other.FontWeight) : string.Empty);
        },
        () => AppearanceFonts.Fonts.Single(f => f.Key == "Tooltip" + TooltipArea.OtherWindows));

    // "Choose…" of a tooltip font: the font picker, starting from the font in use.
    private async Task ChooseTooltipFont(TooltipArea area)
    {
        TooltipFont font = _settings.GetTooltipFont(area);
        await PickFontAsync(
            new FontPickRequest(TooltipFamily(area), font.FontSize, false, font.FontWeight),
            new FontPickResult(font.FontFamily, font.FontSize, font.FontWeight),
            result =>
            {
                _settings.SetTooltipFont(area, new TooltipFont(result.Family, result.Size, result.Weight));
                RefreshFontPages();
            });
    }

    private void SetUiFont(string family, int size)
    {
        _settings.UiFont = family;
        _settings.UiFontSize = size;
        NotifyUiFontChanged();
    }

    private void SetUiFontWeight(int weight)
    {
        _settings.UiFontWeight = weight;
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
