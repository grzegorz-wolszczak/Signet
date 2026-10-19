using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Start Screen: the fonts and the indentation of the list of recent files.</summary>
public sealed partial class PreferencesViewModel
{
    // The size of the folder of a recent file while its size is not set (the start screen draws it a little smaller).
    private const int StartScreenLocationDefaultSize = 11;

    // The weight of the heading of the list while its weight is not set (semi-bold).
    private const int StartScreenSectionDefaultWeight = 600;

    private StartScreenViewModel? _startScreenSample;

    /// <summary>
    /// The start screen of the preview of the Start Screen pages: the real view model with fabricated books, reading the look
    /// from the settings of this window (the draft), so every change shows in it at once. Made when first asked for.
    /// </summary>
    public StartScreenViewModel StartScreenSample => _startScreenSample ??= StartScreenViewModel.CreateSample(_settings);

    /// <summary>Lower bound of <see cref="StartScreenIndent"/>.</summary>
    public static decimal StartScreenIndentMin => StartScreenAppearance.IndentMin;

    /// <summary>Upper bound of <see cref="StartScreenIndent"/>.</summary>
    public static decimal StartScreenIndentMax => StartScreenAppearance.IndentMax;

    /// <summary>Upper bound of <see cref="RecentFilesLimit"/>.</summary>
    public static decimal RecentFilesLimitMax => SettingsStore.RecentFilesMenuMax;

    /// <summary>Upper bound of <see cref="StartScreenRecentFilesLimit"/>.</summary>
    public static decimal StartScreenRecentFilesLimitMax => SettingsStore.RecentFilesMax;

    /// <summary>Indentation (px) of the entries of the list of recent files under its heading.</summary>
    [ObservableProperty]
    private int _startScreenIndent;

    /// <summary>Typeface of the name of a recent file; empty = the interface font.</summary>
    [ObservableProperty]
    private string _startScreenTitleFontFamily = string.Empty;

    /// <summary>Size (px) of the name of a recent file; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _startScreenTitleFontSize;

    /// <summary>Weight of the name of a recent file; 0 = not set.</summary>
    [ObservableProperty]
    private int _startScreenTitleFontWeight;

    /// <summary>Typeface of the folder of a recent file; empty = the interface font.</summary>
    [ObservableProperty]
    private string _startScreenLocationFontFamily = string.Empty;

    /// <summary>Size (px) of the folder of a recent file; 0 = the default (11).</summary>
    [ObservableProperty]
    private int _startScreenLocationFontSize;

    /// <summary>Weight of the folder of a recent file; 0 = not set.</summary>
    [ObservableProperty]
    private int _startScreenLocationFontWeight;

    /// <summary>Typeface of the heading of the list ("Recent files"); empty = the interface font.</summary>
    [ObservableProperty]
    private string _startScreenSectionFontFamily = string.Empty;

    /// <summary>Size (px) of the heading of the list; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _startScreenSectionFontSize;

    /// <summary>Weight of the heading of the list; 0 = not set (semi-bold).</summary>
    [ObservableProperty]
    private int _startScreenSectionFontWeight;

    partial void OnStartScreenIndentChanged(int value) => PersistStartScreenAppearance();

    partial void OnStartScreenTitleFontFamilyChanged(string value) => PersistStartScreenAppearance();

    partial void OnStartScreenTitleFontSizeChanged(int value) => PersistStartScreenAppearance();

    partial void OnStartScreenTitleFontWeightChanged(int value) => PersistStartScreenAppearance();

    partial void OnStartScreenLocationFontFamilyChanged(string value) => PersistStartScreenAppearance();

    partial void OnStartScreenLocationFontSizeChanged(int value) => PersistStartScreenAppearance();

    partial void OnStartScreenLocationFontWeightChanged(int value) => PersistStartScreenAppearance();

    partial void OnStartScreenSectionFontFamilyChanged(string value) => PersistStartScreenAppearance();

    partial void OnStartScreenSectionFontSizeChanged(int value) => PersistStartScreenAppearance();

    partial void OnStartScreenSectionFontWeightChanged(int value) => PersistStartScreenAppearance();

    // Reads the look of the list from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadStartScreenAppearance()
    {
        StartScreenAppearance look = _settings.StartScreenAppearance;
        StartScreenIndent = look.Indent;
        StartScreenTitleFontFamily = look.TitleFontFamily;
        StartScreenTitleFontSize = look.TitleFontSize;
        StartScreenTitleFontWeight = look.TitleFontWeight;
        StartScreenLocationFontFamily = look.LocationFontFamily;
        StartScreenLocationFontSize = look.LocationFontSize;
        StartScreenLocationFontWeight = look.LocationFontWeight;
        StartScreenSectionFontFamily = look.SectionFontFamily;
        StartScreenSectionFontSize = look.SectionFontSize;
        StartScreenSectionFontWeight = look.SectionFontWeight;
    }

