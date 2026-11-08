# AGENTS.md

## Project

**Signet**: desktop editor for EPUB 2/3 ebooks, C# + Avalonia. Areas: Code Editor, Preview, Find & Replace, TOC,
Metadata Editor, Reports, Spellcheck, Reformat/Clean, Check Book (EPUBCheck port + Signet checks), checkpoints.

## Key facts

- **Stack:** `net10.0`, Avalonia 12.x, `CommunityToolkit.Mvvm`, Dock.Avalonia.
- **Libraries:** AngleSharp, AvaloniaEdit + TextMateSharp, WeCantSpell.Hunspell, PCRE.NET, NativeWebView (Preview),
  SkiaSharp, DiffPlex, Serilog. Trees/tables use `Signet.Controls.TreeDataGrid` (our fork, see its `UPSTREAM.md`);
  the app has no `TreeView`/`DataGrid`.
- **Projects:** solution `Signet.slnx`; `Signet.Core` (domain, no UI), `Signet.App`, `Signet.Cli`,
  `Signet.Controls.TreeDataGrid`, `Signet.NativeWebView.Platform.Linux` (fork; assembly/namespace keep upstream name),
  `Signet.EpubCheck.Core` (EPUBCheck port, independent of `Signet.Core`), `Signet.EpubCheck.Cli`. Tests: one
  `tests/<Project>.Tests` per project, plus `Signet.App.UiTests`, `Signet.App.MainWindowUiTests`,
  `Signet.App.PreferencesUiTests`, `Signet.EpubCheck.Conformance.Tests`. Namespaces `Signet.*`.
- **EPUBCheck port:** plan/state in `EPUBCHECK_PLAN.md`; upstream origin and deviations in
  `src/Signet.EpubCheck.Core/UPSTREAM.md`. `BookValidator.ValidateCurrentBook` runs EPUBCheck on the in-memory book
  (`BookEpubSource`, `EpubCheckValidator`; result code = message id, e.g. `OPF-003`), then Signet's own validators
  (codes are `Validation_…` resource keys). Retire a Signet check only when a test in `EpubCheckCoverageTests` shows
  EPUBCheck reports the same situation. Panel fixes are bound to message ids in `EpubCheckFixes`.
- **License:** GPL v3 or later. Source files carry no license header.
- **Localization:** `.resx`; `Strings.resx`/`CoreStrings.resx` = Polish (default), `*.en.resx` = English. Every
  user-visible string goes through resources, in both languages.

## Ground rule — ask, don't assume

For every kind of task: if requirements are under-specified or have several reasonable interpretations, **stop and
ask before starting**. Don't guess intent or pick libraries/names/APIs/scope that were not agreed. Collect all doubts
and ask at once.

## Conventions

- C# `nullable enable`, `TreatWarningsAsErrors`, `ImplicitUsings disable`.
- Code comments, XML docs, examples (docs, tests, commit messages) in **English**; talk to the user in **Polish**.
- Central Package Management (`Directory.Packages.props`); all versions pinned.
- Reuse established domain names (Resource, FolderKeeper, OpfResource, BookPath, …); no synonyms.
- **Own unique identifiers: `Ulid`**, not `Guid`, unless a standard/external API dictates (e.g. `urn:uuid:`).

## Architecture gotchas

- **Dock deferred content is disabled.** Dock defers dockable content via the dispatcher, causing re-entrancy in
  `DataContextChanged` handlers. Tools derive from `SignetTool : Tool, IDeferredContentPresentation`
  (`src/Signet.App/Docking/DockTools.cs`), tabs from `ContentTabViewModel`. Keep this chain for new tool/tab types
  (`DeferredContentOptOutTests`).
- **TreeDataGrid sources belong to the views.** Sources are UI-thread bound; view models are tested off-thread. Views
  build source and columns in code-behind on `DataContext` change (`LocalizedColumns<T>`; cell templates as XAML
  resources) and dispose the previous one. Selection: `TreeSelectionSync<T>`/`TreeMultiSelectionSync<T>`; filtered
  trees: `VisibleItems.For`. Columns virtualize horizontally: no `Auto` column after a star column.
- **TreeDataGrid fork stays close to upstream:** analyzers off; every change listed in its `UPSTREAM.md`; its
  `Fluent.axaml` must come after `FluentTheme` in `App.axaml`.
- **Preview on Linux uses a forked NativeWebView backend** (changes in its `UPSTREAM.md`):
  - WebKitGTK runs on its own GTK thread; events are raised on the UI thread. An exception unwinding through native
    frames kills the process ("stack smashing detected").
  - Code called from native (P/Invoke callbacks, GObject signals) must catch all exceptions and keep its delegate in
    a field (else the GC collects it).
  - Never free a pointer/`GError` a signal hands to a handler; it belongs to the emitter.
  - Embedding under Avalonia's X11 host: show the GTK window only after reparenting, with the web view in it.
  - Diagnose native crashes with `coredumpctl` and `DOTNET_EnableCrashReport=1`, not by guessing.

