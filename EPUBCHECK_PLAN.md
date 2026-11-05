# EPUBCHECK_PLAN.md

A C# port of the official [EPUBCheck](https://github.com/w3c/epubcheck) (W3C / DAISY, BSD-3-Clause) that
lives in this repository as a standalone command-line tool, with its checking core in a separate library
that Signet itself uses for its "Validation Results".

Status: **in progress — tasks 0.1–0.6 are done** (project skeleton, version, upstream pin, message and report model, message texts in English and Polish, conformance harness); the rest is not
implemented yet. Research done in November 2026 against the upstream
`main` (5.4.1-SNAPSHOT, after the 5.4.0 release). The port targets the **stable 5.4.0** (tag `v5.4.0`).

## Decisions taken

| # | Decision |
|---|---|
| Schema engine | **Own** RELAX NG + NVDL + Schematron/XPath 2.0 engine in C# that reads the **original upstream schemas** (also the XHTML5 content model). No Java, no IKVM in the product. |
| Projects | `Signet.EpubCheck.Core` (library), `Signet.EpubCheck.Cli` (the tool). Core must not depend on `Signet.Core`; `Signet.Core` / `Signet.App` depend on EpubCheck.Core. |
| Tool | Executable named **`epubcheck.net`**, a **standalone** (self-contained, single-file) executable — **not** a `dotnet tool`. |
| Flags | Identical to Java EPUBCheck 5.4.0 (see "CLI surface"). |
| Version | Matches upstream **5.4.0**, with the same suffix scheme as Signet: `<VersionPrefix>-<yyyyMMddHHmm UTC>`, e.g. `5.4.0-202611051200` (`version/Versioning.targets` style). `--version` prints the full version with the suffix (`EPUBCheck v5.4.0-202611051200`); the harness substitutes `{{VERSION}}` of `cli.feature` with it. |
| Platforms | `win-x64` and `linux-x64` only (macOS is out of scope). |
| Schema error text | Reproduced **exactly** as Jing words it (`RSC-005` messages). |
| Message ids | Identical to upstream `MessageId` (`OPF-014`, `RSC-005`, …) and identical default severities. |
| Languages | **English (default in the tool) and Polish only.** The 12 upstream locales are not ported. |
| Scope | Everything: EPUB 2 and 3, all profiles (`default`, `dict`, `edupub`, `idx`, `preview`), all modes, all report formats. |
| Signet | Signet's validation moves onto this library (it must end up more complete than today's). Signet-only rules (footnotes, CMYK images, file-name portability, …) stay as a **separate rule set with their own codes**, next to the EPUBCheck results; `ValidationFix`es attach to message ids. |
| Test fixtures | Upstream fixtures (BSD-3) are copied into `tests/corpus` with their license. |
| Feature files | Parsed with the official **`Gherkin`** NuGet package (cucumber/gherkin, MIT, netstandard2.0, 42.0.1 at the time of writing) — we write only the step bindings, not a parser. Test-only dependency; its addition is a separate `build` commit. |

## Open items

- **Polish translations** (about 320 messages and the CLI texts) are drafted by the agent and need the owner's review.
- The standalone build is self-contained single-file for `win-x64` and `linux-x64`; trimming off (resources,
  reflection); ReadyToRun optional; Native AOT not planned.

