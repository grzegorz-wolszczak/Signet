# Signet.EpubCheck.Core — upstream

C# re-implementation (not a line-by-line translation) of the W3C **EPUBCheck**; checks, message ids and reports behave
the same. Plan: [`EPUBCHECK_PLAN.md`](../../EPUBCHECK_PLAN.md).

| | |
|---|---|
| Upstream | <https://github.com/w3c/epubcheck>, release **5.4.0** (tag `v5.4.0`, commit `a51f751`, 2026-09-15); EPUB 3 files are checked against EPUB 3.4 |
| License | BSD-3-Clause, see [`LICENSE-EPUBCheck.md`](LICENSE-EPUBCheck.md); compatible with Signet's GPL v3+ — taken parts keep their notice |
| Port version | `5.4.0-<yyyyMMddHHmm UTC>` (`version/EpubCheckVersion.props`, `version/Versioning.targets`) |

## Taken from upstream
- **Schemas** (RELAX NG, NVDL, Schematron) in `Xml/Schemas/schema` + the two XSLT stylesheets of its build
  (`util/sch`), embedded (`scripts/epubcheck/import_upstream_schemas.sh`). Their own licenses travel with them: IDPF
  (MIT), Nu HTML Checker 26.9.7.2 (MIT), MathML/security (W3C Software).
- **Message ids, default severities, English texts** (`MessageBundle.properties`).
- **Fixtures and `.feature` scenarios** → `tests/corpus/epubcheck` (`scripts/epubcheck/import_upstream_tests.sh`).
- Everything that is Java code is re-implemented.

## Replacements for Java dependencies
| Upstream | Port |
|---|---|
| Jing 20181222 | `Jing/` (port; BSD-3, `Jing/LICENSE-jing.md`); wording exactly Jing's |
| Saxon-HE 11.4 (XPath 2) | `Jing/Validate/Schematron/XPath/`: own evaluator of the subset the `.sch` files use |
| galimatias 0.1.3 | `Galimatias/` (port; MIT); IDNA via .NET `IdnMapping` |
| ICU4J | .NET normalization; code point sets written out; case folding from generated `Util/CaseFolding.g.cs` |
| commons-compress, `java.util.zip` | `System.IO.Compression`; central directory read by hand (`ZipEpubSource`) |
| SAC + upstream CSS parser | port of upstream's CSS parser |
| Jackson / imageio | `System.Text.Json` / own header readers |

