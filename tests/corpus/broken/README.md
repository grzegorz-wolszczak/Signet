# `tests/corpus/broken` — valid EPUBs written around the cases that caught Signet out

## The rule

**When a session finds a case the tests did not cover, the case goes into a book here — not into a
note in a `.md` file.**

Several findings of October 2026 lived only in a throwaway probe and a commit message, and the next
session had to work them out again. A case written into a book is a case every later run exercises.
Adding one is: put the markup into the file it belongs in (or a new file in a new book), then assert
the behaviour in `BrokenBookFootnoteTests`, and say in a comment *when* and *why* the case was added.
If the behaviour is wrong and not yet fixed, the test still goes in — asserting the wrong behaviour
and saying so, so the day it changes, something says it changed.

## Not the same as `malformed/`

`tests/corpus/malformed` holds books that are broken as **EPUBs** — no `mimetype`, no rootfile, an
OPF that is not XML, XHTML that is not well formed. Those books stop Signet at the door, so each one
can only be tested for being turned away, and nothing past loading can be exercised on them.

The books here **load**. They are valid EPUBs whose *content* is awkward: the shapes a real
publishing tool emits by accident, and the ones that found real faults in Signet. That is why there
is more than one — a book can be awkward in several ways at once, but not in ways that stop it from
opening.

## `footnotes`

Footnotes in the shapes that went wrong. Everything in it converts without a problem today; two of
the shapes did not before October 2026.

**`citing.xhtml`**

- A note **cited from inside another note's text** (`one` cites `aside`): a footnote citing a
  footnote, ordinary in scholarly books. Until October 2026 the cited note's reference was dropped
  with the cut and the note kept a link back to a temporary `fn-new-…` id. The F7 check caught it, so
  the caller rewound the book and the user simply could not convert such a book.
- Two notes **citing each other** (`left` ↔ `right`): no order of whole footnotes can serve this,
  because whichever went first was cut out before the other's reference was replaced. It used to come
  out as two edits written over each other — `href="#fn-1#fn-2-ref"` — and a footnote lost.
  Replacing every reference before cutting any note settles both shapes with no ordering at all.
- An ordinary footnote, so the file is not only hard cases.

**`awkward.xhtml`**

- A note **holding another note** because a comment swallowed a closing tag (`<!--\n</aside-->`).
  The book is well formed; the note simply contains the other one. The conversion takes the inner
  note first, so the outer one is left with its own text — `ContainsNote` and `InsideNote` in the
  plan say so.
- A **shared note** with two references and a link back to only the first of them.
- A note **nothing links to**, whose own link back points at an id nobody wrote. It stays as it is,
  and the validators keep saying so after the conversion as before it. That is deliberate: Signet
  does not quietly repair what it was not asked to.

## `markup`

Content that reads as markup but is not, and ids that are not unique.

**`comments.xhtml`**

- A comment that looks as though it held another comment. `<!-- … <!-- … -- …` ends at the first
  `--`, and the rest is text — a scanner pairing `<!--` with `-->` reads this file wrongly. It also
  means the file is not well-formed XML and **`CleanSource.Mend` does not repair it**: mending would
  mean guessing where the author meant the comment to end. That is asserted, as today's behaviour, by
  `CleanSourceTests.Mend_DoesNotRepairACommentHoldingADoubleHyphen`, and it is why the corpus sweeps
  skip the books here (`CorpusPaths.IsFaultyOnPurpose`).
- A **whole footnote written inside a comment**. It must not be found. A scanner reading the file as
  plain text would report it.
- A `<script>` and a `<style>` holding strings that look like tags (`"<aside id=\"not-a-note\">"`,
  `if (1 < 2)`). Neither is content. `FootnoteScanner` parses the file and is right about them;
  **`TagLister` scans the text and is not** — the `<p>` inside the CSS string opens a tag it never
  closes, so it reports the `</style>` that follows as mismatched. That is asserted, as today's
  behaviour, by `TagListerTests.TagsWrittenInsideAStyleAreReadAsMarkup`, with a note on why it is
  harmless where the nesting error is used.
- Entity references, numeric and named, and a non-breaking space.

**`duplicates.xhtml`**

- The same id on two elements. A lookup by id has to answer with the first, as the DOM does — which
  is what `ElementIds` promises and `ElementIdsTests` holds it to.
- A footnote reference's id **repeated on an unrelated element further down**, and a link to an id
  that is written twice. The pairing may not be misled by either.
