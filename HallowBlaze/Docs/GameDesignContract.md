# HallowBlaze — Game Design Contract

| Pole | Wartość |
| --- | --- |
| Status | Żywy kontrakt; obowiązuje do jawnej zmiany |
| Wersja | 0.3 |
| Ostatnia aktualizacja | 2026-09-27 |
| Zakres | First playable vertical slice, multi-board route foundation, and the accepted-but-deferred inventory contract |

## 1. Cel dokumentu

### Decyzja

Ten dokument jest źródłem prawdy dla podstawowych reguł i obietnicy HallowBlaze. Opisuje nie tylko **co** ma zostać zbudowane, ale również **dlaczego** dana decyzja istnieje i jakie ma konsekwencje dla implementacji.

W dokumencie:

- **MUSI** oznacza wymaganie kontraktowe;
- **POWINNO** oznacza silną rekomendację, od której można odejść dopiero po podaniu powodu;
- **MOŻE** oznacza dozwolony wariant lub przyszłe rozszerzenie;
- liczby oznaczone jako konfigurowalne nie są jeszcze decyzjami balansowymi.

Status decyzji może być oznaczony jako:

- **Accepted** — obowiązuje w bieżącym zakresie;
- **Hypothesis** — ma zostać sprawdzona playtestem, lecz do tego czasu stanowi założenie implementacyjne;
- **Deferred** — świadomie odłożona poza vertical slice;
- **Open** — wymaga decyzji właściciela projektu przed zależnym zadaniem.

Jeżeli sekcja nie podaje innego statusu, jej decyzje mają status **Accepted** dla pierwszego vertical slice.

### Rationale — dlaczego

Lista funkcji bez uzasadnienia prowadzi do lokalnie poprawnych, lecz wzajemnie sprzecznych implementacji. Agent może na przykład poprawnie dodać `RunState`, ale nadal trzymać zdrowie także w `PlayerScript`, jeśli nie rozumie, że celem zadania jest uzyskanie jednego źródła prawdy niezależnego od cyklu życia sceny.

### Konsekwencje implementacyjne

- Każde zadanie `HB-xxx` MUSI wskazywać sekcję tego kontraktu.
- Karta zadania MUSI posiadać własne pole `Rationale`; samo ID zadania nie wystarcza.
- Agent nie może po cichu zmieniać decyzji kontraktowej w ramach implementacji.
- Odstępstwo wymaga aktualizacji kontraktu albo osobnego ADR w `Docs/Decisions/`.

## 2. Obietnica produktu

### Decyzja

HallowBlaze jest turowym survival-puzzlerem z strukturą roguelite. Gracz prowadzi kolejne wyprawy na północ przez stały świat, unika i manipuluje przewidywalnymi zombie, wybiera trasy na podstawie niepełnych wskazówek oraz stopniowo tworzy trwały atlas i dziennik wiedzy.

Krótka obietnica dla gracza:

> Każda wyprawa może się skończyć, ale poznany świat i zrozumiane reguły pozostają.

### Rationale — dlaczego

Obecna wersja oferuje głównie ruch od startu do wyjścia i losowe omijanie zombie. Sama większa liczba poziomów nie zmieni tego w pełnoprawną grę. Potrzebny jest powtarzalny proces podejmowania decyzji, w którym porażka przekazuje użyteczną informację do następnej próby.

Trwały atlas daje widoczny, materialny progres. Wiedza o systemach daje progres w głowie gracza. Te dwa rodzaje progresji mogą zapewnić regrywalność bez stałych premii do statystyk.

### Konsekwencje implementacyjne

- Każda większa mechanika POWINNA tworzyć decyzję, nową informację albo nowy sposób wykorzystania znanej reguły.
- Content, który jedynie zwiększa liczbę przeciwników lub długość drogi, nie realizuje obietnicy produktu.
- Pierwszy vertical slice MUSI zawierać co najmniej dwie wyprawy po tym samym świecie oraz zauważalną korzyść z wcześniejszego odkrycia.

### Walidacja

Po pierwszej śmierci gracz powinien umieć odpowiedzieć:

1. Co zachowało się na następną wyprawę?
2. Czego dowiedziałem się o świecie lub zasadach?
3. Jak ta wiedza wpłynie na mój kolejny wybór?

## 3. Filary projektu

### 3.1. Wiedza jest progresją

#### Decyzja

Mechaniki działają od początku. Odkrycie faktu nie odblokowuje reguły, lecz dokumentuje ją w atlasie lub dzienniku.

#### Rationale — dlaczego

Jeżeli przeczytanie notatki dopiero aktywuje interakcję, postęp staje się typowym systemem odblokowań. Jeżeli interakcja zawsze była możliwa, gracz może eksperymentować, formułować hipotezy i czuć, że sam rozumie świat.

#### Konsekwencje implementacyjne

- Fakty w profilu służą UI, narracji, podpowiedziom i statystykom.
- Resolver zasad nie może sprawdzać `discoveredFactIds` przed wykonaniem podstawowej interakcji.
- Sterowanie, podstawowy koszt tury oraz krytyczne informacje o dostępności akcji nie mogą być ukrywaną „wiedzą”.

### 3.2. Porażka zabiera wyprawę, nie odkrycia

#### Decyzja

Śmierć resetuje zasoby i pozycję wyprawy, ale zachowuje atlas, przebyte drogi i poznane fakty.

Wybranie `New Game` w menu głównym rozpoczyna natomiast nowy profil/kampanię i zastępuje zapisany atlas. Nie jest to ta sama operacja co rozpoczęcie kolejnego runu po śmierci.

#### Rationale — dlaczego

Pełny reset byłby sprzeczny z główną obietnicą. Zachowanie ekwipunku i statystyk osłabiłoby natomiast napięcie survivalowe. Rozdzielenie tych warstw pozwala jednocześnie odczuwać ryzyko i postęp.

#### Konsekwencje implementacyjne

- Profil i run MUSZĄ być osobnymi stanami oraz osobnymi zapisami.
- `StartNewRun()` nie może modyfikować profilu.
- `New Game` jest jawną operacją pełnego resetu profilu; stary profil i jego recovery backup nie mogą później przywrócić odkryć.
- Osobny dialog potwierdzający destrukcyjny reset MOŻE zostać dodany wraz z docelowym zarządzaniem profilami; w prototypie świadomy wybór `New Game` jest potwierdzeniem.

### 3.3. Przewidywalność przed refleksem

#### Decyzja

Niebezpieczeństwa mają proste, czytelne reguły, a zamiary zombie są widoczne przed ruchem gracza.

#### Rationale — dlaczego

Gra ma nagradzać analizę sytuacji, nie szybkie sterowanie ani zgadywanie niewidocznych rzutów. Śmierć powinna być możliwa do wyjaśnienia sekwencją znanych zasad.

#### Konsekwencje implementacyjne

- Ten sam stan i komenda MUSZĄ dawać ten sam rezultat.
- Intent pokazany graczowi nie może zostać potajemnie przeliczony po jego ruchu.
- Animacje i czas coroutine nie mogą wpływać na logikę.

### 3.4. Unikanie i manipulacja zamiast tradycyjnej walki

#### Decyzja

Podstawową odpowiedzią na zombie jest zmiana pozycji, kierowanie hałasem, blokowanie, zamykanie przejść i używanie terenu. Gra nie otrzymuje klasycznego systemu broni, DPS i punktów życia przeciwników w pierwszym vertical slice.

#### Rationale — dlaczego

Bezpośrednia walka przesunęłaby uwagę z łamigłówki przestrzennej na optymalizację obrażeń. Pośrednia kontrola przeciwników wykorzystuje istniejącą planszę i lepiej wzmacnia tożsamość gry logicznej.

#### Konsekwencje implementacyjne

- Narzędzia powinny zmieniać teren, czas albo zachowanie zombie.
- Nowy przeciwnik powinien wnosić nową regułę decyzyjną, nie tylko więcej HP lub obrażeń.
- Pułapki mogą unieruchamiać, opóźniać lub przekierowywać zombie; trwałe zabijanie nie jest potrzebne do MVP.

### 3.5. Małe, gęste plansze

#### Decyzja

Zwykłe plansze pierwszego vertical slice pozostają domyślnie w rozmiarze 8×8. Landmark może wyjątkowo użyć innego, ręcznie dobranego rozmiaru.

#### Rationale — dlaczego

Stały, kompaktowy obszar ułatwia objęcie sytuacji wzrokiem, przewidywanie intentów, automatyczną walidację oraz porównywanie poziomów. Powiększanie mapy co pięć dni wydłuża głównie chodzenie, niekoniecznie decyzje.

#### Konsekwencje implementacyjne

Trudność rośnie przez kombinacje reguł, topologię, teren, informacje i zasoby, a nie przez automatyczne dodawanie rzędu lub kolumny.

## 4. Terminologia domenowa

### Decyzja

| Termin | Znaczenie |
| --- | --- |
| Profil / kampania | Trwały zapis jednego atlasu i wiedzy między wyprawami |
| Atlas | Graficzna i danych reprezentacja odkrytego świata |
| Wyprawa / run | Jedna próba od południowego startu do śmierci albo finału |
| Dzień / etap | One committed journey along a world edge; it may contain multiple local boards and is numbered from 1 |
| Węzeł / node | Stałe miejsce na makromapie |
| Droga / edge | A directed connection between nodes that owns an ordered list of stable route segments |
| Odcinek drogi / route segment | One stable, addressable local-board position within an edge |
| Plansza / board | The local turn-based grid for one route segment; it is not itself a day |
| Tura | Jedna zaakceptowana akcja planszowa oraz wynikające z niej fazy |
| Landmark | Ręcznie zaprojektowany węzeł specjalny używający głównych zasad gry |
| Fakt | Trwale odnotowana obserwacja dotycząca mechaniki lub świata |
| Intent | Zablokowany, pokazany zamiar przeciwnika na następną fazę |
| Komenda | Dane opisujące świadomą próbę działania gracza |

### Rationale — dlaczego

Słowo „level” w obecnym kodzie oznacza jednocześnie dzień, numer sceny i stopień trudności. Przy atlasie prowadziłoby to do błędów, takich jak zwiększenie dnia przy ponownym ładowaniu sceny albo pomylenie miejsca świata z lokalnym układem planszy.

### Konsekwencje implementacyjne

- Nowy kod powinien używać nazw domenowych z tabeli.
- `level` może pozostać przejściowo w adapterze starego kodu, ale nie powinien wejść do nowych modeli.
- Day numbering starts at `1` and advances exactly once when a new route leg begins, never on scene load or between its local boards.
- A local board is addressed by stable edge and segment identity rather than by day or destination node alone.

