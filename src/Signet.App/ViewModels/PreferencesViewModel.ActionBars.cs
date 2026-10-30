using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Docking;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>
/// Action Bars: the stripes of the panels along the sides of the main window — their width while they show the names of
/// the panels (General) and the font of the names (Fonts), and the sample stripes of the preview.
/// </summary>
public sealed partial class PreferencesViewModel
{
    /// <summary>Values to choose from in the "Width" field.</summary>
    public static IReadOnlyList<PanelStripeWidthMode> AvailablePanelStripeWidthModes { get; } = Enum.GetValues<PanelStripeWidthMode>();

    /// <summary>The narrowest width that can be set.</summary>
    public static decimal PanelStripeWidthMin => PanelStripeAppearance.MinWidth;

    /// <summary>The widest width that can be set.</summary>
    public static decimal PanelStripeWidthMax => PanelStripeAppearance.MaxWidth;

    /// <summary>
    /// The stripes of the preview: real stripes over a layout of their own with the panels in their default places, over the
    /// draft of the settings (so they take the width not saved yet), always with the names.
    /// </summary>
    public PanelStripesViewModel PanelStripesSample { get; private set; } = null!;

    /// <summary>The look of the stripes in the draft (the font of the names the preview puts into its resources).</summary>
    public PanelStripeAppearance PanelStripeLookDraft => _settings.PanelStripeAppearance;

    /// <summary>How the width of the stripes is decided while they show the names.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelStripeWidthFixed))]
    private PanelStripeWidthMode _panelStripeWidthMode;

    /// <summary>The width (px) of the stripes in <see cref="PanelStripeWidthMode.Fixed"/>.</summary>
    [ObservableProperty]
    private int _panelStripeFixedWidth;

    /// <summary>Typeface of the names; empty = the interface font.</summary>
    [ObservableProperty]
    private string _panelStripeFontFamily = string.Empty;

    /// <summary>Size (px) of the names; 0 = the default (10).</summary>
    [ObservableProperty]
    private int _panelStripeFontSize;

    /// <summary>Weight of the names; 0 = that of the interface.</summary>
    [ObservableProperty]
    private int _panelStripeFontWeight;

    /// <summary>Whether the width is a number set here (the field of the width is enabled only then).</summary>
    public bool IsPanelStripeWidthFixed => PanelStripeWidthMode == PanelStripeWidthMode.Fixed;

    /// <summary>Action Bars › Fonts: the names under the icons.</summary>
    public FontPageViewModel PanelStripeFonts { get; private set; } = null!;

    partial void OnPanelStripeWidthModeChanged(PanelStripeWidthMode value) => PersistPanelStripeAppearance();

    partial void OnPanelStripeFixedWidthChanged(int value) => PersistPanelStripeAppearance();

    partial void OnPanelStripeFontFamilyChanged(string value) => PersistPanelStripeAppearance();

    partial void OnPanelStripeFontSizeChanged(int value) => PersistPanelStripeAppearance();

    partial void OnPanelStripeFontWeightChanged(int value) => PersistPanelStripeAppearance();

    // Made by the constructor.
    private void CreatePanelStripesSample(AppActionRegistry actions)
    {
        MainDockFactory factory = new();
        factory.InitLayout(factory.CreateLayout());
        PanelStripesSample = new PanelStripesViewModel(factory, _settings, actions, isSample: true);
    }

    // Reads the look of the stripes from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadPanelStripeAppearance()
    {
        PanelStripeAppearance look = _settings.PanelStripeAppearance;
        PanelStripeWidthMode = look.WidthMode;
        PanelStripeFixedWidth = look.FixedWidth;
        PanelStripeFontFamily = look.FontFamily;
        PanelStripeFontSize = look.FontSize;
        PanelStripeFontWeight = look.FontWeight;
    }

    // The width dragged in the main window (Resizable) is kept: it is not a field of this page.
    private void PersistPanelStripeAppearance() =>
        _settings.PanelStripeAppearance = _settings.PanelStripeAppearance with
        {
            WidthMode = PanelStripeWidthMode,
            FixedWidth = PanelStripeFixedWidth,
            FontFamily = PanelStripeFontFamily,
            FontSize = PanelStripeFontSize,
            FontWeight = PanelStripeFontWeight,
        };

    /// <summary>"Choose…" of the font of the names.</summary>
    [RelayCommand]
    private Task ChoosePanelStripeFont() => PickFontAsync(
        new FontPickRequest(
            PanelStripeFontFamily.Length > 0 ? PanelStripeFontFamily : EffectiveUiFontFamily,
            PanelStripeFontSize > 0 ? PanelStripeFontSize : PanelStripeAppearance.DefaultFontSize,
            false,
            PanelStripeFontWeight),
        new FontPickResult(PanelStripeFontFamily, PanelStripeFontSize, PanelStripeFontWeight),
        result =>
        {
            PanelStripeFontFamily = result.Family;
            PanelStripeFontSize = result.Size;
            PanelStripeFontWeight = result.Weight;
        });

    // The font row of the Fonts tab of the Action Bars page.
    private FontSettingViewModel PanelStripeFontRow() =>
        InterfaceFontRow(
            "PanelStripeNames", "PreferencesWindow_PanelStripeFont", "FontPreview_PanelStripe",
            () => PanelStripeFontFamily, v => PanelStripeFontFamily = v,
            () => PanelStripeFontSize, v => PanelStripeFontSize = v,
            () =>
            {
                PanelStripeFontFamily = string.Empty;
                PanelStripeFontSize = 0;
                PanelStripeFontWeight = 0;
            },
            ChoosePanelStripeFontCommand,
            () => PanelStripeFontWeight, v => PanelStripeFontWeight = v,
            defaultSize: PanelStripeAppearance.DefaultFontSize, sizeHintKey: "PreferencesWindow_FontSizeZeroAsDefault10");
}
