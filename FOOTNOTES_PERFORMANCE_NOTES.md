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

### 7. Naprawiony crash znaleziony na żywo: wyścig między operacjami po backgroundowaniu checkpointów

Po wdrożeniu punktu 5 użytkownik przetestował na żywo na Windows scenariusz: Renumber → Cancel →
Renumber (dwa razy pod rząd). Drugie uruchomienie zakończyło się realnym crashem:

```
System.ObjectDisposedException: Cannot access a disposed object.
  at FootnoteEditor.<>c__DisplayClass81_0.<RenumberWith>g__Snapshot|1(HtmlResource file)
  at FootnoteEditor.RenumberWith(...)
```

Z logów (`[DBG] Checkpoint`/`Operation`): drugie kliknięcie "Renumber Footnotes" (`17:39:30.227`,
checkpoint utworzony `17:39:32.306`) wystartowało **podczas** gdy przywracanie książki po Cancel z
pierwszego uruchomienia wciąż trwało w tle (`abandoning...` od `17:39:25.153`, `abandoned, book
restored` dopiero `17:39:39.553` — **9 s później**). Drugie uruchomienie zeskanowało książkę
(`FootnoteScanner.Scan`, ~18.6 s), ale do czasu, gdy doszło do etapu Numbering, stare przywracanie
checkpointu już zdążyło podmienić/zwolnić `_currentBook` i jego zasoby — `file.GetText()` w
`Snapshot()` dostał już usunięty (disposed) `HtmlResource`.

To jest dokładnie ten sam rodzaj wyścigu co w punkcie 2, tylko po stronie checkpointów: odkładając
`AddCheckpointBeforeAsync`/`AbandonCheckpointAsync` do tła (punkt 5), UI przestało być zablokowane
*przez cały czas* operacji — więc użytkownik mógł kliknąć drugą operację, zanim pierwsza naprawdę
się skończyła (łącznie z odtwarzaniem książki po Cancel).

**Naprawione:** dodano flagę `_bookOperationBusy`
(`MainWindowViewModel.Operations.cs`) + `TryEnterBookOperation()`/`ExitBookOperation()`.
Obejmuje całe `RunBookOperationAsync` (checkpoint → praca → ewentualne odtworzenie po Cancel) oraz
osobno owinięto nią `ConvertFootnotesAsync`, pięć metod edycji przypisu i
`PrepareFootnotePlacementAsync`/`PrepareFootnoteConversionAsync` (te dwie nie biorą checkpointu, ale
czytają książkę w tle, więc też muszą czekać). Druga próba startu dowolnej z tych operacji, gdy
poprzednia jeszcze trwa (razem z końcówką Cancel), jest **odrzucana synchronicznie** — status bar
pokazuje `Status_OperationBusy`, bez żadnej pracy w tle, więc nic nie wyścigowo dotyka książki.
Test regresyjny: `MainWindowFootnotesTests.Starting_a_new_operation_while_a_cancelled_one_is_still_restoring_the_book_is_refused`
(potwierdzone, że bez poprawki test faktycznie się wywala tym samym rodzajem błędu).

Zakres: naprawione tylko tam, gdzie operacje już są asynchroniczne (idą przez
`RunBookOperationAsync`/`AddCheckpointBeforeAsync`/`AbandonCheckpointAsync`/`RunInBackgroundAsync`
bezpośrednio) — czyli wszystko dotknięte w tej i poprzedniej sesji. Pozostałe, w pełni
synchroniczne miejsca (`SpellCheck`, `RenameClass`, `RenameId`, `RemoveClass`, `RemoveSpans`,
`RemoveEditionPage`, `UnifyFootnoteLetters`) **nie sprawdzają tej flagi** — teoretycznie mogłyby
nadal się wyścigowo zderzyć z operacją działającą w tle (patrz punkt 3 poniżej), ale to nie był
zgłoszony/zweryfikowany scenariusz i nie zostało dziś dotknięte.

### 8. Rsync WSL→Windows: build zawsze czysty

Osobny temat poruszony po drodze: skrypt rsync do kopiowania repo na Windows do budowania
potrzebuje `--delete --delete-excluded`, inaczej stare `bin`/`obj` z poprzednich buildów
(wykluczone z synchronizacji, ale nigdy nieczyszczone) mogą powodować błędy typu `AVLN2000:
Unable to resolve "!AvaloniaResources"` przez niespójny stan inkrementalnego builda.

