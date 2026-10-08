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

    /// <summary>Notifications panel (the status bar message history).</summary>
    public const string Notifications = "Notifications";

    /// <summary>"Find Usages" panel (the usages of a CSS class).</summary>
    public const string FindUsages = "FindUsages";

    /// <summary>Document area (tabs).</summary>
    public const string Documents = "Documents";
}

/// <summary>
/// Common base of Signet's dockable panels. Disables Dock's deferred content
/// (<see cref="IDeferredContentPresentation"/>): when the dock was re-parented,
/// <c>DeferredContentControl</c> materialized the content through the dispatcher queue, which
/// caused re-entrancy in the views' <c>DataContextChanged</c> handlers. Signet's panels
/// are lightweight, so synchronous materialization costs nothing.
/// Panels can be closed (the panel menu / ×): <see cref="MainDockFactory.CloseDockable"/> only hides them, so the
/// View menu shows them again.
/// </summary>
public abstract class SignetTool : Tool, IDeferredContentPresentation
{
    private bool _needsAttention;

    /// <summary>Initializes the panel — closable (closing only hides it, see <see cref="MainDockFactory.CloseDockable"/>).</summary>
    protected SignetTool()
    {
        CanClose = true;
    }

    bool IDeferredContentPresentation.DeferContentPresentation => false;

    /// <summary>
    /// Whether the panel's tab should draw attention (warning color) — e.g. the Notifications
    /// panel with unread warnings. Applied to the tab by a style in <c>App.axaml</c>.
    /// </summary>
    public bool NeedsAttention
    {
        get => _needsAttention;
        set => SetProperty(ref _needsAttention, value);
    }
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
    }

    /// <summary>Checkpoint list view model (embedded by <see cref="MainDockFactory"/>).</summary>
    public CheckpointsViewModel? ViewModel { get; init; }
}

/// <summary>"Find Usages" panel — the usages of a CSS class in the whole book.</summary>
public sealed class FindUsagesTool : SignetTool
{
    /// <summary>Initializes the panel.</summary>
    public FindUsagesTool()
    {
        Id = DockableIds.FindUsages;
        Title = Strings.Get("Panel_FindUsages");
    }

    /// <summary>Panel view model (embedded by <see cref="MainDockFactory"/>).</summary>
    public FindUsagesViewModel? ViewModel { get; init; }
}

/// <summary>Notifications panel — the history of the status bar messages.</summary>
public sealed class NotificationsTool : SignetTool
{
    /// <summary>Initializes the panel.</summary>
    public NotificationsTool()
    {
        Id = DockableIds.Notifications;
        Title = Strings.Get("Panel_Notifications");
    }

    /// <summary>Panel view model (embedded by <see cref="MainDockFactory"/>).</summary>
    public NotificationsViewModel? ViewModel { get; init; }
}