    private void PersistStartScreenAppearance() =>
        _settings.StartScreenAppearance = new StartScreenAppearance(
            StartScreenTitleFontFamily, StartScreenTitleFontSize,
            StartScreenLocationFontFamily, StartScreenLocationFontSize,
            StartScreenSectionFontFamily, StartScreenSectionFontSize,
            StartScreenIndent,
            StartScreenTitleFontWeight, StartScreenLocationFontWeight, StartScreenSectionFontWeight);

    /// <summary>"Choose…" of the font of the names of the recent files.</summary>
    [RelayCommand]
    private Task ChooseStartScreenTitleFont() => ChooseStartScreenFont(0);

    /// <summary>"Choose…" of the font of the folders of the recent files.</summary>
    [RelayCommand]
    private Task ChooseStartScreenLocationFont() => ChooseStartScreenFont(1);

    /// <summary>"Choose…" of the font of the heading of the list.</summary>
    [RelayCommand]
    private Task ChooseStartScreenSectionFont() => ChooseStartScreenFont(2);

    // The font picker for one of the three fonts (0 = the names, 1 = the folders, 2 = the heading), starting from the font in use.
    private async Task ChooseStartScreenFont(int which)
    {
        (string family, int size, int weight) = which switch
        {
            0 => (StartScreenTitleFontFamily, StartScreenTitleFontSize, StartScreenTitleFontWeight),
            1 => (StartScreenLocationFontFamily, StartScreenLocationFontSize, StartScreenLocationFontWeight),
            _ => (StartScreenSectionFontFamily, StartScreenSectionFontSize, StartScreenSectionFontWeight),
        };
        int sizeInUse = size > 0 ? size : which == 1 ? StartScreenLocationDefaultSize : EffectiveUiFontSize;
        int weightInUse = weight > 0 || which != 2 ? weight : StartScreenSectionDefaultWeight;
        await PickFontAsync(
            new FontPickRequest(family.Length > 0 ? family : EffectiveUiFontFamily, sizeInUse, false, weightInUse),
            new FontPickResult(family, size, weight),
            result =>
            {
                switch (which)
                {
                    case 0:
                        StartScreenTitleFontFamily = result.Family;
                        StartScreenTitleFontSize = result.Size;
                        StartScreenTitleFontWeight = result.Weight;
                        break;
                    case 1:
                        StartScreenLocationFontFamily = result.Family;
                        StartScreenLocationFontSize = result.Size;
                        StartScreenLocationFontWeight = result.Weight;
                        break;
                    default:
                        StartScreenSectionFontFamily = result.Family;
                        StartScreenSectionFontSize = result.Size;
                        StartScreenSectionFontWeight = result.Weight;
                        break;
                }
            });
    }

    // The three font rows of the Fonts page of the start screen: the names and the folders of the recent files and the heading.
    private FontSettingViewModel[] StartScreenFontRows() =>
        new[]
        {
            InterfaceFontRow(
                "StartScreenTitles", "PreferencesWindow_StartScreenTitleFont", "FontPreview_StartScreenTitle",
                () => StartScreenTitleFontFamily, v => StartScreenTitleFontFamily = v,
                () => StartScreenTitleFontSize, v => StartScreenTitleFontSize = v,
                () =>
                {
                    StartScreenTitleFontFamily = string.Empty;
                    StartScreenTitleFontSize = 0;
                    StartScreenTitleFontWeight = 0;
                },
                ChooseStartScreenTitleFontCommand,
                () => StartScreenTitleFontWeight, v => StartScreenTitleFontWeight = v),
            InterfaceFontRow(
                "StartScreenLocations", "PreferencesWindow_StartScreenLocationFont", "FontPreview_StartScreenLocation",
                () => StartScreenLocationFontFamily, v => StartScreenLocationFontFamily = v,
                () => StartScreenLocationFontSize, v => StartScreenLocationFontSize = v,
                () =>
                {
                    StartScreenLocationFontFamily = string.Empty;
                    StartScreenLocationFontSize = 0;
                    StartScreenLocationFontWeight = 0;
                },
                ChooseStartScreenLocationFontCommand,
                () => StartScreenLocationFontWeight, v => StartScreenLocationFontWeight = v,
                defaultSize: StartScreenLocationDefaultSize, sizeHintKey: "PreferencesWindow_FontSizeZeroAsDefault11"),
            InterfaceFontRow(
                "StartScreenSection", "PreferencesWindow_StartScreenSectionFont", "FontPreview_StartScreenSection",
                () => StartScreenSectionFontFamily, v => StartScreenSectionFontFamily = v,
                () => StartScreenSectionFontSize, v => StartScreenSectionFontSize = v,
                () =>
                {
                    StartScreenSectionFontFamily = string.Empty;
                    StartScreenSectionFontSize = 0;
                    StartScreenSectionFontWeight = 0;
                },
                ChooseStartScreenSectionFontCommand,
                () => StartScreenSectionFontWeight, v => StartScreenSectionFontWeight = v,
                defaultWeight: StartScreenSectionDefaultWeight),
        };
}
