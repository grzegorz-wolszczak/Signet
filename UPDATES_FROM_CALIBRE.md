# UPDATES_FROM_CALIBRE

Plan for taking over what Calibre added to its book editor ("Edit book") and EPUB handling between 2026-09-10 and
2026-11-06. Source: `../from.net/calibre` (`git log --since=2026-09-10`). Of ~800 commits in the period about 40 touch
the editor or EPUB handling; the rest (content server, OPDS, database, grammar checking, type-checking clean-ups) is out
of scope.

We take **ideas and behaviour**, not code (Calibre is Python/Qt; Signet is C#/Avalonia, GPL v3+ like Calibre, but the
architecture differs). Calibre's commit messages and tests are the specification.

## Rules for every task

- Conventions of `AGENTS.md` apply: English code/comments/commits, Polish talk, resources in both languages
  (`CoreStrings*.resx` for `Signet.Core`, `Strings*.resx` for the app), Conventional Commits, CRLF for .NET files.
- New checks go into `BookValidator.ValidateCurrentBook` as a separate `Check(ValidationStage.…)` call returning
  `IReadOnlyList<ValidationResult>`; the code of a result is a `Validation_…` resource key. Fixes derive from
  `ValidationFix` (see `ValidationFixes.cs`; `IdValidator` + `InvalidIdFix` is the model).
- **Before adding a check, show EPUBCheck does not report the same situation** (`EpubCheckCoverageTests`). The ACC-*
  messages of the port are `Suppressed`/`Usage` by default (`DefaultSeverities.g.cs`), so they are not a substitute.
- Every case found goes into a corpus book (`tests/corpus/broken/`, see its README) with the behaviour asserted in the
  matching test.
- Tests: Arrange/Act/Assert, AwesomeAssertions, `TempDir`, no timing assertions.
- Update this file when a task is done: tick it (`[x]`), note deviations from Calibre under it.

## Status legend

`[ ]` open · `[~]` in progress · `[x]` done

---

## Part A — Accessibility (the biggest block)

Calibre commits: `6b4887b51e` (checks), `1c36946b42` (epub:type without role), `a96f76afdd` (aria references),
`c7d7a6e222` (heading levels across files), `1627b70e33` (accessibility entry in the result list), `81cae32b24` (the tool),
`f3cba29a44` (SVG descriptions), `627717dfe4` (ARIA roles in generated navs), `8a7a70130a` / `eee070af52` / `91b5b81209`
(SVG covers). Basis: the *EPUB Accessibility* specification and the *EPUB Type to ARIA Role Authoring Guide*.

### A1. Check: images without alternative text — `[x]`

- Report `<img>` without `alt` (an empty `alt=""` is legal: decorative) and SVG images without a text alternative
  (`<title>`, `aria-label`, `aria-labelledby`, `role="presentation"`/`none`).
- New `AccessibilityValidator` in `Signet.Core/BookManipulation` (it takes the further checks A2–A6, one method each),
  new stage `ValidationStage.Accessibility`, codes `Validation_Accessibility_ImageNoAlt`, `Validation_Accessibility_SvgNoAlt`.
- Severity: warning. Skippable like any other code (`skippedCodes`).
- No fix in this task (the text must come from a person; the editor is task A9).
- Tests: validator unit tests + a corpus book with img/svg of each kind (alt present, empty, missing, `aria-label`).
- Done when: the Validation Results panel lists the problems with file and line, and a skipped code hides them.
- Done: `AccessibilityValidator` (tag scan via `TagLister`, so no DOM is built), stage `ValidationStage.Accessibility`.
  Deviations: an `img` with `aria-label`/`aria-labelledby` counts as labelled; `aria-hidden="true"` exempts both kinds. The
  cases are built in code in `AccessibilityValidatorTests` instead of a corpus book (no EPUB tooling needed for a single file).

### A2. Check: HTML files without a language — `[x]`

- Report an XHTML file whose `<html>` has neither `lang` nor `xml:lang` (EPUB 3 wants both; EPUB 2 `xml:lang`).
- Fix `SetHtmlLanguageFix`: set the attributes to the main language of the book (the first `dc:language`); no fix when
  the book has no language.