## 5. Główna pętla gry

### Decyzja

Pętla wyprawy:

```text
wejście do miejsca
→ odczyt sytuacji i intentów
→ seria decyzji na planszy
→ dotarcie do wyjścia
→ ekran atlasu i obserwacja kierunków
→ wybór drogi
→ trwała aktualizacja atlasu
→ kolejny dzień lub finał
```

After `M3.12`, the route-leg loop supersedes the legacy assumption that every local board opens the atlas:

```text
arrive at a node/checkpoint
→ inspect the atlas and commit one outgoing edge
→ show Day N once, with the gameplay HUD hidden
→ resolve the edge's ordered local boards
→ intermediate exit advances to the next segment without route choice or day increment
→ final exit records the completed leg, reaches the destination node, and opens the atlas
→ choose the next edge or enter the finale
```

Pętla między wyprawami:

```text
śmierć
→ podsumowanie trasy i nowych odkryć
→ powrót do południowego schronienia
→ nowa wyprawa z nowymi zasobami i seedem
→ użycie zachowanego atlasu i wiedzy
```

### Rationale — dlaczego

Plansza zapewnia krótkoterminową taktykę, wybór drogi zapewnia strategię wyprawy, a atlas spina wiele prób w jedną historię poznawania świata. Każda skala dostarcza innego rodzaju decyzji.

### Konsekwencje implementacyjne

- Ukończenie planszy nie może samodzielnie przeładowywać sceny.
- `LevelFlowController` powinien koordynować zapis, mapę, wybór i następną planszę.
- Wyjście generuje wynik domenowy `ExitReached`, na który reaguje przepływ gry.
- The travel flow, not `BoardManager`, decides whether `ExitReached` advances to another segment or completes the edge.
- `Traversed` and destination `Visited` are committed only after the last segment, not when the route is selected.

### Walidacja

Gracz powinien umieć odróżnić:

- akcję w turze;
- ukończenie lokalnej planszy;
- upływ dnia wyprawy;
- rozpoczęcie nowego runu.

## 6. Model trwałości i własność stanu

### 6.1. Trzy warstwy stanu

#### Decyzja

| Warstwa | Przykładowa zawartość | Czas życia |
| --- | --- | --- |
| `ProfileState` | atlas, discovered facts, historical route observations, known fixed tool sources, statistics | wiele runów |
| `RunState` | health, satiety/legacy food, tools, carried items, reached checkpoint, active route leg, travel log | do śmierci lub zwycięstwa |
| `BoardState` | positions, obstacles, ground item instances, zombies, field effects | jedna plansza lub jej snapshot |

#### Rationale — dlaczego

Obecny stan jest rozproszony między `GameManager`, `PlayerScript`, komponentami obiektów i cyklem życia sceny. To pozwala na rozbieżność danych, podwójne aktualizacje oraz przypadkowe zachowanie zasobów przez `OnDisable`. Jawne warstwy przypisują każdej informacji jednego właściciela.

#### Konsekwencje implementacyjne

- UI, prefab i komponent widoku nie mogą być właścicielem stanu rozgrywki.
- Przeniesienie danych między warstwami wymaga jawnej operacji domenowej.
- Każda właściwość powinna mieć dokładnie jedno autorytatywne miejsce zapisu.

### 6.2. `RunState` — cel HB-010

#### Decyzja

`RunState` MUSI zawierać co najmniej:

- stabilne `runId` i `runSeed`;
- bieżący dzień oraz `worldNodeId`;
- zdrowie i jedzenie;
- dwa sloty narzędzi i ich stan;
- trasę bieżącej wyprawy;
- status `Active`, `Dead` albo `Won`;
- identyfikator lub snapshot aktywnej planszy, gdy wznowienie w jej środku zostanie wdrożone.

`M3.11` extends this historical HB-010 baseline with an optional `ActiveRouteLegState`, a structured edge-based travel log, and stable board addressing. `M9` later extends it with `InventoryState` and replaces the runtime `Food` concept with `Satiety`. These are migrations of one source of truth, not parallel copies.

#### Rationale — dlaczego

HB-010 nie jest „dodaniem kolejnej klasy z polami”. Jego celem jest uniezależnienie wyprawy od tworzenia i niszczenia obiektów Unity. Dzięki temu zmiana sceny, wyłączenie `PlayerScript` albo odtworzenie UI nie resetuje ani nie duplikuje zdrowia, jedzenia i ekwipunku.

#### Konsekwencje implementacyjne

- `PlayerScript` odczytuje stan i prezentuje zdarzenia; nie zapisuje własnej kopii zdrowia.
- `GameManager` może przejściowo wystawiać kompatybilne właściwości, ale deleguje je do `RunState`.
- Nowy run można utworzyć i przetestować bez sceny, prefabów i `MonoBehaviour`.
- Wartości początkowe są konfiguracją, a nie rozrzuconymi stałymi.

#### Walidacja

- Przeładowanie planszy zachowuje zasoby runu.
- `StartNewRun()` daje świeże zasoby.
- Dwa utworzone runy nie współdzielą modyfikowalnego ekwipunku.

### 6.3. `ProfileState` — cel HB-011

#### Decyzja

`ProfileState` MUSI zawierać co najmniej:

- ID i wersję definicji świata;
- statusy odkrycia węzłów;
- statusy odkrycia i przebycia dróg;
- trwałe notatki;
- `discoveredFactIds`;
- podsumowania i statystyki wypraw.

Structured route-resource samples and discovered fixed tool-source IDs belong here once `M7.4` and `M7.5` are implemented. They must not be encoded as free-form note strings or system-fact IDs.

#### Rationale — dlaczego

Profil materializuje główną formę metaprogresji. Oddzielenie go od runu sprawia, że operacje śmierci i restartu są bezpieczne z definicji, zamiast polegać na pamiętaniu, których pól nie należy wyzerować.

#### Konsekwencje implementacyjne

- Profil nie zawiera zdrowia, jedzenia, pogody ani aktualnych narzędzi.
- `StartNewRun()` otrzymuje profil do odczytu, lecz go nie czyści.
- Modyfikacje profilu są natychmiast zapisywane.

### 6.4. `BoardState`

#### Decyzja

`BoardState` jest jedynym źródłem prawdy dla lokalnej planszy i rozróżnia co najmniej:

- podłoże;
- przeszkodę;
- przedmiot;
- aktora;
- efekt pola.

#### Rationale — dlaczego

Collider mówi, co aktualnie znajduje się w scenie, ale nie jest dobrym modelem gry: zależy od klatki, włączania komponentów i kolejności animacji. Jawna siatka umożliwia deterministyczne reguły, walidator generatora, solver puzzli oraz testy bez Unity.

#### Konsekwencje implementacyjne

- `Physics2D.Linecast` nie rozstrzyga przyszłych zasad.
- `Transform` jest synchronizowany z modelem, a nie odwrotnie.
- Dwa podmioty nie mogą zajmować jednego pola, chyba że konkretna warstwa jawnie na to pozwala.
- A ground item remains owned by `BoardState` until an accepted atomic transfer or use removes that exact instance.
- Discarding a carried item destroys it; it does not silently create a new ground entity.

### 6.5. Śmierć, restart i zapis

#### Decyzja

- Śmierć najpierw utrwala zmiany profilu, a potem zamyka run.
- Nowa wyprawa resetuje `RunState` i pozostawia `ProfileState`.
- `New Game` z menu głównego tworzy nowy `ProfileState`; `Continue` wczytuje istniejący profil i aktywny run.
- MVP wznawia grę bezpiecznie pomiędzy planszami lub z ekranu mapy.
- Zapis środka planszy jest rozszerzeniem po stabilizacji resolvera.
- Pełny reset profilu jest jawną opcją `New Game`, odrębną od restartu wyprawy.

#### Rationale — dlaczego

Zapis po każdej animowanej klatce zwiększyłby koszt pierwszego vertical slice. Najważniejsza obietnica wymaga natomiast, by odkrycie nie zniknęło nawet po zamknięciu gry bezpośrednio po wyborze drogi.

#### Konsekwencje implementacyjne

- Profil i aktywny run są osobnymi, wersjonowanymi DTO.
- Zapis używa stabilnych tekstowych ID, nie referencji Unity ani `InstanceID`.
- Zapis powinien być atomowy i posiadać ostatnią poprawną kopię.

### 6.6. Pauza, ustawienia i powrót do menu głównego

#### Decyzja

Podczas aktywnej planszy na PC i w Editorze klawisz Escape otwiera modalne menu pauzy z akcjami `Resume`, `Settings` i `Exit to Menu`.

- `Resume` oraz ponowne naciśnięcie Escape w głównym widoku pauzy wznawiają dokładnie tę samą planszę.
- `Settings` otwiera tę samą funkcjonalność ustawień dźwięku i muzyki, która jest dostępna w menu głównym. Escape albo `Back` wraca z ustawień najpierw do głównego widoku pauzy, nie bezpośrednio do gry.
- Gdy widoczna jest pauza lub jej ustawienia, wejście rozgrywkowe nie tworzy komend, nie wykonuje AI i nie zmienia tury, pozycji, zdrowia, jedzenia ani stanu narzędzi.
- `Exit to Menu` zachowuje ostatnią zatwierdzoną granicę planszy, odłącza aktywny run od bieżącej sesji i wraca do menu głównego. Nie zapisuje zmian wykonanych w środku planszy. Poprawny zapis pozostaje dostępny dla `Continue`; akcja nie usuwa profilu, ustawień audio ani lokalnego rekordu.
- Trwałe porzucenie runu jest osobną, jednoznaczną akcją. Usuwa bieżący i zapasowy plik runu, ale nigdy plik profilu.
- Przed wznowieniem, wyjściem, wyłączeniem kontrolera albo zmianą sceny gra przywraca normalny upływ czasu i usuwa blokadę wejścia.

Mobilny sposób otwierania pauzy pozostaje poza tym zakresem do rozstrzygnięcia O-004. Relacja `Exit to Menu` z wersjonowanym `Continue` została rozstrzygnięta w O-006: poprawny run save pozostaje dostępny do wznowienia.

#### Rationale — dlaczego

Menu otwierane podczas rozgrywki jest granicą sterowania, a nie kosmetyczną nakładką. Samo zatrzymanie `Time.timeScale` nie blokuje kodu czytającego input w `Update`, więc bez jawnej bramki gracz mógłby tracić zasoby lub wykonywać akcje pod menu. Jedna współdzielona funkcjonalność ustawień zapobiega rozjazdowi wartości i prezentacji pomiędzy scenami, a jawne porzucenie legacy runu chroni kolejny start przed stanem pozostawionym przez obiekty `DontDestroyOnLoad`.

