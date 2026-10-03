using System.Collections.Generic;
using Signet.App.ViewModels.Tabs;
using Signet.Core.Resources;
using Signet.Core.Search;

namespace Signet.App.ViewModels;

/// <summary>
/// Main window services that <see cref="FindReplaceViewModel"/> needs for multi-file
/// searching — resolving a <see cref="LookWhere"/> scope to a list of resources,
/// access to the open tabs, opening a file at a match.
/// </summary>
public interface IMultiFileSearchHost
{
    /// <summary>Whether a book is loaded (the panel can work in multi-file mode).</summary>
    bool HasBook { get; }

    /// <summary>
    /// Flushes the content of the open tabs into the resources (before a multi-file
    /// operation) — so that counting/replacing sees the current text.
    /// </summary>
    void FlushOpenTabs();

    /// <summary>
    /// Resources in the given scope, in order (spine / tree / tabs). An empty list when
    /// the scope does not apply (e.g. "Selected …" without a consistent selection).
    /// </summary>
    IReadOnlyList<TextResource> ResolveLookWhere(LookWhere lookWhere);

    /// <summary>The Code View tab displaying the given resource, or <c>null</c> when it is not open.</summary>
    CodeTabViewModel? FindOpenTab(TextResource resource);

    /// <summary>
    /// Opens (or activates) the tab of the resource with the given path and selects the range
    /// <c>[startOffset, endOffset)</c> in it — the multi-file Find Next "jump" to a match.
    /// </summary>
    void OpenResourceAtMatch(string bookPath, int startOffset, int endOffset);

    /// <summary>
    /// Creates an automatic "Before: <paramref name="operation"/>" checkpoint; returns whether one was created.
    /// </summary>
    bool CheckpointBefore(string operation);

    /// <summary>Rolls back the checkpoint when the operation changed nothing.</summary>
    void RewindCheckpoint();
}
