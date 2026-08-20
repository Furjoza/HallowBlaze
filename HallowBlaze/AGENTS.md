# AGENTS.md — HallowBlaze

## 1. Zakres i cel

Ten plik obowiązuje wszystkich agentów pracujących w katalogu projektu Unity `HallowBlaze/` i jego podkatalogach.

Repozytorium Git znajduje się jeden poziom wyżej niż projekt Unity:

```text
repo root/
  .git/
  .gitignore
  HallowBlaze/          ← Unity project root i zakres tego AGENTS.md
    AGENTS.md
    Assets/
    Docs/
    Packages/
    ProjectSettings/
```

Celem tych zasad jest utrzymanie spójności projektu gry, ochrona zmian użytkownika i bezpieczna praca z assetami Unity. Role agentów są mechanizmem ograniczania zakresu oraz uprawnień, a nie substytutem testów i code review.

## 2. Hierarchia źródeł prawdy

Przy sprzeczności stosuj następującą kolejność:

1. bieżące instrukcje systemowe, deweloperskie i jawne polecenie użytkownika;
2. ten `AGENTS.md` dla procesu pracy i bezpieczeństwa repozytorium;
3. `Docs/GameDesignContract.md` dla zachowania gry i zaakceptowanych decyzji projektowych;
4. zaakceptowane ADR-y w `Docs/Decisions/` dla konkretnych decyzji technicznych;
5. aktywna karta `HB-xxx` w `Docs/TechnicalRoadmap.md` dla zakresu zadania;
6. istniejący kod i dotychczasowe wzorce, o ile nie są oznaczone jako legacy lub błąd.

Istniejące zachowanie prototypu nie jest automatycznie zamierzonym designem. Appendix A w `GameDesignContract.md` wymienia znane ograniczenia legacy.

Jeżeli aktywne zadanie zależy od decyzji oznaczonej jako `Open` w `GameDesignContract.md`, zatrzymaj implementację tej części i poproś właściciela projektu o decyzję. Nie wybieraj sam wariantu zmieniającego zachowanie gracza, semantykę resetu ani format save'a.

## 3. Obowiązkowy podział ról

### Coordinator / Planner

- utrzymuje kontrakt, roadmapę, kolejność zależności i kartę aktywnego zadania;
- analizuje repozytorium w trybie read-only, chyba że użytkownik jawnie zlecił zmianę dokumentacji;
- wyznacza dokładnie jednego writera;
- przyjmuje wynik albo zwraca zadanie do poprawy;
- jako jedyny aktualizuje status ticketu w roadmapie po weryfikacji.

### Developer

- jest jedynym agentem zapisującym pliki podczas aktywnego zadania;
- implementuje wyłącznie wybrany ticket i jego konieczne testy;
- nie rozpoczyna kolejnego ticketu;
- nie aktualizuje sam statusu zadania na `Done`;
- kończy pracę ustrukturyzowanym handoffem.

### Reviewer / Tester

- pracuje po zakończeniu zapisu przez Developera;
- domyślnie działa tylko do odczytu;
- sprawdza każde kryterium akceptacji i uruchamia adekwatne testy;
- raportuje błędy wraz z reprodukcją oraz lokalizacją;
- nie naprawia kodu „przy okazji”; poprawki wracają do Developera jako osobna iteracja.

### Specialist ad hoc

- wykonuje ograniczoną analizę, np. algorytmu, UX, save'ów lub generatora;
- domyślnie działa tylko do odczytu;
- nie rozszerza zakresu poza zlecony problem.

Planner i technical lead są na tym etapie jedną rolą. Niezależny lead/reviewer może zostać uruchomiony dla zmian architektury, save'ów, scen lub generatora.

## 4. Zasada jednego writera

W jednym współdzielonym worktree dokładnie jeden agent może modyfikować pliki.

Równolegle wolno wykonywać wyłącznie prace bez zapisu, takie jak:

- eksploracja kodu;
- research;
- projekt testów;
- analiza architektury;
- przegląd zakończonego diffu.

Nie wolno równolegle:

- edytować kodu w dwóch agentach;
- modyfikować scen, prefabów, assetów, `.meta`, `ProjectSettings` albo `Packages`;
- uruchamiać dwóch instancji Unity Editor na tym samym katalogu projektu;
- prowadzić review podczas gdy writer nadal zmienia te same pliki.