### 9. Naprawione (druga sesja): realny % podczas skanowania przypisów

`FootnoteScanner.Scan` dostał opcjonalny `IProgress<ScanProgress>` (nowy publiczny typ
`ScanProgress(Done, Total)` w `Signet.Core.Footnotes`, plik `FootnoteScanner.cs`), raportujący po
każdym pliku. Wątek przez trzy miejsca, które już go wołały:
- `FootnoteEditor.RenumberWith` — etap `RenumberStage.Scanning` ma teraz realny `Done`/`Total`
  zamiast tylko 0/0 na starcie i total/total na końcu (adapter `ScanProgressRelay`).
- `FootnoteEditor.PlanPlacement`/`PlanConversion` — dostały opcjonalny `IProgress<ScanProgress>?`
  i przekazują go prosto do `Scan`.

Po stronie App: `MainWindowViewModel.Footnotes.cs` ma nową `ScanProgressMap` (mapuje `ScanProgress`
na `WorkProgress` z formatem "plik X z Y"), użytą w `PrepareFootnotePlacementAsync`/
`PrepareFootnoteConversionAsync` (nowy parametr `analysisProgress`). `ProgressWindow.RunAsync<T>`
zmienił sygnaturę z `Task<T?> work` na `Func<IProgress<WorkProgress>, Task<T?>> work`, żeby fazy
"Analizowanie…" (Change Placement / Convert Footnotes) też mogły pokazywać realny procent, nie
tylko nieokreślony spinner — jedyni dwaj wołający (`MainWindow.axaml.cs`) dostali odpowiadające
zmiany. Nowe stringi (PL/EN): `FootnotePlacement_Progress_Analyzing`,
`FootnoteConversion_Progress_Analyzing`, zmieniony `FootnoteRenumber_Progress_Scanning` (dodane
`{0}`/`{1}`).

### 10. Naprawione (druga sesja): pozostałe synchroniczne `AddCheckpointBefore` przeniesione do tła

Siedem operacji z punktu 3 (lista poniżej) dostało ten sam wzorzec co pięć szybkich edycji
przypisów (`AddFootnote` itp.): `TryEnterBookOperation`/`ExitBookOperation`,
`AddCheckpointBeforeAsync`, ciężka edycja przez `RunInBackgroundAsync`. Okno progresu: delay 300 ms,
bez Cancel (`ProgressWindow.DelayedRunner(Window)` — nowy wspólny helper w `ProgressWindow.axaml.cs`,
zastąpił prywatny `CodeTabView.DelayedFootnoteProgress`, który robił to samo tylko dla przypisów).

- `ICodeTabHost.ApplyClassRename`/`ApplyIdRename`/`ApplyClassRemoval`/`ApplySpanRemovalInBook` —
  zmienione na `Task` z nowym parametrem `ProgressRunner? runWithProgress = null`; wołający w
  `CodeTabView.axaml.cs`/`CodeTabViewModel.ApplySpanRemoval` dostają `ProgressWindow.DelayedRunner(owner)`.
- `IEditionPageHost.RemoveEditionPage()` → `RemoveEditionPageAsync()` (`Task<bool>`). Wołane z
  `PreferencesViewModel` (okno Preferencji), które nie ma dotąd żadnego dostępu do `Window` dla
  okna progresu — **uruchamiane w tle bez okna progresu** (`runner: null`), pragmatyczny wybór dla
  tej jednej, rzadkiej operacji, żeby nie dodawać nowego przewodu Window przez
  `PreferencesViewModel`/`PreferencesWindow` tylko dla tego przycisku.
- `MainWindowViewModel.ApplySpellcheckChangeAll` (prywatna, event handler `ChangeAllRequested`) —
  z tego samego powodu (brak Window w miejscu podpięcia zdarzenia) też bez okna progresu; podpięta
  teraz przez `RunAsyncAction` (`ApplySpellcheckChangeAllAsync`), żeby testy mogły czekać na
  `WhenIdleAsync()`.
- `UnifyFootnoteLetters` — ma Window w `MainWindow.axaml.cs` (`OnFootnoteLettersRequested`), więc
  dostał pełny `ProgressWindow.DelayedRunner(this)` wątkiem przez `PrepareFootnoteLetters`.
  `FootnoteLettersViewModel.UnifyCommand` zmienione z `RelayCommand` na `AsyncRelayCommand`
  (`IAsyncRelayCommand`), `_apply` z `Func<..., bool>` na `Func<..., Task<bool>>`.