#### Konsekwencje implementacyjne

- Stan widoku pauzy jest przejściowym stanem prezentacji i nie należy do `ProfileState`, `RunState` ani formatu save.
- Kontroler pauzy musi osobno zarządzać widocznością UI, fokusem `EventSystem`, blokadą wejścia oraz odtworzeniem upływu czasu.
- Menu główne i pauza korzystają ze wspólnego panelu lub kontrolera ustawień; nie utrzymują dwóch niezależnych implementacji tych samych przełączników.
- Powrót do menu odłącza run od sesji bez usuwania ani nadpisywania ostatniego checkpointu i jest bezpieczny przy wielokrotnym wywołaniu.
- Test integracyjny musi udowodnić brak kosztu podczas pauzy, poprawne przejścia Escape/Back oraz odtworzenie ostatniej zatwierdzonej granicy po `Exit to Menu` i `Continue`.

## 7. Atlas i świat

### 7.1. Stały świat dla jednego profilu

#### Decyzja

The world macrograph is stable between runs. The first vertical slice uses a hand-authored graph. A local layout belongs to a stable route segment and may be generated again for that segment in another run.

W przyszłości nowy profil może otrzymywać świat wygenerowany raz z szablonów. W takim wariancie pełny wynik generowania grafu musi zostać zapisany; sam seed nie jest wystarczającą gwarancją po zmianach generatora.

#### Rationale — dlaczego

Trwały atlas ma wartość tylko wtedy, gdy notatki odnoszą się do stabilnych miejsc i dróg. Ręczny graf pozwala także świadomie projektować wskazówki, rozwidlenia, powroty oraz tempo ujawniania celu.

#### Konsekwencje implementacyjne

- Węzły i krawędzie mają stabilne tekstowe ID niezależne od kolejności w Inspectorze.
- Zmiana lokalnego seeda nie zmienia położenia cmentarza, sadu ani landmarku.
- The local generator receives the full `BoardAddress`, segment generation configuration, and biome family. A node ID may be contextual metadata but never the board identity by itself.

### 7.2. Kierunek podróży

#### Decyzja

Podróż prowadzi zasadniczo z południa na północ. Graf może zawierać ruch na wschód i zachód, odnogi oraz ponowne połączenia, ale postęp kampanii jest czytelny geograficznie.

#### Rationale — dlaczego

Stały kierunek daje wyprawie cel, wspiera opowieść środowiskową i naturalnie uzasadnia przechodzenie do zimniejszych biomów. Jest czytelniejszy niż abstrakcyjna lista rosnących numerów poziomu.

#### Konsekwencje implementacyjne

- Każdy węzeł posiada pozycję atlasową i warstwę dystansu.
- „Dzień” wynika z postępu wyprawy, a nie z ponownego załadowania sceny.
- Wskazówki używają spójnych kierunków świata.

### 7.3. Stany odkrycia

#### Decyzja

Węzeł używa stanów:

1. `Unknown` — nie jest ujawniony;
2. `Rumored` — istnieje trwała, niepełna wskazówka;
3. `Sighted` — gracz dostrzegł kierunek lub zarys miejsca;
4. `Visited` — gracz dotarł do miejsca i zna jego stabilną tożsamość.

Droga używa co najmniej `Unknown`, `Sighted` i `Traversed`.

#### Rationale — dlaczego

Jeden boolean `discovered` nie odróżnia plotki, obserwacji i osobistego doświadczenia. Stopnie odkrycia pozwalają stopniowo uzupełniać atlas bez zdradzania całej topologii.

#### Konsekwencje implementacyjne

- Samo wyświetlenie osiągalnych kierunków może utrwalić `Sighted`.
- Wybranie i pokonanie drogi utrwala `Traversed`.
- Dotarcie do celu ustawia `Visited` nawet wtedy, gdy gracz później zginie w tym miejscu.
- Nie istnieje status `Cleared` ani obowiązkowy procent kompletności.

### 7.4. Stabilne i dynamiczne informacje

#### Decyzja

Trwale zapisywane są informacje stabilne, np. położenie, typ miejsca, landmark, charakterystyczny teren i stała relacja między drogami. Zwykłe łupy, dokładne rozmieszczenie zombie, pogoda oraz tymczasowe zdarzenia należą do runu lub planszy.

Historical observations are also persistent knowledge without becoming promises about the next run. The atlas may retain what the player actually observed on a route, including a completed-sample range, while the next run's concrete item instances remain dynamic.

#### Rationale — dlaczego

Jeżeli atlas zapisuje losowe łupy tak, jakby zawsze tam były, zaczyna wprowadzać gracza w błąd. Jeżeli wszystko jest dynamiczne, mapa nie dostarcza użytecznej wiedzy. Jasny podział pozwala łączyć rozpoznawalność z niepewnością.

#### Konsekwencje implementacyjne

- UI MUSI wizualnie odróżniać informacje trwałe od warunków aktualnej wyprawy.
- Znana droga nie powinna zawsze być automatycznie najlepsza; jej wartość może zależeć od narzędzi, zasobów i pogody.
- Atlas wording must distinguish an authored rumor, a past observation, and current-run availability.

### 7.5. Wybór drogi

#### Decyzja

At a reached node, the player chooses among at most a few outgoing edges described by observable, diegetic clues. A rumor does not reveal exact loot or percentage risk. Exact numbers may appear only as clearly historical observations from completed travel, never as a guarantee for the next run.

Przykłady:

- dym na północnym zachodzie;
- gęste drzewa;
- ślady przy drodze;
- szum wody;
- świeże tropy zombie.

#### Rationale — dlaczego

Wybór „lewo albo prawo” bez informacji jest losowaniem, nie decyzją. Pełne statystyki usuwają natomiast interpretację i poczucie eksploracji. Wskazówka ma pozwalać na hipotezę, ale nie gwarantować wyniku.

#### Konsekwencje implementacyjne

- Każda krawędź posiada kierunek i dane wskazówki.
- Ekran wyboru pokazuje wyłącznie wiedzę dostępną postaci.
- Wybór jest zapisywany przed przejściem do następnej planszy.
- The route view shows route length separately from aggregated resource knowledge so that a longer edge is not compared to a shorter edge by raw totals alone.

### 7.6. Język wizualny atlasu

#### Decyzja

- trwałe odkrycia: grafit lub tusz;
- przebyte drogi: trwała linia;
- aktualna wyprawa: kolorowe wyróżnienie;
- aktualna pozycja: ruchomy znacznik;
- plotki i niepewne kierunki: linia przerywana lub szkic;
- nieodkryty obszar: bez pełnej topologii.

#### Rationale — dlaczego

Gracz musi natychmiast widzieć, co należy do długoterminowego atlasu, a co zniknie po śmierci. Czytelna materialność mapy wzmacnia poczucie jej własnoręcznego uzupełniania.

## 8. Długość wyprawy i struktura milestone'ów

### Decyzja

- Pierwszy test atlasu obejmuje około 5 dni i kończy się placeholderem lub pierwszym landmarkiem.
- Pierwszy pełny vertical slice obejmuje około 10 dni.
- Landmark występuje około dnia 5, finał około dnia 10.
- Kampania 20–25-dniowa jest rozszerzeniem po pozytywnych playtestach.
- Kampania 30–40-dniowa nie jest obecnym zobowiązaniem.

Status: **Hypothesis** dla dokładnej liczby dni, **Accepted** dla ograniczania pierwszego zakresu, **Deferred** dla długiej kampanii.

### Rationale — dlaczego

Największym ryzykiem nie jest brak contentu, lecz to, czy gracze chcą po śmierci rozpocząć następną wyprawę i wykorzystać atlas. Dziesięć dopracowanych dni pozwala sprawdzić pełną pętlę znacznie wcześniej niż długa kampania.

### Konsekwencje implementacyjne

- Kod nie używa warunku `day % 5 == 0` do wybierania specjalnych poziomów; landmark jest typem węzła grafu.
- System musi obsłużyć rozszerzenie grafu bez zmiany semantyki save'ów.
- Pierwsze zwycięstwo POWINNO być możliwe z częścią atlasu nadal nieodkrytą.

## 9. Kontrakt planszy

### Decyzja

Zwykła plansza vertical slice:

- ma domyślnie 8×8 pól gry;
- posiada jawny start i co najmniej jedno wyjście;
- traktuje pola brzegowe jako normalną część przestrzeni;
- oferuje więcej niż jedną sensowną linię ruchu, jeśli pozwala na to archetyp;
- nie wymaga losowo zdobytego narzędzia do obowiązkowego wyjścia;
- may be regenerated on a later run for the same stable route segment;
- jest walidowana przed pokazaniem graczowi.

### Rationale — dlaczego

Obecny generator losuje elementy tylko wewnątrz planszy, pozostawiając wolny obwód i stałe wyjście w rogu. Tworzy to dominującą, mało interesującą trasę. Model-first i walidacja mają zapewnić poprawną, lecz nadal zmienną sytuację taktyczną.

### Konsekwencje implementacyjne

- Generator najpierw tworzy `BoardBlueprint`, potem go waliduje, a dopiero na końcu tworzy widoki.
- Every blueprint carries its full stable `BoardAddress`; node/day metadata alone cannot identify a local board.
- Start, wyjścia i krytyczna droga są rezerwowane przed dekoracją.
- Po ograniczonej liczbie nieudanych prób używany jest ręczny fallback.

### Walidacja

Walidator sprawdza co najmniej:

- granice i unikalność współrzędnych;
- osiągalność wymaganych wyjść;
- brak obowiązkowego losowego narzędzia;
- minimalny dystans zombie od startu;
- zakres długości najkrótszej drogi;
- brak gwarantowanej bezpiecznej autostrady na obwodzie.

## 10. Kontrakt tury

### 10.1. Koszt akcji

#### Decyzja

- Każda zaakceptowana komenda planszowa kosztuje dokładnie jedną turę i bazowy koszt jedzenia.
- Odrzucona komenda nie zużywa tury, jedzenia ani narzędzia.
- `WaitCommand` jest zaakceptowaną akcją i kosztuje turę.
- Jedzenie na polu docelowym jest podnoszone automatycznie przy wejściu; narzędzia wymagają świadomej `InteractCommand`.
- Otwieranie mapy, wybór slotu, podgląd celu i anulowanie UI nie kosztują tury.
- Zwykły ruch w nieinteraktywną przeszkodę jest odrzucony.
- Próba wejścia w obiekt z jednoznaczną interakcją kontekstową może zostać znormalizowana do zaakceptowanej komendy interakcji, jeżeli UI wcześniej to sygnalizuje.

