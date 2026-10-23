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
5. **Okno progresu może mieć więcej niż jedną linię opisu** (uzgodnione z userem przy B1,
   2026-10-23 — to **zastępuje** wcześniejszy zapis, wedle którego pasek liczył całą operację).
   Operacja złożona z faz pokazuje: tytuł okna = nazwa operacji; wiersz fazy ("Faza 4 z 20:
   Budowanie ściany 1"); opcjonalny wiersz szczegółu ("stawianie cegły: 45/457", **tylko gdy faza go
   raportuje**); pasek z %. **Pasek należy do bieżącej fazy i zaczyna się od zera w każdej nowej
   fazie**; "4 z 20" mówi, gdzie jesteśmy w całości. `Fraction` to postęp najgłębszego poziomu,
   który raport podaje: licznik szczegółu, a gdy go nie ma — sama faza; `null` = pasek indeterminate
   bez % (faza, której postępu nie da się określić — **tego przypadku ma być jak najmniej**, bo nie
   widać, kiedy faza się skończy). Operacja z jedną fazą nie pokazuje "Faza 1 z 1" — działa jak
   dotąd (`Message` + `Fraction`, bez `Phase`). Dla fazowych raportów `WorkProgress.InStage` nie
   służy (składa udziały etapów w jeden pasek) — faza dostaje `FractionOf(done, total)`.
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

- [x] **A2c. Spięcie `IsBookOperationBusy` z Undo/Redo/RevertTo checkpointów.** (2026-10-23, commit
  w `background-processing`) Doprecyzowanie opisu zadania wobec realnego kodu: przycisk Revert
  panelu Checkpoints nie woła `MoveToCheckpointAsync` bezpośrednio — podnosi event
  `RevertRequested`, który `MainWindowViewModel.InitCheckpoints()` subskrybuje i dopiero to woła
  `RevertToCheckpointAsync` → `MoveToCheckpointAsync` (ten już poprawnie robi
  `TryEnterBookOperation`/`ExitBookOperation` — to nie był problem). Problem był odwrotny: podczas
  **innej** operacji na książce (np. Renumber) panel Checkpoints i akcje menu "Revert to before/
  after" nie wiedziały o `IsBookOperationBusy` i dawały się kliknąć, trafiając na komunikat w
  Notifications zamiast disabled przycisku/pozycji menu.

  Co zrobione:
  - `AppActionIds.RevertToBefore`/`RevertToAfter` dopisane do `BookOperationActionIds`
    (`MainWindowViewModel.Operations.cs`) — menu/toolbar/skrót disabled przez istniejący mechanizm
    `AppActionRegistry.SetBusy` z A2, tak jak reszta.
  - `CheckpointsViewModel`: nowa `IsBookOperationBusy` (styl jak w `SpellcheckEditorViewModel` —
    jedyny inny trwały singleton), `CanActOnSelection()` rozdzielone na dwa predykaty: `Compare`
    zostaje przy `CanActOnSelection` (tylko czyta, nie przenosi książki), `Revert` dostaje nowy
    `CanRevert() => CanActOnSelection() && !IsBookOperationBusy`.
  - `CheckpointsViewModel` to trwały singleton (pole `_checkpointsPanel`, tworzony raz w
    `InitCheckpoints()` z konstruktora `MainWindowViewModel`, żyje całą sesję) — spięcie dopisane
    do istniejącego handlera `PropertyChanged` z A2b (ten sam handler aktualizuje teraz i
    `_spellcheckEditor`, i `CheckpointsPanel`).
  - Test: nowy `[Theory]` w `CheckpointsTests.cs` — startuje niezwiązaną operację (Renumber
    Footnotes, nie wymaga przypisów) z kontrolowanym `ProgressRunner`, sprawdza że w trakcie
    `panel.RevertCommand.CanExecute(null)` i `AppActionIds.RevertToBefore/RevertToAfter` są
    `false`, a po zakończeniu znów `true` (po ponownym zaznaczeniu wiersza — Renumber sam tworzy
    checkpoint, co przestawia wybór panelu na nowy stan bieżący, niezależnie od tej zmiany).
  - `dotnet build` + `dotnet test Signet.App.Tests` (1086/1086) — zielone.

