# Funkcje do przeniesienia z calibre (Edit Book)

Przegląd zmian w edytorze książek calibre („Edit book”) z ostatnich **100 wydań**: od **5.36.0**
(2022-02-04) do **9.16.0** (2026-10-09). Lista zawiera to, co mogłoby się przydać w Signecie.
Interesuje nas tylko edycja książek, szczególnie EPUB. Pominięte są czytnik, biblioteka, serwer
treści, konwersja do innych formatów, sterowniki urządzeń i funkcje AI.

**Źródła:**
- strona https://calibre-ebook.com/whats-new: ma tylko 62 ostatnie wydania (do 6.29);
- `D:\repos\from.net\calibre\Changelog.txt` (62 wydania, 6.29 → 9.16) i `Changelog.old.txt` (dalsze 38
  wydań, 5.36 → 6.28.1). Strona „What's new” jest generowana z tych plików.
- kod calibre: `D:\repos\from.net\calibre\src\calibre\` (ścieżki niżej są względem tego katalogu).

Każdą pozycję sprawdziłem z kodem Signeta (stan gałęzi `main`, 2026-10-09). Pozycje, które Signet już
ma, są w osobnej sekcji na końcu.

Oznaczenia: **Rozmiar** S (do 1 dnia), M (kilka dni), L (duża funkcja). **Priorytet**: moja ocena
przydatności w stosunku do kosztu. Do ustalenia z Tobą.

## Podsumowanie

| Nr | Funkcja | calibre | Rozmiar | Priorytet |
|---|---|---|---|---|
| [CF-01](#cf-01--check-book-sprawdzanie-książki-z-automatycznymi-poprawkami) | Check book: sprawdzanie książki z automatycznymi poprawkami | 6.11–9.16 | L | wysoki |
| [CF-02](#cf-02--compress-images-kompresja-i-konwersja-obrazów) | Compress images: kompresja i konwersja obrazów (PNG/GIF → JPEG/WebP) | 6.17, 9.10, 9.12 | M | wysoki |
| [CF-03](#cf-03--upgrade-book-epub-2--epub-3) | Upgrade book: EPUB 2 → EPUB 3 (z zachowaniem landmarks) | 9.15 (poprawka) | M | wysoki |
| [CF-04](#cf-04--panel-spisu-treści-wpisy-z-bieżącego-pliku) | Panel spisu treści: wyróżnienie wpisów z bieżącego pliku | 9.16 | S | wysoki |
| [CF-05](#cf-05--edytor-spisu-treści-sortowanie-wpisów) | Edytor spisu treści: sortowanie wpisów wg kolejności w książce | 9.12 | S | średni |
| [CF-06](#cf-06--przeglądarka-książki-podgląd-obrazu-po-najechaniu) | Przeglądarka książki: podgląd obrazu po najechaniu myszą | 9.14 | S | średni |
| [CF-07](#cf-07--przeglądarka-książki-przenieś-na-początekkoniec) | Przeglądarka książki: przenieś plik na początek/koniec (Ctrl+Shift+←/→) | 9.6, 6.11 | S | średni |
| [CF-08](#cf-08--przeglądarka-książki-oznacz-plik-jako-nav) | Przeglądarka książki: „Oznacz jako spis treści (NAV)” dla EPUB 3 | 6.6.1 | S | średni |
| [CF-09](#cf-09--sprawdzanie-pisowni-wykluczenia-i-eksport-csv) | Pisownia: wykluczanie słów z WIELKICH LITER, camelCase, snake_case; eksport listy do CSV | 7.10, 7.24 | S | średni |
| [CF-10](#cf-10--zapisane-wyszukiwania-filtr-po-słowach-kluczowych) | Zapisane wyszukiwania: filtr po słowach kluczowych | 9.10 | S | niski |
| [CF-11](#cf-11--wklejanie-z-normalizacją-unicode-nfc) | Wklejanie z normalizacją Unicode do NFC | 8.9 | S | średni |
| [CF-12](#cf-12--podgląd-reset-powiększenia-z-menu-kontekstowego) | Podgląd: reset powiększenia do 100% z menu kontekstowego | 9.8 | S | niski |
| [CF-13](#cf-13--raporty-enter--dwuklik) | Raporty: Enter działa jak dwuklik | 9.0 | S | niski |
| [CF-14](#cf-14--wstaw-tag-z-konfigurowalną-listą) | „Wstaw tag” (Insert tag) z konfigurowalną listą tagów | 9.4 | M | średni |
| [CF-15](#cf-15--pobieranie-zasobów-zewnętrznych) | Pobieranie zasobów zewnętrznych (obrazy, CSS z URL-i) do książki | 7.12 | M | średni |
| [CF-16](#cf-16--osadzanie-i-podzbiór-fontów-embed--subset-fonts) | Osadzanie fontów i podzbiór fontów (embed / subset) | 5.37–7.3 | L | niski |
| [CF-17](#cf-17--selektory-css-level-4-w-analizie-kaskady) | Selektory CSS Level 4 (`:is`, `:where`, `:has`) w analizie kaskady | 9.10 | M | średni |
| [CF-18](#cf-18--drobne-usprawnienia-edytora-i-zapisu) | Drobne: pełna ścieżka książki w pasku stanu, „Zapisz kopię” → otwórz kopię, ostrzeżenie przy pliku tylko do odczytu, szerokość kursora | 6.11–7.9 | S | niski |
| [CF-19](#cf-19--obrazy-avif) | Obrazy AVIF: wyświetlanie w zakładce obrazu i w raportach | 9.16 | S | niski |
| [CF-20](#cf-20--narracja-tekstu-media-overlays-smil) | Narracja tekstu: media overlays (SMIL) z syntezy mowy | 7.21 | L | niski |
| [CF-21](#cf-21--poprawki-błędów-z-calibre-do-sprawdzenia-w-signecie) | Poprawki błędów calibre do sprawdzenia w Signecie | różne | S | średni |

---

## CF-01 — Check book: sprawdzanie książki z automatycznymi poprawkami

**calibre:** narzędzie „Check book” istnieje od dawna. W badanym okresie dostało: automatyczne
poprawki prostych błędów CSS i przejście na stylelint (6.11), poprawkę pustego identyfikatora
pakietu (5.39.1), brak fałszywych ostrzeżeń „unreferenced” dla audio z SMIL (9.0), **pomijanie
całych typów problemów** (9.16) i **aktualizację selektorów CSS przy naprawie niepoprawnych id**
(9.16).

**Kod calibre:**
- `ebooks/oeb/polish/check/`: `parsing.py` (kodowanie, encje, duplikaty i niepoprawne id, tekst
  luzem w `<body>`), `links.py` (zepsute linki, kotwice, niezgodny media-type), `opf.py` (brak NAV,
  NCX, UID, okładki, idref, nieliniowe pozycje spine), `images.py` (uszkodzone obrazy, CMYK), `fonts.py`
  (uszkodzone i nieosadzalne fonty), `css.py` (stylelint), `main.py` (uruchamia wszystko i naprawia);
- `gui2/tweak_book/check.py`: lista problemów, pomoc, „Napraw ten” / „Napraw wszystkie”,
  pomijane reguły (`check_book_skipped_rules`);
- naprawa id: `InvalidId.__call__` w `check/parsing.py` + `polish/css.py: rename_ids_in_css`
  + `polish/replace.py: replace_ids`.

**Signet dziś:** ma tylko „Well-Formed Check EPUB” (wyniki w panelu Validation Results) i walidację
CSS przez W3C. Nie ma kontroli linków, OPF, id, obrazów ani fontów i nie ma automatycznych poprawek.

**Propozycja:** silnik `BookChecker` w `Signet.Core` z regułami (każda ma id, poziom, opis, pomoc
i opcjonalną poprawkę), wyniki w istniejącym panelu Validation Results. Do tego „Napraw” i „Napraw
wszystkie” oraz „Pomiń ten typ problemu” zapisywane w ustawieniach. Kolejność wdrażania:
1. linki i kotwice (zepsute `href`, brakujące `#fragment`), rozjazd media-type w manifeście, brak
   NAV/NCX, puste `dc:identifier` (z poprawką);