#### Rationale — dlaczego

Koszt musi zależeć od zaakceptowanej decyzji, nie od przypadkowego inputu, klatki ani liczby wywołań metod. W przeciwnym razie gracz nie może wiarygodnie planować głodu i faz zombie.

#### Konsekwencje implementacyjne

| Czynność | Tura | Jedzenie | Faza zombie |
| --- | --- | --- | --- |
| poprawny ruch | tak | tak | tak |
| `Wait` | tak | tak | tak |
| zaakceptowana interakcja/pchnięcie/kopanie | tak | tak | tak |
| poprawne użycie narzędzia | tak | tak | tak |
| komenda odrzucona | nie | nie | nie |
| wybór aktywnego slotu | nie | nie | nie |
| podgląd mapy lub celu | nie | nie | nie |
| wybór drogi między planszami | nie jest turą planszową | nie | nie |

The automatic-food and fixed-cost rows above remain the active transitional contract through the no-backpack vertical slice. `M9` deliberately supersedes only those item/resource details with the accepted rules in section 27; completed cards such as `M3.4` remain historically correct.

### 10.2. Kolejność faz

#### Decyzja

Jedna tura przebiega w tej kolejności:

1. walidacja komendy;
2. bezpośredni efekt działania gracza, w tym zdobyta nagroda;
3. bazowy koszt zaakceptowanego działania i pozostałe skutki natychmiastowe;
4. sprawdzenie śmierci albo ukończenia planszy;
5. wykonanie wcześniej pokazanych intentów;
6. efekty środowiskowe;
7. ponowne sprawdzenie stanu końcowego;
8. obliczenie i pokazanie intentów następnej tury;
9. odblokowanie wejścia.

W obrębie faz 1-4 ruch gracza ma dokładną kolejność:

1. `ValidateMove`;
2. `MovePlayer`;
3. `CollectAutomaticFood`;
4. `ApplyFoodReward`;
5. `ApplyActionCost`;
6. `EmitStarvationIfFoodIsZero`;
7. `EmitExitIfPresent`, wyłącznie jeżeli gracz przeżył.

Nagroda z automatycznie zebranego jedzenia jest rozliczana przed kosztem tej akcji, więc może uratować gracza przed głodem. Po naliczeniu kosztu najpierw sprawdzana jest śmierć z głodu, a dopiero potem osiągnięcie wyjścia. Jeżeli Food spadnie do zera, akcja kończy się śmiercią i nie emituje ukończenia planszy, nawet gdy gracz wszedł na pole wyjścia. Dotarcie do wyjścia przez żywego gracza kończy planszę przed fazą zombie.

#### Rationale — dlaczego

Jawna kolejność usuwa spory o to, czy zombie może uderzyć gracza już po ucieczce, czy głód zabija przed atakiem oraz kiedy zmiana bramy blokuje zaplanowany ruch. Jest też niezbędna do deterministycznych testów.

#### Konsekwencje implementacyjne

- `TurnResolver` wylicza cały wynik przed animacją.
- Prezentacja odtwarza `GameEvent[]`; nie liczy zasad ponownie.
- Nie można rozpocząć dwóch resolverów jednocześnie.
- Future composite commands must still produce one atomic `TurnResult`; a modal draft is never a partially resolved turn.

## 11. Zombie i intenty

### 11.1. Wspólny kontrakt przeciwników

#### Decyzja

Każdy przeciwnik posiada:

- stabilne `EntityId`;
- czysty stan;
- jedną prostą, nazwaną regułę zachowania;
- planowany `EnemyIntent`;
- widok nieposiadający logiki AI.

Intent jest obliczany i pokazywany przed wejściem gracza, a następnie blokowany do fazy zombie.

#### Rationale — dlaczego

Przewidywalny przeciwnik może być częścią zagadki. Przeciwnik, który po ruchu gracza wybiera potajemnie nową najlepszą odpowiedź, zamienia planowanie w zgadywanie.

#### Konsekwencje implementacyjne

- Planowanie i wykonanie korzystają z tego samego modelu planszy.
- A locked intent never selects a new direction, destination, or target during execution. An invalidated action normally resolves as `Wait`; the Shambler's explicit same-cell movement-to-attack condition and missed attacks follow section 11.3.1 instead of replanning.
- Rejestracja prefabów i kolejność w hierarchii nie mogą wpływać na inicjatywę.

### 11.2. Konflikty intentów

#### Decyzja

W MVP:

- inicjatywa wynika ze stabilnego `EntityId` lub jawnego parametru;
- pierwszy przeciwnik rezerwuje wolne pole;
- kolejny próbujący wejść na to samo pole czeka;
- zamiana miejsc jest zabroniona;
- An invalidated intent does not replan. Ordinary movement conflicts produce `Wait`; a player occupying a Shambler's locked destination uses the declared condition in section 11.3.1. Another enemy occupying that cell is a blocker, not an attack target.

#### Rationale — dlaczego

Reguły konfliktu są częścią zachowania widocznego dla gracza. Pozostawienie ich kolejności komponentów Unity powodowałoby niestabilne i trudne do odtworzenia rezultaty.

#### Accepted sequential occupancy - owner decision 2026-10-09

**Status:** `Accepted`. Movement uses live sequential occupancy, not a phase-start occupancy snapshot.

- Every board character has a deterministic place in its action order. The player-first phase order in section 10.2 remains unchanged; within the current enemy phase, ascending stable `EntityId` determines initiative. Input collection order, hierarchy order, and animation timing cannot change it.
- Locked enemy intents execute one at a time. Each movement checks the authoritative board after all earlier actions in that phase. A later actor may enter a cell that an earlier actor has already vacated, provided the recorded destination remains otherwise legal and empty.
- A destination still occupied by another enemy when an actor executes blocks that movement as `Wait`. The Shambler's declared attack-on-original-player-entry condition remains governed by section 11.3.1; it is not replaced with `Wait`. A blocked actor is not retried after a later actor moves. There is no simultaneous exchange, automatic chain propagation, extra opportunity, or replanning.
- Reciprocal moves remain blocked: neither actor can vacate its source by entering the other's occupied source. A blocked leading actor does not permit followers to overlap it.
- This occupancy decision does not change the accepted Shambler planner's rule against planning through occupied enemy cells. Following behavior for future archetypes can use this execution policy but requires its own planning contract; it is not implemented by this decision.

For the following examples, A starts at `(1,1)` and targets `(2,1)`; B starts at `(2,1)` and targets `(3,1)`. The extended chain adds C at `(3,1)` targeting `(4,1)`. Every listed actor retains one explicit `Move`, and the leading destination is initially free unless marked blocked.

| Chain | Initiative order | Expected final cells | Ordered outcomes |
| --- | --- | --- | --- |
| A -> B -> free | B, A | A `(2,1)`, B `(3,1)` | B moves, A moves |
| A -> B -> free | A, B | A `(1,1)`, B `(3,1)` | A waits, B moves |
| A -> B -> blocked | B, A or A, B | A `(1,1)`, B `(2,1)` | Both wait in initiative order |
| A -> B -> C -> free | C, B, A | A `(2,1)`, B `(3,1)`, C `(4,1)` | C moves, B moves, A moves |
| A -> B -> C -> free | A, B, C | A `(1,1)`, B `(2,1)`, C `(4,1)` | A waits, B waits, C moves |
| A -> B -> C -> blocked | C, B, A or A, B, C | A `(1,1)`, B `(2,1)`, C `(3,1)` | All wait in initiative order |

Each executed actor consumes its retained opportunity once, including a blocked move. No initiative or occupancy state is saved by this policy.

### 11.3. Archetypy vertical slice

#### Decyzja

Vertical slice zawiera co najmniej:

1. **Shambler** — prosty pościg z jawnym rytmem ruchu;
2. **Listener** — reaguje na ostatni słyszany hałas i pokazuje `Investigate`.

Trzeci archetyp, np. biegacz poruszający się po prostej, jest opcjonalny po sprawdzeniu czytelności pierwszych dwóch.

#### Rationale — dlaczego

Shambler uczy pozycji i tempa, Listener tworzy możliwość świadomego manipulowania. Dwa jakościowo różne zachowania są cenniejsze niż wiele wariantów obrażeń.

### 11.3.1. Accepted Shambler rule

#### Owner decision - 2026-10-07

**Status:** `Accepted`. These decisions define M3.7 behavior; they do not accept its implementation or the pending M3.6.3 manual PC smoke.

**Named rule:** Shortest-path pursuit with alternating rest.

- The Shambler knows the player's current cell across the whole local board. There is no detection radius, line-of-sight requirement, hidden aggro, or gameplay RNG.
- In an active phase, it plans `Attack` when the player occupies an orthogonally adjacent, legally reachable cell. Otherwise it plans one orthogonal `Move` on a shortest legal grid path to the player, using BFS or an equivalent deterministic shortest-path method. No diagonal movement or attack is allowed.
- Pathfinding respects bounds, walkable terrain, impassable obstacles, and other actors. The player's occupied cell is the terminal pursuit target, not a cell the enemy may enter. If no legal route exists, it plans `Wait`.
- Equally short legal first steps use the fixed priority **North -> East -> West -> South**. A tie does not itself cause waiting. A future less capable or indecisive archetype requires a separate decision; it is not part of this rule.

#### Cadence and initial planning

- Every Shambler starts a fresh board in the active phase. Its first intent is planned before the first player command; all current Shambler variants share this initial phase.
- The cycle is **active -> rest -> active -> rest**. An active phase allows one move or attack; a rest phase always plans `Wait`, including when the player is adjacent.
- Each executed enemy phase advances the cycle exactly once, including a blocked move, no available route, a missed attack, a conditional attack, or planned rest. An unsuccessful active opportunity is therefore followed by rest, not an immediate retry.
- Planning and presentation do not advance cadence. Rejected player commands, pause, and terminal outcomes reached before the enemy phase do not execute that phase or advance its state.
- Turn order remains player action -> locked enemy phase -> remaining controller phases. For successive accepted nonterminal commands, the enemy outcomes are action, rest, action, rest; there is no additional player turn or resource cost.

#### Locked destinations and attack results

