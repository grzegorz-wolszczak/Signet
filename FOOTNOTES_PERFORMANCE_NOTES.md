# Notatki: wydajność operacji na przypisach (duże książki)

Ten plik podsumowuje dochodzenie w sprawie zawieszania się GUI przy operacjach na przypisach
(zgłoszone na przykładzie `Biblia Tysiąclecia - Zespół biblistów polskich.epub`: **1409 plików
XHTML, ~9551 referencji do przypisów**). Trzymany w repo, żeby kontynuować pracę w kolejnych
sesjach, jeśli coś zostanie przerwane.

## Zgłoszony problem

Kliknięcie "Renumber Footnotes" na dużej książce powodowało pełne zawieszenie GUI na kilkanaście
sekund do kilku minut — okno nie reagowało, nie dało się nic kliknąć.

## Co zbadaliśmy i co znaleźliśmy

### 1. Pierwotna przyczyna: operacje na przypisach synchroniczne na wątku UI

`ICodeTabHost.RenumberFootnotes()` oraz pięć operacji edycji pojedynczego przypisu
(`AddFootnote`, `AddFootnoteReference`, `RemoveFootnote`, `RemoveFootnoteReference`,
`EditFootnote`) wykonywały `FootnoteEditor.*` **synchronicznie na wątku UI** — bez `Task.Run`,
bez okna postępu. Dla książki tej wielkości to realnie zawiesza GUI na dziesiątki sekund do minut.

**Naprawione:** wszystkie sześć operacji przeniesiono na `RunBookOperationAsync`/
`RunInBackgroundAsync` (ten sam mechanizm co "Change Footnote Placement"/"Convert Footnotes").
`RenumberFootnotes` dostał pełne okno postępu z Cancel; pięć szybkich edycji działa w tle, okno
(bez Cancel) pokazuje się tylko gdy operacja faktycznie trwa dłużej niż ~300 ms
(`ProgressWindow.DefaultDelay`, patrz `CodeTabView.axaml.cs: DelayedFootnoteProgress`).

### 2. Odkryty po drodze: cross-thread crash w śledzeniu nawigacji

Przeniesienie pracy do tła ujawniło, że `MainWindowViewModel.OnNavigationResourcePathChanged`
(śledzenie Navigate Back/Forward) dotykał `TextDocument` otwartej karty **wprost z wątku w tle**,
gdy dodanie globalnego przypisu wymagało utworzenia nowego pliku notatek (rename zasobu →
zdarzenie `ResourceBookPathChanged` → `UpdateNavigationPlace()` → `TextDocument.TextLength` →
`InvalidOperationException: Call from invalid thread`).

**Naprawione:** `OnNavigationResourcePathChanged` pomija `UpdateNavigationPlace()` gdy
`_bookEditedInBackground == true` (ten sam wzorzec co `OnBookModifiedStateChanged` już miał);
`RunInBackgroundAsync`'s `finally` wywołuje `UpdateNavigationPlace()` raz po powrocie na wątek UI.

### 3. Dodano granularne logi debugowe (Preferencje → Debug → "Enable debug logging")

Nowe kategorie w `DebugLog`:
- **`"Checkpoint"`** — `creating "..."` / `created "..." (XXX ms)`, `abandoning...` /
  `abandoned, book restored (XXX ms)` — w `MainWindowViewModel.Checkpoints.cs`.
- **`"Operation"`** — `{title}: starting in background` → `finished (XXX ms)` / `cancelled (XXX
  ms)` — w `RunInBackgroundAsync` (`MainWindowViewModel.Operations.cs`), obejmuje **każdą**
  operację w appce idącą przez ten mechanizm (Renumber, Change Placement, Convert, Mend HTML,
  Fix Problems, batch rename itd.), nie tylko przypisy.
- **`"Operation"`** (osobne wpisy) — `Preparing footnote placement/conversion: starting/finished
  (XXX ms)` w `PrepareFootnotePlacementAsync`/`PrepareFootnoteConversionAsync` (wcześniej
  kompletnie bez logów — osobny `Task.Run` omijający resztę instrumentacji).
