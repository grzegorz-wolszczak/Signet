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