- A planned `Move` retains one fixed orthogonally adjacent destination and the explicit **attack if the player enters this destination** condition. The condition is part of the locked rule, not a fresh planner call or a search for a nearby target.
- At execution, if that destination is still legal and empty, the enemy moves there. If the player now occupies that same legal destination, the enemy stays in its original cell and attacks the player there once. An impassable destination or a different actor blocking it produces `Wait`; it does not cause friendly fire, retargeting, or another movement choice.
- Merely moving next to the enemy on a different cell does not activate the condition. A locked `Wait` never becomes an attack. A stale or missing source actor cannot move or deal damage.
- A planned `Attack` retains the original player identity and target cell. It attacks only that recorded cell: if the original player is no longer there, the attack misses and deals zero damage. It does not follow the player to another adjacent cell, attack a replacement occupant, or become pursuit movement. A no-longer-legal attack is blocked without damage or replanning.
- Attack-result events distinguish a hit from an empty-cell miss and report the recorded cell and actual health change. A miss must remain reproducible as a swing at the announced cell; it is not another attack opportunity. Health and terminal handling retain their existing domain/controller ownership.
- The two current variants retain deterministic damage **10 HP** (`Enemy1`) and **20 HP** (`Enemy2`) as definition data, with the same pursuit rule and cadence. Preserving these values does not make either variant a Listener or introduce new enemy combat statistics.

#### Intent visibility and validation

The player must see the movement destination **and its attack-on-player-entry condition before committing a command**. The M3.9 representation must distinguish this conditional movement from unconditional movement and from a planned attack without relying only on color. Showing only a harmless movement arrow and silently changing it into an attack is not acceptable.

Focused tests must cover shortest paths around obstacles, the North/East/West/South tie-break, initial active phase, a complete cadence cycle including failed opportunities, fixed-cell attack misses, player-on-destination attacks, non-player blockers, and no attacks during rest. The same-cell conditional outcome consumes one existing active opportunity and is followed by rest; it never adds a second action, turn, or cost.

## 12. Hałas

### Decyzja

Hałas jest jawnym zdarzeniem rozgrywki posiadającym źródło, pozycję, siłę/zasięg i czas obowiązywania. Nie jest wyłącznie odtwarzanym klipem audio.

### Rationale — dlaczego

Hałas łączy ruch, narzędzia, przeciwników i puzzle w jeden system. Daje graczowi możliwość przyjęcia krótkoterminowego ryzyka w zamian za szybszą drogę albo celowe zwabienie zombie.

### Konsekwencje implementacyjne

- Akcje deklarują hałas w wyniku resolvera.
- Listener używa dokładnie tego zdarzenia, które sygnalizuje UI.
- Nie ma ukrytego losowego testu „czy zombie usłyszało”.
- Siła hałasu jest konfigurowalna i czytelnie komunikowana.

## 13. Narzędzia

### 13.1. Ogólna rola

#### Decyzja

Gracz ma dwa sloty narzędzi. Narzędzia są częścią `RunState`, mają ograniczone użycia i zapewniają szybsze lub taktycznie odmienne rozwiązania. Nie są losowymi kluczami do obowiązkowych drzwi.

#### Rationale — dlaczego

Dwa sloty wymuszają wybór bez tworzenia rozbudowanego inventory managementu. Ograniczone użycia tworzą koszt okazji. Alternatywa bez narzędzia chroni run przed softlockiem wynikającym z losowego dropu.

The two slots remain a separate equipment bar after M9. They are not cells in the spatial bag, and side pockets are not tool slots.

#### Konsekwencje implementacyjne

- `ToolDefinition` zawiera niezmienne dane; stan konkretnej instancji znajduje się w runie.
- Niepoprawny cel nie zużywa tury ani ładunku.
- Zamiana narzędzia jest świadomą, modalną decyzją.
- W vertical slice nie ma craftingu, drzewka ulepszeń ani rozbudowanych napraw.
- A conscious tool pickup routes the tool instance to a free generic tool slot; with both slots occupied, the player chooses `Replace` or `Leave` and nothing is destroyed silently.

### 13.2. Siekiera

#### Decyzja

Siekiera pozwala pokonać krzew lub miękką drewnianą przeszkodę znacznie szybciej niż rękami, ale generuje duży hałas. Drugie zastosowanie, po walidacji pierwszego, może polegać na stworzeniu blokady z oznaczonego drzewa.

#### Rationale — dlaczego

Samo „szybciej” jest mało interesujące. Hałas zamienia oszczędność czasu w ryzyko przestrzenne i wiąże narzędzie z zachowaniem Listenerów.

### 13.3. Łopata

#### Decyzja

Łopata pozwala szybko wykopać oznaczony schowek. Drugim zastosowaniem może być dół tymczasowo zatrzymujący zombie.

#### Rationale — dlaczego

Łopata łączy eksplorację i manipulowanie planszą. Dzięki temu nie jest wyłącznie animacją szybszego zbierania jedzenia.

### 13.4. Wiedza o narzędziach

#### Decyzja

Pierwsze zaobserwowanie konsekwencji może odkryć fakt, np. `AXE_CREATES_LOUD_NOISE` lub `PIT_TRAPS_SHAMBLER`.

#### Rationale — dlaczego

Dziennik powinien utrwalać doświadczenie, a nie zastępować eksperymentowanie tutorialowym tekstem.

## 14. Landmarki i zagadki

### Decyzja

Landmark korzysta z tych samych komend, siatki, narzędzi, zombie i resolvera co zwykła plansza. Pierwszy landmark używa pchanych głazów, płyt i bram oraz oferuje co najmniej dwa sensowne rozwiązania albo rezultaty.

Nie jest osobną minigrą. Nie jest w pełni proceduralnym Sokobanem.

### Rationale — dlaczego

Zagadka systemowa wzmacnia wiedzę przydatną także poza poziomem specjalnym. Osobna minigra uczy reguł, które zaraz zostają porzucone. W pełni proceduralne układanki są kosztowne w projektowaniu, automatycznej weryfikacji i QA.

### Konsekwencje implementacyjne

- Głazy, płyty i bramy są stanem domenowym oraz generują zdarzenia.
- Landmark jest konkretnym typem węzła grafu, nie sprawdzeniem modulo dnia.
- Obowiązkowe rozwiązanie nie wymaga losowego narzędzia, chyba że narzędzie jest zagwarantowane przed wejściem.
- Musi istnieć rozwiązanie awaryjne albo pewność rozwiązywalności.
- Warianty są ręcznie przygotowane i automatycznie sprawdzane solverem lub enumeracją stanów.

### Walidacja

- zero znanych softlocków;
- co najmniej dwa sensowne rezultaty;
- gracz potrafi opisać koszt swojego rozwiązania;
- podstawowe warianty przechodzą automatyczny test rozwiązywalności.

## 15. Biomy, pogoda i narracja podróży

### Decyzja

Pierwszy vertical slice posiada dwa biomy różniące się regułą, nie tylko grafiką. Kierunek północny prowadzi do chłodniejszego środowiska. Pogoda może zmieniać warunki bieżącego runu. Pełne pory roku nie należą do MVP.

Status: **Hypothesis** dla dokładnych reguł biomów, **Deferred** dla pór roku.

Przykładowy podział:

- las: krzewy, widoczność, hałas i przewracane drzewa;
- zimna strefa: śnieg, ślady, lód albo koszt ruchu.

### Rationale — dlaczego

Ochładzanie świata wizualizuje geograficzny postęp i pozwala dawkować reguły. Pory roku wprowadziłyby osobną oś czasu, która może być mylona z podróżą na północ oraz wymagałaby znacznie większej ilości contentu.

### Konsekwencje implementacyjne

- `BiomeDefinition` określa dostępne tereny, rodziny generatora i modyfikatory reguł.
- Biom nie może być wyłącznie zestawem sprite'ów.
- Pogoda jest dynamicznym stanem runu, a nie trwałą notatką atlasu, chyba że chodzi o cechę klimatu miejsca.

## 16. Losowość, deterministyczność i generator

### Decyzja

Losowość wybiera sytuację przed decyzją gracza, ale nie ukryty wynik zaakceptowanej akcji. Generator jest deterministyczny dla znanego wejścia i używa własnych strumieni RNG.

Minimalne strumienie:

- świat/profil;
- wyprawa;
- lokalna plansza;
- puzzle/landmark;
- kosmetyka.

### Rationale — dlaczego

Jawna losowość tworzy różnorodność, a deterministyczny wynik zachowuje uczciwość gry logicznej. Osobne strumienie zapobiegają sytuacji, w której dodatkowy dźwięk lub animacja zmienia następny układ planszy.

### Konsekwencje implementacyjne

- Logika domenowa nie używa globalnego `UnityEngine.Random`.
- Ten sam seed, stan i sekwencja komend dają ten sam hash wyniku.
- `generate → validate → build state → present` jest obowiązującym przepływem.
- Generator ma ograniczoną liczbę prób i poprawny fallback.

### Walidacja

- zwykły test seryjny obejmuje co najmniej 1000 seedów;
- bramka przed milestone'em obejmuje 10 000 seedów aktywnej konfiguracji;
- błąd zawsze raportuje seed, węzeł i przyczynę walidatora.

## 17. Skalowanie trudności

### Decyzja

Trudność jest budżetem sytuacji, nie prostą funkcją rozmiaru planszy ani samej liczby zombie. Może wzrastać przez:

- trudniejszą topologię;
- mniej bezpiecznych pozycji;
- konflikt kilku czytelnych intentów;
- nowe typy terenu;
- presję zasobów;
- mniej kompletne informacje o trasie;
- kombinacje wcześniej poznanych reguł.

### Rationale — dlaczego

Dodawanie przeciwników według jednej funkcji lub powiększanie mapy zwiększa ilość, lecz nie głębię decyzji. Budżet pozwala kontrolować, które źródła presji są łączone i unikać losowych skoków trudności.

### Konsekwencje implementacyjne

- Konfiguracja poziomu opisuje budżet i dozwolone elementy.
- Walidator zbiera metryki, m.in. długość drogi, liczbę gałęzi, presję intentów i częstotliwość fallbacku.
- Nowa reguła jest najpierw prezentowana w prostym kontekście, a dopiero później łączona z innymi.

## 18. Informacja i UI

### Decyzja

UI MUSI komunikować:

- aktualne zasoby runu;
- dwa sloty i pozostałe użycia narzędzi;
- możliwe cele aktywnej interakcji;
- intenty zombie;
- różnicę między trwałym atlasem i aktualną trasą;
- bezpośredni rezultat odkrycia nowego miejsca lub faktu.

Podstawowe informacje muszą być czytelne także przy wyłączonych animacjach.

The full-screen transition shown once at the start of a route leg displays only `Day N`; gameplay health, satiety/food, pockets, and tool HUD are hidden behind it. Day numbering is player-facing from `1`, never `0`.

After M9, the persistent bottom HUD order is `Left pocket | Tool slot 1 | Tool slot 2 | Right pocket`. Tool slots are generic even if the first content set contains only an axe and a shovel.

### Rationale — dlaczego