- **`"Renumber"`** — per-etapowy timing wewnątrz `FootnoteEditor.Renumber`: `Scanning` →
  `Numbering` → `Writing` → `Sorting` → `Styling`, każdy z `starting (N to go)` / `finished (XXX
  ms)`.

### 4. Zmierzone liczby na Biblii Tysiąclecia (realny test na Windows)

| Etap | Czas |
|---|---|
| Checkpoint create (kopia folderu książki) | ~2.1–2.5 s |
| **`FootnoteScanner.Scan`** (pełny parsing DOM przez AngleSharp, 1409 plików) | **~18.7 s** |
| Renumber → Numbering (przerwane po ~50s testu, nie domierzone do końca) | 50+ s (przed pełną
  optymalizacją progresu: cała reszta — Numbering+Writing+Sorting+Styling — zajmowała łącznie
  ~134 s przy jednym pełnym przebiegu) |
| **Checkpoint abandon** (przywracanie książki po Cancel) | **~35–37 s** |
| `PlanPlacement`/`PlanConversion` ("Preparing"/"Analyzing" — skan WSZYSTKICH linków w książce,
  ~43k `<a>` w tej książce) | **~19.5 s** |

**Wniosek:** po naprawie z punktu 1, **checkpoint abandon (35–37s) i same skany (Scan/PlanPlacement,
~19s każdy) stały się największym źródłem freeze'u** — gorszym niż oryginalny zgłoszony problem,
bo działały synchronicznie na UI we wszystkich operacjach z checkpointem w całej aplikacji, nie
tylko w przypisach.

### 5. Naprawione: checkpoint create/abandon w tle

`AddCheckpointBefore`/`AbandonCheckpoint` (`MainWindowViewModel.Checkpoints.cs`) mają teraz
asynchroniczne bliźniaki: `AddCheckpointBeforeAsync`/`AbandonCheckpointAsync` — część dotykająca
UI (`SaveAllTabs`/`CaptureSnapshot`/`SwapToCheckpointBook`) zostaje na wątku UI, ciężkie I/O
(`_checkpoints.AddCheckpoint`/`.Abandon` — kopiowanie/odtwarzanie folderu książki) idzie przez
`Task.Run`.

Używane **tylko** tam, gdzie już wcześniej zmierzyliśmy problem (celowo wąski zakres, żeby nie
ruszać dziesiątek innych, synchronicznych miejsc wywołania `AddCheckpointBefore` w całej appce):
`RunBookOperationAsync`, `ConvertFootnotesAsync`, pięć metod edycji przypisu. **Pozostałe miejsca
(SpellCheck, RenameClass, RenameId, RemoveClass, RemoveSpans, RemoveEditionPage,
UnifyFootnoteLetters) nadal wołają synchroniczny `AddCheckpointBefore`** — nie zmieniane dziś,
bo nie było na nie dowodów z logów, ale **prawdopodobnie mają dokładnie ten sam problem** na
dużej książce.

**Dodatkowo znaleziony i naprawiony analogiczny cross-thread bug:** `CheckpointHistory.Changed`
ma dwóch subskrybentów dotykających UI (`MainWindowViewModel.RefreshCheckpointActions`,
`CheckpointsViewModel.Rebuild`) — bez zabezpieczenia odpaliłby się z wątku w tle przy
`Task.Run`-owym `AddCheckpoint`/`Abandon`, tak jak w punkcie 2. Dodano
`CheckpointHistory.SuppressChanged()`/`ResumeChanged()` (Core) — tłumi zdarzenie na czas pracy w
tle, odpala raz (catch-up) po powrocie na UI.

### 6. Drobna poprawka przy okazji: kopiowanie tekstu z panelu Notifications

`TextBlock` w `NotificationsView.axaml` nie wspierał zaznaczania/kopiowania tekstu. Zamieniony na
`SelectableTextBlock` (plus poprawiony selektor stylu `.error`, bo nie dziedziczył koloru po
zmianie typu kontrolki — FluentTheme ma własny, bardziej specyficzny styl dla
`SelectableTextBlock`).

