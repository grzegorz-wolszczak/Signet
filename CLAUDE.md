# CLAUDE.md

## What this project is

**Signet** is a desktop editor for EPUB 2 and EPUB 3 ebooks, written in **C# + Avalonia**.
Main areas: code editing (Code View), live Preview, Find & Replace, Table of Contents
generation/editing, Metadata Editor, Reports, Spellcheck, Reformat/Clean, well-formedness
validation, checkpoints (snapshot + diff).

## Key facts

- **Stack:** `net10.0`, Avalonia 12.x, `CommunityToolkit.Mvvm`, Dock.Avalonia (docking layout).
- **Libraries:** `AngleSharp` (HTML5/CSS parsing), `AvaloniaEdit` + `TextMateSharp` (code editor),
  `WeCantSpell.Hunspell` (spelling), `PCRE.NET` (regex in Find & Replace), `NativeWebView`
  (Preview), `SkiaSharp` (images/fonts), `DiffPlex` (diffs), Serilog (logging). Trees and tables use
  `Signet.Controls.TreeDataGrid` — Signet's fork of the MIT Avalonia TreeDataGrid (see its `UPSTREAM.md`);
  there is no `TreeView` / `DataGrid` in the app.
- **Naming:** solution `Signet.slnx`; projects `Signet.Core` (domain model, no UI dependencies),
  `Signet.App` (Avalonia app), `Signet.Cli` (headless CLI), `Signet.Controls.TreeDataGrid` (the
  TreeDataGrid fork); tests `Signet.Core.Tests`, `Signet.App.Tests`, `Signet.App.UiTests`,
  `Signet.Controls.TreeDataGrid.Tests`; namespaces `Signet.*`.
- **License:** GNU GPL v3 or later (see `LICENSE`). Source files carry **no** license header.
- **UI localization:** `.resx` resources — `Strings.resx` / `CoreStrings.resx` hold Polish (default),
  `*.en.resx` hold English. Every user-visible string goes through resources, in both languages.

## Ground rule — ask, don't assume

Applies to **every kind of task** (code, planning, research, refactoring, configuration, anything).

If you have any doubt, under-specified requirements, unclear assumptions, or see several reasonable
interpretations — **stop and ask the user before assuming anything and starting work**. Do not
"pick a sensible default and move on", do not guess intent, do not choose libraries/names/APIs/scope
on your own when they have not been agreed.

Better one question too many than an implementation built on a wrong assumption.
When there are several doubts, collect them and ask all at once, not one by one.

## Conventions

- C# `nullable enable`, `TreatWarningsAsErrors`, `ImplicitUsings disable`.
- Code comments and XML docs are written in **English**.
- Examples (in docs, README, comments, tests, commit messages) are always written in **English**,
  even when the conversation with the user is in Polish.
- Central Package Management (`Directory.Packages.props`) — all package versions pinned.
- Domain names in the code (Resource, FolderKeeper, OpfResource, BookPath, …) are established —
  reuse them, do not introduce synonyms.
- **Identifiers: ULID by default.** Wherever we generate our own unique identifier and its format is
  **not** dictated by the EPUB standard or by an external library/API, use `Ulid` (package `Ulid`),
  not `Guid`/UUID. `Guid`/UUID only where required (e.g. `urn:uuid:` in OPF metadata, `dc:identifier`).
- Language of communication with the user: **Polish**.

## Architecture gotchas

- **Dock.Avalonia deferred content is disabled.** Dock 12.1.0.6 defers materializing dockable
  content through the dispatcher queue, which caused re-entrancy in views' `DataContextChanged`
  handlers when docks were re-parented (e.g. a `NullReferenceException` in `CodeTabView` before the
  `TextEditor.Document` binding resolved). Dock has no global switch for this; the only lever is
  `IDeferredContentPresentation.DeferContentPresentation => false` on the content object. Therefore:
  tools derive from `SignetTool : Tool, IDeferredContentPresentation`
  (`src/Signet.App/Docking/DockTools.cs`) and document tabs from `ContentTabViewModel`. Any new
  tool/tab type must keep that inheritance chain (covered by `DeferredContentOptOutTests`).
- **TreeDataGrid sources belong to the views.** A `FlatTreeDataGridSource` / `HierarchicalTreeDataGridSource`
  is bound to the UI thread (its selection and items views call `Dispatcher.VerifyAccess`), while view models
  are unit-tested off the UI thread. Views build the source and its columns in code-behind when the
  `DataContext` changes (`LocalizedColumns<T>` for headers that follow the UI language, cell templates as
  XAML resources by key), and dispose the previous one. Selection helpers: `TreeSelectionSync<T>` /
  `TreeMultiSelectionSync<T>`; filtered trees use `VisibleItems.For` (rows cannot be hidden). Columns are
  virtualized horizontally too: do not put an `Auto` column after a star column (it never gets realized).
