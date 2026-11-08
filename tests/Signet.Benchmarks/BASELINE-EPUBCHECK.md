# EPUBCheck port — baseline

Cost of the EPUBCheck port. **No number is asserted**; compare by eye (see `BASELINE.md`).

```bash
SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release --filter SchemaLoadBenchmarks
SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release --filter EpubCheckScaleBenchmarks
```

## Schema loading (`SchemaLoadBenchmarks`, `XmlValidators`; WSL2, .NET 10, Release, task 2.6)
Each schema is read when first needed and kept for the process. **first use** includes JIT and (for `*-30.sch`) the XSLT
preprocessing; **warm** = re-read from scratch after everything is compiled (best of 3).

| schema | first use, ms | warm, ms |
|---|---:|---:|
| epub-nav-30.rnc (first to read the XHTML/SVG/MathML models) | 208 | 60 |
| epub-svg-30.rnc | 48 | 50 |
| epub-svg-30.nvdl / -informative.nvdl | 53 / 55 | 43 / 57 |
| epub-xhtml-30.rnc / .nvdl | 42 / 53 | 49 / 43 |
| ocf-container-30.rnc, ocf-encryption-30.sch | 38, 39 | 2, 0 |
| ops20-svg.nvdl, ops20.nvdl | 22, 12 | 9, 10 |
| everything else (≤ 22 ms first use each) | | |
| **all 50** | **661** | **338** |

## Whole run by book size (`EpubCheckScaleBenchmarks`)
Generated books, best of 2 after warm-up; the packaged EPUB is what `epubcheck.net` checks, the in-memory book what
Check Book checks. The Java tool (EPUBCheck 5.4.0, JDK 21, whole process) for comparison: 1600 files 6.8 s vs 5.4 s
here (at task 6.3); 8000 notes in 200 files 10.0 s vs 5.0 s.

| book | task 6.3 before → after XPath fix, ms | end of 6.4, ms |
|---|---:|---:|
| 50 files | 78 → 72 | 7 |
| 200 files | 363 → 308 | 30 |
| 800 files | 2119 → 1356 | 275 |
| 1600 files | 7183 → 3209 | 673 |
| 600 files × 6 notes | — | 287 |
| 1200 files × 6 notes (spread) | 8834 → 6507 | 738 |
| 200 files × 40 notes in one file (8000) | 37 392 → 1651 | 556 |

Task 6.3 fix (algorithmic): Schematron's `count($id-set[@id = current()/@id]) = 1` compared every id with every id. The
XPath engine now knows what an expression depends on, keeps the nodes of `//name` per document and indexes a predicate
`[A = B]` over a list it sees twice (`XPathExpressions.cs`).

## Performance review (task 6.4) — 1600 files: 3261 → 673 ms, 3588 → ~420 KiB per file; 1200 files with notes: 6671 → 738 ms
The run is linear in file count (5.3 ms per file at every size); steps in order of gain:
1. **RELAX NG memo kept** (one `ValidatorPatternBuilder` per schema and thread instead of per document; see
   `UPSTREAM.md`): a quarter of all allocation was the memo.
2. **XPath engine allocations:** one regex per (pattern, flags); no closure allocated per `Call`; `EvaluateBoolean`
   without lists; shared boolean lists; direct string value in `GeneralCompare`; loops instead of LINQ in axis steps;
   one reused context per loop (`Rebind`); `MemoStore` no longer allocated by every `With`.
3. **`VocabUtil.ParseProperties`** (no `Regex.Split`/LINQ) and **`XsltPattern`** (no capturing closures).
4. **Allocation stacks** (`dotnet-trace collect --providers Microsoft-Windows-DotNETRuntime:0x1:5`, `GCAllocationTick`
   read with TraceEvent): `Url.Relativize` per element in `XmlHandler.Location()`, double URL parses, list doubling in
   `AxisStep.Evaluate`, LINQ axis walks, double `Relativize` per reference.
5. **Time, not allocations:** inclusive shares of a sampled profile (Schematron `Visit` 49%, XPath `PathExpr` 36%, URL
   parser 7% — so a `Span` rewrite of the parser wasn't worth it); a stopwatch showed 54.7 M rule matches (3.4 s) →
   rules are tried only against elements they name; 277 000 `DatatypeException`s per run → 1.
   - `package-30.sch`'s itemref duplicate assertion is quadratic in the spine **by upstream's design** (57 µs per
     itemref at 1600 items); left alone.

`EpubCheckScaleBenchmarks` prints time, KiB allocated per file, collections and exceptions thrown per run;
`SIGNET_ALLOC_PROFILE=1` adds allocated types (`AllocationProfile`, the runtime's `GCAllocationTick` read in process).

Reading profiles here: `dotnet-trace --profile dotnet-sampled-thread-time` counts idle threads (most "UNMANAGED_CODE_TIME");
top self-time frames (`RuntimeHelpers.GetHashCode`, `Thread.PollGC`) are safepoint bias. Decide by experiment: a 256 MB
gen0 budget (`DOTNET_GCgen0size=0x10000000`) changed nothing (GC isn't the main cost); `DOTNET_TieredPGO=0` makes
everything 20–35% slower (keep dynamic PGO on). What remains is spread thin: strings (23%), the interpreter's
`List<object>`/`object[]`, `char[]`/`StringBuilder` (element string values, XML reader text).
