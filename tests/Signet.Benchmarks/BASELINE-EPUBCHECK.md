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

