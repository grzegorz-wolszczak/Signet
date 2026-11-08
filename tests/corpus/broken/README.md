# `tests/corpus/broken` — valid EPUBs written around cases that caught Signet out

## The rule
**A case the tests missed goes into a book here — not into a `.md` note.** Put the markup in the file it belongs in (or
a new file/book), assert the behaviour in the test of the feature it caught out (`BrokenBookFootnoteTests`,
`CleanupAnalysisTests`, …), and comment *when* and *why*. If the behaviour is wrong and unfixed, assert the wrong
behaviour and say so, so a change gets noticed.

Unlike `malformed/` (broken as EPUBs; stop Signet at the door), these books **load**: their *content* is awkward.

## `footnotes`
Everything converts today; two shapes did not before October 2026.

**`citing.xhtml`**
- A note **cited from inside another note's text** (`one` cites `aside`). Used to leave a link back to a temporary
  `fn-new-…` id; F7 caught it and the book was rewound.
- Two notes **citing each other** (`left` ↔ `right`): used to produce overlapping edits (`href="#fn-1#fn-2-ref"`) and
  lose a footnote. Fixed by replacing every reference before cutting any note.
- An ordinary footnote.
- Preview links follow into the code view only when `FootnoteNavigation` tells them apart by the scanned footnotes
  (`BrokenBookFootnoteTests.Links_of_the_broken_book_are_told_apart_by_its_footnotes`).

**`awkward.xhtml`**
- A note **holding another note** (a comment swallowed a closing tag, `<!--\n</aside-->`); the inner goes first
  (`ContainsNote`, `InsideNote`).
- A **shared note** with two references and a link back to only the first.
- A note **nothing links to**, whose link back points at a missing id: left as is; validators keep reporting it
  (Signet doesn't quietly repair what it wasn't asked to).

## `markup`
**`comments.xhtml`**
- A comment that looks like it holds another (`<!-- … <!-- … -- …` ends at the first `--`). Not well-formed XML and
  **`CleanSource.Mend` doesn't repair it** (asserted by `CleanSourceTests.Mend_DoesNotRepairACommentHoldingADoubleHyphen`);
  corpus sweeps skip these books (`CorpusPaths.IsFaultyOnPurpose`).
- A **footnote written inside a comment** must not be found.
- `<script>`/`<style>` holding tag-like strings. `FootnoteScanner` (parses) is right; **`TagLister` (scans text) is not**
  — asserted by `TagListerTests.TagsWrittenInsideAStyleAreReadAsMarkup`, harmless where the nesting error is used.
- Numeric and named entities, a non-breaking space.

**`duplicates.xhtml`**
- The same id on two elements: lookup answers with the first (`ElementIds`, `ElementIdsTests`).
- A footnote reference's id **repeated on an unrelated element**, and a link to an id written twice: pairing must not
  be misled.

## `classes` (October 2026: Cleanup "classes without CSS definitions")
- **`attributes.xhtml`** + `styles/attributes.css` (selectors read the class attribute: `p[class~="note"]`,
  `[class^="verse-"]`, `span[class|="lang"]`): the step now leaves the whole file alone, `stray` included.
- **`plain.xhtml`** + `styles/plain.css` (class selectors only): `note` and `stray` are really undefined and removed.
  Both in `CleanupAnalysisTests.UndefinedClasses_on_the_broken_classes_book_strips_only_the_file_whose_css_names_classes_by_name`.
- **`combinators.xhtml`** + `styles/combinators.css` (`div.warn_center_1 p`; November 2026): "Go To Link Or Style" used to
  say the class had no definition. `CssClassDefinitionLocatorTests.Find_on_the_broken_classes_book_finds_a_class_named_before_a_combinator`;
  Cleanup also doesn't take it for undefined.

## `languages` (November 2026, Hyphenation: language from the nearest element stating one)
- **`languages.xhtml`:** Polish from `<html lang>`; English from a `<span>`; German from a `<blockquote>` (maybe no
  patterns); `en-GB` when only `en-US` is installed (other variant's patterns, with a warning); `EN_us` (still English);
  an already hyphenated word (kept, counted under Polish); French around only white space (adds no language). Asserted
  by `SoftHyphenSurveyTests`.
- **`outside.xhtml`:** no language stated, so the OPF's (Polish). Soft hyphens outside any language's text — head
  `<title>`, `href` (breaks the link), `id`, `title` attribute, `<pre>`, `<code>`, comment: adding never puts one
  there; removing lists each separately.
