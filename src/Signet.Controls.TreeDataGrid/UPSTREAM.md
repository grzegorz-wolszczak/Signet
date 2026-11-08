# Signet.Controls.TreeDataGrid — upstream

Fork of the open-source **Avalonia TreeDataGrid**.

| | |
|---|---|
| Upstream | <https://github.com/AvaloniaUI/Avalonia.Controls.TreeDataGrid> (archived), commit [`914572f`](https://github.com/AvaloniaUI/Avalonia.Controls.TreeDataGrid/tree/914572f49ac4b51a2e1ea037a86ff3a494385a5f) (2025-10-13, 11.x for Avalonia 11) |
| License | MIT, © .NET Foundation and Contributors — see [`LICENSE.md`](LICENSE.md); keep the notice (Signet as a whole is GPL) |

Later versions (11.2+, 12.x) are commercial; **no code from them is used**. The fork is based only on the MIT source above.

## Imported
`src/Avalonia.Controls.TreeDataGrid` → here; its tests → `tests/Signet.Controls.TreeDataGrid.Tests`; `docs` → `docs/`.
Verbatim in the first commit; later changes are regular commits.

## Changes
- **Names:** namespaces → `Signet.Controls.TreeDataGrid.*`, assembly `Signet.Controls.TreeDataGrid`;
  `GlobalUsings.cs` for Avalonia namespaces. Still registered in the default XAML namespace (`<TreeDataGrid>`); theme
  `avares://Signet.Controls.TreeDataGrid/Themes/Fluent.axaml`.
- **Avalonia 12 / .NET 10:** `OnLostFocus(FocusChangedEventArgs)`; `TreeDataGridCell.OnDoubleTapped` overrides the
  virtual handler; local `Utils/MathUtilities.cs` (Avalonia's is internal); `GetPlatformSettings` for tap size/modifier;
  drag & drop via `DataTransfer`/`DoDragDropAsync`/typed `DataFormat<DragInfo>` (`TreeDataGridRow` keeps the pressed
  event); `NullableAttributes.cs` removed.
- **Signet additions:**
  - `TreeDataGridCell.BeginEdit()` is public.
  - `TreeDataGridExpanderCell.IndentSize` (default 20); the cell computes `IndentPadding` itself, adding the vertical
    `Padding`.
  - `TreeDataGrid.KeepEstimatedScrollExtentStable` (default `false`): keeps the first row-size estimate so the scroll
    thumb doesn't change size while scrolling rows of different heights (`UseStableEstimatedScrollExtent`,
    `RealizedStackElements` `useExistingEstimate`).
- **Fixes:** `TreeDataGridExpanderCell` re-syncs the toggle when a row refuses to expand.
- **Bugs later versions list as fixed** (checked from public release notes only, with tests in
  `Port/UpstreamReleaseNotesTests.cs`):
  - Row mode keeps horizontal scroll when bringing a row/cell into view (12.2.0); Tab still scrolls to the focused cell.
  - `TreeDataGridRowSelectionModel`: PageUp/PageDown use only vertical extent (11.3.2); Left/Right key handling and
    Right moves focus to the child (12.0.3); press-move-release selects only if the pointer stayed within 3 px on both
    axes (12.0.3).
  - `TreeDataGridCell.CancelEdit` restores the model value (12.1.0).
  - `TreeSelectionNode`: replacing an item drops selection inside it (12.0.3).
  - `RealizedStackElements.ItemsReplaced`: a range starting above the first realized row recycles stale rows (12.3.1).
- **Build:** Signet's analyzers/XML docs are off for this project and its tests; compiler warnings are still errors.
- **Tests:** run on xUnit.v3 `mtp-off` + `Avalonia.Headless.XUnit`; `TextCellTests.StringFormat` sets the culture
  explicitly; `TestIsolationSetup.cs` (+ linked `TestIsolation.cs`) as in every Signet test project.
