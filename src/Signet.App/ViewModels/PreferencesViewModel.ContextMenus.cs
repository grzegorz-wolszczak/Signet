using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>
/// Appearance › Context Menus: the fonts of the title of a context menu and of its items (the background of the title is
/// a color of the theme, Appearance › Interface Colors).
/// </summary>
public sealed partial class PreferencesViewModel
{
    // The weight of the title while its weight is not set (bold).
    private const int ContextMenuTitleDefaultWeight = 700;

    /// <summary>Typeface of the title of a context menu; empty = the font of the items.</summary>
    [ObservableProperty]
    private string _contextMenuTitleFontFamily = string.Empty;

    /// <summary>Size (px) of the title; 0 = the size of the items.</summary>
    [ObservableProperty]
    private int _contextMenuTitleFontSize;

    /// <summary>Weight of the title; 0 = bold.</summary>
    [ObservableProperty]
    private int _contextMenuTitleFontWeight;

    /// <summary>Typeface of the items of a context menu; empty = the interface font.</summary>
    [ObservableProperty]
    private string _contextMenuItemsFontFamily = string.Empty;

    /// <summary>Size (px) of the items; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _contextMenuItemsFontSize;

    /// <summary>Weight of the items; 0 = that of the interface.</summary>
    [ObservableProperty]
    private int _contextMenuItemsFontWeight;

    /// <summary>Appearance › Context Menus › Fonts: the title and the items.</summary>
    public FontPageViewModel ContextMenuFonts { get; private set; } = null!;

    partial void OnContextMenuTitleFontFamilyChanged(string value) => PersistContextMenuAppearance();

    partial void OnContextMenuTitleFontSizeChanged(int value) => PersistContextMenuAppearance();

    partial void OnContextMenuTitleFontWeightChanged(int value) => PersistContextMenuAppearance();

    partial void OnContextMenuItemsFontFamilyChanged(string value) => PersistContextMenuAppearance();

    partial void OnContextMenuItemsFontSizeChanged(int value) => PersistContextMenuAppearance();

    partial void OnContextMenuItemsFontWeightChanged(int value) => PersistContextMenuAppearance();

    // Reads the fonts from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadContextMenuAppearance()
    {
        ContextMenuAppearance look = _settings.ContextMenuAppearance;
        ContextMenuTitleFontFamily = look.TitleFontFamily;
        ContextMenuTitleFontSize = look.TitleFontSize;
        ContextMenuTitleFontWeight = look.TitleFontWeight;
        ContextMenuItemsFontFamily = look.ItemsFontFamily;
        ContextMenuItemsFontSize = look.ItemsFontSize;
        ContextMenuItemsFontWeight = look.ItemsFontWeight;
    }

    private void PersistContextMenuAppearance() =>
        _settings.ContextMenuAppearance = new ContextMenuAppearance(
            ContextMenuTitleFontFamily, ContextMenuTitleFontSize, ContextMenuTitleFontWeight,
            ContextMenuItemsFontFamily, ContextMenuItemsFontSize, ContextMenuItemsFontWeight);

    /// <summary>"Choose…" of the font of the title.</summary>
    [RelayCommand]
    private Task ChooseContextMenuTitleFont() => PickFontAsync(
        new FontPickRequest(
            ContextMenuTitleFontFamily.Length > 0 ? ContextMenuTitleFontFamily : ContextMenuItemsFamilyInUse(),
            ContextMenuTitleFontSize > 0 ? ContextMenuTitleFontSize : ContextMenuItemsSizeInUse(),
            false,
            ContextMenuTitleFontWeight > 0 ? ContextMenuTitleFontWeight : ContextMenuTitleDefaultWeight),
        new FontPickResult(ContextMenuTitleFontFamily, ContextMenuTitleFontSize, ContextMenuTitleFontWeight),
        result =>
        {
            ContextMenuTitleFontFamily = result.Family;
            ContextMenuTitleFontSize = result.Size;
            ContextMenuTitleFontWeight = result.Weight;
        });

    /// <summary>"Choose…" of the font of the items.</summary>
    [RelayCommand]
    private Task ChooseContextMenuItemsFont() => PickFontAsync(
        new FontPickRequest(ContextMenuItemsFamilyInUse(), ContextMenuItemsSizeInUse(), false, ContextMenuItemsFontWeight),
        new FontPickResult(ContextMenuItemsFontFamily, ContextMenuItemsFontSize, ContextMenuItemsFontWeight),
        result =>
        {
            ContextMenuItemsFontFamily = result.Family;
            ContextMenuItemsFontSize = result.Size;
            ContextMenuItemsFontWeight = result.Weight;
        });

    private string ContextMenuItemsFamilyInUse() => ContextMenuItemsFontFamily.Length > 0 ? ContextMenuItemsFontFamily : EffectiveUiFontFamily;

    private int ContextMenuItemsSizeInUse() => ContextMenuItemsFontSize > 0 ? ContextMenuItemsFontSize : EffectiveUiFontSize;

    // The two font rows of the Fonts tab: the title and the items.
    private FontSettingViewModel[] ContextMenuFontRows() =>
        new[]
        {
            InterfaceFontRow(
                "ContextMenuTitle", "PreferencesWindow_ContextMenuTitleFont", "FontPreview_ContextMenuTitle",
                () => ContextMenuTitleFontFamily, v => ContextMenuTitleFontFamily = v,
                () => ContextMenuTitleFontSize, v => ContextMenuTitleFontSize = v,
                () =>
                {
                    ContextMenuTitleFontFamily = string.Empty;
                    ContextMenuTitleFontSize = 0;
                    ContextMenuTitleFontWeight = 0;
                },
                ChooseContextMenuTitleFontCommand,
                () => ContextMenuTitleFontWeight, v => ContextMenuTitleFontWeight = v,
                defaultWeight: ContextMenuTitleDefaultWeight),
            InterfaceFontRow(
                "ContextMenuItems", "PreferencesWindow_ContextMenuItemsFont", "FontPreview_ContextMenuItems",
                () => ContextMenuItemsFontFamily, v => ContextMenuItemsFontFamily = v,
                () => ContextMenuItemsFontSize, v => ContextMenuItemsFontSize = v,
                () =>
                {
                    ContextMenuItemsFontFamily = string.Empty;
                    ContextMenuItemsFontSize = 0;
                    ContextMenuItemsFontWeight = 0;
                },
                ChooseContextMenuItemsFontCommand,
                () => ContextMenuItemsFontWeight, v => ContextMenuItemsFontWeight = v),
        };
}
