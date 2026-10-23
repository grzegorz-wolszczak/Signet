# Footnote benchmarks — baseline

What the footnote operations cost on a generated book of a real size. **Nothing in the benchmark run
fails over a number**: a timing is far too noisy for that — the same code measured 2145 ms and
3156 ms on this machine in one session. These numbers are here to be compared by eye when something
feels slower, and to be replaced when a change is meant to move them.

## How to run

```bash
SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release
```

Without `SIGNET_BENCHMARKS` every benchmark is skipped, so an ordinary `dotnet test` of the solution
does not pay for them. To collect the table into a file, add
`SIGNET_BENCHMARK_OUT=/path/to/report.md`.

Release matters: in Debug the numbers are several times worse and say nothing about the product.

## The two shapes, and why both

The books are written by `SyntheticBook` (in `Signet.Core.Tests/TestSupport`), the same for the same
arguments.

- **spread** — 1200 files, 6 notes each: what a real book of many short chapters looks like, each
  note in the file of its reference.
- **concentrated** — 200 files, 40 notes each, every note in one notes file: **every quadratic found
  in the footnote code so far needed this shape in order to show itself.** The 1409-file book that
  started the performance investigation was of this kind; a book of 1331 small files showed none of
  the same costs (see `FOOTNOTES_PERFORMANCE_NOTES.md`, sections 17 and 17a).

`foreign` means footnotes of other origin (what a conversion is given), `Signet` means Signet's own.
`Convert` is only measured on the `foreign` books, since there is nothing to convert in the others.

## Baseline

Measured on WSL2 (Linux 6.18, x64), .NET 10, Release, commit `d07d00d` plus the benchmark project.
Read-only operations are the best of three runs; the ones that change the book run once on a book of
their own.

### spread, Signet — 1200 files × 6 notes = 7200

| operation | ms |
|---|---:|
| FootnoteScanner.Scan | 41 |
| FootnoteValidator.Validate | 72 |
| LinkIntegrityValidator.Validate | 16 |
| PlanConversion | 135 |
| PlanPlacement | 159 |
| Renumber | 620 |

### spread, foreign — 1200 files × 6 notes = 7200

| operation | ms |
|---|---:|
| FootnoteScanner.Scan | 43 |
| FootnoteValidator.Validate | 81 |
| LinkIntegrityValidator.Validate | 16 |
| PlanConversion | 298 |
| PlanPlacement | 41 |
| Renumber | 275 |
| Convert | 1624 |

### concentrated, Signet — 200 files × 40 notes = 8000

| operation | ms |
|---|---:|
| FootnoteScanner.Scan | 85 |
| FootnoteValidator.Validate | 196 |
| LinkIntegrityValidator.Validate | 71 |
| PlanConversion | 219 |
| PlanPlacement | 140 |
| Renumber | 805 |

### concentrated, foreign — 200 files × 40 notes = 8000

| operation | ms |
|---|---:|
| FootnoteScanner.Scan | 57 |
| FootnoteValidator.Validate | 107 |
| LinkIntegrityValidator.Validate | 19 |
| PlanConversion | 353 |
| PlanPlacement | 43 |
| Renumber | 233 |
| Convert | 1588 |

## For scale: what these cost before the fixes of October 2026

On the real 1409-file book (9551 footnotes of Signet, concentrated), measured at commit `aaa990a`
and again after the fixes:

| operation | before | after |
|---|---:|---:|
| FootnoteScanner.Scan | 17 870 ms | 208 ms |
| FootnoteValidator.Validate | 34 604 ms | 253 ms |
| PlanConversion | 35 473 ms | 509 ms |
| Renumber | 40 731 ms | 1832 ms |

If a number in the tables above ever grows by an order of magnitude, that is the shape of regression
to look for: an operation that is linear in the size of one file, called once per footnote.