The `whattf` datatype library is not needed (upstream's `epubcheck.patch` replaced it with plain strings).

## Deviations

**General**
- Languages: English (default) and Polish only (upstream: 12). Tool is a standalone `epubcheck.net`, not a jar.
- Input goes through `IEpubSource`, which can be Signet's in-memory book (unsaved edits are checked).
- RELAX NG derivative memo is kept between documents, one builder per schema and thread (Jing rebuilds it per
  validator: 2–4× slower on large books). Same results (`RelaxNgSharedMemoTests`).
- 23 ids spelled with `_` upstream (`HTM_053`…) are written with a hyphen; `MessageIds.TryParse` accepts both.
- `ReportBase.ReportingLevel` throws for a level outside `Usage`–`Fatal` (upstream prints a message).
- Generated, not hand-edited: `MessageId`/`DefaultSeverities`/`FeatureKind` (`generate_message_ids.py`),
  `CaseFolding.g.cs`, `Vocabularies.g.cs`.
- Texts keep Java placeholders (`%1$s`, `JavaFormat`) so upstream custom-message files work; English `.resx` is a
  verbatim copy; Polish is `EpubCheckMessages.pl.resx` (draft).
- `PKG_007.whitespace` keeps its leading space. Custom message file problems get real line numbers.
- 77 ids have no text (as upstream, all `SUPPRESSED`); if reported anyway the port prints
  `No description is available for <id>.` (`PortMessages.resx`).

**OCF (`Ocf/`)**
- ZIP header check uses `||` (upstream `&&`: `PX` passed).
- `PKG-027` only when a name can't be decoded as UTF-8 (upstream: any `invalid CEN header`; here `PKG-008`).
- `SHA-256` fact is the full hex digest (upstream drops leading zeros).
- Expanded directories end with `/` (upstream's `PKG-014` prefix compare counted `ab.xhtml` as content of `a`).
- Expanded `container.xml` creation date uses the ZIP format (`yyyy-MM-dd'T'HH:mm:ss'Z'`).
- Case folding (`OPF-060`) from Unicode 16.0.0 `CaseFolding.txt` (ICU4J 77.1).

**URLs (`Galimatias/`, `Util/EpubCheckUrlUtils`)**
- Parsed as galimatias 0.1.3 (2014 URL standard); all 802 of its test cases pass.
- ASCII hosts are only lower-cased and checked for empty/too long labels (.NET would reject hyphen-edged labels);
  deviation characters (`ß`, `ς`, ZWJ, ZWNJ) are mapped first (ICU transitional).
- Fixed galimatias bugs: `withFragment`/`withQuery` made URLs hierarchical; `relativize` used regex `replaceFirst`;
  `equals` compared path with host; user info encoded per UTF-16 unit.
- `toFile`/`toFilePath` use .NET `Uri`.

**Vocabularies (`Vocab/`)**
- A prefix declared twice keeps the first URI (upstream's Guava builder throws).
- Prefix URIs are checked like `java.net.URI` (`Util/JavaUri`); IPv6 literals via .NET `IPAddress`.
- Property lists keep order.

**RELAX NG (`Jing/`)**
- Ported: XML syntax parser, builder with restrictions, derivative validation + error recovery, builtin datatypes,
  message bundles (Polish drafts). Jing's `spectest.xml` passes.
- Not ported (unused upstream): ID/IDREF checks, feasible mode.
- SAX → `XmlReader` (`Jing/Sax/XmlEventReader`): DTDs parsed for entities/defaults, nothing fetched; error column is the
  node start (Xerces: end of start tag); DTD notations/unparsed entities re-read via `XmlDocument`.
- Uses the existing keys `datatype_requires_parameter(_detail)` (Jing asks for missing ones).
- Where Jing's output order depends on hash order, the port keeps discovery order.
- Compact syntax (`Parse/Compact/`) is a hand-written port of `CompactSyntax.jj`; comments skipped; a surrogate pair
  gets one position.

**XSD datatypes (`Jing/Datatype/Xsd/`)**
- Regex classes are computed as code point sets from .NET Unicode data (may differ from the JDK's Unicode version);
  `\i`, `\c` are XML 1.0 name characters (BMP).
- `bad_category` names the category; fixed typos `in ths context`, `a units of length`.
- Tiny-fraction `length` facets are refused like any fraction; date/time and `duration` have no length (a length facet
  is refused at schema read); equal values have equal hash codes.
- Datatype messages follow `RelaxNgSchemaReader.Culture`.
- Kept as Jing: digit counts as written, time zone −13:00…+14:00, year ≤ 292278994.

**NVDL (`Jing/Validate/`)**
- Mode caches are `ConcurrentDictionary` (shared schemas across threads).
- `AutoSchemaReader` reads the root to learn the language, then re-reads (schema opened twice).
- Mode and attribute-namespace order is first-named/written order.
- No NVDL options (`mustSupport` option → `option "…" not recognized`); MNS, NRL and W3C XML Schema sub-schemas not
  ported (unused). Anonymous modes numbered per schema.

**Schematron (`Jing/Validate/Schematron/`)**
- Evaluated directly (no XSLT/Saxon) with the semantics of the compiled stylesheet and Jing's `OutputHandler` messages;
  XPath 1.0 compatibility mode, doubles, regexes by .NET.
- Saxon-worded errors have the port's texts (`xpath_syntax_error`, `xpath_dynamic_error`).
- Position is the element's start tag. Not ported: `id()`, `document()`, keys, diagnostics, `phase` option, ID/IDREF
  checks. Comments/PIs are not in the expression tree.

**Schema loading (`Xml/`)**
- `iso-sch-include.xsl`/`iso-sch-abstract.xsl` run (via `XslCompiledTransform`) the first time a `schema/30/*-30.sch`
  is opened; stylesheet problems are errors (upstream's build only prints them).
- Embedded files are `resource:/schema/…` URIs; `epub30-catalog.xml` is embedded but unread (as upstream).
- Each of the 50 schemas is read on first use (thread-safe); a schema with errors throws `InvalidOperationException`
  with Jing's messages.
- `SchematronStatementsOnly` option reproduces EPUBCheck's emptied `assertion failed:`/`report:` bundle.
- `ReportingErrorHandler`: a message that is only `WARNING:`/`USAGE:` gives empty text (upstream throws).

**XML parser / OCF flow (`Xml/`, `Ocf/`, `References/`)**
- `XmlReader` instead of Xerces: not well-formed → `RSC-016` with .NET's text (except two Xerces-worded errors:
  undeclared entity, entity without `;`); XML 1.1 is read as 1.0 with `HTM-001`; other declared encodings decode via .NET.
- `OCFContainer` keyed by normalized URL; root URL `https://<guid>.epubcheck.w3c.org` as upstream.
- `ValidationContext` is an immutable record (`with`).
- A package document peeked without a version uses the requested version (upstream asserts).
- `EPUBVersion.toString` is `3.4` (upstream says `3.3`).

**Package documents (`Opf/`)**
- A guide `reference` whose URL didn't parse is skipped (upstream NPE).
- `OPF-041` names `fallback` (as upstream). `DateParser` keeps Java calendar texts (Julian before 1582) and returns only
  the year. `UUID.fromString` is Java's lenient form.
- EPUB 3: `ResourceCollection` → `CollectionNode`, `Roles` → `CollectionRole` (.NET naming rules).
- `LanguageTag.Validate` ports the JDK parsing (not the builder's later check).
- Item with `properties` but no `media-type` doesn't fail (upstream NPE). `Item` handling keys by raw `id`, base
  class by trimmed id. `MetadataSet` keeps `LinkedListMultimap` order.

**Resources and references (`References/`, `Ncx/`)**
- Registries are thread-safe; feature report keeps each feature once in report order.
- `PublicationResourceChecker.Check` keeps upstream's control flow (post-content checks also run when the resource
  wasn't found).
- NCX vs unique identifier uses Java `trim` (≤ U+0020).
- A checker upstream can't build (`IllegalStateException`) → `InvalidOperationException` → `CHK-008`.

**Content documents (`Ops/`, `Nav/`, `Dtbook/`)**
- `url()` / `url(')` in SVG `fill`/`stroke`/`clip-path` don't fail (upstream `StringIndexOutOfBounds`).
- Unparsable resource URLs are dropped after `RSC-020` (upstream `IllegalArgumentException`).
- Unparsable `type` on `source`/`embed` → `OPF-013` and continue (upstream NPE).
- Media sources keep discovery order (upstream `HashMap`); required/allowed item properties iterate in enum order.
- `xml-stylesheet` pseudo-attributes read by `ProcInstParser` (small Saxon re-implementation).
- `OPFChecker30.CheckPackage` runs feature-based checks even when the package document isn't in the container.

**CSS (`Css/`)**
- Scanner/grammar/parser ported state by state; tests = upstream's `CssScannerTest` (129 of 131) + a selection of
  `CssParserTest`. Messages in `CssMessages.resx`.
- `@charset` searched in the first 256 bytes (upstream loops forever on a file that is exactly `@charset`); empty
  charset = none; unknown charset → UTF-8; BOMs as upstream (incl. its `UTF-32LE`).
- `CssPseudoSelector.Name` is null for `a::<other>` (upstream NPE).
- `CSSHandler` quirks kept (`@keyframes` percentage check compares apostrophes; `CSS-028` per `@font-face` declaration).
- Any exception reading a style sheet → `PKG-008`.

**Media overlays, images, modes, reports**
- `SmilClock`: three syntaxes, truncation to ms; numbers beyond `decimal` aren't clock values; only the full `toString`.
  `OverlayHandler` doesn't fail on an unparsable audio `src`.
- `BitmapChecker` reads sizes from headers (PNG, GIF first image, JPEG, BMP) on in-memory bytes; unknown ImageIO-readable
  formats (WBMP) → `PKG-021`.
- `--mode exp`: if `<folder>.epub` exists and no `--save`, the archive goes to a temp file (upstream overwrites then
  deletes).
- `tests/corpus/epubcheck/cli/files/wrong_extension.zip` is kept despite `.gitignore` (`*.zip`).
- XML/XMP reports via `System.Xml.Linq` (upstream's content and order; .NET white space). JSON via
  `JacksonStyleJsonWriter` (Jackson's pretty printer, upstream's field order checked against the 5.4.0 jar);
  `checker.path` is `Path.GetFullPath`.
- Harness: `JsonPathLite` replaces JsonPath; namespaces are stripped from XML reports before XPath 1.0.
- Six scenarios asking for French messages (`cli.feature` 366/371/376, `localization.feature` 16/31/37) are pending for
  good.

**Command line (`Signet.EpubCheck.Cli`)**
- Argument error reasons go to stderr (upstream swallows them). Unknown `--profile` prints
  `Unknown profile "x", using default validation profile.`
- `-o/-j/-x` without a file name need the checked file before them, and say so.
- Help text is upstream's word for word, typos included (`cli.feature` compares it).
- `--locale` without texts falls back to the user's culture, then English.
- `http(s)://` input is never fetched: error, exit 1 (owner's decision, `cli.feature:197`).
- Port-only texts are in `CliPortMessages.resx`.

Each further deviation is listed here with its reason.
