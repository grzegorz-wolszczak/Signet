using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Code Editor › Fonts: the font of the line numbers.</summary>
public sealed partial class PreferencesViewModel
{
    /// <summary>The font of the line numbers; empty = the Code Editor font.</summary>
    [ObservableProperty]
    private string _lineNumbersFontFamily = string.Empty;

    /// <summary>The size (px) of the line numbers; 0 = the Code Editor size.</summary>
    [ObservableProperty]
    private int _lineNumbersFontSize;

    /// <summary>The weight of the line numbers; 0 = not set (the Code Editor weight).</summary>
    [ObservableProperty]
    private int _lineNumbersFontWeight;

    partial void OnLineNumbersFontWeightChanged(int value) => PersistLineNumbersFont();

    partial void OnLineNumbersFontFamilyChanged(string value) => PersistLineNumbersFont();

    partial void OnLineNumbersFontSizeChanged(int value) => PersistLineNumbersFont();

    // Reads the font from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadLineNumbersFont()
    {
        LineNumberFontAppearance look = _settings.LineNumberFontAppearance;
        LineNumbersFontFamily = look.FontFamily;
        LineNumbersFontSize = look.FontSize;
        LineNumbersFontWeight = look.FontWeight;
    }

    private void PersistLineNumbersFont() =>
        _settings.LineNumberFontAppearance = new LineNumberFontAppearance(LineNumbersFontFamily, LineNumbersFontSize, LineNumbersFontWeight);

    /// <summary>"Choose…" — the font picker, starting from the font of the line numbers (or the editor font).</summary>
    [RelayCommand]
    private async Task ChooseLineNumbersFont()
    {
        string family = LineNumbersFontFamily.Length > 0 ? LineNumbersFontFamily : CodeEditor.FontFamily;
        int size = LineNumbersFontSize > 0 ? LineNumbersFontSize : CodeEditor.FontSize;
        await PickFontAsync(
            new FontPickRequest(family, size, false, LineNumbersFontWeight > 0 ? LineNumbersFontWeight : CodeEditor.FontWeight),
            new FontPickResult(LineNumbersFontFamily, LineNumbersFontSize, LineNumbersFontWeight),
            result =>
            {
                LineNumbersFontFamily = result.Family;
                LineNumbersFontSize = result.Size;
                LineNumbersFontWeight = result.Weight;
            });
    }

    // The row of the Code Editor "Fonts" page: by default the numbers use the editor font (the checkbox); unchecked, the
    // row starts from the editor font and the typeface and size are the numbers' own.
    private FontSettingViewModel LineNumbersFontRow() =>
        new(
            "LineNumbers",
            Strings.Get("PreferencesWindow_LineNumbersFont"),
            Strings.Get("FontPreview_LineNumbers"),
            (() => LineNumbersFontFamily, v => LineNumbersFontFamily = v),
            (() => LineNumbersFontSize, v => LineNumbersFontSize = v, 0, LineNumberFontAppearance.FontSizeMax),
            (() => LineNumbersFontFamily.Length > 0 ? LineNumbersFontFamily : CodeEditor.FontFamily,
             () => LineNumbersFontSize > 0 ? LineNumbersFontSize : CodeEditor.FontSize),
            () =>
            {
                LineNumbersFontFamily = string.Empty;
                LineNumbersFontSize = 0;
                LineNumbersFontWeight = 0;
            },
            ChooseLineNumbersFontCommand,
            placeholder: () => CodeEditor.FontFamily,
            weight: (() => LineNumbersFontWeight, v => LineNumbersFontWeight = v),
            effectiveWeight: () => LineNumbersFontWeight > 0 ? LineNumbersFontWeight : CodeEditor.FontWeight,
            inheritance: new FontInheritance(
                () => LineNumbersFontFamily.Length == 0 && LineNumbersFontSize == 0 && LineNumbersFontWeight == 0,
                inherit =>
                {
                    LineNumbersFontFamily = inherit ? string.Empty : CodeEditor.FontFamily;
                    LineNumbersFontSize = inherit ? 0 : CodeEditor.FontSize;
                    LineNumbersFontWeight = inherit ? 0 : CodeEditor.FontWeight;
                },
                Strings.Get("PreferencesWindow_CodeEditorTextFont"),
                () => CodeEditor.FontFamily + ", " + CodeEditor.FontSize,
                () => CodeEditorFonts.Fonts[0]));
}
