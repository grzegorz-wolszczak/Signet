# Footnote benchmarks — baseline

Cost of the footnote operations on generated books. **No number is asserted** (timings are too noisy: 2145 vs 3156 ms
for the same code in one session); compare by eye, replace when a change is meant to move them.

```bash
SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release        # skipped without the variable
SIGNET_BENCHMARK_OUT=/path/report.md                                       # also write the table
```
Release only (Debug is several times worse).

## Shapes (`SyntheticBook`)
- **spread** — 1200 files × 6 notes, each note in its reference's file.
- **concentrated** — 200 files × 40 notes, all in one notes file. **Every quadratic found so far needed this shape**
  (the 1409-file book that started the investigation was of this kind; a book of 1331 small files showed none of the
  same costs).

`foreign` = footnotes of other origin; `Signet` = Signet's own. `Convert` is measured only on `foreign`.

## Baseline
WSL2 (Linux 6.18, x64), .NET 10, Release, commit `d07d00d` + the benchmark project. Read-only: best of 3; mutating:
one run on a fresh book. Times in ms.

| operation | spread, Signet (7200) | spread, foreign (7200) | concentrated, Signet (8000) | concentrated, foreign (8000) |
|---|---:|---:|---:|---:|
| FootnoteScanner.Scan | 41 | 43 | 85 | 57 |
| FootnoteValidator.Validate | 72 | 81 | 196 | 107 |
| LinkIntegrityValidator.Validate | 16 | 16 | 71 | 19 |
| PlanConversion | 135 | 298 | 219 | 353 |
| PlanPlacement | 159 | 41 | 140 | 43 |
| Renumber | 620 | 275 | 805 | 233 |
| Convert | — | 1624 | — | 1588 |

Real 1409-file book (9551 Signet notes), before → after the October 2026 fixes: Scan 17 870 → 208, Validate 34 604 →
253, PlanConversion 35 473 → 509, Renumber 40 731 → 1832 ms. An order-of-magnitude jump in a table above means an
operation linear in one file's size called once per footnote.
