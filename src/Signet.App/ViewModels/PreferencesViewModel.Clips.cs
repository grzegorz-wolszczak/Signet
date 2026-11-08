using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;

namespace Signet.App.ViewModels;

/// <summary>
/// Appearance › Clips: how many clips the two Clip Bars show, and the four fonts of the Clip Editor window and the Clips
/// panel (the column headers, the entries of the tree, the text of the selected clip, and the rest of the window).
/// </summary>
public sealed partial class PreferencesViewModel
{
    /// <summary>Appearance › Clips › Fonts.</summary>
    public FontPageViewModel ClipsFonts { get; private set; } = null!;

    private ClipsViewModel? _clipsSample;

    /// <summary>
    /// The Clip Editor of the preview of the Clips pages: the real view model over a sample library of its own (never
    /// saved), reading the look from the settings of this window (the draft), so every change shows in it at once. Made
    /// when first asked for.
    /// </summary>
    public ClipsViewModel ClipsSample => _clipsSample ??= CreateClipsSample();

    private ClipsViewModel CreateClipsSample()
    {
        ClipStore store = new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "signet-sample-" + Guid.NewGuid().ToString("N") + ".xml"));
        ClipsViewModel sample = new(store, () => null, () => null, new Services.StatusBarService(), _settings);
        sample.AddGroupCommand.Execute(null);
        sample.Nodes[0].Name = Strings.Get("PreferencesWindow_ClipsSampleGroup");
        sample.SetSelectedNodes(new[] { sample.Nodes[0] });
        sample.AddEntryCommand.Execute(null);
        ClipNodeViewModel entry = sample.Nodes[0].Children[0];
        entry.Name = "letter_spacing_1";
        entry.Text = Strings.Get("FontPreview_ClipsText");
        sample.AddEntryCommand.Execute(null);
        ClipNodeViewModel other = sample.Nodes[0].Children[^1];
        other.Name = "<br/>";
        other.Text = "<br/>";
        sample.SetSelectedNodes(new[] { entry });
        sample.SetMessage(Strings.Get("FontPreview_ClipsMessage"));
        return sample;
    }

    /// <summary>Upper bound of the number of clips of one Clip Bar.</summary>
    public static decimal ClipBarSlotsMax => SettingsStore.ClipBarSlotsMax;

    /// <summary>How many clips the first Clip Bar shows (0..30).</summary>
    [ObservableProperty]
    private int _clipBarSlots;

    /// <summary>How many clips the second Clip Bar shows (0..30); they follow those of the first.</summary>
    [ObservableProperty]
    private int _clipBar2Slots;

    partial void OnClipBarSlotsChanged(int value) =>
        _settings.ClipBarSlots = Math.Clamp(value, 0, SettingsStore.ClipBarSlotsMax);

    partial void OnClipBar2SlotsChanged(int value) =>
        _settings.ClipBar2Slots = Math.Clamp(value, 0, SettingsStore.ClipBarSlotsMax);

    /// <summary>Typeface of the column headers; empty = the interface font.</summary>
    [ObservableProperty]
    private string _clipsHeaderFontFamily = string.Empty;

    /// <summary>Size (px) of the column headers; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _clipsHeaderFontSize;

    /// <summary>Weight of the column headers; 0 = not set.</summary>
    [ObservableProperty]
    private int _clipsHeaderFontWeight;

    /// <summary>Typeface of the entries of the tree; empty = the interface font.</summary>
    [ObservableProperty]
    private string _clipsEntriesFontFamily = string.Empty;

    /// <summary>Size (px) of the entries of the tree; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _clipsEntriesFontSize;

    /// <summary>Weight of the entries of the tree; 0 = not set.</summary>
    [ObservableProperty]
    private int _clipsEntriesFontWeight;

    /// <summary>Typeface of the text of the selected clip.</summary>
    [ObservableProperty]
    private string _clipsTextFontFamily = ClipEditorAppearance.DefaultTextFontFamily;

    /// <summary>Size (px) of the text of the selected clip; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _clipsTextFontSize;

    /// <summary>Weight of the text of the selected clip; 0 = not set.</summary>
    [ObservableProperty]
    private int _clipsTextFontWeight;

    /// <summary>Typeface of the other elements of the Clip Editor (buttons, labels, messages); empty = the interface font.</summary>
    [ObservableProperty]
    private string _clipsOtherFontFamily = string.Empty;

    /// <summary>Size (px) of the other elements of the Clip Editor; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _clipsOtherFontSize;

    /// <summary>Weight of the other elements of the Clip Editor; 0 = not set.</summary>
    [ObservableProperty]
    private int _clipsOtherFontWeight;

    partial void OnClipsHeaderFontFamilyChanged(string value) => PersistClipsFonts();

    partial void OnClipsHeaderFontSizeChanged(int value) => PersistClipsFonts();

    partial void OnClipsHeaderFontWeightChanged(int value) => PersistClipsFonts();

    partial void OnClipsEntriesFontFamilyChanged(string value) => PersistClipsFonts();

    partial void OnClipsEntriesFontSizeChanged(int value) => PersistClipsFonts();

    partial void OnClipsEntriesFontWeightChanged(int value) => PersistClipsFonts();

    partial void OnClipsTextFontFamilyChanged(string value) => PersistClipsFonts();

    partial void OnClipsTextFontSizeChanged(int value) => PersistClipsFonts();

    partial void OnClipsTextFontWeightChanged(int value) => PersistClipsFonts();

    partial void OnClipsOtherFontFamilyChanged(string value) => PersistClipsFonts();

    partial void OnClipsOtherFontSizeChanged(int value) => PersistClipsFonts();

    partial void OnClipsOtherFontWeightChanged(int value) => PersistClipsFonts();

    // Reads the clips settings from the settings store (the constructor; "Restore Appearance Defaults" after it reset them).
    private void LoadClipsSettings()
    {
        ClipBarSlots = _settings.ClipBarSlots;
        ClipBar2Slots = _settings.ClipBar2Slots;
        ClipEditorAppearance look = _settings.ClipEditorAppearance;
        ClipsHeaderFontFamily = look.HeaderFontFamily;
        ClipsHeaderFontSize = look.HeaderFontSize;
        ClipsHeaderFontWeight = look.HeaderFontWeight;
        ClipsEntriesFontFamily = look.EntriesFontFamily;
        ClipsEntriesFontSize = look.EntriesFontSize;
        ClipsEntriesFontWeight = look.EntriesFontWeight;
        ClipsTextFontFamily = look.TextFontFamily;
        ClipsTextFontSize = look.TextFontSize;
        ClipsTextFontWeight = look.TextFontWeight;
        ClipsOtherFontFamily = look.OtherFontFamily;
        ClipsOtherFontSize = look.OtherFontSize;
        ClipsOtherFontWeight = look.OtherFontWeight;
    }

    // One font back to the default (the interface font; the monospaced font of the text of a clip).
    private void ResetClipsFont(string part)
    {
        switch (part)
        {
            case "Header":
                ClipsHeaderFontFamily = string.Empty;
                ClipsHeaderFontSize = 0;
                ClipsHeaderFontWeight = 0;
                break;
            case "Entries":
                ClipsEntriesFontFamily = string.Empty;
                ClipsEntriesFontSize = 0;
                ClipsEntriesFontWeight = 0;
                break;
            case "Text":
                ClipsTextFontFamily = ClipEditorAppearance.DefaultTextFontFamily;
                ClipsTextFontSize = 0;
                ClipsTextFontWeight = 0;
                break;
            default:
                ClipsOtherFontFamily = string.Empty;
                ClipsOtherFontSize = 0;
                ClipsOtherFontWeight = 0;
                break;
        }
    }

    private void PersistClipsFonts() =>
        _settings.ClipEditorAppearance = new ClipEditorAppearance(
            ClipsHeaderFontFamily, ClipsHeaderFontSize, ClipsHeaderFontWeight,
            ClipsEntriesFontFamily, ClipsEntriesFontSize, ClipsEntriesFontWeight,
            ClipsTextFontFamily, ClipsTextFontSize, ClipsTextFontWeight,
            ClipsOtherFontFamily, ClipsOtherFontSize, ClipsOtherFontWeight);

    private FontPageViewModel CreateClipsFonts() => new(new[]
    {
        InterfaceFontRow(
            "ClipsHeader", "PreferencesWindow_ClipsHeaderFont", "FontPreview_ClipsHeader",
            () => ClipsHeaderFontFamily, v => ClipsHeaderFontFamily = v,
            () => ClipsHeaderFontSize, v => ClipsHeaderFontSize = v,
            () => ResetClipsFont("Header"), ChooseClipsHeaderFontCommand,
            () => ClipsHeaderFontWeight, v => ClipsHeaderFontWeight = v),
        InterfaceFontRow(
            "ClipsEntries", "PreferencesWindow_ClipsEntriesFont", "FontPreview_ClipsEntries",
            () => ClipsEntriesFontFamily, v => ClipsEntriesFontFamily = v,
            () => ClipsEntriesFontSize, v => ClipsEntriesFontSize = v,
            () => ResetClipsFont("Entries"), ChooseClipsEntriesFontCommand,
            () => ClipsEntriesFontWeight, v => ClipsEntriesFontWeight = v),
        InterfaceFontRow(
            "ClipsText", "PreferencesWindow_ClipsTextFont", "FontPreview_ClipsText",
            () => ClipsTextFontFamily, v => ClipsTextFontFamily = v,
            () => ClipsTextFontSize, v => ClipsTextFontSize = v,
            () => ResetClipsFont("Text"), ChooseClipsTextFontCommand,
            () => ClipsTextFontWeight, v => ClipsTextFontWeight = v),
        InterfaceFontRow(
            "ClipsOther", "PreferencesWindow_ClipsOtherFont", "FontPreview_ClipsOther",
            () => ClipsOtherFontFamily, v => ClipsOtherFontFamily = v,
            () => ClipsOtherFontSize, v => ClipsOtherFontSize = v,
            () => ResetClipsFont("Other"), ChooseClipsOtherFontCommand,
            () => ClipsOtherFontWeight, v => ClipsOtherFontWeight = v),
    });

    /// <summary>"Choose…" of the font of the column headers.</summary>
    [RelayCommand]
    private Task ChooseClipsHeaderFont() => ChooseClipsFont("Header");

    /// <summary>"Choose…" of the font of the entries of the tree.</summary>
    [RelayCommand]
    private Task ChooseClipsEntriesFont() => ChooseClipsFont("Entries");

    /// <summary>"Choose…" of the font of the text of the selected clip.</summary>
    [RelayCommand]
    private Task ChooseClipsTextFont() => ChooseClipsFont("Text");

    /// <summary>"Choose…" of the font of the other elements of the window.</summary>
    [RelayCommand]
    private Task ChooseClipsOtherFont() => ChooseClipsFont("Other");

    // The font picker, starting from the font in use (the interface font while the setting follows it).
    private async Task ChooseClipsFont(string part)
    {
        (string family, int size, int weight) = part switch
        {
            "Header" => (ClipsHeaderFontFamily, ClipsHeaderFontSize, ClipsHeaderFontWeight),
            "Entries" => (ClipsEntriesFontFamily, ClipsEntriesFontSize, ClipsEntriesFontWeight),
            "Text" => (ClipsTextFontFamily, ClipsTextFontSize, ClipsTextFontWeight),
            _ => (ClipsOtherFontFamily, ClipsOtherFontSize, ClipsOtherFontWeight),
        };
        await PickFontAsync(
            new FontPickRequest(family.Length > 0 ? family : EffectiveUiFontFamily, size > 0 ? size : EffectiveUiFontSize, false, weight),
            new FontPickResult(family, size, weight),
            result =>
            {
                switch (part)
                {
                    case "Header":
                        ClipsHeaderFontFamily = result.Family;
                        ClipsHeaderFontSize = result.Size;
                        ClipsHeaderFontWeight = result.Weight;
                        break;
                    case "Entries":
                        ClipsEntriesFontFamily = result.Family;
                        ClipsEntriesFontSize = result.Size;
                        ClipsEntriesFontWeight = result.Weight;
                        break;
                    case "Text":
                        ClipsTextFontFamily = result.Family;
                        ClipsTextFontSize = result.Size;
                        ClipsTextFontWeight = result.Weight;
                        break;
                    default:
                        ClipsOtherFontFamily = result.Family;
                        ClipsOtherFontSize = result.Size;
                        ClipsOtherFontWeight = result.Weight;
                        break;
                }
            });
    }
}