(Resolved in November 2026: version suffix = Signet's scheme; platforms = Linux and Windows x64; schema error text =
exactly Jing's.)

## Upstream facts the plan rests on

- ~34k lines of Java; 3.2 MB of resources; 323 `MessageId`s (OPF 109, HTM 53, RSC 38, CSS 26, PKG 25, MED 18, ACC 17,
  NAV 11, SCP 10, CHK 8, NCX 6, INF 1, OBS 1); severities `USAGE/INFO/WARNING/ERROR/FATAL/SUPPRESSED`.
- Most rules are **schemas**, not Java: 129 `.rnc` + 88 `.rng` (RELAX NG, includes the Nu HTML5, SVG 1.1, MathML
  models), 10 `.nvdl`, 29 `.sch` (~300 assertions, all `queryBinding="xslt2"`: `tokenize`, `matches`, `replace`,
  `string-join`, `distinct-values`, `lower-case`, `exists`, `empty`, plus Saxon extension functions for line number,
  column, system id).
- The schemas use **XML Schema datatypes only** (`string`, `anyURI`, `token`, `ID`, `IDREF`, `NMTOKEN`, `language`,
  `integer`, …). The Nu HTML5 `whattf` datatype library is **not** needed: upstream's `epubcheck.patch` replaced its
  datatypes with plain strings (checked on `v5.4.0` in task 0.1); attribute-value checks that the Nu validator does
  through those datatypes are done by upstream's Java code and Schematron, so they come with epics 2 and 3.
- ICU4J is used in two places only: `UnicodeUtils` (`Normalizer2`, `CaseMap`) and `OCFFilenameChecker` (`UnicodeSet`,
  `UCharacter`) — .NET covers normalization and character categories; case folding needs care (task 1.2).
- Java libraries behind it: Jing (RELAX NG/NVDL), Saxon-HE 11.4 (XPath 2), ICU4J, galimatias (WHATWG URL),
  commons-compress, SAC (CSS), Jackson (JSON). There is no usable .NET equivalent of Jing + Saxon.
- Own CSS parser (~5.8k lines in `org.idpf.epubcheck.util.css`).
- Conformance suite: **1022 scenarios in 50 `.feature` files** (Cucumber), ~3.5k fixture files (~24 ready `.epub`,
  the rest unpacked folders) in `src/test/resources`, plus `cli.feature` (exact help text, return codes).
  Step vocabulary is small (see task 0.5).

## CLI surface (to reproduce 1:1)

`epubcheck.net <path> [options]`

| Option | Meaning |
|---|---|
| `-v 2\|2.0\|3\|3.0` | EPUB version when checking a non-EPUB file (default 3.0) |
| `-m, --mode opf\|xhtml\|svg\|nav\|mo\|exp` | check a single file / an expanded archive |
| `-p, --profile default\|dict\|edupub\|idx\|preview` | validation profile |
| `--save` | save the EPUB built from an expanded archive |
| `-o, --out <file\|->` / `-x, --xmp <file\|->` / `-j, --json <file\|->` | XML / XMP / JSON report |
| `--maxOfEachMessage N\|unlimited` | per message-id-and-text limit in XML/JSON reports (default 25) |
| `--failonwarnings` | exit 1 on warnings too |
| `-q`, `-f`, `-e`, `-w`, `-u` (long: `--quiet`, `--fatal`, `--error`, `--warn`, `--usage`) | console reporting level |
| `--locale <BCP 47>` | message language (**`en`** and **`pl`** only) |
| `-l, --listChecks [file]` / `-c, --customMessages [file]` | list ids and severities / override severities |
| `--version`, `-h`, `-?`, `--help` | version / help |

Exit code 0 (no errors) or 1. The argument parser is hand-written (optional arguments, `-` for stdout,
`unlimited`), not `System.CommandLine`.

## Tasks

Sizes: **S** a few files · **M** a module · **L** a large module · **XL** a sub-project.
Every task ships with tests in the project's conventions (xUnit.v3 + AwesomeAssertions, `// Arrange` / `// Act` /
`// Assert`, `TempDir`, nothing left behind) and uses Conventional Commits.

### Epic 0 — Foundations

- **0.1 Pin upstream and licenses (S). DONE.** Tag `v5.4.0` exists (commit `a51f751`, 2026-09-15; `pom.xml` says
  5.4.0). `src/Signet.EpubCheck.Core/UPSTREAM.md` and `LICENSE-EPUBCheck.md` added; the schemas' own licenses (IDPF
  MIT, Nu HTML5 MIT, W3C Software) are listed there and travel with the schemas when they are imported (2.6). Findings:
  `whattf` datatypes are not needed; ICU4J is used in two classes only.