- Reuse `LanguageUsageFinder` / `Signet.Core.Semantics.Language`.
- Tests: with/without language, book without `dc:language` (no fix), the fix keeps the other attributes of `<html>`.
- Done: `AccessibilityValidator.CheckLanguage`, `SetHtmlLanguageFix` (EPUB 3: `lang` + `xml:lang`; EPUB 2: `xml:lang`). The "no
  `dc:language` = no fix" branch is implemented but not unit tested (no API to drop the language from a test book).

### A3. Check: headings skipping levels — `[x]`

- Walk the files in **spine order**; a heading deeper by more than one level than the previous heading (also the last one
  of an earlier file, `c7d7a6e222`) is reported. Descending by any amount is fine; the first heading may be any level.
- Reuse `Headings.cs` where it already collects headings.
- No fix (changing a level is an editorial decision).
- Tests: skip inside one file, skip across files (`h2` part page → `h4` chapter), descending is fine, files outside the spine ignored.
- Done: `AccessibilityValidator.CheckHeadingLevels` (warning `Validation_Accessibility_HeadingSkip`, with the line). The nav
  document is skipped; headings are read with `TagLister` rather than `Headings.cs` (that one builds the TOC model).

### A4. Check: accessibility metadata and summary (EPUB 3 only) — `[x]`

- Report missing `schema:accessMode`, `schema:accessibilityFeature`, `schema:accessibilityHazard`,
  `schema:accessibilitySummary`. (`dcterms:conformsTo` is not reported; only the editor, A9, sets it.)
- Fix `AddAccessibilityMetadataFix`, derived from the contents like Calibre does: `accessMode` = `textual`, plus `visual`
  when the book has images; `accessibilityFeature` = `alternativeText` when all images have alt (otherwise left out);
  `accessibilityHazard` = `unknown` (cannot be determined automatically). The summary is **not** invented: it stays reported.
- Uses the Metadata layer (`Signet.Core/Metadata`, `MetadataEditModel`) so the OPF stays consistent.
- Tests: book with and without images, with some images lacking alt, EPUB 2 book (no check).
- Done: `AccessibilityValidator.CheckMetadata` (warning `Validation_Accessibility_NoMetadata` naming the missing properties, info
  `Validation_Accessibility_NoSummary` without a fix), `AddAccessibilityMetadataFix`, `OpfResource.GetMetaPropertyValues`/`AddMetaProperty`.
  Deviations: the checked list is Calibre's current one (it also has `schema:accessModeSufficient`); the fix adds only missing properties,
  `accessModeSufficient` = `textual` when every image has an alternative, and `accessibilityFeature` also gets `tableOfContents` when the nav has
  a toc with links (so a text book can satisfy the check). Calibre's newest version has no automatic fix for this metadata (it makes claims about the
  book); the fix here keeps to what can be checked in the files. A new book has an empty nav, so it has no `tableOfContents` yet.

### A5. Check: `epub:type` without a matching ARIA role — `[x]`

- For every element with `epub:type` that has a DPUB-ARIA role (`AriaRoles.EpubTypeMapping`, `AriaRoles.AllowedTags`, both
  already in `Signet.Core.Semantics`) and no `role`, report; EPUB 3 only.
- Fix `AddAriaRoleFix`: add the role **only when allowed on that element** (`doc-chapter` on `section` yes, on `body` no;
  `doc-noteref` only on links); skip the WAI-ARIA roles `list`/`table`, the deprecated `doc-endnote` / `doc-biblioentry`
  and `doc-cover` (it is for images). Never replace an existing `role`.
- Tests: noteref link, section vs body, existing role kept, deprecated types ignored, EPUB 2 ignored.
- Done: `AriaRoleAdder` (the epub:type → role table and the element rules are Calibre's `ROLE_FOR_EPUB_TYPE` / `ACCEPTS_ANY_ROLE`; tag scan with
  `TagLister`), `AddAriaRolesFix` (per file, like Calibre: the first fix of a file fixes all its results), code
  `Validation_Accessibility_MissingRole`, severity **info** (as in Calibre). Deviation: `AriaRoles.EpubTypeMapping`/`AllowedTags` were not reused:
  their table is Sigil's (role → epub:type, with entries that are not roles), Calibre's table is the one of the authoring guide. An empty
  `role=""` counts as no role and gets the value.

### A6. Check: dangling ARIA references — `[x]`

- `aria-labelledby`, `aria-describedby`, `aria-controls`, `aria-owns`, `aria-flowto`, `aria-details`,
  `aria-activedescendant` (space-separated id lists) that point to an id missing in the same file are reported. No fix.
