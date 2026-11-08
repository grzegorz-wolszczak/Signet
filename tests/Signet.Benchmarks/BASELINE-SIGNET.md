# Signet — whole-book operations, baseline

Cost of Signet's operations as a book grows (`SignetScaleBenchmarks`). **No number is asserted**, only that the
operations ran; compare by eye.

```bash
SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release --filter SignetScaleBenchmarks
SIGNET_BENCH_ONLY="Export" SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release --filter SignetScaleBenchmarks
```
Release only. Books come from `SyntheticBook` (3 Signet notes per file). Each operation runs once on a small book (JIT
warm-up), then at two sizes, best of 2; read-only operations share a book, mutating ones get a freshly opened one.

## Shapes
- **many chapters:** 300 → 1200 files of 20 paragraphs.
- **long chapters:** 100 files of 50 → 400 paragraphs. **Every slow operation found so far showed itself only here**
  (Find/Replace were quadratic in file length).

Size = elements of content. "time/allocation per element, large/small" ratios: near 1 = grows with the book; above
~1.5 = look closer; below 1 = fixed cost shared out.

## Review of November 2026 (Windows 10, .NET 10, Release) — everything is linear now
| operation | before → after (ms) | what was done |
|---|---|---|
| Find: count a word, 100 files × 400 paragraphs | 567 → 38 | `PcreRegex.Matches` once, not `Match(text, pos)` per match (PCRE2 validates the whole UTF text per call) |
| Find: count a regex, same | 1148 → 61 | same |
| Replace in all files, same | 930 → 36 | same + one pass into a `StringBuilder` |
| Save (export), 1200 files | 902 → 306 | archive written from the working folder (copy only for obfuscated fonts) |
| Checkpoint (open + add), 1200 files | 900 → 551 | files copied several at a time |
| Open EPUB, 1200 files | 545 → 275 | parallel unpack, parallel HTML read/check |
| Open EPUB, 100 × 400 paragraphs | 76 → 30 | same |

Unchanged on purpose: the checkpoint is a full folder copy (a hard link would follow in-place writes — see
`CheckpointHistory`); Check Book = EPUBCheck (`BASELINE-EPUBCHECK.md`) + Signet checks (~a third: cross-file duplicate
ids, `IdValidator`, `FootnoteValidator`).

Slowest per element (1200 files, ms): Check Book 1307, Checkpoint 551, Mend & prettify all HTML 473, Report: links 330,
Save 306, Open 275, Classes: prepare rename 177, TOC: generate 160, Ids 150.

Tricks used: `PcreRegex.Matches`; `Parallel.For/ForEach` for independent files (deterministic order of warnings,
progress, errors kept); not copying unchanged data; `ParseAttribute` names as spans. Not used: `ArrayPool`, structs,
LINQ libraries (no measured allocation hot spot).

## Application start (November 2026)
`StartupTimeline` writes `Startup: <phase> at +N ms` to `<prefs>/logs/signet-*.log`. Small book (30 files), own
`SIGNET_PREFS_DIR`, Win10, Release, warm cache (a cold first start takes 4.5–6 s and means nothing).

| phase | JIT ms | ReadyToRun ms |
|---|---:|---:|
| main entered | 71 | 59 |
| services built | 169 | 120 |
| Avalonia initialized | 689 | 436 |
| language, theme, icons applied | 805 | 486 |
| main view model built | 1042 | 596 |
| main window constructed | 1318 | 694 |
| main window opened | 1636 | 839 |
| startup book loaded | 1979 | 1070 |
| first idle | 2807 | 1464 |
| first Preview page loaded | 2976 | 1649 |

`dotnet publish -r <rid>` of `Signet.App` and `epubcheck.net` uses ReadyToRun (set only when a RID is given):
Preview page 1.65 s vs 3.0 s; `epubcheck.net` on a small book 0.69 s vs 1.2 s (+4 MB). Plain builds stay JIT. Left
(R2R): Avalonia + resources 316 ms, main view model 110, window 100, 145 to show it, then 630 ms busy before first
idle — needs a real-start profile.

## Code Editor (November 2026)
`CodeEditorScaleBenchmarks` (`Signet.App.UiTests`) opens a 24 KB–1.9 MB XHTML in a headless `CodeTabView`: first
frame ~60 ms at any size; one edit + redraw 27–30 ms up to 500 KB, 45 ms at 1.9 MB — about two thirds is Avalonia text
layout, dominated by headless fonts, so not a measure of real typing.

Ours is the per-change structure check (`CodeTabViewModel.RefreshWellFormed` → `WellFormedChecker.CheckXhtmlStructure`,
synchronous): 97 KB 3.2 → 2.9 ms; 485 KB 18.1 → 15.5; 1940 KB 50.8 → 36.4. `TagLister` now skips non-tags without
copying and builds tag paths on demand (identical output on 2724 documents).

Open decision: the check runs on every keystroke (~35 ms per key at 1–2 MB). Debouncing it for large files would remove
that but delay the red error mark.

## Preview
First page loads ~170 ms after first idle. 1200 files: first mirror (`PreviewMirror.Sync`) 408 → 305 ms; re-sync of an
unchanged book ~20 → ~5 ms (files written/compared on up to eight threads, record in a `ConcurrentDictionary`). A click
on a Preview link runs `FootnoteScanner.Scan(book)`: 70–150 ms, unchanged.

## Mend & prettify all HTML (why not faster)
Per file: AngleSharp parse for links (`XhtmlUsesWhiteSpace` → `GetPathsToLinkedResources`, cached), structure guard,
`CleanSource.PrettyPrint` (second parse), serialization; 0.4 ms for a small chapter, 7 ms for 60 KB; linear.
Tried, **not kept:**
- Computing new texts on several threads: Mend of 1200 small files 218 → 146 ms (2 threads), but 100 × 400 paragraphs
  302 → 456–486 ms, and Mend & prettify no faster at 2/4/8 threads (32 logical CPUs). 47% of the time in
  `Monitor.Enter_Slowpath` under AngleSharp's `StringBuilderPool` (one locked static pool): serialization doesn't
  scale (parsing does, ~4× on 8).
- Skipping the `style`-attribute loop: saves the loop, not the parse.

What would help: reading `<link rel=stylesheet>` from text instead of AngleSharp — differs in entities, repeated
attributes and name case, so not done without a guarantee of identical results.