- [x] **A2d. Spięcie `IsBookOperationBusy` z wywołaniami bez żadnego `ICommand` (code-behind).**
  (2026-10-23, commit w `background-processing`)

  Co zrobione:
  - `ICodeTabHost` (`src/Signet.App/ViewModels/Tabs/ICodeTabHost.cs`): nowa właściwość
    `bool IsBookOperationBusy { get; }`, zaimplementowana w `MainWindowViewModel` jako
    `bool ICodeTabHost.IsBookOperationBusy => IsBookOperationBusy;`.
  - `CodeTabView.axaml.cs`, `OnContextMenuOpening`: menu kontekstowe jest i tak budowane od zera
    przy **każdym** otwarciu (`ContextMenu.Opening`), więc wystarczyło odpytać
    `host?.IsBookOperationBusy == true` raz na początek metody i dopisać `&& !busy` do `enabled:`
    pozycji, które realnie startują operację na książce: "Remove span" → "the whole book" (nie
    "this file"/"this paragraph" — te są lokalną, synchroniczną edycją w edytorze, nie operacją na
    książce), "Rename Class", "Remove Class", "Rename Id", oraz w podmenu "Footnotes": Edit/Remove
    Reference/Remove/Add/Add Reference **i** Renumber/Convert/Unify Letters — te trzy ostatnie
    wołają `host.ExecuteAction`/`host.ShowFootnoteConversion`/`host.ShowFootnoteLetters`
    **bezpośrednio**, mijając `AppActionRegistry` całkowicie, więc A2's `SetBusy` ich tu nie
    pokrywał mimo że mają swój `AppAction` w głównym menu. "Find Usages"/"Find Id Usages" (tylko
    czytają) i "Go to" zostały bez zmian.
  - Panel Validation Results: `ValidationResultsViewModel` (trwały singleton jak Spellcheck/
    Checkpoints) dostał `IsBookOperationBusy`; `HasFixable`/`CanFixSelection` (bindowane do
    `IsEnabled` przycisków "Fix"/"Fix all") są teraz "jest coś do poprawienia" **i**
    `!IsBookOperationBusy` — osobne prywatne pola (`_anyRowFixable`/`_selectionFixable`) trzymają
    surowy fakt, żeby zmiana samego busy mogła przeliczyć obie właściwości bez ponownego
    skanowania `Rows`/zaznaczenia. `ValidationResultsView.axaml.cs`'s `OnRowMenuOpening` (pozycja
    "Fix" menu kontekstowego tabeli) dostał to samo AND-owanie. Spięcie dopisane jako trzecia linia
    do istniejącego handlera `PropertyChanged` z A2b/A2c.
  - Testy: nowy `[Fact]` w `CheckBookPanelTests.cs` (`HasFixable`/`CanFixSelection` w obie strony);
    nowy `[AvaloniaFact]` w `tests/Signet.App.UiTests/CodeTabViewRenderTests.cs` — otwiera realne
    menu kontekstowe (`ContextRequestedEventArgs`) na pozycji "Rename Class" z kontrolowanym
    `FakeHost.IsBookOperationBusy` i sprawdza `MenuItem.IsEnabled` w obie strony (jedyny istniejący
    wzorzec testu na poziomie widoku dla tego menu — żadnego testu enable/disable nie było tu
    wcześniej, dopisany od zera). `FakeHost` (klasa testowa w tym pliku) i `ICodeTabHost`-Moq-mock w
    `CodeTabContextMenuTests.cs` zaktualizowane/sprawdzone po dodaniu nowego członka interfejsu.
  - `dotnet build Signet.slnx` + `dotnet test Signet.App.Tests` (1087/1087) +
    `dotnet test Signet.App.UiTests` (279/279) — zielone.