2. duplikaty i niepoprawne id z poprawką, która zmienia id we wszystkich plikach, w linkach i **w
   selektorach CSS** (`#old` → `#new`). Signet ma do tego gotową infrastrukturę z `ClassRenamer`
   i analizy kaskady;
3. uszkodzone i CMYK obrazy, uszkodzone fonty;
4. błędy CSS. Stylelint to JS, więc w Signecie raczej parser AngleSharp i własne reguły, bez
   portowania stylelinta.

**Otwarte pytania:** jeden panel z wynikami well-formed czy osobny? Które reguły mają automatyczne
poprawki?

## CF-02 — Compress images: kompresja i konwersja obrazów

**calibre:** „Compress images”: bezstratna optymalizacja PNG/JPEG/WebP albo kompresja stratna
z jakością JPEG/WebP (6.17 dodało WebP). Od 9.10 konwersja **PNG → JPEG/WebP**, od 9.12 **GIF →
JPEG/WebP**, z podmianą nazw i odwołań.

**Kod calibre:** `ebooks/oeb/polish/images.py` (`get_compressible_images`, `convert_png_to_format`,
`convert_gif_to_format`, wątki kompresji), `gui2/tweak_book/polish.py` (okno opcji), `utils/img.py`
(`encode_jpeg`, `encode_webp`, `optimize_png`).

