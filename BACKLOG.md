# Backlog

Ideas worth doing that are not part of any task in progress. Each entry says what it is, why it is
worth it, and what has to be found out first.

## Use the other files of a LibreOffice dictionary package

A Hunspell dictionary package (the ones shipped with LibreOffice) holds more than the `.aff`/`.dic`
pair the spell checker reads. For Polish, for example:

| File | What it is | Used by Signet? |
|---|---|---|
| `pl_PL.aff`, `pl_PL.dic` | the spelling dictionary | **yes** (`SpellChecker`) |
| `hyph_pl_PL.dic` | libhyphen (TeX) hyphenation patterns | **yes** — Tools › Hyphenation (`HyphenationPatterns`, done in November 2026) |
| `plhyph.tex` | the TeX source those patterns are generated from | probably not directly |
| `th_pl_PL_v2.dat`, `th_pl_PL_v2.idx` | MyThes thesaurus (synonyms) | maybe — a "synonyms" feature does not exist yet |
| `dictionaries.xcu` | LibreOffice's own registration file | no (it only names the files above) |

Left of it: the thesaurus. A "synonyms" feature would read the MyThes format (`.idx` maps a word to an offset into
`.dat`, which lists its meanings and synonyms); what to find out first is where in the editor it would be offered.
