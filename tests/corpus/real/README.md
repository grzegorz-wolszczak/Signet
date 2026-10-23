# `tests/corpus/real` — the markup of real books, without their words

The books under `tests/corpus/epub2`, `epub3` and `edge` hold one to three content files each. That
is enough to say what an operation does and nothing at all about what a published book looks like:
every performance fault found in the footnote code so far needed a thousand files or thousands of
notes in one file before it showed itself, and none of them could have been reproduced on those
books. The markup a real publishing tool emits is also not something anyone writes by hand into a
fixture — the wrappers, the shared notes, the stray files.

So the books here are taken from real ones and stripped of their prose: every tag, attribute, id,
href, class, `epub:type`, language, entity, digit and piece of punctuation stays exactly as it was,
and each run of letters becomes a Lorem word of the same length. The text of a published book is
someone's work and this repository is public, which is the other reason the words do not travel.

**Scale is not what these are for.** A thousand anonymised files would weigh twelve megabytes and
teach nothing a generated book does not — `SyntheticBook` (in `Signet.Core.Tests/TestSupport`)
writes a book of any size for that, and `tests/Signet.Benchmarks` measures on it. What is kept here
is one file per distinct shape of markup.

## `verse-chapters`

Chapters of numbered verses with footnotes of other origin. Ten content files, 140 KB.

Taken from a 1331-file EPUB of a Polish Bible translation produced by a web-to-EPUB tool. That book
turned out to hold **eleven** distinct shapes of markup over its 1331 files (1075 files share one of
them), so one file per shape keeps everything it had to teach:

- footnotes of other origin: `<sup class="fnref"><a epub:type="noteref" href="#fn1" id="ref1">1</a></sup>`
  with the notes in a `<section class="footnotes" epub:type="footnotes">` of their own, each
  `<aside class="footnote">` opening with an `<a class="fn-back">[1]</a>`;
- **shared notes** — `#fn6` and `#fn11` of `2kor-03.xhtml` are each read from three places, which is
  what makes a note shared and gives it a letter per reference;
- `<span class="verse-num">` carrying an id per verse, so a file holds hundreds of ids;
- a deep, doubled folder name — `OEBPS/e0b/OEBPS/` — and a second, stale
  `META-INF/META-INF/container.xml` pointing at an OPF that is not there. Both are how the book
  arrived, and both are the kind of thing that breaks path handling.

### It is not a clean book, and that is the point

The faults below were in the published book and are kept. A fixture tidied up for the tests would
stop being evidence of anything. `RealBookFootnoteTests` therefore does not demand a clean
validator: it records what the book comes with and holds the operations to **adding nothing** — the
same rule the conversion checks for itself (F7).

| what | where | what it is |
|---|---|---|
| `Validation_FootnoteOrphan` | `est-01.xhtml` | notes that nothing in the book links to |
| `Validation_BadFragmentSameFile` × 6 | `est-01.xhtml` | those notes' links back point at ids that were never written |
| `Validation_FootnoteManyRefs` | `2sm-13.xhtml` | a note read from two places with a link back to only one of them |
| `Validation_DeadLink` | `dz-13.xhtml` | `href="otworz.php?skrot=Wj 12,51"` — a leftover of the website the book was made from |

### Regenerating it

```csharp
EpubAnonymizer.AnonymizeTree(unpackedBook, CorpusPaths.RealVerseChapters, keep.Contains);
```

`keep` is the set of content files to hold on to, by their path inside the book; the OPF manifest,
the OPF spine and the navigation document lose their entries for the rest.
`EpubAnonymizerTests` holds the anonymiser to what it promises, because if it ever starts replacing
an id or an entity these fixtures quietly stop testing the thing they were taken from.

## The rule

**When a session finds a case the tests did not cover, the case goes into a book under
`tests/corpus/broken` (or here, if it is well-formed), not into a note in a `.md` file.** Several
findings of October 2026 lived only in throwaway probes and in commit messages, and each later
session had to work them out again.
