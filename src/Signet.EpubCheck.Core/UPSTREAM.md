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
| Jing 20181222 (RELAX NG, NVDL) | `Jing/`: a port of Jing (BSD-3-Clause, `Jing/LICENSE-jing.md`) — RELAX NG today, NVDL in task 2.4; error wording is Jing's exactly |
| Saxon-HE 11.4 (XPath 2.0 for Schematron) | own evaluator of the XPath 2.0 subset the `.sch` files use |
| galimatias 0.1.3 (WHATWG URL of 2014) | `Galimatias/`: a port of galimatias (MIT, `Galimatias/LICENSE-galimatias.md`), checked against its own test data; IDNA by .NET's `IdnMapping` |
| ICU4J — `Normalizer2`, `CaseMap` (`UnicodeUtils`), `UnicodeSet`, `UCharacter` (`OCFFilenameChecker`) | .NET string normalization; the code point sets of `OCFFilenameChecker` written out; full case folding from a generated table (`Util/CaseFolding.g.cs`) |
| commons-compress, `java.util.zip` | `System.IO.Compression`; the central directory is read by hand (`ZipEpubSource`) |
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
  `scripts/epubcheck/generate_message_ids.py`; they are not edited by hand. `Util/CaseFolding.g.cs` is generated from
  the Unicode Character Database by `scripts/epubcheck/generate_case_folding.py`, `Vocab/Vocabularies.g.cs` from the
  vocabulary classes by `scripts/epubcheck/generate_vocabs.py`.

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

The OCF container (`Ocf/`, upstream's `com.adobe.epubcheck.ocf`):

- The ZIP header check tests `header[0] != 'P' || header[1] != 'K'`; upstream writes `&&`, so a file starting with
  `PX` passes as a ZIP header.
- `PKG-027` (file names not in UTF-8) is reported only when a name cannot be decoded as UTF-8. Upstream reports it for
  every Java error starting with `invalid CEN header`, also a broken central directory, which is `PKG-008` here.
  Like Java's `ZipFile(file, UTF_8)`, every name is read as UTF-8, whether or not the entry sets the UTF-8 flag.
- The `SHA-256` fact of a resource is the full hexadecimal digest; upstream drops the leading zero of each byte
  (`Integer.toHexString`), so its digests are not the real ones.
- Directories of an expanded publication end with `/`, as in a ZIP. Upstream's empty-directory check (`PKG-014`)
  compares by prefix, so for an expanded publication a directory `a` counts the file `ab.xhtml` as its content.
- The creation date of an expanded publication's `container.xml` has the format of a ZIP one
  (`yyyy-MM-dd'T'HH:mm:ss'Z'`, UTC); upstream prints Java's `FileTime` there.
- Case folding (duplicate names, `OPF-060`) uses a table generated from `CaseFolding.txt` of Unicode 16.0.0 (ICU4J 77.1,
  the version upstream uses) by `scripts/epubcheck/generate_case_folding.py`: .NET has no case folding.

URLs (`Galimatias/`, a port of galimatias 0.1.3, and `Util/EpubCheckUrlUtils`, upstream's `URLUtils`):

- URLs are parsed as galimatias 0.1.3 parses them (the URL standard of 2014: "relative" schemes, scheme data), not
  as today's URL standard: EPUBCheck's behaviour depends on that parser. All 802 cases of galimatias' test data pass.
- IDNA (UTS #46) is .NET's `IdnMapping` instead of ICU4J. ICU's `IDNA.DEFAULT` is transitional processing, so the
  deviation characters (`ß`, `ς`, ZWJ, ZWNJ) are mapped before `IdnMapping`, which is nontransitional; ASCII domain
  names are only lower-cased and checked for empty and too long labels (what ICU reports for them), because .NET would
  also reject a label starting or ending with a hyphen, which galimatias accepts.
- Fixed bugs of galimatias: `withFragment`/`withQuery` made every URL hierarchical (`urn:x#y` without its fragment became
  `urn://`); `relativize` removed the prefix with `String.replaceFirst`, a regular expression (a path with `(` threw);
  `equals` compared the path of one URL with the host of the other; the user info was percent-encoded per UTF-16 unit, so
  a character outside the BMP became `%3F%3F`.
- The `toJavaURI`-based helpers of upstream's `URLUtils` (`toFile`, `toFilePath`) use .NET's `Uri` for `file:` URLs.

Vocabularies (`Vocab/`, upstream's `com.adobe.epubcheck.vocab`):

- A prefix declared twice in a `prefix` attribute keeps its first URI. Upstream collects the mappings in Guava's
  `ImmutableMap.Builder`, whose `build()` throws on a repeated key, so such a package document stops the check with an
  unexpected error.
- The URIs of prefix declarations are checked as `java.net.URI(String)` does (`Util/JavaUri`, after the JDK's
  `URI.Parser`); an IPv6 literal in brackets is checked with .NET's `IPAddress` instead of the JDK's own reader.
- The enums of properties and their names are generated from the Java sources by `scripts/epubcheck/generate_vocabs.py`
  (`Vocab/Vocabularies.g.cs`); a property list (`ParsePropertyList`) keeps its order, as upstream's `ImmutableSet`.

RELAX NG (`Jing/`, a port of Jing 20181222, tag `V20181222` of `relaxng/jing-trang`, the version upstream depends on):

- Ported: the XML syntax parser (`rng-parse`), the schema builder with its restrictions (section 7 of the RELAX NG
  specification), the pattern model with derivative-based validation and Jing's error recovery, the builtin datatype
  library, and the message bundles (`Jing/Resources/PatternMessages.resx`, `ParseMessages.resx`, copies of Jing's
  `.properties` made by `scripts/epubcheck/properties_to_resx.py`; the Polish `*.pl.resx` are drafts written for the port).
  The texts keep Java's `MessageFormat` placeholders (`{0}`), filled by `JingLocalizer.Format`.
- Jing's own RELAX NG test suite (`spectest.xml`) runs as Jing's `TestDriver` runs it: all 376 cases without XML Schema
  datatypes pass; the 9 that use them wait for task 2.3.
- Not ported, because upstream does not use them: the ID/IDREF checks (`CHECK_ID_IDREF`, `IdTypeMap`), the feasible
  mode (`FeasibleTransform`), the compact syntax (task 2.2), the datatype libraries other than the builtin one (XSD in 2.3).
- SAX is replaced by `XmlReader` (`Jing/Sax/XmlEventReader`): DTDs are parsed for entities and default attributes, no
  external resource is fetched. The position of an error is where `XmlReader` is — the start of the node — while Xerces
  reports the end of the start tag, so a column can differ from upstream's (the line is the same unless a tag spans lines).
  DTD notations and unparsed entities are not reported to the validator (`XmlReader` does not expose them; no EPUB schema
  uses `ENTITY` datatypes).
- Jing looks up `datatype_requires_param` and `datatype_requires_param_detail`, keys its bundle does not have (Java then
  fails with a missing resource); the port uses the keys that exist, `datatype_requires_parameter(_detail)`.
- Where Jing's output order depends on a `HashSet` or `HashMap` (the reasons of a `data` failure, the undefined
  references of a grammar), the port keeps the order in which they were found, so the messages are stable.
- `GetPrefix` of the validator context compares namespace URIs by value; Jing compares references, which with Xerces
  (it interns the URIs) is the same.

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
