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

    partial void OnDocumentTabFontFamilyChanged(string value) => PersistDocumentTabFont();

    partial void OnDocumentTabFontSizeChanged(int value) => PersistDocumentTabFont();

    // Reads the font from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadDocumentTabFont()
    {
        DocumentTabFontAppearance look = _settings.DocumentTabFontAppearance;
        DocumentTabFontFamily = look.FontFamily;
        DocumentTabFontSize = look.FontSize;
    }

    private void PersistDocumentTabFont() =>
        _settings.DocumentTabFontAppearance = new DocumentTabFontAppearance(DocumentTabFontFamily, DocumentTabFontSize);

    /// <summary>"Choose…" — the font picker, starting from the font of the tabs.</summary>
    [RelayCommand]
    private async Task ChooseDocumentTabFont()
    {
        string family = DocumentTabFontFamily.Length > 0 ? DocumentTabFontFamily : string.Empty;
        int size = DocumentTabFontSize > 0 ? DocumentTabFontSize : 12;
        if (await PickFontAsync(new FontPickRequest(family, size, false)) is { } result)
        {
            DocumentTabFontFamily = result.Family;
            DocumentTabFontSize = result.Size;
        }
    }

    // The row of the Code Editor "Fonts" page; the colors of the tabs are in the theme (Interface Colors).
    private FontSettingViewModel DocumentTabFontRow() =>
        new(
            "DocumentTabs",
            Strings.Get("PreferencesWindow_DocumentTabsFont"),
            Strings.Get("FontPreview_DocumentTabs"),
            (() => DocumentTabFontFamily, v => DocumentTabFontFamily = v),
            (() => DocumentTabFontSize, v => DocumentTabFontSize = v, 0, DocumentTabFontAppearance.FontSizeMax),
            (() => DocumentTabFontFamily, () => DocumentTabFontSize > 0 ? DocumentTabFontSize : 12),
            () =>
            {
                DocumentTabFontFamily = string.Empty;
                DocumentTabFontSize = 0;
            },
            ChooseDocumentTabFontCommand,
            placeholder: () => string.Empty,
            previewForeground: () => AppTheme.Current.Foreground(ThemeKeys.UiDocumentTab)?.ToString(),
            previewBackground: () => AppTheme.Current.Background(ThemeKeys.UiDocumentTab)?.ToString());
}
