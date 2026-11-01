using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>
/// Appearance and Behavior › Spelling Editor › Fonts: the column headers, the "Word" column and the other cells. The
/// background of the column headers is a color of the theme, edited on the same page
/// (<see cref="SpellcheckEditorHeaderBackgroundColor"/>).
/// </summary>
public sealed partial class PreferencesViewModel
{
    private SpellcheckEditorViewModel? _spellcheckEditorSample;

    /// <summary>
    /// The table of the preview of this page: the real Spelling Editor view model over the words of the sample book,
    /// reading the look from the settings of this window (the draft), so every change shows in it at once. Made when
    /// first asked for.
    /// </summary>
    public SpellcheckEditorViewModel SpellcheckEditorSample
    {
        get
        {
            if (_spellcheckEditorSample is null)
            {
                _spellcheckEditorSample = new SpellcheckEditorViewModel(_spellChecker, _settings)
                {
                    // Every word, not only the misspelled ones: the preview is about the look of the table.
                    ShowAllWords = true,
                };
                _spellcheckEditorSample.Refresh(SampleBookInMemory);
            }

            return _spellcheckEditorSample;
        }
    }

    /// <summary>Typeface of the column headers of the window; empty = the interface font.</summary>
    [ObservableProperty]
    private string _spellcheckEditorHeaderFontFamily = string.Empty;

    /// <summary>Size (px) of the column headers; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _spellcheckEditorHeaderFontSize;

    /// <summary>Weight of the column headers; 0 = not set.</summary>
    [ObservableProperty]
    private int _spellcheckEditorHeaderFontWeight;

    /// <summary>Typeface of the "Word" column; empty = the interface font.</summary>
    [ObservableProperty]
    private string _spellcheckEditorWordFontFamily = string.Empty;

    /// <summary>Size (px) of the "Word" column; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _spellcheckEditorWordFontSize;

    /// <summary>Weight of the "Word" column; 0 = not set.</summary>
    [ObservableProperty]
    private int _spellcheckEditorWordFontWeight;

    /// <summary>Typeface of the other cells (count, language, misspelled); empty = the interface font.</summary>
    [ObservableProperty]
    private string _spellcheckEditorCellsFontFamily = string.Empty;

    /// <summary>Size (px) of the other cells; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _spellcheckEditorCellsFontSize;

    /// <summary>Weight of the other cells; 0 = not set.</summary>
    [ObservableProperty]
    private int _spellcheckEditorCellsFontWeight;

    partial void OnSpellcheckEditorHeaderFontFamilyChanged(string value) => PersistSpellcheckEditorFonts();

    partial void OnSpellcheckEditorHeaderFontSizeChanged(int value) => PersistSpellcheckEditorFonts();

    partial void OnSpellcheckEditorHeaderFontWeightChanged(int value) => PersistSpellcheckEditorFonts();

    partial void OnSpellcheckEditorWordFontFamilyChanged(string value) => PersistSpellcheckEditorFonts();

    partial void OnSpellcheckEditorWordFontSizeChanged(int value) => PersistSpellcheckEditorFonts();

    partial void OnSpellcheckEditorWordFontWeightChanged(int value) => PersistSpellcheckEditorFonts();

    partial void OnSpellcheckEditorCellsFontFamilyChanged(string value) => PersistSpellcheckEditorFonts();

    partial void OnSpellcheckEditorCellsFontSizeChanged(int value) => PersistSpellcheckEditorFonts();

    partial void OnSpellcheckEditorCellsFontWeightChanged(int value) => PersistSpellcheckEditorFonts();

    // Reads the fonts from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadSpellcheckEditorFonts()
    {
        SpellcheckEditorAppearance look = _settings.SpellcheckEditorAppearance;
        SpellcheckEditorHeaderFontFamily = look.HeaderFontFamily;
        SpellcheckEditorHeaderFontSize = look.HeaderFontSize;
        SpellcheckEditorHeaderFontWeight = look.HeaderFontWeight;
        SpellcheckEditorWordFontFamily = look.WordFontFamily;
        SpellcheckEditorWordFontSize = look.WordFontSize;
        SpellcheckEditorWordFontWeight = look.WordFontWeight;
        SpellcheckEditorCellsFontFamily = look.CellsFontFamily;
        SpellcheckEditorCellsFontSize = look.CellsFontSize;
        SpellcheckEditorCellsFontWeight = look.CellsFontWeight;
    }

    private void PersistSpellcheckEditorFonts() =>
        _settings.SpellcheckEditorAppearance = new SpellcheckEditorAppearance(
            SpellcheckEditorHeaderFontFamily, SpellcheckEditorHeaderFontSize,
            SpellcheckEditorWordFontFamily, SpellcheckEditorWordFontSize,
            SpellcheckEditorCellsFontFamily, SpellcheckEditorCellsFontSize,
            SpellcheckEditorHeaderFontWeight, SpellcheckEditorWordFontWeight, SpellcheckEditorCellsFontWeight);

    /// <summary>"Choose…" of the font of the column headers of the window.</summary>
    [RelayCommand]
    private Task ChooseSpellcheckEditorHeaderFont() =>
        ChooseSpellcheckEditorFont(
            SpellcheckEditorHeaderFontFamily, SpellcheckEditorHeaderFontSize, SpellcheckEditorHeaderFontWeight, (f, s, w) =>
            {
                SpellcheckEditorHeaderFontFamily = f;
                SpellcheckEditorHeaderFontSize = s;
                SpellcheckEditorHeaderFontWeight = w;
            });

    /// <summary>"Choose…" of the font of the "Word" column.</summary>
    [RelayCommand]
    private Task ChooseSpellcheckEditorWordFont() =>
        ChooseSpellcheckEditorFont(
            SpellcheckEditorWordFontFamily, SpellcheckEditorWordFontSize, SpellcheckEditorWordFontWeight, (f, s, w) =>
            {
                SpellcheckEditorWordFontFamily = f;
                SpellcheckEditorWordFontSize = s;
                SpellcheckEditorWordFontWeight = w;
            });

    /// <summary>"Choose…" of the font of the other cells.</summary>
    [RelayCommand]
    private Task ChooseSpellcheckEditorCellsFont() =>
        ChooseSpellcheckEditorFont(
            SpellcheckEditorCellsFontFamily, SpellcheckEditorCellsFontSize, SpellcheckEditorCellsFontWeight, (f, s, w) =>
            {
                SpellcheckEditorCellsFontFamily = f;
                SpellcheckEditorCellsFontSize = s;
                SpellcheckEditorCellsFontWeight = w;
            });

    // The font picker, starting from the font in use (the interface font while the setting follows it).
    private async Task ChooseSpellcheckEditorFont(string family, int size, int weight, System.Action<string, int, int> apply)
    {
        await PickFontAsync(
            new FontPickRequest(family.Length > 0 ? family : EffectiveUiFontFamily, size > 0 ? size : EffectiveUiFontSize, false, weight),
            new FontPickResult(family, size, weight),
            result => apply(result.Family, result.Size, result.Weight));
    }
}