**Signet dziś:** tylko zmiana rozmiaru pojedynczego obrazu w zakładce obrazu
(`Signet.App/Imaging/ImageResizer.cs`, SkiaSharp).

**Propozycja:** okno „Kompresuj obrazy” dla całej książki albo zaznaczonych plików. Lista obrazów
z rozmiarem przed i po (podgląd jak w Cleanup), jakość JPEG/WebP i konwersja PNG/GIF z zachowaniem
przezroczystości: JPEG tylko bez alfy, WebP z alfą. SkiaSharp koduje JPEG, PNG i WebP. Do
bezstratnej optymalizacji PNG trzeba by sprawdzić bibliotekę .NET, bo SkiaSharp nie ma odpowiednika
optipng. Zmiana rozszerzenia przez istniejący mechanizm zmiany nazwy z aktualizacją odwołań
(manifest, `src`, `srcset`, CSS `url()`, SVG).

## CF-03 — Upgrade book: EPUB 2 → EPUB 3

**calibre:** „Upgrade book internals” zamienia EPUB 2 na EPUB 3: tworzy NAV z NCX, przenosi
landmarks z `<guide>`, ustawia `properties` w manifeście (`nav`, `scripted`, `svg`, `cover-image`,
`mathml`), poprawia media-type fontów i obsługuje obfuskację fontów. W 9.15 naprawiono **gubienie
landmarks przy upgradzie**. Pokrewna poprawka z 9.16: przy EPUB 3 → EPUB 3 zachowywać `id` na
elementach `<nav>`, żeby linki do nich działały.

**Kod calibre:** `ebooks/oeb/polish/upgrade.py` (`epub_2_to_3`, `collect_properties`,
`create_nav`, `migrate_obfuscated_fonts`).

**Signet dziś:** ma narzędzia dla EPUB 3 (NCX/guide z NAV, usuń NCX/guide, NAV w spine, aktualizacja
`properties` w manifeście), ale nie ma konwersji EPUB 2 → EPUB 3.