Gra logiczna jest uczciwa tylko wtedy, gdy gracz ma dostęp do informacji potrzebnej do prognozy. Ukrywanie intentów lub kosztu akcji nie tworzy głębi; tworzy błędne założenia.

### Konsekwencje implementacyjne

- UI obserwuje stan lub zdarzenia, ale nie przechowuje własnych kopii danych.
- Sygnalizacja nie może polegać wyłącznie na kolorze lub krótkiej animacji.
- Nieznana topologia mapy pozostaje ukryta, ale dostępne w danej chwili wybory są jednoznaczne.

## 19. Zwycięstwo, śmierć i regrywalność

### Decyzja

Vertical slice ma skończony finał i jawne stany `Won` oraz `Dead`. Po każdym końcu runu gracz otrzymuje podsumowanie trasy, przyczyny porażki oraz nowych odkryć.

Metaprogresja vertical slice nie przyznaje trwałych procentowych premii do zdrowia, obrażeń, głodu ani szansy na loot.

### Rationale — dlaczego

Skończony cel nadaje podróży sens i pozwala ocenić pacing. Trwała moc mogłaby maskować problemy z czytelnością oraz zastąpić uczenie się grindem. Atlas i wiedza powinny być wystarczającym powodem kolejnej próby.

### Konsekwencje implementacyjne

- Pierwsze zwycięstwo nie wymaga pełnego atlasu.
- Powtórna wyprawa powinna mieć zarówno znane decyzje, jak i zmienione warunki.
- Skróty między runami mogą pojawić się później, ale muszą mieć jawny koszt; nie są darmowymi checkpointami.
- Daily Run lub Endless Mode są osobnymi trybami po kampanii, nie zastępstwem finału.

## 20. Zakres pierwszego vertical slice

### Decyzja

Vertical slice obejmuje:

- stały, ręcznie zaprojektowany graf około 10 dni;
- prototyp atlasu sprawdzony wcześniej na około 5 dniach;
- dwa mechanicznie różne biomy;
- zwykłe plansze 8×8;
- Shamblera i Listenera;
- jawne intenty oraz `Wait`;
- system hałasu;
- dwa sloty, siekierę i łopatę;
- landmark około dnia 5;
- finał około dnia 10;
- oddzielny profil i run;
- trwały zapis atlasu i wiedzy;
- wznowienie pomiędzy planszami;
- testy deterministyczności, walidatora i trwałości profilu.

Poza zakresem pozostają:

- kampania 30–40-dniowa;
- pełne pory roku;
- crafting i drzewka ulepszeń;
- tradycyjna walka;
- w pełni proceduralny Sokoban;
- nowy losowy makrograf przy każdym runie;
- trwałe bonusy do statystyk;
- procent ukończenia mapy;
- obowiązek odwiedzenia każdego węzła;
- pełny zapis środka planszy przed stabilizacją resolvera.
- spatial backpack, side-pocket quick use, and the M9 satiety economy; these have an accepted deferred contract but are intentionally evaluated after the no-backpack vertical slice.

### Rationale — dlaczego

Zakres zawiera wszystkie elementy potrzebne do oceny głównej obietnicy, lecz ogranicza ilość contentu i ryzyko techniczne. Każdy element spoza zakresu może zostać oceniony na podstawie danych z pełnej pętli zamiast intuicji.

## 21. Kontrakt architektury wspierającej projekt gry

### 21.1. Czysty rdzeń domenowy

#### Decyzja

Kod zasad gry nie przechowuje `GameObject`, `MonoBehaviour`, `Transform`, prefabów, colliderów ani Unity `InstanceID`.

#### Rationale — dlaczego

Reguły muszą być szybkie do testowania, odtwarzania i przeszukiwania przez solver bez uruchamiania sceny.

### 21.2. Komendy, resolver i zdarzenia

#### Decyzja

Wejście tworzy komendę, centralny resolver wylicza wynik i listę zdarzeń, a prezentacja je odtwarza.

```text
Input → PlayerCommand → TurnResolver → TurnResult + GameEvent[] → Presentation
```

#### Rationale — dlaczego

Jedna ścieżka wykonania zapobiega podwójnemu ruchowi, wielokrotnemu kosztowi i rozbieżności między AI, UI i fizyką.

### 21.3. Stabilne ID i data-driven content

#### Decyzja

Nodes, edges, route segments, resource families, item definitions, fixed tool sources, facts, tools, enemies, and archetypes have stable textual IDs. Content definitions are immutable during a run; mutable instance state is stored separately.

#### Rationale — dlaczego

Indeks listy i referencja do assetu nie są bezpiecznymi identyfikatorami save'a. Rozdzielenie definicji od instancji zapobiega współdzieleniu modyfikowalnego stanu przez dwa runy.

### 21.4. Wersjonowany i atomowy zapis

#### Decyzja

DTO profilu i runu posiadają `schemaVersion`. Zapis odbywa się przez plik tymczasowy, zastąpienie bieżącego pliku i zachowanie backupu.

#### Rationale — dlaczego

Atlas ma być najcenniejszym trwałym artefaktem gracza. Jego utrata przez przerwany zapis byłaby szczególnie dotkliwa, a rozwój contentu nie może unieważniać wszystkich profili.

### 21.5. Docelowa rola obecnych komponentów

| Obecny komponent | Docelowa odpowiedzialność | Rationale |
| --- | --- | --- |
| `GameManager` | composition root/fasada sesji | nie powinien jednocześnie posiadać danych, tur, AI, scen i UI |
| `BoardManager` | przejściowa fasada generatora, walidatora i prezentera | generowanie danych musi być oddzielone od `Instantiate` |
| `PlayerScript` | wejściowy adapter i docelowo widok gracza | zdrowie, jedzenie i zasady nie mogą zależeć od życia prefabu |
| `MovingObject` | wyłącznie animacja ruchu albo usunięcie | linecast i coroutine nie są źródłem prawdy |
| `Enemy` | widok przeciwnika | AI i cadence należą do modelu |
| `Wall` | widok przeszkody | HP i istnienie przeszkody należą do `BoardState` |
| `ManageRecords` | opcjonalny leaderboard | leaderboard nie jest repozytorium profilu ani atlasu |

## 22. Strategia testów i bramki projektowe

### Decyzja

Każda mechanika przechodzi trzy rodzaje weryfikacji:

1. **EditMode** — czyste reguły i przypadki brzegowe;
2. **PlayMode** — integracja z Unity, sceną, UI i cyklem życia;
3. **Playtest** — czy gracz poprawnie rozumie informację i podejmuje zamierzoną decyzję.

### Rationale — dlaczego

Test jednostkowy może potwierdzić poprawny ruch Listenera, ale nie sprawdzi, czy jego ikonę da się zrozumieć. Playtest może wykryć nieczytelność, ale bez testu deterministycznego trudno odtworzyć błąd. Te warstwy rozwiązują inne problemy.

### Bramka atlasu

- Gracz rozumie najpóźniej w drugim runie, co pozostaje po śmierci.
- Używa wcześniejszej wiedzy przy wyborze trasy.
- Nie interpretuje atlasu wyłącznie jako listy poziomów.
- Część testerów dobrowolnie zaczyna drugą wyprawę.
- A tester can distinguish `rumored` supply from a historical observed range and uses route length when comparing totals.
- Reloading or revisiting a depleted board never creates a false additional observation.

### Bramka intentów

- Większość testerów poprawnie przewiduje zachowanie znanego zombie.
- Intent i wykonanie zawsze korzystają z tej samej reguły.
- Porażka daje się wyjaśnić bez odwołania do ukrytego losowania.

### Bramka narzędzi

- Siekiera i łopata prowadzą do różnych decyzji.
- Każde narzędzie ma koszt lub ryzyko, nie tylko premię.
- Brak narzędzia nie tworzy losowego softlocka.

### Bramka landmarku

- Wszystkie wspierane warianty mają rozwiązanie.
- Istnieją co najmniej dwa sensowne rezultaty.
- Gracz potrafi wyjaśnić konsekwencję wybranego rozwiązania.

### Inventory and resource-pressure gate

- No accepted or rejected path duplicates or silently loses an item.
- Players understand that one bag session allows at most two `Use`/`Discard` actions but costs one complete turn total.
- A rejected primary command consumes neither a quick-pocket item nor turn resources.
- Players use current health, satiety, free space, pockets, tools, route length, and atlas knowledge to choose between routes.
- Observed sessions show less compulsion to collect every resource and at least some deliberate food-versus-healing choices.

### Rationale wskaźników

Te bramki są hipotezami diagnostycznymi, nie statystycznym dowodem sukcesu rynkowego. Ich rolą jest zatrzymanie produkcji contentu, gdy główna informacja lub pętla nadal nie działa.

## 23. Wymagany format kart `HB-xxx`

### Decyzja

Każde zadanie implementacyjne MUSI zawierać:

```text
ID i nazwa
Powiązana sekcja kontraktu
Rationale
Obecne zachowanie
Oczekiwany rezultat
Zakres
Non-goals
Zależności
Dozwolony obszar plików
Kryteria akceptacji
Plan testów
Wpływ na save'y i kompatybilność
Wymagany handoff
```

### Rationale — dlaczego

Agent nie powinien rekonstruować intencji zadania z numeru i nazwy. `Rationale` pozwala mu rozstrzygnąć nieprzewidziany szczegół zgodnie z celem, a `Non-goals` ogranicza rozszerzanie zakresu.

### Przykład — HB-010 `RunState`

**Powiązana sekcja:** 6.1 i 6.2.

**Rationale:** uzyskać jedno autorytatywne miejsce dla danych wyprawy, niezależne od sceny i prefabu gracza. To zapobiega resetom przez cykl życia Unity, współdzieleniu danych oraz podwójnemu aktualizowaniu zasobów.

**Oczekiwany rezultat:** run można utworzyć, zmodyfikować i zresetować w czystym teście C#; widok gracza tylko prezentuje jego dane.

**Non-goals:** w tym zadaniu nie należy jeszcze implementować JSON-a, atlasu, generatora ani pełnego snapshotu planszy.

**Kryteria akceptacji:** przejście sceny zachowuje stan, nowy run go resetuje, a `PlayerScript` nie jest właścicielem zdrowia i jedzenia.

## 24. Proces zmiany kontraktu

### Decyzja

Zmiana decyzji następuje jawnie:

1. opis problemu lub wyniku playtestu;
2. proponowana zmiana;
3. alternatywy;
4. wpływ na istniejące zadania, save'y i testy;
5. aktualizacja tej sekcji lub dodanie ADR;
6. dopiero potem implementacja.

### Rationale — dlaczego

