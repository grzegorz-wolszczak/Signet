# Flaky main window UI tests — investigation notes

Written in November 2026, when the investigation was paused to go on with other work. Everything a later agent needs
to pick it up: what fails, how to make it fail, what was measured, what is ruled out, what is still open.

## The symptom

`tests/Signet.App.MainWindowUiTests` fails now and then in a **full run of the project** (`dotnet test
tests/Signet.App.MainWindowUiTests`), roughly **once in 5–25 runs**. A different test case fails each time. Never seen
when only a subset of the project runs (e.g. `--filter "FullyQualifiedName~PanelCloseReopenTests"`, 6+ runs clean).

Failures seen (all with the code at commit `5148515` plus the tab strip style of `ToolTabStripEdgeTests`, which only
changes borders — see below why it is not a suspect):

| Test | Case | Message |
|---|---|---|
| `PanelCloseReopenTests.A_closed_panel_comes_back_visible` | `Notifications`, `FloatingViewToggle`, `StripeButton` | (first sighting, message not kept) |
| same | `Clips`, `DockedPanelClose`, `ViewToggle` | `Expected IsShown(vm, main, panel) to be True because the docked panel's content is on screen, but found False.` |
| same | `Clips`, `DockedPanelClose`, `Focus` | same |
| same | `TableOfContents`, `DockedPanelClose`, `Focus` | same (in the same run as the one above) |
| same | `Preview`, `FloatingPanelClose`, `Focus` | `... the floating panel's content is on screen, but found False.` |
| same | `ValidationResults`, `DockedPanelClose`, `ViewToggle` | docked, same |
| same | `Checkpoints`, `DockedPanelClose`, `ViewToggle` | docked, same |
| same | `FindUsages`, `DockedPanelClose`, `StripeButton` | **different**: `[Test Case Cleanup Failure ...]: System.InvalidOperationException : The calling thread cannot access this object because a different thread owns it.` at `Avalonia.Threading.Dispatcher.VerifyAccess` — the rest of the stack was cut off by the filter of the loop and not kept |
| `WindowRenderTests.Opening_a_resource_renders_a_code_editor_tab_in_the_document_dock` | — | message not kept |

So the main one is `IsShown(...) == false` after a panel was closed and shown again, in both the docked and the
floating variants, for any panel, with any way of closing and showing it.

## How to make it fail

There is no deterministic repro yet. A loop of full runs that keeps the whole output of the first failure:

```bash
S=/tmp/flaky; mkdir -p $S; for i in $(seq 1 20); do
  out=$(dotnet test tests/Signet.App.MainWindowUiTests --no-build 2>&1)
  if echo "$out" | grep -q "Failed!"; then echo "$out" > $S/failure.txt; echo "failed at run $i"; break; fi
done
```