**Propozycja:** akcja „Konwertuj do EPUB 3” złożona w dużej mierze z istniejących kroków: NAV z
NCX + landmarks z `<guide>`, `properties` w manifeście, `version="3.0"` w OPF, metadane EPUB 3
(`dcterms:modified`) i opcjonalnie zostawienie NCX dla czytników EPUB 2. Na koniec checkpoint, żeby
dało się cofnąć.

## CF-04 — Panel spisu treści: wpisy z bieżącego pliku

**calibre (9.16):** panel ToC wyróżnia wpisy, które wskazują na plik otwarty w edytorze.

**Kod calibre:** `gui2/tweak_book/toc.py` (`mark_name_as_current`).

**Signet dziś:** panel spisu treści (`TableOfContentsViewModel`) nie wie, który plik jest
aktywny.

**Propozycja:** po zmianie aktywnej zakładki pogrubić wpisy, których `href` (bez fragmentu)
wskazuje na ten plik, i rozwinąć gałąź do pierwszego z nich. Opcjonalnie przewinąć do niego.

## CF-05 — Edytor spisu treści: sortowanie wpisów

**calibre (9.12):** przycisk sortujący wpisy ToC według kolejności w książce: indeks pliku w spine,
a w obrębie pliku pozycja kotwicy. W 8.0.1 doszło przenoszenie wielu zaznaczonych wpisów naraz.

**Kod calibre:** `gui2/toc/main.py: sort_toc`.

**Signet dziś:** `EditTocWindow` ma dodawanie, usuwanie, przenoszenie i zmianę poziomu (zakresy
wielu wpisów), ale nie ma sortowania.

**Propozycja:** przycisk „Sortuj wg kolejności w książce”, rekurencyjnie na każdym poziomie, z
możliwością cofnięcia w oknie.

## CF-06 — Przeglądarka książki: podgląd obrazu po najechaniu

**calibre (9.14):** po najechaniu myszą na plik obrazu obok listy plików wyskakuje pływający podgląd.

**Kod calibre:** `gui2/tweak_book/file_list.py: ImagePreviewPopup`.

**Signet dziś:** brak. Obraz widać dopiero po otwarciu zakładki albo `ViewImageWindow`.

**Propozycja:** tooltip z miniaturą (np. maks. 256 px) i wymiarami pliku dla wierszy obrazów
w `BookBrowserView`. Miniatury cache'owane i unieważniane po zmianie pliku.

## CF-07 — Przeglądarka książki: przenieś na początek/koniec

**calibre:** Ctrl+Shift+←/→ przenosi plik na początek lub koniec listy (9.6). Skróty do zmiany
kolejności spine z klawiatury (6.11).

**Kod calibre:** `gui2/tweak_book/file_list.py` (obsługa `ControlModifier | ShiftModifier` +
`Key_Left`/`Key_Right`).

**Signet dziś:** jest „Przenieś w górę/w dół” i sortowanie, nie ma „na początek/koniec”.

**Propozycja:** dwie akcje w katalogu akcji (`BookBrowserMoveToTop` / `BookBrowserMoveToBottom`)
z domyślnymi skrótami, działające na zaznaczeniu wielu plików.

## CF-08 — Przeglądarka książki: oznacz plik jako NAV

**calibre (6.6.1):** w EPUB 3 menu kontekstowe pliku HTML ma „Oznacz jako spis treści”, czyli
ustawia `properties="nav"`.

**Kod calibre:** `gui2/tweak_book/file_list.py: mark_as_nav`.

**Signet dziś:** do sprawdzenia, czy „Add Semantics” obejmuje dokument NAV. Osobnej akcji brak.

**Propozycja:** akcja w menu kontekstowym dla XHTML w EPUB 3: przenosi `properties="nav"` na ten
plik i usuwa ją z poprzedniego.

## CF-09 — Sprawdzanie pisowni: wykluczenia i eksport CSV