Kontrakt ma chronić spójność, ale nie może zamrozić eksperymentów. Jawny proces pozwala zmieniać kierunek na podstawie danych bez pozostawiania sprzecznych założeń w kodzie i dokumentacji.

## 25. Otwarte parametry balansowe

Poniższe wartości mają zostać ustalone przez konfigurację i playtest, a nie przez przypadkową decyzję implementatora:

- początkowe zdrowie i jedzenie;
- bazowy koszt zaakceptowanej akcji;
- liczba użyć narzędzia;
- liczba tur ręcznego karczowania lub kopania;
- zasięg hałasu siekiery;
- czas działania dołu;
- budżety trudności poszczególnych dni;
- dokładna liczba węzłów i gałęzi po prototypie pięciodniowym.
- target and supported distribution of route-segment counts per edge;
- resource-family thresholds for truthful `low` / `medium` / `high` rumors;
- individual consumable values and route supply variance after M9.

The M9 starting geometry and resource model are accepted implementation baselines, not choices for an implementer to improvise: a `3×3` main bag, two one-item side pockets, health capped at `100`, satiety capped at `200`, a normal threshold of `100`, and action costs of `1`/`2`. They remain data-driven so a later playtest decision can revise their numbers without changing ownership or atomicity.

### Rationale — dlaczego

Kontrakt definiuje znaczenie i relacje systemów. Liczby balansowe powinny być łatwe do zmiany i wynikać z obserwacji, a nie zostać przypadkowo utrwalone w kodzie jako część architektury.

## 26. Otwarte decyzje zachowania

Poniższe pytania mają status **Open**. Agent nie może rozstrzygnąć ich sam, jeżeli realizowane zadanie od nich zależy.

| ID | Pytanie | Rekomendacja robocza | Blokuje |
| --- | --- | --- | --- |
| O-001 | Czy atlas fabularnie należy do tej samej postaci, czy do schronienia i kolejnych zwiadowców? | wspólny atlas schronienia, ponieważ naturalnie tłumaczy śmierć i kolejne runy | narrację podsumowania i nowego runu |

### Decyzje rozstrzygnięte dla bieżącego zakresu

| ID | Decyzja właściciela | Warunek ponownego otwarcia |
| --- | --- | --- |
| O-002 | 2026-09-27 — po nagrodzie i koszcie najpierw rozstrzygana jest śmierć z głodu; martwy gracz nie kończy planszy, nawet jeśli akcja weszła na wyjście. | Osobna decyzja produktowa zmieniająca priorytet terminalnych wyników tej samej akcji. |
| O-003 | 2026-09-27 — until M9, food is collected automatically on entry and resolved before the action cost; tools require conscious interaction. M9 replaces only the food/consumable pickup rule with section 27.6. | A separate product decision changing conscious tool pickup or the accepted M9 transfer semantics. |
| O-004 | 2026-09-08 — PC jest platformą referencyjną dla vertical slice. Mobile ma zachować tę samą semantykę po ustabilizowaniu interakcji. | Osobna decyzja produktowa zmieniająca platformę referencyjną lub wymagająca równorzędnej walidacji filesystemu na mobile. |
| O-005 | 2026-08-27 — leaderboard online nie jest częścią bieżącego projektu; najpierw powstaje prywatne podsumowanie wyprawy. Usuniętej integracji Dreamlo nie przywracamy, a rotację starej wartości właściciel świadomie odkłada. | Osobna decyzja produktowa o ponownym wprowadzeniu funkcji online; wtedy wymagane są nowy model bezpieczeństwa, nowa integracja i poświadczenia, bez ponownego użycia historycznej wartości. |
| O-006 | 2026-09-09 — `Exit to Menu` zachowuje poprawny run save dla `Continue`; trwałe porzucenie jest osobną, jednoznaczną akcją. | Osobna decyzja produktowa zmieniająca oczekiwania gracza wobec wznowienia lub zamknięcia runu. |
| O-007 | 2026-09-27 — one selected edge is one numbered day and may contain a variable authored sequence of local boards; route choice and atlas return happen at reached nodes. | A product decision returning to one-board-per-day travel or changing when a day advances. |
| O-008 | 2026-09-27 — the atlas stores truthful qualitative rumors, historical completed-route resource ranges, separate partial lower bounds, and persistent icons for stable repeatable tool sources. | A product decision making ordinary loot fixed/currently guaranteed or allowing deliberately false route information. |
| O-009 | 2026-09-27 — section 27 defines the accepted spatial bag, satiety, pickup, bag-session, and quick-pocket behavior; implementation remains deferred until its roadmap gate. | A product decision changing inventory topology, action budgets, ownership, or atomic turn semantics. |

Wskazówki tras w MVP nie są decyzją otwartą: MUSZĄ być prawdziwe w odniesieniu do obserwowalnych, stabilnych cech. Mogą być niepełne, a dynamiczne warunki mogą zmienić wartość drogi, lecz gra nie wprowadza celowo fałszywego opisu.

## 27. Accepted future contract: route legs, atlas observations, and inventory

### 27.1. Status and migration boundary

#### Decision

The rules in this section are **Accepted design with Deferred implementation** where explicitly assigned to M9. The route-leg foundation is implemented earlier by `M3.11`–`M3.12` because board identity, generation, save, and replay depend on it.

Until the named migration card is accepted, existing implemented behavior remains authoritative. In particular, `M3.4` automatic food pickup and its fixed cost are not retroactively incorrect. When M9 begins, its cards deliberately replace that transitional food path rather than maintaining two runtime rule sets.

#### Rationale

The game must remain playable while the no-backpack vertical slice is tested. At the same time, future work must not cement node-as-board, automatic food, or unbounded resource assumptions so deeply that the later inventory becomes a rewrite.

#### Implementation consequences

- Historical `Done` cards keep their recorded scope and validation.
- New work uses follow-up migrations and explicit schema versions.
- M9 remains after the no-backpack playtest gate and does not depend on optional M8 features.

### 27.2. Route legs, local boards, and days

#### Decision

- Committing an outgoing edge starts one route leg and one player-facing day.
- The first leg is `Day 1`; no player-facing `Day 0` exists.
- An edge owns an ordered, non-empty list of stable route-segment IDs. Different edges may have different lengths; five boards is an initial content target, not a hard-coded invariant.
- Starting a leg creates one persisted run-local `routeLegId`/traversal ID. Reusing the same directed edge later in the same run creates a different leg ID.
- Each segment creates one local board with an address containing at least run ID, route-leg ID, edge ID, stable segment ID, day number, generation version/configuration, and deterministic board seed.
- An intermediate `ExitReached` advances to the next segment without incrementing the day, marking the edge `Traversed`, marking the destination `Visited`, or opening route choice.
- Only the final segment completes the leg, commits `Traversed`, reaches the destination checkpoint, records the complete observation sample, and opens the atlas.
- In normal flow the full-screen `Day N` transition appears once before the first segment and hides the gameplay HUD. Across a crash it has explicit **at-most-once** semantics: the run checkpoints `Consumed` before rendering, so recovery may skip an interrupted intro but never repeats it.
- A landmark may be the final authored segment of an inbound leg; it does not require an additional day merely because it uses a special board template.

#### Rationale

Several short tactical situations can make one strategic route choice meaningful without enlarging the 8×8 board. Stable segment IDs also let saves, replay, fixed tool sources, and diagnostics identify the exact situation even when content order changes.

#### Implementation consequences

- `RunState` separates the last reached node/checkpoint, optional active leg with `routeLegId`, completed-day count, current day number, durable `Pending`/`Consumed` Day-transition state, and structured travel history.
- Route selection begins and persists a leg; it does not teleport the run to the destination.
- Board seeds never derive from node/day alone.
- `Continue` restores the same route segment and never repeats a consumed day transition or accepts an outcome from another leg/segment.
- Published segment IDs are migration-sensitive content IDs; reordering a list must not silently change identity.

### 27.3. Route-resource knowledge and fixed tool sources

#### Decision

- Atlas resource knowledge is keyed by directed edge and stable resource-family ID, not by a destination node or UI label.
- `Rumored` supply uses truthful authored qualitative bands such as `low`, `medium`, and `high`. Thresholds are defined per resource family, and generation must stay inside the advertised band.
- `Sighted` may reveal the resource family or icon, but it does not fabricate an exact total.
- One completed traversal contributes at most one sample containing the total initially observable supply across all of that leg's segments. The atlas displays the completed-sample minimum, maximum, and sample count, for example `Observed so far: 9–10 · 2 visits`.
- A death or abandoned run partway through a leg may persist only a separate lower bound such as `At least 6 · observed 3/5 boards`. Partial data never changes the completed minimum/maximum.
- Only actually revealed or observable items count. Hidden content is not leaked. Collection, leaving an item, revisiting a depleted board, or reloading does not create a new sample or change the recorded initial observation.
- The observation identity is the persisted run ID plus route-leg/traversal ID. This makes reload idempotent while allowing a later legal traversal of the same directed edge in the same run to contribute a distinct sample.
- Route length is shown separately from aggregate supply.
- A fixed tool source has a stable source ID, tool-definition ID, and physical edge-segment binding. Its atlas icon may be visually anchored to that segment or its destination node only when the owning inbound edge remains unambiguous; it must never imply availability from another route. Execution always resolves through a concrete `BoardAddress`. Multiple axe or shovel sources are allowed.
- Seeing/recognizing a fixed source permanently reveals its atlas icon; pickup is not required. The source is guaranteed once per new run after it is authored as fixed. Taking it marks only current-run availability as `taken`; the profile icon remains.
- No fixed or random tool source may be required for a mandatory exit.

#### Rationale

The atlas becomes tangible metaprogression when repeated travel improves a useful record, but it must remain honest about variation. Historical ranges create informed choices without converting dynamic loot into a promise. Stable tool icons reward discovery more strongly because their authored repeatability is explicit.

#### Implementation consequences

- `ProfileState` owns typed observation aggregates and discovered source IDs.
- The active leg owns its observation accumulator and current-run taken-source state.
- `BoardState` owns concrete item/tool entities.
- Facts about tool mechanics remain separate from knowledge of tool-source locations.
- Atlas queries choose among rumor, partial lower bound, completed historical range, and current-run taken state; UI does not infer these from prose.

### 27.4. Item definitions, instances, and inventory topology

#### Decision

