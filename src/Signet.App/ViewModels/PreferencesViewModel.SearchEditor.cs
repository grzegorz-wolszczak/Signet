using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;

namespace Signet.App.ViewModels;

/// <summary>
/// Appearance › Saved Searches: the action button look of its list view, and the fonts of the "Name" column header
/// and of the tree rows / Details preview / Name field, shared by its list view and its Add/Edit view.
/// </summary>
public sealed partial class PreferencesViewModel
{
    private SearchEditorViewModel? _searchEditorListSample;
    private SearchEditorViewModel? _searchEditorEditSample;

    /// <summary>Whether the Saved Searches window's batch action buttons show an icon, text, or both.</summary>
    [ObservableProperty]
    private FindActionButtonDisplay _searchEditorActionButtonDisplay;

    partial void OnSearchEditorActionButtonDisplayChanged(FindActionButtonDisplay value) =>
        _settings.SearchEditorActionButtonDisplay = value;

    /// <summary>
    /// The list view of the preview of the Saved Searches pages: the real view model with a sample group and
    /// entries, over its own (never saved) library, reading the look from the settings of this window (the draft),
    /// so every change shows in it at once. Made when first asked for.
    /// </summary>
    public SearchEditorViewModel SearchEditorListSample => _searchEditorListSample ??= CreateSearchEditorListSample();

    /// <summary>
    /// The Add/Edit view of the preview of the Saved Searches pages: a second instance, switched to Edit Search on a
    /// sample entry, so both of the window's two views can be checked against the same settings at once. Made when
    /// first asked for.
    /// </summary>
    public SearchEditorViewModel SearchEditorEditSample => _searchEditorEditSample ??= CreateSearchEditorEditSample();

    private SearchEditorViewModel CreateSearchEditorListSample()
    {
        SearchEditorViewModel sample = NewSearchEditorSample();
        sample.AddGroupCommand.Execute(null);
        SearchEntryNodeViewModel group = sample.Nodes[0];
        group.Name = Strings.Get("PreferencesWindow_SearchEditorSampleGroup");
        sample.SetSelectedNodes(new[] { group });
        sample.AddEntryCommand.Execute(null);
        sample.EditingName = Strings.Get("PreferencesWindow_SearchEditorSampleEntry");
        sample.FindReplace.FindText = "lighthouse";
        sample.FindReplace.ReplaceText = "tower";
        sample.DoneEditCommand.Execute(null);
        sample.SetSelectedNodes(new[] { Descendant(sample) });
        return sample;
    }

    private SearchEditorViewModel CreateSearchEditorEditSample()
    {
        SearchEditorViewModel sample = NewSearchEditorSample();
        sample.AddEntryCommand.Execute(null);
        sample.EditingName = Strings.Get("PreferencesWindow_SearchEditorSampleEntry");
        sample.FindReplace.FindText = "lighthouse";
        sample.FindReplace.ReplaceText = "tower";
        return sample;
    }

    private SearchEditorViewModel NewSearchEditorSample()
    {
        SavedSearchStore store = new(Path.Combine(Path.GetTempPath(), "signet-sample-" + Guid.NewGuid().ToString("N") + ".xml"));
        return new SearchEditorViewModel(store, _settings, CreateFindReplaceSample(), CreateFindReplaceSample());
    }

    // The entry added last (under whichever group is selected when it is added).
    private static SearchEntryNodeViewModel Descendant(SearchEditorViewModel vm) =>
        vm.Nodes[0].Children.Count > 0 ? vm.Nodes[0].Children[0] : vm.Nodes[0];

    /// <summary>Typeface of the "Name" column header; empty = the interface font.</summary>
    [ObservableProperty]
    private string _searchEditorHeaderFontFamily = string.Empty;

    /// <summary>Size (px) of the "Name" column header; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _searchEditorHeaderFontSize;

    /// <summary>Weight of the "Name" column header; 0 = not set.</summary>
    [ObservableProperty]
    private int _searchEditorHeaderFontWeight;

    /// <summary>Typeface of the tree rows, the Details preview and the Name field; empty = the interface font.</summary>
    [ObservableProperty]
    private string _searchEditorRowsFontFamily = string.Empty;

    /// <summary>Size (px) of the tree rows, the Details preview and the Name field; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _searchEditorRowsFontSize;

    /// <summary>Weight of the tree rows, the Details preview and the Name field; 0 = not set.</summary>
    [ObservableProperty]
    private int _searchEditorRowsFontWeight;

    partial void OnSearchEditorHeaderFontFamilyChanged(string value) => PersistSearchEditorFonts();

    partial void OnSearchEditorHeaderFontSizeChanged(int value) => PersistSearchEditorFonts();

    partial void OnSearchEditorHeaderFontWeightChanged(int value) => PersistSearchEditorFonts();

    partial void OnSearchEditorRowsFontFamilyChanged(string value) => PersistSearchEditorFonts();

    partial void OnSearchEditorRowsFontSizeChanged(int value) => PersistSearchEditorFonts();

    partial void OnSearchEditorRowsFontWeightChanged(int value) => PersistSearchEditorFonts();

    // Reads the fonts from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadSearchEditorFonts()
    {
        SearchEditorAppearance look = _settings.SearchEditorAppearance;
        SearchEditorHeaderFontFamily = look.HeaderFontFamily;
        SearchEditorHeaderFontSize = look.HeaderFontSize;
        SearchEditorHeaderFontWeight = look.HeaderFontWeight;
        SearchEditorRowsFontFamily = look.RowsFontFamily;
        SearchEditorRowsFontSize = look.RowsFontSize;
        SearchEditorRowsFontWeight = look.RowsFontWeight;
    }

    private void PersistSearchEditorFonts() =>
        _settings.SearchEditorAppearance = new SearchEditorAppearance(
            SearchEditorHeaderFontFamily, SearchEditorHeaderFontSize,
            SearchEditorRowsFontFamily, SearchEditorRowsFontSize,
            SearchEditorHeaderFontWeight, SearchEditorRowsFontWeight);

    /// <summary>"Choose…" of the font of the "Name" column header.</summary>
    [RelayCommand]
    private Task ChooseSearchEditorHeaderFont() => ChooseSearchEditorFont(headers: true);

    /// <summary>"Choose…" of the font of the tree rows, the Details preview and the Name field.</summary>
    [RelayCommand]
    private Task ChooseSearchEditorRowsFont() => ChooseSearchEditorFont(headers: false);

    // The font picker, starting from the font in use (the interface font while the setting follows it).
    private async Task ChooseSearchEditorFont(bool headers)
    {
        string family = headers ? SearchEditorHeaderFontFamily : SearchEditorRowsFontFamily;
        int size = headers ? SearchEditorHeaderFontSize : SearchEditorRowsFontSize;
        int weight = headers ? SearchEditorHeaderFontWeight : SearchEditorRowsFontWeight;
        await PickFontAsync(
            new FontPickRequest(family.Length > 0 ? family : EffectiveUiFontFamily, size > 0 ? size : EffectiveUiFontSize, false, weight),
            new FontPickResult(family, size, weight),
            result =>
            {
                if (headers)
                {
                    SearchEditorHeaderFontFamily = result.Family;
                    SearchEditorHeaderFontSize = result.Size;
                    SearchEditorHeaderFontWeight = result.Weight;
                }
                else
                {
                    SearchEditorRowsFontFamily = result.Family;
                    SearchEditorRowsFontSize = result.Size;
                    SearchEditorRowsFontWeight = result.Weight;
                }
            });
    }
}
