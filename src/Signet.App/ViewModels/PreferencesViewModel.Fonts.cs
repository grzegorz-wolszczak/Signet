using System.ComponentModel;
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

    /// <summary>Appearance › Fonts: the interface font and the font size of the warnings in dialogs.</summary>
    public FontPageViewModel AppearanceFonts { get; private set; } = null!;

    /// <summary>Code View › Appearance › Fonts: the editor font and the opening tag hint font.</summary>
    public FontPageViewModel CodeViewFonts { get; private set; } = null!;

    /// <summary>Preview › Appearance › Fonts: the standard, serif and sans-serif fonts and their size.</summary>
    public FontPageViewModel PreviewFonts { get; private set; } = null!;

    /// <summary>Special Characters › Appearance › Fonts: the font of the "Insert Special Character" window.</summary>
    public FontPageViewModel SpecialCharacterFonts { get; private set; } = null!;

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
                previewForeground: () => WarningColor.Value),
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
                    CodeView.ChooseFontCommand),
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
                    previewForeground: () => OpenTagHintForeground.Value,
                    previewBackground: () => OpenTagHintBackground.Value,
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
            },
            Strings.Get("PreferencesWindow_FontColorRestartNotice"));

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
        });

        // The pages show values that change elsewhere too ("Restore Appearance Defaults", the compact mode, the colors
        // of the warning and of the hint): they are refreshed after every change.
        PropertyChanged += (_, _) => RefreshFontPages();
        CodeView.PropertyChanged += (_, _) => CodeViewFonts.Refresh();
        foreach (INotifyPropertyChanged color in new INotifyPropertyChanged[] { WarningColor, OpenTagHintBackground, OpenTagHintForeground })
        {
            color.PropertyChanged += (_, _) => RefreshFontPages();
        }
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

    private void SetUiFont(string family, int size)
    {
        _settings.UiFont = family;
        _settings.UiFontSize = size;
        NotifyUiFontChanged();
    }

    private void RefreshFontPages()
    {
        foreach (FontPageViewModel page in new[] { AppearanceFonts, CodeViewFonts, PreviewFonts, SpecialCharacterFonts })
        {
            page?.Refresh();
        }
    }
}