**calibre:** opcje wykluczania z listy słów: WIELKIE LITERY, słowa z cyframi, camelCase,
snake_case (7.10). Eksport aktualnie widocznej listy do CSV (7.24). Liczba widocznych słów (7.11).

**Kod calibre:** `gui2/tweak_book/spell.py` (`all_caps`, `camel_case_pat`, `snake_case_pat`,
`to_csv`).

**Signet dziś:** jest opcja „sprawdzaj słowa z cyframi”, reszty brak.

**Propozycja:** trzy przełączniki w oknie pisowni (WIELKIE LITERY, camelCase, snake_case), licznik
widocznych słów i „Eksportuj CSV” (słowo, liczba wystąpień, język, poprawne/błędne).

## CF-10 — Zapisane wyszukiwania: filtr po słowach kluczowych

**calibre (9.10):** filtr listy zapisanych wyszukiwań dzieli tekst na słowa i dopasowuje wpis,
gdy każde słowo występuje w nazwie, w dowolnej kolejności.

**Kod calibre:** `gui2/tweak_book/search.py` (`filter_keywords = text.split()`).

**Signet dziś:** `SearchEditorViewModel.Matches` szuka całego tekstu filtra jako podciągu nazwy.

**Propozycja:** dopasowanie wszystkich słów filtra (np. `quote fix` znajdzie „Fix smart quotes”).

## CF-11 — Wklejanie z normalizacją Unicode NFC

**calibre (8.9):** tekst wklejany do edytora jest normalizowany do NFC, więc „é” złożone
z dwóch znaków staje się jednym znakiem.

**Kod calibre:** `gui2/tweak_book/editor/text.py` (`unicodedata.normalize('NFC', …)` przy wklejaniu).

**Signet dziś:** brak normalizacji przy wklejaniu do Code View.

**Propozycja:** normalizacja `string.Normalize(NormalizationForm.FormC)` przy wklejaniu do Code View.
Ważne dla wyszukiwania i pisowni, bo tekst z PDF-ów często ma rozłożone znaki.

## CF-12 — Podgląd: reset powiększenia z menu kontekstowego

**calibre (9.8):** prawy klik w panelu podglądu daje „Reset zoom to 100%”.

**Signet dziś:** reset jest tylko w pasku stanu.

**Propozycja:** pozycja w menu kontekstowym podglądu. Trzeba sprawdzić, czy `NativeWebView`
pozwala na własne menu kontekstowe.

## CF-13 — Raporty: Enter = dwuklik

**calibre (9.0):** w raportach Enter wykonuje tę samą akcję co dwuklik.

**Signet dziś:** `ReportsWindow` obsługuje tylko `DoubleTapped`.

**Propozycja:** obsługa `Key.Enter` w tabelach raportów.

## CF-14 — „Wstaw tag” z konfigurowalną listą

**calibre:** „Insert tag” (smart insert) owija zaznaczenie w tag albo wstawia parę tagów. Lista
tagów w menu jest konfigurowalna, a w 9.4 dostała wygodniejsze okno edycji. Poprawki: zachowanie
zaznaczenia po owinięciu (7.5.1), poprawne działanie, gdy zaznaczenie zaczyna się od `>` (6.28.1).

**Kod calibre:** `gui2/tweak_book/editor/smarts/html.py` (`insert_tag`), `gui2/tweak_book/editor/insert_resource.py`
(okno listy tagów).

**Signet dziś:** są „Zmień nazwę tagu”, „Podziel tag”, „Wstaw tag zamykający” i formatowanie
(Bold/Italic…). Nie ma ogólnego „owiń zaznaczenie w dowolny tag” z listą do wyboru.

**Propozycja:** akcja „Wstaw tag…” z rozwijaną listą ostatnich i ulubionych tagów, wpisywaniem
z atrybutami (np. `span class="smallcaps"`) i zachowaniem zaznaczenia.

