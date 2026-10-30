using System.Collections.Generic;
using System.Linq;
using System;
using System.Globalization;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.Misc;

namespace Signet.App.Docking;

/// <summary>
/// Dock layout factory of the main window: document tabs in the middle and the panels in six places around them
/// (<see cref="PanelPlace"/>, IntelliJ's tool window anchors): the top and the bottom half of the left and of the right
/// column, and the left and the right half of the bottom row (along the whole window width). Each place is an area of
/// tabs. By default Book Browser and Clips are on the left, Preview, TOC and Checkpoints on the right, and Validation
/// Results, Notifications and Find Usages at the bottom. The factory keeps the order of the panels of every place,
/// closed ones included — the order of their buttons on the stripes (<see cref="PanelsAt"/>).
/// </summary>
public sealed class MainDockFactory : Factory
{
    /// <summary>
    /// Lower bound of a region's proportion when restoring a saved layout — prevents
    /// recreating a region with a degenerate (near-zero) width, which would make the panel
    /// look "vanished" even though its visibility flag is correct (bug: panels do not
    /// come back after enlarging the window when the proportion was saved with a very narrow window).
    /// </summary>
    private const double MinRestoredProportion = 0.08;

    private readonly Dictionary<string, Tool> _tools = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IToolDock> _regionDocks = new(StringComparer.Ordinal);

    // The panels of every place in their order (closed and floating ones included): the order of the stripe buttons.
    private readonly Dictionary<PanelPlace, List<string>> _order = new();

    // The panels whose button was taken off the stripes ("Hide" in its menu); "More Tool Windows" lists them.
    private readonly HashSet<string> _offStripe = new(StringComparer.Ordinal);

    // The columns and the row that hold the halves: never removed from the layout, like the places themselves.
    private readonly List<IProportionalDock> _placeContainers = new();

    // Every row / column of the layout with all its children, the splitters included (some of them are taken out of the
    // layout while a side of theirs shows nothing: see ShowSplittersBetweenShownParts).
    private readonly Dictionary<IProportionalDock, IDockable[]> _splitHosts = new();

    // The region a panel belongs to while it floats in its own window: where closing the window (or hiding the
    // panel) docks it back. Filled with the default layout and updated whenever a panel is floated.
    private readonly Dictionary<string, IToolDock> _homeDocks = new(StringComparer.Ordinal);
    // The window place of panels hidden while floating (their tab's ×, their window's ×, the View menu): showing such
    // a panel again floats it there, the way it was closed.
    private readonly Dictionary<string, FloatingBounds> _floatingWhenHidden = new(StringComparer.Ordinal);
    private readonly BookBrowserViewModel? _bookBrowser;
    private readonly PreviewViewModel? _preview;
    private IRootDock? _root;

    // Set while the main window closes all floating windows: their panels are docked back (the layout and the
    // floating windows were already persisted), not hidden.
    private bool _closingAllWindows;

