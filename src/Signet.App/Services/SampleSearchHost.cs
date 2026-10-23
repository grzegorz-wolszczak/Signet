using System;
using System.Collections.Generic;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.App.Services;

/// <summary>
/// The host of the sample Find &amp; Replace panel of the Preferences preview: there is no book, so no search finds anything and
/// nothing is opened, replaced or checkpointed.
/// </summary>
public sealed class SampleSearchHost : IMultiFileSearchHost
{
    /// <inheritdoc />
    public bool HasBook => false;

    /// <inheritdoc />
    public void FlushOpenTabs()
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<TextResource> ResolveLookWhere(Signet.Core.Search.LookWhere lookWhere) => Array.Empty<TextResource>();

    /// <inheritdoc />
    public CodeTabViewModel? FindOpenTab(TextResource resource) => null;

    /// <inheritdoc />
    public void OpenResourceAtMatch(string bookPath, int startOffset, int endOffset)
    {
    }

    /// <inheritdoc />
    public void ShowTextUsages(Func<string> header, IReadOnlyList<ClassUsage> usages, Func<IReadOnlyList<ClassUsage>> rerun)
    {
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool?> CheckpointBeforeAsync(string operation) =>
        System.Threading.Tasks.Task.FromResult<bool?>(false);

    /// <inheritdoc />
    public void RewindCheckpoint()
    {
    }
}
