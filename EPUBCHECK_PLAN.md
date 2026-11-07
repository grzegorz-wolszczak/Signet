# EPUBCHECK_PLAN.md

A C# port of the official [EPUBCheck](https://github.com/w3c/epubcheck) (W3C / DAISY, BSD-3-Clause) that
lives in this repository as a standalone command-line tool, with its checking core in a separate library
that Signet itself uses for its "Validation Results".

Status: **in progress — epics 0 and 1 are done** (project skeleton, version, upstream pin, message and report model,
message texts in English and Polish, conformance harness, command line, EPUB sources and OCF/ZIP checks, galimatias URL
parser and URL fragments, MIME types, viewport, package-document peeker, vocabularies); tasks 1.1–1.4 were reviewed and
re-ported on 2026-11-06 (see "Review of November 2026"); epic 2: **2.1 (RELAX NG, a port of Jing), 2.2 (its compact
syntax), 2.3 (its XML Schema datatypes and regular expressions), 2.4 (NVDL), 2.5 (Schematron with an XPath 2.0 subset), 2.6
(upstream's schemas embedded and loaded) and 2.7 (schema errors as EPUBCheck's messages) are done — epic 2 is done**;
epic 3: **3.1 (the files of `META-INF`), 3.2 (OPF 2), 3.3 (OPF 3), 3.4 (NCX and the registries) and 3.5 (content documents with the navigation document) 3.6 (references and the resource graph) and 3.7 (CSS) and 3.8 (media and fonts), 3.9 (media overlays), 3.10 (scripting and accessibility) and 3.11 (profiles and extensions) are done**; **next: task 3.12 single-file and expanded modes**.
Research done in November 2026 against the upstream
`main` (5.4.1-SNAPSHOT, after the 5.4.0 release). The port targets the **stable 5.4.0** (tag `v5.4.0`).

## Handoff — read this first (for the next agent)

The owner types **"kontynuuj"** (continue the work on EPUBCHECK). Do exactly this, **without asking questions**
(everything below is already decided; ask only when something truly new and unspecified comes up, and then collect all
questions into one message, as `AGENTS.md` demands).

### What to do on "kontynuuj"