- **The TreeDataGrid fork stays close to upstream.** It keeps its upstream style (analyzers off for the
  project); every change against upstream is listed in `src/Signet.Controls.TreeDataGrid/UPSTREAM.md`.
  The fork's `Fluent.axaml` must come after `FluentTheme` in `App.axaml` (it resolves Fluent colours with
  `StaticResource`).

## Running the GUI

Never launch the desktop application or drive it (UI Automation, SendKeys, simulated mouse/keyboard)
without asking the user first — it takes over their desktop. Headless tests (`dotnet test`,
`[AvaloniaFact]`) are fine without asking.

## Commits

Every commit made by Claude in this project must be a **Conventional Commit**
(`type(scope): description`, e.g. `feat(core): add OPF parser`), with a type from:
`feat`, `fix`, `refactor`, `test`, `docs`, `build`, `chore`, `perf`, `ci`.

The commit body must be **detailed** — not a single sentence. Describe:
- what was changed and why,
- key design decisions / trade-offs,
- impact on other modules, if any.

## Unit tests

Fixed technology set for all test projects:

| Purpose | Library |
|---|---|
| Test framework | **xUnit.v3**, variant **`xunit.v3.mtp-off`** (without Microsoft.Testing.Platform), **pinned to 3.2.2** |
| Assertions | **AwesomeAssertions** (`value.Should().Be(...)` style) |
| Mocks / stubs | **Moq** |
| Anonymous / random values | **AutoFixture** (+ `AutoFixture.Xunit3` for `[Theory]` with `[AutoData]` / `[InlineAutoData]`) |
| Avalonia view tests (UI thread) | **`Avalonia.Headless.XUnit`** — `[AvaloniaFact]` / `[AvaloniaTheory]`, project `tests/Signet.App.UiTests` |

Rules:
- Do not use xUnit `Assert.*` for state assertions — always AwesomeAssertions.
- Generate values irrelevant to the tested behavior with AutoFixture; do not hard-code them.
- Unit tests live in `tests/Signet.Core.Tests` and `tests/Signet.App.Tests`; tests that use sample
  EPUBs read them from `tests/corpus/`.
- Shared test-project settings (only MSBuild properties and analyzer suppressions — package
  references go per project) are in `tests/Directory.Build.props`.
- We use `xunit.v3.mtp-off` (not `xunit.v3`) so test projects stay plain libraries and `dotnet test`
  works classically (VSTest + `xunit.runner.visualstudio`), without Microsoft.Testing.Platform and
  without an opt-in in `global.json`. Do not switch to `xunit.v3` without adding a `test` section
  to `global.json`.
- **xUnit.v3 is pinned to `3.2.2`** (runner `3.1.5`). Reason: the official `Avalonia.Headless.XUnit`
  (12.1.x) is built against xUnit.v3 3.2.x and fails during discovery on 4.0.0
  (`MissingMethodException`). Do not bump xUnit without checking that `Signet.App.UiTests` still runs.
- View render tests (`[AvaloniaFact]`) in `Signet.App.UiTests`: after `window.Show()` force a layout
  pass with `Dispatcher.UIThread.RunJobs()` + `window.CaptureRenderedFrame()`.
  **Adding a document tab to `DocumentDock` hangs under headless** (Dock.Avalonia) — test tab logic
  without UI (`TabManagerModelTests` / `TabManagerTests`).
- `Catastrophic failure: Call from invalid thread` lines printed during a full `dotnet test` run are
  known noise from the headless UI tests; they do not fail the run.

## Build settings

- `Directory.Build.props` (root): `TargetFramework net10.0`, `Nullable enable`,
  `ImplicitUsings disable`, `TreatWarningsAsErrors true`, `EnforceCodeStyleInBuild true`,
  `AnalysisLevel latest-recommended`. `CS1591` (missing XML doc) is suppressed globally — public API
  documentation is enforced by review.
- `Directory.Packages.props` — Central Package Management. A package version change = a separate
  `build` commit.
- **Versioning:** `<VersionPrefix>-<yyyyMMddHHmm UTC>`, e.g. `0.4.0-202610021432`
  (`version/Versioning.targets`, imported only by `Signet.App.csproj`; `VersionPrefix` in
  `Directory.Build.props`). At runtime: `Signet.App.Infrastructure.AppVersion.Text`.