## Running the GUI

Never launch or drive the desktop app (UI Automation, SendKeys, simulated input) without asking first. Headless tests
(`dotnet test`, `[AvaloniaFact]`) are fine.

## Commits

Conventional Commits (`type(scope): description`), type from `feat fix refactor test docs build chore perf ci`.
The body must be detailed: what changed and why, key decisions/trade-offs, impact on other modules.
A package version change = its own `build` commit.

## Unit tests

| Purpose | Library |
|---|---|
| Framework | xUnit.v3 `xunit.v3.mtp-off`, **pinned 3.2.2** (runner 3.1.5) |
| Assertions | AwesomeAssertions (never xUnit `Assert.*` for state) |
| Mocks | Moq |
| Values | AutoFixture (+ `AutoFixture.Xunit3`) |
| Avalonia views | `Avalonia.Headless.XUnit` (`[AvaloniaFact]`/`[AvaloniaTheory]`) |

- Every test has `// Arrange`, `// Act`, `// Assert` (repeat pairs for multi-step tests; `// Act & Assert` when one
  expression does both; no `// Arrange` if nothing to arrange). Applies to all test projects.
- Generate irrelevant values with AutoFixture.
- Unit tests: `tests/Signet.Core.Tests`, `tests/Signet.App.Tests`; sample EPUBs from `tests/corpus/`. Shared settings
  in `tests/Directory.Build.props`.
- `xunit.v3.mtp-off` keeps classic VSTest `dotnet test`; don't switch to `xunit.v3` without a `test` section in
  `global.json`. Don't bump xUnit without checking the headless projects (Avalonia.Headless.XUnit 12.1.x fails on 4.0.0).
- Headless UI tests are split into three projects only to run in parallel (main window + docking /
  Preferences / everything else); keep them similar in length.
- Avalonia.Headless creates a new `App` per test: static state walked by `App.Initialize` must reset (see
  `TooltipFonts.Reset`).
- Render tests: after `window.Show()` call `Dispatcher.UIThread.RunJobs()` + `window.CaptureRenderedFrame()`.
  Tab logic without UI goes in `TabManagerModelTests`/`TabManagerTests`.

### A case found goes into a corpus book

- A case the tests missed becomes a valid EPUB in `tests/corpus/broken/` (comment: when and why; see its README), with
  the behaviour asserted in the matching test. If the behaviour is wrong and unfixed, assert the wrong behaviour and
  say so. `tests/corpus/malformed/` = books broken as EPUBs.
- `tests/corpus/real/` = markup of real books with prose replaced by Lorem ipsum (`EpubAnonymizer`), faults kept.
  Tests record what the book comes with and hold the operation to adding nothing.
- Scale is generated (`SyntheticBook`), not stored.
- Timings are never asserted. `tests/Signet.Benchmarks` runs only with `SIGNET_BENCHMARKS=1`; numbers go to
  `BASELINE*.md`.

### Tests leave nothing behind

- Every test deletes what it made. After writing/changing tests, run them and make sure the guard passes.
- Temp files only via `TempDir` (`Signet.Core.Tests.TestSupport`), never hand-built `Path.GetTempPath()` paths.
  Dispose owners of files: `Book`, `MainWindowViewModel`, `PreferencesViewModel`, `SpellCheckSetup`
  (`SpellCheckTestFactory.New()`), `TestHost`. Use `using` or a `Disposables` field (`_disposables.Add(...)`,
  `_disposables.DeleteFile(path)`).
- Headless UI tests: constructor/`Dispose` don't run on the UI thread; close windows inside the test and dispose the
  main window's view model explicitly (closing with a book open asks and cancels).
- Each test project calls `TestIsolation.Install` from `TestIsolationSetup.cs` (new projects need their own): the
  process gets `%TEMP%/Signet.TestRuns/<project>-<guid>/` with `temp` as temp path and `prefs` as app data folder
  (`SIGNET_PREFS_DIR`). `TempCleanupGuard` fails the run if `temp` isn't empty; leftovers show with
  `dotnet test --logger "console;verbosity=detailed"`.

## Build settings

- Root `Directory.Build.props`: `net10.0`, nullable, `ImplicitUsings disable`, `TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`, `AnalysisLevel latest-recommended`; `CS1591` suppressed.
- **Fix warnings last.** `IDExxxx` style rules are warnings (`IdeWarnings.props`, severity in `.editorconfig`). Fix
  all warnings only at the very end, right before the final full test run.
- **Line endings:** `* text=auto` (LF in git, platform endings on disk); only `*.sh` LF and `*.cmd`/`*.bat` CRLF.
  Byte-stable: `tests/corpus/`, `src/Signet.Core/EmbeddedData/` (LF).
- **Version:** `<VersionPrefix>-<yyyyMMddHHmm UTC>` (`version/Versioning.targets`, only in `Signet.App.csproj`);
  at runtime `Signet.App.Infrastructure.AppVersion.Text`.
