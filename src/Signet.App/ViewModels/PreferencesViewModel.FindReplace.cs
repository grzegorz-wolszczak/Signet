using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Services;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Find and Replace: the sample panel of the preview, and the action button look setting.</summary>
public sealed partial class PreferencesViewModel
{
    private FindReplaceViewModel? _findReplaceSample;
    private SettingsStore? _findReplaceSampleSettings;

    /// <summary>
    /// The Find and Replace panel of the preview of its Appearance page: the real view model with no book behind it (nothing
    /// is found, replaced or opened), over settings of its own that are never saved, so what is typed in the preview stays
    /// there. Made when first asked for.
    /// </summary>
    public FindReplaceViewModel FindReplaceSample => _findReplaceSample ??= CreateFindReplaceSample();

    /// <summary>Values to choose from in the "Action buttons" field.</summary>
    public static IReadOnlyList<FindActionButtonDisplay> AvailableFindActionButtonDisplays { get; } =
        Enum.GetValues<FindActionButtonDisplay>();

    /// <summary>Whether the Find &amp; Replace action buttons show an icon, text, or both.</summary>
    [ObservableProperty]
    private FindActionButtonDisplay _findActionButtonDisplay;

    partial void OnFindActionButtonDisplayChanged(FindActionButtonDisplay value) => _settings.FindActionButtonDisplay = value;

    /// <summary>Typeface of the Find and Replace fields; empty = the interface font.</summary>
    [ObservableProperty]
    private string _findReplaceFieldFontFamily = string.Empty;

    /// <summary>Size (px) of the Find and Replace fields; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _findReplaceFieldFontSize;

    /// <summary>Weight of the Find and Replace fields; 0 = not set.</summary>
    [ObservableProperty]
    private int _findReplaceFieldFontWeight;

    partial void OnFindReplaceFieldFontFamilyChanged(string value) => PersistFindReplaceFieldFont();

    partial void OnFindReplaceFieldFontSizeChanged(int value) => PersistFindReplaceFieldFont();

    partial void OnFindReplaceFieldFontWeightChanged(int value) => PersistFindReplaceFieldFont();

    // Reads the font from the settings (the constructor; "Restore Appearance Defaults" after it cleared it).
    private void LoadFindReplaceFieldFont()
    {
        FindReplaceAppearance look = _settings.FindReplaceAppearance;
        FindReplaceFieldFontFamily = look.FieldFontFamily;
        FindReplaceFieldFontSize = look.FieldFontSize;
        FindReplaceFieldFontWeight = look.FieldFontWeight;
    }

    private void PersistFindReplaceFieldFont()
    {
        FindReplaceAppearance look = new(FindReplaceFieldFontFamily, FindReplaceFieldFontSize, FindReplaceFieldFontWeight);
        _settings.FindReplaceAppearance = look;
        if (_findReplaceSample is not null)
        {
            _findReplaceSample.FieldFont = look.Normalized();
        }
    }

    private FontSettingViewModel FindReplaceFieldsFontRow() =>
        InterfaceFontRow(
            "FindReplaceFields", "PreferencesWindow_FindReplaceFieldsFont", "FontPreview_FindReplaceFields",
            () => FindReplaceFieldFontFamily, v => FindReplaceFieldFontFamily = v,
            () => FindReplaceFieldFontSize, v => FindReplaceFieldFontSize = v,
            () =>
            {
                FindReplaceFieldFontFamily = string.Empty;
                FindReplaceFieldFontSize = 0;
                FindReplaceFieldFontWeight = 0;
            },
            ChooseFindReplaceFieldFontCommand,
            () => FindReplaceFieldFontWeight, v => FindReplaceFieldFontWeight = v);

    /// <summary>"Choose…" of the font of the Find and Replace fields.</summary>
    [RelayCommand]
    private Task ChooseFindReplaceFieldFont() =>
        PickFontAsync(
            new FontPickRequest(
                FindReplaceFieldFontFamily.Length > 0 ? FindReplaceFieldFontFamily : EffectiveUiFontFamily,
                FindReplaceFieldFontSize > 0 ? FindReplaceFieldFontSize : EffectiveUiFontSize, false, FindReplaceFieldFontWeight),
            new FontPickResult(FindReplaceFieldFontFamily, FindReplaceFieldFontSize, FindReplaceFieldFontWeight),
            result =>
            {
                FindReplaceFieldFontFamily = result.Family;
                FindReplaceFieldFontSize = result.Size;
                FindReplaceFieldFontWeight = result.Weight;
            });

    private FindReplaceViewModel CreateFindReplaceSample()
    {
        _findReplaceSampleSettings = new SettingsStore(Path.Combine(Path.GetTempPath(), "signet-sample-" + Guid.NewGuid().ToString("N") + ".xml"));
        return new FindReplaceViewModel(_findReplaceSampleSettings, SampleStatusBar, () => null, new SampleSearchHost())
        {
            FindText = "lighthouse",
            ReplaceText = "tower",
            FieldFont = _settings.FindReplaceAppearance,
        };
    }
}
