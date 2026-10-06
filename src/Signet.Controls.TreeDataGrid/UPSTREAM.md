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

- Ported to Avalonia 12 / .NET 10.
- Namespaces renamed from `Avalonia.Controls.*` to `Signet.Controls.TreeDataGrid.*` (and the assembly to
  `Signet.Controls.TreeDataGrid`), so the fork cannot be confused with or collide with the official package.
- Signet's stricter analyzer rules are relaxed for this project only (the code keeps its upstream style).
- Tests ported to Signet's test stack (xUnit.v3, AwesomeAssertions).
