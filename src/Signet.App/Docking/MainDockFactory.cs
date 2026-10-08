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

namespace Signet.App.Docking;

/// <summary>
/// Snapshot of a single dock region (Left/Right/Bottom) to persist — order and
/// assignment of panels plus the region's proportion. Part of <see cref="DockLayoutState"/>.
/// </summary>
public sealed record DockRegionState(string DockId, double Proportion, IReadOnlyList<string> ToolIds, string? ActiveToolId);

/// <summary>
/// Snapshot of the main window's dock layout (dock layout persistence across restarts):
/// assignment of panels to regions, their order, the region's active tab and the proportions
/// (of the regions and of the document area). It does not cover document tabs (handled separately by
/// <c>TabManager.CaptureSession</c>), panel visibility (handled separately by
/// <see cref="MainDockFactory.CaptureToolVisibility"/>) or floating windows (a floating panel is saved in the region it
/// was taken from; its window is persisted separately by <see cref="MainDockFactory.CaptureFloatingPanels"/>).
/// </summary>
public sealed record DockLayoutState(IReadOnlyList<DockRegionState> Regions, double DocumentsProportion);

/// <summary>
/// Dock layout factory of the main window: BookBrowser and Clips on the left, Preview and TOC
/// on the right, Validation Results at the bottom, document tabs in the middle.
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
    private readonly Dictionary<string, int> _attentionCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IToolDock> _regionDocks = new(StringComparer.Ordinal);

    // The region a panel belongs to while it floats in its own window: where closing the window (or hiding the
    // panel) docks it back. Filled with the default layout and updated whenever a panel is floated.
    private readonly Dictionary<string, IToolDock> _homeDocks = new(StringComparer.Ordinal);
    private readonly BookBrowserViewModel? _bookBrowser;
    private readonly PreviewViewModel? _preview;
    private IRootDock? _root;

    /// <summary>Creates the factory; the optional Book Browser and Preview panel view models are embedded in the panels.</summary>
    public MainDockFactory(BookBrowserViewModel? bookBrowser = null, PreviewViewModel? preview = null)
    {
        _bookBrowser = bookBrowser;
        _preview = preview;

        // "Float" (and dragging a tab out of the window) needs a host window; without a locator the panel moved into
        // a window model that was never shown, and so it vanished.
        // Every floating window is checked once shown (after Dock has placed it): when it is not fully on screen it is
        // moved to the center of the primary monitor.
        DefaultHostWindowLocator = () =>
        {
            HostWindow host = new();
            host.Opened += (_, _) => Dispatcher.UIThread.Post(() => WindowPlacement.EnsureVisible(host), DispatcherPriority.Background);
            return host;
        };

        // Panel operations for the debug log (dragging, docking, auto-hide, showing/hiding, floating windows).
        DockableHidden += (_, e) => DebugLog.Write("Panel", $"hidden {NameOf(e.Dockable)}");
        DockableRestored += (_, e) => DebugLog.Write("Panel", $"shown {NameOf(e.Dockable)}");
        DockablePinned += (_, e) => DebugLog.Write("Panel", $"auto-hide {NameOf(e.Dockable)}");
        DockableUnpinned += (_, e) => DebugLog.Write("Panel", $"docked back from auto-hide {NameOf(e.Dockable)}");
        DockableMoved += (_, e) => DebugLog.Write("Panel", $"moved {NameOf(e.Dockable)}");
        DockableDocked += (_, e) => DebugLog.Write("Panel", $"docked {NameOf(e.Dockable)} ({e.Operation})");
        DockableUndocked += (_, e) => DebugLog.Write("Panel", $"undocked {NameOf(e.Dockable)} ({e.Operation})");
        WindowOpened += (_, e) => DebugLog.Write("Panel", $"floating window opened: {PanelsOf(e.Window)}");
        WindowClosed += (_, e) => DebugLog.Write("Panel", $"floating window closed: {PanelsOf(e.Window)}");
        WindowMoveDragEnd += (_, e) => DebugLog.Write("Panel", $"floating window moved: {PanelsOf(e.Window)}");
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

    /// <summary>Value in the visibility snapshot for a panel collapsed by "Auto Hide" (pinned to the edge).</summary>
    public const string PinnedState = "pinned";

    /// <inheritdoc />
    public override IRootDock CreateLayout()
    {
        BookBrowserTool bookBrowser = new() { ViewModel = _bookBrowser };
        ClipsTool clips = new() { ViewModel = Clips };
        PreviewTool preview = new() { ViewModel = _preview };
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

        IToolDock leftDock = CreateToolDock();
        leftDock.Id = "LeftDock";
        leftDock.Alignment = Alignment.Left;
        leftDock.Proportion = 0.18;
        leftDock.VisibleDockables = CreateList<IDockable>(bookBrowser, clips);
        leftDock.ActiveDockable = bookBrowser;

        IToolDock rightDock = CreateToolDock();
        rightDock.Id = "RightDock";
        rightDock.Alignment = Alignment.Right;
        rightDock.Proportion = 0.26;
        rightDock.VisibleDockables = CreateList<IDockable>(preview, toc, checkpoints);
        rightDock.ActiveDockable = preview;

        IToolDock bottomDock = CreateToolDock();
        bottomDock.Id = "BottomDock";
        bottomDock.Alignment = Alignment.Bottom;
        bottomDock.Proportion = 0.28;
        bottomDock.VisibleDockables = CreateList<IDockable>(validation, notifications, findUsages);
        bottomDock.ActiveDockable = validation;

        _regionDocks["LeftDock"] = leftDock;
        _regionDocks["RightDock"] = rightDock;
        _regionDocks["BottomDock"] = bottomDock;
        foreach (IToolDock region in _regionDocks.Values)
        {
            // The last panel of a region can be closed too (it is only hidden, see CloseDockable).
            region.CanCloseLastDockable = true;
            foreach (Tool tool in region.VisibleDockables!.OfType<Tool>())
            {
                _homeDocks[tool.Id] = region;
            }
        }

        IDocumentDock documentDock = CreateDocumentDock();
        documentDock.Id = DockableIds.Documents;
        documentDock.Title = Strings.Get("Panel_Documents");
        documentDock.CanCreateDocument = false;
        // The tab area must keep its height even when empty — otherwise it
        // collapses to zero and the bottom panel (Validation Results) takes the whole middle.
        documentDock.Proportion = 0.72;
        documentDock.IsCollapsable = false;
        documentDock.VisibleDockables = CreateList<IDockable>();
        DocumentDock = documentDock;

        IProportionalDock centerColumn = CreateProportionalDock();
        centerColumn.Orientation = Orientation.Vertical;
        centerColumn.VisibleDockables = CreateList<IDockable>(
            documentDock,
            CreateProportionalDockSplitter(),
            bottomDock);

        IProportionalDock mainLayout = CreateProportionalDock();
        mainLayout.Orientation = Orientation.Horizontal;
        mainLayout.VisibleDockables = CreateList<IDockable>(
            leftDock,
            CreateProportionalDockSplitter(),
            centerColumn,
            CreateProportionalDockSplitter(),
            rightDock);

        IRootDock root = CreateRootDock();
        root.Id = "Root";
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
        root.ActiveDockable = mainLayout;
        root.DefaultDockable = mainLayout;
        // "Auto Hide": a slid-out panel takes up space in the layout instead of overlaying the content.
        // The overlay does not work with the preview — the native WebView control is always drawn above
        // Avalonia elements, so it covered the slid-out panel.
        root.PinnedDockDisplayMode = PinnedDockDisplayMode.Inline;

        _root = root;
        return root;
    }

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
                tool.Title = _attentionCounts.TryGetValue(tool.Id, out int count) && count > 0
                    ? $"{title} ({count})"
                    : title;
            }
        }

        if (DocumentDock is not null)
        {
            DocumentDock.Title = Strings.Get("Panel_Documents");
        }
    }

    /// <summary>Whether the panel is collapsed by "Auto Hide" (pinned to the window edge).</summary>
    public bool IsToolPinned(string id) =>
        _root is not null && _tools.TryGetValue(id, out Tool? tool) && IsDockablePinned(tool, _root);

    /// <summary>
    /// Snapshot of panel visibility (id → <c>"1"</c> visible, <c>"0"</c> hidden,
    /// <see cref="PinnedState"/> collapsed by "Auto Hide") — for persistence.
    /// </summary>
    public IReadOnlyDictionary<string, string> CaptureToolVisibility()
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (string id in _tools.Keys)
        {
            result[id] = _hidden.Contains(id) ? "0" : IsToolPinned(id) ? PinnedState : "1";
        }

        return result;
    }

    /// <summary>
    /// Applies the stored panel visibility (including "Auto Hide"). Panels from
    /// <see cref="AlwaysHiddenOnStart"/> stay hidden regardless of the stored state.
    /// </summary>
    public void ApplyToolVisibility(IReadOnlyDictionary<string, string> stored)
    {
        foreach (KeyValuePair<string, Tool> pair in _tools)
        {
            if (!stored.TryGetValue(pair.Key, out string? v))
            {
                continue;
            }

            bool pinned = v == PinnedState;
            bool want = (v == "1" || pinned) && !AlwaysHiddenOnStart.Contains(pair.Key);
            bool have = !_hidden.Contains(pair.Key);
            if (want && !have)
            {
                RestoreDockable(pair.Value);
                _hidden.Remove(pair.Key);
            }
            else if (!want && have)
            {
                HideDockable(pair.Value);
                _hidden.Add(pair.Key);
            }

            if (want && pinned && !IsToolPinned(pair.Key))
            {
                PinDockable(pair.Value);
            }
        }
    }

    /// <summary>
    /// Snapshot of the slid-out sizes of "Auto Hide" panels (id → <c>"width;height"</c> in DIP, invariant
    /// culture) — only the panels whose slid-out size is known (Dock records it in
    /// <see cref="IDockable.PinnedBounds"/> when the user resizes the slid-out panel). For persistence.
    /// </summary>
    public IReadOnlyDictionary<string, string> CapturePinnedSizes()
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, Tool> pair in _tools)
        {
            pair.Value.GetPinnedBounds(out _, out _, out double width, out double height);
            if (IsUsablePinnedSize(width) && IsUsablePinnedSize(height))
            {
                result[pair.Key] = string.Create(CultureInfo.InvariantCulture, $"{width};{height}");
            }
        }

        return result;
    }

    /// <summary>
    /// Restores the slid-out sizes of "Auto Hide" panels saved by <see cref="CapturePinnedSizes"/>.
    /// Without it a slid-out panel opens at Dock's minimum width (50 px) after every restart.
    /// Unreadable or degenerate entries are skipped.
    /// </summary>
    public void ApplyPinnedSizes(IReadOnlyDictionary<string, string> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        foreach (KeyValuePair<string, string> pair in stored)
        {
            string[] parts = pair.Value.Split(';');
            if (_tools.TryGetValue(pair.Key, out Tool? tool)
                && parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double width)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double height)
                && IsUsablePinnedSize(width)
                && IsUsablePinnedSize(height))
            {
                tool.SetPinnedBounds(0, 0, width, height);
            }
        }
    }

    // Dock's own minimum of a slid-out panel is 50 px; anything above a sane screen size is garbage.
    private static bool IsUsablePinnedSize(double size) =>
        double.IsFinite(size) && size >= 50 && size <= 10_000;

    /// <summary>Toggles the panel's visibility; returns the new visibility state.</summary>
    public bool ToggleTool(string id)
    {
        if (!_tools.TryGetValue(id, out Tool? tool))
        {
            return false;
        }

        if (_hidden.Contains(id))
        {
            RestoreDockable(tool);
            _hidden.Remove(id);
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
        // A floating panel is docked back first (its window closes), so showing it again docks it in its region.
        DockBackIfFloating(tool);
        HideDockable(tool);
        _hidden.Add(tool.Id);
    }

    /// <summary>
    /// Snapshot of the current assignment of panels to regions, their order, the region's active tab
    /// and the proportions — to be persisted in <c>SettingsStore.MainWindowDockLayout</c>
    /// via <c>DockSerializer.SystemTextJson</c>.
    /// </summary>
    public DockLayoutState CaptureLayoutState()
    {
        List<DockRegionState> regions = new();
        foreach (KeyValuePair<string, IToolDock> pair in _regionDocks)
        {
            List<string> toolIds = pair.Value.VisibleDockables is null
                ? new List<string>()
                : pair.Value.VisibleDockables.OfType<Tool>().Select(t => t.Id).ToList();
            // Floating panels belong to the region they were taken from.
            toolIds.AddRange(_tools.Values
                .Where(t => IsFloating(t) && ReferenceEquals(_homeDocks.GetValueOrDefault(t.Id), pair.Value))
                .Select(t => t.Id));
            string? activeId = (pair.Value.ActiveDockable as IDockable)?.Id;
            regions.Add(new DockRegionState(pair.Key, pair.Value.Proportion, toolIds, activeId));
        }

        double documentsProportion = DocumentDock?.Proportion ?? 0;
        return new DockLayoutState(regions, documentsProportion);
    }

    /// <summary>
    /// Applies a previously persisted layout snapshot: moves panels between
    /// regions, restores the proportions and the region's active tab. Called AFTER
    /// <see cref="CreateLayout"/>/<see cref="InitLayout"/>, BEFORE restoring panel
    /// visibility (a panel hidden in the meantime is no longer in the source region's
    /// <c>VisibleDockables</c>, so the call order matters).
    /// </summary>
    public void ApplyLayoutState(DockLayoutState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (DocumentDock is not null && state.DocumentsProportion > 0)
        {
            DocumentDock.Proportion = Math.Max(state.DocumentsProportion, MinRestoredProportion);
        }

        foreach (DockRegionState region in state.Regions)
        {
            if (!_regionDocks.TryGetValue(region.DockId, out IToolDock? targetDock))
            {
                continue;
            }

            if (region.Proportion > 0)
            {
                targetDock.Proportion = Math.Max(region.Proportion, MinRestoredProportion);
            }

            foreach (string toolId in region.ToolIds)
            {
                if (!_tools.TryGetValue(toolId, out Tool? tool))
                {
                    continue;
                }

                IToolDock? sourceDock = FindOwnerDock(tool);
                if (sourceDock is not null && !ReferenceEquals(sourceDock, targetDock))
                {
                    MoveDockable(sourceDock, targetDock, tool, null);
                }
            }

            if (region.ActiveToolId is not null
                && _tools.TryGetValue(region.ActiveToolId, out Tool? activeTool))
            {
                SetActiveDockable(activeTool);
            }
        }
    }

    /// <summary>
    /// Restores from the persisted snapshot ONLY the widths of the side regions (LeftDock —
    /// Book Browser, RightDock — Preview). Unlike <see cref="ApplyLayoutState"/>
    /// it does not move panels and does not touch the vertical proportions (document area / bottom panel) —
    /// those degenerated the layout when the bottom region was saved as collapsed (documents
    /// proportion ≈ 1). Each width is clamped and their sum is limited so that the middle
    /// column with the tabs always has room.
    /// </summary>
    public void ApplySideRegionWidths(DockLayoutState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        double left = RestorableWidth(state, "LeftDock");
        double right = RestorableWidth(state, "RightDock");
        double sum = (double.IsNaN(left) ? 0 : left) + (double.IsNaN(right) ? 0 : right);
        if (sum > MaxRestoredSidesSum)
        {
            double scale = MaxRestoredSidesSum / sum;
            left *= scale;
            right *= scale;
        }

        if (!double.IsNaN(left))
        {
            _regionDocks["LeftDock"].Proportion = left;
        }

        if (!double.IsNaN(right))
        {
            _regionDocks["RightDock"].Proportion = right;
        }
    }

    /// <summary>Upper bound of a single side region's width when restoring.</summary>
    private const double MaxRestoredSideProportion = 0.6;

    /// <summary>Upper bound of the sum of both side regions' widths when restoring.</summary>
    private const double MaxRestoredSidesSum = 0.8;

    // NaN = no (sensible) saved value — the region keeps its default width.
    private static double RestorableWidth(DockLayoutState state, string dockId)
    {
        DockRegionState? region = state.Regions?.FirstOrDefault(r => r.DockId == dockId);
        if (region is null || double.IsNaN(region.Proportion) || region.Proportion <= 0)
        {
            return double.NaN;
        }

        return Math.Clamp(region.Proportion, MinRestoredProportion, MaxRestoredSideProportion);
    }

    /// <inheritdoc />
    public override void FloatDockable(IDockable dockable)
    {
        DebugLog.Write("Panel", $"float {NameOf(dockable)}");
        RememberHomes(dockable);
        base.FloatDockable(dockable);
    }

    /// <inheritdoc />
    public override void FloatDockable(IDockable dockable, DockWindowOptions? options)
    {
        RememberHomes(dockable);
        base.FloatDockable(dockable, options);
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
        RememberHomes(dockable);
        base.SplitToWindow(dock, dockable, x, y, width, height);
    }

    /// <inheritdoc />
    public override void SplitToWindow(IDock dock, IDockable dockable, double x, double y, double width, double height, DockWindowOptions? options)
    {
        RememberHomes(dockable);
        base.SplitToWindow(dock, dockable, x, y, width, height, options);
    }

    /// <summary>
    /// "Dock" in a panel's menu: Dock only uses it to undo "Auto Hide", so for a floating panel it did nothing; here it
    /// docks the panel back into the region it was taken from (and closes the emptied window).
    /// </summary>
    public override void PinDockable(IDockable dockable)
    {
        if (dockable is Tool tool && IsFloating(tool))
        {
            DockBackIfFloating(tool);
            return;
        }

        base.PinDockable(dockable);
    }

    /// <summary>
    /// Closing a floating window docks its panels back into the regions they were taken from; otherwise they would
    /// be closed together with the window and vanish.
    /// </summary>
    public override bool OnWindowClosing(IDockWindow? window)
    {
        bool close = base.OnWindowClosing(window);
        if (close && window?.Layout is { } layout)
        {
            foreach (Tool tool in ToolsIn(layout).ToList())
            {
                DockHome(tool);
            }
        }

        return close;
    }

    /// <summary>
    /// The three regions (left, right, bottom) are never collapsed out of the layout: when their last panel is floated
    /// Dock would remove the emptied region, and the panel could not be docked back into it. An empty region stays in
    /// the layout like one whose panels are all hidden.
    /// </summary>
    public override void CollapseDock(IDock dock)
    {
        if (dock is IToolDock toolDock && _regionDocks.ContainsValue(toolDock))
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
        foreach (IDockWindow window in _root?.Windows?.ToList() ?? new List<IDockWindow>())
        {
            window.Exit();
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
        var groups = stored
            .Select(pair => (Tool: _tools.GetValueOrDefault(pair.Key), Bounds: ParseFloatingBounds(pair.Value)))
            .Where(x => x.Tool is not null && x.Bounds is not null && !_hidden.Contains(x.Tool.Id))
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

            window.X = x;
            window.Y = y;
            window.Width = width;
            window.Height = height;
            window.Host?.SetPosition(x, y);
            window.Host?.SetSize(width, height);
            if (window.Host is Avalonia.Controls.Window host)
            {
                Dispatcher.UIThread.Post(() => WindowPlacement.EnsureVisible(host), DispatcherPriority.Background);
            }
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
        !_hidden.Contains(tool.Id) && FindOwnerDock(tool) is null && !IsToolPinned(tool.Id) && tool.Owner is not null;

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
            return;
        }

        MoveDockable(source, home, tool, null);
        SetActiveDockable(tool);
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

    /// <summary>Makes the panel visible (if hidden) and activates it — for the "Focus on…" actions.</summary>
    public void FocusTool(string id)
    {
        if (!_tools.TryGetValue(id, out Tool? tool))
        {
            return;
        }

        if (_hidden.Remove(id))
        {
            RestoreDockable(tool);
        }

        SetActiveDockable(tool);
        if (_root is not null)
        {
            SetFocusedDockable(_root, tool);
        }
    }

    /// <summary>
    /// Makes a hidden panel visible but collapsed ("Auto Hide" — only its tab at the window edge),
    /// so it does not take space from the editor. A panel that is already visible is left as it is.
    /// </summary>
    public void ShowToolCollapsed(string id)
    {
        if (!_tools.TryGetValue(id, out Tool? tool) || !_hidden.Remove(id))
        {
            return;
        }

        RestoreDockable(tool);
        if (!IsToolPinned(id))
        {
            PinDockable(tool);
        }
    }

    /// <summary>
    /// Shows the panel expanded: makes it visible (if hidden), slides it out when it is collapsed
    /// by "Auto Hide" and activates it.
    /// </summary>
    public void ExpandTool(string id)
    {
        if (!_tools.TryGetValue(id, out Tool? tool))
        {
            return;
        }

        if (IsToolPinned(id))
        {
            PreviewPinnedDockable(tool);
            SetActiveDockable(tool);
            return;
        }

        FocusTool(id);
    }

    /// <summary>
    /// Sets the attention state of a panel tab: <paramref name="count"/> &gt; 0 appends "(count)" to
    /// the title and draws the tab with the warning color (<see cref="SignetTool.NeedsAttention"/>).
    /// </summary>
    public void SetToolAttention(string id, int count)
    {
        if (!_tools.TryGetValue(id, out Tool? tool))
        {
            return;
        }

        _attentionCounts[id] = count;
        string title = Strings.TryGet("Panel_" + id) ?? tool.Title ?? id;
        tool.Title = count > 0 ? $"{title} ({count})" : title;
        if (tool is SignetTool signetTool)
        {
            signetTool.NeedsAttention = count > 0;
        }
    }
}