- Every run starts with a spatial backpack whose main area is a fixed `3×3` grid. The initial M9 configuration starts it empty; future starting contents remain explicit run configuration rather than hidden UI state.
- Carried consumable items are rectangular and have immutable definition IDs plus run-local instance IDs.
- Initial M9 supports fixed orientation only: no rotation, irregular shapes, or stacks. A berry bundle is one `1×1` item rather than a stack system.
- Two side pockets each hold exactly one eligible item. A pocket item must be `QuickPocketEligible`, one cell wide, and one to three cells tall; a `1×1` item still occupies the whole pocket.
- The two generic tool slots remain separate from the bag and pockets.
- Initial M9 has no bag expansion, weight, crafting, spoilage, rarity affixes, or drop-to-ground command.

#### Rationale

The small grid makes packing a readable resource decision without turning the game into warehouse management. Separate quick pockets create preparation choices, while separate tools preserve their existing charge/equipment semantics.

#### Implementation consequences

- `ItemDefinition` contains stable ID, footprint, resource family/category, pocket eligibility, and use-effect ID.
- `ItemInstanceState` contains instance identity and only mutable per-instance data.
- `InventoryState` contains placements and the left/right pocket references; it contains no Unity objects.
- Placement validation rejects overlap, out-of-bounds anchors, duplicate instances, and incompatible pockets.
- Inventory contents reset with `RunState` and never become profile power.

### 27.5. Health, satiety, and consumable effects

#### Decision

- M9 replaces the player-facing `Food` concept with `Satiety` as the single runtime resource.
- Health is clamped to `0..100` and can never be healed above `100`.
- Satiety is clamped to `0..200` and starts at `100` in the initial M9 balance configuration.
- Every accepted board command, including `Move`, `Wait`, interaction, tool use, and inventory management, pays the resource cost. Rejected commands remain free.
- The cost tier is sampled from satiety at the start of the command: `1` at `0..100`, `2` above `100`. Consumable effects resolve before that already-selected cost. Therefore eating from `100` to `150` still costs `1` for that turn; the next accepted command costs `2` while satiety remains above `100`.
- Reaching zero satiety follows the existing starvation priority. Excess healing or satiety beyond its cap is wasted rather than stored elsewhere.
- Item effects are data-driven domain operations; an item may intentionally be weak as long as its effect is legible.

#### Rationale

Health cannot be banked for an unknown future injury, while food can be carried or deliberately converted into inefficient short-term buffer. This makes food-versus-healing routes depend on the player's current condition and bag layout without adding combat statistics.

#### Implementation consequences

- The migration renames the one authoritative runtime field; it does not keep synchronized `Food` and `Satiety` copies.
- Cost policy and caps live in configuration/domain code, not UI.
- Events expose the chosen cost tier and capped/wasted effect for clear presentation and replay.

### 27.6. Ground-item pickup

#### Decision

- Entering a tile with a consumable offers `Use now`, `Store`, or `Leave` as the decision that completes the same movement turn.
- An accepted interaction that reveals a consumable, such as opening a shovel cache, creates one concrete ground item instance and uses the same `Use now` / `Store` / `Leave` transfer contract within that interaction turn; it never calls a raw resource restore that bypasses item ownership.
- `Use now` remains available when the bag is full if the effect itself is legal. `Store` is unavailable when no valid placement exists.
- The item is removed from `BoardState` only after a successful use or transfer.
- `Leave`, an invalid choice, or lack of capacity leaves the same item instance on the ground.
- The prompt is triggered by entry, not by standing on the tile. To try again, the player must step off and re-enter, paying the normal turns and enemy phases.
- A consciously accepted tool pickup follows the separate equipment flow: free slot, or `Replace`/`Leave` when full.
- Discarding a carried item destroys it permanently and never places it on the current tile.

#### Rationale

Pickup should create an immediate resource decision without silently deleting rewards or pausing the resolver halfway through a committed move. Re-entry gives the player a recoverable choice, but its tactical cost prevents free repeated prompts.

#### Implementation consequences

- The submitted command contains the movement and pickup choice as one atomic intent; a presentation modal does not mutate domain state.
- Board-to-run transfer is all-or-nothing and cannot duplicate an item after stale input, reload, or rapid submit.

### 27.7. Backpack session

#### Decision

- Pressing `B` opens a modal inventory draft. Leaving it through `Finish`, including a no-op finish, submits one `ManageInventoryCommand` and costs exactly one complete turn.
- Within that one session the player may perform at most two ordered item actions. Each action is either `Use` or permanent `Discard`; any mix of zero, one, or two is legal.
- Reorganizing any number of remaining items between the main grid and side pockets is included in the same turn and does not consume the two-action budget.
- The command carries the expected inventory revision, its ordered actions, and the final layout. The resolver validates the entire proposal before the first mutation and commits it atomically.
- Every accepted `ManageInventoryCommand`, including a no-op finish, increments the inventory revision exactly once so duplicate submission becomes stale.
- Invalid or stale proposals apply nothing and consume no turn or resources.
- One accepted bag session produces one resource cost and one enemy/environment phase, never one phase per used item.
- Quick-pocket use cannot be combined with `ManageInventoryCommand`. Items placed into pockets become quick-usable only after the bag turn fully resolves.

#### Rationale

One opening must be meaningful enough to justify a valuable turn. Two item actions allow combinations such as food plus medicine after danger without permitting an unlimited heal/eat pause before enemies react. Atomic final-layout submission keeps save, replay, and rollback behavior comprehensible.

#### Implementation consequences

- UI maintains only a draft; authoritative inventory changes on accepted command completion.
- After a successful open there is no normal free close path: `Finish`, including a no-op finish, pays the one-turn cost. The UI communicates that consequence before confirmation.
- A third use/discard is disabled and rejected by domain validation if submitted anyway.

### 27.8. Quick-pocket composite turns

#### Decision

- At most one item from either side pocket may be attached to an otherwise ordinary accepted non-inventory command.
- Supported primary actions include `Move`, `Wait`, legal interactions, and legal tool use where their command contract allows the modifier.
- The item effect and primary action form one atomic turn with one resource cost and one enemy/environment phase.
- A rejected or stale primary command consumes neither the pocket item, turn, satiety, nor tool charge.
- Quick use is never combined with a bag session. Using both pockets requires two accepted ordinary turns; the player may attach a pocket item to `Wait` when no movement or interaction is desired.

#### Rationale

Pockets reward preparation by avoiding an extra bag-opening turn, but the one-item limit preserves enemy response and prevents front-loading both pockets plus two bag actions before a single hostile phase.

#### Implementation consequences

- Quick use is a command modifier/envelope, not an independently resolved zero-time command.
- Validation covers item eligibility, pocket ownership, primary command validity, and all-or-nothing consumption.

### 27.9. Persistence, replay, UI, and validation

#### Decision

- Route-leg, atlas-observation, and inventory changes use separate versioned DTO migrations with stable textual IDs.
- The run save includes active route address/accumulator, durable Day-transition status, item instances, bag placements, pockets, tool instances, health, and satiety once their owning cards ship.
- The profile save includes typed observation aggregates and discovered fixed-source IDs.
- Mid-board snapshot, when implemented, captures only a completed turn; modal pickup and bag drafts are never persisted.
- Replay is a versioned tagged-command format. Its canonical hash expands whenever authoritative route, resource, tool, or inventory state is added; unknown command types fail explicitly.
- The route-choice view shows current health, satiety, predicted cost tier, free bag capacity, pocket/tool status, route length, rumor or historical resource range, and fixed-source knowledge without calculating a single “best route” score.

#### Rationale

These features cross `ProfileState`, `RunState`, and `BoardState`; save and replay are therefore part of the mechanic, not cleanup after UI. Showing both current need and historical route knowledge is what turns inventory pressure into a strategic choice.

#### Validation

- Round-trip and migration tests cover empty, full, partially filled, and invalid inventories plus active and completed route legs.
- Deterministic replay covers pickup, two-action bag sessions, quick use, route-segment advance, complete/partial atlas samples, and fixed-source collection.
- Property tests prove no overlap, duplicate placement, false rumor band, duplicated observation sample, or mandatory tool softlock.
- PlayMode tests cover modal input locks, one enemy phase per accepted composite command, Day transition HUD hiding, and `Continue` at route boundaries.
- Playtests compare the no-backpack baseline with M9 using the inventory gate in section 22.

## Appendix A — obserwowane ograniczenia obecnej implementacji

Ta sekcja jest opisowa i wyjaśnia, dlaczego fundamenty pojawiają się przed rozbudową contentu.

- `PlayerScript.AttemptMove()` wywołuje bazową próbę ruchu, a następnie ponownie `Move`, co może powodować podwójne rozstrzygnięcie jednej komendy.
- Zdrowie i jedzenie są kopiowane między `PlayerScript` i `GameManager`, między innymi przez `OnDisable`, zamiast posiadać jednego właściciela.
- `GameManager` łączy cykl sceny, numer dnia, AI, UI i stan zasobów.
- `MovingObject` używa `Physics2D.Linecast` jako autorytatywnej reguły ruchu.
- `BoardManager` generuje wyłącznie pola wewnętrzne, zostawiając wolny obwód planszy, oraz miesza generowanie danych z `Instantiate`.
- Losowość rozgrywki korzysta z globalnego `UnityEngine.Random`.
- `Wall` przechowuje autorytatywne HP w komponencie widoku.
- Brakuje jawnego stanu zwycięstwa; istnieje głównie rosnący licznik dni i game over.
- Historyczna integracja `ManageRecords` zawierała prywatny identyfikator leaderboardu. Bieżący klient nie zawiera transportu ani tej wartości i działa wyłącznie offline. Właściciel 2026-08-27 świadomie odłożył rotację do ewentualnego powrotu funkcji online; nie oznacza to technicznego unieważnienia starej wartości, dlatego nie wolno jej ponownie użyć, a publiczne udostępnienie historii nadal wymaga osobnej akceptacji ryzyka albo oczyszczenia historii i unieważnienia po stronie usługi.
- Po HB-000E sceny, prefaby, animacje i wspierane ustawienia projektu są zapisane jako Unity YAML. Jedynym znanym binarnym wyjątkiem w `ProjectSettings` pozostaje legacy `NetworkManager.asset`, którego publiczny workflow Unity `6000.3.21f1` nie konwertuje; nie stanowi on bariery dla diffu assetów gry.
- Repozytorium Git znajduje się katalog wyżej niż projekt Unity, natomiast główny `.gitignore` używa wzorców zakotwiczonych tak, jakby leżał w katalogu projektu. W efekcie wygenerowane katalogi projektu mogą nie być prawidłowo ignorowane.

### Rationale — dlaczego ta lista istnieje

Zadania fundamentowe nie są abstrakcyjnym „przepisywaniem na czysto”. Każde usuwa konkretną przeszkodę dla trwałego atlasu, deterministycznych tur, automatycznych testów albo bezpiecznej pracy wieloagentowej.
