# Plan: the Hunspell dictionary manager

Written in November 2026, ready to be implemented as it stands. Everything the user decided is in
"Decisions"; nothing in this plan is waiting for an answer.

## Why

Signet reads its Hunspell dictionaries from a folder the user has to fill by hand
(`AppDirectories.HunspellDictionariesDirectory` — `%LOCALAPPDATA%\Signet\hunspell_dictionaries` on
Windows, `~/.local/share/Signet/...` elsewhere). Today the only way to install a dictionary is to
find the files somewhere (a LibreOffice dictionary package, for instance), know which of them
matter, copy them in and restart Preferences. Nothing tells the user whether what they copied is
complete, whether it is a dictionary at all, or how to get rid of one. The two bugs fixed just
before this plan (a dictionary read in the wrong encoding, a book checked in the wrong language)
both came out of a dictionary the user had installed blind.

The manager makes that a supported operation: see what is installed, add a dictionary from files
anywhere on disk, delete one, and be sure that what is listed actually works.

## Decisions (settled with the user — do not re-litigate)

1. **Required files:** a dictionary is the pair `<name>.aff` + `<name>.dic` with the same base
   name. That is all Signet reads. The known companion files of a dictionary package may be added
   alongside, and are kept with it; anything else is refused with a list of the refused files.
2. **"Enabled" means visible**, not a switch: a dictionary that is listed is one that is installed
   and works. There is no enable/disable toggle. **An incomplete dictionary cannot be added at
   all** — the Add step fails and says what is missing.
3. **Delete removes every file of the dictionary**, with a confirmation that lists them first. If
   the deleted dictionary was the primary or the secondary one, the setting falls back to the
   built-in `en_US`.
4. **The built-in `en_US` is listed**, marked as built-in, and cannot be deleted. An installed
   `en_US` overrides it (as `SpellChecker.DiscoverAvailableDictionaries` already does), and the
   list says so.
5. A separate backlog item (see `BACKLOG.md`) asks whether Signet could *use* the companion files
   (`hyph_*.dic` for hyphenation, `th_*` for a thesaurus). The manager only keeps them; it does not
   read them.

## What a Hunspell dictionary is made of

Needed by Signet (`SpellChecker` → `WordList.CreateFromFiles`):

| File | What it is |
|---|---|
| `<name>.aff` | the affix file: the encoding (`SET`), the flags, the prefix/suffix rules, `TRY`, `WORDCHARS`, … |
| `<name>.dic` | the word list: a count on the first line, then one entry per line (`word/FLAGS`) |

`<name>` is both the file name and the name Signet shows (`SpellChecker.DisplayName` turns `pl_PL`
into "Polish - Poland"). It is usually a language code, but nothing enforces that.

Companion files found in LibreOffice packages (kept, never read by Signet):

| File | What it is |
|---|---|
| `hyph_<lang>.dic` | libhyphen hyphenation patterns |
| `<something>.tex` | the TeX source those patterns were generated from (e.g. `plhyph.tex`) |
| `th_<lang>_v2.dat`, `th_<lang>_v2.idx` | MyThes thesaurus |
| `dictionaries.xcu` | LibreOffice's registration file for the package |
| `README*`, `LICENSE*`, `COPYING*`, `*.txt` | documentation and licences |

