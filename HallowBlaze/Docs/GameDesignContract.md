# HallowBlaze — Game Design Contract

| Pole | Wartość |
| --- | --- |
| Status | Żywy kontrakt; obowiązuje do jawnej zmiany |
| Wersja | 0.2 |
| Ostatnia aktualizacja | 2026-08-29 |
| Zakres | Pierwszy grywalny vertical slice i fundament dalszej produkcji |

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

#### Rationale — dlaczego

Pełny reset byłby sprzeczny z główną obietnicą. Zachowanie ekwipunku i statystyk osłabiłoby natomiast napięcie survivalowe. Rozdzielenie tych warstw pozwala jednocześnie odczuwać ryzyko i postęp.

#### Konsekwencje implementacyjne

- Profil i run MUSZĄ być osobnymi stanami oraz osobnymi zapisami.
- `StartNewRun()` nie może modyfikować profilu.
- Pełne wymazanie atlasu wymaga oddzielnej, potwierdzanej operacji.

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
| Dzień / etap | Jedno przesunięcie wyprawy na makromapie; nie pojedyncza akcja |
| Węzeł / node | Stałe miejsce na makromapie |
| Droga / edge | Połączenie dwóch węzłów i możliwy kierunek podróży |
| Plansza / board | Lokalna, turowa przestrzeń kafelkowa dla jednego etapu |
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
- Dzień zwiększa się raz w kontrolowanym przepływie podróży, a nie w `sceneLoaded`.

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
| `ProfileState` | atlas, odkryte fakty, statystyki | wiele runów |
| `RunState` | zdrowie, jedzenie, narzędzia, bieżący węzeł i trasa | do śmierci lub zwycięstwa |
| `BoardState` | pozycje, przeszkody, łupy, zombie, efekty pola | jedna plansza lub jej snapshot |

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

### 6.5. Śmierć, restart i zapis

#### Decyzja

- Śmierć najpierw utrwala zmiany profilu, a potem zamyka run.
- Nowa wyprawa resetuje `RunState` i pozostawia `ProfileState`.
- MVP wznawia grę bezpiecznie pomiędzy planszami lub z ekranu mapy.
- Zapis środka planszy jest rozszerzeniem po stabilizacji resolvera.
- Pełny reset profilu jest osobną opcją z potwierdzeniem.

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
- `Exit to Menu` zapisuje aktywny run na granicy planszy, odłącza go od bieżącej sesji i wraca do menu głównego. Poprawny zapis pozostaje dostępny dla `Continue`; akcja nie usuwa profilu, ustawień audio ani lokalnego rekordu.
- Trwałe porzucenie runu jest osobną, jednoznaczną akcją. Usuwa bieżący i zapasowy plik runu, ale nigdy plik profilu.
- Przed wznowieniem, wyjściem, wyłączeniem kontrolera albo zmianą sceny gra przywraca normalny upływ czasu i usuwa blokadę wejścia.

Mobilny sposób otwierania pauzy pozostaje poza tym zakresem do rozstrzygnięcia O-004. Relacja `Exit to Menu` z wersjonowanym `Continue` została rozstrzygnięta w O-006: poprawny run save pozostaje dostępny do wznowienia.

#### Rationale — dlaczego

Menu otwierane podczas rozgrywki jest granicą sterowania, a nie kosmetyczną nakładką. Samo zatrzymanie `Time.timeScale` nie blokuje kodu czytającego input w `Update`, więc bez jawnej bramki gracz mógłby tracić zasoby lub wykonywać akcje pod menu. Jedna współdzielona funkcjonalność ustawień zapobiega rozjazdowi wartości i prezentacji pomiędzy scenami, a jawne porzucenie legacy runu chroni kolejny start przed stanem pozostawionym przez obiekty `DontDestroyOnLoad`.

#### Konsekwencje implementacyjne

- Stan widoku pauzy jest przejściowym stanem prezentacji i nie należy do `ProfileState`, `RunState` ani formatu save.
- Kontroler pauzy musi osobno zarządzać widocznością UI, fokusem `EventSystem`, blokadą wejścia oraz odtworzeniem upływu czasu.
- Menu główne i pauza korzystają ze wspólnego panelu lub kontrolera ustawień; nie utrzymują dwóch niezależnych implementacji tych samych przełączników.
- Powrót do menu wykonuje jawny cleanup legacy runu przed załadowaniem sceny i jest bezpieczny przy wielokrotnym wywołaniu.
- Test integracyjny musi udowodnić brak kosztu podczas pauzy, poprawne przejścia Escape/Back oraz świeży start po `Exit to Menu`.

