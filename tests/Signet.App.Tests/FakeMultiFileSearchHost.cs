using System;
using System.Collections.Generic;
using System.Linq;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.Resources;
using Signet.Core.Search;

namespace Signet.App.Tests;

/// <summary>
/// Test implementation of <see cref="IMultiFileSearchHost"/>: controls the <see cref="LookWhere"/>
/// scope and the list of open tabs, and records Find Next "jumps".
/// </summary>
internal sealed class FakeMultiFileSearchHost : IMultiFileSearchHost
{
    public bool BookLoaded { get; set; }

    public Func<LookWhere, IReadOnlyList<TextResource>> Resolver { get; set; } =
        _ => Array.Empty<TextResource>();

    public List<CodeTabViewModel> OpenTabs { get; } = new();

    public List<(string BookPath, int Start, int End)> Jumps { get; } = new();

    public int FlushCount { get; private set; }

    public List<string> Checkpoints { get; } = new();

    public int Rewinds { get; private set; }

    public bool HasBook => BookLoaded;

    public bool CheckpointBefore(string operation)
    {
        Checkpoints.Add(operation);
        return true;
    }

    public void RewindCheckpoint() => Rewinds++;

    public void FlushOpenTabs() => FlushCount++;

    public IReadOnlyList<TextResource> ResolveLookWhere(LookWhere lookWhere) => Resolver(lookWhere);

    public CodeTabViewModel? FindOpenTab(TextResource resource) =>
        OpenTabs.FirstOrDefault(t => ReferenceEquals(t.Resource, resource));

    /// <summary>Called on a "jump", e.g. to activate the tab of the matched file.</summary>
    public Action<string, int, int>? OnJump { get; set; }

    public void OpenResourceAtMatch(string bookPath, int startOffset, int endOffset)
    {
        Jumps.Add((bookPath, startOffset, endOffset));
        OnJump?.Invoke(bookPath, startOffset, endOffset);
    }
}
