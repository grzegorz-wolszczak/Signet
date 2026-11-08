# `tests/corpus/real` — the markup of real books, without their words

The other corpus books have one to three content files; that says what an operation does, not what a published book
looks like (performance faults needed thousands of notes or files; real tool markup isn't written by hand). These books
come from real ones with the prose replaced: every tag, attribute, id, href, class, `epub:type`, language, entity,
digit and punctuation stays; each run of letters becomes a Lorem word of the same length (the text is someone's work and
this repo is public).

**Not for scale:** `SyntheticBook` (`Signet.Core.Tests/TestSupport`) writes books of any size and `tests/Signet.Benchmarks`
measures on it. Here: one file per distinct markup shape.

## `verse-chapters`
Ten content files, 140 KB, from a 1331-file Polish Bible EPUB made by a web-to-EPUB tool (it has **eleven** distinct
shapes; 1075 files share one):
- footnotes of other origin: `<sup class="fnref"><a epub:type="noteref" href="#fn1" id="ref1">`, notes in
  `<section class="footnotes" epub:type="footnotes">`, each `<aside class="footnote">` opening with `<a class="fn-back">[1]</a>`;
- **shared notes** (`#fn6`, `#fn11` of `2kor-03.xhtml`, each read from three places);
- `<span class="verse-num">` with an id per verse (hundreds per file);
- a doubled folder `OEBPS/e0b/OEBPS/` and a stale `META-INF/META-INF/container.xml` pointing at a missing OPF.

### It is not a clean book, on purpose
Faults the published book shipped with are kept (a tidied fixture is no evidence). `RealBookFootnoteTests` records what
the book comes with and holds operations to **adding nothing** (the conversion's own rule F7).

| what | where | what it is |
|---|---|---|
| `Validation_FootnoteOrphan` | `est-01.xhtml` | notes nothing links to |
| `Validation_BadFragmentSameFile` × 6 | `est-01.xhtml` | their links back point at ids never written |
| `Validation_FootnoteManyRefs` | `2sm-13.xhtml` | a note read from two places, link back to only one |
| `Validation_DeadLink` | `dz-13.xhtml` | `href="otworz.php?skrot=Wj 12,51"` — leftover of the source website |

### Regenerating
```csharp
EpubAnonymizer.AnonymizeTree(unpackedBook, CorpusPaths.RealVerseChapters, keep.Contains);
```
`keep` = content files to keep (path inside the book); the OPF manifest, spine and nav lose the rest.
`EpubAnonymizerTests` holds the anonymiser to its promise (never replacing an id or entity).

## The rule
A case the tests missed goes into a book under `tests/corpus/broken` (or here if well-formed), not into a `.md` note.
