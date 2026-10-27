using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Code Editor › Fonts: the font of the titles of the document tabs.</summary>
public sealed partial class PreferencesViewModel
{
    /// <summary>The font of the titles of the document tabs; empty = the interface font.</summary>
    [ObservableProperty]
    private string _documentTabFontFamily = string.Empty;

    /// <summary>The size (px) of the titles of the document tabs; 0 = the default size of the tabs.</summary>
    [ObservableProperty]
    private int _documentTabFontSize;

    /// <summary>The weight of the titles of the document tabs; 0 = not set.</summary>
    [ObservableProperty]
    private int _documentTabFontWeight;

    partial void OnDocumentTabFontWeightChanged(int value) => PersistDocumentTabFont();

    partial void OnDocumentTabFontFamilyChanged(string value) => PersistDocumentTabFont();

    partial void OnDocumentTabFontSizeChanged(int value) => PersistDocumentTabFont();

    // Reads the font from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadDocumentTabFont()
    {
        DocumentTabFontAppearance look = _settings.DocumentTabFontAppearance;
        DocumentTabFontFamily = look.FontFamily;
        DocumentTabFontSize = look.FontSize;
        DocumentTabFontWeight = look.FontWeight;
    }

    private void PersistDocumentTabFont() =>
        _settings.DocumentTabFontAppearance = new DocumentTabFontAppearance(DocumentTabFontFamily, DocumentTabFontSize, DocumentTabFontWeight);

    /// <summary>"Choose…" — the font picker, starting from the font of the tabs.</summary>
    [RelayCommand]
    private async Task ChooseDocumentTabFont()
    {
        string family = DocumentTabFontFamily.Length > 0 ? DocumentTabFontFamily : string.Empty;
        int size = DocumentTabFontSize > 0 ? DocumentTabFontSize : 12;
        await PickFontAsync(
            new FontPickRequest(family, size, false, DocumentTabFontWeight),
            new FontPickResult(DocumentTabFontFamily, DocumentTabFontSize, DocumentTabFontWeight),
            result =>
            {
                DocumentTabFontFamily = result.Family;
                DocumentTabFontSize = result.Size;
                DocumentTabFontWeight = result.Weight;
            });
    }

    // The row of the Code Editor "Fonts" page; the colors of the tabs are in the theme (Interface Colors).
    private FontSettingViewModel DocumentTabFontRow() =>
        new(
            "DocumentTabs",
            Strings.Get("PreferencesWindow_DocumentTabsFont"),
            Strings.Get("FontPreview_DocumentTabs"),
            (() => DocumentTabFontFamily, v => DocumentTabFontFamily = v),
            (() => DocumentTabFontSize, v => DocumentTabFontSize = v, 0, DocumentTabFontAppearance.FontSizeMax),
            (() => DocumentTabFontFamily.Length > 0 ? DocumentTabFontFamily : EffectiveUiFontFamily, () => DocumentTabFontSize > 0 ? DocumentTabFontSize : 12),
            () =>
            {
                DocumentTabFontFamily = string.Empty;
                DocumentTabFontSize = 0;
                DocumentTabFontWeight = 0;
            },
            ChooseDocumentTabFontCommand,
            weight: (() => DocumentTabFontWeight, v => DocumentTabFontWeight = v),
            placeholder: () => string.Empty,
            previewForeground: () => AppTheme.Current.Foreground(ThemeKeys.UiDocumentTab)?.ToString(),
            previewBackground: () => AppTheme.Current.Background(ThemeKeys.UiDocumentTab)?.ToString(),
            inheritance: ToolHeaderInheritance(() => _settings.DocumentTabFontInheritsToolHeader, v => _settings.DocumentTabFontInheritsToolHeader = v));

    // The inheritance of a panel's font from "Fonts › Font of the panel headers" (Appearance › General), the one setting
    // meant to cover every panel's title bar — the document tabs and the Notifications rows can follow it too instead of
    // having a typeface of their own. get/set is the inherit flag of the particular consumer (its own stored bool).
    private FontInheritance ToolHeaderInheritance(Func<bool> get, Action<bool> set) => new(
        get,
        set,
        Strings.Get("PreferencesWindow_ToolHeaderFont"),
        () => (ToolHeaderFontFamily.Length > 0 ? ToolHeaderFontFamily : EffectiveUiFontFamily) + ", " + (ToolHeaderFontSize > 0 ? ToolHeaderFontSize : 12),
        () => AppearanceFonts.Fonts.Single(f => f.Key == "ToolHeader"));
}
