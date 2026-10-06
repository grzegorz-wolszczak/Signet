# Signet.Controls.TreeDataGrid — upstream

This project is a fork of the open-source **Avalonia TreeDataGrid** control.

| | |
|---|---|
| Upstream repository | <https://github.com/AvaloniaUI/Avalonia.Controls.TreeDataGrid> (archived, read-only) |
| Upstream commit | [`914572f49ac4b51a2e1ea037a86ff3a494385a5f`](https://github.com/AvaloniaUI/Avalonia.Controls.TreeDataGrid/tree/914572f49ac4b51a2e1ea037a86ff3a494385a5f) (2025-10-13, the last commit, version 11.x for Avalonia 11) |
| License | MIT, Copyright (c) .NET Foundation and Contributors — see [`LICENSE.md`](LICENSE.md) |

The upstream README states that the archived code "will remain accessible for reference and forking under its current
license". Later versions of the control (11.2 and up, including 12.x) are a commercial product (Avalonia Accelerate);
**no code from those versions is used here**. This fork is based exclusively on the MIT-licensed source above.

The MIT license is compatible with Signet's GPL v3 or later: the files of this project stay under MIT (the license
notice must be kept), Signet as a whole is distributed under the GPL.

## What was imported

- `src/Avalonia.Controls.TreeDataGrid` → `src/Signet.Controls.TreeDataGrid` (sources, themes),
- `tests/Avalonia.Controls.TreeDataGrid.Tests` → `tests/Signet.Controls.TreeDataGrid.Tests`,
- `docs` → `src/Signet.Controls.TreeDataGrid/docs` (usage documentation, for reference).

The first commit of this fork imports these files verbatim; every change after it is a regular commit in Signet's
history.

## Changes against upstream

- **Names.** Namespaces renamed from `Avalonia.Controls.*` / `Avalonia.Experimental.Data.*` /
  `Avalonia.Data.Core.Parsers` to `Signet.Controls.TreeDataGrid.*` (`Models.TreeDataGrid` and `Models` merged into
  `Models`; the parser and the typed bindings under `Experimental.Data.*`), the assembly to
  `Signet.Controls.TreeDataGrid`. `GlobalUsings.cs` imports the Avalonia namespaces the code used to see implicitly.
  Like upstream, the control is registered in the default Avalonia XAML namespace (`XmlnsDefinition`), so XAML uses
  `<TreeDataGrid>` without a prefix; the theme is `avares://Signet.Controls.TreeDataGrid/Themes/Fluent.axaml`.
- **Avalonia 12 / .NET 10:**
  - `OnLostFocus` takes `FocusChangedEventArgs` (cells).
  - `InputElement.OnDoubleTapped` is now a virtual class handler: `TreeDataGridCell.OnDoubleTapped` overrides it
    instead of registering its own class handler for a method of the same name.
  - `Avalonia.Utilities.MathUtilities` is internal: a local copy of the three comparisons used
    (`Utils/MathUtilities.cs`).
  - `TopLevel.PlatformSettings` → `VisualExtensions.GetPlatformSettings` (tap size, command modifier).
  - Drag & drop: `DataObject` / `DoDragDrop` / `DragEventArgs.Data` → `DataTransfer` / `DoDragDropAsync` /
    `DragEventArgs.DataTransfer`; `DragInfo.DataFormat` is a typed in-process `DataFormat<DragInfo>`;
    `DoDragDropAsync` needs the pointer-pressed event, so `TreeDataGridRow` keeps it until the drag starts.
  - `NullableAttributes.cs` (a polyfill for old target frameworks) removed.
- **Signet additions** (small API changes for Signet's views):
  - `TreeDataGridCell.BeginEdit()` is public (upstream: `protected internal`), so a command can start editing a
    cell, not only an edit gesture.
- **Fixes:**
  - `TreeDataGridExpanderCell`: when the row refuses to expand (its children are empty), the expander toggle
    (bound two-way) is put back in step with the row; upstream left it checked, inverting later clicks.
- **Bugs that later versions list as fixed.** Their public release notes (NuGet, 11.2-12.3; their code was not looked
  at) were checked against this fork with tests (`Port/UpstreamReleaseNotesTests.cs`, which also keeps the
  regression tests of the notes this fork never had). Fixed here:
  - `TreeDataGridRow`: in row selection mode, bringing a row or a cell into view (keyboard navigation, a focused cell)
    keeps the horizontal scroll offset; upstream the focused cell was brought into view, so arrow keys in a
    horizontally scrolled grid jumped back to the first column (12.2.0). Tab still scrolls to the focused cell.
  - `TreeDataGridRowSelectionModel`:
    - PageUp / PageDown look only at the vertical extent of the rows: with rows wider than the viewport no row was
      "fully visible" and PageUp jumped to the first row (11.3.2);
    - Left to the parent / Right to the first child mark the key as handled, and Right moves the focus to the child
      too (12.0.3);
    - a press-move-release selects the row only if the pointer stayed within 3 px in both directions (upstream: in
      either one, so a vertical drag over a multiple selection collapsed it) (12.0.3).
  - `TreeDataGridCell.CancelEdit` shows the model's value again: a text cell kept the cancelled text (12.1.0).
  - `TreeSelectionNode`: replacing an item drops the selection inside it (its selected descendants stayed selected)
    (12.0.3).
  - `RealizedStackElements.ItemsReplaced`: a replaced range that starts above the first realized row and reaches into
    the realized ones recycles those rows (upstream skipped the range, leaving stale rows) (12.3.1).
- **Build.** Signet's stricter analyzer rules (`EnableNETAnalyzers`, `EnforceCodeStyleInBuild`, `AnalysisLevel`,
  XML documentation file) are off for this project and its tests; compiler warnings are still errors.
- **Tests.** The upstream tests run on Signet's test stack (xUnit.v3 `mtp-off`, `Avalonia.Headless.XUnit`).
