using System;
using System.IO;
using Signet.App.Services;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Find and Replace: the sample panel of the preview.</summary>
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