- Tests: every attribute, a list with one missing id, ids present.
- Done: `AccessibilityValidator.CheckAriaReferences` (warning `Validation_Accessibility_AriaReference`, one result per missing id, with the
  line); EPUB 3 only (ARIA attributes are not valid in XHTML 1.1). Calibre has no such check: this one is ours.

### A7. ID renaming also updates ARIA references and CSS selectors — `[x]`

- Calibre `a96f76afdd` (aria references) and `8eff51f371` (`#id` selectors in CSS) after an automatic invalid-id fix.
- Extend `IdRenamer` / `InvalidIdFix` / `IdRenamePreparation` / `IdReferenceIndex`: one renaming rewrites the `aria-*` id lists
  in HTML and the `#id` selectors in the CSS files and `<style>` elements that apply to the file. Leave selectors-like text in
  strings and comments alone; handle ids that occur in several files.
- The preparation (risk) dialog text mentions the new places.
- Tests: `aria-labelledby` lists, a selector `#a.b`, an attribute selector `[id="…"]`, an id in a comment/string unchanged.
- Done: `IdRefAttributes` (Calibre's `iteridrefs` list: the ARIA attributes, `for` of `label`/`output`, `headers` of `td`/`th`) is used by
  `IdRenamer` (in the files that carry the id; counted in `ReferencesUpdated`, whose label in the dialog now says "links and ARIA references"), by
  `InvalidIdFix` and by the A6 check (which also gained `aria-errormessage`, `for`, `headers`). `[id=…]` attribute selectors (exact match) are renamed
  too: `CssToolbox.FindIdsInSelector` returns them (so Find Usages lists them), `CssIdSelectors.Rename` rewrites them. `#id`, comments, strings and
  declaration blocks were handled before. Not taken from Calibre: its "remove the references to this id" fix for dangling references.

### A8. Accessibility group in the Validation Results panel — `[x]`

- Calibre `1627b70e33`: the accessibility checks are an entry in the list of results (instead of a separate checkbox).
- In Signet every code is already skippable; make sure the codes of A1–A6 are labelled "Accessibility" in the skip dialog
  and the panel, with a short tooltip explaining the group.
- Tests: UI test of the panel listing and of the skip.
- Done: a "Type" column in the panel ("Accessibility" for every `Validation_Accessibility_…` code, with a tooltip explaining the group),
  and the same word in front of the rule name in the list of skipped rules (`ValidationResultsViewModel.RuleName`). Skipping itself needed no
  change: every code was skippable already. There is no separate skip dialog in Signet; the skipped rules are the menu of the panel's button.

### A9. Tool "Accessibility" (Tools menu) — `[x]`

- Calibre `81cae32b24`: one window with three tabs. In Signet `AccessibilityWindow` + `AccessibilityViewModel`, action
  `AppActionIds.Accessibility` (catalog entry, menu entry, **tooltip** — memory rule on keymap tooltips; no default shortcut).
  - **Language** tab: the languages of the book (`dc:language`, add/remove) and "set the language of HTML files": only those
    without one / all, to the main language of the book (reuses A2).
  - **Image descriptions** tab: every image of the book in reading order with a preview, its file, an editable `alt` or a
    "decorative" mark (`alt=""` + `role="presentation"`); inline SVG too (`f3cba29a44`). An image used in several places lists
    each place. Save writes all files in one step (checkpoint like the other book-wide operations).
  - **Accessibility metadata** tab (EPUB 3): summary, access modes, features, hazards, conformance (`dcterms:conformsTo`) as
    checkable lists with the schema.org vocabulary and localized names.
- Buttons "Zapisz" / "Zamknij" like the other book-data editors (unsaved-changes prompt).
- Subtasks (each its own commit): A9a Core operations (list images in order, apply descriptions, read/write the metadata);
  A9b window and view model; A9c action, menu, localization, tooltips; A9d UI tests.
- Open questions (ask before starting): whether the list also shows images referenced only from CSS; the place in the menu.
- Decisions taken (the owner asked for no questions): images referenced only from CSS are not listed (only `img` and inline `svg`); the menu
  entry goes to Tools, after Hyphenation.
- A9a `[x]`: `AccessibilityLanguage` (languages of the book, `lang`/`xml:lang` of the files: only the missing or all), `AccessibilityMetadataEditor` +
  `AccessibilityVocabulary` (the schema.org values), `AccessibilityImages` (list in reading order; write `alt`/`role` of an `img`, `aria-label` kept in
  step with `<title>`/`aria-hidden` of an inline svg; text named by `aria-labelledby` is never changed, the reference is replaced), `OpfResource.SetLanguages`/
  `RemoveMetaProperty`.
- A9b–A9d `[x]`: `AccessibilityViewModel` + `AccessibilityWindow` (modal, three tabs: language and what to do with the language of the files; image
  list with preview, editable description, "decorative" mark, filter "only without a description", double click opens the place; metadata checklists
  with localized names, the summary, and values the vocabulary does not know kept; EPUB 2 shows a note instead); `MainWindowViewModel.Accessibility`
  (one checkpoint, `Status_AccessibilitySaved`); action `AppActionIds.Accessibility` (catalog, Tools menu after Hyphenation, tooltip, disabled during
  book operations, no shortcut); unsaved-changes prompt on close like the Metadata Editor. Not taken: setting the language of the OPF `<package>`.

### A10. Accessibility in what Signet generates — `[x]`

- `627717dfe4`: the EPUB 3 nav Signet writes (`NavProcessor`, `Toc/NavEntries`) gets the ARIA roles (`doc-toc`, `doc-pagelist`;
  check the guide for landmarks).
- `eee070af52`, `91b5b81209`: generated cover pages (Add Cover, `BookCreator`, `EditionPage`) use the book title as the text
  alternative and the book language in `lang` / `xml:lang`.
- `48863bb103`: an XHTML file Signet creates (new file, split, import) gets the language of the book.
- Tests: generated nav and cover contain the attributes; split / new file carries the language.
- Done: the nav already got `role="doc-toc"` / `role="doc-pagelist"` (checked and kept under test; the landmarks nav has no role). New (X)HTML files
  (`Book.CreateEmptyHtmlFile`, so also the HTML toc page, the edition page and the notes file that use it) and the HTML toc (`TocHtmlWriter`) now carry the
  main language of the book (`lang` + `xml:lang`, EPUB 2: `xml:lang`; nothing when the book has none, not English). The cover page template has the title of the
  book (or "Cover") as the title of the page and of the svg image (`SGC_COVER_TITLE`) and the language of the book. A file made by "Split" keeps the language of
  the original (it keeps its `<html>` tag; test added). Imports are not touched: the language of an imported file is the one it has.

---

## Part B — Editor features

### B1. TOC view: mark entries that belong to the open file — `[x]`

- Calibre `665ef7bdcc`. In the TOC panel, entries pointing into the file of the active code tab get a visual mark (bold or an
  icon); the mark follows tab changes. Decide whether it needs a setting.
- Touch the TOC view models and the tab-change notifications of `MainWindowViewModel`; no per-keystroke work.
- Tests: view-model test of the marked set after a tab change; UI render test.
- Done: Signet already had it: `TableOfContentsViewModel.SetCurrentFile` (called by `MainWindowViewModel` on every active-tab change) marks the entries
  that point at the file of the active tab (`TocEntryViewModel.IsCurrent`, shown in bold) and asks the view to reveal them; `TableOfContentsViewTests` cover the
  view. No setting: one pass over the entries per tab change. Added: `MainWindowTocMarkTests` (the mark follows opening and closing tabs).

### B2. Colors in CSS and HTML: underline preview and color picker — `[ ]`

- Calibre `9ec8e99d48`, `86546c1069`, `776db6a5c9`. In the Code Editor (AvaloniaEdit) draw an underline in the color for every
  color value in CSS (and inline `style` / `<style>`): hex, `rgb()`/`rgba()`, `hsl()`/`hsla()`, named colors; CSS Color 4
  (`oklch`, `lab`, `color()`) at least recognized without crashing.
- Ctrl+click on a color opens a picker; the chosen color replaces the value, in the same notation when possible.
- Preference (Preferences > Editor: "Show color preview under color values", default on); scan only the visible lines.
- Subtasks: B2a Core `CssColorScanner` (+ tests; cases from `src/tinycss/tests/color4.py` in Calibre); B2b editor renderer and
  preference; B2c Ctrl+click picker.
- Open question (ask): Avalonia `ColorPicker` or a small own dialog.

---

## Part C — EPUB 3 correctness (bugs Calibre fixed; check whether Signet has them)

For each: write the test first on a corpus book; if Signet already behaves correctly, record it here and keep the test.

### C1. Upgrade EPUB 2 → 3 keeps landmarks — `[x]`

- Calibre `fbf4423c6f`: the `<guide>` references must become the landmarks `nav` in `EpubUpgrader`.
- Done: Signet already did this (`EpubUpgrader.AddLandmarks` turns the guide into landmarks and, unlike Calibre, keeps the guide); the test
  `The_guide_entries_of_different_files_end_up_in_the_landmarks_with_their_types_and_titles` (cover + start in two files, `text` → `bodymatter`) keeps it so.

### C2. nav element ids survive regeneration — `[x]`

- Calibre `5a3593a1ef`: ids on `nav` elements are preserved when Signet regenerates the nav, so links to them keep working
  (`NavProcessor`, TOC editor save).
- Done: `NavProcessor` rebuilt every section from nothing, so the ids of the nav, its heading, its lists and its entries were lost. Now the old
  opening tag of the nav (with an `id` and a `role` added when it has none) and its heading are kept as they were, an entry keeps the ids of its `li`
  and `a` (matched by link, then by file), and the ids of what is gone stay as empty `<span id>` anchors before the nav (a nav may hold only a
  heading and a list; they are not written twice). Landmarks and the page list work the same way.

### C3. Valid nav markup after regeneration — `[x]`

- Calibre `82d8dc6886`: verify our generated nav with the EPUBCheck port on corpus books (nested lists, empty entries,
  entries without text).
- Done (tests in `NavMarkupValidityTests`, run through the EPUBCheck port): found and fixed three ways to write invalid markup: an entry without
  text (`RSC-005: Anchors within nav elements must contain text`; now the file name, for a landmark its standard title), a first entry below the top
  level (an unbalanced list: fatal `RSC-016`), a jump of more than one level (a duplicated entry; the level is now the one the list can have). An
  empty toc stays an empty `<ol>` (invalid: a nav needs an entry; the new-book template has the same state), nested lists and `&`/`<` in titles
  were fine.

### C4. Landmarks win over the legacy guide — `[x]`

- Calibre `251c1726e7`: when a book has both landmarks and a `<guide>`, use the landmarks (`Landmarks`, `GuideItems`,
  cover detection, Reports).
- Done: the places that look for a page by its semantics (`Book.FindExistingCoverHtmlResource`, `TocGenerator.CreateHtmlToc`) now use
  `Book.FindHtmlResourceBySemanticCode`: the landmarks of the nav win; the guide is consulted only when no page has a landmark of that kind
  (before, an EPUB 3 book with a nav ignored its guide altogether, so a guide-only cover was missed). The other readers of landmarks
  (Book Browser, the semantics map, footnote files) already use the landmarks of an EPUB 3 book. "Reports" has nothing to change.

### C5. Damaged ZIPs: missing entries and rootfiles — `[x]`

- Calibre "Fix handling of missing entries in damaged EPUB ZIPs" and "EPUB metadata: Ignore missing rootfiles in damaged ZIPs".
- Add books to `tests/corpus/malformed/` (central directory points to a missing entry; `container.xml` lists a missing rootfile
  besides a valid one); `OcfReader` / `ImportEpub` load what can be loaded with a `LoadWarning` instead of failing the book.
  Where it must fail, the message names the entry.
- Done: `OcfReader` loads what it can. A ZIP without its central directory is read from the local headers (`LocalZipReader`: stored and deflated entries; sizes from
  the local header or from the data descriptor of an entry written to a stream that could not seek) and the book gets `LoadWarning_DamagedZipRecovered` (no file at all
  is still `LoadError_CorruptZip`); a rootfile of `container.xml` that is not in the book is skipped for one that is (`LoadWarning_RootfileMissing`, the first used to
  fail the book), and when none is there the message names them all. The corpus book `malformed/first-rootfile-missing`; the damaged ZIPs are made by the test from
  `epub3/minimal` (README). A missing `container.xml` with no `.opf` already said so by name. Not taken: ZIP64, encrypted ZIP entries, descriptors without a signature.

---

## Suggested order

1. A1 → A2 → A3 → A5 → A6 → A4 → A8
2. A7, then C1–C4 (cheap verification tasks, may be done in between)
3. A9 (the tool), A10
4. B1, C5
5. B2 (largest, independent)

## Not taken

Content server, OPDS, full-text search, notes, calibredb, grammar checking, PDF / DOCX / RTF / KF8 input, metadata download
plugins, type-checking clean-ups, CI changes: no counterpart in Signet.