### 7. Rsync WSL→Windows: build zawsze czysty

Osobny temat poruszony po drodze: skrypt rsync do kopiowania repo na Windows do budowania
potrzebuje `--delete --delete-excluded`, inaczej stare `bin`/`obj` z poprzednich buildów
(wykluczone z synchronizacji, ale nigdy nieczyszczone) mogą powodować błędy typu `AVLN2000:
Unable to resolve "!AvaloniaResources"` przez niespójny stan inkrementalnego builda.

## Co NIE zostało zrobione / zostało do następnej sesji

1. **Brak realnego % podczas `FootnoteScanner.Scan`** (etap "Scanning" w Renumber) oraz podczas
   `PlanPlacement`/`PlanConversion` ("Preparing"/"Analyzing"). Użytkownik potwierdził chęć dodania
   tego (raportowanie per-plik: "plik X z Y"), ale **nie zostało zaimplementowane** — w trakcie
   pracy nad tym odkryliśmy pilniejszy problem (checkpoint abandon, punkt 4–5) i tam skierowaliśmy
   uwagę. `FootnoteScanner.Scan(Book book)` iteruje `for (int spine = 0; spine < files.Count;
   spine++)` — dodanie opcjonalnego `IProgress<int>` (plik X z Y) powinno być proste i
   nieinwazyjne (dodatkowy parametr opcjonalny, jak przy `Renumber`/`RenumberProgress`).

2. **Nie zweryfikowane na żywo po najnowszych zmianach** (checkpoint create/abandon w tle +
   granularne logi Renumber) — ostatni test użytkownika na Windows pokazywał STARĄ wersję kodu
   (sprzed backgroundowania checkpointów). Trzeba: zrsyncować, zbudować (pamiętać o
   `--delete --delete-excluded`), powtórzyć scenariusz Renumber + Cancel oraz Change Placement +
   Cancel na tej samej książce, sprawdzić nowe logi `[DBG] Checkpoint: .../Operation: .../
   Renumber: ...`.

3. **Pozostałe synchroniczne miejsca `AddCheckpointBefore`** (patrz punkt 5) — SpellCheck,
   RenameClass, RenameId, RemoveClass, RemoveSpans, RemoveEditionPage, UnifyFootnoteLetters.
   Prawdopodobnie też wieszają UI na dużej książce, ale niezweryfikowane logami — do zrobienia po
   potwierdzeniu.

4. **`RevertToBeforeCheckpoint`/`RevertToAfterCheckpoint`/`RevertToCheckpoint`**
   (`MoveToCheckpoint`) — manualne przechodzenie między checkpointami (Undo/Redo w panelu
   Checkpoints) — też robi `_checkpoints.Undo`/`.Redo`/`.RevertTo` synchronicznie. Nie dotknięte
   dziś, nie zmierzone, ale tej samej kategorii co punkt 5.

5. **Niewyjaśnione:** użytkownik zgłosił, że po zakończeniu Cancel (Renumber) i zamknięciu okna
   postępu, kliknięcie panelu Notifications dało kolejne ~9–20s zawieszenia, mimo że log nie
   pokazuje w tym czasie żadnej zarejestrowanej operacji. Podejrzenie: `BookBrowser.SuspendRebuilds
   = false` (ustawiane w `finally` `RunInBackgroundAsync` po zakończeniu pracy w tle) mogło
   wyzwolić odłożony pełny rebuild drzewa BookBrowser dla 1409 plików, synchronicznie, bez logów.
   Niezweryfikowane — wymaga dodania logu wokół `BookBrowser.SuspendRebuilds` / rebuildu drzewa.

## Co warto zoptymalizować/przyspieszyć (od najdłuższego)

