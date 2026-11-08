# tests/corpus — the EPUB test corpus

Each subdirectory is the **unpacked** content of one EPUB (plain files in git). `EpubBuilder` (`Signet.Core.Tests`,
`TestSupport`) packs a tree into an `.epub` during a test (`mimetype` first, stored; the rest deflated). Tests find the
directory via `AssemblyMetadata("CorpusRoot", …)` from the `.csproj`.

## Fixture rules
- XHTML/XML without named HTML entities (`&nbsp;`): use numeric ones (`&#160;`), so comparisons parse without the DTD.
- `mimetype` is exactly `application/epub+zip`, no trailing newline. Paths use `/`.

## Contents
| Directory | What it exercises |
|---|---|
| `epub3/minimal` | simplest valid EPUB 3: OPF, nav, 1 XHTML, 1 CSS |
| `epub3/with-ncx` | EPUB 3 with NCX (`spine@toc`) |
| `epub3/media` | images, font (`@font-face`), audio, cover, manifest properties |
| `epub3/rich-metadata` | refinements, multiple `dc:identifier`, `link`, OPF comment, `guide`, multi-token `properties` |
| `epub3/obfuscated-fonts` | two obfuscated fonts (IDPF + Adobe) + `encryption.xml`; files stored **deobfuscated** |
| `epub2/minimal` | OPF 2.0, NCX, `guide`, `dc:creator` with `opf:role`/`opf:file-as` |
| `edge/deep-folders` | deep non-standard directory structure (`BookPath`) |
| `malformed/*` | books broken as EPUBs (no `mimetype`, empty `<rootfiles/>`, unclosed OPF tag, non-well-formed XHTML) |
| `broken/` | valid EPUBs with awkward content (see its README) |
| `real/` | anonymised markup of real books (see its README) |
| `epubcheck/` | upstream EPUBCheck fixtures and scenarios (see its README) |

## Binary stubs
`epub3/media` has minimal files made deterministically by `generate-binaries.sh` (1×1 PNG, ≥1040 B font dummy, MP3
dummy) — only for round-trip and manifest tests. `epub3/obfuscated-fonts` stores the golden (deobfuscated) fonts;
tests obfuscate a copy by the book's `dc:identifier` (`urn:uuid:5b2e8c1a-0000-4000-8000-0000000000ab`) before packing
and check import decrypts and export re-obfuscates. Obfuscated bytes are never committed.
