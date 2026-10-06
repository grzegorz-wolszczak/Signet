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
- **Build.** Signet's stricter analyzer rules (`EnableNETAnalyzers`, `EnforceCodeStyleInBuild`, `AnalysisLevel`,
  XML documentation file) are off for this project and its tests; compiler warnings are still errors.
- **Tests.** The upstream tests run on Signet's test stack (xUnit.v3 `mtp-off`, `Avalonia.Headless.XUnit`).