1. **Główna pętla numerująca w `FootnoteEditor.RenumberWith`** (etap "Numbering", ~9551
   przypisów) — największy pojedynczy koszt (dziesiątki-do-stu+ sekund na tej książce). Nie
   profilowane szczegółowo — per-footnote robi m.in. `Snapshot`/`TagLister.For`/`FindAnchor`/
   `FindElementById` (parsowanie/regex). Warto sprawdzić, czy da się ograniczyć powtórne
   parsowanie / przyspieszyć wyszukiwanie elementów po ID.
2. **`FootnoteScanner.Scan`** (~18.7 s) — pełny DOM-parsing (AngleSharp) 1409 plików przy każdym
   wywołaniu. Używane w wielu miejscach (Renumber, PlanPlacement, PlanConversion, GetFootnoteState
   przy każdym otwarciu menu kontekstowego przypisów!). Potencjalny kandydat do cache'owania
   wyniku między operacjami (inwalidacja przy edycji) — **ale to ryzykowniejsza zmiana
   architektoniczna, nie podejmowana dziś bez wyraźnej zgody**.
3. **`BookLinks`/link-scanning w `PlanConversion`/`PlanPlacement`** (~19.5 s) — skan wszystkich
   `<a href>` w całej książce (~43k w tej książce). Podobny charakter do punktu 2.
4. **Checkpoint create/abandon** (2–3 s / 35–37 s) — już przeniesione do tła (punkt 5 wyżej), więc
   nie blokują UI, ale wciąż zajmują realny czas kopiowania/odtwarzania całego folderu książki na
   dysku. Gdyby nadal za wolne nawet w tle (np. bardzo duże książki z obrazami), warto rozważyć
   przyrostowe/różnicowe kopiowanie zamiast pełnej kopii folderu za każdym razem.

## Pliki zmienione w tej sesji (orientacyjnie)

- `src/Signet.Core/Footnotes/FootnoteEditor.cs` — `Renumber`/`RenumberWith` z `IProgress<RenumberProgress>`, nowe typy `RenumberStage`/`RenumberProgress`.
- `src/Signet.Core/Footnotes/FootnoteEditor.Placement.cs`, `FootnoteEditor.Conversion.cs` — dopasowanie wywołań `RenumberWith` do nowej sygnatury.
- `src/Signet.Core/BookManipulation/CheckpointHistory.cs` — `SuppressChanged`/`ResumeChanged`.
- `src/Signet.App/ViewModels/MainWindowViewModel.Footnotes.cs` — sześć operacji przypisów w tle, `RenumberProgressMap`, logi `Preparing footnote placement/conversion`.
- `src/Signet.App/ViewModels/MainWindowViewModel.Checkpoints.cs` — `AddCheckpointBeforeAsync`/`AbandonCheckpointAsync`, logi `"Checkpoint"`.
- `src/Signet.App/ViewModels/MainWindowViewModel.Operations.cs` — logi `"Operation"`, `UpdateNavigationPlace()` po pracy w tle.
- `src/Signet.App/ViewModels/MainWindowViewModel.Navigation.cs` — guard na `_bookEditedInBackground` w `OnNavigationResourcePathChanged`.
- `src/Signet.App/ViewModels/Tabs/ICodeTabHost.cs`, `CodeTabViewModel.cs`, `Views/Tabs/CodeTabView.axaml.cs` — async sygnatury + `DelayedFootnoteProgress`.
- `src/Signet.App/Views/NotificationsView.axaml` — `SelectableTextBlock`.
- `src/Signet.App/Resources/Strings.resx`, `Strings.en.resx` — nowe stringi progresu/tytułów.
- Testy: `tests/Signet.Core.Tests/Footnotes/FootnoteEditorTests.cs`, `tests/Signet.App.Tests/MainWindowFootnotesTests.cs`, `tests/Signet.App.UiTests/CodeTabViewRenderTests.cs`, `tests/Signet.App.Tests/MainWindowViewModelTests.cs` (dodane `UiCultureScope`), `tests/Signet.App.Tests/CheckpointsTests.cs` (test przerobiony na `async Task` + `WhenIdleAsync`).
