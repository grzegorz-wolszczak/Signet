# EPUBCHECK_PLAN.md

A C# port of the official [EPUBCheck](https://github.com/w3c/epubcheck) (W3C / DAISY, BSD-3-Clause) that
lives in this repository as a standalone command-line tool, with its checking core in a separate library
that Signet itself uses for its "Validation Results".

Status: **in progress — epics 0 and 1 are done** (project skeleton, version, upstream pin, message and report model,
message texts in English and Polish, conformance harness, command line, EPUB sources and OCF/ZIP checks, galimatias URL
parser and URL fragments, MIME types, viewport, package-document peeker, vocabularies); tasks 1.1–1.4 were reviewed and
re-ported on 2026-11-06 (see "Review of November 2026"); **next: task 2.1 RELAX NG model and validator**.
Research done in November 2026 against the upstream
`main` (5.4.1-SNAPSHOT, after the 5.4.0 release). The port targets the **stable 5.4.0** (tag `v5.4.0`).

## Handoff — read this first (for the next agent)

The owner types **"kontynuuj"** (continue the work on EPUBCHECK). Do exactly this, **without asking questions**
(everything below is already decided; ask only when something truly new and unspecified comes up, and then collect all
questions into one message, as `AGENTS.md` demands).

### What to do on "kontynuuj"

