using Dock.Controls.DeferredContentControl;
using Dock.Model.Mvvm.Controls;
using Signet.App.Resources;
using Signet.App.ViewModels;

namespace Signet.App.Docking;

/// <summary>Constant identifiers of the dockable panels — also used to toggle their visibility.</summary>
public static class DockableIds
{
    /// <summary>Resource tree panel (BookBrowser).</summary>
    public const string BookBrowser = "BookBrowser";

    /// <summary>Clips panel.</summary>
    public const string Clips = "Clips";

    /// <summary>Preview panel (WebView).</summary>
    public const string Preview = "Preview";

    /// <summary>Table of contents panel.</summary>
    public const string TableOfContents = "TableOfContents";

    /// <summary>Validation results panel.</summary>
    public const string ValidationResults = "ValidationResults";

    /// <summary>Book checkpoints panel.</summary>
    public const string Checkpoints = "Checkpoints";

    /// <summary>Document area (tabs).</summary>
    public const string Documents = "Documents";
}

/// <summary>
/// Common base of Signet's dockable panels. Disables Dock's deferred content
/// (<see cref="IDeferredContentPresentation"/>): when the dock was re-parented,
/// <c>DeferredContentControl</c> materialized the content through the dispatcher queue, which
/// caused re-entrancy in the views' <c>DataContextChanged</c> handlers. Signet's panels
/// are lightweight, so synchronous materialization costs nothing.
/// </summary>
public abstract class SignetTool : Tool, IDeferredContentPresentation
{
    bool IDeferredContentPresentation.DeferContentPresentation => false;
}

/// <summary>
/// Panel whose content is a plain text placeholder.
/// </summary>
public class PlaceholderTool : SignetTool
{
    /// <summary>Text displayed in the middle of the panel.</summary>
    public string Placeholder { get; init; } = string.Empty;
}

/// <summary>Publication resource tree panel.</summary>
public sealed class BookBrowserTool : PlaceholderTool
{
    /// <summary>Initializes the panel.</summary>
    public BookBrowserTool()
    {
        Id = DockableIds.BookBrowser;
        Title = Strings.Get("Panel_BookBrowser");
        CanClose = false;
    }

    /// <summary>Resource tree view model (embedded by <see cref="MainDockFactory"/>).</summary>
    public BookBrowserViewModel? ViewModel { get; init; }
}

/// <summary>User clips panel.</summary>
public sealed class ClipsTool : SignetTool
{
    /// <summary>Initializes the panel.</summary>
    public ClipsTool()
    {
        Id = DockableIds.Clips;
        Title = Strings.Get("Panel_Clips");
        CanClose = false;
    }

    /// <summary>Clip library view model (embedded by <see cref="MainDockFactory"/>).</summary>
    public ClipsViewModel? ViewModel { get; init; }
}

/// <summary>Panel showing the rendered (X)HTML preview.</summary>
public sealed class PreviewTool : SignetTool
{
    /// <summary>Initializes the panel.</summary>
    public PreviewTool()
    {
        Id = DockableIds.Preview;
        Title = Strings.Get("Panel_Preview");
        CanClose = false;
    }

    /// <summary>Preview view model (embedded by <see cref="MainDockFactory"/>).</summary>
    public PreviewViewModel? ViewModel { get; init; }
}

/// <summary>Table of contents panel — preview and navigation.</summary>
public sealed class TableOfContentsTool : SignetTool
{
    /// <summary>Initializes the panel.</summary>
    public TableOfContentsTool()
    {
        Id = DockableIds.TableOfContents;
        Title = Strings.Get("Panel_TableOfContents");
        CanClose = false;
    }

    /// <summary>Table of contents view model (embedded by <see cref="MainDockFactory"/>).</summary>
    public TableOfContentsViewModel? ViewModel { get; init; }
}

/// <summary>Validation results panel.</summary>
public sealed class ValidationResultsTool : SignetTool
{
    /// <summary>Initializes the panel.</summary>
    public ValidationResultsTool()
    {
        Id = DockableIds.ValidationResults;
        Title = Strings.Get("Panel_ValidationResults");
        CanClose = false;
    }

    /// <summary>Panel view model (embedded by <see cref="MainDockFactory"/>).</summary>
    public ValidationResultsViewModel? ViewModel { get; init; }
}

/// <summary>Book checkpoints panel.</summary>
public sealed class CheckpointsTool : SignetTool
{
    /// <summary>Initializes the panel.</summary>
    public CheckpointsTool()
    {
        Id = DockableIds.Checkpoints;
        Title = Strings.Get("Panel_Checkpoints");
        CanClose = false;
    }

    /// <summary>Checkpoint list view model (embedded by <see cref="MainDockFactory"/>).</summary>
    public CheckpointsViewModel? ViewModel { get; init; }
}
