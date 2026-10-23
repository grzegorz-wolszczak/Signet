using System;
using System.Threading.Tasks;
using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Cleanup: the sample dialog of the preview.</summary>
public sealed partial class PreferencesViewModel
{
    private CleanupViewModel? _cleanupSample;

    /// <summary>
    /// The Cleanup dialog of the preview of its Appearance page: the real view model over the analysis of the sample book (which
    /// has rules to merge, nested divs and empty elements), where "Clean" changes nothing. Made when first asked for.
    /// </summary>
    public CleanupViewModel CleanupSample => _cleanupSample ??= CreateCleanupSample();

    private CleanupViewModel CreateCleanupSample()
    {
        CleanupAnalysis analysis = CleanupAnalysis.Prepare(SampleBookInMemory).Analysis
            ?? throw new InvalidOperationException("The sample book is not well-formed.");
        return new CleanupViewModel(analysis, Enum.GetValues<CleanupStep>(), (_, _) => { }, (_, _) => Task.FromResult<CleanupAnalysis?>(analysis));
    }
}