## 7. Atlas i świat

### 7.1. Stały świat dla jednego profilu

#### Decyzja

Makrograf świata jest stały między runami. Pierwszy vertical slice korzysta z ręcznie zaprojektowanego grafu. Lokalny układ planszy w danym węźle może być generowany ponownie.

W przyszłości nowy profil może otrzymywać świat wygenerowany raz z szablonów. W takim wariancie pełny wynik generowania grafu musi zostać zapisany; sam seed nie jest wystarczającą gwarancją po zmianach generatora.

#### Rationale — dlaczego

Trwały atlas ma wartość tylko wtedy, gdy notatki odnoszą się do stabilnych miejsc i dróg. Ręczny graf pozwala także świadomie projektować wskazówki, rozwidlenia, powroty oraz tempo ujawniania celu.

#### Konsekwencje implementacyjne

- Węzły i krawędzie mają stabilne tekstowe ID niezależne od kolejności w Inspectorze.
- Zmiana lokalnego seeda nie zmienia położenia cmentarza, sadu ani landmarku.
- Generator lokalnej planszy otrzymuje `worldNodeId` i rodzinę biomu jako dane wejściowe.

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

#### Rationale — dlaczego

Jeżeli atlas zapisuje losowe łupy tak, jakby zawsze tam były, zaczyna wprowadzać gracza w błąd. Jeżeli wszystko jest dynamiczne, mapa nie dostarcza użytecznej wiedzy. Jasny podział pozwala łączyć rozpoznawalność z niepewnością.

#### Konsekwencje implementacyjne

- UI MUSI wizualnie odróżniać informacje trwałe od warunków aktualnej wyprawy.
- Znana droga nie powinna zawsze być automatycznie najlepsza; jej wartość może zależeć od narzędzi, zasobów i pogody.

### 7.5. Wybór drogi

#### Decyzja

Po planszy gracz wybiera spośród maksymalnie kilku kierunków opisanych obserwowalnymi, diegetycznymi wskazówkami. MVP nie pokazuje dokładnego łupu ani procentowego ryzyka.

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
- może być przelosowana przy kolejnej wyprawie do tego samego węzła;
- jest walidowana przed pokazaniem graczowi.

### Rationale — dlaczego

Obecny generator losuje elementy tylko wewnątrz planszy, pozostawiając wolny obwód i stałe wyjście w rogu. Tworzy to dominującą, mało interesującą trasę. Model-first i walidacja mają zapewnić poprawną, lecz nadal zmienną sytuację taktyczną.

### Konsekwencje implementacyjne

- Generator najpierw tworzy `BoardBlueprint`, potem go waliduje, a dopiero na końcu tworzy widoki.
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

Nagroda z poprawnej akcji zbierania jest rozliczana przed kosztem tej akcji, więc może uratować gracza przed głodem. Dotarcie do wyjścia kończy planszę przed fazą zombie. Śmierć z kosztu własnej akcji również zatrzymuje dalsze fazy. Priorytet jednoczesnego osiągnięcia wyjścia i progu śmierci pozostaje decyzją otwartą w sekcji 26.

#### Rationale — dlaczego

Jawna kolejność usuwa spory o to, czy zombie może uderzyć gracza już po ucieczce, czy głód zabija przed atakiem oraz kiedy zmiana bramy blokuje zaplanowany ruch. Jest też niezbędna do deterministycznych testów.

#### Konsekwencje implementacyjne

- `TurnResolver` wylicza cały wynik przed animacją.
- Prezentacja odtwarza `GameEvent[]`; nie liczy zasad ponownie.
- Nie można rozpocząć dwóch resolverów jednocześnie.

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
- Zablokowany intent nie jest zastępowany nowym ruchem; zwykle kończy się `Wait`.
- Rejestracja prefabów i kolejność w hierarchii nie mogą wpływać na inicjatywę.

### 11.2. Konflikty intentów

#### Decyzja

W MVP:

- inicjatywa wynika ze stabilnego `EntityId` lub jawnego parametru;
- pierwszy przeciwnik rezerwuje wolne pole;
- kolejny próbujący wejść na to samo pole czeka;
- zamiana miejsc jest zabroniona;
- przeciwnik z unieważnionym celem czeka zamiast przeplanowywać.