### 11. Naprawione (druga sesja): Revert do checkpointu (Undo/Redo/panel) w tle

`RevertToBeforeCheckpoint`/`RevertToAfterCheckpoint`/`RevertToCheckpoint` (`MoveToCheckpoint`) —
ten sam problem jak `Abandon` (`CheckpointHistory.Undo`/`.Redo`/`.RevertTo` → `MoveTo` → `OnChanged()`
z wątku w tle), więc dostały ten sam lek: `_checkpoints.SuppressChanged()`/`.ResumeChanged()` wokół
`RunInBackgroundAsync`. W przeciwieństwie do punktu 10 (szybkie, pojedyncze edycje) Revert to
zawsze pełne przeładowanie folderu stanu — tej samej kategorii kosztu co `Abandon` (35+ s na dużej
książce) — więc dostało okno progresu **od razu, bez delay'u** (domyślny `ProgressRunner` główne
okno, jak `Renumber Footnotes`), bez Cancel (nie ma tu sensownego "cofnij w pół drogi"). Wołający
(`_actions.SetHandler(RevertToBefore/After)`, `CheckpointsViewModel.RevertRequested`,
`MainWindow.axaml.cs: OnRevertConfirmationRequested`) owinięte przez `RunAsyncAction` lub `await`.

### 12. Niewyjaśnione — logi dodane (trzecia sesja), reprodukcja na żywo wciąż do zrobienia

Użytkownik zgłosił, że po zakończeniu Cancel (Renumber) i zamknięciu okna postępu, kliknięcie
panelu Notifications dało kolejne ~9–20s zawieszenia, mimo że log nie pokazuje w tym czasie
żadnej zarejestrowanej operacji. Podejrzenie: `BookBrowser.SuspendRebuilds = false` (ustawiane w
`finally` `RunInBackgroundAsync` po zakończeniu pracy w tle) mogło wyzwolić odłożony pełny rebuild
drzewa BookBrowser dla 1409 plików, synchronicznie, bez logów.

Dodane logi (kategoria `"BookBrowser"`, `BookBrowserViewModel.cs`):
- `SuspendRebuilds: on` / `SuspendRebuilds: off` — przy każdej realnej zmianie flagi.
- `model changed while suspended, rebuild deferred` — w `OnModelChanged`, gdy zmiana modelu
  przyszła podczas `SuspendRebuilds == true` (potwierdza/zaprzecza, że coś naprawdę czeka).
- `Rebuild: N files (XXX ms)` (albo `Rebuild: no book (...)`) — czas i rozmiar każdego pełnego
  przebudowania drzewa, z `Stopwatch`.

**Wciąż do zrobienia:** odtworzyć scenariusz na Windows z włączonym debug loggingiem (Preferencje
→ Debug → "Enable debug logging") i sprawdzić, czy w logu po Cancel pojawia się `Rebuild` z dużym
czasem w momencie kliknięcia Notifications, czy coś innego (wtedy problem jest poza
`BookBrowserViewModel`, np. w `TreeDataGrid`/renderowaniu, i log tego nie złapie).

### 13. Naprawione (trzecia sesja): O(n²) w `FindElementById`/`FindAnchor` — pasek postępu "Change
    Footnote Placement" wyraźnie zwalniał w trakcie

Użytkownik zgłosił na żywo (zrzut ekranu "Moving footnotes: 4268 of 8923", 24%): pasek postępu w
"Change Footnote Placement (Local / Global)" wyraźnie **zwalnia z czasem**, nie idzie równym
tempem.

**Przyczyna:** `FootnoteEditor.FindElementById`/`FindAnchor` (prywatne, używane w pętli
`ChangePlacement` — i tak samo w `RenumberWith`, `ChangeConversion`, `UnifyFootnoteLetters`, czyli
we wszystkich operacjach na wielu przypisach naraz) skanowały **całą** listę tagów pliku od
indeksu 0 przy **każdym** wywołaniu — liniowo. Wywoływane raz na przypis. Gdy wiele przypisów
dzieli jeden plik w czytanej kolejności (typowy przypadek: plik globalnych przypisów rośnie
przypis po przypisie, albo odwrotnie — przy "global → local" `NoteFile` to cały czas ten sam,
coraz dalej skanowany plik), koszt pojedynczego wywołania rósł wraz z pozycją przypisu w pliku:
pierwsze przypisy — szybkie skanowanie od początku, ostatnie — skanowanie niemal całej (coraz
większej/dalszej) listy tagów. Sumarycznie O(n²) względem liczby przypisów w jednym pliku — i
stąd dokładnie obserwowany efekt "pasek coraz wolniej idzie naprzód", a nie stałe spowolnienie.
To był dokładnie kandydat opisany już wcześniej jako punkt 1 w sekcji "Co warto
zoptymalizować/przyspieszyć" ("Nie profilowane szczegółowo... Warto sprawdzić, czy da się
przyspieszyć wyszukiwanie elementów po ID") — tym razem potwierdzony i naprawiony.

