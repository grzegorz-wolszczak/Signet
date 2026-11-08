# EPUBCHECK_PLAN.md

C# port of the W3C [EPUBCheck](https://github.com/w3c/epubcheck) (BSD-3-Clause), pinned to **stable 5.4.0** (tag
`v5.4.0`). A standalone CLI (`epubcheck.net`) plus a library that Signet uses for Check Book.

**Status: every task of the plan is done** (epics 0–6). Per-task detail is in git history; what comes from upstream and
every deliberate deviation is in `src/Signet.EpubCheck.Core/UPSTREAM.md`.

## Working on it ("kontynuuj")

1. Read this file, `AGENTS.md`, `src/Signet.EpubCheck.Core/UPSTREAM.md`, the memory index.
2. No open task. Read "After the plan" and ask the owner what to do; start nothing unasked.
3. Update this file as you go.
4. End with the git routine, then report in Polish in a few lines.

**Git routine** (standing permission): fix warnings last; run all EpubCheck test projects, build the whole solution
and run `dotnet test Signet.slnx` (green, including the "leaves nothing behind" guard). Commit **directly on `main`**
(owner's decision for this feature), one logical step per commit, each building; Conventional Commits with detailed
body and the `Co-Authored-By` line; package changes in a separate `build` commit; `*.md`-only changes need no
build/test. `git pull --ff-only` first, `git fetch` before pushing (rebase own unpushed commits); on conflicts or red
tests stop and report; never force-push `main`.

## Conventions

- Talk to the owner in Polish; code, comments, commits, tests in English. `AGENTS.md` rules apply.
- `Signet.EpubCheck.Core` must **not** reference `Signet.Core`. Repeated literals become named constants.
- **Obvious upstream bugs/typos are not ported**: fix and list each in `UPSTREAM.md` (exception: typos in the help
  text stay; `cli.feature` compares it exactly).
- User texts: English (default) and Polish only, via `.resx` (Java's `%1$s`/`%d` placeholders kept). Tool texts go to
  `ToolMessages`/`CliPortMessages` (Cli) or `PortMessages` (Core). Polish is a draft for the owner's review.
- Cases found go into corpus books (`tests/corpus/broken/`), not notes.
- Generated files are regenerated, not edited (`scripts/epubcheck/`): `generate_message_ids.py`
  (`MessageId.g.cs`, `DefaultSeverities.g.cs`, `FeatureKind.g.cs`), `generate_case_folding.py`, `generate_vocabs.py`,
  `properties_to_resx.py`, `import_upstream_tests.sh` (`tests/corpus/epubcheck`), `import_upstream_schemas.sh`,
  `publish.sh`.

**Lessons (binding):**
- Port, don't reinvent: read the upstream class, port it state by state; match library behaviour (galimatias, Jing,
  Saxon, ICU) where upstream relies on one.
- Upstream's tests (`.feature` scenarios and JUnit) are the oracle; port them with the code.
- Never tune code to a scenario with a heuristic; a scenario needing a later task goes to `PendingScenarios`.
- Run the whole solution's tests at the end (fixture import once broke 41 Signet tests that sweep `tests/corpus`).

## Where things are

| What | Where |
|---|---|
| Library | `src/Signet.EpubCheck.Core` (`Messages/ Reporting/ Ocf/ Opf/ Ops/ Nav/ Dtbook/ Css/ Ncx/ References/ Vocab/ Galimatias/ Jing/ Media/ Overlay/ Dict/ Xml/ Util/`) |
| Tool | `src/Signet.EpubCheck.Cli` (`ToolRun` = upstream's `EpubChecker`) |
| Tests | `tests/Signet.EpubCheck.{Core,Cli,Conformance}.Tests` |
| Upstream fixtures + `.feature` | `tests/corpus/epubcheck` (byte for byte) |
| Scenario gating | `Conformance.Tests/Harness/ConformanceCorpus.cs`: `EnabledFeatures`, `PendingScenarios` (`path:line` → reason); `SIGNET_CONFORMANCE_ALL=1` runs everything |
| Upstream source | not in repo; `git clone --depth 1 --branch v5.4.0 https://github.com/w3c/epubcheck <scratchpad>/epubcheck-v540` (research only; never network at run time) |
| Conformance numbers | `tests/Signet.EpubCheck.Conformance.Tests/CONFORMANCE.md` (1245 of 1251 pass; 6 need French messages) |
| Benchmarks | `tests/Signet.Benchmarks/BASELINE-EPUBCHECK.md` |

```
dotnet build Signet.slnx
dotnet test tests/Signet.EpubCheck.Core.Tests
dotnet test tests/Signet.EpubCheck.Cli.Tests
dotnet test tests/Signet.EpubCheck.Conformance.Tests
```

## Decisions

| Topic | Decision |
|---|---|
| Schema engine | Own RELAX NG + NVDL + Schematron/XPath 2.0 engine in C# reading the original upstream schemas. No Java/IKVM. |
| Projects | `Signet.EpubCheck.Core` (library), `Signet.EpubCheck.Cli` (tool); `Signet.Core`/`App` depend on Core, not the reverse. |
| Tool | `epubcheck.net`, standalone self-contained single-file executable (not a `dotnet tool`), `win-x64` and `linux-x64`. |
| Flags | Identical to Java EPUBCheck 5.4.0; hand-written argument parser. Exit code 0/1. |
| Version | `5.4.0-<yyyyMMddHHmm UTC>`; `--version` prints it; harness substitutes `{{VERSION}}`. |
| Schema errors | Worded exactly as Jing (`RSC-005`). |
| Message ids | Identical to upstream `MessageId` and default severities. |
| Languages | English and Polish only. |
| Scope | EPUB 2 and 3, all profiles, modes and report formats. |
| Signet | Check Book runs on the library; Signet-only rules keep `Validation_…` codes; fixes attach to message ids. |
| Remote files | `http(s)://` input is permanently unsupported: prints `Checking a remote file is not supported: "<url>"`, exit 1. No network access ever. |
| Workflow | Commits and push directly on `main`. |
| Feature files | Parsed with the `Gherkin` NuGet package (test-only); we write only step bindings. |

## What was built (summary)

- **Epic 0 foundations:** upstream pin and licenses; projects; message/report model; message texts EN/PL; conformance
  harness (all 1251 scenarios bound); CLI.
- **Epic 1 input:** `IEpubSource` (ZIP, directory, in-memory); OCF/ZIP checker and file name checker; galimatias URL
  parser; MIME/viewport/package peeker/vocabularies.
- **Epic 2 schema engine:** `Jing/` (RELAX NG, compact syntax, XSD datatypes, NVDL, Schematron with an XPath 2.0
  subset); upstream's schemas embedded (`Xml/Schemas`); mapping to `RSC-005`/`RSC-017`/etc. (`ReportingErrorHandler`).
- **Epic 3 rules:** META-INF files, OPF 2 and 3, NCX, content documents and nav, references/resource graph, CSS
  (ported parser), bitmaps/fonts, media overlays, scripting/accessibility (schemas), profiles, single-file and
  expanded modes.
- **Epic 4:** console, XML/XMP/JSON reports, standalone executable (ICU needed on Linux for Polish texts).
- **Epic 5 Signet integration:** `BookEpubSource` (validates the book as the export would write it); `EpubCheckValidator`
  (code = message id; `Usage`/`Fatal` severities; usage off by default via `CheckBookIncludeUsage`); `EpubCheckFixes`;
  skipped rules become severity overrides (`SkippedRuleMigration` maps old codes); old validators retired where
  `EpubCheckCoverageTests` shows the same report. Kept (EPUBCheck doesn't report them): missing DOCTYPE, media type vs
  extension (`ManifestMediaTypeValidator`), non-NCName ids (`IdValidator`), validators of fonts, cross-file structure,
  file names, media types vs content, images, CSS declarations, footnotes. `LinkIntegrityValidator` is no longer used
  by Check Book but stays for footnote operations.
- **Epic 6 quality:** conformance report; differential test against Java (`DifferentialTests`, skipped unless
  `SIGNET_JAVA_EPUBCHECK` names the 5.4.0 jar and `SIGNET_JAVA` a JDK 11+); scale and performance review (algorithms
  first; faster than the Java tool on generated books; method in `BASELINE-EPUBCHECK.md`).

## After the plan (each needs the owner's word)

- **More performance:** XPath interpreter, RELAX NG derivatives, XML event reader. Span/ArrayPool/LINQ libraries were
  never needed. Measure first, with inclusive shares, stopwatch, allocation traces, exception counts.
- The `itemref` duplicate assertion of `package-30.sch` is quadratic by upstream's design; a special case would be a
  deviation.
- Upstream 5.4.1+: separate task.
- Polish texts (~320 messages, CLI) need the owner's review.

## Upstream facts

- ~34k lines of Java, 323 `MessageId`s; severities `USAGE/INFO/WARNING/ERROR/FATAL/SUPPRESSED`.
- Most rules are schemas: 129 `.rnc`, 88 `.rng`, 10 `.nvdl`, 29 `.sch` (XSLT2 binding). Only XML Schema datatypes are
  used (no `whattf`).
- ICU4J only in `UnicodeUtils` and `OCFFilenameChecker`. Java libraries behind it: Jing, Saxon-HE 11.4, ICU4J,
  galimatias, commons-compress, SAC, Jackson. No .NET equivalent of Jing + Saxon.
- Conformance suite: 1251 scenarios in 50 `.feature` files, ~3.5k fixture files.

## CLI

`epubcheck.net <path> [options]`: `-v 2|3` version for non-EPUB input; `-m, --mode opf|xhtml|svg|nav|mo|exp`;
`-p, --profile default|dict|edupub|idx|preview`; `--save`; `-o/-x/-j` XML/XMP/JSON report (`-` = stdout);
`--maxOfEachMessage N|unlimited`; `--failonwarnings`; `-q -f -e -w -u` reporting level; `--locale en|pl`;
`-l, --listChecks [file]`; `-c, --customMessages [file]`; `--version`; `-h -? --help`.

## Risks

- Fidelity to Jing's error wording and Saxon's XPath semantics is where scenarios fail first.
- Upstream moves on; the port is pinned to 5.4.0.
- Polish translations need review.