- [x] **A3. Zamienić `runner: null` na realne okno.** (2026-10-23, commit w `background-processing`)
  Korekta założenia zadania po audycie kodu: `runner: null` leży w `MainWindowViewModel`
  (`RemoveEditionPageAsync`, `ApplySpellcheckChangeAllAsync`), **nie** w `PreferencesViewModel` czy w
  subskrypcji `ChangeAllRequested` — te dwa miejsca nie mają `Window`, ale `runner: null` nie jest
  w ich kodzie. `MainWindowViewModel` **już ma** dostęp do swojego własnego `ProgressRunner` (pole
  ustawiane przez `MainWindow.axaml.cs`) — nie trzeba żadnego przewodu przez
  `PreferencesViewModel`/`PreferencesWindow`/`SpellcheckEditorViewModel`/`SpellcheckEditorWindow`.

  Co zrobione:
  - Nowa właściwość `MainWindowViewModel.DelayedProgressRunner` (`MainWindowViewModel.Operations.cs`,
    analogiczna do istniejącego `ProgressRunner`, ustawiana przez widok).
  - `MainWindow.axaml.cs`: `_boundViewModel.DelayedProgressRunner = ProgressWindow.DelayedRunner(this);`
    tuż przy istniejącym ustawieniu `ProgressRunner`.
  - `RemoveEditionPageAsync`/`ApplySpellcheckChangeAllAsync`: `runner: null` → `DelayedProgressRunner`
    (okno po 300 ms, bez Cancel — zgodnie z zaleceniem zadania dla "reszty szybkich edycji").
  - Testy: nowy `[Fact]` `RemoveEditionPageAsync_runs_under_the_delayed_progress_runner` i
    rozszerzony `SpellcheckEditor_ChangeAll_replaces_word_in_all_files_and_reloads_open_tabs`
    (`MainWindowViewModelTests.cs`) — ustawiają `sut.DelayedProgressRunner` i sprawdzają, że
    operacja przechodzi przez niego (tytuł, `cancellable: false`), zamiast biegnąć bez żadnego okna.
  - `dotnet build Signet.slnx` + `dotnet test Signet.App.Tests` (1088/1088) — zielone.

