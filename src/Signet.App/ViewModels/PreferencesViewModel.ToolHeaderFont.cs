using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>Appearance › General › Fonts: the font of the title bar of a tool panel (Book Browser, Preview, …).</summary>
public sealed partial class PreferencesViewModel
{
    /// <summary>The font of the title bar of a tool panel; empty = the interface font.</summary>
    [ObservableProperty]
    private string _toolHeaderFontFamily = string.Empty;

    /// <summary>The size (px) of the title bar of a tool panel; 0 = the default size of the title.</summary>
    [ObservableProperty]
    private int _toolHeaderFontSize;

    /// <summary>The weight of the title bar of a tool panel; 0 = not set.</summary>
    [ObservableProperty]
    private int _toolHeaderFontWeight;

    partial void OnToolHeaderFontWeightChanged(int value) => PersistToolHeaderFont();

    partial void OnToolHeaderFontFamilyChanged(string value) => PersistToolHeaderFont();

    partial void OnToolHeaderFontSizeChanged(int value) => PersistToolHeaderFont();

    // Reads the font from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadToolHeaderFont()
    {
        ToolHeaderFontAppearance look = _settings.ToolHeaderFontAppearance;
        ToolHeaderFontFamily = look.FontFamily;
        ToolHeaderFontSize = look.FontSize;
        ToolHeaderFontWeight = look.FontWeight;
    }

    private void PersistToolHeaderFont() =>
        _settings.ToolHeaderFontAppearance = new ToolHeaderFontAppearance(ToolHeaderFontFamily, ToolHeaderFontSize, ToolHeaderFontWeight);

    /// <summary>"Choose…" — the font picker, starting from the font of the tool panel titles.</summary>
    [RelayCommand]
    private async Task ChooseToolHeaderFont()
    {
        string family = ToolHeaderFontFamily.Length > 0 ? ToolHeaderFontFamily : string.Empty;
        int size = ToolHeaderFontSize > 0 ? ToolHeaderFontSize : 12;
        await PickFontAsync(
            new FontPickRequest(family, size, false, ToolHeaderFontWeight),
            new FontPickResult(ToolHeaderFontFamily, ToolHeaderFontSize, ToolHeaderFontWeight),
            result =>
            {
                ToolHeaderFontFamily = result.Family;
                ToolHeaderFontSize = result.Size;
                ToolHeaderFontWeight = result.Weight;
            });
    }

    // The row of the Appearance "Fonts" page.
    private FontSettingViewModel ToolHeaderFontRow() =>
        new(
            "ToolHeader",
            Strings.Get("PreferencesWindow_ToolHeaderFont"),
            Strings.Get("FontPreview_ToolHeader"),
            (() => ToolHeaderFontFamily, v => ToolHeaderFontFamily = v),
            (() => ToolHeaderFontSize, v => ToolHeaderFontSize = v, 0, ToolHeaderFontAppearance.FontSizeMax),
            (() => ToolHeaderFontFamily.Length > 0 ? ToolHeaderFontFamily : EffectiveUiFontFamily, () => ToolHeaderFontSize > 0 ? ToolHeaderFontSize : 12),
            () =>
            {
                ToolHeaderFontFamily = string.Empty;
                ToolHeaderFontSize = 0;
                ToolHeaderFontWeight = 0;
            },
            ChooseToolHeaderFontCommand,
            weight: (() => ToolHeaderFontWeight, v => ToolHeaderFontWeight = v),
            placeholder: () => string.Empty,
            // The title bar of an active tool panel (Dock.Avalonia's own Fluent theme colors; Signet has no copy of them
            // in its own theme), so the preview looks like a real panel header, not plain text.
            previewForeground: () => ToolHeaderPreviewColor("DockTabActiveForegroundBrush"),
            previewBackground: () => ToolHeaderPreviewColor("DockSurfaceHeaderActiveBrush"));

    private static string? ToolHeaderPreviewColor(string resourceKey) =>
        Application.Current?.TryFindResource(resourceKey, out object? resource) == true && resource is ISolidColorBrush brush
            ? brush.Color.ToString()
            : null;
}
