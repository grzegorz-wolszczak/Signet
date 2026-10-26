using System;
using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
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

    private FindReplaceViewModel CreateFindReplaceSample()
    {
        _findReplaceSampleSettings = new SettingsStore(Path.Combine(Path.GetTempPath(), "signet-sample-" + Guid.NewGuid().ToString("N") + ".xml"));
        return new FindReplaceViewModel(_findReplaceSampleSettings, SampleStatusBar, () => null, new SampleSearchHost())
        {
            FindText = "lighthouse",
            ReplaceText = "tower",
        };
    }
}
