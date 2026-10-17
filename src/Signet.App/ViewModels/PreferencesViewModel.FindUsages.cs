using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>Appearance and Behavior › Find Usages Window › Fonts: the column headers, the "Context" column and the other cells.</summary>
public sealed partial class PreferencesViewModel
{
    /// <summary>Typeface of the column headers of the panel; empty = the interface font.</summary>
    [ObservableProperty]
    private string _findUsagesHeaderFontFamily = string.Empty;

    /// <summary>Size (px) of the column headers of the panel; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _findUsagesHeaderFontSize;

    /// <summary>Typeface of the "Context" column (e.g. a fixed-width one for the code); empty = the interface font.</summary>
    [ObservableProperty]
    private string _findUsagesContextFontFamily = string.Empty;

    /// <summary>Size (px) of the "Context" column; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _findUsagesContextFontSize;

    /// <summary>Typeface of the other cells and of the heading above the tree; empty = the interface font.</summary>
    [ObservableProperty]
    private string _findUsagesCellsFontFamily = string.Empty;

    /// <summary>Size (px) of the other cells and of the heading; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _findUsagesCellsFontSize;

    /// <summary>Weight of the column headers of the panel; 0 = not set.</summary>
    [ObservableProperty]
    private int _findUsagesHeaderFontWeight;

    /// <summary>Weight of the "Context" column; 0 = not set.</summary>
    [ObservableProperty]
    private int _findUsagesContextFontWeight;

    /// <summary>Weight of the other cells and of the heading; 0 = not set.</summary>
    [ObservableProperty]
    private int _findUsagesCellsFontWeight;

    partial void OnFindUsagesHeaderFontWeightChanged(int value) => PersistFindUsagesFonts();

    partial void OnFindUsagesContextFontWeightChanged(int value) => PersistFindUsagesFonts();

    partial void OnFindUsagesCellsFontWeightChanged(int value) => PersistFindUsagesFonts();

    partial void OnFindUsagesHeaderFontFamilyChanged(string value) => PersistFindUsagesFonts();

    partial void OnFindUsagesHeaderFontSizeChanged(int value) => PersistFindUsagesFonts();

    partial void OnFindUsagesContextFontFamilyChanged(string value) => PersistFindUsagesFonts();

    partial void OnFindUsagesContextFontSizeChanged(int value) => PersistFindUsagesFonts();

    partial void OnFindUsagesCellsFontFamilyChanged(string value) => PersistFindUsagesFonts();

    partial void OnFindUsagesCellsFontSizeChanged(int value) => PersistFindUsagesFonts();

    // Reads the fonts from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadFindUsagesFonts()
    {
        FindUsagesAppearance look = _settings.FindUsagesAppearance;
        FindUsagesHeaderFontFamily = look.HeaderFontFamily;
        FindUsagesHeaderFontSize = look.HeaderFontSize;
        FindUsagesContextFontFamily = look.ContextFontFamily;
        FindUsagesContextFontSize = look.ContextFontSize;
        FindUsagesCellsFontFamily = look.CellsFontFamily;
        FindUsagesCellsFontSize = look.CellsFontSize;
        FindUsagesHeaderFontWeight = look.HeaderFontWeight;
        FindUsagesContextFontWeight = look.ContextFontWeight;
        FindUsagesCellsFontWeight = look.CellsFontWeight;
    }

    private void PersistFindUsagesFonts() =>
        _settings.FindUsagesAppearance = new FindUsagesAppearance(
            FindUsagesHeaderFontFamily, FindUsagesHeaderFontSize,
            FindUsagesContextFontFamily, FindUsagesContextFontSize,
            FindUsagesCellsFontFamily, FindUsagesCellsFontSize,
            FindUsagesHeaderFontWeight, FindUsagesContextFontWeight, FindUsagesCellsFontWeight);

    /// <summary>"Choose…" of the font of the column headers of the panel.</summary>
    [RelayCommand]
    private Task ChooseFindUsagesHeaderFont() =>
        ChooseFindUsagesFont(FindUsagesHeaderFontFamily, FindUsagesHeaderFontSize, FindUsagesHeaderFontWeight, (f, s, w) =>
        {
            FindUsagesHeaderFontFamily = f;
            FindUsagesHeaderFontSize = s;
            FindUsagesHeaderFontWeight = w;
        });

    /// <summary>"Choose…" of the font of the "Context" column of the panel.</summary>
    [RelayCommand]
    private Task ChooseFindUsagesContextFont() =>
        ChooseFindUsagesFont(FindUsagesContextFontFamily, FindUsagesContextFontSize, FindUsagesContextFontWeight, (f, s, w) =>
        {
            FindUsagesContextFontFamily = f;
            FindUsagesContextFontSize = s;
            FindUsagesContextFontWeight = w;
        });

    /// <summary>"Choose…" of the font of the other cells of the panel.</summary>
    [RelayCommand]
    private Task ChooseFindUsagesCellsFont() =>
        ChooseFindUsagesFont(FindUsagesCellsFontFamily, FindUsagesCellsFontSize, FindUsagesCellsFontWeight, (f, s, w) =>
        {
            FindUsagesCellsFontFamily = f;
            FindUsagesCellsFontSize = s;
            FindUsagesCellsFontWeight = w;
        });

    // The font picker, starting from the font in use (the interface font while the setting follows it).
    private async Task ChooseFindUsagesFont(string family, int size, int weight, System.Action<string, int, int> apply)
    {
        await PickFontAsync(
            new FontPickRequest(family.Length > 0 ? family : EffectiveUiFontFamily, size > 0 ? size : EffectiveUiFontSize, false, weight),
            new FontPickResult(family, size, weight),
            result => apply(result.Family, result.Size, result.Weight));
    }
}