Równoległe pisanie może zostać dopuszczone dopiero na osobnych branchach/worktree, po ustabilizowaniu granic modułów i CI. Nawet wtedy jeden agent jest właścicielem konkretnej sceny lub prefabu.

## 5. Protokół pojedynczego ticketu

### Przed zmianami

Agent MUSI:

1. przeczytać w całości ten `AGENTS.md`;
2. przeczytać powiązane sekcje `Docs/GameDesignContract.md`;
3. przeczytać całą kartę aktywnego `HB-xxx`, w tym `Rationale` i `Non-goals`;
4. sprawdzić istniejące ADR-y mające zastosowanie;
5. sprawdzić stan odpowiednich plików i, jeśli Git jest dostępny, bieżący status/diff;
6. wskazać istniejące zmiany użytkownika, których nie wolno nadpisać;
7. potwierdzić zależności oraz otwarte decyzje;
8. zaplanować minimalny zestaw plików i testów.

Jeżeli karta nie posiada `Rationale`, kryteriów akceptacji lub jest zbyt szeroka na jedną spójną zmianę, Developer nie rozpoczyna implementacji. Coordinator musi najpierw poprawić kartę.

### Podczas zmian

- Zachowuj minimalny zakres potrzebny do spełnienia kryteriów.
- Nie implementuj przyszłych ticketów „przy okazji”.
- Nie wykonuj szerokiego formatowania, rename'ów ani porządków niezwiązanych z zadaniem.
- Zachowuj kompatybilność save'ów, chyba że ticket jawnie definiuje migrację lub reset.
- Każde nieuniknione odejście od karty zapisz w handoffie wraz z powodem.
- Dla plików tekstowych używaj małych, kontrolowanych patchy.
- Nie generuj ani nie edytuj ręcznie GUID-ów Unity.

### Po zmianach

Developer MUSI:

1. uruchomić testy wymagane przez kartę;
2. wykonać adekwatny smoke test lub jawnie wskazać, dlaczego nie był możliwy;
3. sprawdzić zmienione pliki oraz diff bez włączania zmian użytkownika do własnego zakresu;
4. potwierdzić każde kryterium akceptacji albo oznaczyć je jako niespełnione;
5. przekazać handoff według sekcji 12;
6. zatrzymać się bez rozpoczynania następnego ticketu.

## 6. Bezpieczeństwo Git i worktree

- Traktuj istniejące zmiany oraz pliki untracked jako własność użytkownika.
- Nie używaj `git reset --hard`, `git clean`, `git checkout --`, `git restore`, force-push ani destrukcyjnego rebase bez jawnego polecenia użytkownika.
- Nie wykonuj `git add -A` ani szerokich commitów obejmujących wygenerowane katalogi.
- Nie twórz commitów, branchy ani PR-ów bez jawnego polecenia użytkownika lub zakresu ticketu.
- Nie zmieniaj globalnej konfiguracji Git, w tym `safe.directory`, bez zgody użytkownika. Jeżeli Git odmawia pracy z powodu własności katalogu, zgłoś ograniczenie i kontynuuj bezpiecznymi kontrolami plików, o ile to wystarcza.
- Przed operacją obejmującą katalog nadrzędny potwierdź rzeczywisty repo root i dokładny zakres. Domyślnie nie zmieniaj plików poza projektem Unity.
- Ticket higieny może jawnie autoryzować zmianę rootowego `../.gitignore` lub `../.gitattributes`; nie rozszerzaj tego na inne pliki rodzica.

Do czasu zakończenia ticketów higieny repo zakładaj, że rootowy `.gitignore` niepoprawnie obsługuje zagnieżdżony projekt. Nie skanuj rekurencyjnie ani nie stage'uj:

- `Library/`;
- `Temp/`;
- `obj/`;
- `Logs/`;
- `Build/` i `Builds/`;
- `UserSettings/`;
- innych wygenerowanych wyników Unity lub IDE.

## 7. Bezpieczeństwo assetów Unity

Projekt używa Unity `6000.3.21f1`.

### Przed ukończeniem tekstowej serializacji

- Traktuj `.unity`, `.prefab`, większość `.asset` i część `ProjectSettings` jako potencjalnie binarne.
- Nie modyfikuj binarnego assetu ręcznie.
- Zmianę `Force Text`, `Visible Meta Files` i reserializację wykonuj wyłącznie w przeznaczonym do tego tickecie, przez Unity Editor i w osobnej zmianie.
- Nie mieszaj reserializacji z modyfikacją rozgrywki.