1. Read this file, `AGENTS.md`, `src/Signet.EpubCheck.Core/UPSTREAM.md` and the memory index.
2. Take the **first task in "Tasks" that is not marked DONE** (today: **2.1 RELAX NG model and validator**, see "Next
   task" below). Do **one task**, then stop. Do not start the next one.
3. Work, following the conventions below. Update this file as you go: mark the task **DONE**, describe what was
   built and what is deliberately not (as the done tasks 0.1–0.6 do), update the "Status" line and the numbers in
   "Current state".
4. Finish with the git routine below, then report in Polish in a few lines: what was done, the test numbers, and the
   next task.

### Git routine after a task (standing permission from the owner — do it without asking)

1. Fix the warnings (the `IDExxxx` ones and any other) last, then run **all** tests of the EpubCheck projects (below)
   and the whole solution build; `dotnet test` must be green, including the "leaves nothing behind" guard.
2. Commit **directly on `main`** (the owner's decision, November 2026: work on this feature on `main`, no feature
   branch; this overrides the general "branch first" habit for this feature only), **nicely split** (one logical step
   per commit, each commit builds): Conventional Commits (`feat`, `test`, `docs`, `build`, …), detailed body (what, why,
   trade-offs, impact), ending with the `Co-Authored-By` line from the system reminder. A package version change is a
   separate `build` commit. Only `*.md` changes need no build or test run.
3. `git pull --ff-only` before starting (and `git fetch` before pushing; if `main` moved, rebase your own unpushed
   commits on it), then `git push origin main`. On conflicts or red tests after a rebase: stop and report, never
   force-push `main`.
   (The branch `feat/epubcheck-port` was merged into `main` on 2026-11-05 and is no longer used.)

### Conventions (summary of what the repo demands)

- Language: talk to the owner in **Polish with full diacritics**; code, comments, XML docs, commit messages, examples
  and test names in **English**. `CLAUDE.md`/`AGENTS.md` rules apply (no GUI, no screenshots, no asking-by-assuming).
- C# `nullable enable`, `ImplicitUsings disable`, `TreatWarningsAsErrors`. `Signet.EpubCheck.Core` must **not**
  reference `Signet.Core`. Repeated literals become constants (`feedback_constants_not_literals`).
- Tests: xUnit.v3 (`xunit.v3.mtp-off`) + AwesomeAssertions + Moq + AutoFixture; every test has `// Arrange`, `// Act`,
  `// Assert` (or `// Act & Assert`); temporary files only in a `TempDir`; nothing left in `%TEMP%` (each test
  project has its own `TestIsolationSetup.cs`). No `Assert.*` for state, no asserted timings.
- **Obvious upstream bugs and typos are not ported**: fix them and list each in `UPSTREAM.md` ("deliberate
  deviations"). The one exception: the typos in the help text stay (`cli.feature` compares it exactly).
- User-visible texts: English (default) + Polish only, through `.resx` (the messages keep Java's `%1$s` / `%d`
  placeholders). New tool texts go to `ToolMessages` / `CliPortMessages` (Cli) or `PortMessages` (Core) in both
  languages; the Polish text is a draft for the owner's review.
- A case found by the work goes into a corpus book (`tests/corpus/broken/`), not into a note.
- Generated files are regenerated, not edited: `scripts/epubcheck/generate_message_ids.py` (`MessageId.g.cs`,
  `DefaultSeverities.g.cs`, `FeatureKind.g.cs`), `scripts/epubcheck/generate_case_folding.py` (`CaseFolding.g.cs`),
  `scripts/epubcheck/generate_vocabs.py` (`Vocabularies.g.cs`), `scripts/epubcheck/properties_to_resx.py` (the `.resx`
  copies of upstream's `.properties`), `scripts/epubcheck/import_upstream_tests.sh` (`tests/corpus/epubcheck`).

### Where things are

| What | Where |
|---|---|
| Library | `src/Signet.EpubCheck.Core` (`Messages/`, `Reporting/`, `Ocf/`, `Opf/`, `Vocab/`, `Galimatias/` (the URL parser, MIT), `Util/` (incl. `Util/Mime/`), `EpubVersion.cs`, `EpubProfile.cs`, `EpubCheckVersion.cs`) |
| Tool | `src/Signet.EpubCheck.Cli` (assembly `epubcheck.net`; `ToolRun` = upstream's `EpubChecker`; a whole EPUB goes through `OCFChecker`, the single-file modes still say "checker not implemented") |
| Tests | `tests/Signet.EpubCheck.Core.Tests` (1154, incl. ports of upstream's JUnit tests and galimatias' URL data), `tests/Signet.EpubCheck.Cli.Tests` (44), `tests/Signet.EpubCheck.Conformance.Tests` (harness + upstream scenarios: 203 passed, 1084 skipped) |
| Upstream fixtures + `.feature` files | `tests/corpus/epubcheck` (byte for byte, own `.gitattributes`) |
| Gating of upstream scenarios | `tests/Signet.EpubCheck.Conformance.Tests/Harness/ConformanceCorpus.cs`: `EnabledFeatures` (must pass) and `PendingScenarios` (`path:line` → reason). The checker plugs in through `IEpubChecker` (today `UnavailableEpubChecker` → "not implemented" = skip). `SIGNET_CONFORMANCE_ALL=1` runs everything. |
| Upstream source for reading | Not in the repo. Clone it into the scratchpad: `git clone --depth 1 --branch v5.4.0 https://github.com/w3c/epubcheck <scratchpad>/epubcheck-v540` (network is only for this research, never at run time). Sources: `src/main/java/com/adobe/epubcheck/**`, schemas/resources: `src/main/resources/**`. |

Commands (from the repo root):

```
dotnet build Signet.slnx
dotnet test tests/Signet.EpubCheck.Core.Tests
dotnet test tests/Signet.EpubCheck.Cli.Tests
dotnet test tests/Signet.EpubCheck.Conformance.Tests                      # enabled features only
SIGNET_CONFORMANCE_ALL=1 dotnet test tests/Signet.EpubCheck.Conformance.Tests   # all 1251 scenarios, most skipped
```

### Next task: 2.1 RELAX NG model and validator

XML syntax loading, simplification (`include`, `ref`, `externalRef`, `define combine`, `grammar`) and a streaming
derivative-based validator over `XmlReader` with line and column; the error wording follows Jing exactly (see the task
in "Tasks" and "Risks"). Port Jing's behaviour, not a validator of your own design: clone Jing's sources (the version
upstream depends on, `pom.xml`) into the scratchpad next to EPUBCheck's and read them. The schemas themselves are
imported in 2.6; 2.1 is tested on small schemas of its own and on Jing's own test suite where one exists.

### Current state of the conformance run

Enabled features: `cli/cli.feature`, `epub3/04-ocf/ocf.feature`, `epub3/04-ocf/filename-checker.feature`,
`unit-tests/url-fragment.feature`, `epub3/C-viewport-meta-tag/viewport-syntax.feature`: 203 scenarios pass, the
others of these features are pending in `ConformanceCorpus.PendingScenarios`, each with its reason and task id
(the checker of later epics, report writers 4.2, `--save` 3.12, French messages that are never meant to pass,
`PKG-025` of 3.3, references 3.5/3.6, schemas 2.x, `container.xml` 3.1).

### Review of November 2026 (what to keep doing)

On 2026-11-06 tasks 1.1–1.4 were reviewed against upstream and largely redone: the OCF checks diverged from upstream (a
`PKG-025` heuristic written to make a scenario pass, file names checked per segment, case folding by upper-casing,
valid UTF-8 names rejected), the URL parser was not WHATWG/galimatias, and the "utilities" were written from scratch
instead of ported. Lessons, binding for every next task:

- **Port, do not reinvent.** Read the upstream class and port it state by state; where upstream relies on a library
  (galimatias, Jing, Saxon, ICU), port or match that library's behaviour.
- **Use upstream's own tests as the oracle**: the `.feature` scenarios *and* the JUnit tests in `src/test/java` (and
  a library's own tests, like galimatias' URL data). Port them before or with the code.
- **Never tune the code to a scenario** with a heuristic; a scenario that needs a later task goes to `PendingScenarios`.
- **Run the whole solution's tests** at the end (`dotnet test Signet.slnx`), not only the EpubCheck projects: the
  import of the fixtures had broken 41 Signet tests that sweep `tests/corpus` (fixed by `CorpusPaths.EnumerateFiles`).
- No magic numbers (named constants grouped in static classes), detailed commit bodies with real line breaks.

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
| Remote files | Checking a file by `http(s)://` URL (upstream's `URLResourceProvider`) is **not supported, permanently** (owner's decision, November 2026): the tool prints `Checking a remote file is not supported: "<url>"` and exits 1 (the one upstream scenario, `cli.feature:197`, expects 1). Listed in `UPSTREAM.md`. The tool and the library never touch the network. |
| Workflow | One task per "kontynuuj", then stop. After each task: nicely split Conventional Commits **directly on `main`**, push (standing permission; details in "Handoff"). |
| Feature files | Parsed with the official **`Gherkin`** NuGet package (cucumber/gherkin, MIT, netstandard2.0, 42.0.1 at the time of writing) — we write only the step bindings, not a parser. Test-only dependency; its addition is a separate `build` commit. |

## Open items

- **Polish translations** (about 320 messages and the CLI texts) are drafted by the agent and need the owner's review.
- No other open questions: every decision needed to continue is in "Decisions taken" and "Handoff".
- The standalone build is self-contained single-file for `win-x64` and `linux-x64`; trimming off (resources,
  reflection); ReadyToRun optional; Native AOT not planned.

(Resolved in November 2026: version suffix = Signet's scheme; platforms = Linux and Windows x64; schema error text =
exactly Jing's; remote `http(s)` files = never supported; git workflow = commit and push directly on `main` after
every task; no feature branch.)

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
- Conformance suite: **1251 scenarios (outlines expanded) in 50 `.feature` files** (Cucumber), ~3.5k fixture files (~24 ready `.epub`,
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

- **1.1 EPUB source abstraction (M). DONE.** `IEpubSource` has ZIP, expanded-directory and in-memory implementations;
  entries keep name, order, compression method, sizes and time stamp, and open read-only streams. `ZipEpubSource` reads
  only the central directory by hand (order, strict UTF-8 names as Java's `ZipFile(file, UTF_8)`, ZIP64); the
  directory source refuses symbolic links (upstream's `OCFDirectoryResources`).
- **1.2 OCF / ZIP checker (L). DONE.** `OCFChecker` in upstream's order (`OCFZipChecker` on the first 58 bytes, the
  container structure, `mimetype`, `container.xml`), `OCFFilenameChecker` on the whole path with upstream's code point
  sets and ICU's character names, full Unicode case folding (`Util/CaseFolding.g.cs`, Unicode 16.0.0), the resource
  facts of the reports. `ocf.feature` and `filename-checker.feature` are enabled. Not here: parsing `container.xml` and
  `encryption.xml` (3.1), `PKG-025` (3.3, from the manifest).
- **1.3 WHATWG URL parser (M). DONE.** `Galimatias/` ports galimatias 0.1.3, the URL library upstream uses (the URL
  standard of 2014); all 802 cases of its test data pass; IDNA through .NET's `IdnMapping` with ICU's transitional
  mapping. `Util/EpubCheckUrlUtils` ports upstream's `URLUtils`; `EpubUrlFragment` (upstream's `URLFragment`) parses
  fragments by media type; `unit-tests/url-fragment.feature` is enabled. Checking references is 3.5/3.6.
- **1.4 Utilities (M). DONE.** Ports of upstream's `MIMETypeParser` (all 54 cases of its JUnit test), `CodePoints`,
  `ViewportMeta` (`viewport-syntax.feature` enabled, 34 scenarios), `PackageDocumentPeeker` (the 19 cases of
  `OPFPeekerTest` on its fixtures), and the `vocab` package: every vocabulary (generated from the Java enums by
  `scripts/epubcheck/generate_vocabs.py`), `VocabUtil`, `PrefixDeclarationParser` with a `java.net.URI` syntax check
  (`VocabTest`, `PrefixParsingTest`, `PropertyTest` ported).

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
