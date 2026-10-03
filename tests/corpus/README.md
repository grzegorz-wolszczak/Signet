# tests/corpus — the EPUB test corpus

Every subdirectory is the **unpacked** content of a single EPUB (plain files, versioned in git —
readable and diffable). The `EpubBuilder` helper (in `Signet.Core.Tests`, namespace
`Signet.Core.Tests.TestSupport`) packs the chosen tree into an `.epub` file during a test:
`mimetype` as the first entry, uncompressed; the rest deflated.

Tests locate this directory through the assembly attribute `AssemblyMetadata("CorpusRoot", …)`
injected from the `.csproj` (`$(MSBuildThisFileDirectory)../corpus`). This assumes that the build and
the test run happen on the same machine (true locally and in CI).

## Rules for fixture files

- **XHTML/XML without named HTML entities** (`&nbsp;`, `&mdash;` …) — use numeric ones
  (`&#160;`). This way parsing for comparisons can work without resolving the DTD
  (`DtdProcessing.Ignore`, `XmlResolver = null`).
- `mimetype` — exactly `application/epub+zip`, without a trailing newline.
- Paths inside the EPUB always use `/`.

## Contents

| Directory | Version | What it exercises |
|---|---|---|
| `epub3/minimal` | EPUB 3.0 | the simplest valid epub3: OPF, nav, 1 XHTML, 1 CSS |
| `epub3/with-ncx` | EPUB 3.0 | epub3 with an NCX included (backward compatibility, `spine@toc`) |
| `epub3/media` | EPUB 3.0 | images, a font (`@font-face`), audio, a cover, manifest properties |
| `epub3/rich-metadata` | EPUB 3.0 | refinements (`meta@refines`), multiple `dc:identifier`, `link`, a comment in the OPF, `guide` in epub3, `properties` with multiple tokens |
| `epub3/obfuscated-fonts` | EPUB 3.0 | two obfuscated fonts (IDPF + Adobe) + `META-INF/encryption.xml`; the font files are stored **deobfuscated** (see below) |
| `epub2/minimal` | EPUB 2.0 | OPF 2.0, NCX, `guide`, `dc:creator` with `opf:role`/`opf:file-as` |
| `edge/deep-folders` | EPUB 3.0 | a non-standard, deep directory structure (a `BookPath` test) |
| `malformed/missing-mimetype` | — | no `mimetype` file |
| `malformed/no-rootfile` | — | `container.xml` with an empty `<rootfiles/>` |
| `malformed/bad-opf-xml` | — | an OPF with an unclosed tag (not well-formed XML) |
| `malformed/not-wellformed-xhtml` | — | a valid OPF, but XHTML with unclosed tags |

## Binary stubs

`epub3/media` contains minimal binary files generated deterministically by the
`generate-binaries.sh` script (a 1×1 PNG, a font dummy ≥1040 B for the obfuscation tests, an MP3 dummy).
They are not "real" media — they serve only for round-trip (byte-for-byte) tests and manifest
parsing.

`epub3/obfuscated-fonts` contains two font dummies (≥1040 B, deterministic filler)
in a **deobfuscated** (golden) form. In a real EPUB the file on disk would be obfuscated —
the `ImportEpubTests` / `RoundTripTests` tests obfuscate a copy before packing, according to the book's `dc:identifier`
(`urn:uuid:5b2e8c1a-0000-4000-8000-0000000000ab`), and then check that `ImportEpub`
decrypted the fonts back to the golden form and that `ExportEpub` obfuscated them again.
The bytes of the *obfuscated* form are **not committed** — the test computes them on the fly as
`FontObfuscation(golden)`, so they cannot drift from the golden.