### Po ukończeniu tekstowej serializacji

- YAML nadal nie jest bezpieczny do ślepego automatycznego merge'owania.
- Jedna scena, prefab lub `ProjectSettings` ma jednego writera w danej iteracji.
- Nie rozwiązuj konfliktów YAML przez wybieranie całej jednej strony bez inspekcji referencji i GUID-ów.

### Zasady ogólne

- Nie usuwaj ani nie przenoś assetu bez jego `.meta`.
- Preferuj przenoszenie i zmianę nazw przez Unity Editor, jeżeli operacja dotyczy asset database.
- Nie regeneruj `.meta` istniejącego assetu.
- Nie zmieniaj wersji Unity ani pakietów przy okazji innego zadania.
- `Packages/manifest.json`, `Packages/packages-lock.json` oraz `ProjectSettings` zmieniaj tylko wtedy, gdy karta jawnie je wymienia.
- Po zmianie sceny lub prefabu sprawdź brak `Missing Script` i utraconych referencji.
- Tester uruchamia Unity dopiero po zamknięciu instancji używanej przez Developera.

## 8. Niezmienne zasady architektury

Szczegóły i rationale znajdują się w `Docs/GameDesignContract.md`. Każda implementacja MUSI zachować poniższe reguły:

- `ProfileState`, `RunState` i `BoardState` mają rozdzieloną odpowiedzialność.
- Nowy run nie usuwa atlasu ani wiedzy profilu.
- Stan siatki, a nie fizyka ani `Transform`, jest docelowym źródłem prawdy.
- Wejście tworzy komendę; centralny resolver zwraca wynik i zdarzenia; prezentacja tylko je odtwarza.
- Odrzucona komenda nie kosztuje tury, jedzenia ani narzędzia.
- Intent pokazany graczowi jest tym intentem, który zostanie wykonany lub jawnie zablokowany.
- Kod domenowy nie przechowuje `GameObject`, `MonoBehaviour`, `Transform`, prefabów, colliderów ani Unity `InstanceID`.
- Gameplay RNG jest deterministyczny i oddzielony od losowości kosmetycznej.
- Save używa stabilnych tekstowych ID i wersjonowanych DTO, nie bezpośredniej serializacji obiektów Unity.
- Odkryty fakt dokumentuje istniejącą regułę; nie odblokowuje jej działania.
- Losowo znalezione narzędzie nie może być wymagane do obowiązkowego wyjścia.

Jeżeli istniejący kod łamie te reguły, migruj go etapami zgodnie z ticketem. Nie wykonuj big-bang rewrite bez osobnej zgody.

## 9. Testy i weryfikacja

Unity Test Framework `1.6.0` jest już zainstalowany. Nie dodawaj innego frameworka bez ticketu.

Dobierz minimalny wymagany poziom weryfikacji:

| Rodzaj zmiany | Minimalna weryfikacja |
| --- | --- |
| wyłącznie dokumentacja | kontrola struktury, linków i zgodności źródeł prawdy |
| czysta logika domenowa | EditMode tests |
| integracja `MonoBehaviour`, sceny lub UI | EditMode, odpowiednie PlayMode tests i smoke test |
| generator | test deterministyczności, walidator i partia seedów |
| save/profil/run | test round-trip, reset semantics, uszkodzony zapis i katalog tymczasowy |
| prefab/scena | otwarcie w Unity, brak brakujących skryptów/referencji i odpowiedni smoke test |
| input/tura | test „jedna zaakceptowana komenda = jedna tura” oraz odrzuconych komend |

Zasady testów:

- Test nie może pisać do prawdziwego katalogu profilu gracza.
- Testy deterministyczne raportują seed i identyfikator węzła przy błędzie.
- Gameplay i kosmetyka nie korzystają z tego samego strumienia RNG.
- Test PlayMode nie może polegać wyłącznie na czasie animacji, jeżeli może obserwować stan.
- Nie deklaruj testu jako przechodzącego, jeżeli nie został uruchomiony.
- Jeżeli środowisko, licencja lub otwarty Editor blokują test, oznacz wynik jako `Not run` i podaj konkretną przyczynę.

