# Backlog

Ideas worth doing that are not part of any task in progress. Each entry says what it is, why it is
worth it, and what has to be found out first.

## Use the other files of a LibreOffice dictionary package

A Hunspell dictionary package (the ones shipped with LibreOffice) holds more than the `.aff`/`.dic`
pair Signet reads. For Polish, for example:

| File | What it is | Could Signet use it? |
|---|---|---|
| `pl_PL.aff`, `pl_PL.dic` | the spelling dictionary | **used today** (`SpellChecker`) |
| `hyph_pl_PL.dic` | libhyphen (TeX) hyphenation patterns | **the interesting one — see below** |
| `plhyph.tex` | the TeX source those patterns are generated from | probably not directly |
| `th_pl_PL_v2.dat`, `th_pl_PL_v2.idx` | MyThes thesaurus (synonyms) | maybe — a "synonyms" feature does not exist yet |
| `dictionaries.xcu` | LibreOffice's own registration file | no (it only names the files above) |

**Why `hyph_*.dic` matters.** "Add Soft Hyphens" (`SoftHyphenInserter`) deliberately does not
hyphenate by language: it uses a language-independent "vowel + single consonant" heuristic, because
no maintained .NET package implements the TeX/libhyphen pattern format (see the remarks on that
class). A user who installs a dictionary through the dictionary manager already has the right
patterns for their language sitting in `hunspell_dictionaries`. Reading that format (a list of
patterns with digits for break priorities, plus the `COMPOUNDLEFTHYPHENMIN`/`RIGHT` limits, and the
file's own encoding line) is a self-contained piece of work, and it would turn a rough heuristic
into real hyphenation for every language the user has a dictionary for.

**What to find out first:** whether the pattern files can be read as they are (licences allow it —
they ship with the dictionaries the user chose), how much of libhyphen's algorithm is needed for
plain words (the non-standard hyphenation rules of libhyphen are probably not), and what to do when
the book's language has no pattern file (keep today's heuristic as the fallback).