1. Read this file, `AGENTS.md`, `src/Signet.EpubCheck.Core/UPSTREAM.md` and the memory index.
2. Take the **first task in "Tasks" that is not marked DONE** (today: **3.12 Single-file and expanded modes**, see
   "Next task" below). Do **one task**, then stop. Do not start the next one.
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
   force-push `main` (the one exception so far: on 2026-11-06 the owner asked to rewrite four commit messages that held a
   literal `\n`; `main` was force-pushed with the same trees).
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
| Library | `src/Signet.EpubCheck.Core` (`Messages/`, `Reporting/`, `Ocf/`, `Opf/`, `Ops/` (content documents), `Nav/`, `Dtbook/`, `Css/` (the seam of the CSS checker), `Ncx/`, `References/`, `Vocab/`, `Galimatias/` (the URL parser, MIT), `Jing/` (the RELAX NG engine, a port of Jing, BSD-3), `Util/` (incl. `Util/Mime/`), `EpubVersion.cs`, `EpubProfile.cs`, `EpubCheckVersion.cs`) |
| Tool | `src/Signet.EpubCheck.Cli` (assembly `epubcheck.net`; `ToolRun` = upstream's `EpubChecker`; a whole EPUB goes through `OCFChecker`, the single-file modes still say "checker not implemented") |
| Tests | `tests/Signet.EpubCheck.Core.Tests` (2972 passed, 7 skipped — the NVDL cases with MNS or W3C XML Schema and the Schematron cases with `id()` or `document()`, incl. ports of upstream's JUnit tests, galimatias' URL data and Jing's test suites in `Jing/TestData`: `spectest.xml`, `compacttest.xml`, `regextest.xml`, `hardtest.xml`, `xsdtest.xml`, `nvdltest.xml`, `schematrontest.xml`), `tests/Signet.EpubCheck.Cli.Tests` (44), `tests/Signet.EpubCheck.Conformance.Tests` (harness + upstream scenarios: 1222 passed, 65 skipped) |
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

### Next task: 3.12 Single-file and expanded modes

`ToolRun.ProcessFile` and the single-document modes of the command line (`--mode xhtml|svg|opf|nav|mo|exp`, `--save`) still answer
"not implemented". Wire `OCFChecker` / `CheckerFactory` into them; the pending scenarios of `cli.feature` (155, 166, 172, 187, 205, 230, 234,
241, 248, 259, 395) wait for it, and 205 needs the EPUB archive built from an expanded EPUB.

### Current state of the conformance run

47 features are enabled (`ConformanceCorpus.EnabledFeatures`): the command line, OCF, filename checker, URL fragments, viewport syntax,
the package documents of EPUB 2 and 3, the NCX, the content documents (XHTML, SVG, fixed layout, the navigation document, EDUPUB,
indexes), the vocabularies, the obsolete features and the profiles' package documents and publications. 1229 scenarios pass; the
others of these features are pending in `ConformanceCorpus.PendingScenarios`, each with its reason and task id (report writers 4.2, `--save`
3.12, French messages that are never meant to pass). Not enabled yet:
`localization` and `reporting` (4.2). Single documents the harness checks: package documents, XHTML, SVG
and navigation documents (`OcfEpubChecker`); `mo` (media overlays) too. The test run uses English texts (`TestIsolationSetup` sets
`DefaultThreadCurrentUICulture`), so a Polish machine passes too.

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

- **2.1 RELAX NG model and validator (XL). DONE.** `Jing/` ports Jing 20181222 (the version upstream depends on):
  the XML syntax parser with `include`, `externalRef`, `grammar`, `parentRef` and `define`/`start` combining
  (`Parse/`), the schema builder with the restrictions of section 7 (`Patterns/SchemaBuilderImpl`, `Restrictions`),
  the pattern model with its memo, derivatives and Jing's error recovery (`PatternMatcher`), the builtin datatypes, and
  a validator fed by `XmlReader` with line and column (`Sax/XmlEventReader`, `PatternValidator`). Public entry points:
  `RelaxNgSchemaReader.CreateSchema` → `RelaxNgSchema.CreateValidator` (`RelaxNgSchema.cs`). The messages are Jing's
  bundles word for word (`Resources/*.resx`, Polish drafts beside them). Tested on Jing's own suite (`spectest.xml`,
  385 cases: 376 pass, the 9 with XSD datatypes are skipped until 2.3), ports of `PatternMatcherTest` and
  `NameClassNormalizerTest`, and 22 tests of the wording. Deliberately not here: ID/IDREF checks and the feasible mode
  (upstream does not use them), the compact syntax (2.2), XSD datatypes (2.3), mapping to `RSC-005` (2.7). The
  deviations from Jing are in `UPSTREAM.md`.
- **2.2 Compact syntax `.rnc` parser (M). DONE.** `Jing/Parse/Compact/` ports Jing's `CompactSyntax.jj` (JavaCC) by
  hand: `CompactCharStream` (Jing's edited `JavaCharStream`: line ends, `\x{…}` escapes, characters XML forbids, Jing's
  line and column with tabs to the next multiple of 8, decoded lazily so errors come in Jing's order), `CompactLexer`
  (the token manager with its three lexical states; comments are skipped, documentation lines are tokens),
  `CompactSyntax` (the productions one by one, including the lookahead that tells a grammar from a pattern at the top
  level) and `CompactParseable` (UTF-8 unless a UTF-16 byte order mark). `RelaxNgSchemaReader.Syntax` chooses the
  syntax (EPUBCheck: `.rnc` → compact); included documents are read in the same syntax. Messages: Jing's bundle
  (`Resources/CompactMessages.resx`, Polish draft). Tested on Jing's `compacttest.xml` (87 cases: 80 pass — a correct
  case must give the same errors as its XML counterpart and the same simplified pattern; 7 with XSD datatypes wait for
  2.3) and on positions, escapes, messages and validation with a compact schema. The plan's "equivalence with the `.rng`
  counterparts" does not apply: upstream's `.rnc` (EPUB 3, `schema/30`) and `.rng` (EPUB 2, `schema/20`) are different
  schemas. Instead all 129 `.rnc` of upstream were loaded once (with a stand-in XSD library): the 25 top-level schemas
  load without errors, the 104 modules report only what a fragment lacks (undefined references, no `start`), and the one
  syntax error is `mod/html5/web-forms2-scripting.rnc`, a file of comments that nothing includes (Jing fails on it the
  same way).
- **2.3 Datatypes (M). DONE.** `Jing/Datatype/Xsd/` ports Jing's `xsd-datatype` module: every datatype of
  `DatatypeLibraryImpl` (string types, decimal and the integer types on `JavaBigDecimal`, which keeps Java's scale,
  double/float, names, QName, ID/IDREF/ENTITY, lists, binary types, anyURI, the date/time types with the field limits of
  Jing's non-lenient `GregorianCalendar`, duration with its four reference months), the facets of `DatatypeBuilderImpl`
  with their checks and messages, and `RegularExpressions/`, a port of Jing's java `Translator` (XSD regex → here .NET
  regex): the same parser, errors and positions; the character classes are computed as code point sets and printed with
  surrogate pairs, so .NET matches by code point as `java.util.regex` does (categories and blocks from .NET's Unicode
  data). It is the default `RelaxNgSchemaReader.DatatypeLibraryFactory`. Messages: Jing's two bundles
  (`XsdDatatypeMessages.resx`, `RegexMessages.resx`, Polish drafts); numbers in messages are grouped as `MessageFormat`
  does. `XmlEventReader` now reports the notations and unparsed entities of the internal DTD subset (for `ENTITY`).
  Tested on Jing's suites: all 385 cases of `spectest.xml` and 87 of `compacttest.xml` now run (none skipped),
  `regextest.xml` (48) and `hardtest.xml` (4) as Jing's regex `TestDriver` runs them, `xsdtest.xml` turned into
  schemas as Jing's `xsdtest.xsl` does (240 cases: literals, classes of equal values, lengths, order), a port of
  `DatatypeLibraryImplTest`, and message tests. All 129 `.rnc` of upstream load with the real library as they did with
  the stand-in of 2.2. Deviations from Jing are listed in `UPSTREAM.md`.
- **2.4 NVDL (L). DONE.** `Jing/Validate/Nvdl/` ports Jing's NVDL implementation in full (not only the constructs
  upstream's 10 files use: they use modes, `namespace`/`anyNamespace` with `match`, `validate` with `schemaType`,
  `context` paths, `useMode`, `attach`, `allow`, `reject`; the port also has `unwrap`, `attachPlaceholder`, `trigger`,
  `cancelNestedActions`, included and nested modes, wildcards): the schema reader validates the NVDL document with Jing's
  `nvdl.rng` (embedded) while it builds the modes, and the validator cuts the document into sections and dispatches them
  to the sub-validators, attributes filtered by namespace. `Jing/Validate/Validation.cs` brings Jing's validate layer:
  `ISchema`/`IValidator` (RELAX NG schemas implement them) and `AutoSchemaReader`, which picks the language from the
  root element (RELAX NG, NVDL; Schematron registers itself in 2.5). A `.rnc` media type gives a compact schema, as in
  Jing. Messages: Jing's NVDL and auto-reader bundles with Polish drafts. Tested on Jing's `nvdltest.xml` (12 cases: the
  8 without MNS or W3C XML Schema pass, those 4 are skipped: EPUBCheck uses neither) and message tests. Of upstream's
  10 `.nvdl`, the 2 of EPUB 2 and `epub-svg-30-informative.nvdl` loaded and validated after 2.4; since 2.5 all 10 do. Not ported: NVDL options (Jing's only ones are its RELAX NG reader's `check-id-idref` and `feasible`;
  an option that must be supported is reported as unknown), MNS and NRL (Jing's older languages).
- **2.5 Schematron + XPath 2.0 subset (XL). DONE.** Jing runs Schematron by compiling the schema to XSLT
  (`iso-schematron.xsl`, `schematron.xsl`) and running it in Saxon; the port has no XSLT 2.0 engine, so
  `Jing/Validate/Schematron/` does what the compiled stylesheet does: the schema is validated with Jing's
  `iso-schematron.rnc` / `schematron.rnc` (ISO and 1.5 namespaces), checked as Jing's stylesheet checks it (default
  phase, active patterns, extended rules, cycles, diagnostics), then each document is built as a tree and every element
  visited in document order, the first matching rule of each pattern firing; lets (schema, phase and pattern lets
  global, rule lets local), phases, abstract rules and `extends`, `name` and `value-of`; the messages are written as
  Jing's `OutputHandler` writes them (`assertion failed:` / `report:`, then the statement indented on a new line, white
  space collapsed) at the line of the node. `XPath/` is the evaluator: a parser and an evaluator of the XPath 2.0 subset
  the 29 files use (all axes but namespace, predicates, filter steps, `if`, `some`/`every`/`for`, comparisons, `is`,
  `union`/`intersect`/`except`, ~40 functions, XSLT's `current()`), in XPath 1.0 compatibility mode as Saxon runs
  Jing's `version="1.0"` stylesheet, and `XsltPattern` for rule contexts. Registered with `AutoSchemaReader`.
  Messages: Jing's bundle with Polish drafts, plus two keys of the port for XPath errors. Tested on Jing's
  `schematrontest.xml` (20 cases: 17 pass, 3 need `id()` or `document()`, which EPUBCheck does not use), message and
  XPath tests; checked once on all of upstream's schemas after the build's include/abstract expansion: every `.sch` and
  `.nvdl` loads, and sample documents give the texts upstream's scenarios expect (`Exactly one manifest item must
  declare the "nav" property`, `does not resolve to a dc:identifier element`). Not here: the expansion itself (2.6),
  mapping to messages (2.7).
- **2.6 Schema catalog and loading (S). DONE.** `scripts/epubcheck/import_upstream_schemas.sh` copies upstream's
  `schema/` (279 files with their licenses and `epub30-catalog.xml`) and the two stylesheets of its build
  (`src/main/util/sch`) into `src/Signet.EpubCheck.Core/Xml/Schemas`, embedded under their paths. `Xml/SchemaResources`
  serves them as `resource:/schema/…` URIs with a resolver for the references between them, and runs
  `iso-sch-include.xsl` then `iso-sch-abstract.xsl` (`XslCompiledTransform`) on the nine `schema/30/*-30.sch` files the
  first time one is opened, as upstream's build does. `Xml/XmlValidator` (upstream's `XMLValidator`: `.rnc` in compact
  syntax, anything else through `AutoSchemaReader`) and `Xml/XmlValidators` (upstream's enum, 50 schemas, each read
  once on first use and shared). Tests: every schema loads; the preprocessing; validation with upstream's messages
  (`nav` assertion, an included pattern, a RELAX NG error); parallel validation. Start-up cost:
  `tests/Signet.Benchmarks/SchemaLoadBenchmarks` and `BASELINE-EPUBCHECK.md` (all 50 schemas: ~0.66 s on first use,
  ~0.34 s warm, Release). Not done, deliberately: the catalog is embedded but not read; upstream does not read it
  either (every reference between the schemas is relative).
- **2.7 Mapping to messages (M). DONE.** `Xml/ReportingErrorHandler` ports upstream's `ReportingErrorHandler`: a warning
  is `RSC-017` (`RSC-024` for an informative schema), an error `RSC-005` (`RSC-025`), or `RSC-017` / `RSC-024` when its
  message starts with `WARNING:` and `RSC-036` when with `USAGE:` (prefix and space dropped), a fatal error `RSC-016`;
  always at the line and column of the error in the checked file. `XmlValidator.CreateValidator(report, location,
  culture)` is what `XMLParser.addValidator` makes. EPUBCheck overrides Jing's Schematron bundle (`report=` and
  `failed_assertion=` empty, `src/main/resources/com/thaiopensource/validate/schematron/resources`), so its Schematron
  messages are the statement alone and the prefixes reach the handler: the port's Jing got the option
  `SchematronStatementsOnly` (`AutoSchemaReader`, `NvdlSchemaReader`, passed to the schemas an NVDL schema uses), set by
  `XmlValidator`; Jing's own behaviour stays the default. The other bundles EPUBCheck overrides (`relaxng/pattern`,
  `datatype/xsd`) have the same English texts as Jing's. Tests: the mapping, and the fixtures of the upstream scenarios
  whose messages come from the schemas alone, checked with the validators upstream's checkers use for them (guide
  duplicates `RSC-017`, nested `page-list` `RSC-017`, the four obsolete attributes `RSC-036`, SVG errors against the
  informative schema `RSC-025`). Not done, deliberately: the rest of `XMLParser` (encoding sniffing, `RSC-027`/`RSC-028`/
  `HTM-058`, `DeclarationHandler`, `PreprocessingDefaultHandler`, `DefaultResolver`, the parser's own errors) needs
  upstream's `ValidationContext` and comes with the first checker that parses a file, task 3.1.

### Epic 3 — Publication rules

- **3.1 `container.xml`, `encryption.xml`, `metadata.xml`, signatures (M). DONE.** The XML layer and the OCF flow of
  upstream's `OCFChecker` up to the package documents. `Xml/XmlParser` (upstream's `XMLParser`: encoding sniffing with
  `XMLEncodingSniffer` and `HTM-058`/`RSC-027`/`RSC-028`, the UTF-8 BOM, handlers and validators fed from one parse, errors
  through `ReportingErrorHandler`, `PKG-008`, `RSC-016`), `PreprocessingHandler` (upstream's `PreprocessingDefaultHandler`:
  custom elements, `data-*` and custom-namespace attributes, `HTM-061`, `HTM-054`), `XmlHandler` (the base of the content
  handlers: `XMLElement` chain, `xml:base`/`base`, locations, `HTM-001`), `HTMLUtils`, `References/URLChecker` (`RSC-020`,
  `RSC-026`, `RSC-030`, `RSC-033`), `Opf/ValidationContext` (a record; `with` is the builder), `Ocf/OCFContainer` (files by
  URL under a root URL unique to the check, encryption), `OCFCheckerState`, `OCFMetaFile`. `OCFChecker` now: reads
  `container.xml` (`OPF-016`, `OPF-017`, mapping document), `RSC-003`, `OPF-002`, peeks at each package document
  (`OPF-001`, `RSC-016`, renditions count), checks version (`PKG-001`) and profile (`PKG-023`, `OPF-064`), `PKG-013`,
  `RSC-019`, validates `encryption.xml` (with the schemas; `RSC-007`, `OBS-001`, encrypted and obfuscated resources),
  `container.xml` (again, with the schemas), `metadata.xml` and `signatures.xml`, and the file extension (`PKG-016`,
  `PKG-017`, `PKG-024`). Six scenarios of `ocf.feature` pass now (container, encryption, signatures, missing package
  document); the one that needs `RSC-004` waits for the resource checks. Not done, deliberately: the declaration handler of
  the parser (task 3.2), the mapping document of multiple renditions (task 3.11), the checks after the package documents
  (3.2/3.3), `PKG-026`.
- **3.2 OPF 2 (L). DONE.** `Opf/OPFChecker` (upstream's `OPFChecker`, the EPUB 2 checker, with the shared parts), `OPFHandler`
  (items, spine, guide, unique identifier, Dublin Core: `OPF-030`, `OPF-047`–`OPF-055`, `OPF-062`/`OPF-063`, `OPF-072`,
  `OPF-085`, facts), `OPFItem` / `OPFItems` / `OPFReference`, `FallbackChainResolver` (`OPF-040`, `OPF-041`, `OPF-045`),
  `OpfMediaTypes` (the blessed and core media types), `Util/DateParser` (with the error texts of Java's calendar). Checks:
  manifest items (`OPF-035`–`OPF-039`, `OPF-074`, `OPF-099`), spine (`OPF-033`, `OPF-034`, `OPF-042`–`OPF-044`), guide
  (`OPF-031`, `OPF-032`), `PKG-020`, items count and declared media types. `Xml/DeclarationHandler` (DOCTYPE checks `HTM-004`,
  `HTM-009`, `OPF-073`, external entities `HTM-003`) with the DOCTYPE and entity events of `XmlEventReader`.
  `OCFChecker` runs the package documents of EPUB 2 and then the consistency of the container (`OPF-003`, `PKG-026`). The
  conformance harness checks a single package document (EPUB 2) and no longer gives the version in EPUB mode (upstream's
  `EpubCheck(file, report, profile)` does not); `epub2/ocf-publication`, `opf-package-document` and `opf-publication` are
  enabled. Not done, deliberately: the resource and reference registries and the checks of the resources an item declares
  (`CheckItemContent`: NCX 3.4, content documents 3.5, `RSC-007`/`RSC-032` and the reference checker 3.6); six scenarios of
  `opf-publication.feature` wait for them.
- **3.3 OPF 3 (XL). DONE.** `Opf/OPFChecker30` (schemas of EPUB 3 and of the profile or publication type; items `RSC-029`,
  `PKG-025`, `OPF-090`, `OPF-091`; spine items `OPF-043`, `OPF-044`, `OPF-077`, `OPF-100`; linked resources `OPF-067`;
  collections: dictionary, index, preview `OPF-071`, `OPF-075`, `OPF-076`, `OPF-081`–`OPF-084`) and `OPFHandler30` (the vocabularies
  of the `prefix` attribute, the properties of items, itemrefs, links and `meta`, `OPF-012`, `OPF-065`, `OPF-070`, `OPF-086`,
  `OPF-089`, `OPF-092`–`OPF-095`, `OPF-098`, `OBS-001`, the rendition facts). New model: `MetadataSet` (with refinements and
  cycle detection), `LinkedResource(s)`, `CollectionNode` / `ResourceCollections` (upstream's `ResourceCollection`; renamed because
  .NET reserves the suffix), `Util/LanguageTag` (BCP 47 well-formedness with Java's error texts), `Reporting/QuietReport`.
  `OCFChecker` runs EPUB 3 package documents too and the container consistency counts linked resources. Conformance:
  `epub3/05-package-document/package-document.feature` is enabled (the scenarios that need content document properties and the
  reference checks wait: 25). Not done, deliberately: the checks that use the features content documents report
  (`NAV-003`–`NAV-008`, `OPF-066`, `OPF-078`, `OPF-079`, `HTM-051`: tasks 3.5 and 3.11), the total duration of media overlays
  (`MED-016`, `SmilClock`: task 3.9), the checks after the resources were validated (`RSC-006`, `OPF-096`, `OPF-097`,
  `MED-010`–`MED-013`: task 3.6); `featureReport` and `registerReference` are not ported yet.
- **3.4 Navigation (M). DONE, narrowed.** The NCX and the infrastructure every checker of a resource needs: `Ncx/NCXChecker` and
  `NCXHandler` (`NCX-001`, `NCX-004`, `NCX-006`, `OBS-001` in EPUB 3, `ncx.rng/sch`, the references of `content`);
  `References/` — `ReferenceType`, `Reference`, `ReferenceRegistry`, `Resource`, `ResourceRegistry` (upstream's
  `org.w3c.epubcheck.core.references` without `ResourceReferencesChecker`); `Reporting/FeatureReport`; `Opf/PublicationResourceChecker`
  (`RSC-001`, `RSC-004`, with upstream's quirky control flow), `IChecker` and `CheckerFactory`. `ValidationContext` has the
  registries and the feature report (`WithContainer` makes new registries, as upstream's builder does). `OPFChecker.CheckPackage`
  registers the resources and checks each item with its checker (`CheckItemContent`; a checker that cannot be made for the item
  is `CHK-008`); the handlers register the references of the guide, of `link` and of media overlays, and the features
  `UNIQUE_IDENT` and the media overlay classes. `epub2/ncx-publication.feature` is enabled and `opf-publication.feature:117` passes
  (`CHK-008`). The navigation document (`NavChecker`, `NavHandler`, `NAV-*`) moved to 3.5: `NavHandler` extends `OPSHandler30`.
  Not done, deliberately: `ResourceReferencesChecker` (task 3.6), so `RSC-007`, `RSC-008`, `RSC-010`, `RSC-032`… wait for it.
- **3.5 Content documents (XL). DONE.** `Ops/OPSChecker` (the schemas of each case through `Xml/ValidatorMap` and its
  `ValidationContextPredicates`: XHTML and SVG of EPUB 2 and 3, the informative SVG schema, dictionaries, EDUPUB, indexes, data
  navigation), `OPSHandler` (references of links, images, objects, style sheets, scripts, SVG paints, clip paths, symbols and fonts,
  `xml-stylesheet` of SVG, ids, `style` text handed to the CSS checker, the accessibility of tables and `epub:type`: `HTM-025`,
  `HTM-045`, `ACC-005`–`ACC-007`, `ACC-012`) and `OPSHandler30` (`epub:type` and the `prefix` attribute with the structural and
  other vocabularies, `OPF-088`, `HTM-052`; the properties a document needs, `OPF-014`, `OPF-015`, `OPF-018`; images with `srcset`
  and `picture`, `MED-003`, `MED-007`, `OPF-013`; audio and video sources with their fallbacks; objects with palpable-content
  fallbacks; the viewport of fixed-layout documents, `HTM-046`–`HTM-048`, `HTM-056`, `HTM-057`, `HTM-059`, `HTM-060a/b`; `HTM-010`,
  `HTM-055`, `HTM-062`, `HTM-063`, `CSS-005`, `CSS-015`, `CSS-030`, `ACC-004`, `ACC-009`, `ACC-011`; the features `HAS_SCRIPTS`,
  `PAGE_BREAK`, `INDEX`, `DICTIONARY`, `TABLE`, `FIGURE`, `AUDIO`, `VIDEO`, `SECTIONS`, `HAS_MICRODATA`, `HAS_RDFA`),
  `Nav/NavChecker` and `NavHandler` (`NAV-001`, `NAV-010`, the kinds of nav, `TOC_LINKS`, `PAGE_LIST`, `LOI`/`LOT`/`LOA`/`LOV`, the
  `NavTocLink` and `NavPagelistLink` references), `Dtbook/DTBookChecker` and `DTBookHandler`, `Css/CSSChecker` (only the seam, see
  3.7), `Util/SourceSet` (with upstream's test), `Util/UriSchemes`, `Util/ProcInstParser`, `ValidationContext.GetMimeType`.
  `CheckerFactory` gives each media type its checker and `OPFChecker.CheckItemContent` the navigation document its `NavChecker`.
  `OPFChecker30.CheckPackage` now runs the checks that use the features the content documents report: `NAV-003`–`NAV-008`,
  `OPF-066`, `OPF-078`, `OPF-079`, `HTM-051`. The XML layer got what content documents need: an XML 1.1 declaration is read (as
  XML 1.0; `HTM-001`) and the two well-formedness errors that scenarios check are worded as Xerces words them. The conformance
  harness checks single XHTML, SVG and navigation documents; 33 features were enabled (see "Current state"). Not done, deliberately:
  the references checker (3.6), so the pending scenarios list `RSC-006`–`RSC-015` and the like; the CSS checker (3.7).
- **3.6 References and resource graph (L). DONE.** `References/ResourceReferencesChecker` (upstream's class: `RSC-006`, `RSC-007`,
  `RSC-007w`, `RSC-008` once per resource, `RSC-009`–`RSC-015`, `RSC-021`, `RSC-031`, `RSC-032`, `RSC-034`, `RSC-035`, `OBS-001`
  for a manifest fallback, `NAV-009`, `NAV-011`, `MED-015`, `MED-017`, `MED-018`, the `resource` fact) run at the end of
  `OPFChecker.CheckPackage`, and `OPFChecker30.CheckItemAfterResourceValidation` (`RSC-006`/`RSC-006b` for a remote item nothing
  references, `OPF-097`, `OPF-096`/`OPF-096b`). The media overlay attribute checks of that method (`MED-010`, `MED-012`, `MED-013`)
  need the overlay text checker and come with 3.9. `epub3/03-resources/resources.feature` is enabled; 1114 conformance scenarios
  pass, the pending ones wait for CSS (3.7), images and audio (3.8), media overlays (3.9) and the profiles (3.11).
- **3.7 CSS (L). DONE.** A port of upstream's own CSS parser (`Css/`: `CssScanner`, `CssReader`, `CssEscape`, `CssToken`, `CssTokenList`,
  `CssGrammar` with its constructs and the selector and value factories, `CssParser`, `CssSource`, the error codes and messages in
  `CssMessages.resx`) and of `CSSChecker` / `CSSHandler`: the errors of the parser (`CSS-008`), the references of `@import`, `url()`
  and fonts registered for the references checker, the fonts a style sheet declares (`CSS-007`, `CSS-019`, `CSS-028`, the font
  facts), the outdated `-epub-` properties (`OBS-001`), `CSS-001`, `CSS-002`, `CSS-006`, `CSS-029`, the item properties a style
  needs (`OPF-014`, `OPF-018`), and a style sheet that is not UTF-8 (`CSS-003`, `CSS-004`). It checks style sheets, `style` elements
  and `style` attributes. `epub3/07-content-documents/content-documents-css.feature` is enabled and every CSS scenario of the other
  features passes: 1148 conformance scenarios pass. The tests are upstream's scanner tests converted (129), a selection of the parser
  tests, the input stream tests and tests of the checker.
- **3.8 Media and fonts (M). DONE.** `Media/BitmapChecker` (upstream's, for `image/gif`, `image/jpeg`, `image/png`): the header against the declared
  type (`MED-004`, `OPF-029`), the format of the file against its extension (`PKG-022`), unreadable images (`PKG-021`), images over
  3840×2160 (`OPF-051`) or 4 MB (`OPF-057`). Upstream reads the image with Java's ImageIO; the port tells the format from the first
  bytes and reads the size from the PNG, GIF, JPEG and BMP headers (no native dependency), with the extension lists of ImageIO's
  readers. The checks of fonts and of the media types of the manifest were done with the package document (3.2, 3.3) and CSS (3.7).
  The three pending scenarios of `resources.feature` pass: 1151 conformance scenarios.
- **3.9 Media Overlays (M). DONE.** `Overlay/`: `SmilClock` (the three syntaxes of SMIL clock values, tolerance, sums; with `ClipTime`),
  `OverlayTextChecker` (which overlay refers to each content document, in `ValidationContext`), `OverlayChecker` (`mo30.rnc` and `.sch`)
  and `OverlayHandler` (`text`/`seq`/`body` links registered as `OverlayTextLink`, audio registered with an intrinsic fallback,
  `MED-005`, `MED-008`, `MED-009`, `MED-011`, `MED-014`, `OPF-088`, `OPF-014` for remote audio). `OPFChecker30` now checks the media
  overlay attributes of the items (`MED-010`, `MED-012`, `MED-013`) and the total duration (`MED-016`, within a second). The harness has
  the `mo` single-document mode. `09-media-overlays` and `vocabularies-association` are enabled (their `.smil` examples included); the
  fixture `content_001.mo` that `.gitignore`'s `*.mo` had kept out of the repository is added back (`!tests/corpus/epubcheck/**/*.mo`).
  1222 conformance scenarios pass.
- **3.10 Scripting and accessibility (M). DONE with nothing new to port.** The `SCP-*` messages and most `ACC-*` come from upstream's
  Schematron schemas, which run since epic 2 (`epub-xhtml-30.sch` and the rest); the `ACC-004`–`ACC-012` hints that Java reports are in
  `OPSHandler` / `OPSHandler30` (3.5) and the scripted/properties checks in 3.5 and 3.7. `epub-accessibility` and
  `epub-scriptable-components` pass, and so do the script scenarios of the content documents.
- **3.11 Profiles and extensions (L). DONE.** `dict`, `edupub`, `idx`, `preview`, data navigation, distributable objects and
  scriptable components were schemas, loaded since epic 2. What was left in Java is ported: `Dict/SearchKeyMapChecker` (`OPF-080` when the
  file name does not end in `.xml`; `search-key-map.rnc`) and `SearchKeyMapHandler` (the `search-key-group` and `match` hrefs are
  registered as `ReferenceType.SearchKey`), given by `CheckerFactory` to search key maps in EPUB 3; `Ocf/MappingDocumentChecker` (the
  mapping document of multiple renditions, validated with `rendition-mapping.rnc` and `.sch` when the context has the
  `epubcheck:rendition-mapping` property), which `OCFChecker.CheckRenditions` runs. `epub-dictionaries` and `epub-multiple-renditions`
  are fully enabled. 1229 conformance scenarios pass.
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