- **0.2 Project skeleton (S). DONE.** `Signet.EpubCheck.Core`, `Signet.EpubCheck.Cli` (assembly `epubcheck.net`),
  `Signet.EpubCheck.Core.Tests`, `Signet.EpubCheck.Cli.Tests`; entries in `Signet.slnx`; `TestIsolationSetup.cs` in each
  test project; version scheme.
- **0.3 Report and message model (M). DONE.** `MessageId` (323 ids, upstream order), `MessageIds` (text form always with a hyphen, `TryParse` also accepts
  upstream's underscore typo of 23 ids), `DefaultSeverities`, `Severity`, `Message`, `IMessageDictionary`,
  `EpubLocation`, `FeatureKind` (60 facts), `IReport`, `ReportBase` (reporting level, counters), `CollectingReport`.
  The enum files are generated from the upstream sources by `scripts/epubcheck/generate_message_ids.py`. Not in 0.3
  (they need the message texts): the dictionary with texts, severity/text overrides from a `--customMessages` file
  and the `--listChecks` dump — they move to 0.4; `maxOfEachMessage` is stored in the report and applied by the
  XML/JSON writers (4.2).
- **0.4 Message texts, dictionary and EN/PL (M). DONE.** `EpubCheckMessages.resx` (English, neutral; a verbatim copy
  of upstream's `MessageBundle.properties`, 280 keys) and `EpubCheckMessages.pl.resx` (Polish), `LocalizedMessages`
  (texts, suggestions, localized arguments, `Format`), `JavaFormat` (placeholders `%1$s` kept instead of `{0}`, so that
  upstream's custom messages files work as they are), `LocalizedMessageDictionary`, `OverriddenMessageDictionary`
  (`--customMessages`, `CHK-001`…`CHK-007`), `MessageDictionaryDumper` (`--listChecks`), `ReportBase.SetLocale` /
  `SetOverrideFile`, `PathUtil`, and a fallback text (`PortMessages.resx`) for the 77 ids that have none. Not in 0.4: the texts of the tool itself (help, counters, "Validating using EPUB
  version…", `messages.properties` of upstream's `util` package) — they come with the CLI in 0.6.
- **0.5 Conformance harness (M). DONE.** `tests/Signet.EpubCheck.Conformance.Tests`: the `.feature` files are parsed by
  the `Gherkin` package (MIT); the steps are bound after upstream's step definitions (Cucumber expressions translated by a
  small `StepExpression`; Cucumber's own .NET package was not used — it leaves the parameter-type registry to the caller).
  The fixtures and scenarios of `v5.4.0` are in `tests/corpus/epubcheck` (23 MB, 3449 files; imported by
  `scripts/epubcheck/import_upstream_tests.sh`, kept byte for byte). All **1251 scenarios** (50 features, outlines
  expanded) are understood — every step of every one has exactly one binding (`StepCoverageTests`). Scenarios of the
  features in `ConformanceCorpus.EnabledFeatures` must pass (none yet — add a feature there when its task lands:
  `cli/cli.feature` with 0.6, `epub3/04-ocf/ocf.feature` with 1.2, …); the others are skipped with the reason.
  `SIGNET_CONFORMANCE_ALL=1` runs all of them: a scenario that needs a part of the port that is missing ends as
  "not implemented" (skipped), any other failure is shown (today the 23 pending ones of `cli.feature`). The steps that need the report
  writers (JSON/XML), the URL parser, the file name checker or the viewport parser are bound to "not implemented" with
  the task that brings them. The harness itself has tests on scenarios of its own and a scripted checker.
- **0.6 CLI parser (M). DONE.** `ToolRun` (upstream's `EpubChecker`): every option and spelling, the help and version texts
  (`ToolMessages.resx`, a copy of upstream's `messages.properties`; Polish in `ToolMessages.pl.resx`), `--listChecks`,
  `--customMessages` (also from the `ePubCheckCustomMessageFile` variable), `--locale`, the reporting levels, `--quiet`,
  the argument errors, the closing counters and the exit codes; `ConsoleReport` prints messages as upstream's default
  report does (task 4.1's console part). What needs the checker or the report writers says it is not implemented yet.
  `cli/cli.feature` is enabled in the conformance harness: 23 of its 53 scenarios run and pass; 30 are `pending` with the
  reason (the checker, the report writers, the archive of `--save`, French messages that the port does not have).

### Epic 1 — Raw input layer (read-only)

- **1.1 EPUB source abstraction (M).** `IEpubSource` over a ZIP file, a folder (`exp`), a single file and an
  **in-memory source** (so Signet can validate unsaved edits). Gives entries, order, compression method, sizes, bytes.
- **1.2 OCF / ZIP checker (L).** `PKG-*`: `mimetype`, duplicate entries, empty directories, file names (characters,
  normalization, case collisions), broken archives as FATAL. After `OCFChecker`, `OCFZipChecker`, `OCFFilenameChecker`.
- **1.3 WHATWG URL parser (M).** Replacement for galimatias (`System.Uri` is not WHATWG), URL fragments, path
  normalization.
- **1.4 Utilities (M).** MIME type parser, `CodePoints`, microsyntaxes, `PackageDocumentPeeker`, prefix / vocabulary
  parsing (`vocab`, 24 files).

### Epic 2 — Schema engine (critical path)

- **2.1 RELAX NG model and validator (XL).** XML syntax loading, simplification (`include`, `ref`, `externalRef`,
  `define combine`, `grammar`), streaming derivative-based validation over `XmlReader` with line/column; error messages
  **exactly as Jing words them** (e.g. `element "x" not allowed here; expected …`); the wording is part of the
  expected behaviour and is covered by the `the message contains '…'` scenarios.
- **2.2 Compact syntax `.rnc` parser (M).** 129 files; verify equivalence with the `.rng` counterparts.
- **2.3 Datatypes (M).** XSD datatypes only (via `XmlSchemaDatatype` or own, incl. XSD-regex → .NET translation);
  the `whattf` library is not needed (see "Upstream facts").
- **2.4 NVDL (L).** 10 files; first list the constructs upstream really uses, implement that set.
- **2.5 Schematron + XPath 2.0 subset (XL).** List the constructs used by the 29 `.sch` files; parser and evaluator of
  that XPath 2.0 subset (functions above, quantifiers, `for`), custom functions (line, column, system id); patterns,
  rules, asserts/reports, `let`.
- **2.6 Schema catalog and loading (S).** Embedded resources, `epub30-catalog.xml`, compiled-schema cache, start-up cost
  measured in `Signet.Benchmarks`.
- **2.7 Mapping to messages (M).** Schema errors → `RSC-005` with location; Schematron assertions → proper ids.

### Epic 3 — Publication rules

- **3.1 `container.xml`, `encryption.xml`, `metadata.xml`, signatures (M).**
- **3.2 OPF 2 (L).** `opf.rng/sch`, manifest, spine, `guide`, fallback chains.
- **3.3 OPF 3 (XL).** `package-30.rnc/sch`, `OPFChecker30` / `OPFHandler30`, `properties`, refinements, collections,
  vocabularies.
- **3.4 Navigation (M).** `nav` (`NAV-*`), NCX (`NCX-*`).
- **3.5 Content documents (XL).** `OPSChecker` / `OPSHandler`(30), XHTML5, EPUB 2 XHTML, SVG, MathML, DTBook,
  `epub-xhtml-30.sch`, entities, encodings.
- **3.6 References and resource graph (L).** `RSC-*`, fragments, remote resources, unused resources, external identifiers.
- **3.7 CSS (L).** Port of upstream's parser and `CSSChecker` / `CSSHandler` (`CSS-*`) — a separate parser for
  fidelity; Signet's own CSS parsers serve another purpose.
- **3.8 Media and fonts (M).** `MED-*`, image headers (JPEG, PNG, GIF, WebP, SVG) without a native dependency.
- **3.9 Media Overlays (M).**
- **3.10 Scripting and accessibility (M).** `SCP-*`, `ACC-*`.
- **3.11 Profiles and extensions (L).** `dict`, `edupub`, `idx`, `preview`, multiple renditions, data navigation,
  distributable objects, scriptable components, region-based navigation.
- **3.12 Single-file and expanded modes (M).** `--mode opf|xhtml|svg|nav|mo|exp`, `--save`.

### Epic 4 — Reports and the executable

- **4.1 Console output (S).** Upstream format, counters, `-q`. Done with 0.6 (`ConsoleReport`, the closing counters).
- **4.2 XML / XMP / JSON reports (M).** Publication metadata, items, messages, locations; `System.Text.Json`, field
  order as upstream; `maxOfEachMessage`.
- **4.3 Standalone executable (S).** Publish profiles for `win-x64` and `linux-x64` (self-contained, single-file, no
  trimming), a script in `scripts/`, an archive per RID, README. **No** `PackAsTool`. Smoke test: run the published
  binary on a corpus book.

### Epic 5 — Signet integration

- **5.1 `Book` → `IEpubSource` adapter (M).** Validate the in-memory state, unsaved changes included.
- **5.2 Results in the UI (M).** `MessageId` as `ValidationResult.Code`, Polish texts per the app language, progress and
  cancellation.
- **5.3 Signet-only rules and fixes (M).** Keep as a separate rule set with codes that cannot collide with EPUBCheck
  ids; `ValidationFix` bound to message ids.
- **5.4 Preferences (S).** "Skipped rule codes" become severity overrides (`SUPPRESSED`) — the same mechanism as
  `--customMessages`.
- **5.5 Retire the old validators (M).** Remove the replaced classes and tests, update `README.md` and `AGENTS.md` —
  only when the new library covers everything the old code detected.

### Epic 6 — Quality

- **6.1 Conformance report (S).** Table of scenarios: green / skipped (with reason) / red.
- **6.2 Differential test against Java (S, optional).** Local only, needs a JDK; compares sets of message ids on
  `tests/corpus/real` and `tests/corpus/broken`.
- **6.3 Scale (S).** Large generated books (`SyntheticBook`), measured in `Signet.Benchmarks` (timings never asserted).

## Order and milestones

Critical path: 0.2 → 0.3 → 1.1 → 2.1 → 2.2 → 2.3 → 2.4 → 2.5 → 3.2 / 3.3 → 3.5. Epic 2 is the biggest risk.

- **M1 (walking skeleton):** 0.1–0.6, 1.1, 1.2, 4.1 — the tool opens an EPUB, checks the OCF layer, prints a report;
  the scenarios of `ocf.feature` and `cli.feature` that depend only on this pass.
- **M2:** 2.1–2.7, 3.1–3.4 — schema engine works; OPF and navigation are checked.
- **M3:** 3.5–3.8 — XHTML, SVG, CSS, media, references; most EPUB 3 scenarios pass.
- **M4:** 3.9–3.12, epic 4, 6.2 — the rest of the scope and all report formats.
- **M5:** epic 5 — Signet moves onto the library.

## Risks

- The schema engine (epic 2) is a project of its own; fidelity to Jing's error wording and to Saxon's XPath semantics
  is where scenarios will fail first. Matching Jing's error wording exactly is a hard requirement, so the RELAX NG
  validator must follow Jing's choice of reported error (and its "expected …" lists), not just accept/reject.
- Upstream moves on (5.4.1 and later): the port is pinned to 5.4.0, later versions are a separate task.
- Start-up cost of compiling ~200 schemas; mitigated by the cache (2.6) and measured early.
- Polish translations need a human review.
