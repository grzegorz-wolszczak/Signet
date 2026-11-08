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

### A1. Check: images without alternative text — `[ ]`

- Report `<img>` without `alt` (an empty `alt=""` is legal: decorative) and SVG images without a text alternative
  (`<title>`, `aria-label`, `aria-labelledby`, `role="presentation"`/`none`).
- New `AccessibilityValidator` in `Signet.Core/BookManipulation` (it takes the further checks A2–A6, one method each),
  new stage `ValidationStage.Accessibility`, codes `Validation_Accessibility_ImageNoAlt`, `Validation_Accessibility_SvgNoAlt`.
- Severity: warning. Skippable like any other code (`skippedCodes`).
- No fix in this task (the text must come from a person; the editor is task A9).
- Tests: validator unit tests + a corpus book with img/svg of each kind (alt present, empty, missing, `aria-label`).
- Done when: the Validation Results panel lists the problems with file and line, and a skipped code hides them.

### A2. Check: HTML files without a language — `[ ]`

- Report an XHTML file whose `<html>` has neither `lang` nor `xml:lang` (EPUB 3 wants both; EPUB 2 `xml:lang`).
- Fix `SetHtmlLanguageFix`: set the attributes to the main language of the book (the first `dc:language`); no fix when
  the book has no language.
- Reuse `LanguageUsageFinder` / `Signet.Core.Semantics.Language`.
- Tests: with/without language, book without `dc:language` (no fix), the fix keeps the other attributes of `<html>`.

### A3. Check: headings skipping levels — `[ ]`

- Walk the files in **spine order**; a heading deeper by more than one level than the previous heading (also the last one
  of an earlier file, `c7d7a6e222`) is reported. Descending by any amount is fine; the first heading may be any level.
- Reuse `Headings.cs` where it already collects headings.
- No fix (changing a level is an editorial decision).
- Tests: skip inside one file, skip across files (`h2` part page → `h4` chapter), descending is fine, files outside the spine ignored.

### A4. Check: accessibility metadata and summary (EPUB 3 only) — `[ ]`

- Report missing `schema:accessMode`, `schema:accessibilityFeature`, `schema:accessibilityHazard`,
  `schema:accessibilitySummary`. (`dcterms:conformsTo` is not reported; only the editor, A9, sets it.)
- Fix `AddAccessibilityMetadataFix`, derived from the contents like Calibre does: `accessMode` = `textual`, plus `visual`
  when the book has images; `accessibilityFeature` = `alternativeText` when all images have alt (otherwise left out);
  `accessibilityHazard` = `unknown` (cannot be determined automatically). The summary is **not** invented: it stays reported.
- Uses the Metadata layer (`Signet.Core/Metadata`, `MetadataEditModel`) so the OPF stays consistent.
- Tests: book with and without images, with some images lacking alt, EPUB 2 book (no check).

### A5. Check: `epub:type` without a matching ARIA role — `[ ]`

- For every element with `epub:type` that has a DPUB-ARIA role (`AriaRoles.EpubTypeMapping`, `AriaRoles.AllowedTags`, both
  already in `Signet.Core.Semantics`) and no `role`, report; EPUB 3 only.
- Fix `AddAriaRoleFix`: add the role **only when allowed on that element** (`doc-chapter` on `section` yes, on `body` no;
  `doc-noteref` only on links); skip the WAI-ARIA roles `list`/`table`, the deprecated `doc-endnote` / `doc-biblioentry`
  and `doc-cover` (it is for images). Never replace an existing `role`.
- Tests: noteref link, section vs body, existing role kept, deprecated types ignored, EPUB 2 ignored.

### A6. Check: dangling ARIA references — `[ ]`

- `aria-labelledby`, `aria-describedby`, `aria-controls`, `aria-owns`, `aria-flowto`, `aria-details`,
  `aria-activedescendant` (space-separated id lists) that point to an id missing in the same file are reported. No fix.
- Tests: every attribute, a list with one missing id, ids present.

### A7. ID renaming also updates ARIA references and CSS selectors — `[ ]`

- Calibre `a96f76afdd` (aria references) and `8eff51f371` (`#id` selectors in CSS) after an automatic invalid-id fix.
- Extend `IdRenamer` / `InvalidIdFix` / `IdRenamePreparation` / `IdReferenceIndex`: one renaming rewrites the `aria-*` id lists
  in HTML and the `#id` selectors in the CSS files and `<style>` elements that apply to the file. Leave selectors-like text in
  strings and comments alone; handle ids that occur in several files.
- The preparation (risk) dialog text mentions the new places.
- Tests: `aria-labelledby` lists, a selector `#a.b`, an attribute selector `[id="…"]`, an id in a comment/string unchanged.

### A8. Accessibility group in the Validation Results panel — `[ ]`

- Calibre `1627b70e33`: the accessibility checks are an entry in the list of results (instead of a separate checkbox).
- In Signet every code is already skippable; make sure the codes of A1–A6 are labelled "Accessibility" in the skip dialog
  and the panel, with a short tooltip explaining the group.
- Tests: UI test of the panel listing and of the skip.

### A9. Tool "Accessibility" (Tools menu) — `[ ]`

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

### A10. Accessibility in what Signet generates — `[ ]`

- `627717dfe4`: the EPUB 3 nav Signet writes (`NavProcessor`, `Toc/NavEntries`) gets the ARIA roles (`doc-toc`, `doc-pagelist`;
  check the guide for landmarks).
- `eee070af52`, `91b5b81209`: generated cover pages (Add Cover, `BookCreator`, `EditionPage`) use the book title as the text
  alternative and the book language in `lang` / `xml:lang`.
- `48863bb103`: an XHTML file Signet creates (new file, split, import) gets the language of the book.
- Tests: generated nav and cover contain the attributes; split / new file carries the language.

---

## Part B — Editor features

### B1. TOC view: mark entries that belong to the open file — `[ ]`

- Calibre `665ef7bdcc`. In the TOC panel, entries pointing into the file of the active code tab get a visual mark (bold or an
  icon); the mark follows tab changes. Decide whether it needs a setting.
- Touch the TOC view models and the tab-change notifications of `MainWindowViewModel`; no per-keystroke work.
- Tests: view-model test of the marked set after a tab change; UI render test.

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

### C1. Upgrade EPUB 2 → 3 keeps landmarks — `[ ]`

- Calibre `fbf4423c6f`: the `<guide>` references must become the landmarks `nav` in `EpubUpgrader`.

### C2. nav element ids survive regeneration — `[ ]`

- Calibre `5a3593a1ef`: ids on `nav` elements are preserved when Signet regenerates the nav, so links to them keep working
  (`NavProcessor`, TOC editor save).

### C3. Valid nav markup after regeneration — `[ ]`

- Calibre `82d8dc6886`: verify our generated nav with the EPUBCheck port on corpus books (nested lists, empty entries,
  entries without text).

### C4. Landmarks win over the legacy guide — `[ ]`

- Calibre `251c1726e7`: when a book has both landmarks and a `<guide>`, use the landmarks (`Landmarks`, `GuideItems`,
  cover detection, Reports).

### C5. Damaged ZIPs: missing entries and rootfiles — `[ ]`

- Calibre "Fix handling of missing entries in damaged EPUB ZIPs" and "EPUB metadata: Ignore missing rootfiles in damaged ZIPs".
- Add books to `tests/corpus/malformed/` (central directory points to a missing entry; `container.xml` lists a missing rootfile
  besides a valid one); `OcfReader` / `ImportEpub` load what can be loaded with a `LoadWarning` instead of failing the book.
  Where it must fail, the message names the entry.

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