    /// <summary>Creates the factory; the optional Book Browser and Preview panel view models are embedded in the panels.</summary>
    public MainDockFactory(BookBrowserViewModel? bookBrowser = null, PreviewViewModel? preview = null)
    {
        _bookBrowser = bookBrowser;
        _preview = preview;

        // The Preview panel says in its title which file it shows ("Preview [chapter1.xhtml]"): the panel is one, while
        // the file follows the active tab. Subscribed here, not in CreateLayout, which runs again for every layout.
        if (_preview is not null)
        {
            _preview.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(PreviewViewModel.CurrentBookPath))
                {
                    UpdatePreviewTitle();
                }
            };
        }

        // "Float" (and dragging a tab out of the window) needs a host window; without a locator the panel moved into
        // a window model that was never shown, and so it vanished.
        // Every floating window is checked once shown (after Dock has placed it): when it is not fully on screen it is
        // moved to the center of the primary monitor.
        DefaultHostWindowLocator = () =>
        {
            HostWindow host = new();
            host.Opened += (_, _) => Dispatcher.UIThread.Post(() => WindowPlacement.EnsureVisible(host), DispatcherPriority.Background);
            HostWindowCreated?.Invoke(this, host);
            return host;
        };

        // Panel operations for the debug log (dragging, docking, auto-hide, showing/hiding, floating windows); after
        // every change of place the panels' places follow, so a log shows where each panel was when something went wrong.
        DockableHidden += (_, e) => LogPlaces($"hidden {NameOf(e.Dockable)}");
        DockableRestored += (_, e) => LogPlaces($"shown {NameOf(e.Dockable)}");
        DockableMoved += (_, e) => DebugLog.Write("Panel", $"moved {NameOf(e.Dockable)}");
        DockableDocked += (_, e) => LogPlaces($"docked {NameOf(e.Dockable)} ({e.Operation})");
        DockableUndocked += (_, e) => LogPlaces($"undocked {NameOf(e.Dockable)} ({e.Operation})");
        WindowOpened += (_, e) => LogPlaces($"floating window opened: {PanelsOf(e.Window)}");
        WindowClosed += (_, e) => LogPlaces($"floating window closed: {PanelsOf(e.Window)}");
        WindowMoveDragEnd += (_, e) => DebugLog.Write("Panel", $"floating window moved: {PanelsOf(e.Window)}");

        // Whatever changes whether a panel is shown, which tab of its place is the active one or where it is: the stripes
        // follow (PanelsChanged).
        DockableHidden += (_, _) => RaisePanelsChanged();
        DockableRestored += (_, _) => RaisePanelsChanged();
        DockableMoved += (_, _) => RaisePanelsChanged();
        DockableDocked += (_, _) => RaisePanelsChanged();
        DockableUndocked += (_, _) => RaisePanelsChanged();
        ActiveDockableChanged += (_, _) => RaisePanelsChanged();
        WindowOpened += (_, _) => RaisePanelsChanged();
        WindowClosed += (_, _) => RaisePanelsChanged();
    }

    /// <summary>
    /// Raised after a panel was shown, closed, activated, moved to another place or floated, and after its button was put
    /// on or taken off the stripes — the stripes refresh.
    /// </summary>
    public event EventHandler? PanelsChanged;

    // Several changes of one operation (a move: restore, move, hide) make one notice.
    private int _changeBatch;
    private bool _changedInBatch;

    private void RaisePanelsChanged()
    {
        if (_changeBatch > 0)
        {
            _changedInBatch = true;
            return;
        }

        GiveRoomToShownHalves();
        ShowSplittersBetweenShownParts();
        PanelsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void InBatch(Action action)
    {
        _changeBatch++;
        try
        {
            action();
        }
        finally
        {
            _changeBatch--;
        }

        if (_changeBatch == 0 && _changedInBatch)
        {
            _changedInBatch = false;
            GiveRoomToShownHalves();
            ShowSplittersBetweenShownParts();
            PanelsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Raised for every window created for floating dockables (panels, or document tabs dragged out of the tab strip),
    /// before it is shown — the main window adds its keyboard shortcuts to it.
    /// </summary>
    public event EventHandler<HostWindow>? HostWindowCreated;

    /// <summary>
    /// Raised after a document tab was taken out of the tab strip into a window of its own (by its menu or by dragging it
    /// out): the tab is the one being worked on, so the tab manager makes it the active one — otherwise the tab strip it
    /// left activates a neighbour and the preview shows that one.
    /// </summary>
    public event EventHandler<IDockable>? DocumentFloated;

    private void RaiseDocumentFloated(IDockable dockable)
    {
        if (dockable is IDocument)
        {
            DocumentFloated?.Invoke(this, dockable);
        }
    }

    // Writes an operation to the debug log with the places of all panels after it.
    private void LogPlaces(string operation) => DebugLog.Write("Panel", $"{operation}; places: {DescribePlaces()}");

    /// <summary>
    /// Where every panel is: the dock of its place, "floating" (in its own window), "hidden", or "?" when it is nowhere
    /// (a defect) — for the debug log and the tests.
    /// </summary>
    public string DescribePlaces()
    {
        List<string> places = new();
        foreach ((string id, Tool tool) in _tools.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            string place = _hidden.Contains(id) ? "hidden"
                : WindowOf(tool) is not null ? (tool.Owner is IToolDock ? "floating" : "floating?")
                : FindOwnerDock(tool)?.Id ?? "?";
            places.Add($"{id}={place}");
        }

        return string.Join(", ", places);
    }

    /// <summary>
    /// View model of the "Table Of Contents" panel. Set by the main window
    /// BEFORE <see cref="CreateLayout"/> (the panel is created in that method).
    /// </summary>
    public TableOfContentsViewModel? TableOfContents { get; set; }

    /// <summary>
    /// View model of the "Clips" panel. Set by the main window BEFORE
    /// <see cref="CreateLayout"/>.
    /// </summary>
    public ClipsViewModel? Clips { get; set; }

    /// <summary>
    /// View model of the "Validation Results" panel. Set by the main window
    /// BEFORE <see cref="CreateLayout"/>.
    /// </summary>
    public ValidationResultsViewModel? ValidationResults { get; set; }

    /// <summary>
    /// View model of the "Checkpoints" panel. Set by the main window BEFORE
    /// <see cref="CreateLayout"/>.
    /// </summary>
    public CheckpointsViewModel? Checkpoints { get; set; }

    /// <summary>
    /// View model of the "Notifications" panel. Set by the main window BEFORE
    /// <see cref="CreateLayout"/>.
    /// </summary>
    public NotificationsViewModel? Notifications { get; set; }

    /// <summary>
    /// View model of the "Find Usages" panel. Set by the main window BEFORE
    /// <see cref="CreateLayout"/>.
    /// </summary>
    public FindUsagesViewModel? FindUsages { get; set; }

    /// <summary>Document tab area.</summary>
    public IDocumentDock? DocumentDock { get; private set; }

    /// <summary>
    /// What the document area shows while it has no tabs (the start screen). Set by the main window BEFORE
    /// <see cref="CreateLayout"/>.
    /// </summary>
    public object? DocumentsEmptyContent { get; set; }

    // Whether a book is open (SetBookOpen): the content of the book's panels is enabled only then.
    private bool _bookOpen = true;

    /// <summary>
    /// Enables the content of the panels that show the book (all but Notifications) when a book is open, and grays it
    /// out when none is.
    /// </summary>
    public void SetBookOpen(bool open)
    {
        _bookOpen = open;
        foreach (SignetTool tool in _tools.Values.OfType<SignetTool>())
        {
            tool.IsContentEnabled = open || tool.Id == DockableIds.Notifications;
        }
    }

    /// <summary>
    /// Panels that start hidden (shown after toggling them in the View menu) — Clips and
    /// Validation Results only appear on demand.
    /// </summary>
    private static readonly HashSet<string> InitiallyHidden = new(StringComparer.Ordinal)
    {
        DockableIds.Clips,
        DockableIds.ValidationResults,
        DockableIds.Checkpoints,
        DockableIds.Notifications,
        DockableIds.FindUsages,
    };

    /// <summary>
    /// Panels that are always hidden at startup, regardless of the state saved on exit —
    /// validation results from the previous session would be empty anyway; the panel only shows after F7 /
    /// the View menu.
    /// </summary>
    private static readonly HashSet<string> AlwaysHiddenOnStart = new(StringComparer.Ordinal)
    {
        DockableIds.ValidationResults,
        DockableIds.FindUsages,
    };

    /// <summary>
    /// Value in the visibility snapshots saved while panels could be collapsed by "Auto Hide" (pinned to the edge). The
    /// stripes took its place: such a panel is read as closed (its button stays on the stripe).
    /// </summary>
    public const string PinnedState = "pinned";

    // The places of the panels in the default layout, in their order.
    private static readonly (PanelPlace Place, string[] Ids)[] DefaultPlaces =
    {
        (PanelPlace.LeftTop, new[] { DockableIds.BookBrowser, DockableIds.Clips }),
        (PanelPlace.RightTop, new[] { DockableIds.Preview, DockableIds.TableOfContents, DockableIds.Checkpoints }),
        (PanelPlace.BottomLeft, new[] { DockableIds.ValidationResults, DockableIds.Notifications, DockableIds.FindUsages }),
    };

    /// <summary>The id of the column of the left places in the layout (its width is saved).</summary>
    public const string LeftColumnId = "LeftColumn";

    /// <summary>The id of the column of the right places in the layout (its width is saved).</summary>
    public const string RightColumnId = "RightColumn";

    /// <summary>The id of the row of the bottom places in the layout (its height is saved).</summary>
    public const string BottomRowId = "BottomRow";

    /// <inheritdoc />
    public override IRootDock CreateLayout()
    {
        BookBrowserTool bookBrowser = new() { ViewModel = _bookBrowser };
        ClipsTool clips = new() { ViewModel = Clips };
        PreviewTool preview = new() { ViewModel = _preview, Title = PreviewTitle };
        TableOfContentsTool toc = new() { ViewModel = TableOfContents };
        ValidationResultsTool validation = new() { ViewModel = ValidationResults };
        CheckpointsTool checkpoints = new() { ViewModel = Checkpoints };
        NotificationsTool notifications = new() { ViewModel = Notifications };
        FindUsagesTool findUsages = new() { ViewModel = FindUsages };

        foreach (Tool tool in new Tool[]
                 {
                     bookBrowser, clips, preview, toc, validation, checkpoints, notifications, findUsages,
                 })
        {
            _tools[tool.Id] = tool;
        }

        SetBookOpen(_bookOpen);

        // The stripes replace Dock's "Auto Hide" (a panel collapsed to a tab at the edge of the window) and the dragging of
        // panels by their tab or title (a stripe button is dragged instead); a panel still floats from its menu.
        foreach (Tool tool in _tools.Values)
        {
            tool.CanPin = false;
            tool.CanDrag = false;
            tool.CanDrop = false;
        }

        _regionDocks.Clear();
        _homeDocks.Clear();
        _order.Clear();
        _placeContainers.Clear();
        foreach (PanelPlace place in PanelPlaces.All)
        {
            IToolDock dock = CreateToolDock();
            dock.Id = PanelPlaces.DockId(place);
            dock.Alignment = PanelPlaces.SideOf(place) == PanelStripeSide.Left && place != PanelPlace.BottomLeft ? Alignment.Left
                : place is PanelPlace.BottomLeft or PanelPlace.BottomRight ? Alignment.Bottom
                : Alignment.Right;
            dock.VisibleDockables = CreateList<IDockable>();
            // The last panel of a place can be closed too (it is only hidden, see CloseDockable).
            dock.CanCloseLastDockable = true;
            dock.CanDrop = false;
            _regionDocks[dock.Id] = dock;
            _order[place] = new List<string>();
        }

        foreach ((PanelPlace place, string[] ids) in DefaultPlaces)
        {
            IToolDock dock = DockOf(place);
            foreach (string id in ids)
            {
                Tool tool = _tools[id];
                dock.VisibleDockables!.Add(tool);
                tool.Owner = dock;
                _order[place].Add(id);
                _homeDocks[id] = dock;
            }

            dock.ActiveDockable = dock.VisibleDockables!.FirstOrDefault();
        }

        // The halves of a column / of the bottom row: a half with no shown panel is collapsed by Dock (empty), and so is
        // the whole column when both are.
        IProportionalDock leftColumn = Halves(LeftColumnId, Orientation.Vertical, PanelPlace.LeftTop, PanelPlace.LeftBottom);
        leftColumn.Proportion = 0.18;
        IProportionalDock rightColumn = Halves(RightColumnId, Orientation.Vertical, PanelPlace.RightTop, PanelPlace.RightBottom);
        rightColumn.Proportion = 0.26;
        IProportionalDock bottomRow = Halves(BottomRowId, Orientation.Horizontal, PanelPlace.BottomLeft, PanelPlace.BottomRight);
        bottomRow.Proportion = 0.28;

        IDocumentDock documentDock = CreateDocumentDock();
        documentDock.Id = DockableIds.Documents;
        documentDock.Title = Strings.Get("Panel_Documents");
        documentDock.CanCreateDocument = false;
        // The tab area must keep its size even when empty — otherwise it collapses to zero and the side
        // panels take the whole row. Its width is what the side regions leave (no proportion of its own).
        documentDock.IsCollapsable = false;
        documentDock.VisibleDockables = CreateList<IDockable>();
        documentDock.EmptyContent = DocumentsEmptyContent;
        DocumentDock = documentDock;

        IProportionalDock mainRow = CreateProportionalDock();
        mainRow.Orientation = Orientation.Horizontal;
        mainRow.Proportion = 0.72;
        mainRow.VisibleDockables = CreateList<IDockable>(
            leftColumn,
            CreateProportionalDockSplitter(),
            documentDock,
            CreateProportionalDockSplitter(),
            rightColumn);

        // The bottom row spans the whole window width, under the side columns too (IntelliJ's bottom tool windows).
        IProportionalDock mainLayout = CreateProportionalDock();
        mainLayout.Orientation = Orientation.Vertical;
        mainLayout.VisibleDockables = CreateList<IDockable>(
            mainRow,
            CreateProportionalDockSplitter(),
            bottomRow);

        IRootDock root = CreateRootDock();
        root.Id = "Root";
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
        root.ActiveDockable = mainLayout;
        root.DefaultDockable = mainLayout;

        _splitHosts.Clear();
        foreach (IProportionalDock host in _placeContainers.Concat(new[] { mainRow, mainLayout }))
        {
            _splitHosts[host] = host.VisibleDockables!.ToArray();
        }

        _root = root;
        ShowSplittersBetweenShownParts();
        return root;
    }

    /// <summary>
    /// Gives a half of a column / of the bottom row that shows a panel again its share of the room. Dock leaves a half
    /// that had no shown panel with proportion 0 (its sibling took 1), and nothing gives it back when a panel is shown in
    /// it: the panel got no width (the bottom panels opened as an empty white area). A shown half without a share gets
    /// what its shown sibling leaves, or half of the room when the sibling took all of it, or all of it when alone.
    /// </summary>
    private void GiveRoomToShownHalves()
    {
        foreach (IProportionalDock container in _placeContainers)
        {
            if (container.VisibleDockables is not { } children)
            {
                continue;
            }

            IDock[] halves = children.OfType<IToolDock>().Cast<IDock>().ToArray();
            if (halves.Length != 2)
            {
                continue;
            }

            IDock first = halves[0];
            IDock second = halves[1];
            bool firstShown = first.VisibleDockables is { Count: > 0 };
            bool secondShown = second.VisibleDockables is { Count: > 0 };
            if (firstShown && secondShown)
            {
                bool firstFits = IsShare(first.Proportion);
                bool secondFits = IsShare(second.Proportion);
                if (firstFits && secondFits)
                {
                    continue;
                }

                if (firstFits)
                {
                    second.Proportion = 1 - first.Proportion;
                }
                else if (secondFits)
                {
                    first.Proportion = 1 - second.Proportion;
                }
                else
                {
                    first.Proportion = 0.5;
                    second.Proportion = 0.5;
                }
            }
            else if (firstShown && !(first.Proportion > 0))
            {
                first.Proportion = 1;
            }
            else if (secondShown && !(second.Proportion > 0))
            {
                second.Proportion = 1;
            }
        }
    }

    /// <summary>
    /// Keeps in the layout only the splitters with something shown on both sides. Dock collapses a dock with no shown
    /// panel but leaves the splitter next to it, which cut a gap into the frame of the panel above (at the foot of a column
    /// whose second half is empty) and could be dragged for nothing. A splitter taken out goes back to its place as soon
    /// as both its sides show something; the parts themselves are never moved (their views and contents stay).
    /// </summary>
    private void ShowSplittersBetweenShownParts()
    {
        foreach ((IProportionalDock host, IDockable[] all) in _splitHosts)
        {
            if (host.VisibleDockables is not { } children)
            {
                continue;
            }

            int kept = 0;
            bool shownBefore = false;
            for (int i = 0; i < all.Length; i++)
            {
                IDockable child = all[i];
                bool wanted = child is not IProportionalDockSplitter
                    || shownBefore && all.Skip(i + 1).TakeWhile(d => d is not IProportionalDockSplitter).Any(IsShownPart);
                if (child is not IProportionalDockSplitter && IsShownPart(child))
                {
                    shownBefore = true;
                }

                int at = children.IndexOf(child);
                if (wanted && at < 0)
                {
                    children.Insert(Math.Min(kept, children.Count), child);
                    child.Owner = host;
                }
                else if (!wanted && at >= 0)
                {
                    children.RemoveAt(at);
                }

                if (wanted)
                {
                    kept++;
                }
            }
        }
    }

    // Whether a part of the layout shows something (Dock collapses one that does not).
    private static bool IsShownPart(IDockable part) => part switch
    {
        IDocumentDock => true,
        IToolDock tools => tools.VisibleDockables is { Count: > 0 },
        IProportionalDock row => row.VisibleDockables?.Any(d => d is not IProportionalDockSplitter && IsShownPart(d)) == true,
        _ => true,
    };

    // A share one half can have with its sibling shown too.
    private static bool IsShare(double proportion) =>
        proportion >= MinRestoredProportion && proportion <= 1 - MinRestoredProportion;

    // A column or a row of two places (each one half of it), with a splitter between them.
    private IProportionalDock Halves(string id, Orientation orientation, PanelPlace first, PanelPlace second)
    {
        IProportionalDock halves = CreateProportionalDock();
        halves.Id = id;
        halves.Orientation = orientation;
        halves.VisibleDockables = CreateList<IDockable>(DockOf(first), CreateProportionalDockSplitter(), DockOf(second));
        _placeContainers.Add(halves);
        return halves;
    }

    private IToolDock DockOf(PanelPlace place) => _regionDocks[PanelPlaces.DockId(place)];

    /// <inheritdoc />
    public override void InitLayout(IDockable layout)
    {
        base.InitLayout(layout);

        foreach (string id in InitiallyHidden)
        {
            if (_tools.TryGetValue(id, out Tool? tool))
            {
                HideDockable(tool);
                _hidden.Add(id);
            }
        }
    }

    /// <summary>Whether the panel with the given identifier is currently visible.</summary>
    public bool IsToolVisible(string id) =>
        _tools.ContainsKey(id) && !_hidden.Contains(id);

    /// <summary>All panel identifiers.</summary>
    public IReadOnlyCollection<string> ToolIds => _tools.Keys;

    /// <summary>Recomputes the panel titles (keys <c>Panel_&lt;Id&gt;</c>) after the UI language changes.</summary>
    public void RefreshTitles()
    {
        foreach (Tool tool in _tools.Values)
        {
            if (Strings.TryGet("Panel_" + tool.Id) is { } title)
            {
                tool.Title = title;
            }
        }

        if (DocumentDock is not null)
        {
            DocumentDock.Title = Strings.Get("Panel_Documents");
        }

        // The name of the shown file goes back after the title of the panel was reset above.
        UpdatePreviewTitle();
        RaisePanelsChanged();
    }

    /// <summary>
    /// The title of the Preview panel: the name of the file it shows in square brackets
    /// (<c>Preview [chapter1.xhtml]</c>), or the bare name of the panel when it shows nothing. The file name alone —
    /// the whole book path would not fit in the title bar of the panel.
    /// </summary>
    public string PreviewTitle
    {
        get
        {
            string title = Strings.Get("Panel_Preview");
            if (_preview?.CurrentBookPath is not { Length: > 0 } bookPath)
            {
                return title;
            }

            string name = bookPath[(bookPath.LastIndexOf('/') + 1)..];
            return name.Length == 0 ? title : $"{title} [{name}]";
        }
    }

    private void UpdatePreviewTitle()
    {
        if (_tools.TryGetValue(DockableIds.Preview, out Tool? preview))
        {
            preview.Title = PreviewTitle;
        }
    }

    /// <summary>Snapshot of panel visibility (id → <c>"1"</c> visible, <c>"0"</c> hidden) — for persistence.</summary>
    public IReadOnlyDictionary<string, string> CaptureToolVisibility()
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (string id in _tools.Keys)
        {
            result[id] = _hidden.Contains(id) ? "0" : "1";
        }

        return result;
    }

    /// <summary>
    /// Applies the stored panel visibility. Panels from <see cref="AlwaysHiddenOnStart"/> stay hidden regardless of the
    /// stored state; a panel saved as collapsed by "Auto Hide" (<see cref="PinnedState"/>, before the stripes) is read as
    /// hidden — its button on the stripe shows it.
    /// </summary>
    public void ApplyToolVisibility(IReadOnlyDictionary<string, string> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        InBatch(() =>
        {
            foreach (KeyValuePair<string, Tool> pair in _tools)
            {
                if (!stored.TryGetValue(pair.Key, out string? v))
                {
                    continue;
                }

                bool want = v == "1" && !AlwaysHiddenOnStart.Contains(pair.Key);
                bool have = !_hidden.Contains(pair.Key);
                if (want && !have)
                {
                    ShowHiddenTool(pair.Value);
                }
                else if (!want && have)
                {
                    HideDockable(pair.Value);
                    _hidden.Add(pair.Key);
                }
            }
        });
    }

    // ---- the places of the panels and the stripes ----------------------------------------------------------------

    /// <summary>The place of a panel (where it is docked, or where it docks back to when it floats or is closed).</summary>
    public PanelPlace PlaceOf(string id) =>
        _order.First(pair => pair.Value.Contains(id, StringComparer.Ordinal)).Key;

    /// <summary>The panels of a place in their order (closed and floating ones included): the buttons of its stripe group.</summary>
    public IReadOnlyList<string> PanelsAt(PanelPlace place) => _order[place].ToList();

    /// <summary>
    /// Moves a panel to another place (or to another position in its own), at <paramref name="index"/> among the panels of
    /// the target place (clamped; the panel itself not counted). A shown panel moves at once and stays the active tab; a
    /// closed one shows there next time; a floating one docks back there.
    /// </summary>
    public void MoveTool(string id, PanelPlace place, int index)
    {
        if (!_tools.TryGetValue(id, out Tool? tool))
        {
            return;
        }

        PanelPlace from = PlaceOf(id);
        List<string> target = _order[place];
        _order[from].Remove(id);
        target.Insert(Math.Clamp(index, 0, target.Count), id);
        IToolDock dock = DockOf(place);
        DebugLog.Write("Panel", $"move {id}: {from} → {place} at {index}");

        InBatch(() =>
        {
            _homeDocks[id] = dock;
            if (IsFloating(tool))
            {
                // It docks back into the new place when its window closes.
            }
            else if (_hidden.Contains(id))
            {
                // A closed panel is moved by Dock only while shown: shown, moved, closed again (one notice for the stripes).
                if (!ReferenceEquals(HiddenOwner(tool), dock))
                {
                    RestoreDockable(tool);
                    MoveIntoPlace(tool, dock);
                    HideDockable(tool);
                }
            }
            else
            {
                MoveIntoPlace(tool, dock);
                SetActiveDockable(tool);
            }

            SortDock(DockOf(from));
            SortDock(dock);
            RaisePanelsChanged();
        });
    }

    // The dock a closed panel goes back to when it is shown.
    private static IDockable? HiddenOwner(Tool tool) => tool.OriginalOwner ?? tool.Owner;

    private void MoveIntoPlace(Tool tool, IToolDock dock)
    {
        if (tool.Owner is IDock source && !ReferenceEquals(source, dock))
        {
            bool outer = _dockingBack;
            _dockingBack = true;
            try
            {
                MoveDockable(source, dock, tool, null);
            }
            finally
            {
                _dockingBack = outer;
            }
        }
    }

    // Puts the tabs of a place in the order of its panels (Dock adds a panel shown again or moved in at the end).
    private void SortDock(IToolDock dock)
    {
        if (dock.VisibleDockables is not { } tabs || !PanelPlaces.TryParseDockId(dock.Id, out PanelPlace place))
        {
            return;
        }

        List<IDockable> wanted = _order[place]
            .Select(id => _tools[id])
            .Where(tabs.Contains)
            .Cast<IDockable>()
            .ToList();
        for (int i = 0; i < wanted.Count; i++)
        {
            if (!ReferenceEquals(tabs[i], wanted[i]))
            {
                tabs.Remove(wanted[i]);
                tabs.Insert(i, wanted[i]);
            }
        }
    }

    /// <summary>Whether a panel's button is on the stripes (not taken off by "Hide" of its menu).</summary>
    public bool IsOnStripe(string id) => _tools.ContainsKey(id) && !_offStripe.Contains(id);

    /// <summary>
    /// Takes a panel's button off the stripes ("Hide" of its menu, closing the panel too) or puts it back ("More Tool
    /// Windows").
    /// </summary>
    public void SetOnStripe(string id, bool onStripe)
    {
        if (!_tools.TryGetValue(id, out Tool? tool) || onStripe == IsOnStripe(id))
        {
            return;
        }

        InBatch(() =>
        {
            if (onStripe)
            {
                _offStripe.Remove(id);
            }
            else
            {
                _offStripe.Add(id);
                if (!_hidden.Contains(id))
                {
                    HideTool(tool);
                }
            }

            RaisePanelsChanged();
        });
    }

    /// <summary>Snapshot of the panels whose button is off the stripes (id → <c>"0"</c>) — for persistence.</summary>
    public IReadOnlyDictionary<string, string> CaptureOffStripe() =>
        _offStripe.OrderBy(id => id, StringComparer.Ordinal).ToDictionary(id => id, _ => "0", StringComparer.Ordinal);

    /// <summary>Takes off the stripes the buttons saved by <see cref="CaptureOffStripe"/>.</summary>
    public void ApplyOffStripe(IReadOnlyDictionary<string, string> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        foreach (KeyValuePair<string, string> pair in stored.Where(p => p.Value == "0" && _tools.ContainsKey(p.Key)))
        {
            _offStripe.Add(pair.Key);
        }

        RaisePanelsChanged();
    }

    /// <summary>
    /// Whether a panel is shown and is the one its place shows (the active tab), or floats — a click on its stripe button
    /// then closes it.
    /// </summary>
    public bool IsToolInFront(string id)
    {
        if (!_tools.TryGetValue(id, out Tool? tool) || _hidden.Contains(id))
        {
            return false;
        }

        return IsFloating(tool) || FindOwnerDock(tool) is { } dock && ReferenceEquals(dock.ActiveDockable, tool);
    }

    /// <summary>
    /// A click on a panel's stripe button (IntelliJ's): a closed panel opens, a panel behind another tab of its place comes
    /// to the front, a panel in front closes.
    /// </summary>
    public void ClickStripeButton(string id)
    {
        if (!_tools.TryGetValue(id, out Tool? tool))
        {
            return;
        }

        if (IsToolInFront(id))
        {
            HideTool(tool);
        }
        else
        {
            FocusTool(id);
        }
    }

    /// <summary>The title of a panel on its stripe button: the name of the panel (not, e.g., the file the Preview shows).</summary>
    public static string StripeTitle(string id) => Strings.TryGet("Panel_" + id) ?? id;

    /// <summary>Toggles the panel's visibility; returns the new visibility state.</summary>
    public bool ToggleTool(string id)
    {
        if (!_tools.TryGetValue(id, out Tool? tool))
        {
            return false;
        }

        if (_hidden.Contains(id))
        {
            ShowHiddenTool(tool);
            SetActiveDockable(tool);
            return true;
        }

        HideTool(tool);
        return false;
    }

    /// <summary>
    /// "Close" on a panel (its title bar menu or the × button) hides it, exactly like switching it off in the View
    /// menu: Dock's own close would remove the panel from the layout for good and the View menu could not bring it
    /// back. Document tabs and other dockables are closed by Dock as usual.
    /// </summary>
    public override void CloseDockable(IDockable? dockable)
    {
        if (dockable is Tool tool && _tools.TryGetValue(tool.Id, out Tool? known) && ReferenceEquals(known, tool))
        {
            DebugLog.Write("Panel", $"close {tool.Id}");
            if (!_hidden.Contains(tool.Id))
            {
                HideTool(tool);
            }

            return;
        }

        base.CloseDockable(dockable);
    }

    private void HideTool(Tool tool)
    {
        // A floating panel remembers its window's place (showing it again floats it there) and is docked back first:
        // Dock hides and restores a panel within a dock of the main layout, and its emptied window closes.
        if (IsFloating(tool) && WindowOf(tool) is { } window)
        {
            _floatingWhenHidden[tool.Id] = BoundsOf(window);
        }

        DockBackIfFloating(tool);
        HideDockable(tool);
        _hidden.Add(tool.Id);
    }

    // Makes a hidden panel visible again: in its region, or in its own window at the remembered place when it was
    // hidden while floating.
    private void ShowHiddenTool(Tool tool)
    {
        InBatch(() =>
        {
            // Shown in any way (the View menu, a shortcut, new results), a panel gets its stripe button back.
            _offStripe.Remove(tool.Id);
            RestoreDockable(tool);
            _hidden.Remove(tool.Id);
            if (FindOwnerDock(tool) is { } dock)
            {
                SortDock(dock);
            }

            if (_floatingWhenHidden.Remove(tool.Id, out FloatingBounds bounds))
            {
                FloatAt(tool, bounds);
            }

            RaisePanelsChanged();
        });
    }

    private static FloatingBounds BoundsOf(IDockWindow window)
    {
        // The window model takes the host's current place and size.
        window.Save();
        return new FloatingBounds(window.X, window.Y, window.Width, window.Height);
    }

    // Floats a panel in its own window at the given place (corrected when it is no longer fully on screen).
    private void FloatAt(Tool tool, FloatingBounds bounds)
    {
        FloatDockable(tool);
        if (WindowOf(tool) is { } window)
        {
            Place(window, bounds);
        }
    }

    private static void Place(IDockWindow window, FloatingBounds bounds)
    {
        window.X = bounds.X;
        window.Y = bounds.Y;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.Host?.SetPosition(bounds.X, bounds.Y);
        window.Host?.SetSize(bounds.Width, bounds.Height);
        if (window.Host is Avalonia.Controls.Window host)
        {
            Dispatcher.UIThread.Post(() => WindowPlacement.EnsureVisible(host), DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Snapshot of the places of the panels (every place with all its panels in their order, closed and floating ones
    /// included, its active tab and its share of its column / row) and of the sizes of the columns and of the bottom row —
    /// to be persisted in <see cref="SettingsStore.MainWindowDockLayout"/>.
    /// </summary>
    public DockLayoutState CaptureLayoutState()
    {
        List<DockRegionState> regions = new();
        foreach (PanelPlace place in PanelPlaces.All)
        {
            IToolDock dock = DockOf(place);
            string? activeId = (dock.ActiveDockable as IDockable)?.Id;
            regions.Add(new DockRegionState(dock.Id, Saved(dock.Proportion), _order[place].ToList(), activeId));
        }

        foreach (IProportionalDock container in _placeContainers)
        {
            regions.Add(new DockRegionState(container.Id, Saved(container.Proportion), Array.Empty<string>(), null));
        }

        // The document area takes what the side columns leave, so its proportion may be unset (NaN) — which JSON
        // cannot store; 0 = not saved.
        return new DockLayoutState(regions, Saved(DocumentDock?.Proportion ?? double.NaN));
    }

    // A proportion as it is saved: 0 for none (NaN, which JSON cannot store).
    private static double Saved(double proportion) => double.IsNaN(proportion) ? 0 : proportion;

    /// <summary>
    /// Puts the panels in the places of a persisted snapshot (<see cref="CaptureLayoutState"/>), in its order, with the
    /// active tab of each place and the share of each half. A snapshot from before the places were split
    /// (LeftDock, RightDock, BottomDock) puts the panels of each region in the first half of it. Unknown panels and docks
    /// are skipped; panels the snapshot does not name stay where they are. Called AFTER <see cref="CreateLayout"/> /
    /// <see cref="InitLayout"/>, BEFORE restoring panel visibility.
    /// </summary>
    public void ApplyPanelPlaces(DockLayoutState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        InBatch(() =>
        {
            foreach (DockRegionState region in state.Regions ?? Array.Empty<DockRegionState>())
            {
                if (!PanelPlaces.TryParseDockId(region.DockId, out PanelPlace place))
                {
                    continue;
                }

                int index = 0;
                foreach (string id in region.ToolIds ?? Array.Empty<string>())
                {
                    if (_tools.ContainsKey(id))
                    {
                        MoveTool(id, place, index++);
                    }
                }

                // The share of a half within its column / row; a legacy region's proportion was the column's width.
                IToolDock dock = DockOf(place);
                if (region.DockId == dock.Id && region.Proportion > 0 && region.Proportion < 1)
                {
                    dock.Proportion = Math.Clamp(region.Proportion, MinRestoredProportion, 1 - MinRestoredProportion);
                }

                if (region.ActiveToolId is not null && _tools.TryGetValue(region.ActiveToolId, out Tool? active)
                    && ReferenceEquals(FindOwnerDock(active), dock))
                {
                    dock.ActiveDockable = active;
                }
            }
        });
    }

    /// <summary>
    /// Restores from the persisted snapshot ONLY the widths of the side columns (the left one — Book Browser, the right
    /// one — Preview; in a snapshot from before the places were split, LeftDock and RightDock). It does not touch the
    /// vertical proportions (document area / bottom row) — those degenerated the layout when the bottom region was saved
    /// as collapsed (documents proportion ≈ 1). Each width is clamped and their sum is limited so that the middle column
    /// with the tabs always has room.
    /// </summary>
    public void ApplySideRegionWidths(DockLayoutState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        double left = RestorableWidth(state, LeftColumnId, PanelPlaces.LegacyLeftDock);
        double right = RestorableWidth(state, RightColumnId, PanelPlaces.LegacyRightDock);
        double sum = (double.IsNaN(left) ? 0 : left) + (double.IsNaN(right) ? 0 : right);
        if (sum > MaxRestoredSidesSum)
        {
            double scale = MaxRestoredSidesSum / sum;
            left *= scale;
            right *= scale;
        }

        if (!double.IsNaN(left))
        {
            Container(LeftColumnId).Proportion = left;
        }

        if (!double.IsNaN(right))
        {
            Container(RightColumnId).Proportion = right;
        }
    }

    private IProportionalDock Container(string id) => _placeContainers.First(c => c.Id == id);

    /// <summary>Upper bound of a single side column's width when restoring.</summary>
    private const double MaxRestoredSideProportion = 0.6;

    /// <summary>Upper bound of the sum of both side columns' widths when restoring.</summary>
    private const double MaxRestoredSidesSum = 0.8;

    // NaN = no (sensible) saved value — the column keeps its default width.
    private static double RestorableWidth(DockLayoutState state, string columnId, string legacyId)
    {
        DockRegionState? region = state.Regions?.FirstOrDefault(r => r.DockId == columnId)
                                  ?? state.Regions?.FirstOrDefault(r => r.DockId == legacyId);
        if (region is null || double.IsNaN(region.Proportion) || region.Proportion <= 0)
        {
            return double.NaN;
        }

        return Math.Clamp(region.Proportion, MinRestoredProportion, MaxRestoredSideProportion);
    }

    /// <summary>
    /// The panel a region (the dock of the left, right or bottom panels) stands for when it is dragged out of the window
    /// by its title bar: Dock then floats the region itself, which must stay in the main layout (the panels in a floating
    /// region still counted as docked, so "Dock" in their menu collapsed them in the window instead of docking them
    /// back, and docking back again recursed until the stack overflowed). The panel the region shows floats instead,
    /// as with the panel's own "Float".
    /// </summary>
    private Tool? PanelInsteadOfRegion(IDockable dockable) =>
        dockable is IToolDock dock && _regionDocks.ContainsValue(dock)
            ? dock.ActiveDockable as Tool ?? dock.VisibleDockables?.OfType<Tool>().FirstOrDefault()
            : null;

    // Dock places a floating window at the pointer position stored on the dockable it floats. A region's title bar
    // dragged out stores it on the region; the panel floated instead must carry it, or the window opens at the panel's
    // stale position. Copied only when the region has a whole position (Dock leaves NaN when there is none).
    private static void CopyPointerScreenPosition(IDockable source, Tool target)
    {
        source.GetPointerScreenPosition(out double x, out double y);
        if (!double.IsNaN(x) && !double.IsNaN(y))
        {
            target.SetPointerScreenPosition(x, y);
        }
    }

    /// <inheritdoc />
    public override void FloatDockable(IDockable dockable)
    {
        if (PanelInsteadOfRegion(dockable) is { } panel)
        {
            CopyPointerScreenPosition(dockable, panel);
            DebugLog.Write("Panel", $"float {NameOf(dockable)} requested (its title bar dragged out): floating the panel {panel.Id} instead");
            FloatDockable(panel);
            return;
        }

        DebugLog.Write("Panel", $"float {NameOf(dockable)}");
        RememberHomes(dockable);
        base.FloatDockable(dockable);
        RaiseDocumentFloated(dockable);
    }

    /// <inheritdoc />
    public override void FloatDockable(IDockable dockable, DockWindowOptions? options)
    {
        if (PanelInsteadOfRegion(dockable) is { } panel)
        {
            CopyPointerScreenPosition(dockable, panel);
            DebugLog.Write("Panel", $"float {NameOf(dockable)} requested (with options): floating the panel {panel.Id} instead");
            FloatDockable(panel, options);
            return;
        }

        DebugLog.Write("Panel", $"float {NameOf(dockable)} (with options)");
        RememberHomes(dockable);
        base.FloatDockable(dockable, options);
        RaiseDocumentFloated(dockable);
    }

    /// <inheritdoc />
    public override void FloatAllDockables(IDockable dockable)
    {
        RememberHomes(dockable.Owner ?? dockable);
        base.FloatAllDockables(dockable);
    }

    /// <inheritdoc />
    public override void SplitToWindow(IDock dock, IDockable dockable, double x, double y, double width, double height)
    {
        IDockable moved = PanelInsteadOfRegion(dockable) ?? dockable;
        DebugLog.Write("Panel", $"split to window {NameOf(moved)} at {x:0},{y:0} {width:0}×{height:0}");
        RememberHomes(moved);
        base.SplitToWindow(dock, moved, x, y, width, height);
        RaiseDocumentFloated(moved);
    }

    /// <inheritdoc />
    public override void SplitToWindow(IDock dock, IDockable dockable, double x, double y, double width, double height, DockWindowOptions? options)
    {
        IDockable moved = PanelInsteadOfRegion(dockable) ?? dockable;
        DebugLog.Write("Panel", $"split to window {NameOf(moved)} at {x:0},{y:0} {width:0}×{height:0} (with options)");
        RememberHomes(moved);
        base.SplitToWindow(dock, moved, x, y, width, height, options);
        RaiseDocumentFloated(moved);
    }

    // Set while a panel is moved back into its region: Dock's MoveDockable then calls UnpinDockable, which calls
    // PinDockable again (it toggles auto-hide); that call must reach Dock, not start another round of docking back.
    private bool _dockingBack;

    /// <summary>
    /// "Dock" in a panel's menu: Dock only uses it to undo "Auto Hide", so for a floating panel it did nothing; here it
    /// docks the panel back into its place (and closes the emptied window). There is no "Auto Hide" (the stripes took its
    /// place), so a docked panel is never pinned; Dock's own call while it moves a panel back is passed on.
    /// </summary>
    public override void PinDockable(IDockable dockable)
    {
        bool floating = dockable is Tool t && IsFloating(t);
        DebugLog.Write("Panel", $"pin/dock requested for {NameOf(dockable)} (floating: {floating}, docking back already: {_dockingBack}); places: {DescribePlaces()}");
        if (!_dockingBack && dockable is Tool tool)
        {
            if (floating)
            {
                DockBackIfFloating(tool);
            }

            return;
        }

        base.PinDockable(dockable);
    }

    /// <summary>
    /// Closing a floating window (its ×) hides its panels like their own × does: showing one again (View menu) floats it
    /// at the window's place. They are docked back into the regions they were taken from first, otherwise they would be
    /// closed together with the window and vanish. When the main window closes them all
    /// (<see cref="CloseFloatingWindows"/>) the panels are only docked back.
    /// </summary>
    public override bool OnWindowClosing(IDockWindow? window)
    {
        FloatingBounds? bounds = !_closingAllWindows && window is not null ? BoundsOf(window) : null;
        bool close = base.OnWindowClosing(window);
        if (close && window?.Layout is { } layout)
        {
            // Document tabs go back to the tab strip of the main window: closing the window only "attaches" them again
            // (closing a tab is what closes the file).
            foreach (IDocument document in DocumentsIn(layout).ToList())
            {
                DockDocumentBack(document);
            }

            foreach (Tool tool in ToolsIn(layout).ToList())
            {
                DockHome(tool);
                if (bounds is { } place && _tools.TryGetValue(tool.Id, out Tool? known) && ReferenceEquals(known, tool)
                    && !_hidden.Contains(tool.Id))
                {
                    DebugLog.Write("Panel", $"close floating {tool.Id}");
                    HideDockable(tool);
                    _hidden.Add(tool.Id);
                    _floatingWhenHidden[tool.Id] = place;
                }
            }
        }

        return close;
    }

    /// <summary>
    /// The six places and the columns / row that hold them are never collapsed out of the layout: when their last panel
    /// is floated Dock would remove the emptied place, and the panel could not be docked back into it. An empty place
    /// stays in the layout like one whose panels are all hidden (Dock gives it no room).
    /// </summary>
    public override void CollapseDock(IDock dock)
    {
        if (dock is IToolDock toolDock && _regionDocks.ContainsValue(toolDock)
            || dock is IProportionalDock container && _placeContainers.Contains(container))
        {
            return;
        }

        base.CollapseDock(dock);
    }

    /// <summary>
    /// Closes every floating window (their panels dock back into their regions). Called when the main window closes:
    /// a floating window is not owned by it, so it would stay open and keep the application running.
    /// </summary>
    public void CloseFloatingWindows()
    {
        _closingAllWindows = true;
        try
        {
            foreach (IDockWindow window in _root?.Windows?.ToList() ?? new List<IDockWindow>())
            {
                window.Exit();
            }
        }
        finally
        {
            _closingAllWindows = false;
        }
    }

    /// <summary>
    /// Snapshot of the floating panel windows (id → <c>"window;x;y;width;height"</c>, invariant culture): the panels
    /// sharing a window get the same window number. For persistence — taken before <see cref="CloseFloatingWindows"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> CaptureFloatingPanels()
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        int index = 0;
        foreach (IDockWindow window in _root?.Windows?.ToList() ?? new List<IDockWindow>())
        {
            List<Tool> tools = window.Layout is { } layout
                ? ToolsIn(layout).Where(t => _tools.ContainsKey(t.Id) && !_hidden.Contains(t.Id)).ToList()
                : new List<Tool>();
            if (tools.Count == 0)
            {
                continue;
            }

            window.Save();
            string bounds = string.Create(CultureInfo.InvariantCulture, $"{index};{window.X};{window.Y};{window.Width};{window.Height}");
            foreach (Tool tool in tools)
            {
                result[tool.Id] = bounds;
            }

            index++;
        }

        // Panels hidden while floating keep their window's place: shown again (also after a restart) they float there.
        foreach (KeyValuePair<string, FloatingBounds> pair in _floatingWhenHidden.Where(p => _hidden.Contains(p.Key)))
        {
            FloatingBounds b = pair.Value;
            result[pair.Key] = string.Create(CultureInfo.InvariantCulture, $"{index++};{b.X};{b.Y};{b.Width};{b.Height}");
        }

        return result;
    }

    /// <summary>
    /// Floats the panels saved by <see cref="CaptureFloatingPanels"/> again, each window at its saved place (corrected
    /// when it is no longer fully on screen). Hidden panels and unreadable entries are skipped. Called once the main
    /// window is shown.
    /// </summary>
    public void ApplyFloatingPanels(IReadOnlyDictionary<string, string> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var entries = stored
            .Select(pair => (Tool: _tools.GetValueOrDefault(pair.Key), Bounds: ParseFloatingBounds(pair.Value)))
            .Where(x => x.Tool is not null && x.Bounds is not null)
            .ToList();

        // A hidden panel that floated is only remembered: showing it floats it at its place.
        foreach (var (tool, bounds) in entries.Where(x => _hidden.Contains(x.Tool!.Id)))
        {
            (_, double hx, double hy, double hw, double hh) = bounds!.Value;
            _floatingWhenHidden[tool!.Id] = new FloatingBounds(hx, hy, hw, hh);
        }

        var groups = entries
            .Where(x => !_hidden.Contains(x.Tool!.Id))
            .GroupBy(x => x.Bounds!.Value.Window);
        foreach (var group in groups)
        {
            Tool first = group.First().Tool!;
            (_, double x, double y, double width, double height) = group.First().Bounds!.Value;
            FloatDockable(first);
            IDockWindow? window = WindowOf(first);
            if (window is null)
            {
                continue;
            }

            foreach (Tool other in group.Skip(1).Select(g => g.Tool!))
            {
                if (other.Owner is IDock source && first.Owner is IDock target)
                {
                    RememberHomes(other);
                    MoveDockable(source, target, other, null);
                }
            }

            Place(window, new FloatingBounds(x, y, width, height));
        }
    }

    private static (int Window, double X, double Y, double Width, double Height)? ParseFloatingBounds(string value)
    {
        string[] parts = value.Split(';');
        if (parts.Length != 5 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int window))
        {
            return null;
        }

        double[] numbers = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) || !double.IsFinite(numbers[i]))
            {
                return null;
            }
        }

        return numbers[2] > 0 && numbers[3] > 0 ? (window, numbers[0], numbers[1], numbers[2], numbers[3]) : null;
    }

    private static string NameOf(IDockable? dockable) => dockable is null ? "(none)" : dockable.Id;

    private static string PanelsOf(IDockWindow? window) =>
        window?.Layout is { } layout ? string.Join(", ", ToolsIn(layout).Select(t => t.Id)) : "(empty)";

    private IDockWindow? WindowOf(Tool tool) =>
        _root?.Windows?.FirstOrDefault(w => w.Layout is { } layout && ToolsIn(layout).Contains(tool));

    /// <summary>Whether the panel floats in its own window (neither docked in a region, collapsed, nor hidden).</summary>
    public bool IsToolFloating(string id) => _tools.TryGetValue(id, out Tool? tool) && IsFloating(tool);

    private bool IsFloating(Tool tool) =>
        !_hidden.Contains(tool.Id) && FindOwnerDock(tool) is null && tool.Owner is not null;

    // Remembers the current region of every panel in the dockable (a panel or a dock) before it leaves for a window.
    private void RememberHomes(IDockable dockable)
    {
        foreach (Tool tool in ToolsIn(dockable))
        {
            if (FindOwnerDock(tool) is { } region)
            {
                _homeDocks[tool.Id] = region;
            }
        }
    }

    // Docks a floating panel back and closes its window when it is left empty.
    private void DockBackIfFloating(Tool tool)
    {
        if (!IsFloating(tool))
        {
            return;
        }

        IDockWindow? window = _root?.Windows?.FirstOrDefault(w => w.Layout is { } layout && ToolsIn(layout).Contains(tool));
        DockHome(tool);
        if (window?.Layout is { } emptied && !ToolsIn(emptied).Any())
        {
            CloseWindow(window);
        }
    }

    private void DockHome(Tool tool)
    {
        if (tool.Owner is not IDock source || !_homeDocks.TryGetValue(tool.Id, out IToolDock? home))
        {
            DebugLog.Write("Panel", $"dock back {tool.Id}: no home region or no owner (owner: {tool.Owner?.Id ?? "none"}), left where it is");
            return;
        }

        DebugLog.Write("Panel", $"dock back {tool.Id}: {source.Id} → {home.Id}");
        bool outer = _dockingBack;
        _dockingBack = true;
        try
        {
            MoveDockable(source, home, tool, null);
        }
        finally
        {
            _dockingBack = outer;
        }

        SetActiveDockable(tool);
    }

    // Moves a document tab from a floating window into the tab strip of the main window and makes it the active one.
    private void DockDocumentBack(IDocument document)
    {
        if (DocumentDock is not { } main || document.Owner is not IDock source || ReferenceEquals(source, main))
        {
            return;
        }

        DebugLog.Write("Panel", $"dock back document {document.Id}: {source.Id} → {main.Id}");
        MoveDockable(source, main, document, null);
        SetActiveDockable(document);
    }

    private static IEnumerable<IDocument> DocumentsIn(IDockable dockable)
    {
        // Dock's Tool also implements IDocument: a panel is not a document tab.
        if (dockable is Tool)
        {
            yield break;
        }

        if (dockable is IDocument document)
        {
            yield return document;
        }
        else if (dockable is IDock dock && dock.VisibleDockables is { } children)
        {
            foreach (IDockable child in children)
            {
                foreach (IDocument nested in DocumentsIn(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<Tool> ToolsIn(IDockable dockable)
    {
        if (dockable is Tool tool)
        {
            yield return tool;
        }
        else if (dockable is IDock dock && dock.VisibleDockables is { } children)
        {
            foreach (IDockable child in children)
            {
                foreach (Tool nested in ToolsIn(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private IToolDock? FindOwnerDock(Tool tool)
    {
        foreach (IToolDock dock in _regionDocks.Values)
        {
            if (dock.VisibleDockables is not null && dock.VisibleDockables.Contains(tool))
            {
                return dock;
            }
        }

        return null;
    }

    /// <summary>Makes the panel visible (if hidden) and activates it — for the "Focus on…" actions, a click on its stripe
    /// button and new results shown in a panel.</summary>
    public void FocusTool(string id)
    {
        if (!_tools.TryGetValue(id, out Tool? tool))
        {
            return;
        }

        if (_hidden.Contains(id))
        {
            ShowHiddenTool(tool);
        }

        SetActiveDockable(tool);
        if (_root is not null)
        {
            SetFocusedDockable(_root, tool);
        }
    }

    /// <summary>
    /// Sets the attention state of a panel tab: <paramref name="count"/> &gt; 0 puts a red dot on the tab's bell
    /// and "[count]" after it (<see cref="SignetTool.AttentionCount"/>).
    /// </summary>
    public void SetToolAttention(string id, int count)
    {
        if (_tools.TryGetValue(id, out Tool? tool) && tool is SignetTool signetTool && signetTool.AttentionCount != count)
        {
            signetTool.AttentionCount = count;
            RaisePanelsChanged();
        }
    }

    /// <summary>The attention state of a panel (<see cref="SetToolAttention"/>): its unread errors, 0 = none.</summary>
    public int AttentionOf(string id) =>
        _tools.TryGetValue(id, out Tool? tool) && tool is SignetTool signetTool ? signetTool.AttentionCount : 0;

    // The place of a floating panel window (Dock's window coordinates).
    private readonly record struct FloatingBounds(double X, double Y, double Width, double Height);
}