One full run takes about a minute. Keep the **whole** output (an earlier loop kept only three lines after "Error
Message" and lost the stack of the cleanup failure).

## The test

`A_closed_panel_comes_back_visible(id, close, show)` (`tests/Signet.App.MainWindowUiTests/PanelCloseReopenTests.cs`):
a new `MainWindowViewModel` and `MainWindow` (1200×800) are shown; the panel is shown if hidden; optionally floated;
closed (`CloseDockable` / closing its window / `ToggleTool`); shown again (`ToggleTool` / `FocusTool` / the stripe
button); then `Settle` (`RunJobs` → `CaptureRenderedFrame` → `RunJobs`) of the main window and of every floating host;
then `IsShown` = some `ContentControl`/`ContentPresenter` whose `DataContext` is the panel is effectively visible with a
non-empty size, in the main window or a floating host.

Note in passing: the test never disposes its `MainWindowViewModel` (the rule of AGENTS.md says it must) — not the cause
as far as is known, but worth fixing together.

## What was measured (temporary diagnostics, since removed)

The `because` of the failing assertion was replaced by a `Diag(...)` string. The code, to put back when continuing (it
goes into `PanelCloseReopenTests`, used as `IsShown(vm, main, panel).Should().BeTrue("DIAG " + Diag(vm, main, panel))`
in both branches; `IsShown` is evaluated before `Diag`, so the extra `Settle` inside does not change the result):

```csharp
private static string Diag(MainWindowViewModel vm, MainWindow main, IDockable panel)
{
    var owner = panel.Owner as Dock.Model.Core.IDock;
    var parent = owner?.Owner as Dock.Model.Core.IDock;
    IEnumerable<TopLevel> windows = new TopLevel[] { main }
        .Concat(vm.Layout.Windows?.Select(w => w.Host).OfType<HostWindow>() ?? Enumerable.Empty<HostWindow>());
    IEnumerable<Control> all() => windows.SelectMany(w => w.GetVisualDescendants()).OfType<Control>();
    string Controls() => string.Join(" | ", all().Where(c => ReferenceEquals(c.DataContext, panel))
        .Select(c => $"{c.GetType().Name}#{c.Name} vis={c.IsVisible} eff={c.IsEffectivelyVisible} b={c.Bounds}"));
    string Group() => string.Join(" | ", all().OfType<Dock.Avalonia.Controls.ToolControl>()
        .Where(t => ReferenceEquals(t.DataContext, owner))
        .Select(t => $"ToolControl vis={t.IsVisible} eff={t.IsEffectivelyVisible} b={t.Bounds}"));
    string Model() => $"ownerEmpty={owner?.IsEmpty} ownerProp={owner?.Proportion} inParent={parent?.VisibleDockables?.Contains(owner!)} "
        + $"parent={parent?.Id} parentChildren={string.Join(",", parent?.VisibleDockables?.Select(d => d.Id + "/e=" + (d as Dock.Model.Core.IDock)?.IsEmpty + "/p=" + d.Proportion) ?? [])} "
        + $"parentViews={string.Join(",", all().Where(c => ReferenceEquals(c.DataContext, parent)).Select(c => c.GetType().Name))}";
    string Tree() => $"mainVisuals={main.GetVisualDescendants().Count()} "
        + $"docks={string.Join(",", main.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DockControl>().Select(d => $"vis={d.IsEffectivelyVisible} b={d.Bounds} layoutSame={ReferenceEquals(d.Layout, vm.Layout)} children={d.GetVisualDescendants().Count()}"))} "
        + $"toolControls={main.GetVisualDescendants().OfType<Dock.Avalonia.Controls.ToolControl>().Count()}";
    string first = $"TREE=[{Tree()}] MODEL=[{Model()}] group=[{Group()}] active={owner?.ActiveDockable?.Id} "
        + $"visible={string.Join(",", owner?.VisibleDockables?.Select(d => d.Id) ?? [])} controls=[{Controls()}]";
    foreach (TopLevel w in windows.ToList()) Settle(w);
    return first + $" || AFTER ANOTHER SETTLE shown={IsShown(vm, main, panel)} MODEL=[{Model()}] group=[{Group()}] controls=[{Controls()}]";
}
```

A log sink of Avalonia's warnings (`Avalonia.Logging.Logger.Sink`, `level >= Warning`) was also tried.

Results, in order:

1. `Clips`, docked: `owner=LeftTopDock active=Clips visibleDockables=BookBrowser,Clips controls=[]` — and the same
   **after another `Settle`**. The model says the panel is the active tab of its group; no control of the window has the
   panel as its `DataContext`, and one more settle does not bring it. So it is **not a matter of running the dispatcher
   once more** — the view is left in a wrong state.
2. `ValidationResults`, docked: `group=[]` — no `ToolControl` for the group (`BottomLeftDock`) exists at all.
   The log sink caught only the usual binding warnings (`DockControl` bindings to `Layout.*` while `Layout` is null at
   startup, `Action.IconState` of toolbar items) — no exception, nothing about templates.
3. `Checkpoints`, docked: model is right — `RightTopDock` is in `RightColumn`, `IsEmpty=False`, proportion 1, active is
   `Checkpoints`, its visible dockables are `Preview,TableOfContents,Checkpoints` — but **`parentViews=` is empty too**:
   no control has `RightColumn` (the proportional dock above the group) as its `DataContext` either. Together with 2,
   this points at **the whole layout view missing from the window**, not at one group failing to show a panel.
4. The `Tree()` measurement (are there any visuals in the main window at all, is there a `DockControl`, is its `Layout`
   the view model's `Layout`, how many `ToolControl`s) was written but **no failing run was caught with it** — the loop
   was stopped. This is the next thing to read.

## What is ruled out

- **The tab strip style** added for `ToolTabStripEdgeTests` (`dock|ToolTabStrip:singleitem` in `App.axaml`): it only
  hides two borders and draws one; it cannot remove views. The failures also hit floating panels and the document
  dock test.
- **Garbage collection of cached views**: forcing `GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();` between
  closing and showing the panel, for all 96 cases of the theory, did not make anything fail. Signet does not use Dock's
  control recycling (`Dock.Controls.Recycling`) either.
- **One more round of the dispatcher**: the extra `Settle` in `Diag` does not bring the view back (result 1).
- **Signet's own posted work**: `MainDockFactory` posts only `WindowPlacement.EnsureVisible` (floating windows); the
  main window posts the focus of the Find field. Nothing posted touches the layout views.
- **Order of `_splitHosts`** (`MainDockFactory.ShowSplittersBetweenShownParts`): a `Dictionary` filled once after
  `Clear()`, enumerated in insertion order — deterministic.

## What is still open

1. **Read the `Tree()` measurement of a failing run.** If the main window has (almost) no visuals, or its `DockControl`
   shows another `Layout` than `vm.Layout`, or none: the window under test is not the one showing the layout — then
   look at how the headless application, the window and the view model of the previous test interact (see 3).
2. **Get the whole stack of the cleanup failure** (`The calling thread cannot access this object because a different
   thread owns it`). It says something of a test touches Avalonia objects from another thread during xUnit's cleanup —
   possibly a `Dispose`/finalizer of something a previous test left behind. It may be the same root cause.
3. **Leftovers between tests.** Avalonia.Headless creates a new `App` per test (AGENTS.md, "PerTest isolation");
   this test and `PanelStripeTests.Host` never dispose their `MainWindowViewModel`. A view model of an earlier test still
   alive (timers, static events — see `TooltipFonts.Reset` in AGENTS.md for this kind of problem) could act on the
   dispatcher of a later test. Check what `MainWindowViewModel.Dispose` stops, dispose the view models in these tests,
   and see whether the failures go away.
4. **What is random at all.** The code under test is single-threaded and deterministic; the randomness has to come
   from time (timers, `DispatcherTimer`, `Task.Delay`, real-time render/animation clocks of the headless platform),
   from other threads (thread pool work finishing in a different order — compare the NativeWebView test fixed in
   `5148515`, where xUnit's pool thread was the cause), or from the garbage collector. Failures only in full runs point
   at load (other tests' leftovers, GC pressure, timing).
5. **`WindowRenderTests.Opening_a_resource_renders_a_code_editor_tab_in_the_document_dock`** failed once in the same
   kind of run; its message was not kept. Probably the same cause (a view of the layout not realized).

## Where things are

- The test: `tests/Signet.App.MainWindowUiTests/PanelCloseReopenTests.cs`.
- Showing / hiding panels: `src/Signet.App/Docking/MainDockFactory.cs` — `ToggleTool`, `FocusTool`, `HideTool`,
  `ShowHiddenTool` (`RestoreDockable`, `SortDock`, `InBatch`), `CloseDockable`, `CollapseDock` (places and their
  columns are never collapsed out of the layout), `GiveRoomToShownHalves`, `ShowSplittersBetweenShownParts` (splitters
  are removed and inserted in the proportional docks — the only place Signet edits the layout's collections itself).
- Dock.Avalonia 12.1.0.6; its Fluent theme is compiled (no XAML to read in the package).
