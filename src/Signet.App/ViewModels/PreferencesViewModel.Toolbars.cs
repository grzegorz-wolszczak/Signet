using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Toolbars;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>
/// Toolbars: the main window's toolbars — the same settings as the "Toolbar Settings" section of a toolbar's context
/// menu (locked positions on General; text position, icon size and the font of the button texts on Appearance), and the
/// sample toolbars of the preview of Appearance.
/// </summary>
public sealed partial class PreferencesViewModel
{
    // The toolbars of the preview, in rows: ones whose buttons have long labels (wrapped in two lines under the icons).
    private static readonly ToolbarId[][] ToolbarSampleLayout =
    {
        new[] { ToolbarId.File, ToolbarId.Tools, ToolbarId.PrettifyMend },
        new[] { ToolbarId.Insert },
    };

    /// <summary>
    /// The manager of the sample toolbars: over the draft of the settings (so it takes the look not saved yet), and never
    /// touching the application resources — the view puts its look into the resources of the preview.
    /// </summary>
    public ToolbarManager ToolbarSampleManager { get; private set; } = null!;

    /// <summary>The rows of the sample toolbars of the preview (real toolbars with the buttons of the user's layout).</summary>
    public IReadOnlyList<ToolbarRowViewModel> ToolbarSampleRows { get; private set; } = Array.Empty<ToolbarRowViewModel>();

    // Made by the constructor. Every change of the look in the draft reaches the sample toolbars through the manager.
    private void CreateToolbarSample(AppActionRegistry actions)
    {
        ToolbarSampleManager = new ToolbarManager(_settings, appliesResources: false);
        ToolbarSampleRows = ToolbarSampleLayout
            .Select(row => new ToolbarRowViewModel(row.Select(id => new ToolbarViewModel(id, ToolbarSampleManager, actions, isSample: true)).ToList()))
            .ToList();
        ToolbarSampleManager.AppearanceChanged += (_, _) =>
        {
            foreach (ToolbarViewModel toolbar in ToolbarSampleRows.SelectMany(r => r.Toolbars))
            {
                toolbar.RefreshAppearance();
            }
        };
    }

    /// <summary>Values to choose from in the "Text position" field.</summary>
    public static IReadOnlyList<ToolbarTextPosition> AvailableToolbarTextPositions { get; } = Enum.GetValues<ToolbarTextPosition>();

    /// <summary>Values to choose from in the "Icon size" field.</summary>
    public static IReadOnlyList<ToolbarIconSize> AvailableToolbarIconSizes { get; } = Enum.GetValues<ToolbarIconSize>();

    /// <summary>Where the toolbar buttons show their text.</summary>
    [ObservableProperty]
    private ToolbarTextPosition _toolbarTextPosition;

    /// <summary>The size of the toolbar icons.</summary>
    [ObservableProperty]
    private ToolbarIconSize _toolbarIconSize;

    /// <summary>Whether the toolbars stay where they are (no grips).</summary>
    [ObservableProperty]
    private bool _toolbarPositionsLocked;

    /// <summary>Typeface of the button texts; empty = the interface font.</summary>
    [ObservableProperty]
    private string _toolbarFontFamily = string.Empty;

    /// <summary>Size (px) of the button texts; 0 = the default (11).</summary>
    [ObservableProperty]
    private int _toolbarFontSize;

    /// <summary>Weight of the button texts; 0 = not set.</summary>
    [ObservableProperty]
    private int _toolbarFontWeight;

    partial void OnToolbarTextPositionChanged(ToolbarTextPosition value) => PersistToolbarAppearance();

    partial void OnToolbarIconSizeChanged(ToolbarIconSize value) => PersistToolbarAppearance();

    partial void OnToolbarPositionsLockedChanged(bool value) => PersistToolbarAppearance();

    partial void OnToolbarFontFamilyChanged(string value) => PersistToolbarAppearance();

    partial void OnToolbarFontSizeChanged(int value) => PersistToolbarAppearance();

    partial void OnToolbarFontWeightChanged(int value) => PersistToolbarAppearance();

    // Reads the look of the toolbars from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadToolbarAppearance()
    {
        ToolbarAppearance look = _settings.ToolbarAppearance;
        ToolbarTextPosition = look.TextPosition;
        ToolbarIconSize = look.IconSize;
        ToolbarPositionsLocked = look.PositionsLocked;
        ToolbarFontFamily = look.FontFamily;
        ToolbarFontSize = look.FontSize;
        ToolbarFontWeight = look.FontWeight;
    }

    private void PersistToolbarAppearance() =>
        _settings.ToolbarAppearance = new ToolbarAppearance(
            ToolbarTextPosition, ToolbarIconSize, ToolbarPositionsLocked, ToolbarFontFamily, ToolbarFontSize, ToolbarFontWeight);

    /// <summary>"Choose…" of the font of the button texts.</summary>
    [RelayCommand]
    private async Task ChooseToolbarFont()
    {
        int size = ToolbarFontSize > 0 ? ToolbarFontSize : ToolbarAppearance.DefaultFontSize;
        await PickFontAsync(
            new FontPickRequest(ToolbarFontFamily.Length > 0 ? ToolbarFontFamily : EffectiveUiFontFamily, size, false, ToolbarFontWeight),
            new FontPickResult(ToolbarFontFamily, ToolbarFontSize, ToolbarFontWeight),
            result =>
            {
                ToolbarFontFamily = result.Family;
                ToolbarFontSize = result.Size;
                ToolbarFontWeight = result.Weight;
            });
    }

    // The font row of the Fonts tab of the Toolbars page.
    private FontSettingViewModel ToolbarFontRow() =>
        InterfaceFontRow(
            "Toolbars", "PreferencesWindow_ToolbarFont", "FontPreview_Toolbar",
            () => ToolbarFontFamily, v => ToolbarFontFamily = v,
            () => ToolbarFontSize, v => ToolbarFontSize = v,
            () =>
            {
                ToolbarFontFamily = string.Empty;
                ToolbarFontSize = 0;
                ToolbarFontWeight = 0;
            },
            ChooseToolbarFontCommand,
            () => ToolbarFontWeight, v => ToolbarFontWeight = v,
            defaultSize: ToolbarAppearance.DefaultFontSize, sizeHintKey: "PreferencesWindow_FontSizeZeroAsDefault11");
}