## CF-15 — Pobieranie zasobów zewnętrznych

**calibre (7.12):** „Download external resources” znajduje w HTML i CSS odwołania `http(s)://`
do obrazów i arkuszy, pobiera je do książki i podmienia odwołania.

**Kod calibre:** `ebooks/oeb/polish/download.py` (`get_external_resources`,
`download_external_resources`, `replace_resources`).

**Signet dziś:** brak.

**Propozycja:** okno z listą znalezionych URL-i do zaznaczenia, pobieranie przez `HttpClient` z
limitem rozmiaru i timeoutem, nazwa pliku z `Content-Type` (poprawka z 8.0.1), dodanie do manifestu
i podmiana odwołań. Ruch sieciowy tylko na wyraźne polecenie użytkownika.

## CF-16 — Osadzanie i podzbiór fontów (embed / subset fonts)

**calibre:** „Embed referenced fonts” osadza fonty systemowe użyte w CSS (5.37: tworzy `<head>`, gdy
go brak). „Subset embedded fonts” zostawia w fontach tylko używane znaki: WOFF i CID (6.17),
zachowanie wszystkich funkcji OpenType (7.3), `text-transform` (7.8), `:first-line`/`:first-letter`
(6.6.1).

**Kod calibre:** `ebooks/oeb/polish/embed.py`, `ebooks/oeb/polish/subset.py`, `utils/fonts/sfnt/subset.py`.

**Signet dziś:** brak.

**Propozycja:** subset wymaga biblioteki do podzbioru fontów (np. HarfBuzz subset przez
HarfBuzzSharp, do sprawdzenia). Zbieranie użytych znaków na font i grubość wymaga kaskady, a tę
Signet ma. Duży koszt, średnia przydatność, więc priorytet niski.

## CF-17 — Selektory CSS Level 4 w analizie kaskady

**calibre (9.10):** parser CSS obsługuje selektory Level 4 (`:is()`, `:where()`, `:has()`, `:not()`
z listą).

**Signet dziś:** `CssMergeRiskAnalyzer` traktuje selektory Selectors 4 jako ryzykowne (reguła może
wypaść u czytnika), a kaskada w Cleanup, Live CSS i zmianie nazwy klas opiera się na AngleSharp
1.7.2.

**Propozycja:** sprawdzić, które pseudoklasy Level 4 AngleSharp 1.7.2 dopasowuje, i uzupełnić
brakujące. Dzięki temu Live CSS i analiza ryzyka nie pomijałyby takich reguł, a zmiana nazwy klasy
obejmowałaby klasy wewnątrz `:is(...)`.

## CF-18 — Drobne usprawnienia edytora i zapisu

- **Pełna ścieżka edytowanej książki w pasku stanu** (calibre 6.28.1).
- **„Zapisz kopię” → od razu otwórz kopię** w tym samym albo nowym oknie (6.28.1).
- **Ostrzeżenie przed nadpisaniem pliku tylko do odczytu** przy zapisie (6.11); zapis kopii, gdy
  oryginał nie ma prawa zapisu (5.37).
- **Szerokość kursora** w ustawieniach Code View (7.9).

## CF-19 — Obrazy AVIF

**calibre (9.16):** odczyt obrazów AVIF.

**Signet dziś:** `MediaTypes` zna `image/avif`. Do sprawdzenia, czy SkiaSharp 3.119 dekoduje AVIF
w zakładce obrazu, raportach i miniaturach (CF-06). Jeśli nie, pokazać informację zamiast błędu.

## CF-20 — Narracja tekstu (media overlays, SMIL)

**calibre (7.21):** „Add text narration” generuje syntezą mowy audio dla całego tekstu i tworzy
media overlays (SMIL) z podświetlaniem zdań. Dla postaci można wybrać różne głosy.

**Kod calibre:** `ebooks/oeb/polish/tts.py`, `gui2/tweak_book/polish.py`.