- [x] **A4. Owinąć `AddCheckpointBeforeAsync`/`AbandonCheckpointAsync` w okno progresu.**
  (2026-10-23, commit w `background-processing`) B1 nie jest zrobione, więc zgodnie z opisem
  zadania zrobione jako zwykłe, jednowierszowe okno (`DelayedProgressRunner`, bez wiersza fazy).

  Co zrobione:
  - `CheckpointHistory.cs`: `CopyStateFolder` i nowa `AddCheckpointCancellable(Book, string,
    CancellationToken)` sprawdzają token między plikami/folderami — cancel nie zostawia nic (patrz
    test `AddCheckpointCancellable_cancelled_leaves_nothing_behind`). Istniejąca
    `AddCheckpoint(Book, string)` zostaje bez zmian (woła `AddCheckpointCancellable` z
    `CancellationToken.None`) — **osobna nazwa, nie przeciążenie**, bo przeciążenie o tej samej
    nazwie z `CancellationToken` powodowało błąd analizatora `xUnit1051` na **wszystkich**
    dotychczasowych wywołaniach dwuargumentowych w testach (analizator łapie po samej nazwie
    metody, nie po faktycznie wybranym przeciążeniu).
  - `MainWindowViewModel`: nowa `DelayedProgressRunner` (patrz A3) używana teraz też przez
    `AddCheckpointBeforeAsync` (`cancellable: true`, zgodnie z decyzją A1) i `AbandonCheckpointAsync`
    (`cancellable: false` — przywracanie samo jest już tym, co Cancel zaczął, nie ma już "dalszego
    wstecz"). Nowe klucze zasobów `Checkpoint_AbandonProgressTitle` (PL/EN) na tytuł okna
    przywracania; tytuł okna tworzenia checkpointu to istniejące `Checkpoint_Before` (nazwa
    checkpointu i tytuł okna to teraz to samo, sensownie).
  - `RunBookOperationAsync`: anulowanie **samego tworzenia checkpointu** teraz przerywa całą
    operację (komunikat `Status_OperationCancelled`, książka nietknięta, operacja nigdy nie
    startuje) — zamiast dotychczasowego "checkpoint się nie udał, operacja i tak rusza bez
    ochrony", które było poprawne dla błędów (IOException i podobne — tam zostaje bez zmian), ale
    złe dla świadomego Cancel przez użytkownika, zgodnie z decyzją A1.
  - `RemoveEditionPageAsync`/`ApplySpellcheckChangeAllAsync` (A3) same nie przechodzą przez
    `RunBookOperationAsync` — dopisany analogiczny `catch (OperationCanceledException)` wokół ich
    własnego wywołania `AddCheckpointBeforeAsync`, żeby anulowanie checkpointu nie uciekało jako
    nieobsłużony wyjątek.
  - Testy: `AddCheckpointCancellable_cancelled_leaves_nothing_behind` (`Signet.Core.Tests`);
    `Cancelling_the_checkpoint_itself_aborts_the_operation_before_it_ever_starts` (nowy) i
    rozszerzony `Cancelling_the_renumbering_brings_the_book_back` (sprawdza tytuły i `cancellable`
    obu wywołań `DelayedProgressRunner` — tworzenie i przywracanie) w `MainWindowFootnotesTests.cs`;
    rozszerzone testy A3 dla RemoveEditionPage/SpellCheck o drugi wpis (checkpoint) w liście wywołań
    runnera.
  - `dotnet build Signet.slnx` + `dotnet test Signet.Core.Tests` (2991/2991) +
    `dotnet test Signet.App.Tests` (1089/1089) + `dotnet test Signet.App.MainWindowUiTests`
    (183/183) — zielone.
  - **Znana niedoróbka do dalszych zadań (C1 albo nowe B-zadanie):** `ProgressWindow.DelayedRunner`
    ignoruje parametr `cancellable` i zawsze pokazuje okno **bez** przycisku Cancel
    (`cancellable: false` na stałe w jego implementacji) — więc mimo że tworzenie checkpointu jest
    teraz mechanicznie przerywalne (token działa, testy to dowodzą), użytkownik **nie ma dziś
    żadnego przycisku**, którym mógłby to zrobić w realnym UI, nawet przy bardzo dużej książce.
    Żeby to naprawić, tworzenie checkpointu przed operacją, która sama ma duże, cancellable okno
    (np. Renumber), powinałoby prawdopodobnie użyć **tego samego** `runner` co operacja główna
    (z realnym Cancel), nie `DelayedProgressRunner` — do rozstrzygnięcia w C1 albo osobnym zadaniu.

### Część B — okno z wieloma wierszami (fazy)

- [x] **B1. Rozszerzyć `WorkProgress`/`ProgressWindow` o opcjonalny wiersz "faza".** (2026-10-23)
  Tylko mechanizm — żadna operacja nie jest jeszcze podłączona (to B2–B5). Zakres zmieniony wobec
  pierwotnego opisu po rozmowie z userem: pasek jest **per faza**, nie per cała operacja (patrz
  punkt 5 kontraktu).

  Co zrobione:
  - `WorkProgress(Message, Fraction, PhaseInfo? Phase = null)` + nowy `PhaseInfo(Index, Count, Name)`
    (`Infrastructure/WorkProgress.cs`). Trzeci parametr jest opcjonalny, więc wszystkie dotychczasowe
    wywołania `new WorkProgress(msg, frac)` działają bez zmian.
  - `ProgressWindow.axaml`: nowy `PhaseText` (SemiBold, domyślnie ukryty) nad `MessageText`.
    `Apply`: z fazą — `PhaseText` = `Progress_Phase` ("Faza {0} z {1}: {2}" / "Phase {0} of {1}:
    {2}"), `MessageText` to szczegół i **chowa się**, gdy raport nie ma `Message`; bez fazy —
    zachowanie bez zmian (jedna linia, `null` zachowuje ostatni tekst).
  - `UiProgress.Report`: sticky `Message` (`null` = zachowaj) obowiązuje tylko dla raportów bez
    fazy; w raporcie z fazą `Message = null` znaczy "brak szczegółu" (inaczej szczegół poprzedniej
    fazy przeciekałby do następnej). `Phase = null` znaczy "brak fazy" — każdy raport fazowy musi
    sam podać fazę.
  - Cancel (`RunWithProgressAsync`) chowa wiersz fazy i pokazuje "Anulowanie…".
  - Testy (`ProgressWindowTests.cs`): faza ze szczegółem i %, faza z % bez szczegółu, faza
    indeterminate, powrót do raportu bez fazy; Cancel w trakcie fazy.
  - `dotnet build Signet.slnx` (0 ostrzeżeń) + `dotnet test Signet.App.Tests` (1089/1089) +
    `dotnet test Signet.App.UiTests` (281/281) — zielone.
  - **Do uwzględnienia w B2–B5:** `ProgressWindow.DelayedRunner` (A4) zawsze chowa Cancel, więc fazy
    przez niego też pokażą wiersz, ale bez Cancel. Dla walidacji rozważyć zebranie 10 krótkich
    kroków po fazie plików w jedną fazę "Dalsze sprawdzenia" (szczegół = nazwa kroku, pasek =
    krok/10), zamiast 10 faz indeterminate; `Preparing` w Placement/Convert jest dziś raportowane
    jako 0 % (pasek stoi) — lepiej indeterminate albo własny licznik.

- [x] **B2. Fazy w Renumber.** (2026-10-23) Pięć etapów `RenumberStage` = pięć faz ("Faza 2 z 5:
  Numerowanie przypisów"). Szczegół = licznik etapu ("przypis 4268 z 8923" w Numbering, "plik 12 z
  40" w pozostałych), pasek = `Done/Total` fazy i zaczyna się od zera w każdej nowej fazie.
  Etap, którego liczba kroków nie jest jeszcze znana (`Total` = 0, np. start skanowania), nie ma
  szczegółu ani %, tylko pasek indeterminate (nowy `WorkProgress.InPhase`) — zamiast dawnego "0 z 0"
  i stojącego paska. Usunięte stare klucze `FootnoteRenumber_Progress_*`, dodane
  `FootnoteRenumber_Phase_*` oraz wspólne `Progress_Detail_File`/`Progress_Detail_Footnote` (PL/EN).
  `InStage` w Renumber nie jest już używany (zostaje dla walidacji i Convert). Test:
  `The_renumbering_reports_each_stage_as_a_phase_of_five_with_its_own_count`
  (`MainWindowFootnotesTests.cs`). Uwaga (zastane, nie ruszane): Numbering raportuje licznik przed
  przetworzeniem przypisu, więc ostatni raport to "n-1 z n".

- [x] **B3. Fazy w Change Footnote Placement.** (2026-10-23) Trzy fazy `PlacementStage`:
  1 Preparing ("Analizowanie przypisów", "plik x z y"), 2 Moving ("Przenoszenie przypisów", "przypis x
  z y"), 3 Finishing ("Zapisywanie plików i numerowanie przypisów", indeterminate).
  **Zmiana w Core:** `FootnoteEditor.ChangePlacement` przekazuje teraz do `FootnoteScanner.Scan`
  postęp (`PlacementScanRelay`), więc `Preparing` niesie `Done/Total` plików (pierwszy raport ma
  `Total` = 0 = liczba nieznana); wcześniej skanowanie całej książki szło bez żadnego postępu, przy
  pasku stojącym na 0 %. Dialog "Analizowanie" przed oknem zmiany (`PrepareFootnotePlacementAsync`,
  `ScanProgressMap`) to osobna operacja z jedną fazą i zostaje bez faz. Usunięte stare klucze
  `FootnotePlacement_Progress_{Preparing,Moving,Finishing}`, dodane `FootnotePlacement_Phase_*`.
  Testy: `The_preparing_stage_counts_the_files_of_the_book_it_scans` (Core) i rozszerzony
  `The_footnotes_move_under_the_progress_runner_behind_a_checkpoint` (App).
  **Zostaje przypadek 3 (indeterminate): faza 3 Finishing.** To zapis plików + `RenumberWith`, który
  dziś jest wołany z `progress: null`, a na dużej książce to prawdopodobnie najdłuższy etap. Da się to
  naprawić, przekazując postęp Renumber do fazy (osobne zadanie — dopisać jako B3b, jeśli user chce).

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
