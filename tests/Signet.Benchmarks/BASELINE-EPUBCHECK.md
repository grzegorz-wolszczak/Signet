# EPUBCheck schema loading — baseline

What reading EPUBCheck's schemas costs (`SchemaLoadBenchmarks`, `Signet.EpubCheck.Core.Xml.XmlValidators`). As with
the footnote benchmarks (`BASELINE.md`), **nothing fails over a number**; these are here to compare by eye.

## How to run

```bash
SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release --filter SchemaLoadBenchmarks
```

## What the columns mean

- **first use** — the schema read the first time the process asks for it, in upstream's order. This is what a check
  pays once: each schema is read when first needed and kept for the life of the process (upstream's enum reads all of
  them when it is first touched). The first schemas also pay for the JIT compiling the parsers, and a `*-30.sch` file
  for the XSLT preprocessing of upstream's build (`iso-sch-include.xsl`, `iso-sch-abstract.xsl`) and for compiling the
  two stylesheets once.
- **warm** — the same schema read again from scratch once everything is compiled (best of 3): what the schema costs by
  itself. The preprocessed `*-30.sch` text is kept, so it is not paid again here.

The large ones are the XHTML and SVG content models (Nu HTML Checker's HTML5 schemas, SVG, MathML): `epub-nav-30.rnc`
is the first to read them, the NVDL schemas read them again for themselves.

## Baseline

Measured on WSL2 (Linux 6.18, x64), .NET 10, Release, at task 2.6 of `EPUBCHECK_PLAN.md`.

| schema | first use, ms | warm, ms |
|---|---:|---:|
| schema/20/rng/container.rng | 21 | 0 |
| schema/30/ocf-container-30.rnc | 38 | 2 |
| schema/30/multiple-renditions/container.sch | 22 | 0 |
| schema/20/rng/dtbook-2005-2.rng | 4 | 3 |
| schema/20/rng/encryption.rng | 2 | 0 |
| schema/30/ocf-encryption-30.rnc | 7 | 3 |
| schema/30/ocf-encryption-30.sch | 39 | 0 |
| schema/20/sch/id-unique.sch | 0 | 0 |
| schema/30/ocf-metadata-30.rnc | 2 | 1 |
| schema/30/ocf-metadata-30.sch | 5 | 0 |
| schema/30/edupub/edu-ocf-metadata.sch | 0 | 0 |
| schema/30/media-overlay-30.rnc | 1 | 1 |
| schema/30/media-overlay-30.sch | 0 | 0 |
| schema/30/epub-nav-30.rnc | 208 | 60 |
| schema/30/epub-nav-30.sch | 4 | 0 |
| schema/20/rng/ncx.rng | 0 | 0 |
| schema/20/sch/ncx.sch | 0 | 0 |
| schema/20/rng/opf.rng | 1 | 1 |
| schema/20/sch/opf.sch | 0 | 0 |
| schema/30/package-30.rnc | 1 | 1 |
| schema/30/package-30.sch | 7 | 2 |
| schema/30/dict/dict-collection.sch | 0 | 0 |
| schema/30/collection-do-30.sch | 0 | 0 |
| schema/30/idx/idx-collection.sch | 0 | 0 |
| schema/30/collection-manifest-30.sch | 0 | 0 |
| schema/30/previews/preview-collection.sch | 0 | 0 |
| schema/30/dict/dict-opf.sch | 0 | 0 |
| schema/30/edupub/edu-opf.sch | 0 | 0 |
| schema/30/previews/preview-pub-opf.sch | 0 | 0 |
| schema/30/multiple-renditions/mapping.rnc | 0 | 0 |
| schema/30/multiple-renditions/mapping.sch | 0 | 0 |
| schema/30/dict/search-key-map.rnc | 0 | 0 |
| schema/20/rng/signatures.rng | 0 | 0 |
| schema/30/ocf-signatures-30.rnc | 1 | 1 |
| schema/20/rng/ops20-svg.nvdl | 22 | 9 |
| schema/30/epub-svg-30.rnc | 48 | 50 |
| schema/30/epub-svg-30.nvdl | 53 | 43 |
| schema/30/epub-svg-30-informative.nvdl | 55 | 57 |
| schema/30/epub-svg-30.sch | 0 | 0 |
| schema/20/rng/ops20.nvdl | 12 | 10 |
| schema/20/sch/xhtml.sch | 0 | 0 |
| schema/30/epub-xhtml-30.sch | 11 | 1 |
| schema/30/epub-xhtml-30.rnc | 42 | 49 |
| schema/30/epub-xhtml-30.nvdl | 53 | 43 |
| schema/30/edupub/edu-structure.sch | 1 | 0 |
| schema/30/edupub/edu-semantics.sch | 0 | 0 |
| schema/30/datanav/datanav-xhtml.sch | 0 | 0 |
| schema/30/dict/dict-xhtml.sch | 0 | 0 |
| schema/30/idx/idx-xhtml.sch | 1 | 1 |
| schema/30/idx/idx-xhtml-index.sch | 0 | 0 |
| **all 50** | **661** | **338** |


## A whole run, by the size of the book

`EpubCheckScaleBenchmarks` (generated books, best of 2 after a warm-up run; the packaged EPUB is what `epubcheck.net` checks, the
book in memory is what Check Book checks). Measured on Windows 10, .NET 10, Release, at task 6.3 of `EPUBCHECK_PLAN.md`.

```bash
SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release --filter EpubCheckScaleBenchmarks
```

| book | before the XPath fix, ms | after, ms |
|---|---:|---:|
| 50 files | 78 | 72 |
| 200 files | 363 | 308 |
| 800 files | 2119 | 1356 |
| 1600 files | 7183 | 3209 |
| 300 files x 6 notes (spread) | - | 1612 |
| 600 files x 6 notes (spread) | - | 3169 |
| 1200 files x 6 notes (spread) | 8834 | 6507 |
| 200 files x 40 notes in one file (8000 notes) | **37392** | **1651** |

The fix was algorithmic: Schematron's `count($id-set[@id = current()/@id]) = 1` (and its twin on `//@id`) compared every id with
every id. The XPath engine now knows what an expression depends on, keeps the nodes a `//name` selects in the document, and
indexes a predicate `[A = B]` over a list it sees twice (`XPathExpressions.cs`).

Against the Java tool (EPUBCheck 5.4.0 on JDK 21, whole process including start-up, on the same two generated books written
to disk): 1600 files 6.8 s Java, 5.4 s here; 8000 notes in 200 files 10.0 s Java, 5.0 s here.

### Phase 2 start (task 6.4): is anything still super-linear?

The three "spread" books (300, 600, 1200 files, 6 notes each) cost 5.3 ms per file at every size, so the run is linear in the
number of files: the algorithmic part of the review is finished. A file with notes costs ~2.5 times a file without
(2.0 ms): that is a constant per element, to be attacked with the "tricks" of phase 2.

How to read a profile here (found at the start of phase 2): `dotnet-trace --profile dotnet-sampled-thread-time` counts every
thread of the process, so most of "UNMANAGED_CODE_TIME" is idle threads and the xUnit runner waiting. Its top managed frames
(`RuntimeHelpers.GetHashCode`, `Thread.PollGC`) are safepoint bias, not cost. Decide by experiment: a 256 MB gen0 budget
(`DOTNET_GCgen0size=0x10000000`) changed nothing except the spread-notes book (-25%), so GC is not the main cost; `DOTNET_TieredPGO=0`
makes everything 20-35% slower (dynamic PGO is worth keeping on).

### Measuring allocations (6.4)

`EpubCheckScaleBenchmarks` now also prints the KiB one run allocates per file and the number of collections (`GC.GetTotalAllocatedBytes`,
exact), and with `SIGNET_ALLOC_PROFILE=1` the types that allocate most (`AllocationProfile`, the runtime's `GCAllocationTick` event read
in the process: it names the type, not the caller, but it has none of the bias of a sampled CPU profile). Allocation per file grew with
the size of the book (2.0 MB at 50 files, 3.6 MB at 1600) and gen2 ran every ~25 files; the table by type showed a quarter of everything
in the memo of RELAX NG derivatives (`Dictionary<Name,PatternMemo>`, `PatternMemo`, `Pattern[]` in the LOH).

Cause: `RelaxNgSchema.CreateValidator` built a new `ValidatorPatternBuilder` for every document, so the memo was thrown away after each
one. Fix: one builder per schema and thread (see `UPSTREAM.md`). Best of 2 (warm), ms, and KiB allocated per file:

| book | before, ms | after, ms | before, KiB/file | after, KiB/file |
|---|---:|---:|---:|---:|
| 50 files | 77 | 11 | 1960 | 551 |
| 200 files | 319 | 59 | 2081 | 698 |
| 800 files | 1347 | 456 | 2721 | 1345 |
| 1600 files | 3261 | 1464 | 3588 | 2213 |
| 300 files x 6 notes | 1621 | 1152 | 4853 | 2278 |
| 600 files x 6 notes | 3235 | 625 | 5177 | 2577 |
| 1200 files x 6 notes (spread) | 6671 | 1651 | 5830 | 3232 |
| 200 files x 40 notes in one file | 1697 | 914 | 11810 | 9437 |

A first run in a fresh process (the first book in the table, 300 files x 6 notes, includes reading the schemas and the JIT): 3696 ms
before, 2138 ms after. Later books of the same process start with the memo the earlier ones built, so their "cold" column says less.

### The XPath engine of Schematron (6.4, second step)

The allocation table after the memo fix was led by the XPath engine: iterators and closures of `AxisStep`, an `XPathContext` per item, a list
and a boxed value for every boolean, `Atomize` in every comparison, a `new Regex` for every `matches()`, and a closure class that the compiler
allocated at the start of every `XPathFunctions.Call` (a lambda in `concat` and `distinct-values` made it). What changed: a regex per
(pattern, flags) is made once; the lambdas left `Call`; `EvaluateBoolean` answers tests (`and`, `or`, `not`, comparisons, assertions,
predicates) without a list; the two boolean lists are shared; `GeneralCompare` reads a node's string value directly; a step down the tree
fills a list with a loop; one context is reused for the items of a loop (`Rebind`); `MemoStore` is no longer allocated by every
`XPathContext.With` (a property initializer ran before the one that was set); `normalize-space` returns a clean string as it is.
Best of 2, ms, and KiB per file:

| book | after the memo, ms | after XPath, ms | after the memo, KiB/file | after XPath, KiB/file |
|---|---:|---:|---:|---:|
| 50 files | 11 | 9 | 551 | 362 |
| 200 files | 59 | 44 | 698 | 386 |
| 800 files | 456 | 336 | 1345 | 523 |
| 1600 files | 1464 | 800 | 2213 | 710 |
| 300 files x 6 notes | 1152 | 1124 | 2278 | 1441 |
| 600 files x 6 notes | 625 | 458 | 2577 | 1499 |
| 1200 files x 6 notes (spread) | 1651 | 1052 | 3232 | 1644 |
| 200 files x 40 notes in one file | 914 | 821 | 9437 | 6938 |
