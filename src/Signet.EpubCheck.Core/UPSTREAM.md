# Signet.EpubCheck.Core — upstream

This project (with `Signet.EpubCheck.Cli`) is a C# port of the official conformance checker for EPUB publications,
**EPUBCheck**. It is a re-implementation, not a translation line by line: the checks, the message ids and the
reports are meant to behave the same, the code is not. The plan is in [`EPUBCHECK_PLAN.md`](../../EPUBCHECK_PLAN.md).

| | |
|---|---|
| Upstream repository | <https://github.com/w3c/epubcheck> |
| Ported release | **5.4.0** — tag `v5.4.0` (commit `a51f751`, "chore: prepare release v5.4.0", 2026-09-15). EPUB 3 files are checked against EPUB 3.4. |
| License | BSD-3-Clause — Copyright © 2007 Adobe Systems Incorporated, © 2008 IDPF, © 2017 W3C (MIT, ERCIM, Keio, Beihang) — see [`LICENSE-EPUBCheck.md`](LICENSE-EPUBCheck.md) |
| Version of this port | `5.4.0-<yyyyMMddHHmm UTC>` — `version/EpubCheckVersion.props` and `version/Versioning.targets` |

The BSD-3-Clause license is compatible with Signet's GPL v3 or later: what comes from EPUBCheck (message ids and texts,
schemas, test fixtures, the structure of the checks) keeps its notice; Signet as a whole is distributed under the GPL.

## What is taken from upstream

Imported verbatim (when the task that needs it lands; each import lists its source path and keeps its license):

- the **schemas** (`src/main/resources/com/adobe/epubcheck/schema`): RELAX NG (`.rnc`, `.rng`), NVDL, Schematron.
  They carry their own licenses, which come along: IDPF (MIT, `schema/30/LICENSE`), the Nu HTML Checker HTML5 schemas
  (MIT, Copyright © 2005–2007 Elika J. Etemad and Henri Sivonen, © 2007–2012 Mozilla Foundation; kept up to date with
  Nu Html Checker `26.9.7.2`), the MathML and security schemas (W3C Software Notice and License);
- the **message ids**, their default severities and the English texts (`MessageBundle.properties`);
- the **test fixtures** and the Cucumber `.feature` scenarios (`src/test/resources`) into `tests/corpus/epubcheck`
  (`scripts/epubcheck/import_upstream_tests.sh`; run by `tests/Signet.EpubCheck.Conformance.Tests`).

Re-implemented in C#: everything that is Java code in upstream.

## What the Java code relies on, and what replaces it

| Upstream | Replaced by |
|---|---|
| Jing (RELAX NG, NVDL) | own engine in `Signet.EpubCheck.Core`; error wording follows Jing's exactly |
| Saxon-HE 11.4 (XPath 2.0 for Schematron) | own evaluator of the XPath 2.0 subset the `.sch` files use |
| galimatias (WHATWG URL) | own URL parser |
| ICU4J — `Normalizer2`, `CaseMap` (`UnicodeUtils`), `UnicodeSet`, `UCharacter` (`OCFFilenameChecker`) | .NET string normalization and Unicode categories; case folding to be written where .NET has none |
| commons-compress, `java.util.zip` | `System.IO.Compression` |
| SAC + upstream's own CSS parser | port of upstream's CSS parser |
| Jackson | `System.Text.Json` |
| imageio (images) | own header readers |

The datatype library `http://whattf.org/datatype-draft` is **not** needed: upstream's `epubcheck.patch` replaces the
`w:` datatypes of the Nu HTML5 schemas with plain strings (the remaining mention is a leftover declaration in
`sectional.rnc`). The schemas use XML Schema datatypes only (`string`, `anyURI`, `token`, `ID`, `IDREF`, `NMTOKEN`,
`language`, `integer`, …).

## Differences from upstream

- The UI and message languages are **English (default) and Polish** only; upstream has 12 locales.
- The tool is a standalone executable `epubcheck.net` (`win-x64`, `linux-x64`), not a jar.
- The core reads the publication through an abstraction (`IEpubSource`) that can also be backed by Signet's in-memory
  book, so Signet can check unsaved edits.

- 23 message ids are spelled with an underscore in upstream's text form (`HTM_053`, `MED_006`, …: a typo). The port
  always writes the hyphen (`HTM-053`) and still accepts the underscore when it reads an id (`MessageIds.TryParse`).
- `ReportBase.ReportingLevel` throws for a level outside `Usage`–`Fatal`; upstream prints a message and keeps the old level.
- `MessageId`, `DefaultSeverities` and `FeatureKind` are generated from the upstream sources by
  `scripts/epubcheck/generate_message_ids.py`; they are not edited by hand.

- The message texts keep upstream's placeholders (`%1$s`, filled by `JavaFormat`, a subset of Java's `String.format`),
  not .NET's `{0}`: the custom messages files people write for upstream keep working, and the English resource stays a
  verbatim copy of `MessageBundle.properties` (`EpubCheckMessages.resx`, made by `scripts/epubcheck/properties_to_resx.py`).
  The Polish texts are `EpubCheckMessages.pl.resx` (written for this port; the script checks that the keys and the
  placeholders are the same as the English ones).
- `PKG_007.whitespace` keeps its leading space. Java drops the white space before a property value, so upstream prints
  `"application/epub+zip"with no whitespace or newline.`; the port prints the sentence with the space.
- The problems of a custom messages file are reported with the real line of the file. Upstream does not count the
  header and the lines it skips, so its line numbers lag behind.
- 77 of the 323 ids have no text, as in upstream (all of them are `SUPPRESSED` by default, and `--listChecks` writes an empty
  field for them). When one is reported anyway — its severity was raised by a custom messages file — upstream prints an empty
  message; the port prints `No description is available for ACC-001.` (`PortMessages.resx`, English and Polish).

The command line (`Signet.EpubCheck.Cli`, upstream's `EpubChecker`):

- The reason of an argument error is printed on the error stream (`Version number omitted from the -v argument.`,
  `Version not supported`, ...). Upstream throws it and then swallows the exception, so only the help is shown.
- An unknown `--profile` prints `Unknown profile "x", using default validation profile.`; upstream prints the text about
  ignored modes and versions, which does not fit.
- `-o`, `-j` and `-x` without a file name need the file to check to come before them (upstream fails with an
  unexplained null pointer error), and say so.
- The EPUB 3 version in the texts is `3.4`, the version the port checks against; upstream's enum still says `3.3`.
- The help text is upstream's word for word, typos included (`in ouput`): `cli.feature` compares it exactly.
- A language without texts (`--locale fr-FR`) falls back to the default locale of the user, and to English when that has
  none either; upstream has 12 languages, the port English and Polish (`cli.feature`'s three French scenarios are not
  meant to pass).
- Files given by an `http://` or `https://` address are not fetched, and never will be (owner's decision, November 2026): the tool prints an error and exits 1, as the one upstream scenario (`cli.feature:197`) expects.
- The texts that only the port has (`The EPUB checker is not implemented yet.`, ...) are in `CliPortMessages.resx`.

Each further deviation from upstream is listed here, with the reason.