**Signet dziś:** brak. Duża funkcja zależna od silnika TTS. Na Windows dałoby się użyć
`Windows.Media.SpeechSynthesis`, ale to łamie przenośność. Priorytet niski, raczej do odłożenia.

## CF-21 — Poprawki błędów z calibre do sprawdzenia w Signecie

Błędy naprawione w calibre, które mogą występować też w Signecie. Każdy do sprawdzenia testem
(najlepiej najpierw test, który pokaże błąd):

| calibre | Problem | Gdzie sprawdzić w Signecie |
|---|---|---|
| 9.14 | Reguła `@charset` przesuwa „skocz do klasy” o jedną pozycję | `GoToLinkOrStyle`, Live CSS, Find Usages |
| 5.44 | `@namespace` psuje skok do definicji reguły | jak wyżej |
| 7.17, 6.26, 6.10 | Znaki spoza BMP (np. emoji, `𝔸`) przesuwają zaznaczenie wyników wyszukiwania i wstawianie formatowania | Find & Replace, `CodeFormatOperations` |
| 5.38 | Słowa za komentarzem HTML nie są sprawdzane pisownią | `HtmlSpellCheck` |
| 5.38 | Dwuklik na słowie zaznacza też otaczające cudzysłowy typograficzne | Code View (AvaloniaEdit) |
| 6.6.1 | Komentarze wewnątrz nieznanych reguł `@` w CSS powodują błąd | parsowanie CSS (Cleanup, Live CSS) |
| 9.0 | Fałszywe „nieużywany plik” dla audio z media overlays SMIL | Cleanup → `UnusedMedia` |
| 9.5 | Po komunikacie z liczbą wyników wyszukiwania fokus nie wraca do edytora | Find & Replace → Count |
| 6.12 | Po poprawieniu słowa w oknie pisowni zaznaczenie idzie w górę zamiast w dół | okno pisowni |

Najważniejsza wydaje się pozycja **9.0 (SMIL w `UnusedMedia`)**: jeśli Signet nie liczy odwołań
z plików `.smil`, Cleanup może zaproponować usunięcie audio, którego książka używa.

---

## Co Signet już ma (bez potrzeby przenoszenia)

- **Usuwanie nieużywanych obrazów** (calibre 9.5): Cleanup → zakładka Pliki (`CleanupStep.UnusedMedia`),
  obejmuje też SVG, audio i wideo.
- **Typograficzny apostrof w pisowni** (calibre 9.15): `SpellChecker.NormalizeForSpelling` zamienia
  `’` na `'`.
- **Liczba słów na plik w raportach** (calibre 6.9): raport „Word & Character Counts” i kolumna
  liczby słów w raporcie plików HTML.
- **Tytuł wpisu ToC z atrybutu `title` nagłówka** (calibre 5.41): `HeadingSelectorModel.SetTitle`.
- **Przenoszenie wielu wpisów w edytorze ToC** (calibre 8.0.1): `EditTocViewModel` działa na
  zakresach zaznaczenia.
- **`<s>`/`<del>` zamiast przestarzałego `<strike>`** (calibre 6.10): przekreślenie w Signecie wstawia `<del>`.
- **Przełączanie zawijania wierszy** (calibre 7.14): akcja `WordWrap`.

## Odrzucone (poza zakresem)

- Funkcje zastępowania w Pythonie (regex-function mode): Signet świadomie ich nie obsługuje (README).
- Czytnik, biblioteka, serwer treści, okładki w siatce, metadane biblioteki, sterowniki urządzeń,
  konwersje do PDF/DOCX/MOBI/KEPUB, funkcje AI i „Create your own adventure”.
- Poprawki specyficzne dla Qt i platform (wycieki pamięci w podświetlaniu, okna na macOS) oraz
  kopiowanie plików między instancjami edytora.
- Przycisk „Edit book” w czytniku: Signet nie ma czytnika.
