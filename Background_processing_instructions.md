# Operacje w tle — podejście i lista zadań

Ten dokument powstał z konkretnego zgłoszenia użytkownika (2026-10-23): za dużo operacji w appce
dzieje się "po cichu" — albo bez żadnego okna progresu, albo z oknem, które nie blokuje
wystarczająco, przez co można trafić na komunikat "poprzednia operacja jeszcze trwa" zamiast po
prostu nie móc kliknąć drugiej operacji. Celem jest ustalić jedno, spójne podejście dla **każdej**
długiej operacji na książce i rozbić wdrożenie na małe, niezależne zadania do zrobienia jedno po
drugim w kolejnych sesjach.

**Jak korzystać z tego pliku w kolejnej sesji:**
1. Przeczytaj sekcję "Ustalone podejście" niżej — to jest kontrakt, który wszystkie zadania wdrażają.
2. Weź **jedno** zadanie z listy (od góry, chyba że user powie inaczej), zaimplementuj, przetestuj
   (`dotnet build` + odpowiednie `dotnet test`, zgodnie z CLAUDE.md), zaznacz je jako zrobione w tym
   pliku (dopisz krótką notatkę co się zmieniło — tak jak w `FOOTNOTES_PERFORMANCE_NOTES.md`).
3. **Zrób przerwę po każdym zadaniu** — nie łącz kilku zadań w jednej sesji bez wyraźnej prośby
   użytkownika (ryzyko kontekstu/compactowania przy dużych zadaniach).
4. Zadania są zaprojektowane jako niezależne w miarę możliwości, ale kolejność w sekcji "Zadania"
   ma znaczenie tam, gdzie jedno zadanie buduje mechanizm, z którego korzysta kolejne (zaznaczone
   przy zadaniu).

## Co już istnieje (nie wymyślać na nowo)

Z poprzednich sesji (patrz `FOOTNOTES_PERFORMANCE_NOTES.md`, sekcje 5, 7, 9–11) mamy już:
- `MainWindowViewModel.Operations.cs`: `TryEnterBookOperation`/`ExitBookOperation` (flaga
  `_bookOperationBusy`, blokada drugiej operacji — ale dziś **reaktywna**, przez komunikat w
  Notifications, nie przez zablokowanie UI), `RunInBackgroundAsync<T>` (praca w tle pod
  `ProgressRunner`), `RunBookOperationAsync<T>` (checkpoint przed + praca w tle + automatyczny
  rewind checkpointu przy Cancel — **to już realizuje zasadę 1a/1b z zgłoszenia**: operacja
  edytująca książkę jest `cancellable` tylko wtedy, gdy checkpoint faktycznie powstał).
- `Signet.App/Infrastructure/WorkProgress.cs`: `WorkProgress(Message, Fraction)`,
  `WorkProgress.InStage(...)` (do składania etapów w jeden pasek), `ProgressRunner` delegate.
- `Signet.App/Views/ProgressWindow.axaml(.cs)`: okno modalne (`ShowDialog` — blokuje właściciela),
  pasek z `%`, Cancel (chowany/disabled zależnie od `cancellable`), `DelayedRunner` (pokazuje się
  dopiero po 300 ms, bez Cancel — dla szybkich edycji pojedynczego przypisu), `RunWithProgressAsync`
  (z Cancel), `RunAsync<T>` (zwraca wynik albo `null` przy Cancel), `ShowWhileAsync` (bez Cancel,
  bez %, tylko komunikat — używane dziś do Open/Save/New książki przez `IFileWorkflowPrompts`).
- Przykłady fazowego raportowania: `RenumberStage`/`RenumberProgress`, `PlacementStage`/
  `PlacementProgress`, konwersja ma analogiczny enum — już dziś dzielą operację na etapy, ale okno
  pokazuje to jako **jeden** tekst w jednej linii (np. "Numerowanie: 4268 z 8923"), nie jako osobny
  wiersz "faza 2/4".

## Ustalone podejście (kontrakt)

Synteza zgłoszenia (I)–(VI) na konkretne reguły, które zadania niżej wdrażają:

1. **Żadna operacja na książce nie może zacząć się "po cichu".** Każda operacja, która edytuje
   książkę w tle lub czyta ją na tyle długo, że to się liczy, musi: (a) ustawić stan "zajęte"
   **synchronicznie**, zanim cokolwiek asynchronicznego się zacznie (to już robi
   `TryEnterBookOperation`), (b) pokazać modalne okno progresu — od razu albo po krótkim
   opóźnieniu (dziś 300 ms, `DelayedRunner`, dla operacji, które zwykle są natychmiastowe). Dopóki
   trwa krok (a), żaden **inny** trigger operacji na książce nie może się powieść — nie przez
   komunikat w Notifications po fakcie, tylko przez to, że komenda/przycisk/skrót jest **disabled**
   od momentu (a). To wymaga nowego stanu `IsBookOperationBusy` (czy podobnego) spiętego z
   `CanExecute` komend, nie tylko z `TryEnterBookOperation`.
2. **`runner: null` (operacja bez żadnego okna) jest niedozwolone.** Dziś mają to
   `RemoveEditionPage` i `ApplySpellcheckChangeAllAsync` — wyjątek udokumentowany jako świadomy
   kompromis zakresu w poprzedniej sesji, do naprawienia.
3. **Operacje, o których dziś nie wiadomo, że trwają, a powinno być wiadomo:**
   `AddCheckpointBeforeAsync`/`AbandonCheckpointAsync` (tworzenie/przywracanie checkpointu) nie mają
   dziś **żadnego** okna — a to one, nie "właściwa" praca, odpowiadały w notatkach o wydajności za
   35–37 s zawieszenia. Trzeba je owinąć w okno tak samo jak każdą inną długą operację.
4. **Cancel — macierz zachowań:**
   - operacja **tylko czytająca** książkę (np. walidacja, raport) — Cancel zawsze dostępny, działa
     "w miarę" natychmiast (patrz punkt 6 niżej o częstotliwości sprawdzania tokenu);
   - operacja **edytująca**, dla której checkpoint **powstał** — Cancel dostępny podczas właściwej
     pracy; w momencie startu przywracania checkpointu (`AbandonCheckpointAsync`) Cancel znika/jest
     disabled — to już robi dzisiejszy kod przez stan "Cancelling…", ale przywracanie samo w sobie
     nie ma dziś żadnego widocznego okna (patrz punkt 3) — do spięcia;
   - operacja **edytująca**, dla której checkpoint **nie powstał** (np. nieotwarta historia
     checkpointów) — dziś taka operacja w ogóle nie jest `cancellable` (patrz
     `RunBookOperationAsync`: `cancellable: !editsBook || checkpoint`). Do zweryfikowania, czy to
     rzeczywiście jest to, co chcemy, i czy user to jakoś widzi (brak przycisku Cancel w oknie to
     already wystarczający sygnał, czy potrzeba czegoś więcej?).
   - **tworzenie checkpointu** (`AddCheckpointBeforeAsync`) — **decyzja zatwierdzona w A1
     (2026-10-23):** tak, cancellable. Efekt Cancel podczas tworzenia checkpointu: checkpoint nie
     powstaje, operacja, która go potrzebowała, też się nie zaczyna, książka zostaje nietknięta.
     Wymaga dodania sprawdzania `CancellationToken` w `CopyStateFolder`/`CheckpointHistory.AddCheckpoint`
     (dziś tam nie ma żadnego) i przeprowadzenia tokenu przez `AddCheckpointBeforeAsync` — do
     zrobienia w zadaniu A4.
5. **Okno progresu może mieć więcej niż jedną linię opisu.** Dziś `WorkProgress` niesie tylko jeden
   `Message` + `Fraction`. Docelowo: wiersz "faza" (np. "Faza 2 z 4: Konwertowanie") pokazywany
   zawsze, gdy operacja ma rozpoznawalne fazy — nawet bez dokładnego %, z paskiem indeterminate na
   czas tej fazy — plus opcjonalny trzeci wiersz ze szczegółem (np. "plik 120 z 405"), **tylko
   gdy dana faza faktycznie go raportuje**. Pasek postępu (%) dalej reprezentuje **całą** operację
   (tak jak dziś robi `WorkProgress.InStage`), nie samą bieżącą fazę.
6. **Częstotliwość sprawdzania `CancellationToken`** nie jest ustalana z góry hurtowo. Dodawać/
   poprawiać tylko tam, gdzie ręczny test na dużej, prawdziwej książce pokaże, że Cancel reaguje
   zauważalnie wolno. Nie ma tu zadania "dodaj token wszędzie, gdzie się da" — patrz zadanie D1.