**Naprawa:** indeks `id → pozycja tagu` i `href → lista pozycji tagów <a>` budowany raz na plik
(raz na instancję `TagLister`, cache'owany w `ConditionalWeakTable<TagLister, TagIndex>`), zamiast
liniowego skanu przy każdym wywołaniu. `FindElementById` to teraz słownikowy lookup O(1);
`FindAnchor` to lookup + indeksacja do listy kandydatów po `href` zamiast liczenia `occurrence`-tego
dopasowania od zera. Reszta logiki (dopasowanie `<sup>` wokół linku, `FindCloseTagForOpen`) bez
zmian. Dotyczy wszystkich operacji korzystających z tych dwóch funkcji: Renumber, Change
Placement, Convert Footnotes, Letters — więc powinno też realnie skrócić największy pojedynczy
koszt z sekcji "Co warto zoptymalizować" (główna pętla numerująca w `RenumberWith`).

Zweryfikowane: `dotnet build` (0 ostrzeżeń/błędów), `Signet.Core.Tests` (2781/2782 — jedyny fail to
znany, niezwiązany quirk WSL z punktu "Pliki zmienione w trzeciej sesji" niżej) i
`Signet.App.Tests` (1078/1078) zielone. **Nie zweryfikowane na żywo na dużej książce** — warto
powtórzyć scenariusz z Windows (ta sama książka, Change Placement) i sprawdzić, czy pasek
postępu teraz idzie równym tempem.

### 14. Naprawiony (trzecia sesja): `TempDirTests.Dispose_throws_naming_the_file_that_is_still_open`
    na WSL/Linux

Wcześniej opisywany jako "pre-existing, niezwiązany fail" (patrz niżej, sekcja plików). Na
żądanie użytkownika zbadane i naprawione. Przyczyna: `TempDir.Dispose()` polegał na tym, że
`Directory.Delete(recursive: true)` rzuci `IOException`, gdy plik w środku jest otwarty — prawda
na Windows (NTFS odmawia), ale nie na Linuksie/WSL, gdzie `unlink` zawsze się udaje niezależnie od
otwartych uchwytów (usuwany jest tylko wpis katalogu, i-węzeł żyje dalej, dopóki uchwyt nie
zostanie zamknięty) — czyli na Linuksie realna luka w regule "testy nic po sobie nie zostawiają",
nie tylko czerwony test. Potwierdzone empirycznie (mały probe na tej maszynie): drugi
`FileStream.Open(..., FileShare.None)` na otwarty plik **rzuca** `IOException` nawet na Linuksie
(.NET to emuluje w ramach procesu), ale `File.Delete`/`Directory.Delete` **nie sprawdza** otwartych
uchwytów wcale.

Naprawa: `TempDir.Dispose()` przed usunięciem katalogu próbnie otwiera każdy plik z
`FileAccess.Read, FileShare.None` (`FindOpenFile`) — to wykrywa dowolny otwarty uchwyt niezależnie
od trybu, w jakim go otwarto, na obu platformach, bo żądanie `FileShare.None` od siebie koliduje z
każdym istniejącym uchwytem. Zweryfikowane: `Signet.Core.Tests` 2782/2782 zielone (zniknął pre-
existing fail), `Signet.App.Tests` 1078/1078 bez zmian.

## Co NIE zostało zrobione / zostało do następnej sesji

1. **Częściowo zweryfikowane na żywo** — test po backgroundowaniu checkpointów (punkt 5) znalazł
   realny crash (punkt 7, naprawiony: `_bookOperationBusy`). Trzeba powtórzyć na Windows: Renumber
   → Cancel → Renumber dwa razy pod rząd (dokładny scenariusz z logów), oraz Renumber + Cancel →
   Change Placement (druga operacja innego typu, nie tylko to samo Renumber), sprawdzić że
   pojawia się komunikat "Status_OperationBusy" i że nie ma już crasha; dopiero potem zmierzyć,
   czy same czasy (checkpoint abandon, Scan) faktycznie spadły jak oczekiwano. To samo dotyczy
   teraz Revert (punkt 11) i siedmiu operacji z punktu 10 — **niezweryfikowane na żywo na
   Windows**, tylko testy jednostkowe/headless.

2. **Notifications freeze** — patrz punkt 12 wyżej.

3. **`RemoveEditionPage` i `ApplySpellcheckChangeAll` bez okna progresu** (patrz punkt 10) —
   działają w tle, ale użytkownik nie widzi nic podczas oczekiwania (checkpoint 2–3s na dużej
   książce). Jeśli to się okaże niewystarczające, trzeba dodać przewód `Window`/`ProgressRunner`
   przez `PreferencesViewModel`/`PreferencesWindow` i przez subskrypcję `ChangeAllRequested`.

## Co warto zoptymalizować/przyspieszyć (od najdłuższego)

1. ~~**Główna pętla numerująca w `FootnoteEditor.RenumberWith`**~~ — **zrobione w trzeciej sesji**,
   patrz punkt 13 niżej: `FindElementById`/`FindAnchor` skanowały listę tagów całego pliku od
   zera przy każdym wywołaniu (liniowo), a są wywoływane raz na przypis. Gdy wiele przypisów
   dzieli jeden plik (typowo plik globalnych przypisów), to dawało O(n²) — i to tłumaczy też
   obserwację użytkownika: pasek postępu w "Change Footnote Placement" wyraźnie zwalniał z
   czasem (coraz wolniej szedł naprzód), bo każde kolejne wywołanie skanowało coraz dalej w
   coraz większej liście tagów. Naprawione przez indeks (id → pozycja, href → lista pozycji)
   budowany raz na plik i cache'owany per instancja `TagLister`. Dotyczy wszystkich operacji
   korzystających z tych dwóch funkcji: Renumber, Change Placement, Convert Footnotes, Letters.
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
- `src/Signet.App/ViewModels/MainWindowViewModel.Footnotes.cs` — sześć operacji przypisów w tle, `RenumberProgressMap`, logi `Preparing footnote placement/conversion`, guard `_bookOperationBusy` wokół `ConvertFootnotesAsync`, pięciu edycji przypisu i `Prepare*Async` (punkt 7).
- `src/Signet.App/ViewModels/MainWindowViewModel.Checkpoints.cs` — `AddCheckpointBeforeAsync`/`AbandonCheckpointAsync`, logi `"Checkpoint"`.
- `src/Signet.App/ViewModels/MainWindowViewModel.Operations.cs` — logi `"Operation"`, `UpdateNavigationPlace()` po pracy w tle, flaga `_bookOperationBusy` + `TryEnterBookOperation`/`ExitBookOperation` (punkt 7).
- `src/Signet.App/ViewModels/MainWindowViewModel.Navigation.cs` — guard na `_bookEditedInBackground` w `OnNavigationResourcePathChanged`.
- `src/Signet.App/ViewModels/Tabs/ICodeTabHost.cs`, `CodeTabViewModel.cs`, `Views/Tabs/CodeTabView.axaml.cs` — async sygnatury + `DelayedFootnoteProgress`.
- `src/Signet.App/Views/NotificationsView.axaml` — `SelectableTextBlock`.
- `src/Signet.App/Resources/Strings.resx`, `Strings.en.resx` — nowe stringi progresu/tytułów.
- Testy: `tests/Signet.Core.Tests/Footnotes/FootnoteEditorTests.cs`, `tests/Signet.App.Tests/MainWindowFootnotesTests.cs` (plus nowy test regresyjny na race z punktu 7), `tests/Signet.App.UiTests/CodeTabViewRenderTests.cs`, `tests/Signet.App.Tests/MainWindowViewModelTests.cs` (dodane `UiCultureScope`), `tests/Signet.App.Tests/CheckpointsTests.cs` (test przerobiony na `async Task` + `WhenIdleAsync`).
- `src/Signet.App/Resources/Strings.resx`, `Strings.en.resx` — nowy string `Status_OperationBusy` (punkt 7).

## Pliki zmienione w drugiej sesji (punkty 9–11)

- `src/Signet.Core/Footnotes/FootnoteScanner.cs` — `Scan` z `IProgress<ScanProgress>?`, nowy typ `ScanProgress`.
- `src/Signet.Core/Footnotes/FootnoteEditor.cs` — `ScanProgressRelay`, `RenumberWith` przekazuje progres do `Scan`.
- `src/Signet.Core/Footnotes/FootnoteEditor.Placement.cs`, `FootnoteEditor.Conversion.cs` — `PlanPlacement`/`PlanConversion` z `IProgress<ScanProgress>?`.
- `src/Signet.App/Views/ProgressWindow.axaml.cs` — `RunAsync<T>` przyjmuje teraz `Func<IProgress<WorkProgress>, Task<T?>>` (był `Task<T?>`), nowy `DelayedRunner(Window)`.
- `src/Signet.App/Views/Tabs/CodeTabView.axaml.cs` — `ApplyClassRename`/`ApplyIdRename`/`ApplyClassRemoval`/`RemoveSpan` z `await` + `ProgressWindow.DelayedRunner(owner)`; usunięty prywatny `DelayedFootnoteProgress`.
- `src/Signet.App/ViewModels/Tabs/ICodeTabHost.cs`, `CodeTabViewModel.cs` — cztery metody `Apply*` zmienione na `Task` z `ProgressRunner?`.
- `src/Signet.App/Services/IEditionPageHost.cs` — `RemoveEditionPage()` → `RemoveEditionPageAsync()` (`Task<bool>`).
- `src/Signet.App/ViewModels/MainWindowViewModel.cs` — cztery operacje `ICodeTabHost`/`IEditionPageHost` + `ApplySpellcheckChangeAllAsync` w tle za `TryEnterBookOperation`/`AddCheckpointBeforeAsync`/`RunInBackgroundAsync`.
- `src/Signet.App/ViewModels/MainWindowViewModel.Footnotes.cs` — `ScanProgressMap`, `PrepareFootnotePlacementAsync`/`PrepareFootnoteConversionAsync` z `analysisProgress`, `UnifyFootnoteLetters` → `Task<bool>` z `runWithProgress`, `PrepareFootnoteLetters` z `runWithProgress`.
- `src/Signet.App/ViewModels/MainWindowViewModel.Checkpoints.cs` — `MoveToCheckpoint` → `MoveToCheckpointAsync` (w tle, `SuppressChanged`/`ResumeChanged`), `RevertToBeforeCheckpoint`/`RevertToAfterCheckpoint`/`RevertToCheckpoint` → warianty `*Async`, `RequestRevert` → `RequestRevertAsync`, `RequestRevertToBefore`/`RequestRevertToAfter` → `*Async`.
- `src/Signet.App/Views/MainWindow.axaml.cs` — `OnFootnotePlacementRequested`/`OnFootnoteConversionRequested` (nowy `analysisProgress`), `OnFootnoteLettersRequested` (`DelayedRunner`), `OnRevertConfirmationRequested` (`await`).
- `src/Signet.App/ViewModels/FootnoteLettersViewModel.cs` — `UnifyCommand` `RelayCommand` → `AsyncRelayCommand`, `_apply` → `Func<..., Task<bool>>`.
- `src/Signet.App/ViewModels/PreferencesViewModel.cs` — `RemoveEditionPage` (metoda `[RelayCommand]`) → `async Task`.
- `src/Signet.App/Resources/Strings.resx`, `Strings.en.resx` — `FootnotePlacement_Progress_Analyzing`, `FootnoteConversion_Progress_Analyzing`, `FootnoteRenumber_Progress_Scanning` z `{0}`/`{1}`.
- Testy: `FootnoteScannerTests`-style nowe testy w `FootnotePlacementTests.cs`/`FootnoteConversionTests.cs`/`FootnoteEditorTests.cs` (progres skanowania), `ProgressWindowTests.cs` (nowa sygnatura `RunAsync`), `MainWindowFootnotePlacementTests.cs`/`MainWindowFootnotesTests.cs` (testy `analysisProgress`), `CodeTabViewRenderTests.cs`, `FootnoteLettersViewModelTests.cs`, `FootnoteLettersWindowTests.cs`, `PreferencesViewModelTests.cs`, `MainWindowViewModelTests.cs`, `CheckpointsTests.cs` (kilka testów przerobionych na `async Task` + `SingleThreadContext.RunAsync`/`WhenIdleAsync`, bo Revert/RenameClass/itd. teraz działają w tle).
