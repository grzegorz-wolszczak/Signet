# Signet — whole-book operations, baseline

What the operations of Signet itself cost as a book grows (`SignetScaleBenchmarks`). As in the other benchmarks **nothing fails over a number**;
these are here to compare by eye. What is asserted is only that the operations ran.

## How to run

```bash
SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release --filter SignetScaleBenchmarks
SIGNET_BENCH_ONLY="Export" SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release --filter SignetScaleBenchmarks   # one operation
```

Release matters. The books are generated (`SyntheticBook`; 3 Signet notes per file). Each operation runs once on a small book first (the JIT is
warm), then is measured at two sizes, best of 2. A read-only operation shares one book between the runs; one that changes the book gets a freshly
opened book for each run (the opening is not part of the time).

## The two shapes, and how to read the table

- **many chapters**: 300 -> 1200 files of 20 paragraphs. The book grows by chapters; the work per chapter stays.
- **long chapters**: 100 files of 50 -> 400 paragraphs. The book grows by the length of its chapters; **every operation found slow so far
  showed itself only here** (Find and Replace were quadratic in the length of a file).

The size is counted in elements of the content (a heading, a paragraph, a note). The columns "time per element, large / small" and "allocation per
element, large / small" are the ratio of the cost of one element at the two sizes: **near 1 is an operation that grows with the book, a larger
one grows faster** (a ratio below 1 is a fixed cost shared out over more elements). Look for the ones above ~1.5.

## What the review found (November 2026, Windows 10, .NET 10, Release)

Everything is linear now in both shapes. The ones that were not, or were slow in absolute terms (best of 2, ms):

| operation | before | after | what was done |
|---|---:|---:|---|
| Find: count a word, 100 files of 400 paragraphs | 567 | 38 | `PcreRegex.Matches` once, not `Match(text, position)` per match (PCRE2 checks the whole text for valid UTF in every such call: a square) |
| Find: count a regex, the same | 1148 | 61 | the same |
| Replace in all files, the same | 930 | 36 | the same, and one pass into a `StringBuilder` instead of joining the whole text for every replacement |
| Save (export), 1200 files | 902 | 306 | the archive is written from the working folder, not from a copy of all of it (the copy stays only for obfuscated fonts) |
| Checkpoint (open + add), 1200 files | 900 | 551 | the files are copied several at a time |
| Open the EPUB, 1200 files | 545 | 275 | the entries are unpacked by several threads, and the HTML files read and checked by several |
| Open the EPUB, 100 files of 400 paragraphs | 76 | 30 | the same |

Not changed, on purpose: the checkpoint is a full copy of the folder (documented in `CheckpointHistory`: a hard link would be changed by a write in place
to the file that is live); Check Book is EPUBCheck (see `BASELINE-EPUBCHECK.md`) plus Signet's own checks, about a third of it (duplicate ids across
files with an AngleSharp document per file, `IdValidator`, `FootnoteValidator`).

The slowest per element now (1200 files, ms): Check Book 1307, Checkpoint 551, Mend & prettify all HTML 473, Report: links 330, Save 306, Open 275,
Classes: prepare rename 177, TOC: generate 160, Ids 150. None grows faster than the book.

## Tricks used and not used

Used: `PcreRegex.Matches` instead of a loop of `Match` (the biggest single gain, ~25x); `Parallel.For/ForEach` where files are independent
(unpacking, checking, copying a checkpoint), each with the deterministic parts (order of warnings, of progress, of errors, the choice
among entries of the same name) kept; not copying what is not changed (the export, `ParseAttribute` names compared as spans). Not used: `ArrayPool`,
structs, LINQ libraries: no measurement pointed at an allocation-bound hot spot in these operations.

## The start of the application (November 2026)