Not supported, and refused: `*.dic_delta` (Sigil's delta files — `SpellChecker` says so already).

## How a candidate is validated

Three steps, in order; the first failure stops the add and is reported.

1. **By name.** Every chosen file must be one of: `.aff`, `.dic`, `.dat`, `.idx`, `.xcu`, `.tex`,
   `.txt`, or a licence/readme without an extension. Everything else goes on the "not dictionary
   files" list.
2. **By shape.** Among the chosen files there must be exactly one `.aff`, and a `.dic` with the
   same base name (`hyph_*.dic` does not count as the word list — it is a companion). Missing one
   of them is the "incomplete" failure, with a message naming what is missing.
3. **By content.** The pair is actually loaded before anything is copied:
   - the `.aff` must be readable as text and hold at least one Hunspell directive at the start of a
     line (`SET`, `TRY`, `FLAG`, `WORDCHARS`, `SFX`, `PFX`, `REP`, `MAP`, `KEEPCASE`, `NOSUGGEST`,
     `ICONV`, `OCONV`, `COMPOUND*`, `NEEDAFFIX`, `ONLYINCOMPOUND`, `BREAK`, `LANG`, `IGNORE`);
   - the `.dic`'s first non-empty line must parse as an integer (the word count);
   - `WordList.CreateFromFiles(dic, aff)` must not throw, and the loaded list must check at least
     one word from the `.dic` as correct (a word with no flags, so a bad flag table shows up).
   Call `LegacyEncodings.Ensure()` first — the `.aff` may name a code page encoding (this is the
   bug fixed in `a5539d8`).

A dictionary of the same name already installed: the add asks whether to replace it (it is a
replace, not a merge — every file of the old one is removed first).

## The window

Modal, opened from Preferences › Spelling by a new button next to the "Main dictionary" /
"Additional dictionary" combo boxes (`PreferencesWindow.axaml`, the `Spellcheck` tab, the
`Classes="labeled"` grid at the top). Roughly 640 × 440, `WindowStartupLocation="CenterOwner"`.

```
┌─ Dictionary manager ─────────────────────────────────────────────┐
│  Dictionaries in <path to hunspell_dictionaries>            [↗]  │  ← the path, and a button that
│                                                                  │    opens the folder
│  ┌────────────────────────────────────────────────┐  ┌────────┐  │
│  │ Name            │ Files │ State                │  │ Add…   │  │
│  ├────────────────────────────────────────────────┤  ├────────┤  │
│  │ English - US    │   2   │ built-in             │  │ Delete │  │
│  │ Polish - Poland │   6   │ works                │  └────────┘  │
│  │ German          │   2   │ cannot be read: …    │               │
│  └────────────────────────────────────────────────┘               │
│                                                                   │
│  Files of the selected dictionary:                                │
│  ┌────────────────────────────────────────────────────────────┐   │
│  │ pl_PL.aff          required                                │   │
│  │ pl_PL.dic          required                                │   │
│  │ hyph_pl_PL.dic     hyphenation (not used by Signet)        │   │
│  │ th_pl_PL_v2.dat    thesaurus (not used by Signet)          │   │
│  │ dictionaries.xcu   LibreOffice (not used by Signet)        │   │
│  └────────────────────────────────────────────────────────────┘   │
│                                                      [ Close ]    │
└───────────────────────────────────────────────────────────────────┘
```

- **The list** is a `TreeDataGrid` with a `FlatTreeDataGridSource` built in the code-behind with
  `LocalizedColumns<T>` (the convention of this codebase — see `SpellcheckWordsView`), sorted by
  display name, the built-in one first. Single selection.
- **State** is one of: *built-in*, *works*, *overrides the built-in one*, or the reason it does not
  work (an incomplete pair that was put there by hand, or a load failure). A broken entry can be
  deleted but never becomes the primary dictionary.
- **Files** is the list of the files that belong to the selected dictionary, each with what it is
  (required / hyphenation / thesaurus / LibreOffice / documentation).
- **Add…** opens a multi-select file picker (`StorageProvider.OpenFilePickerAsync`) filtered to the
  supported extensions, then validates (above) and copies. On failure nothing is copied and a
  message window shows what was wrong, with the list of refused or missing files.
- **Delete** is a `Classes="danger"` button; it asks first (`ConfirmWindow`), listing the files it
  will remove. Disabled for the built-in `en_US`.
- Both buttons are disabled while nothing is selected.

## Behaviour after the window closes

The manager writes files; everything that reads them has to be told:

1. `SpellChecker.Reload()` — rediscovers the dictionaries and reloads the primary/secondary ones.
2. If the primary or the secondary dictionary no longer exists, set it to `EmbeddedDictionary`'s
   `en_US` (primary) or empty (secondary) in `SettingsStore` before the reload.
3. The Preferences window rebuilds `AvailableDictionaries` / `AvailableSecondaryDictionaries` (the
   lists are built once in the `PreferencesViewModel` constructor today — extract that into a
   `RefreshAvailableDictionaries()` and call it when the manager closes), keeping the current
   selection when it still exists.
4. The open Code Editor tabs re-check their spelling (`MainWindowViewModel.RefreshOpenTabsSpellcheck`
   — already exists and is already called after Preferences are applied).

## How to implement

### Core (`Signet.Core/Spellcheck`)

A new `HunspellDictionaryInstaller` (no UI, so it can be tested without Avalonia):

```csharp
public enum DictionaryFileRole { Required, Hyphenation, Thesaurus, LibreOffice, Documentation }

public readonly record struct DictionaryFile(string Path, DictionaryFileRole Role);

public sealed record InstalledDictionary(
    string Name, IReadOnlyList<DictionaryFile> Files, bool IsBuiltIn, bool OverridesBuiltIn, string? Problem);

public sealed record DictionaryInstallResult(
    bool Installed, string? Name, IReadOnlyList<string> RefusedFiles, string? Problem);

public static class HunspellDictionaryInstaller
{
    public static IReadOnlyList<InstalledDictionary> List(string directory);
    public static DictionaryInstallResult Validate(IReadOnlyList<string> files);   // no copying
    public static DictionaryInstallResult Install(IReadOnlyList<string> files, string directory, bool replace);
    public static IReadOnlyList<string> FilesOf(string directory, string name);    // what Delete would remove
    public static void Remove(string directory, string name);
}
```

- `FilesOf` matches the pair by base name and the companions by the language part of the name
  (`pl_PL` → `hyph_pl_PL.dic`, `th_pl_PL_v2.*`); `dictionaries.xcu` and `*.tex` are shared by a
  whole package, so they are only removed when no other dictionary is left in the folder.
- `Install` copies into a temporary folder inside the target directory first and moves the files in
  only when every check passed, so a failed add never leaves half a dictionary behind.
- All messages come from `CoreStrings` (the Core resources), not from the view.

### App

- `DictionaryManagerViewModel` (`Signet.App/ViewModels`): the list, the selected entry, its files,
  the Add/Delete commands, and the events the window answers (`AddFilesRequested`,
  `ConfirmDeleteRequested`, `ProblemReported`). It takes the `SpellChecker` and the `SettingsStore`
  and does the fallback of points 1–2 above.
- `DictionaryManagerWindow` (`Signet.App/Views`): the XAML above; the `TreeDataGrid` source and
  columns in the code-behind, the file pickers and the confirmation through the window's
  `StorageProvider` / `ConfirmWindow`, as the other windows do.
- Preferences: a button `PreferencesWindow_ManageDictionaries` ("Manage dictionaries…") on the
  Spelling tab, opening the window modally over the Preferences window; on close, the refresh of
  point 3.
- Strings in `Strings.resx` and `Strings.en.resx` (Polish is the default language, English the
  translation), Core messages in `CoreStrings.resx` / `CoreStrings.en.resx`.

### Tests

`tests/Signet.Core.Tests/Spellcheck/HunspellDictionaryInstallerTests.cs` (the bulk of them), using
`DictionaryFixture` (which can already write a UTF-8 and an ISO-8859-2 pair) and `TempDir`:

- a complete pair is installed and listed as working;
- an `.aff` without its `.dic` (and the other way round) is refused as incomplete, and nothing is
  copied;
- a file that only looks like a dictionary (a `.dic` whose first line is not a count, an `.aff`
  with no directive, a `.dic` renamed from a text file) is refused, and the refused list names it;
- an unsupported extension (`.docx`, `.dic_delta`) is refused by name;
- the companion files of a LibreOffice package are copied along and listed with their roles;
- installing over an existing name with `replace: false` fails and changes nothing; with
  `replace: true` the old files are gone;
- `Remove` deletes every file of the dictionary and leaves the other dictionaries alone;
- `List` marks the built-in `en_US`, and marks an installed `en_US` as overriding it;
- a dictionary in a code page encoding (`DictionaryFixture.WriteIso88592`) passes validation —
  the regression of `a5539d8` seen from the installer's side.

`tests/Signet.App.Tests/DictionaryManagerViewModelTests.cs`: the fallback of the primary/secondary
dictionary after a delete, the commands being disabled for the built-in one, and the refresh of the
list after an install.

`tests/Signet.App.PreferencesUiTests`: the Spelling page has the button, and the dictionary lists
are rebuilt after the manager reports a change. (The manager window itself: a render test in
`Signet.App.UiTests`, like the other windows.)

Every test deletes what it made (`TempDir`, `Disposables`) — `TempCleanupGuard` fails the run
otherwise.

## Things to watch out for

- **Encoding.** Always `LegacyEncodings.Ensure()` before `WordList.CreateFromFiles`. The validation
  must load the dictionary the same way `SpellChecker` does, or a dictionary could pass here and
  fail there.
- **Case.** File names differ in case between platforms; compare dictionary names with
  `StringComparer.OrdinalIgnoreCase` when looking for an existing one, but keep the name the user's
  files have.
- **The folder may not exist** when the manager opens for the first time — create it (the same way
  `SpellChecker.EnsureDefaultUserDictionaryFile` creates the user dictionaries folder).
- **Do not touch `user_dictionaries`.** That folder is the user's own word lists and is managed on
  the Spelling page already; this manager is only about `hunspell_dictionaries`.
- **The files are the user's.** A failed add must leave the source files alone and the target
  folder exactly as it was.
