using System.Collections.Generic;
using System.Linq;
using System;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm;
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
/// <see cref="MainDockFactory.CaptureToolVisibility"/>) or floating windows (the factory does not create them).
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
    private readonly Dictionary<string, IToolDock> _regionDocks = new(StringComparer.Ordinal);
    private readonly BookBrowserViewModel? _bookBrowser;
    private readonly PreviewViewModel? _preview;
    private IRootDock? _root;

    /// <summary>Creates the factory; the optional Book Browser and Preview panel view models are embedded in the panels.</summary>
    public MainDockFactory(BookBrowserViewModel? bookBrowser = null, PreviewViewModel? preview = null)
    {
        _bookBrowser = bookBrowser;
        _preview = preview;
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
    };

    /// <summary>
    /// Panels that are always hidden at startup, regardless of the state saved on exit —
    /// validation results from the previous session would be empty anyway; the panel only shows after F7 /
    /// the View menu.
    /// </summary>
    private static readonly HashSet<string> AlwaysHiddenOnStart = new(StringComparer.Ordinal)
    {
        DockableIds.ValidationResults,
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

        foreach (Tool tool in new Tool[]
                 {
                     bookBrowser, clips, preview, toc, validation, checkpoints,
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
        bottomDock.VisibleDockables = CreateList<IDockable>(validation);
        bottomDock.ActiveDockable = validation;

        _regionDocks["LeftDock"] = leftDock;
        _regionDocks["RightDock"] = rightDock;
        _regionDocks["BottomDock"] = bottomDock;

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
                tool.Title = title;
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

        HideDockable(tool);
        _hidden.Add(id);
        return false;
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
}