Domyślna instalacja Unity wykryta dla tego środowiska:

```text
C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Unity.exe
```

Nie zakładaj, że ścieżka jest identyczna na innej maszynie. Wersję projektu odczytuj z `ProjectSettings/ProjectVersion.txt`.

## 10. Sekrety, sieć i dane zewnętrzne

- Nie dodawaj sekretów, tokenów, prywatnych kodów ani danych logowania do repozytorium, logów, promptów i raportów.
- Prywatny kod Dreamlo obecny w `ManageRecords.cs` traktuj jako ujawniony. Nie powtarzaj jego wartości. Rotacja/usunięcie wymaga osobnego ticketu i ewentualnej akcji właściciela usługi.
- Nie wysyłaj wyników, kodu ani telemetrii do zewnętrznego serwisu bez jawnej autoryzacji użytkownika.
- Testy leaderboardu używają fake'a albo kontrolowanego endpointu, nigdy produkcyjnego sekretu.
- Lokalna telemetria playtestowa ma być opt-in podczas developmentu i zapisywana poza profilem gracza.

## 11. Definicja ukończenia

Ticket może zostać przyjęty jako `Done` wyłącznie, gdy:

- jego `Rationale` nadal jest realizowane przez rozwiązanie;
- wszystkie kryteria akceptacji mają dowód;
- wymagane testy zostały uruchomione i przeszły albo właściciel jawnie zaakceptował ograniczenie;
- projekt kompiluje się w zakresie objętym zmianą;
- nie ma niezamierzonych zmian w plikach użytkownika;
- nie rozpoczęto pracy z kolejnego ticketu;
- zmiany save'a posiadają wersję/migrację albo jawnie zatwierdzony reset;
- dokumentacja i konfiguracja zostały zaktualizowane, jeżeli zmienił się kontrakt lub publiczne API;
- Developer przekazał handoff, a Reviewer/Coordinator zaakceptował rezultat.

Brak czasu, limit tokenów lub częściowo działający prototyp nie są podstawą do oznaczenia `Done`.

## 12. Wymagany handoff Developera

Użyj formatu:

```text
Ticket:
Rezultat:
Jak rozwiązanie realizuje Rationale:
Zmienione pliki:
Decyzje implementacyjne:
Odstępstwa od karty:
Testy i dokładne wyniki:
Testy niewykonane i powód:
Wpływ na save'y/kompatybilność:
Manualne kroki w Unity:
Znane ryzyka:
```

Handoff nie zawiera implementacji kolejnego zadania. Może wskazać następny ticket wynikający z zależności.

## 13. Wymagany raport Reviewera / Testera

Użyj formatu:

```text
Ticket:
Werdykt: Pass / Rework / Blocked
Kryteria akceptacji:
- AC1: Pass/Fail — dowód
- AC2: Pass/Fail — dowód
Wykonane testy:
Ustalenia:
- [P0/P1/P2/P3] plik:linia — problem, wpływ, reprodukcja
Ryzyka nieweryfikowalne w środowisku:
```

Priorytety:

- `P0` — utrata danych, destrukcyjna operacja, krytyczny problem bezpieczeństwa;
- `P1` — crash, softlock, złamanie kontraktu lub brak możliwości ukończenia;
- `P2` — istotny błąd zachowania albo regresja bez utraty danych;
- `P3` — niewielki problem nieblokujący kryteriów.

Reviewer nie zgłasza uwag wyłącznie stylistycznych, jeżeli nie wpływają na poprawność, czytelność kontraktu albo utrzymanie kodu.

## 14. Aktualna bramka bezpieczeństwa

Do czasu ukończenia pierwszych ticketów higieny obowiązują dodatkowe ograniczenia:

- nie uruchamiaj równoległych writerów;
- nie wykonuj masowej reserializacji bez otwartego Unity i osobnego zakresu;
- nie próbuj automatycznie „sprzątać” całego dirty worktree;
- nie stage'uj wygenerowanych katalogów;
- nie zaczynaj refaktoru rozgrywki przed utrwaleniem baseline'u i uruchomieniem minimalnych testów;
- zachowaj wszystkie zmiany migracyjne użytkownika.

Pierwszym zadaniem implementacyjnym powinien być pierwszy nieukończony ticket higieny repo w `Docs/TechnicalRoadmap.md`.