Measured with the start-up log (`StartupTimeline`: each phase is written once to `<prefs>/logs/signet-*.log` as `Startup: <phase> at +N ms`, the time since
the process started), on a small book given on the command line (30 files), an own data folder (`SIGNET_PREFS_DIR`), Windows 10, a Release build,
warm disk cache. The first start after the files are written is slower (cold disk, anti-virus: 4.5-6 s) and says nothing about the product.

| phase | JIT, ms | ReadyToRun, ms |
|---|---:|---:|
| main entered | 71 | 59 |
| services built | 169 | 120 |
| Avalonia initialized | 689 | 436 |
| language, theme and icons applied | 805 | 486 |
| main view model built | 1042 | 596 |
| main window constructed | 1318 | 694 |
| main window opened | 1636 | 839 |
| startup book loaded | 1979 | 1070 |
| first idle after the window opened | 2807 | 1464 |
| first Preview page loaded | 2976 | 1649 |

`dotnet publish -r <rid>` of `Signet.App` and of the `epubcheck.net` tool compiles the assemblies ahead of time now (`PublishReadyToRun`, set in the
projects only when a runtime identifier is given): the first Preview page after 1.65 s instead of 3.0 s; `epubcheck.net` on a small book 0.69 s instead of
1.2 s (4 MB more in the executable). A plain build is the JIT one, as before.

What is left of the start (ReadyToRun): Avalonia and the application resources 316 ms, the main view model 110 ms, the main window 100 ms, 145 ms to
show it, then 630 ms of the UI thread busy before the first idle moment. Reading where that goes needs a profile of a real start.

## The Code Editor (November 2026)

`CodeEditorScaleBenchmarks` (in `Signet.App.UiTests`, `SIGNET_BENCHMARKS=1`, Release) puts one XHTML file of 24 KB to 1.9 MB into a headless
`CodeTabView`. Opening it (first frame) is a constant ~60 ms, whatever the size. One edit with its redraw: 27-30 ms up to 500 KB, 45 ms at 1.9 MB. **About two thirds of
that is Avalonia's text layout** (`TextView.MeasureOverride`, `FontManager.TryMatchCharacter`), and under a headless platform that is dominated by the fonts of
the test environment: it says nothing certain about typing in the real window, which can only be measured there.

What is ours is the check of the structure the Code Editor makes at every change (`CodeTabViewModel.RefreshWellFormed` ->
`WellFormedChecker.CheckXhtmlStructure`: the XML parse of the whole text and the list of its tags), synchronously. Per check, by the size of the file:

| file | before, ms | after, ms |
|---|---:|---:|
| 97 KB | 3.2 | 2.9 |
| 485 KB | 18.1 | 15.5 |
| 1940 KB | 50.8 | 36.4 |

`TagLister` (the list of tags that many features share through `TagLister.For`) copied every piece of text between the tags into a string that was then thrown
away, and made the text of the path ("body -1,div 3,p 0") of every tag, though one tag at the caret needs it. It skips what is not a tag without copying and makes
the path when it is asked for. The result is the same for every tag of 2724 documents (the corpus and tricky strings), compared with the old implementation.

Left, and a decision that changes behaviour: the check runs on every keystroke, so a file of a megabyte or two costs ~35 ms of it per key. Running it a moment
after the last change (for large files) would remove that, and move the red mark of an error to that moment.

## The Preview

The page itself loads fast (the first page 170 ms after the first idle moment of the start). What the application does around it, 1200 files:

| | before | after |
|---|---:|---:|
| the first mirroring of the book into the Preview's folder (`PreviewMirror.Sync`) | 408 ms | 305 ms |
| a later sync of a book that did not change (every refresh of the live Preview does it) | ~20 ms | ~5 ms |
| a click on a link in the Preview: `FootnoteScanner.Scan(book)` | 70-150 ms | not changed |

`PreviewMirror.Sync` works out what is to be written one resource after the other, then writes the files (or finds them unchanged) on up to eight threads, with
the record of what was written in a `ConcurrentDictionary`. Left, and not changed: the scan of the whole book for footnotes at every click on a link.