## Zadania

Legenda statusu: `[ ]` do zrobienia, `[x]` zrobione (z krótką notatką i commitem/datą).

### Część A — fundament (rób w tej kolejności, każde kolejne zależy od A2)

- [x] **A1. Decyzja: czy i jak można przerwać tworzenie checkpointu.** (2026-10-23) Zweryfikowano
  w kodzie: `CheckpointHistory.AddCheckpoint` → `CopyStateFolder` (kopiowanie plików/folderów przez
  `Directory.CreateDirectory`/`File.Copy`) i `CheckpointHistory.Abandon` nie przyjmują i nie
  sprawdzają dziś żadnego `CancellationToken` — potwierdzone grepem, zero wystąpień w
  `CheckpointHistory.cs` i `MainWindowViewModel.Checkpoints.cs`. `RunBookOperationAsync` przekazuje
  token tylko do `work` (patrz komentarz w `RunInBackgroundAsync`: "the task is not given the
  token: it always starts"), nie do `AddCheckpointBeforeAsync`/`AbandonCheckpointAsync`. User
  zatwierdził rekomendację: tworzenie checkpointu jest cancellable, efekt = checkpoint nie
  powstaje, operacja docelowa się nie zaczyna, książka nietknięta. Decyzja zapisana w "Ustalone
  podejście", punkt 4. Zadanie A4 implementuje token w `CopyStateFolder`/`AddCheckpoint` i
  przeprowadza go przez `AddCheckpointBeforeAsync`.

- [x] **A2. `IsBookOperationBusy` + spięcie z `CanExecute` wszystkich komend, które startują
  operację na książce.** (2026-10-23, commit w `background-processing`) Zrobione dla **wszystkich
  komend podpiętych przez `AppActionRegistry`** (menu górne, toolbar, skróty — to pokrywa
  dokładnie warunek akceptacji z opisu zadania). Pozostałe trzy kategorie entry-pointów (inne
  viewmodels z własnym `RelayCommand`, code-behind bez żadnego `ICommand`) wydzielone jako A2b/A2c/
  A2d niżej — audyt pokazał, że są trzy różne mechanizmy komend w appce, nie jeden.

  Co zrobione:
  - `AppAction.cs`: nowa właściwość `IsBusy` (analogicznie do istniejącego `IsBlocked`) —
    `IsEnabled => _isEnabled && !_isBlocked && !_isBusy`.
  - `AppActionRegistry.cs`: nowa metoda `SetBusy(bool busy, params string[] ids)` (analogicznie do
    `SetEnabled`/`SetBookOpen`).
  - `MainWindowViewModel.Operations.cs`: nowa publiczna właściwość `IsBookOperationBusy`
    (`SetProperty`, prywatny setter) + statyczna tablica `BookOperationActionIds` z identyfikatorami
    wszystkich `AppAction`, które same wołają `RunBookOperationAsync`/`RunInBackgroundAsync` **lub**
    otwierają okno dialogowe, które to robi po kliknięciu własnego Apply/Convert/Unify
    (`MendPrettifyHtml`, `MendHtml`, `AddSoftHyphens`, `RemoveSoftHyphens`, `WellFormedCheckEpub`,
    `RenumberFootnotes`, `ConvertFootnotes`, `UnifyFootnoteLetters`, `ChangeFootnotePlacement`).
    `TryEnterBookOperation`/`ExitBookOperation` ustawiają `IsBookOperationBusy` i wołają
    `_actions.SetBusy(...)` synchronicznie, przed jakimkolwiek `await`.
  - Decyzja projektowa: okna-dialogi (Change Placement / Convert Footnotes / Unify Letters) też są
    na liście, mimo że ich `AppAction`-handler sam nie woła `RunBookOperationAsync` — tylko otwiera
    okno z własnym przyciskiem Apply na innym viewmodelu. Bez tego dałoby się otworzyć okno w
    trakcie Renumber i dopiero Apply by się nie powiodło — niezgodne z warunkiem akceptacji ("nie
    da się w ogóle kliknąć").
  - Test: rozszerzony `Starting_a_new_operation_while_a_cancelled_one_is_still_restoring_the_book_is_refused`
    (`MainWindowFootnotesTests.cs`) — asercje na `sut.IsBookOperationBusy` i na
    `sut.Actions.Require(AppActionIds.ChangeFootnotePlacement).CanExecute(null)` przed/po operacji.
  - `Status_OperationBusy` w Notifications zostaje jako fallback (np. dla skrótu klawiszowego
    złapanego tuż przed disable) — zgodnie z opisem zadania.

- [x] **A2b. Spięcie `IsBookOperationBusy` z komendami dialogów przypisów/spellchecku.**
  (2026-10-23, commit w `background-processing`) Problem był realny: `FootnotePlacementViewModel`/
  `FootnoteLettersViewModel`/`FootnoteConversionViewModel`/`SpellcheckEditorViewModel` nie wiedziały
  o `IsBookOperationBusy` wcale, a dla Spellchecku (jedyny trwały singleton z tej czwórki, żyje
  cały czas działania okna głównego) dało się realnie otworzyć okno, zacząć inną operację z menu, i
  kliknąć "Change All" — trafiając na `TryEnterBookOperation` → `false` → komunikat w Notifications
  zamiast disabled przycisku.

  Co zrobione:
  - Każdy z 4 viewmodeli dostał własną właściwość `IsBookOperationBusy` (styl zgodny z lokalnym
    idiomem każdej klasy: `[ObservableProperty]` + `[NotifyCanExecuteChangedFor]` w
    `FootnoteConversionViewModel`, ręczny `SetProperty` + `NotifyCanExecuteChanged()` w pozostałych
    trzech) i `CanApply`/`CanUnify`/`CanConvert` zaktualizowane o `&& !IsBookOperationBusy`.
    `SpellcheckEditorViewModel.ChangeAllCommand` nie miał żadnego `CanExecute` — dodany.
  - `FootnotePlacementViewModel`/`FootnoteLettersViewModel`/`FootnoteConversionViewModel` żyją tylko
    tyle, ile otwarte okno (nowa instancja za każdym otwarciem) — spięcie zrobione w widoku
    (`MainWindow.axaml.cs`, `OnFootnotePlacementRequested`/`OnFootnoteLettersRequested`/
    `OnFootnoteConversionRequested`): subskrypcja `vm.PropertyChanged` (filtr po
    `nameof(IsBookOperationBusy)`) ustawiana tuż przed `ShowDialog`/`ShowAsync` i zdejmowana w
    `finally` po jego zakończeniu — działa niezależnie od tego, czy dialog się zamknął przez Apply/
    Unify/Convert czy przez Cancel/X.
  - `SpellcheckEditorViewModel` jest jedynym trwałym singletonem z czwórki (pole `_spellcheckEditor`
    w `MainWindowViewModel`, żyje całą sesję) — spięcie zrobione raz, w konstruktorze
    `MainWindowViewModel`, bez odsubskrybowania (oba obiekty żyją i umierają razem).
  - Testy: po jednym nowym `[Fact]` w `FootnotePlacementViewModelTests.cs`,
    `FootnoteLettersViewModelTests.cs`, `FootnoteConversionViewModelTests.cs`,
    `SpellcheckEditorViewModelTests.cs` — ustawiają `IsBookOperationBusy = true/false` wprost na
    viewmodelu (bez przechodzenia przez `MainWindowViewModel`/widok, które są przetestowane osobno
    w A2) i sprawdzają `CanXxx`/`XxxCommand.CanExecute(null)` w obie strony.
  - `dotnet build` całego `Signet.slnx` + `dotnet test Signet.App.Tests` (1085/1085) +
    `dotnet test Signet.App.MainWindowUiTests` (183/183) — zielone.

- [ ] **A2c. Spięcie `IsBookOperationBusy` z Undo/Redo/RevertTo checkpointów.**
  `CheckpointsViewModel` ma własne komendy wołające `MainWindowViewModel.MoveToCheckpointAsync`
  (przez `RunInBackgroundAsync`) — nie są podpięte przez `AppActionRegistry`. Dziś panel
  Checkpoints nie disabluje tych przycisków w trakcie innej operacji na książce.

- [ ] **A2d. Spięcie `IsBookOperationBusy` z wywołaniami bez żadnego `ICommand` (code-behind).**
  `CodeTabView.axaml.cs` woła bezpośrednio `ICodeTabHost.AddFootnote`/`AddFootnoteReference`/
  `RemoveFootnote`/`RemoveFootnoteReference`/`EditFootnote`/`ApplyClassRename`/`ApplyIdRename`/
  `ApplyClassRemoval`/`ApplySpanRemovalInBook` z menu kontekstowego edytora kodu — dziś enable tych
  pozycji menu to ręczny warunek (`enabled: state.HasFootnotes` itp.), niezwiązany z
  `IsBookOperationBusy`. Podobnie `_validationResults.FixRequested` (przycisk w panelu Validation
  Results, woła `FixValidationResultsAsync`) nie jest `AppAction` i nie ma dziś żadnego disable.
  Do zrobienia: sprawdzać `IsBookOperationBusy` przy budowaniu tych pozycji menu/przycisków (albo
  przy samym kliknięciu, zanim cokolwiek się zacznie).

- [ ] **A3. Zamienić `runner: null` na realne okno.** `RemoveEditionPage`
  (`MainWindowViewModel.cs`, `IEditionPageHost.RemoveEditionPageAsync`) i
  `ApplySpellcheckChangeAllAsync` dziś dostają `runner: null`, bo `PreferencesViewModel` nie ma
  referencji do `Window`, a `ChangeAllRequested` to subskrypcja viewmodel→viewmodel bez widoku.
  Trzeba przeprowadzić `Window`/`ProgressRunner` przez `PreferencesViewModel`/`PreferencesWindow` i
  przez subskrypcję `ChangeAllRequested` (dokładnie to, co poprzednia sesja świadomie odłożyła —
  patrz `FOOTNOTES_PERFORMANCE_NOTES.md`, "Co NIE zostało zrobione", punkt 3). Użyć
  `ProgressWindow.DelayedRunner` (bez Cancel, jak reszta szybkich edycji) — chyba że audyt w C1
  wykaże, że powinny być cancellable.

- [ ] **A4. Owinąć `AddCheckpointBeforeAsync`/`AbandonCheckpointAsync` w okno progresu.** Zależy od
  decyzji A1 (cancellable czy nie). Prawdopodobnie najprościej: dodać im własny `ProgressRunner?`
  parametr (tak jak reszta operacji) i wywoływać przez `RunWithProgressAsync`/`DelayedRunner`
  zamiast gołego `await Task.Run(...)`. Uwaga: to są dwie osobne, krótkie fazy jednej logicznej
  operacji z punktu widzenia użytkownika (np. "Cofanie do checkpointu: tworzenie migawki" /
  "Cofanie do checkpointu: przywracanie") — dobry naturalny pierwszy przypadek użycia dla okna
  wielowierszowego z zadania B1, jeśli B1 jest już zrobione; jeśli nie, zacząć od zwykłego
  jednowierszowego okna i dopiero potem (osobnym małym zadaniem, jeśli trzeba) dodać wiersz fazy.

### Część B — okno z wieloma wierszami (fazy)

- [ ] **B1. Rozszerzyć `WorkProgress`/`ProgressWindow` o opcjonalny wiersz "faza".** Zmiana modelu
  (np. nowe pole `Phase` obok `Message`/`Fraction`, albo osobny typ) + XAML `ProgressWindow.axaml`
  (drugi `TextBlock` nad paskiem, widoczny tylko gdy `Phase` nie jest `null`). Pasek % nadal liczy
  się na całą operację (jak dziś `WorkProgress.InStage`), nie resetuje się między fazami. To
  zadanie tylko **buduje mechanizm** — nie podłącza go jeszcze do żadnej konkretnej operacji (to
  B2–B5).

- [ ] **B2. Fazy w Renumber.** User wskazał to wprost jako najbliższy/najłatwiejszy przypadek:
  `RenumberStage` już istnieje (Scanning → Numbering → …), brakuje tylko pokazania "Faza X z Y: nazwa"
  w oknie. Zależy od B1.

- [ ] **B3. Fazy w Change Footnote Placement.** `PlacementStage` (Preparing/Moving/Finishing +
  Scanning z `ScanProgress`) → wiersz fazy. Zależy od B1.

- [ ] **B4. Fazy w Convert Footnotes.** Analogicznie do B3, dla etapów konwersji. Zależy od B1.

- [ ] **B5. Fazy przy otwieraniu/wczytywaniu książki.** Dziś `ShowProgressAsync`/`ShowWhileAsync`
  (`IFileWorkflowPrompts`) pokazują jeden statyczny komunikat, bez % i bez faz — a to jest
  dokładnie przykład z (III): rozpakowanie epub → szukanie plików → walidacja → wczytywanie. Nawet
  bez dokładnego % w każdej fazie, same nazwy faz + numer X/Y już dają userowi informację, że coś
  się dzieje i na jakim jest etapie. Zależy od B1; wymaga znalezienia, gdzie dokładnie w kodzie
  ładowania książki da się rozróżnić te kroki (prawdopodobnie `Book`/`EpubLoader` czy podobne —
  do zbadania w ramach tego zadania, nie założone tutaj).

### Część C — audyt Cancel

- [ ] **C1. Audyt dostępności Cancel na wszystkich dzisiejszych wywołaniach
  `RunBookOperationAsync`/`RunInBackgroundAsync`.** Przejść każde miejsce wywołania, sprawdzić że
  odpowiada macierzy z punktu 4 "Ustalonego podejścia" (read-only = zawsze cancellable; editing +
  checkpoint = cancellable aż do startu przywracania; editing bez checkpointu = nie cancellable);
  naprawić niezgodności. Dobra okazja, żeby rozstrzygnąć pytanie z punktu 4 o tym, czy user ma
  dostawać jakiś sygnał, gdy operacja edytująca nie ma Cancel z powodu braku checkpointu (dziś: po
  prostu nie ma przycisku w oknie — czy to wystarczy?).

- [ ] **C2. Zweryfikować, że start przywracania checkpointu po Cancel jest widoczny i
  nieprzerywalny.** Zależy od A4 (okno dla `AbandonCheckpointAsync` musi już istnieć). Sprawdzić
  realnym testem/manualnie: po kliknięciu Cancel w trakcie długiej operacji edytującej, czy
  użytkownik widzi ciągłość (np. "Cancelling…" → bez przerwy w "Przywracanie checkpointu…", nie
  pusty ekran/zamrożenie między zamknięciem jednego okna a otwarciem drugiego).

### Część D — cadence tokenu (meta, nie konkretne zadanie)

- [ ] **D1. (ciągłe) Przy ręcznym testowaniu na dużych, prawdziwych książkach notować każdą
  operację, w której Cancel reaguje zauważalnie wolno, i zakładać dla niej osobne, konkretne
  zadanie dodania/poprawienia sprawdzania `CancellationToken` w jej pętli.** Świadomie NIE jest to
  lista "dodaj token wszędzie" — zgodnie z (V), robimy to tylko tam, gdzie się realnie okaże, że
  trzeba.

### Część E — dokumentacja

- [x] **E1. Zaktualizować `FOOTNOTES_PERFORMANCE_NOTES.md`.** Zrobione od razu (trzecia sesja, przy
  pisaniu tego dokumentu) zamiast czekać na osobną turę — zmiana była jednozdaniowa. Dodana sekcja
  15 z odnośnikiem do tego pliku, punkty 2 i 3 w "Co NIE zostało zrobione" zaktualizowane, żeby
  wskazywać na zadania A3/A4 tutaj zamiast duplikować opis. `FOOTNOTES_PERFORMANCE_NOTES.md` nie
  został skasowany — zostaje jako dziennik śledztwa (sekcje 1–14), wciąż wartościowy.

## Dygresja (nie zadanie, obserwacja z researchu do tego dokumentu)

Podczas rozpoznania pod ten dokument znalazłem, że `AddCheckpointBeforeAsync`/
`AbandonCheckpointAsync` nie mają dziś żadnego okna progresu (`await Task.Run(...)` wprost, bez
`ProgressRunner`) — to bardzo prawdopodobny winowajca "9–20s zawieszenia po Cancel + kliknięcie
Notifications" z `FOOTNOTES_PERFORMANCE_NOTES.md` sekcja 12 (dodane tam logi "BookBrowser" mogą
wyjść puste, bo problem wcale nie jest w `BookBrowser`, tylko w tym, że `AbandonCheckpointAsync`
po prostu jeszcze pracuje bez żadnego widocznego znaku). Warto to zweryfikować jako pierwsze przy
zadaniu A4 — może się okazać, że to naprawia zarówno A4, jak i niewyjaśniony freeze z punktu 12,
jednym ruchem.