#### Rationale — dlaczego

Reguły konfliktu są częścią zachowania widocznego dla gracza. Pozostawienie ich kolejności komponentów Unity powodowałoby niestabilne i trudne do odtworzenia rezultaty.

### 11.3. Archetypy vertical slice

#### Decyzja

Vertical slice zawiera co najmniej:

1. **Shambler** — prosty pościg z jawnym rytmem ruchu;
2. **Listener** — reaguje na ostatni słyszany hałas i pokazuje `Investigate`.

Trzeci archetyp, np. biegacz poruszający się po prostej, jest opcjonalny po sprawdzeniu czytelności pierwszych dwóch.

#### Rationale — dlaczego

Shambler uczy pozycji i tempa, Listener tworzy możliwość świadomego manipulowania. Dwa jakościowo różne zachowania są cenniejsze niż wiele wariantów obrażeń.

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

#### Konsekwencje implementacyjne

- `ToolDefinition` zawiera niezmienne dane; stan konkretnej instancji znajduje się w runie.
- Niepoprawny cel nie zużywa tury ani ładunku.
- Zamiana narzędzia jest świadomą, modalną decyzją.
- W vertical slice nie ma craftingu, drzewka ulepszeń ani rozbudowanych napraw.

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

Węzły, drogi, fakty, narzędzia, przeciwnicy i archetypy posiadają stabilne tekstowe ID. Definicje contentu są niemodyfikowalne podczas runu; bieżący stan istnieje osobno.

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

### Rationale — dlaczego

Kontrakt definiuje znaczenie i relacje systemów. Liczby balansowe powinny być łatwe do zmiany i wynikać z obserwacji, a nie zostać przypadkowo utrwalone w kodzie jako część architektury.

## 26. Otwarte decyzje zachowania

Poniższe pytania mają status **Open**. Agent nie może rozstrzygnąć ich sam, jeżeli realizowane zadanie od nich zależy.

| ID | Pytanie | Rekomendacja robocza | Blokuje |
| --- | --- | --- | --- |
| O-001 | Czy atlas fabularnie należy do tej samej postaci, czy do schronienia i kolejnych zwiadowców? | wspólny atlas schronienia, ponieważ naturalnie tłumaczy śmierć i kolejne runy | narrację podsumowania i nowego runu |
| O-002 | Co wygrywa, gdy ta sama akcja osiąga wyjście i próg śmierci z głodu? | wyjście, jeśli gracz faktycznie wszedł na pole celu | ostateczne testy kontraktu tury |
| O-003 | Czy drobne przedmioty są podnoszone automatycznie przy wejściu, czy wymagają `Interact`? | jedzenie automatycznie; narzędzia przez świadomą interakcję | pickupy i UI zamiany |

### Decyzje rozstrzygnięte dla bieżącego zakresu

| ID | Decyzja właściciela | Warunek ponownego otwarcia |
| --- | --- | --- |
| O-004 | 2026-09-08 — PC jest platformą referencyjną dla vertical slice. Mobile ma zachować tę samą semantykę po ustabilizowaniu interakcji. | Osobna decyzja produktowa zmieniająca platformę referencyjną lub wymagająca równorzędnej walidacji filesystemu na mobile. |
| O-005 | 2026-08-27 — leaderboard online nie jest częścią bieżącego projektu; najpierw powstaje prywatne podsumowanie wyprawy. Usuniętej integracji Dreamlo nie przywracamy, a rotację starej wartości właściciel świadomie odkłada. | Osobna decyzja produktowa o ponownym wprowadzeniu funkcji online; wtedy wymagane są nowy model bezpieczeństwa, nowa integracja i poświadczenia, bez ponownego użycia historycznej wartości. |
| O-006 | 2026-09-09 — `Exit to Menu` zachowuje poprawny run save dla `Continue`; trwałe porzucenie jest osobną, jednoznaczną akcją. | Osobna decyzja produktowa zmieniająca oczekiwania gracza wobec wznowienia lub zamknięcia runu. |

Wskazówki tras w MVP nie są decyzją otwartą: MUSZĄ być prawdziwe w odniesieniu do obserwowalnych, stabilnych cech. Mogą być niepełne, a dynamiczne warunki mogą zmienić wartość drogi, lecz gra nie wprowadza celowo fałszywego opisu.

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
