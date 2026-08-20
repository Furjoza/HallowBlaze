# HallowBlaze — Technical Roadmap

> Status dokumentu: **Accepted / execution roadmap v0.1**  
> Data: 2026-08-19  
> Właściciel statusów i kolejności: **Coordinator**  
> Kontrakt produktu: [`GameDesignContract.md`](./GameDesignContract.md)  
> Zasady pracy agentów: [`../AGENTS.md`](../AGENTS.md)

## 1. Cel dokumentu

Ten dokument zamienia kontrakt projektowy na kolejność małych, weryfikowalnych zmian technicznych. Każda karta odpowiada nie tylko na pytanie „co zrobić”, lecz także „dlaczego robimy to teraz”, czego celowo nie robić i jak udowodnić poprawność.

### Rationale — dlaczego roadmapa jest osobnym dokumentem

`GameDesignContract.md` opisuje docelowe zachowanie gry, natomiast ta roadmapa opisuje drogę od istniejącego prototypu do tego zachowania. Oddzielenie tych ról zapobiega sytuacji, w której chwilowa kolejność implementacji zaczyna po cichu zmieniać projekt gry.

## 2. Jak korzystać z roadmapy

1. Coordinator wybiera pierwszą niezakończoną kartę, której zależności są spełnione.
2. Przed przydzieleniem zmienia jej status z `Ready` na `Active` i wskazuje jednego writera.
3. Developer realizuje wyłącznie tę kartę zgodnie z `AGENTS.md`.
4. Po handoffie Reviewer/Tester pracuje read-only i wydaje werdykt `Accept`, `Changes requested` albo `Blocked`.
5. Tylko Coordinator zmienia status na `Done` albo zwraca kartę do `Active`.
6. Karta `Planned` musi zostać ponownie sprawdzona i, jeśli potrzeba, doprecyzowana przed zmianą na `Ready`.

Dozwolone statusy:

- `Done` — zaakceptowane i zweryfikowane;
- `Ready` — kompletne, zależności spełnione, można przydzielić;
- `Active` — ma aktualnie jednego writera;
- `Planned` — kolejność jest zaakceptowana, ale zależności nie są jeszcze spełnione;
- `Blocked` — wymaga decyzji, dostępu albo działania właściciela projektu;
- `Deferred` — świadomie poza vertical slice.

W danej chwili może istnieć najwyżej jedna karta `Active`. Równolegle można wykonywać wyłącznie niezależne audyty i review w trybie read-only.

### Rationale — dlaczego tylko jeden aktywny writer

Agenci współdzielą ten sam katalog, a Unity może automatycznie modyfikować assety, pliki `.meta`, `Library` i ustawienia. Jeden writer oraz jawny handoff redukują konflikty, przypadkową reserializację i pomieszanie zmian kilku ticketów.

## 3. Stan wyjściowy i założenia

- Repozytorium Git ma root w `E:\Repos\HallowBlaze`, a projekt Unity w `E:\Repos\HallowBlaze\HallowBlaze`.
- Projekt używa Unity `6000.3.21f1`.
- Unity Test Framework `1.6.0` jest już zadeklarowany, ale nie ma jeszcze assembly definitions ani testów projektu.
- `Visible Meta Files` jest włączone i bieżący audyt nie wykazał brakujących ani osieroconych plików `.meta`.
- `Force Text` nie jest włączone. Dwie sceny, około 30 prefabów, 21 animacji, trzy kontrolery, jeden override controller oraz część `ProjectSettings` są nadal binarne.
- Rootowy `.gitignore` zawiera wzorce zakotwiczone jak dla projektu w root repo. Nie obejmują one poprawnie katalogów takich jak `HallowBlaze/Library` i `HallowBlaze/Temp`.
- Working tree zawiera zmiany związane z migracją do nowej wersji Unity. Wszystkie zastane zmiany są własnością użytkownika i nie wolno ich cofać ani sprzątać automatycznie.
- `Assets/Scripts/ManageRecords.cs` zawiera wartość dostępową Dreamlo. Należy traktować ją jako ujawnioną; nie wolno jej cytować, testować przez sieć ani kopiować.

### 3.1. Operacyjny pre-flight Git

W aktualnym środowisku agentowym Git odmawia odczytu repozytorium jako `unsafe repository` z powodu różnicy właściciela katalogu. Logowanie do Microsoft/GitHub nie zmienia tego mechanizmu systemowego. Przed rozpoczęciem HB-000A writer MUSI wykonać `git status --short` z prawdziwego rootu. Jeżeli Git nadal odmawia:

- agent zatrzymuje ticket i zgłasza dokładny katalog;
- nie ustawia samodzielnie globalnego `safe.directory`;
- właściciel może jawnie zaufać wyłącznie `E:/Repos/HallowBlaze`, nigdy wildcardowi `*`, albo uruchomić zadanie w środowisku o poprawnym właścicielu;
- po odblokowaniu agent zapisuje baseline statusu przed pierwszą zmianą.

To jest pre-flight środowiska, nie zmiana repozytorium ani część kryteriów produktu.

### Rationale — dlaczego higiena poprzedza gameplay

Nowy atlas, resolver tur i save system dotkną wielu plików. Bez poprawnego ignorowania katalogów generowanych, tekstowej serializacji i testów każda kolejna zmiana będzie trudniejsza do review, a konflikty mogą uszkodzić referencje Unity. Te zadania nie są kosmetyką — tworzą warunki do bezpiecznej rozbudowy gry i pracy agentowej.

## 4. Mapa etapów

| Etap | Rezultat | Bramka przejścia |
| --- | --- | --- |
| M0 — Safe Repository | repo jest czytelne dla Git, Unity i agentów; istnieje baseline oraz test runner | brak katalogów generowanych w statusie, tekstowe assety, zielony smoke test |
| M1 — State Foundation | `RunState` i `ProfileState` mają jednego właściciela i bezpieczny zapis | restart sceny i aplikacji nie miesza profilu z runem |
| M2 — Persistent Atlas Prototype | stały graf około pięciu dni zachowuje odkrycia pomiędzy runami | tester używa wiedzy z pierwszej próby w drugiej |
| M3 — Deterministic Tactical Core | komenda ma jeden wynik, jedną turę i jawne intenty | replay tych samych komend daje ten sam hash |
| M4 — Generation, Noise and Enemies | model-first generator jest walidowany, a hałas tworzy decyzje | 10 000 seedów bez softlocka; Listener jest przewidywalny |
| M5 — Tools and Landmark | dwa narzędzia oraz systemowa zagadka tworzą alternatywne trasy | brak losowego softlocka; co najmniej dwa sensowne rezultaty landmarku |
| M6 — Ten-day Vertical Slice | pełna podróż przez dwa biomy ma finał i podsumowanie | gracz rozumie atlas, intenty, narzędzia i chce rozpocząć kolejny run |

Etapy są sekwencyjne jako bramki jakości, ale nie oznaczają masowego refaktoru. Każda karta ma pozostawić projekt w uruchamialnym stanie.

## 5. Reguły wspólne dla wszystkich kart

### 5.1. Domyślny obszar zmian

Dozwolony obszar plików w karcie jest zamkniętą listą. Pliki `.meta` odpowiadające nowym, przeniesionym lub usuniętym assetom są domyślnie częścią tego samego obszaru. `ProjectSettings`, `Packages`, sceny i prefaby są zabronione, jeśli karta nie wymienia ich jawnie.

### 5.2. Save compatibility

Do czasu powstania HB-013 nie ma gwarancji kompatybilności prototypowych `PlayerPrefs`. Od HB-013 każda zmiana DTO musi:

- zwiększyć `schemaVersion`, jeśli zmienia format;
- dostarczyć migrację albo jawnie odrzucić starszy zapis z bezpiecznym komunikatem;
- nigdy nie kasować pliku profilu przed utworzeniem poprawnego backupu.

### 5.3. Testy

- Czyste reguły: EditMode.
- Cykl Unity, sceny, input i prezentacja: PlayMode oraz ręczny smoke test.
- Generator: deterministyczność, osiągalność i testy wielu seedów.
- Regresja: test odtwarzający błąd, gdy jest technicznie rozsądny.

„Projekt się kompiluje” nie oznacza „testy przeszły”. Każdy handoff podaje dokładne wyniki oraz luki w weryfikacji.

### 5.4. Domyślny handoff

Każda karta wymaga handoffu z `AGENTS.md`: ID, rationale rozwiązania, zmienione pliki, potwierdzone non-goals, dokładne testy, ręczne kroki, ryzyka, otwarte decyzje i stan working tree. Karty poniżej dopisują tylko wymagania specyficzne.

### Rationale — dlaczego wspólne reguły nie są kopiowane do każdej karty

Powtarzanie tych samych zasad w kilkudziesięciu miejscach tworzyłoby sprzeczne wersje. Każda karta nadal jawnie podaje własny wpływ na save, testy, obszar plików i dodatkowy handoff; ten rozdział definiuje wyłącznie niezmienny baseline.

---

# M0 — Safe Repository

## HB-000A — Project-local Unity `.gitignore`

**Status:** `Done`  
**Ukończono:** 2026-08-20 — review `Pass`; 17/17 testów pozytywnych i 8/8 negatywnych `git check-ignore`.  
**Priorytet:** P0  
**Powiązany kontrakt:** Appendix A oraz sekcja 22.

**Rationale:** Rootowy `.gitignore` jest o jeden poziom za wysoko i używa zakotwiczonych wzorców, więc Git widzi wygenerowane katalogi zagnieżdżonego projektu Unity. Lokalny plik w katalogu projektu rozwiązuje problem bez modyfikowania plików poza zakresem aktualnego `AGENTS.md` i bez kasowania danych.

**Obecne zachowanie:** `Library`, `Temp`, `Logs`, `obj`, `Build`/`Builds` i podobne katalogi mogą pojawiać się jako untracked mimo istniejącego rootowego `.gitignore`.

**Oczekiwany rezultat:** Git ignoruje artefakty generowane przez Unity, IDE i buildy wewnątrz `HallowBlaze`, ale nadal widzi `Assets`, `Packages`, `ProjectSettings`, `Docs` i źródła.

**Zakres:** Utworzyć projektowy `/.gitignore` z zakotwiczonymi względem projektu regułami dla katalogów i plików generowanych. Zweryfikować reguły poleceniem `git check-ignore`, nie przez tworzenie albo usuwanie katalogów.

**Non-goals:** Nie usuwać istniejących katalogów; nie wykonywać `git clean`, `git rm`, stagingu ani commita; nie zmieniać `../.gitignore`; nie uruchamiać Unity; nie włączać `Force Text`.

**Zależności:** `AGENTS.md` i ten dokument. Brak zależności gameplayowych.

**Dozwolony obszar plików:** `/.gitignore`; status tej karty w `Docs/TechnicalRoadmap.md` może zmienić wyłącznie Coordinator.

**Kryteria akceptacji:**

- Z rootu Git `git check-ignore -v --no-index HallowBlaze/Library/probe.tmp` wskazuje regułę z projektowego `.gitignore`.
- Analogicznie ignorowane są `Temp`, `obj`, `Logs`, `Build`, `Builds`, `UserSettings`, wygenerowane solution/project files i typowe pliki IDE.
- `Assets/Scripts/GameManager.cs`, `Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt`, `Docs/GameDesignContract.md`, `.vscode`, `.vsconfig` oraz pliki `.meta` nie są ignorowane.
- Żaden istniejący plik ani katalog nie został skasowany lub przeniesiony.
- Diff zawiera wyłącznie nowy `.gitignore` i ewentualną zmianę statusu wykonaną przez Coordinatora.

**Plan testów:** Uruchomić `git check-ignore -v --no-index` dla co najmniej sześciu ścieżek pozytywnych i pięciu negatywnych. Następnie sprawdzić `git status --short` z prawdziwego rootu Git i potwierdzić, że zniknęły wyłącznie artefakty objęte regułami.

**Wpływ na save i kompatybilność:** Brak. Lokalnych save'ów nie wolno dodawać do repo; ticket ich nie usuwa.

**Wymagany handoff:** Dołączyć listę sprawdzonych ścieżek, źródło każdej reguły z `git check-ignore` i porównanie statusu przed/po bez ujawniania zawartości generowanych katalogów.

## HB-000B — Audyt baseline po migracji

**Status:** `Done`  
**Ukończono:** 2026-08-20 — review `Pass`; raport baseline zweryfikowany bez ustaleń P0–P3.  
**Priorytet:** P0  
**Powiązany kontrakt:** Appendix A i sekcja 22.

**Rationale:** Zastane ostrzeżenia, binarne assety i zmiany migracyjne muszą być opisane przed kolejnymi modyfikacjami. Inaczej agent może przypisać stary problem nowemu ticketowi albo „naprawić” zmianę użytkownika.

**Obecne zachowanie:** Istnieją cząstkowe obserwacje z audytu, lecz nie ma wersjonowanego baseline'u wskazującego co jest śledzone, binarne, wygenerowane i możliwe do uruchomienia.

**Oczekiwany rezultat:** `Docs/Validation/RepositoryBaseline.md` zawiera datę, wersję Unity, stan pakietów, kategorie binarnych assetów, wynik audytu `.meta`, śledzone katalogi generowane, stan testów i znane ostrzeżenia smoke testu.

**Zakres:** Audyt read-only projektu po HB-000A oraz zapis zredagowanego raportu. Wartości wyglądających na sekrety raportuje się tylko nazwą pliku i kategorią.

**Non-goals:** Bez napraw kodu, konwersji assetów, zmiany pakietów, usuwania plików, publikacji, wywołań Dreamlo ani przepisywania historii.

**Zależności:** HB-000A.

**Dozwolony obszar plików:** `/Docs/Validation/RepositoryBaseline.md` i jego `.meta`, jeśli Unity je utworzy później. Cała reszta tylko do odczytu.

**Kryteria akceptacji:** Raport odróżnia fakty od przypuszczeń, podaje polecenia audytowe, nie zawiera wartości Dreamlo, identyfikuje wszystkie śledzone artefakty generowane i kończy się listą znanych ograniczeń baseline'u.

**Plan testów:** `git status --short`, `git ls-files`, kontrola rozszerzeń/formatów bez otwierania sekretów, audyt par asset–`.meta`, odczyt `ProjectVersion.txt` i `Packages/manifest.json`; opcjonalny ręczny smoke w Unity tylko po potwierdzeniu, że Editor użytkownika jest zamknięty.

**Wpływ na save i kompatybilność:** Brak; raport nie może zawierać lokalnych save'ów ani danych gracza.

**Wymagany handoff:** Podać, które wyniki były możliwe do potwierdzenia, a które blokował stan Git/Unity. Każdy nowy problem otrzymuje proponowane ID follow-upu, ale nie jest naprawiany w tym tickecie.

## HB-000C — Usunięcie artefaktów generowanych wyłącznie z indeksu

**Status:** `Done`  
**Ukończono:** 2026-08-20 — review `Pass`; 173 artefakty usunięte tylko z indeksu, lokalny manifest zachowany.  
**Decyzja właściciela:** 2026-08-20 — nie tworzyć osobnego archiwum; priorytetem jest minimalny bieżący indeks Git. Build może pozostać lokalnie i w istniejącej historii.  
**Priorytet:** P0  
**Powiązany kontrakt:** Appendix A i sekcja 22.

**Rationale:** `.gitignore` nie działa na pliki już śledzone. Audyt indeksu wykazał około 168 plików pod `Builds/` i pięć plików pod `.vs/`; ich kolejne zmiany nadal zasłaniałyby diff i zwiększały repo.

**Obecne zachowanie:** Buildy i dane Visual Studio są śledzone. Lokalne katalogi zawierają około 176 MiB i 5 MiB danych, a historia Git zachowuje ich starsze wersje.

**Oczekiwany rezultat:** `Builds/` i `.vs/` nie są już w bieżącym indeksie, pozostają lokalnie na dysku i nie wracają jako untracked dzięki HB-000A.

**Zakres:** Po zapisaniu dokładnej listy wykonać wyłącznie kontrolowane `git rm -r --cached --` dla jawnych ścieżek `HallowBlaze/Builds` i `HallowBlaze/.vs` z rootu Git. Zweryfikować istnienie lokalnych katalogów przed i po.

**Non-goals:** Bez fizycznego kasowania, `git clean`, przepisywania historii, usuwania innych tracked files, stagingu całego repo, publikowania buildów lub tworzenia release'u.

**Zależności:** HB-000A i HB-000B; jawna decyzja właściciela dotycząca ewentualnego archiwum starych buildów.

**Dozwolony obszar plików:** Wyłącznie wpisy indeksu pod `/Builds/**` i `/.vs/**`; żaden plik roboczy nie może zostać usunięty.

**Kryteria akceptacji:** `git ls-files HallowBlaze/Builds HallowBlaze/.vs` nie zwraca wpisów; oba katalogi nadal istnieją lokalnie; `git status` pokazuje wyłącznie oczekiwane usunięcia z indeksu i inne zastane zmiany; zawartość nie pojawia się ponownie jako `??`.

**Plan testów:** Porównać liczbę i rozmiar lokalnych plików przed/po; wykonać `git ls-files`, `git status --short`, `git check-ignore -v --no-index` i przejrzeć dokładny staged diff. Nie uruchamiać Unity.

**Wpływ na save i kompatybilność:** Brak. Stare buildy mogą zawierać ujawnioną wartość Dreamlo, dlatego nie powinny być ponownie rozpowszechniane.

**Wymagany handoff:** Liczba usuniętych wpisów z każdej ścieżki, potwierdzenie zachowania lokalnych plików i jawne stwierdzenie, że historia Git nie została zmieniona.

### Checkpoint właściciela po HB-000C

Po review HB-000A–C zalecany jest osobny baseline commit obejmujący właściwe źródła projektu, `Assets` z `.meta`, `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings`, przenośne ustawienia `.vscode`, `AGENTS.md` i `Docs`. Agent nie tworzy commita ani nie wykonuje bulk stagingu bez jawnego polecenia użytkownika. Reserializacja musi pozostać osobną zmianą.

### Rationale — dlaczego checkpoint jest przed reserializacją

Pozwala rozróżnić faktyczną migrację projektu i porządki indeksu od mechanicznej konwersji binarnych assetów. Jeśli po reserializacji coś przestanie działać, istnieje mały, znany punkt odniesienia.

## HB-000D — Włączenie `Force Text`

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** Appendix A oraz sekcja 21.

**Rationale:** Tekstowy YAML umożliwia sensowny diff, review i scalanie assetów Unity. Samo ustawienie trybu należy oddzielić od masowej reserializacji, aby łatwo wykryć nieoczekiwane skutki.

**Obecne zachowanie:** `Visible Meta Files` jest już aktywne, ale asset serialization nie używa `Force Text`; część `ProjectSettings` jest binarna.

**Oczekiwany rezultat:** Projekt zapisuje nowe i modyfikowane assety jako tekst, a tryb wersjonowania `.meta` pozostaje widoczny.

**Zakres:** W Unity `6000.3.21f1` ustawić Asset Serialization Mode na `Force Text`, zweryfikować Version Control Mode i zapisać ustawienia. Zakończyć Editor przed diffem.

**Non-goals:** Bez `Force Reserialize Assets`, gameplayu, aktualizacji Unity/pakietów, ręcznej edycji binarnych `ProjectSettings` ani czyszczenia `Library`.

**Zależności:** HB-000C i zaakceptowany checkpoint baseline; Unity Editor użytkownika musi być zamknięty przed przejęciem lease.

**Dozwolony obszar plików:** Tylko ustawienia Unity faktycznie zmienione przez tę opcję, przede wszystkim `/ProjectSettings/EditorSettings.asset`; odpowiadające `.meta`, jeśli istnieją.

**Kryteria akceptacji:** Unity pokazuje `Force Text` i `Visible Meta Files`; zapisany plik ustawienia jest możliwy do odczytu/diffu; po ponownym otwarciu opcje pozostają aktywne; diff nie zawiera scen, prefabów ani kodu.

**Plan testów:** Ponowne otwarcie projektu dokładnie w `6000.3.21f1`, odczyt opcji w Editorze, kontrola Console i diffu po zamknięciu.

**Wpływ na save i kompatybilność:** Brak wpływu na runtime save. Zmienia wyłącznie format przyszłej serializacji assetów.

**Wymagany handoff:** Screenshot albo precyzyjny zapis ustawień, lista automatycznie zmienionych plików, ostrzeżenia Console i potwierdzenie wersji Unity.

## HB-000E — Izolowana reserializacja assetów Unity

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** Appendix A, sekcje 21 i 22.

**Rationale:** Włączenie `Force Text` nie konwertuje automatycznie całego istniejącego projektu. Jednorazowa, izolowana reserializacja usuwa binarną barierę dla późniejszych zmian scen/prefabów, a oddzielny diff pozwala sprawdzić zachowanie GUID-ów.

**Obecne zachowanie:** Co najmniej 57 scen/prefabów/animacji/kontrolerów oraz część ustawień pozostaje binarna.

**Oczekiwany rezultat:** Wspierane assety i ustawienia są zapisane jako Unity YAML bez zmiany zachowania gry i referencji.

**Zakres:** W Unity `6000.3.21f1` wykonać kontrolowaną reserializację śledzonych assetów. Przed i po sporządzić listę plików oraz zweryfikować nagłówki YAML i GUID-y `.meta`.

**Non-goals:** Bez refaktoru, poprawiania prefabów, zmian gameplayu, zmiany nazw/położenia assetów, aktualizacji pakietów lub normalizacji całego repo zewnętrznym formatterem.

**Zależności:** HB-000D; osobny writer lease; zamknięte wszystkie inne instancje Unity.

**Dozwolony obszar plików:** Istniejące śledzone pliki Unity pod `/Assets` i `/ProjectSettings`, które sam Unity reserializuje. Kod `.cs`, `Packages` i dokumentacja są zabronione.

**Kryteria akceptacji:** Docelowe pliki mają poprawny nagłówek Unity YAML; liczba i wartości GUID w `.meta` nie zmieniły się bez uzasadnienia; wszystkie sceny otwierają się; prefab references nie zgłaszają `Missing`; projekt kompiluje się; menu i obecna plansza przechodzą smoke test.

**Plan testów:** Skryptowy audyt sygnatur plików przed/po, przegląd pełnego diffu, otwarcie obu scen, Console bez nowych błędów, ręczne uruchomienie menu → gra → przejście poziomu → game over.

**Wpływ na save i kompatybilność:** Brak zamierzonego wpływu. Każda zmiana wartości serializowanych jest błędem lub musi zostać osobno wyjaśniona.

**Wymagany handoff:** Podać liczbę plików w każdej kategorii, wynik kontroli GUID i pełny smoke test. Nie mieszać handoffu z żadną poprawką gameplayową.

## HB-000F — Minimalna infrastruktura testowa

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcja 22.

**Rationale:** Kolejne refaktory stanu i resolvera wymagają szybkiej informacji zwrotnej. Sam zainstalowany pakiet Test Framework nie daje uruchamialnych zestawów ani ustalonej struktury.

**Obecne zachowanie:** Pakiet `com.unity.test-framework` jest dostępny, ale projekt nie zawiera test assemblies ani testów.

**Oczekiwany rezultat:** Istnieją osobne katalogi i asmdefy EditMode/PlayMode oraz minimalne testy infrastruktury wykonywane lokalnie i w trybie batch.

**Zakres:** Utworzyć `/Assets/Tests/EditMode` i `/Assets/Tests/PlayMode` wraz z asmdefami, `.meta`, prostym smoke testem każdej warstwy oraz udokumentowanymi poleceniami uruchomienia.

**Non-goals:** Bez przenoszenia istniejących skryptów do nowych assembly, testowania przyszłych mechanik, CI w chmurze lub aktualizacji pakietów.

**Zależności:** HB-000E.

**Dozwolony obszar plików:** `/Assets/Tests/**`, `/Docs/Testing.md`, odpowiadające `.meta`.

**Kryteria akceptacji:** Test Runner wykrywa oba zestawy; każdy ma co najmniej jeden przechodzący test infrastruktury; kompilacja gracza nie zawiera assembly testowych; `Docs/Testing.md` podaje wersję Unity, komendy, ścieżki wyników i zasadę jednej instancji.

**Plan testów:** Uruchomić osobno EditMode i PlayMode w Unity `6000.3.21f1`; zapisać passed/failed/skipped oraz sprawdzić kod wyjścia batch mode.

**Wpływ na save i kompatybilność:** Brak.

**Wymagany handoff:** Dokładne polecenia oraz wyniki obu zestawów; wskazać wszystkie pliki wygenerowane przez uruchomienie i potwierdzić, że są ignorowane.

## HB-000G — Regresja pojedynczej akcji ruchu

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 9, 10, 21.2 i Appendix A.

**Rationale:** `PlayerScript.AttemptMove()` wywołuje obecnie ścieżkę ruchu, a następnie próbuje wykonać `Move` ponownie. Rozbudowywanie zasobów i tur na tej podstawie utrwaliłoby podwójny koszt albo podwójny skutek wejścia.

**Obecne zachowanie:** Jedno wejście może przejść przez dwie ścieżki rozstrzygnięcia ruchu.

**Oczekiwany rezultat:** Jedno zaakceptowane wejście gracza powoduje dokładnie jedną próbę ruchu, jeden koszt i najwyżej jedną zmianę pola.

**Zakres:** Dodać możliwie mały test regresji, usunąć drugie rozstrzygnięcie i zachować istniejące animacje oraz blokowanie ruchu.

**Non-goals:** Bez pełnego `TurnResolver`, przebudowy AI, zmiany balansu jedzenia lub przejęcia stanu przez `RunState`.

**Zależności:** HB-000F.

**Dozwolony obszar plików:** `/Assets/Scripts/PlayerScript.cs`, niezbędny test pod `/Assets/Tests/**` i odpowiadające `.meta`.

**Kryteria akceptacji:** Test wykazuje jedną próbę na jedno wejście; koszt zasobu nalicza się raz; zablokowana próba nie przesuwa postaci; obecne przejście planszy nadal działa.

**Plan testów:** Nowy test regresji, pełne EditMode/PlayMode oraz ręczny ruch w wolne pole, ścianę, przeszkodę i wyjście.

**Wpływ na save i kompatybilność:** Brak formatu save; chwilowa dynamika rozgrywki może się poprawić, bo znika niezamierzony drugi skutek.

**Wymagany handoff:** Pokazać reprodukcję przed i wynik po bez cytowania dużego diffu; jawnie potwierdzić, że nie rozpoczęto docelowego resolvera.

## HB-000H — Kwarantanna ujawnionej integracji Dreamlo

**Status:** `Blocked` — wymaga decyzji O-005 i rotacji po stronie właściciela  
**Priorytet:** P0 przed publiczną dystrybucją; nie blokuje lokalnego developmentu offline  
**Powiązany kontrakt:** O-005, sekcja 21.5 i Appendix A.

**Rationale:** Uprzywilejowana wartość osadzona w kliencie Unity jest możliwa do odczytania, a usunięcie jej z bieżącego pliku nie unieważnia wartości obecnej w historii. Bezpieczny vertical slice nie powinien wysyłać wyników z klienta przy użyciu prywatnego kodu.

**Obecne zachowanie:** `ManageRecords.cs` zawiera wartość Dreamlo i może wykonywać operacje sieciowe z klienta.

**Oczekiwany rezultat:** Stara wartość jest obrócona/unieważniona poza repo; klient nie zawiera sekretu i bezpiecznie działa bez leaderboardu albo korzysta w przyszłości z osobnego backendu.

**Zakres:** Po decyzji właściciela usunąć/wyłączyć ścieżkę uprzywilejowanego uploadu, zapewnić łagodne zachowanie UI offline i udokumentować zewnętrzne potwierdzenie rotacji bez zapisywania nowej wartości.

**Non-goals:** Bez przepisywania historii Git, tworzenia backendu, testowych wywołań produkcyjnego Dreamlo, nowego leaderboardu lub samodzielnego logowania się przez agenta.

**Zależności:** Decyzja O-005; właściciel wykonuje rotację. Kodowa część wymaga HB-000F, jeśli zmienia zachowanie UI/runtime.

**Dozwolony obszar plików:** `/Assets/Scripts/ManageRecords.cs`, jawnie wskazane testy i — tylko jeśli potrzebne — scena/prefab zawierający ten komponent.

**Kryteria akceptacji:** Brak prywatnej wartości w śledzonym working tree; brak nieautoryzowanego uploadu; gra działa offline; właściciel potwierdza unieważnienie starej wartości; skan raportuje wyłącznie nazwy plików, nie wartości.

**Plan testów:** Test zachowania offline bez prawdziwego requestu, ręczny smoke menu/game over, zredagowany skan bieżącego drzewa. Historia jest raportowana jako osobne ryzyko, nie modyfikowana.

**Wpływ na save i kompatybilność:** Lokalne rekordy można zachować; zewnętrzne wyniki mogą stać się niedostępne zgodnie z decyzją O-005.

**Wymagany handoff:** Bez sekretów. Podać tylko status rotacji, usunięte punkty użycia, zachowanie offline i ryzyko historii Git.

### Bramka M0

M0 jest zaliczony, gdy HB-000A–G mają status `Done`, nie ma nieopisanych artefaktów generowanych ani binarnych assetów wymaganych do dalszej pracy, a EditMode i PlayMode smoke są zielone. HB-000H może pozostać `Blocked` wyłącznie dla lokalnego developmentu; blokuje każdy publiczny build lub udostępnienie repo.

### Rationale — dlaczego bramka jest twarda

Po M0 zaczynają się zmiany własności stanu i zapisu. Ich review nie może być zasłonięte tysiącami plików generowanych, migracją YAML albo znaną podwójną akcją.

---

# M1 — State Foundation

## HB-010 — Autorytatywny `RunState`

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 6.1, 6.2, 21.1–21.3 i przykład z sekcji 23.

**Rationale:** Dane wyprawy są obecnie kopiowane pomiędzy `GameManager` i prefab gracza. Jeden czysty właściciel zapobiega resetom sceny, współdzieleniu danych i rozbieżnym kosztom akcji.

**Obecne zachowanie:** Zdrowie i jedzenie istnieją w kilku komponentach, a `OnDisable` uczestniczy w zachowaniu danych.

**Oczekiwany rezultat:** Czysty C# `RunState` posiada zasoby, bieżący etap, seed, wyposażenie i wynik wyprawy; można go utworzyć, zmienić i zresetować bez sceny.

**Zakres:** Nowa assembly domenowa, wartości i inwarianty `RunState`, jawne metody mutacji oraz testy. Pola przyszłych systemów mogą mieć stabilne typy/placeholdery, ale bez ich mechanik.

**Non-goals:** Bez JSON-a, atlasu, generatora, pełnego snapshotu planszy, UI lub przepisywania wszystkich komponentów.

**Zależności:** Bramka M0.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/State/**`, odpowiedni asmdef, `/Assets/Tests/EditMode/**` i `.meta`.

**Kryteria akceptacji:** Nowy run ma poprawne wartości początkowe z konfiguracji; mutacje respektują inwarianty; `Dead`/`Won` są jawne; dwa runy nie współdzielą kolekcji; kod domenowy nie zależy od `UnityEngine`.

**Plan testów:** EditMode dla tworzenia, mutacji, granic, resetu, kopiowania kolekcji i końca runu; pełny dotychczasowy zestaw regresji.

**Wpływ na save i kompatybilność:** Brak trwałego zapisu w tym tickecie. Struktura staje się źródłem przyszłego DTO, ale nie jest nim bezpośrednio.

**Wymagany handoff:** Opisać inwarianty i uzasadnić każde pole. Wskazać, które obecne komponenty nadal przechowują kopie do czasu HB-012.

## HB-011 — Autorytatywny `ProfileState`

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 4, 5, 6.1–6.3 i 21.3.

**Rationale:** Atlas i wiedza mają przetrwać śmierć, lecz nie mogą wyciekać do stanu pojedynczego runu. Osobny profil jest technicznym odpowiednikiem obietnicy „wiedza gracza jest progresją”.

**Obecne zachowanie:** Trwałość ogranicza się głównie do wyników/`PlayerPrefs`; nie istnieje model profilu ani rozróżnienie odkryć od bieżącej trasy.

**Oczekiwany rezultat:** Czysty `ProfileState` przechowuje stabilne ID odkrytych miejsc, dróg i faktów oraz statystyki potrzebne wyłącznie profilowi.

**Zakres:** Typy profilu, monotoniczne operacje odkrywania, deduplikacja i testy niezależności od `RunState`.

**Non-goals:** Bez UI atlasu, zapisu plikowego, procentu ukończenia, bonusów statystyk, odkrywania ukrytej topologii ani rozstrzygnięcia fabularnego O-001.

**Zależności:** HB-010.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/State/**`, `/Assets/Tests/EditMode/**` i `.meta`.

**Kryteria akceptacji:** Ponowne odkrycie jest idempotentne; nowy run nie czyści profilu; nowy profil jest pusty; kolekcje nie są publicznie modyfikowalne; brak zależności od Unity.

**Plan testów:** EditMode dla wszystkich przejść discovery, duplikatów, serializowalnych stabilnych ID i izolacji dwóch profili/runów.

**Wpływ na save i kompatybilność:** Definiuje przyszłe dane trwałe, ale jeszcze ich nie zapisuje. Nie importuje automatycznie obecnych rekordów.

**Wymagany handoff:** Macierz „żyje w Profile / Run / późniejszym BoardState” oraz lista świadomie pominiętych danych.

## HB-012 — `GameSession` i migracja własności stanu

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 6, 21.2 i 21.5.

**Rationale:** Same klasy stanu nie usuną błędu, jeśli `GameManager` i `PlayerScript` nadal są równoległymi właścicielami. Sesja ma zapewnić jedno miejsce składania profilu i runu, a widoki mają wyłącznie obserwować lub wysyłać intencje.

**Obecne zachowanie:** `GameManager` łączy stan, tury, sceny, UI i listę przeciwników; `PlayerScript.OnDisable` kopiuje dane.

**Oczekiwany rezultat:** `GameSession` posiada dokładnie jeden `ProfileState` i opcjonalny `RunState`; `GameManager` jest przejściową fasadą/composition root, a gracz nie zapisuje stanu przy wyłączeniu.

**Zakres:** Wprowadzić sesję, przepiąć zdrowie/jedzenie/dzień na pojedyncze źródło, dodać zdarzenia/odczyt dla UI i usunąć kopiowanie w cyklu życia.

**Non-goals:** Bez docelowego resolvera tur, atlas UI, JSON-a, nowego menu czy zmiany balansu.

**Zależności:** HB-010 i HB-011.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Session/**`, `GameManager.cs`, `PlayerScript.cs`, bezpośrednie adaptery UI, testy i jawnie wymagane prefaby/sceny dopiero po zatwierdzeniu przez Coordinatora.

**Kryteria akceptacji:** Zmiana sceny zachowuje ten sam run; wyłączenie prefabu nie zmienia zasobów; UI pokazuje dane sesji; nowy run tworzy nową instancję; brak dwóch zapisywalnych kopii health/food/day.

**Plan testów:** EditMode sesji; PlayMode z przeładowaniem sceny i odtworzeniem prefabu; ręczny menu → poziom → następny poziom → game over.

**Wpływ na save i kompatybilność:** Obecne `PlayerPrefs` nie są jeszcze migrowane; w handoffie należy jawnie opisać tymczasowe zachowanie rekordów.

**Wymagany handoff:** Diagram własności przed/po, lista usuniętych kopii oraz dowód przejścia cyklu sceny.

## HB-013 — Wersjonowane DTO i mapowanie stanu

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 6.3 i 21.3–21.4.

**Rationale:** Atlas będzie najcenniejszym artefaktem gracza, a serializowanie bezpośrednio klas domenowych związałoby rozwój reguł z formatem pliku. Osobne DTO i mapowanie tworzą kontrolowaną granicę wersjonowania przed dodaniem operacji dyskowych.

**Obecne zachowanie:** Brak jawnych DTO profilu/runu, wersjonowania i mapowania; bieżące modele są obiektami runtime.

**Oczekiwany rezultat:** Osobne DTO profilu i runu mają `schemaVersion`, jawne mapowanie do/z domeny oraz walidację wymaganych pól. Round-trip nie traci informacji.

**Zakres:** DTO schema v1, mappery, walidacja danych i serializacja/deserializacja do tekstu przez istniejące możliwości platformy. Test używa pamięci lub katalogu tymczasowego, ale nie implementuje jeszcze produkcyjnego filesystem store.

**Non-goals:** Bez produkcyjnego zapisu plikowego, atomowej zamiany, backupu, chmury, szyfrowania, leaderboardu, pełnego mid-board snapshotu lub importu dowolnych starych wersji prototypu.

**Zależności:** HB-012.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Persistence/Dto/**`, `/Mapping/**`, testy; `Packages` i sceny zabronione.

**Kryteria akceptacji:** Round-trip zachowuje profil i run; brak/duplikat wymaganych ID daje kontrolowany błąd; nieznana przyszła wersja nie jest interpretowana jako v1; DTO nie zawiera Unity object references; log nie wypisuje pełnych danych profilu.

**Plan testów:** EditMode dla round-trip, minimalnych/maksymalnych danych, błędnych ID, brakujących pól i przyszłej wersji. Testy serializacji nie dotykają realnego profilu użytkownika.

**Wpływ na save i kompatybilność:** Powstaje logiczna schema v1. Prototypowe `PlayerPrefs` pozostają nietknięte, chyba że Coordinator zatwierdzi osobną prostą migrację.

**Wymagany handoff:** Przykład zredagowanego DTO, tabela pole domeny ↔ pole DTO i błędów walidacji.

## HB-014 — Atomowy file store, backup i recovery

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 6.3 i 21.4.

**Rationale:** Poprawny format nie wystarcza, jeśli przerwany zapis może skasować atlas. Oddzielny ticket filesystemu pozwala wstrzykiwać awarie i reviewować operacje I/O bez równoczesnej zmiany lifecycle gry.

**Obecne zachowanie:** DTO v1 działa w pamięci/testach, lecz gra nie ma produkcyjnego repozytorium, backupu ani recovery.

**Oczekiwany rezultat:** Osobne repozytoria profilu i runu zapisują pod `persistentDataPath` przez plik tymczasowy, kontrolowaną zamianę oraz ostatni poprawny backup; load ma typowane wyniki.

**Zakres:** `ISaveStore`, adapter ścieżki Unity, implementacja filesystemu, flush/replace właściwe dla platformy referencyjnej, backup, recovery i migracja testowa v0→v1.

**Non-goals:** Bez menu/lifecycle, chmury, background sync, szyfrowania, mid-board snapshotu i pisania do prawdziwych danych użytkownika w testach.

**Zależności:** HB-013 i rozstrzygnięta O-004 dla gwarancji platformowych filesystemu.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Persistence/Storage/**`, adapter Unity persistence path, tests i dokumentacja formatu; `Packages` zabronione.

**Kryteria akceptacji:** Przerwany zapis nie niszczy ostatniej poprawnej wersji; uszkodzony aktywny plik próbuje backupu; profil i run są osobnymi plikami; brak pliku jest normalnym typowanym wynikiem; przyszła schema nie jest nadpisywana.

**Plan testów:** EditMode na unikalnym katalogu tymczasowym dla success, missing, corrupt, interrupted replace, backup, permissions/error i migration; PlayMode ścieżki `persistentDataPath` z nazwą testową, nigdy realnym profilem.

**Wpływ na save i kompatybilność:** Materializuje schema v1 na dysku. Każda migracja działa na kopii/backupie i jest testowana przed zastąpieniem aktywnego pliku.

**Wymagany handoff:** Diagram plik temp/current/backup, tabela błędów/reakcji, lokalizacje testowe i potwierdzenie cleanupu wyłącznie własnego katalogu tymczasowego.

## HB-015 — Cykl `New Run`, `Continue`, `Dead`, `Won`

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 6.2, 19 i O-001.

**Rationale:** Oddzielne modele i zapis muszą zostać spięte jawną maszyną stanów. Inaczej śmierć może skasować profil albo `Continue` wznowić nieprawidłowy moment.

**Obecne zachowanie:** Gra posiada głównie game over i rosnący licznik dni; nie ma jawnego zwycięstwa ani rozróżnionych operacji sesji.

**Oczekiwany rezultat:** Sesja obsługuje tworzenie runu, wznowienie między planszami, zakończenie `Dead`/`Won` oraz powrót do profilu bez utraty odkryć.

**Zakres:** Jawne przejścia lifecycle, autosave na granicach plansz, bezpieczne menu actions i komunikaty dla braku/uszkodzonego run save.

**Non-goals:** Bez pełnego finału fabularnego, mid-board save, atlasowej grafiki i ostatecznej narracji tożsamości postaci.

**Zależności:** HB-014; O-001 może pozostać nierozstrzygnięte tylko na poziomie tekstu, nie semantyki wspólnego profilu.

**Dozwolony obszar plików:** Sesja/persistence, adapter scen/menu, jawnie wskazane sceny/prefaby UI i testy.

**Kryteria akceptacji:** `Continue` istnieje tylko dla aktywnego poprawnego runu; death/win zamykają run save po zapisaniu profilu; crash pomiędzy planszami wraca do ostatniej zatwierdzonej granicy; nowy run nie czyści profilu.

**Plan testów:** EditMode tabela wszystkich dozwolonych/zabronionych przejść; PlayMode restart aplikacji na każdej granicy; ręczne testy przycisków i błędnego save.

**Wpływ na save i kompatybilność:** Używa schema v1; każda zmiana musi przejść przez repozytorium i backup.

**Wymagany handoff:** Tabela stanów/przejść oraz lista dokładnych momentów autosave.

## HB-016 — Test kontraktu własności i trwałości

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 6, 19 i 22.

**Rationale:** M1 jest refaktorem ryzykownym, bo błędy ujawniają się dopiero po zmianie sceny, śmierci lub restarcie procesu. Jedna macierz integracyjna chroni przed regresją w kolejnych etapach.

**Obecne zachowanie:** Poszczególne testy ticketów nie tworzą jeszcze pełnego dowodu, że profil i run zachowują się odmiennie przez cały lifecycle.

**Oczekiwany rezultat:** Automatyczna macierz potwierdza własność danych, granice zapisu i izolację kolejnych runów.

**Zakres:** Testy scenariuszy: nowy profil, nowy run, przejście planszy, restart, death, kolejny run, win, uszkodzony save i dwa profile w izolacji.

**Non-goals:** Bez nowego gameplayu, balansu, UI atlasu lub optymalizacji.

**Zależności:** HB-010–015.

**Dozwolony obszar plików:** `/Assets/Tests/**`, fixtures/fakes persistence i `/Docs/Validation/HB-016.md`.

**Kryteria akceptacji:** Wszystkie przejścia z kontraktu mają test pozytywny i istotne przejścia zabronione test negatywny; testy nie dotykają prawdziwych save'ów; kolejność uruchomienia nie wpływa na wynik.

**Plan testów:** Pełne EditMode i PlayMode uruchomione dwukrotnie w innej kolejności; ręczny smoke na kopii testowego profilu.

**Wpływ na save i kompatybilność:** Nie zmienia schematu; fixtures muszą jawnie podawać wersję.

**Wymagany handoff:** Macierz scenariusz → test → wynik oraz wszystkie niepokryte zachowania.

### Bramka M1

M1 jest zaliczony, gdy istnieje dokładnie jeden właściciel profilu i runu, przejścia lifecycle są jawne, save v1 przechodzi test uszkodzenia/backup, a restart sceny i aplikacji nie przenosi danych do niewłaściwej warstwy.

---

# M2 — Persistent Atlas Prototype

## HB-020 — Stabilne ID i definicja pięciodniowego grafu

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 4, 5.1, 5.3, 7.1 i 20.

**Rationale:** Wiedza może być trwała tylko wtedy, gdy opisuje ten sam świat pomiędzy runami. Stały, mały graf pozwala sprawdzić obietnicę atlasu przed produkcją dziesięciu lub czterdziestu dni contentu.

**Obecne zachowanie:** Poziomy są sekwencyjne i nie mają stabilnych węzłów, krawędzi ani alternatywnych tras.

**Oczekiwany rezultat:** Data-driven graf około pięciu dni zawiera stabilne `NodeId`/`EdgeId`, start, co najmniej dwie decyzje trasy, złączenie oraz placeholder landmarku.

**Zakres:** Czyste definicje grafu i jeden ręcznie utworzony asset/fixture prototypu. ID są tekstowe, nie wynikają z indeksu listy.

**Non-goals:** Bez losowego makrografu, UI mapy, finalnej narracji, biome artu i generacji lokalnych plansz.

**Zależności:** Bramka M1.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/World/**`, `/Assets/GameData/World/**`, EditMode tests i `.meta`.

**Kryteria akceptacji:** Każdy node/edge ma unikalne stabilne ID; graf ma start i osiągalny cel prototypu; zmiana kolejności assetów nie zmienia tożsamości; model nie zależy od scen.

**Plan testów:** EditMode dla unikalności, lookupu ID i oczekiwanych tras; snapshot jawnej topologii fixture.

**Wpływ na save i kompatybilność:** Profil może od tej chwili zapisywać te ID. Zmiana opublikowanego ID będzie wymagała migracji albo aliasu.

**Wymagany handoff:** Czytelny diagram grafu oraz rejestr stabilnych ID.

## HB-021 — Walidator makrografu

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 5.3, 7.1 i 16.

**Rationale:** Błędna krawędź może zablokować cały run albo pozostawić odkrycie bez celu. Walidator przenosi te problemy z playtestu do szybkiego testu danych.

**Obecne zachowanie:** Definicja z HB-020 nie ma jeszcze kompletnego automatycznego sprawdzania.

**Oczekiwany rezultat:** Walidator raportuje duplikaty, brakujące referencje, brak startu/celu, nieosiągalne wymagane węzły, martwe końce bez oznaczenia i niewłaściwy dzień landmarku.

**Zakres:** Czysty walidator z wieloma komunikatami w jednym przebiegu i testami uszkodzonych fixture'ów.

**Non-goals:** Bez oceny jakości narracji, balansu lokalnej planszy i automatycznego poprawiania grafu.

**Zależności:** HB-020.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/World/Validation/**`, testy i fixtures.

**Kryteria akceptacji:** Poprawny graf przechodzi; każda wymieniona klasa błędu ma osobny test; komunikat zawiera stabilne ID i przyczynę; walidator nie mutuje danych.

**Plan testów:** EditMode parametryzowany dla poprawnego i celowo uszkodzonych grafów.

**Wpływ na save i kompatybilność:** Brak zmiany formatu; zapobiega zapisaniu odkryć wskazujących nieistniejący content.

**Wymagany handoff:** Tabela reguła → fixture → oczekiwany komunikat.

## HB-022 — Model odkryć atlasu

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 5.2, 5.4 i 6.1.

**Rationale:** `Unknown`, `Rumored`, `Sighted` i `Visited` niosą różną ilość informacji. Jeden boolean „odkryte” ujawniłby za dużo albo nie potrafił pokazać postępu.

**Obecne zachowanie:** `ProfileState` zna stabilne ID, ale nie ma jeszcze pełnych, monotonicznych przejść discovery dla węzłów i dróg.

**Oczekiwany rezultat:** Profil obsługuje stany węzła `Unknown → Rumored → Sighted → Visited` i drogi `Unknown → Sighted → Traversed`, bez cofania wiedzy.

**Zakres:** Typy, reguły przejść, jawne operacje odkrywania oraz wynik opisujący czy profil faktycznie się zmienił.

**Non-goals:** Bez procentu mapy, fog-of-war lokalnej planszy, UI, fałszywych wskazówek lub automatycznego odsłaniania sąsiadów poza regułą zadania.

**Zależności:** HB-020 i HB-011.

**Dozwolony obszar plików:** Core World/State, persistence mapping jeśli schema tego wymaga, testy.

**Kryteria akceptacji:** Przejścia są monotoniczne i idempotentne; niewłaściwe ID daje kontrolowany błąd; stan trasy runu nie jest zapisywany jako wiedza profilu bez zdarzenia discovery.

**Plan testów:** Pełna tabela przejść w EditMode, round-trip przez schema save i test dwóch runów na jednym profilu.

**Wpływ na save i kompatybilność:** Jeśli schema v1 nie przewidziała stanów, powstaje migracja v1→v2 z jednoznacznym mapowaniem istniejących ID.

**Wymagany handoff:** Macierz przejść i przykład migracji; wskazać każde miejsce, które emituje discovery.

## HB-023 — `WorldMapService` i zdarzenia odkryć

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 5, 7.1 i 18.

**Rationale:** UI, save i przejście poziomu nie powinny niezależnie decydować, co gracz odkrył. Jedna usługa łączy definicję świata, profil i aktualną pozycję runu.

**Obecne zachowanie:** Modele istnieją osobno; brak autorytatywnego API do wejścia w węzeł, zobaczenia drogi i pobrania legalnych następnych wyborów.

**Oczekiwany rezultat:** Usługa zwraca widok mapy ograniczony wiedzą gracza, legalne wyjścia i domenowe zdarzenia discovery, które wyzwalają zapis profilu.

**Zakres:** Query/commands serwisu, reguła ujawniania informacji i integracja z `GameSession` bez grafiki.

**Non-goals:** Bez renderowania, animacji mapy, lokalnej generacji planszy i heurystyki rekomendowania najlepszej trasy.

**Zależności:** HB-021 i HB-022.

**Dozwolony obszar plików:** Core World/Session, persistence orchestration i testy.

**Kryteria akceptacji:** Nieznane węzły nie wyciekają przez query; wszystkie aktualnie dostępne wybory są jednoznaczne; wejście/traversal emituje dokładnie jedno zdarzenie; powtórzenie nie tworzy duplikatu zapisu.

**Plan testów:** EditMode scenariuszy pierwszego i drugiego runu, ukrywania informacji i illegal route; integration test z fake repository.

**Wpływ na save i kompatybilność:** Korzysta z discovery schema HB-022 i zapisuje profil po rzeczywistej zmianie.

**Wymagany handoff:** Przykładowe odpowiedzi query dla kolejnych stanów wiedzy oraz lista momentów autosave.

## HB-024 — Logika wyboru trasy pomiędzy planszami

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 7.1, 7.3, 18 i O-002.

**Rationale:** Atlas ma wpływać na decyzję, a nie być galerią odkryć. Wybór po ukończeniu planszy musi używać trwałej wiedzy, lecz zapisywać wybraną trasę tylko w bieżącym runie.

**Obecne zachowanie:** Przejście poziomu zwiększa dzień i ładuje kolejny poziom bez wyboru węzła.

**Oczekiwany rezultat:** Po wyjściu gracz otrzymuje legalne kierunki, wybiera jeden, zapisuje `currentNodeId`/trasę w runie i dopiero potem uruchamia następną planszę.

**Zakres:** Stan wyboru, komenda `ChooseRoute`, walidacja, integracja lifecycle i placeholder prezentacji możliwy do testowania.

**Non-goals:** Bez finalnego atlas UI, fałszywych podpowiedzi, cofania wyboru po zatwierdzeniu i bez rozstrzygania O-002 w kodzie poza istniejącą rekomendacją.

**Zależności:** HB-023 i HB-015.

**Dozwolony obszar plików:** Core World/Session, adapter LevelFlow, testy; scena UI tylko po jawnej zgodzie w karcie aktywnej.

**Kryteria akceptacji:** Nielegalny wybór nie zmienia stanu; poprawny wybór zmienia run raz; crash po zatwierdzeniu wraca do wybranego węzła; profil pamięta traversed edge; brak domyślnego losowego kierunku.

**Plan testów:** EditMode wszystkich krawędzi grafu, podwójnego submitu i błędnego ID; PlayMode przejścia dwóch węzłów i restartu pomiędzy nimi.

**Wpływ na save i kompatybilność:** Run DTO zapisuje bieżący node i trasę; wymaga jawnej migracji wersji, jeśli pola nie istniały.

**Wymagany handoff:** Sekwencja zdarzeń od exit do rozpoczęcia nowej planszy oraz punkty zapisu profilu/runu.

## HB-025 — Pierwszy ekran atlasu

**Status:** `Planned` — nie może przejść na `Ready` bez decyzji O-004  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 5.2, 5.4, 7.3 i 18.

**Rationale:** Główna hipoteza jest percepcyjna: gracz ma zobaczyć fizyczny postęp uzupełniającej się mapy i odróżnić go od bieżącej trasy. Sam poprawny model tego nie potwierdzi.

**Obecne zachowanie:** Brak widoku makromapy i języka wizualnego stanów odkrycia.

**Oczekiwany rezultat:** Ekran pokazuje wyłącznie dozwolone informacje, rozróżnia cztery stany węzłów i trzy stany dróg oraz wyróżnia bieżącą pozycję i legalne kierunki.

**Zakres:** Funkcjonalny UI prototypowy, dostępne etykiety/ikony, wybór trasy i komunikat nowego odkrycia. Dane płyną tylko z `WorldMapService`.

**Non-goals:** Bez finalnego artu, swobodnego pan/zoom dla dużej kampanii, procentu ukończenia, animowanej pogody i ukrytej topologii renderowanej „na zapas”.

**Zależności:** HB-024 oraz zaakceptowana O-004 określająca platformę referencyjną i minimalny input/layout.

**Dozwolony obszar plików:** `/Assets/Scripts/Presentation/Atlas/**`, dedykowana scena/prefab/UI assets, testy i `.meta`.

**Kryteria akceptacji:** Stan nie opiera się wyłącznie na kolorze; ukryty node nie zdradza nazwy/typu; wszystkie legalne wybory są dostępne z klawiatury/myszy; ponowne otwarcie mapy nie mutuje stanu; drugi run pokazuje odkrycia pierwszego.

**Plan testów:** PlayMode dla mapowania wszystkich stanów i inputu; ręczny test w standardowej rozdzielczości oraz z wyłączonymi animacjami; krótki playtest rozumienia legendy.

**Wpływ na save i kompatybilność:** UI nie zapisuje własnych danych; obserwuje profile/run DTO.

**Wymagany handoff:** Screenshoty każdego stanu, lista zastosowanych sygnałów innych niż kolor i wyniki krótkiego testu rozumienia.

## HB-026 — Adapter planszy do węzła świata

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 7.1, 7.2 i 21.5.

**Rationale:** Makrograf i lokalna plansza mają różne cykle życia. Adapter musi przekazać node/seed do obecnego `BoardManager`, nie zmuszając go jeszcze do docelowej generacji model-first.

**Obecne zachowanie:** `BoardManager` otrzymuje głównie numer poziomu i sam interpretuje trudność.

**Oczekiwany rezultat:** Rozpoczęcie węzła przekazuje jawny `BoardRequest` z node ID, run seed, lokalnym seedem i tymczasową konfiguracją; ukończenie zwraca `BoardOutcome`.

**Zakres:** Cienki adapter kompatybilności wokół obecnej planszy oraz mapping istniejącego level flow.

**Non-goals:** Bez przebudowy generatora, zmiany 8×8, nowych zombie, narzędzi i landmark mechanics.

**Zależności:** HB-024.

**Dozwolony obszar plików:** Session/World adapters, `BoardManager.cs`, `GameManager.cs`, testy i niezbędne serializowane referencje jawnie zatwierdzone.

**Kryteria akceptacji:** Każde wejście zna stabilny node i seed; powrót zawiera jawny outcome; nie ma inkrementacji dnia w dwóch miejscach; obecne zwykłe plansze pozostają grywalne.

**Plan testów:** EditMode mappingu; PlayMode dwa różne węzły, restart granicy i outcome death/exit.

**Wpływ na save i kompatybilność:** Run DTO musi przechowywać node/seed; brak mid-board snapshotu.

**Wymagany handoff:** Diagram granicy makro/lokalna plansza i lista tymczasowych zależności do usunięcia w M3/M4.

## HB-027 — Dowód trwałości atlasu między runami

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 4, 5, 6 i bramka atlasu z sekcji 22.

**Rationale:** To najwcześniejszy moment, w którym można sfalsyfikować główną hipotezę produktu bez budowania narzędzi, biomów i finału.

**Obecne zachowanie:** Elementy atlasu mogą działać osobno, lecz nie ma jednego testu pełnej pętli dwóch runów.

**Oczekiwany rezultat:** Test i ręczny scenariusz dowodzą, że pierwsza wyprawa odkrywa trasę, śmierć czyści run, a druga wyprawa widzi atlas i może podjąć lepszy wybór.

**Zakres:** Integracyjny fixture pięciodniowego grafu, automatyczna sekwencja dwóch runów i skrypt moderowanego playtestu.

**Non-goals:** Bez deklarowania sukcesu rynkowego, rozszerzania grafu, finalnego artu i trwałych bonusów mocy.

**Zależności:** HB-020–026.

**Dozwolony obszar plików:** Testy, fixtures i `/Docs/Validation/HB-027.md`; poprawki produkcyjne wymagają powrotu do właściwej karty.

**Kryteria akceptacji:** Automatyczny test przechodzi po restarcie repozytorium save; skrypt playtestu mierzy rozumienie trwałości i użycie wiedzy; odkrycia nie zdradzają nieodwiedzonych tras.

**Plan testów:** EditMode/PlayMode pełnej pętli, ręcznie co najmniej trzy sesje obserwacyjne; zanotować nie tylko deklaracje, ale faktyczny wybór w drugim runie.

**Wpływ na save i kompatybilność:** Weryfikuje aktualną wersję schema; używa wyłącznie profili testowych.

**Wymagany handoff:** Wyniki każdej sesji, obserwowane błędne modele mentalne i rekomendacja `Proceed`, `Revise` albo `Stop` dla M3. Bez zmiany kontraktu na podstawie pojedynczego testera.

### Bramka M2

M2 jest zaliczony, gdy pięciodniowy graf przechodzi walidację, discovery pozostaje po śmierci i co najmniej część testerów faktycznie wykorzystuje wcześniejszą wiedzę w kolejnym runie. Jeśli mapa jest odbierana wyłącznie jako ekran wyboru poziomu, należy poprawić informację lub strukturę przed M3.

---

# M3 — Deterministic Tactical Core

## HB-030 — Stabilne typy siatki i encji

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 8, 9.1, 11.1 i 21.1–21.3.

**Rationale:** Resolver, generator, AI i solver muszą mówić tym samym językiem pozycji oraz tożsamości. `Transform`, indeks listy i Unity `InstanceID` nie są stabilnym stanem domenowym ani bezpiecznym ID save'a.

**Obecne zachowanie:** Pozycje wynikają głównie z obiektów sceny i fizyki 2D, a przeciwnicy są rejestrowani jako komponenty.

**Oczekiwany rezultat:** Czyste typy `GridPosition`, `Direction`, `EntityId`, `EntityKind` oraz jawne zasady sąsiedztwa działają bez Unity.

**Zakres:** Niemutowalne value types, porównywanie, stabilne sortowanie, konwersje tylko w adapterze prezentacji oraz testy granic.

**Non-goals:** Bez ruchu, pathfindingu, generatora, serializacji scen i finalnej reprezentacji wszystkich rodzajów terenu.

**Zależności:** Bramka M2.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Board/Primitives/**`, testy EditMode i `.meta`.

**Kryteria akceptacji:** Równość/hash są deterministyczne; brak zależności od `UnityEngine`; kolejność encji nie zależy od kolejności utworzenia obiektów; niedozwolona pozycja jest odrzucana przez jawny boundary, nie collider.

**Plan testów:** EditMode dla równości, hashowania, kierunków, sąsiedztwa, granic i stabilnego sortowania.

**Wpływ na save i kompatybilność:** `EntityId` runtime nie musi przetrwać kolejnej planszy; ID contentu nadal są tekstowe. Ewentualny zapis pozycji pojawi się dopiero w Deferred HB-090.

**Wymagany handoff:** Krótki kontrakt wartości i lista adapterów, które później zastąpią odczyt `Transform`.

## HB-031 — Warstwowy `BoardState`

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 8, 9.1, 11.1 i 21.1.

**Rationale:** Teren, przeszkoda, przedmiot i aktor mogą współistnieć na polu oraz zmieniać się niezależnie. Jedna tablica GameObjectów utrudni pchanie głazu, dół, otwartą bramę i solver.

**Obecne zachowanie:** Autorytatywny stan wynika z colliderów, obiektów oraz pól komponentów takich jak `Wall`.

**Oczekiwany rezultat:** Czysty `BoardState` posiada rozmiar, warstwy terrain/obstacle/item/actor, indeks encji i bezpieczne query/mutacje respektujące inwarianty.

**Zakres:** Model 8×8 bez narzuconego bezpiecznego obwodu, podstawowe typy istniejącego contentu, immutable definitions oraz jawny mutable state planszy.

**Non-goals:** Bez prezentacji, generatora, AI, hałasu, narzędzi, pełnego HP przeszkód i zmiany assetów sceny.

**Zależności:** HB-030.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Board/State/**`, testy i fixtures.

**Kryteria akceptacji:** Nie można umieścić dwóch aktorów na jednym polu; warstwy nie nadpisują się; lookup ID i pozycji jest spójny po mutacji; clone/snapshot nie współdzieli kolekcji; model działa w teście bez Unity.

**Plan testów:** EditMode dla dodawania/usuwania/przenoszenia, konfliktów warstw, granic, clone i inwariantów.

**Wpływ na save i kompatybilność:** Brak mid-board save. Model ma być możliwy do snapshotu, ale DTO nie jest częścią ticketu.

**Wymagany handoff:** Diagram warstw i lista stanów nadal tymczasowo pozostających w widokach.

## HB-032 — `PlayerCommand`, `TurnResult` i `GameEvent`

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 9, 10 i 21.2.

**Rationale:** Jedna reprezentacja intencji i wyniku pozwala oddzielić zasady od animacji. Bez niej input, AI, UI i coroutine mogą ponownie policzyć tę samą akcję.

**Obecne zachowanie:** Input bezpośrednio wywołuje zachowanie `MonoBehaviour`, a wynik jest rozproszony pomiędzy ruch, coroutine, zasoby i collidery.

**Oczekiwany rezultat:** Jawne komendy co najmniej `Move`, `Wait`, `Interact` oraz wyniki `Rejected`/`Accepted` z uporządkowanym `GameEvent[]`.

**Zakres:** Dyskryminowane typy komend, kody odrzucenia, kontrakt kosztu tury i minimalny katalog zdarzeń do obecnej planszy.

**Non-goals:** Bez implementacji narzędzi, pełnej fazy zombie, animacji i sieciowego event busa.

**Zależności:** HB-031.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Turns/Contracts/**`, testy EditMode.

**Kryteria akceptacji:** Rejected zawsze ma stabilną przyczynę i nie deklaruje kosztu; Accepted jawnie mówi, że zużywa turę; kolejność events jest stabilna; kontrakty nie zawierają referencji Unity.

**Plan testów:** EditMode dla konstrukcji wszystkich wariantów, semantyki kosztu i stabilnej kolejności.

**Wpływ na save i kompatybilność:** Komendy nie są jeszcze trwałym replay formatem; wszelkie ID w zdarzeniach muszą być stabilne w obrębie planszy.

**Wymagany handoff:** Tabela command → możliwe result codes → events oraz lista celowo niezaimplementowanych komend.

## HB-033 — Resolver ruchu, `Wait` i podstawowej interakcji

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 9 i 10.

**Rationale:** Walidacja i skutek muszą korzystać z jednego modelu. Czysty resolver usuwa `Physics2D.Linecast` jako źródło prawdy i gwarantuje, że odrzucona akcja jest bezpłatna.

**Obecne zachowanie:** `MovingObject` i collidery decydują o możliwości ruchu, a koszt może być naliczany przez komponent gracza.

**Oczekiwany rezultat:** Czysty resolver przyjmuje `BoardState`, `RunState` i komendę, a zwraca nowy wynik/mutacje oraz events dla ruchu, wait, blokady, wyjścia i prostego collect/interact.

**Zakres:** Jedna kratka ortogonalnie, jawne walkability, koszt zaakceptowanej akcji, nagroda przed kosztem i early result dla exit/death.

**Non-goals:** Bez fazy zombie, pchania, narzędzi, losowego hit chance, diagonali i animacji.

**Zależności:** HB-032 oraz decyzja O-003 tylko dla semantyki testowego pickup; jeśli nie jest potrzebny, pickup pozostaje poza kartą.

**Dozwolony obszar plików:** Core Board/Turns/State i testy EditMode.

**Kryteria akceptacji:** Jedna komenda zmienia najwyżej jedno pole gracza; invalid target nie zmienia żadnego stanu; accepted action nalicza koszt dokładnie raz; poprawna nagroda może uratować przed głodem; wejście na exit emituje jawny outcome.

**Plan testów:** Tabela przypadków wolne/zablokowane/out of bounds/wait/pickup/exit/death, immutability odrzuconego wyniku i powtórzenie tych samych danych.

**Wpływ na save i kompatybilność:** Modyfikuje tylko aktywny model run/board. Run save na granicy planszy pozostaje zgodny.

**Wymagany handoff:** Macierz komend i kosztów; wskazać roboczą semantykę O-003 albo potwierdzić, że jej nie implementowano.

## HB-034 — Centralny `TurnController`

**Status:** `Planned` — nie może przejść na `Ready` bez decyzji O-002  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcja 10 oraz O-002.

**Rationale:** Priorytet nagrody, kosztu, śmierci, wyjścia, intentów i środowiska jest regułą gry. Jawny kontroler umożliwia deterministyczne testy oraz zatrzymuje późniejsze fazy po końcu planszy.

**Obecne zachowanie:** Kolejność zależy od callbacków, coroutine i flag w `GameManager`; O-002 pozostaje decyzją właściciela.

**Oczekiwany rezultat:** Kontroler realizuje dokładnie dziewięć faz z kontraktu, posiada blokadę wejścia i zwraca jeden `TurnResult` do prezentacji.

**Zakres:** Faza gracza, early terminal checks, placeholder fazy zablokowanych intentów, środowisko i ponowne planowanie; jawne rozstrzygnięcie simultaneous exit/starvation po aktualizacji kontraktu.

**Non-goals:** Bez zaawansowanego AI, real-time input buffering, animacji, narzędzi i pogody.

**Zależności:** HB-033 oraz zaakceptowana decyzja O-002 wpisana do `GameDesignContract.md`.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Turns/**`, testy i ewentualna aktualizacja kontraktu wykonana przed ticketem przez Coordinatora.

**Kryteria akceptacji:** Fazy mają stałą kolejność; rejected command kończy się bez tury; accepted command wykonuje jedną pełną turę; death/exit zatrzymuje dalsze fazy zgodnie z O-002; drugi resolver nie może rozpocząć się równolegle.

**Plan testów:** EditMode każdej ścieżki terminalnej i kolejności events, w tym simultaneous exit/starvation; test reentrancy.

**Wpływ na save i kompatybilność:** Brak zmiany schema; outcome jest zapisywany przez istniejący lifecycle po zakończeniu planszy.

**Wymagany handoff:** Chronologiczny log przykładowych tur oraz odniesienie do rozstrzygniętej O-002.

## HB-035 — Adapter inputu i prezentacji zdarzeń

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 9.2, 18 i 21.2/21.5.

**Rationale:** Widok ma odtwarzać wynik, a nie ponownie rozstrzygać zasady. Migracja adaptera zamyka drogę do podwójnego ruchu, podwójnego kosztu i kolizji pomiędzy coroutine.

**Obecne zachowanie:** `PlayerScript`, `MovingObject`, `GameManager` i UI współdzielą logikę ruchu, tur i prezentacji.

**Oczekiwany rezultat:** Input tworzy jedną komendę; `TurnController` ją rozstrzyga; prezentacja sekwencyjnie odtwarza events i dopiero potem odblokowuje wejście.

**Zakres:** Adapter obecnego inputu PC, presenter ruchu/zasobów/block/exit, synchronizacja widoków z domeną oraz tymczasowe zachowanie istniejącego artu.

**Non-goals:** Bez nowego systemu input dla wielu platform, finalnego HUD, nowych animacji i zmiany reguł domenowych.

**Zależności:** HB-034; O-004 przed finalnym layoutem, ale ten adapter może zachować obecny input referencyjny.

**Dozwolony obszar plików:** Presentation/adapters, `PlayerScript`, `MovingObject`, `GameManager`, UI scripts, jawnie wymagane sceny/prefaby i testy.

**Kryteria akceptacji:** Widok nie wykonuje mutacji domenowej; w trakcie odtwarzania kolejna komenda jest blokowana; przy wyłączonej animacji stan końcowy jest ten sam; po każdym evencie Transform zgadza się z modelem.

**Plan testów:** PlayMode szybkiego wielokrotnego inputu, blokady, wyłączenia animacji, reload sceny; pełna regresja HB-000G.

**Wpływ na save i kompatybilność:** Brak zmiany schema; nie zapisuje stanu UI/animacji.

**Wymagany handoff:** Diagram Input → Command → Result → Events → View oraz lista usuniętych źródeł prawdy.

## HB-036 — Czysty model Shamblera i zablokowany intent

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 11.1 i 11.3.

**Rationale:** Przewidywalny zombie jest elementem zagadki. Intent policzony przed wejściem gracza musi pozostać ten sam podczas wykonania, nawet jeśli cel stanie się nieważny.

**Obecne zachowanie:** AI działa w komponentach i może zależeć od listy `GameManager`, pozycji obiektów i czasu Unity.

**Oczekiwany rezultat:** Czysty Shambler planuje `Move`, `Attack` lub `Wait` według jednej nazwanej reguły; intent jest zapisany w stanie tury i później wykonywany bez przeplanowania.

**Zakres:** Enemy state/definition, planner Shamblera, podstawowy path/tie-break, cadence i events wykonania.

**Non-goals:** Bez Listenera, hałasu, losowych decyzji, ukrytego aggro i finalnego VFX.

**Zależności:** HB-034.

**Dozwolony obszar plików:** Core Enemies/Turns/Board, testy; `Enemy.cs` dopiero w adapterze HB-035/038.

**Kryteria akceptacji:** Ten sam stan daje ten sam intent; tie-break jest jawny; zablokowany cel kończy się `Wait`, nie replanem; plan nie mutuje planszy; cadence jest częścią definicji/stanu.

**Plan testów:** EditMode dla kierunków, przeszkód, tie-breaków, cadence, invalidated target i attack range.

**Wpływ na save i kompatybilność:** Intenty nie są zapisywane między planszami; mid-board save pozostaje Deferred.

**Wymagany handoff:** Jednozdaniowa reguła AI możliwa do pokazania graczowi oraz komplet przykładów tie-break.

## HB-037 — Konflikty intentów i stabilna inicjatywa

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcja 11.2.

**Rationale:** Dwa zombie celujące w to samo pole nie mogą być rozstrzygane przez kolejność GameObjectów. Reguła konfliktu jest widoczną częścią logiki planszy i musi być odtwarzalna.

**Obecne zachowanie:** Kolejność listy przeciwników i callbacków Unity może wpływać na rezultat.

**Oczekiwany rezultat:** Stabilna inicjatywa rezerwuje cele; pierwszy legalny intent wykonuje się, kolejny czeka, swap jest zabroniony, a invalid intent nie planuje ponownie.

**Zakres:** Batch plan/execution dla wielu przeciwników oraz deterministyczny resolver konfliktów.

**Non-goals:** Bez reakcji łańcuchowych, opportunity attacks, knockbacku, stadnego AI i równoległego wykonywania.

**Zależności:** HB-036.

**Dozwolony obszar plików:** Core Enemies/Turns i testy EditMode.

**Kryteria akceptacji:** Permutacja kolejności wejściowej listy nie zmienia wyniku; wspólny cel ma jednego zwycięzcę; swap nie zachodzi; event order odpowiada inicjatywie.

**Plan testów:** Parametryzowane konflikty 2–5 encji, permutacje listy, swap, chain blocking i usunięty aktor.

**Wpływ na save i kompatybilność:** Brak zmiany schema.

**Wymagany handoff:** Tabela konfliktów i uzasadnienie wybranej inicjatywy.

## HB-038 — Czytelne UI intentów

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 11, 18 i bramka intentów w sekcji 22.

**Rationale:** Deterministyczny intent ma wartość tylko wtedy, gdy gracz potrafi go odczytać przed decyzją. Informacja nie może zależeć wyłącznie od krótkiej animacji lub koloru.

**Obecne zachowanie:** Przeciwnicy wykonują ruch, ale nie pokazują zablokowanego planu w spójnym języku UI.

**Oczekiwany rezultat:** Każdy zombie pokazuje ikonę/kształt i cel `Move`, `Attack`, `Investigate` lub `Wait`; UI odświeża się wyłącznie po fazie planowania.

**Zakres:** Presenter intentów, symbole, dostępna legenda, synchronizacja z eventami i testy mapowania.

**Non-goals:** Bez finalnego artu, przewidywania wielu tur, pokazywania ukrytych przyszłych losowań i tutorialu tekstowego dla całej gry.

**Zależności:** HB-037 i HB-035.

**Dozwolony obszar plików:** Presentation/Enemies/UI, prefaby i sceny jawnie wymienione, testy PlayMode.

**Kryteria akceptacji:** Wszystkie wspierane intenty są rozróżnialne bez samego koloru; cel jest jednoznaczny; UI nie zmienia intentu; zablokowany intent nie „przeskakuje” po ruchu gracza.

**Plan testów:** PlayMode mapowania model→symbol i lifecycle; ręczny test z wyłączonymi animacjami oraz krótki blind prediction test.

**Wpływ na save i kompatybilność:** Brak; UI jest odtwarzane ze stanu planszy.

**Wymagany handoff:** Screenshot każdego intentu i odsetek poprawnych przewidywań z testu jakościowego.

## HB-039 — Replay komend i hash deterministyczności

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 16, 21.2 i 22.

**Rationale:** Seed bez historii komend nie odtwarza decyzji, a screenshot nie wyjaśnia rozbieżności modelu. Lekki replay jest narzędziem debugowania i fundamentem solvera, nie funkcją dla gracza.

**Obecne zachowanie:** Błędu tury nie można jednoznacznie odtworzyć z małego zestawu danych.

**Oczekiwany rezultat:** Testowy recorder zapisuje wersję zasad, seed, board blueprint i komendy; replay daje stabilny hash kanonicznego stanu po każdej turze.

**Zakres:** Kanoniczna serializacja do testów/debugu, state hash, runner replay i raport pierwszej rozbieżności.

**Non-goals:** Bez publicznego formatu replay, synchronizacji sieciowej, anty-cheatu, nagrywania animacji i kompatybilności między dowolnymi wersjami gry.

**Zależności:** HB-030–038.

**Dozwolony obszar plików:** Core Diagnostics/Turns, testy i ignorowany katalog wyników.

**Kryteria akceptacji:** Dwa uruchomienia tych samych danych mają identyczne hashe; zmiana komendy daje kontrolowaną różnicę; hash nie zależy od kolejności słowników, Transform ani czasu.

**Plan testów:** EditMode powtarzalności, permutacji kolekcji i raportu divergence; PlayMode porównania modelu z końcową prezentacją.

**Wpływ na save i kompatybilność:** Replay jest artefaktem diagnostycznym i nie zastępuje save. Zawiera własny jawny numer wersji.

**Wymagany handoff:** Minimalny zredagowany przykład replay i hashów; lokalizacja artefaktów oraz potwierdzenie, że są ignorowane.

### Bramka M3

M3 jest zaliczony, gdy każde wejście tworzy najwyżej jedną komendę, rejected action jest darmowa, accepted action uruchamia jedną pełną turę, intenty są zablokowane i czytelne, a identyczny seed/stan/ciąg komend daje identyczny hash.

---

# M4 — Generator, hałas i drugi archetyp zombie

## HB-040 — Niezależne strumienie deterministycznego RNG

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcja 16.

**Rationale:** Globalny `UnityEngine.Random` sprawia, że dodatkowy efekt kosmetyczny może zmienić układ następnej planszy. Nazwane strumienie izolują świat, run, planszę, puzzle i kosmetykę.

**Obecne zachowanie:** Gameplay i generacja korzystają z globalnego RNG Unity.

**Oczekiwany rezultat:** Czysty provider tworzy powtarzalne strumienie z root seed oraz stabilnego stream ID; logika domenowa nie wywołuje `UnityEngine.Random`.

**Zakres:** Algorytm/abstrakcja RNG, derivation seedów, API zakresów bez modulo bias, stan/debug label i test vectors.

**Non-goals:** Bez kryptografii, zabezpieczenia seedów przed graczem, daily run i zmiany kosmetycznego RNG poza potrzebnym adapterem.

**Zależności:** Bramka M3.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Random/**`, adaptery obecnej generacji, testy.

**Kryteria akceptacji:** Ten sam root/stream daje tę samą sekwencję; pobranie z kosmetyki nie zmienia board stream; zakresy są udokumentowane; brak globalnego RNG w zmigrowanym gameplayu.

**Plan testów:** Stałe test vectors, niezależność strumieni, granice range, replay M3.

**Wpływ na save i kompatybilność:** Run DTO przechowuje root seed oraz identyfikatory/wersję algorytmu niezbędne do odtworzenia granicy planszy.

**Wymagany handoff:** Schemat derivation oraz repo-wide lista pozostałych dozwolonych wywołań `UnityEngine.Random`.

## HB-041 — `BoardBlueprint` i konfiguracja budżetu

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 7.2, 8, 16 i 17.

**Rationale:** Generator powinien wytworzyć dane możliwe do walidacji przed `Instantiate`. Blueprint jest też wejściem replayu, testów i prezentera.

**Obecne zachowanie:** `BoardManager` losuje oraz natychmiast tworzy GameObjecty, a trudność wynika głównie z dnia/liczby zombie.

**Oczekiwany rezultat:** Niemutowalny `BoardBlueprint` opisuje rozmiar 8×8, start, exit, warstwy, spawn points i metadane seed/config; `BoardGenerationConfig` opisuje budżet sytuacji.

**Zakres:** Typy danych, validation-friendly construction, mapping do `BoardState` oraz tymczasowy exporter istniejącej planszy do porównań.

**Non-goals:** Bez algorytmu generacji, presenter instancjonującego cały content, większych map i balansu finalnego.

**Zależności:** HB-040 i HB-031.

**Dozwolony obszar plików:** Core Generation/Board, GameData configs, tests.

**Kryteria akceptacji:** Blueprint nie zawiera GameObjectów; jest deterministycznie porównywalny/haszowalny; nie może mieć duplikatu start/exit; konfiguracja nie opiera trudności wyłącznie na rozmiarze/liczbie zombie.

**Plan testów:** EditMode construction, round-trip blueprint→state, hash i invalid examples.

**Wpływ na save i kompatybilność:** Blueprint może wejść do replay, ale nie do normalnego save między planszami.

**Wymagany handoff:** Przykład jednego blueprintu i mapping pól budżetu na zamierzoną presję.

## HB-042 — Walidator lokalnej planszy

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 8, 14, 16 i 17.

**Rationale:** Losowa plansza musi gwarantować legalny start i osiągalne wyjście zanim gracz straci run. Walidator jest niezależny od algorytmu, aby sprawdzać też ręczne i landmarkowe blueprinty.

**Obecne zachowanie:** Osiągalność wynika pośrednio z bezpiecznego obwodu i nie jest dowodzona dla danych.

**Oczekiwany rezultat:** Czysty walidator raportuje out-of-bounds, konflikty warstw, brak start/exit, nieosiągalność bez opcjonalnego loot/tool oraz naruszenia budżetu krytycznego.

**Zakres:** Reachability dla podstawowych reguł, lista wszystkich błędów z kontekstem, metryki ścieżki i branching.

**Non-goals:** Bez pełnego solvera tur z zombie, automatycznej poprawy, oceny „fun” i landmark mechanics jeszcze nieistniejących.

**Zależności:** HB-041.

**Dozwolony obszar plików:** Core Generation/Validation, test fixtures.

**Kryteria akceptacji:** Poprawna plansza przechodzi; każdy błąd ma test; wymagane wyjście jest osiągalne bez losowego narzędzia; komunikat zawiera seed/config, gdy dostępne.

**Plan testów:** EditMode dla ręcznych dobrych/złych blueprintów, granic i metryk.

**Wpływ na save i kompatybilność:** Brak.

**Wymagany handoff:** Katalog reguł walidacji i znane ograniczenia względem przyszłego dynamicznego AI.

## HB-043 — Generator model-first zwykłej planszy 8×8

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 7.2, 8, 16 i Appendix A.

**Rationale:** Docelowa różnorodność ma pochodzić z decyzji/topologii, nie z wydłużania mapy lub automatycznie bezpiecznej ramki. Generacja danych przed widokiem umożliwia walidację i deterministyczność.

**Obecne zachowanie:** Generator zostawia wolny obwód, używa globalnego RNG i łączy losowanie z `Instantiate`.

**Oczekiwany rezultat:** Dla request/config/seed generator zwraca blueprint 8×8 bez gwarantowanego bezpiecznego obwodu, z legalnym startem, exit i kontrolowaną topologią.

**Zakres:** Pierwsza rodzina zwykłej planszy, podstawowe terrain/obstacles/items/enemies, jawne fazy generacji i adapter prezentacji blueprintu.

**Non-goals:** Bez biomów finalnych, landmarku, pełnego tool contentu, większych rozmiarów i ręcznego „naprawiania” GameObjectów po walidacji.

**Zależności:** HB-040–042.

**Dozwolony obszar plików:** Core Generation, `BoardManager` jako fasada/presenter, configs, testy i jawne prefaby tylko do mapowania widoku.

**Kryteria akceptacji:** Ten sam input daje identyczny blueprint; każdy wynik przechodzi walidator; outer ring może zawierać meaningful terrain/obstacles, ale start nie jest unfair; prezentacja nie zmienia danych.

**Plan testów:** Snapshot determinism, set znanych seedów, PlayMode blueprint→widok i porównanie liczby/pozycji encji.

**Wpływ na save i kompatybilność:** Run przechowuje seed i config ID, nie pełny środek planszy.

**Wymagany handoff:** Fazy algorytmu, przykładowe blueprinty i lista usuniętych użyć globalnego RNG.

## HB-044 — Ograniczone próby i poprawny fallback generatora

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcja 16.

**Rationale:** Generator nie może wisieć ani wypuścić błędnej planszy, gdy losowy kandydat nie przechodzi walidacji. Limit i przygotowany fallback dają kontrolowany koszt oraz pewność ukończenia.

**Obecne zachowanie:** Brak formalnego protokołu generate → validate → retry → fallback i raportowania przyczyn.

**Oczekiwany rezultat:** Pipeline wykonuje skończoną liczbę deterministycznych prób, raportuje odrzucenia i używa poprawnego fallback blueprintu zgodnego z requestem.

**Zakres:** Orkiestrator prób, sub-seedy, telemetryka diagnostyczna, biblioteka minimalnych fallbacków i ich walidacja przy starcie/testach.

**Non-goals:** Bez nieskończonego retry, pobierania contentu, ukrywania fallback rate i automatycznego obniżania trudności runu bez komunikacji.

**Zależności:** HB-043.

**Dozwolony obszar plików:** Core Generation/Pipeline, GameData fallback, tests.

**Kryteria akceptacji:** Limit zawsze obowiązuje; ten sam input wybiera tę samą próbę/fallback; każdy fallback przechodzi walidator; failure report zawiera seed, node, config i przyczyny bez ogromnego logu.

**Plan testów:** Wymuszony generator zawsze-fail, sukces na N-tej próbie, uszkodzony fallback oraz timeout/performance bound.

**Wpływ na save i kompatybilność:** Wybrany board seed/config zostaje w run boundary data; zmiana algorytmu może zmienić planszę po aktualizacji i wymaga version label dla replay.

**Wymagany handoff:** Maksymalna liczba prób, zasada sub-seedów i raport z wymuszonego fallbacku.

## HB-045 — Testy 1 000/10 000 seedów i metryki trudności

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 16, 17 i 22.

**Rationale:** Kilka ręcznych plansz nie ujawni rzadkich softlocków ani dryfu trudności. Batch test ma zatrzymać błędny generator przed produkcją contentu.

**Obecne zachowanie:** Nie ma automatycznej dystrybucji metryk ani rejestrowania seedów porażki.

**Oczekiwany rezultat:** Szybki test codzienny sprawdza co najmniej 1 000 seedów, milestone gate 10 000; raportuje reachability, długość/gałęzie ścieżki, gęstość przeszkód, presję i fallback rate.

**Zakres:** Headless batch harness, limity czasu, histogramy/summary i zapis minimalnych failing cases poza źródłami.

**Non-goals:** Bez udowadniania „fun”, publicznej telemetrii, arbitralnego automatycznego balansu i ukrywania outlierów średnią.

**Zależności:** HB-044.

**Dozwolony obszar plików:** Tests/Generation, diagnostics/reporting, ignorowany output.

**Kryteria akceptacji:** 10 000 aktywnych seedów ma zero invalid/softlock; failure podaje reprodukowalny seed; fallback rate mieści się w jawnym progu ustalonym przed testem; runtime jest zapisany.

**Plan testów:** Dwa identyczne batch runy porównują summary/hash; celowo uszkodzona config dowodzi, że harness wykrywa problem.

**Wpływ na save i kompatybilność:** Brak; używa synthetic runs.

**Wymagany handoff:** Raport zbiorczy, wszystkie failing seeds i rekomendacja budżetu; bez selektywnego pomijania wyników.

## HB-050 — Hałas jako zdarzenie domenowe

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcja 12.

**Rationale:** Hałas ma połączyć narzędzia, trasę i manipulowanie zombie. Musi być tym samym zdarzeniem, które interpretuje AI i pokazuje UI, a nie tylko klipem audio.

**Obecne zachowanie:** Dźwięki nie tworzą jawnego, deterministycznego stanu rozgrywki.

**Oczekiwany rezultat:** Akcja może emitować `NoiseEvent` ze źródłem, pozycją, siłą/zasięgiem i czasem; query słyszalności jest deterministyczne i prezentowalne.

**Zakres:** Model hałasu, propagacja w podstawowym terenie, lifetime, events i prosta wizualizacja debug/UI.

**Non-goals:** Bez fizycznej symulacji akustyki, losowego perception check, finalnego audio mixu i reakcji Listenera.

**Zależności:** HB-039 i HB-043.

**Dozwolony obszar plików:** Core Noise/Turns/Board, Presentation/Noise, tests.

**Kryteria akceptacji:** Ten sam event daje ten sam obszar; UI i AI czytają ten sam model; wygasanie jest jawne; brak ukrytego rzutu; sygnał jest czytelny bez audio.

**Plan testów:** EditMode zasięg/przeszkody/lifetime i replay; PlayMode zgodności wizualizacji.

**Wpływ na save i kompatybilność:** Bieżący hałas jest BoardState i nie jest zapisywany między planszami.

**Wymagany handoff:** Reguła propagacji w jednym akapicie i screenshot/debug view dla kilku sił.

## HB-051 — Listener reagujący na ostatni słyszany hałas

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 11.3 i 12.

**Rationale:** Listener zmienia hałas z kary w narzędzie planowania: gracz może zaakceptować ryzyko albo zwabić zombie. Jego zachowanie musi pozostać proste do przewidzenia.

**Obecne zachowanie:** Istnieje tylko obecny typ przeciwnika; żaden archetyp nie ma jawnej pamięci celu hałasu.

**Oczekiwany rezultat:** Listener zapamiętuje ostatni słyszany, jednoznacznie wybrany hałas i planuje `Investigate`; przy braku sygnału używa jawnego `Wait`/fallback zachowania.

**Zakres:** Definition/state/planner Listenera, tie-break wielu hałasów, utrata celu, execution i presenter intentu.

**Non-goals:** Bez sight cone, ukrytego alert level, grupowej komunikacji, losowej percepcji i trzeciego archetypu.

**Zależności:** HB-050 i HB-038.

**Dozwolony obszar plików:** Core Enemies/Noise, presentation mapping/prefab, tests.

**Kryteria akceptacji:** Reguła wyboru hałasu jest stabilna; `Investigate` wskazuje cel przed ruchem gracza; unieważniony intent nie replanowuje; gracz może w teście odciągnąć Listenera.

**Plan testów:** EditMode dla zasięgu, wielu źródeł, tie-break, wygasania i konfliktów; PlayMode intent→wykonanie.

**Wpływ na save i kompatybilność:** Pamięć hałasu żyje tylko w BoardState.

**Wymagany handoff:** Jednozdaniowa reguła Listenera i macierz przykładów.

## HB-052 — Bramka czytelności intentów i hałasu

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** bramka intentów z sekcji 22.

**Rationale:** Poprawne testy jednostkowe nie dowodzą, że człowiek rozumie ikonę, zasięg i moment blokowania planu. Content narzędzi nie powinien powstać, dopóki ta informacja jest nieczytelna.

**Obecne zachowanie:** Shambler, Listener i noise UI mogą przejść testy techniczne, ale nie mają wspólnego wyniku playtestu.

**Oczekiwany rezultat:** Moderowany scenariusz mierzy przewidywanie intentów oraz świadome użycie hałasu; wyniki prowadzą do `Proceed`, `Revise` albo `Stop`.

**Zakres:** 5–8 krótkich sesji, scenariusze bez/ze wskazówką, zapis przewidywań przed turą i analiza błędów.

**Non-goals:** Bez nowych mechanik, publicznej analityki, testu marketingowego i poprawiania produkcyjnych plików w tej samej karcie.

**Zależności:** HB-050 i HB-051.

**Dozwolony obszar plików:** `/Docs/Validation/HB-052.md` oraz test-only fixtures; produkcja read-only.

**Kryteria akceptacji:** Większość uczestników przewiduje znanego zombie; błędy są sklasyfikowane UI/reguła/tutorial; porażkę można wyjaśnić bez ukrytego RNG; istnieje decyzja bramki.

**Plan testów:** Playtest według jednego skryptu, osobne wyniki pierwszego kontaktu i po nauczeniu, porównanie z automatycznym replayem.

**Wpływ na save i kompatybilność:** Brak; profile testowe są izolowane.

**Wymagany handoff:** Zanonimizowana tabela wyników, obserwacje i dokładna rekomendacja dalszego działania.

### Bramka M4

M4 jest zaliczony, gdy 10 000 seedów aktywnej konfiguracji nie zawiera invalid boardów, replay jest stabilny, globalny RNG nie steruje logiką, a testerzy potrafią przewidzieć Shamblera i Listenera oraz świadomie wykorzystać hałas.

---

# M5 — Narzędzia i systemowa interakcja

## HB-060 — Wspólny model celowanej interakcji

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 9, 13.1 i 18.

**Rationale:** Siekiera, łopata, pickup i pchanie nie powinny tworzyć czterech osobnych ścieżek inputu. Wspólny model celu pozwala walidować akcję przed kosztem i jasno pokazać możliwe pola.

**Obecne zachowanie:** Interakcje wynikają głównie z wejścia na collider i metod konkretnego komponentu.

**Oczekiwany rezultat:** `InteractionQuery` zwraca legalne akcje/cel, a `InteractCommand` jednoznacznie wskazuje wybraną możliwość; brak legalnego celu nie zużywa tury.

**Zakres:** Query model, target IDs/positions, kody dostępności, podstawowe collect/push hooki i presenter podświetlenia.

**Non-goals:** Bez narzędzi, rozbudowanego inventory, menu radialnego, craftingu i automatycznego wyboru „najlepszej” interakcji.

**Zależności:** Bramka M4.

**Dozwolony obszar plików:** Core Interaction/Turns/Board, Presentation/Interaction i testy.

**Kryteria akceptacji:** Query nie mutuje stanu; komenda odwołuje się do stabilnego celu; invalid/stale target jest darmowy; wszystkie legalne cele są widoczne bez polegania tylko na kolorze.

**Plan testów:** EditMode empty/single/multiple/stale target; PlayMode selection, cancel i disabled animation.

**Wpływ na save i kompatybilność:** Brak nowego trwałego stanu.

**Wymagany handoff:** Tabela typ celu → legalna komenda → koszt oraz przykład konfliktu wielu interakcji.

## HB-061 — Dwa sloty i stan instancji narzędzia

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcja 13.1.

**Rationale:** Dwa sloty tworzą czytelny koszt okazji bez budowania pełnego inventory managementu. Definicja narzędzia musi być oddzielona od pozostałych użyć konkretnej instancji.

**Obecne zachowanie:** Run nie ma docelowego modelu slotów, definicji tool contentu ani ograniczonych użyć.

**Oczekiwany rezultat:** `ToolDefinition` ma stabilne ID i niezmienne dane, `ToolInstanceState` ma charges, a `RunState` posiada dokładnie dwa jawne sloty.

**Zakres:** Model, equip/replace/remove, inwarianty charges, konfiguracje placeholder axe/shovel i podstawowy read-only HUD.

**Non-goals:** Bez użycia narzędzia, craftingu, stacków, napraw, ulepszeń, losowych affixów i trwałego przenoszenia narzędzi między runami.

**Zależności:** HB-060 i HB-010.

**Dozwolony obszar plików:** Core Tools/State, GameData/Tools, Presentation/HUD, tests.

**Kryteria akceptacji:** Są dokładnie dwa sloty; definitions nie są mutowane; dwa runy nie współdzielą charges; UI obserwuje stan; nie można utworzyć ujemnych charges.

**Plan testów:** EditMode equip/replace/empty/charges/copy; PlayMode HUD po reload sceny.

**Wpływ na save i kompatybilność:** Run DTO otrzymuje sloty i stable tool IDs; wymaga migracji schema z pustymi slotami.

**Wymagany handoff:** Model definition vs instance oraz zachowanie nieznanego tool ID przy load.

## HB-062 — `UseToolCommand` i atomowe zużycie charges

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 9, 10 i 13.1.

**Rationale:** Narzędzie nie może zużyć ładunku lub tury, jeśli cel stał się nieważny. Walidacja, efekt, hałas, koszt i charge muszą być jednym wynikiem resolvera.

**Obecne zachowanie:** Sloty istnieją, ale nie ma wspólnego kontraktu użycia w turze.

**Oczekiwany rezultat:** `UseToolCommand(slot, target)` jest najpierw w pełni walidowane, a accepted result atomowo emituje efekt, noise, charge spend i koszt tury.

**Zakres:** Command/result, dispatch do zachowania tool definition, kody błędu, eventy i podgląd konsekwencji dostępnych przed potwierdzeniem.

**Non-goals:** Bez konkretnych efektów axe/shovel, rollbacku po animacji, durability repair i dowolnych skryptów narzędzi z dostępem do Unity.

**Zależności:** HB-061 i HB-050.

**Dozwolony obszar plików:** Core Tools/Turns/Interaction, presentation preview, tests.

**Kryteria akceptacji:** Invalid target/empty slot/zero charges nie zmieniają stanu; accepted use zużywa dokładnie jeden charge i jedną turę; noise jest częścią tego samego result; podwójny submit jest blokowany.

**Plan testów:** EditMode pełna macierz walidacji i atomicity; PlayMode preview/confirm/cancel/rapid input.

**Wpływ na save i kompatybilność:** Zmiana charges jest zapisywana w runie na istniejących granicach autosave.

**Wymagany handoff:** Chronologiczny event list poprawnego i odrzuconego użycia.

## HB-063 — Siekiera: szybsza trasa za duży hałas

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 12, 13.2 i 13.4.

**Rationale:** Samo „szybciej usuwa krzak” jest płaską premią. Duży hałas zamienia oszczędność czasu w przestrzenne ryzyko i łączy siekierę z Listenerem.

**Obecne zachowanie:** Przeszkody mogą mieć HP w widoku, ale nie istnieje domenowy kontrast ręczne karczowanie vs użycie axe.

**Oczekiwany rezultat:** Krzew/miękka drewniana przeszkoda ma wolną alternatywę bez narzędzia; axe usuwa ją szybciej, zużywa charge i emituje czytelny głośny noise.

**Zakres:** Domenowy stan przeszkody/progress, akcja ręczna, efekt axe, konfiguracja liczb, events, audio/visual feedback i fakt `AXE_CREATES_LOUD_NOISE`.

**Non-goals:** Bez walki siekierą, ścinania każdego drzewa, craftingu, trwałych ulepszeń i drugiego zastosowania „barykada”, dopóki pierwsze nie przejdzie bramki.

**Zależności:** HB-062 i HB-051.

**Dozwolony obszar plików:** Core Tools/Obstacles/Knowledge, configs, presentation/prefabs jawnie wymienione, tests.

**Kryteria akceptacji:** Obie trasy są legalne; axe zawsze jest szybsza według config; noise i charge są pokazane przed/po akcji; brak axe nie softlockuje; Listener reaguje na dokładnie ten noise event.

**Plan testów:** EditMode porównania kosztów/charges/noise i replay; PlayMode przeszkoda z/bez axe oraz reakcja Listenera.

**Wpływ na save i kompatybilność:** Tool charges w runie; fact discovery w profilu może wymagać rozszerzenia/migracji listy stabilnych fact IDs.

**Wymagany handoff:** Porównanie kosztu obu rozwiązań i nagranie/screenshot czytelności noise.

## HB-064 — Łopata: schowek i dół

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 13.3 i 13.4.

**Rationale:** Łopata ma łączyć eksplorację z manipulowaniem planszą, a nie wyłącznie skracać animację zbierania. Schowek daje korzyść ekonomiczną, dół daje taktyczną kontrolę pola.

**Obecne zachowanie:** Brak oznaczonych schowków, domenowego digging progress i tymczasowej pułapki na zombie.

**Oczekiwany rezultat:** Oznaczony schowek ma wolną alternatywę lub jawnie opcjonalną nagrodę; shovel wykopuje go szybko. Drugie użycie tworzy czasowy dół zatrzymujący wspierany archetyp.

**Zakres:** Dig targets/progress, cache reward, pit state/lifetime, interakcja Shamblera, charges, events i fact `PIT_TRAPS_SHAMBLER`.

**Non-goals:** Bez dowolnego kopania każdego pola, terraformingu, losowej ukrytej pułapki, obrażeń jako walki i obowiązkowego celu wymagającego losowej shovel.

**Zależności:** HB-062, HB-036 i HB-042.

**Dozwolony obszar plików:** Core Tools/Terrain/Enemies/Knowledge, configs, presentation, tests.

**Kryteria akceptacji:** Invalid terrain jest darmowy; pit ma jawny czas i efekt; wyjście pozostaje osiągalne bez shovel; nagroda jest przyznana przed kosztem akcji zgodnie z kontraktem; facts odkrywają się po obserwacji.

**Plan testów:** EditMode cache/pit/lifetime/enemy/softlock validation; PlayMode oba zastosowania i UI pozostałych tur/charges.

**Wpływ na save i kompatybilność:** Charges i facts jak HB-063; pit jest wyłącznie BoardState.

**Wymagany handoff:** Macierz zastosowanie → korzyść → koszt/ryzyko → alternatywa bez narzędzia.

## HB-065 — Pickup, zamiana slotu i decyzja O-003

**Status:** `Planned` — nie może przejść na `Ready` bez decyzji O-003  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 13.1, 18 i O-003.

**Rationale:** Przy dwóch pełnych slotach znalezione narzędzie tworzy ważny wybór, ale przypadkowa zamiana byłaby karą za wejście na pole. Semantyka pickup musi być spójna z jedzeniem i świadomą interakcją.

**Obecne zachowanie:** Nie ma docelowego flow podnoszenia tool instance ani modalnej zamiany.

**Oczekiwany rezultat:** Po zaakceptowaniu O-003 jedzenie i narzędzia używają jawnej, spójnej semantyki; pełne sloty otwierają confirm replace z możliwością anulowania bez kosztu.

**Zakres:** Pickup commands, modal state, wybór slotu, UI porównania stable ID/charges i integracja z turą.

**Non-goals:** Bez plecaka, dropowania stosów, handlu, craftingu i automatycznego niszczenia istniejącego narzędzia.

**Zależności:** HB-061, HB-062 i zaakceptowana O-003 w kontrakcie.

**Dozwolony obszar plików:** Core Items/Tools/Turns, Presentation/Inventory, configs/prefabs i tests.

**Kryteria akceptacji:** Anulowanie/invalid replace nic nie kosztuje; zatwierdzenie zmienia dokładnie jeden slot i rozlicza turę według przyjętej reguły; rapid input nie duplikuje itemu; UI pokazuje charges obu opcji.

**Plan testów:** EditMode empty/full/cancel/stale target; PlayMode modal/input/reload; test accepted O-003 dla food i tools.

**Wpływ na save i kompatybilność:** Run DTO zachowuje wynik zamiany; brak nowych danych profilu.

**Wymagany handoff:** Odniesienie do rozstrzygniętej O-003 i pełny diagram modalnego flow.

## HB-066 — Field Notes dla faktów systemowych

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 4, 5.4, 13.4 i 18.

**Rationale:** Druga forma progresu to zrozumienie reguł. Dziennik ma utrwalać zaobserwowany fakt i wspierać pamięć, ale nie zdradzać skutku przed eksperymentem.

**Obecne zachowanie:** `ProfileState` potrafi przechować IDs faktów, lecz gracz nie ma uporządkowanego widoku ani momentu odkrycia.

**Oczekiwany rezultat:** Po pierwszej obserwacji fakt trafia do profilu, pojawia się jednoznaczny feedback i jest dostępny w Field Notes z tekstem/ikoną; nieodkryte fakty nie zdradzają treści.

**Zakres:** `FactDefinition`, event discovery, lokalizacja robocza, ekran/lista notesów i pierwsze fakty axe/shovel/Listener.

**Non-goals:** Bez pełnego bestiariusza, checklisty procentowej, quizów, automatycznego tutorialu i bonusów statystyk.

**Zależności:** HB-063–065 i HB-022.

**Dozwolony obszar plików:** Core Knowledge/Profile, GameData/Facts, Presentation/FieldNotes, persistence, tests.

**Kryteria akceptacji:** Fact odkrywa się tylko po zdefiniowanej obserwacji, dokładnie raz; trwa przez nowy run; unknown nie pokazuje treści; UI nie jest jedynym właścicielem danych.

**Plan testów:** EditMode trigger/idempotency/save migration; PlayMode feedback i ponowne otwarcie po death/new run.

**Wpływ na save i kompatybilność:** Profil zapisuje stable fact IDs; brakująca definicja ma kontrolowany placeholder, nie niszczy profilu.

**Wymagany handoff:** Rejestr facts: ID, warunek odkrycia, tekst, system źródłowy.

### Bramka M5

M5 jest zaliczony, gdy oba narzędzia tworzą różne decyzje, każde ma koszt lub ryzyko, brak narzędzia nie tworzy losowego softlocka, a odkryte fakty przeżywają śmierć bez zdradzania niepoznanych mechanik.

---

# M6 — Landmark z głazami, płytami i bramami

## HB-070 — Format ręcznie przygotowanego szablonu landmarku

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 7.1, 14 i 16.

**Rationale:** Pierwsza zagadka ma być kuratorowana i automatycznie sprawdzalna, nie generowana jak pełny Sokoban. Szablon danych pozwala tworzyć warianty bez osobnej minigry i bez ręcznej logiki sceny.

**Obecne zachowanie:** Brak typu node `Landmark`, formatu zagadki i mappingu do wspólnego BoardState.

**Oczekiwany rezultat:** `LandmarkTemplate` opisuje blueprint, cele/rezultaty, dozwolone warianty i wymagania walidacji przy użyciu stable IDs.

**Zakres:** Data format, loader, validator strukturalny i jeden pusty/sanity fixture w grafie dnia około 5.

**Non-goals:** Bez mechaniki głazów/bram, proceduralnego układania puzzla, osobnej sceny minigry i finalnego contentu.

**Zależności:** HB-045 i HB-020.

**Dozwolony obszar plików:** Core Landmarks/World/Generation, GameData/Landmarks, tests.

**Kryteria akceptacji:** Szablon buduje ten sam BoardState co zwykła plansza; ma co najmniej dwa nazwane outcomes; invalid references są raportowane; wymagane narzędzie musi być guaranteed albo opcjonalne.

**Plan testów:** EditMode load/validation/stable ID/round-trip template→blueprint.

**Wpływ na save i kompatybilność:** Run zapisuje landmark node ID/outcome na granicy planszy, nie środek puzzla.

**Wymagany handoff:** Schemat szablonu i przykład dwóch outcomes bez implementowania rozwiązania.

## HB-071 — Pchanie głazów w wspólnym resolverze

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 9, 10 i 14.

**Rationale:** Głaz ma być systemową przeszkodą używaną w normalnej siatce, a nie skryptem poziomu. Pchanie musi respektować tę samą walidację, koszt i kolejność tury.

**Obecne zachowanie:** Przeszkody są statyczne lub posiadają HP w widoku; brak domenowej akcji push.

**Oczekiwany rezultat:** `PushCommand` albo jednoznaczny wariant `Interact` atomowo przesuwa gracza i głaz, jeśli pole za nim jest legalne.

**Zakres:** Boulder state, walidacja łańcucha o długości jeden, events, presenter i wpływ na path/reachability.

**Non-goals:** Bez pchania wielu głazów, ciągnięcia, fizyki rigidbody, bezwładności i narzędziowej siły.

**Zależności:** HB-060 i HB-031–035.

**Dozwolony obszar plików:** Core Obstacles/Turns/Board, Presentation, prefab boulder i tests.

**Kryteria akceptacji:** Zablokowane pchnięcie jest darmowe; poprawne zużywa jedną turę; gracz/głaz nie zajmują konfliktowych pól; AI/path query widzi nową pozycję natychmiast.

**Plan testów:** EditMode wolne/blokada/krawędź/aktor/płyta; PlayMode synchronizacji dwóch animacji i rapid input.

**Wpływ na save i kompatybilność:** Tylko BoardState; brak mid-board save.

**Wymagany handoff:** Macierz legalności pchania i event order.

## HB-072 — Płyty i bramy jako stan domenowy

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 10 i 14.

**Rationale:** Stan bramy musi wynikać z planszy po każdej mutacji i być znany resolverowi przed wykonaniem intentu. Animator nie może decydować, czy pole jest przechodnie.

**Obecne zachowanie:** Brak plates/gates; analogiczne przeszkody opierają stan na komponentach widoku.

**Oczekiwany rezultat:** Płyta obserwuje aktora/głaz według jawnej reguły, brama ma stable link ID i stan open/closed, a zmiana emituje event przed kolejną fazą.

**Zakres:** Definitions/state, recompute trigger, wiele płyt do jednej bramy według wybranej konfiguracji, walkability i presentation.

**Non-goals:** Bez złożonych obwodów logicznych, timerów, kolorowych kluczy, fizycznych jointów i ukrytych połączeń.

**Zależności:** HB-071.

**Dozwolony obszar plików:** Core Landmarks/Board/Turns, presentation prefabs, tests.

**Kryteria akceptacji:** Model i widok zawsze zgadzają się; zejście z płyty aktualizuje bramę; intent w zamknięte pole staje się `Wait` bez replanu; link errors wykrywa validator.

**Plan testów:** EditMode actor/boulder enter/leave, multi-plate rule, intent invalidation; PlayMode animation disabled/enabled.

**Wpływ na save i kompatybilność:** Tylko BoardState.

**Wymagany handoff:** Jawna logika wielu płyt i chronologia events względem faz tury.

## HB-073 — Pierwszy landmark dnia około 5

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 14 i 20.

**Rationale:** Landmark ma sprawdzić, czy wspólne systemy tworzą ciekawą logiczną kulminację. Jeden starannie przygotowany układ dostarcza więcej wiedzy niż wiele niesprawdzonych losowych puzzli.

**Obecne zachowanie:** Mechaniki są dostępne, lecz nie istnieje pełny authored puzzle z trasą podstawową i alternatywnym kosztem.

**Oczekiwany rezultat:** Landmark używa głazów, płyt, bram, co najmniej jednego zombie oraz opcjonalnego narzędzia; ma minimum dwa sensowne outcomes/rozwiązania i drogę niewymagającą losowego dropu.

**Zakres:** Jeden template, konfiguracja rewards/consequences, wejście/wyjście z node, presentation i jasna informacja celu.

**Non-goals:** Bez pełnej proceduralności, dziesiątek wariantów, jedynego poprawnego rozwiązania i nowych reguł działających tylko na tym poziomie.

**Zależności:** HB-070–072 i HB-063/064.

**Dozwolony obszar plików:** GameData/Landmarks, potrzebne art/prefabs/scene presentation, localization/UI i tests.

**Kryteria akceptacji:** Co najmniej dwa outcomes mają różny koszt; brak narzędzia nie blokuje celu obowiązkowego; zasady są komunikowane przez istniejące UI; ukończenie zapisuje outcome w runie i discovery w profilu, jeśli dotyczy.

**Plan testów:** Ręczne przejście każdą zamierzoną drogą, automated validator, replay wszystkich rozwiązań i test death/exit.

**Wpływ na save i kompatybilność:** Stable template/outcome IDs w run/profile; zmiana ID wymaga migracji/aliasu.

**Wymagany handoff:** Diagram układu, lista zamierzonych rozwiązań, kosztów i awaryjnej ścieżki.

## HB-074 — Solver rozwiązywalności landmarku

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 14, 16 i 22.

**Rationale:** Pchanie głazu może bezpowrotnie zablokować stan. Solver wykorzystujący czysty resolver pozwala sprawdzić każdy wspierany wariant bez ręcznego testowania wszystkich sekwencji.

**Obecne zachowanie:** Validator zna strukturę, ale nie dowodzi istnienia sekwencji prowadzącej do celu.

**Oczekiwany rezultat:** Ograniczony BFS/A* enumeruje domenowe stany dla landmarku, znajduje co najmniej jedno rozwiązanie każdego wymaganego outcome albo raportuje minimalny failing fixture.

**Zakres:** Kanoniczny hash stanu, legal command enumeration, limity czasu/stanów, reconstruction path i integration z testami template.

**Non-goals:** Bez AI podpowiadającego graczowi, optymalnego solvera dowolnego poziomu, generowania puzzli i uwzględniania animacji.

**Zależności:** HB-073 i HB-039.

**Dozwolony obszar plików:** Core Solver/Diagnostics, tests i ignorowane raporty.

**Kryteria akceptacji:** Każdy wymagany outcome ma znalezioną ścieżkę w limicie; celowo zablokowany wariant failuje; hash deduplikuje stany; raport zawiera template ID/variant/seed.

**Plan testów:** Znane małe puzzle, no-solution fixture, determinism i performance budget.

**Wpływ na save i kompatybilność:** Brak; solver używa snapshotów testowych.

**Wymagany handoff:** Liczba odwiedzonych stanów, czas i przykładowa sekwencja dla każdego outcome.

## HB-075 — Kuratorowane warianty i bramka landmarku

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** bramka landmarku z sekcji 22.

**Rationale:** Jeden znany układ traci regrywalność, ale pełna proceduralność jest zbyt ryzykowna. Mała rodzina ręcznie sprawdzonych wariantów daje różnorodność i zachowuje kontrolę jakości.

**Obecne zachowanie:** Jeden landmark może przejść solver, lecz jego decyzje i czytelność nie są jeszcze sprawdzone na wariantach.

**Oczekiwany rezultat:** 3–5 wariantów zmienia początkowe położenia, presję lub reward, każdy przechodzi solver; playtest potwierdza rozumienie kosztu rozwiązania.

**Zakres:** Wariant data, selection przez landmark RNG stream, batch solve i 5–8 sesji playtestowych.

**Non-goals:** Bez losowego przestawiania dowolnego obiektu, nowej mechaniki na wariant, content farm i skalowania planszy.

**Zależności:** HB-074.

**Dozwolony obszar plików:** GameData/Landmarks variants, tests i `/Docs/Validation/HB-075.md`.

**Kryteria akceptacji:** Wszystkie warianty mają rozwiązanie i dwa sensowne rezultaty tam, gdzie wymagane; gracz potrafi wyjaśnić konsekwencję; selection jest deterministyczny; brak znanego softlocka.

**Plan testów:** Solver dla każdego wariantu, replay, ręczny playtest bez instrukcji rozwiązania.

**Wpływ na save i kompatybilność:** Run/replay zapisuje variant ID/seed; profil nie zaznacza automatycznie wszystkich wariantów jako odwiedzone.

**Wymagany handoff:** Macierz wariant → rozwiązania → koszt → wynik solvera/playtestu.

### Bramka M6

M6 jest zaliczony, gdy każdy wspierany wariant przechodzi solver, brak losowego narzędzia nie blokuje celu, istnieją co najmniej dwa sensowne rezultaty, a testerzy rozumieją konsekwencje własnej trasy.

---

# M7 — Dziesięciodniowy vertical slice

## HB-080 — Rozszerzenie stałego grafu do około 10 dni

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 5.1, 7.1, 19 i 20.

**Rationale:** Dopiero pozytywny wynik pięciodniowego atlasu uzasadnia podwojenie contentu. Stały graf utrzymuje wartość trwałej wiedzy i pozwala zaprojektować pacing do finału.

**Obecne zachowanie:** Prototyp kończy się około dnia 5 i zawiera placeholder landmarku/celu.

**Oczekiwany rezultat:** Zweryfikowany graf ma około 10 dni, kilka tras, landmark około dnia 5, złączenia, skróty/informacje do wykorzystania w kolejnym runie i finałowy node.

**Zakres:** Rozbudowa data, stable IDs, hints placeholders, difficulty budgets i migracja/aliasy tylko jeśli absolutnie konieczne.

**Non-goals:** Bez kampanii 20–40 dni, losowego makrografu, obowiązku odwiedzenia wszystkiego i procentu ukończenia.

**Zależności:** Bramka M2 i M6; wyniki HB-027 muszą rekomendować `Proceed`.

**Dozwolony obszar plików:** GameData/World, validator tests, docs diagram.

**Kryteria akceptacji:** Wszystkie obowiązkowe cele są osiągalne; istnieją meaningful route choices; pierwsze zwycięstwo nie wymaga pełnego atlasu; istnieje wartość informacji dla co najmniej jednej kolejnej próby.

**Plan testów:** Graph validator, enumeracja tras, save migration ID i design walkthrough pacingu.

**Wpływ na save i kompatybilność:** Stare discovery IDs pozostają prawidłowe; usunięte ID wymagają aliasu lub kontrolowanej migracji.

**Wymagany handoff:** Diagram 10-dniowego grafu, pacing table i uzasadnienie każdej gałęzi.

## HB-081 — Dwa biomy różniące się regułą

**Status:** `Planned` — dokładne reguły pozostają `Hypothesis`, zanim playtest je zatwierdzi  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 15, 17 i 20.

**Rationale:** Podróż na północ potrzebuje widocznego postępu geograficznego, ale sam reskin nie zmienia decyzji. Dwa biomy wystarczą do sprawdzenia uczenia i kombinowania reguł bez produkcji pór roku.

**Obecne zachowanie:** Plansze używają jednego zestawu terenu i nie posiadają biome definition.

**Oczekiwany rezultat:** Las i zimna strefa mają po jednej–dwóch wyraźnych regułach wpływających na ruch, ślady, widoczność lub hałas; graf prowadzi ku chłodowi.

**Zakres:** `BiomeDefinition`, palety rodziny generatora, mechaniczny modifier każdego biomu, presentation i intro→combine pacing.

**Non-goals:** Bez pełnych pór roku, temperatury survival-sim, wielu skinów bez reguły i jednoczesnego wprowadzania wszystkich mechanik.

**Zależności:** HB-080 i HB-045; Coordinator wybiera eksperymentalne reguły na podstawie prototypu.

**Dozwolony obszar plików:** Core/GameData Biomes/Generation, presentation art/audio, tests.

**Kryteria akceptacji:** Tester potrafi opisać różnicę reguły; ten sam modifier jest widoczny dla generatora/resolvera/UI; pierwsze wystąpienie izoluje nową regułę; biom nie jest tylko sprite setem.

**Plan testów:** EditMode modifierów i generator constraints, batch seeds osobno na biom, PlayMode readability oraz playtest kontrastowy.

**Wpływ na save i kompatybilność:** Node definition wskazuje stable biome ID; brak dynamicznej pory roku w profilu.

**Wymagany handoff:** Hypothesis → mechanic → expected decision → observed playtest dla obu biomów.

## HB-082 — Archetypy miejsc i prawdziwe wskazówki tras

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 5.3, 7.3, 17 i 18.

**Rationale:** Wybór trasy jest ciekawy, gdy informacja jest niepełna, ale wiarygodna. Stabilne archetypy pozwalają wykorzystać pamięć atlasu bez fałszywych opisów.

**Obecne zachowanie:** Node może mieć nazwę/typ, ale nie istnieje system hints powiązany ze stabilnymi cechami contentu.

**Oczekiwany rezultat:** Archetypy takie jak schronienie, polana, most czy opuszczony obóz mają jawne tagi/cechy; wskazówka ujawnia prawdziwy podzbiór informacji w stanie `Rumored`/`Sighted`.

**Zakres:** Definitions/tags, hint generation bez kłamstwa, atlas presentation, localization i po kilka przykładów na biom.

**Non-goals:** Bez proceduralnej narracji LLM, ukrytego procentu prawdy, perfekcyjnej informacji i dziesiątek archetypów.

**Zależności:** HB-081 i HB-023.

**Dozwolony obszar plików:** Core/GameData World/Locations/Hints, Atlas UI, tests.

**Kryteria akceptacji:** Każda hint jest prawdziwa dla stabilnej cechy; może być niepełna, nie sprzeczna; unknown nie wycieka; tester używa co najmniej jednej wskazówki w wyborze.

**Plan testów:** EditMode wszystkich hint→definition, property test braku fałszu, PlayMode stanów atlasu i playtest wyboru.

**Wpływ na save i kompatybilność:** Profil zapisuje discovery/hint IDs tylko jeśli potrzebne; definitions używają stable IDs.

**Wymagany handoff:** Rejestr hintów z dowodem źródłowej cechy oraz obserwowane decyzje testerów.

## HB-083 — Finał i jawne zwycięstwo

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 19, 20 i O-001.

**Rationale:** Skończony cel nadaje podróży sens i umożliwia ocenę pacingu. Sam rosnący licznik dni nie mówi, czy gracz opanował systemy ani kiedy run jest kompletny.

**Obecne zachowanie:** Istnieje game over, lecz brak docelowego `Won` i finałowego node z zakończeniem.

**Oczekiwany rezultat:** Finał około dnia 10 sprawdza znane reguły bez dodawania nieujawnionej mechaniki, emituje `Won`, zamyka run i zachowuje profil.

**Zakres:** Final node/board, warunek zwycięstwa, lifecycle, minimalna presentation/narracja zgodna z rozstrzygniętą O-001.

**Non-goals:** Bez cutsceny wysokobudżetowej, new game+, boss fight jako tradycyjna walka i obowiązku kompletnego atlasu.

**Zależności:** HB-080–082, M3–M6 oraz rozstrzygnięcie O-001 przed finalnym tekstem.

**Dozwolony obszar plików:** Core outcome/session, final GameData/scene/presentation, tests.

**Kryteria akceptacji:** Finał jest osiągalny co najmniej jedną trasą bez kompletowania mapy; win i death są rozłączne zgodnie z O-002; po win `Continue` nie wznawia zakończonego runu; atlas pozostaje.

**Plan testów:** Automated route/final validation, PlayMode win lifecycle/restart, ręczne przejście minimalnej i alternatywnej trasy.

**Wpływ na save i kompatybilność:** Run zapisuje terminal `Won`, następnie jest archiwizowany/usuwany zgodnie z lifecycle; profil zachowuje discovery.

**Wymagany handoff:** Warunek zwycięstwa, minimalna ścieżka, zachowanie save przed/w trakcie/po finale.

## HB-084 — Podsumowanie wyprawy i dziennik trasy

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 4, 5.4, 19 i O-005.

**Rationale:** Po śmierci lub zwycięstwie gracz powinien rozumieć przyczynę, przebytą trasę i trwałe odkrycia. To wzmacnia chęć kolejnego runu lepiej niż leaderboard surowej liczby dni.

**Obecne zachowanie:** Game over koncentruje się na wyniku/liczbie dni i zewnętrznych rekordach.

**Oczekiwany rezultat:** Lokalny ekran pokazuje trasę, terminal cause, użyte narzędzia, kluczowe decyzje i nowe node/edge/facts; pozwala przejść do atlasu lub nowego runu.

**Zakres:** Run summary model, event aggregation, UI, integracja Field Notes/Atlas i prywatna lokalna historia ostatnich kilku wypraw, jeśli nie komplikuje schema.

**Non-goals:** Bez publicznego leaderboardu, oceniania „optymalności”, trwałych bonusów mocy i śledzenia danych bez zgody.

**Zależności:** HB-083, HB-066 i decyzja O-005 dla usunięcia/zastąpienia starego ekranu rekordów.

**Dozwolony obszar plików:** Core Summary/Profile, Presentation Summary/Atlas/Notes, persistence, tests.

**Kryteria akceptacji:** Przyczyna końca odpowiada events; nowe odkrycia są odróżnione od starych; summary nie zmienia stanu; restart zachowuje profil; można rozpocząć kolejny run bez zewnętrznej sieci.

**Plan testów:** EditMode aggregation różnych outcomes, PlayMode death/win/restart, ręczny test czytelności.

**Wpływ na save i kompatybilność:** Opcjonalna historia ma limit i schema migration; brak danych osobowych lub sieciowego uploadu.

**Wymagany handoff:** Screenshoty death/win oraz mapping każdego pola summary do źródłowego event/state.

## HB-085 — Stabilizacja, dostępność i playtest vertical slice

**Status:** `Planned`  
**Priorytet:** P0 release gate  
**Powiązany kontrakt:** sekcje 18–22.

**Rationale:** Vertical slice ma odpowiedzieć, czy pełna pętla jest zrozumiała i regrywalna, nie tylko czy każda mechanika działa osobno. Stabilizacja musi poprzedzić rozszerzanie kampanii.

**Obecne zachowanie:** Wszystkie systemy istnieją, ale nie przeszły wspólnej macierzy jakości, dostępności, performance i obserwacji wielu runów.

**Oczekiwany rezultat:** Kandydat vertical slice przechodzi automatyczne bramki, pełny smoke, checklistę dostępności i 8–12 obserwowanych sesji; powstaje decyzja dalszego zakresu.

**Zakres:** Regression pass, seed gate, save corruption/restart, input/UI scaling, sygnały nie tylko kolorem, możliwość wyłączenia/przyspieszenia animacji, lokalna opt-in telemetryka testowa lub ręczne arkusze oraz triage.

**Non-goals:** Bez nowych głównych mechanik, monetyzacji, kampanii 40-dniowej, publicznego uploadu telemetryki i poprawiania wszystkich sugestii testerów bez priorytetu.

**Zależności:** HB-080–084 oraz wszystkie wcześniejsze bramki.

**Dozwolony obszar plików:** Najpierw tests/docs/config; każda poprawka produkcyjna powstaje jako osobny child ticket z własnym zakresem i review.

**Kryteria akceptacji:** Zero P0/P1 defects; 10 000 seedów zielone; save recovery zielony; większość testerów rozumie trwałość atlasu i intenty; część dobrowolnie rozpoczyna drugi run; finale osiągalne; brak sekretu/obowiązkowej sieci.

**Plan testów:** Pełne EditMode/PlayMode, dwa powtórne batch runs, manual smoke macierzy rozdzielczości/inputu, 8–12 playtestów z wcześniej zdefiniowanymi pytaniami.

**Wpływ na save i kompatybilność:** Test aktualizacji z ostatniego wspieranego schema; backup i recovery obowiązkowe.

**Wymagany handoff:** Release report z wynikami, defektami, wskaźnikami jakościowymi i jedną decyzją: `expand`, `iterate core loop` albo `stop/pivot`.

### Bramka M7

Vertical slice jest ukończony dopiero po `Accept` HB-085. Samo zaimplementowanie listy funkcji nie wystarcza; główna obietnica atlasu, wiedzy i przewidywalnych tur musi być zrozumiała w obserwowanym zachowaniu graczy.

---

# Po vertical slice — zadania świadomie odroczone

## HB-090 — Snapshot i wznowienie środka planszy

**Status:** `Deferred`  
**Powiązany kontrakt:** sekcje 6.2, 10, 16 i 20.  
**Rationale:** Wymaga stabilnego BoardState, versioned replay i wszystkich dynamicznych systemów; wcześniejszy snapshot podwoiłby koszt migracji.  
**Obecne zachowanie:** Save działa na bezpiecznych granicach między planszami.  
**Oczekiwany rezultat:** Atomowy snapshot pełnego board/turn/intent/noise state.  
**Zakres:** DTO, migration, recovery i exact-turn resume.  
**Non-goals:** Cloud sync.  
**Zależności:** HB-085.  
**Dozwolony obszar plików:** Core Persistence/Board/Turns i testy.  
**Kryteria akceptacji:** Wznowienie każdej wspieranej fazy daje ten sam stan/hash, a corruption wraca do bezpiecznego backupu.  
**Plan testów:** Crash injection na granicach faz, round-trip każdego typu encji i porównanie replay/hash.  
**Wpływ na save:** Nowa wersja schema z migracją.  
**Wymagany handoff:** Tabela snapshot fields i wyniki crash injection.

## HB-091 — Rozbudowa kampanii do 20–25 dni

**Status:** `Deferred`  
**Powiązany kontrakt:** sekcje 7.1, 17, 19 i 20.  
**Rationale:** Więcej contentu ma sens dopiero, gdy 10-dniowa pętla generuje dobrowolne powtórki; długość nie naprawi słabej decyzji.  
**Obecne zachowanie:** Vertical slice kończy się około dnia 10.  
**Oczekiwany rezultat:** Dłuższy pacing bez rozmycia wiedzy atlasu.  
**Zakres:** Graf, content budgets, kolejne kombinacje poznanych reguł.  
**Non-goals:** Automatyczne 30–40 dni i losowy makrograf.  
**Zależności:** Pozytywna decyzja `expand` z HB-085.  
**Dozwolony obszar plików:** Zostanie określony przed `Ready`.  
**Kryteria akceptacji:** Każdy nowy odcinek wnosi kombinację decyzji, graf pozostaje osiągalny, a pacing nie wymaga kompletowania atlasu.  
**Plan testów:** Walidacja całego grafu, seed gate per biome oraz playtest długości/retencji sesji.  
**Wpływ na save:** Stable IDs i migracje.  
**Wymagany handoff:** Pacing/content budget i dowód potrzeby rozszerzenia.

## HB-092 — Pory roku

**Status:** `Deferred`  
**Powiązany kontrakt:** sekcja 15.  
**Rationale:** Pory roku dodają drugą oś czasu i dużą macierz contentu, która może mylić geograficzne ochładzanie podróży na północ.  
**Obecne zachowanie:** Biomy i ewentualna pogoda opisują przestrzeń/bieżący run.  
**Oczekiwany rezultat:** Tylko po osobnym prototypie mechanicznie znaczący cykl sezonowy.  
**Zakres:** Do ustalenia po playteście.  
**Non-goals:** Sezon jako reskin.  
**Zależności:** HB-091 i osobna decyzja kontraktu.  
**Dozwolony obszar plików:** Niedookreślony — karta nie może przejść na `Ready`.  
**Kryteria akceptacji:** Sezon tworzy odrębne decyzje i nie zaciera kierunku podróży.  
**Plan testów:** Osobny prototyp A/B oraz playtest rozumienia osi geografia–czas przed produkcją contentu.  
**Wpływ na save:** Prawdopodobna nowa oś profile/world state.  
**Wymagany handoff:** Prototyp i wynik playtestu przed produkcją contentu.

## HB-093 — Daily/Endless Mode

**Status:** `Deferred`  
**Powiązany kontrakt:** sekcja 19.  
**Rationale:** Tryby regrywalności powinny wzmacniać działającą kampanię, a nie zastępować brak finału.  
**Obecne zachowanie:** Jedna skończona kampania vertical slice.  
**Oczekiwany rezultat:** Osobne, jasno oznaczone tryby po stabilizacji core.  
**Zakres:** Seed rules, scoring i oddzielny lifecycle do zaprojektowania.  
**Non-goals:** Włączenie ich do MVP.  
**Zależności:** HB-085 i osobna decyzja produktu.  
**Dozwolony obszar plików:** Niedookreślony.  
**Kryteria akceptacji:** Reprodukowalny daily, brak uszkodzenia profilu kampanii i jasny cel endless.  
**Plan testów:** Cross-machine seed vectors, lifecycle isolation, offline mode i playtest motywacji.  
**Wpływ na save:** Oddzielne run types/schema fields.  
**Wymagany handoff:** Projekt trybu i dowód, że rozwiązuje obserwowaną potrzebę.

## HB-094 — Tradycyjna walka, crafting i trwała moc

**Status:** `Deferred`  
**Powiązany kontrakt:** sekcje 1, 3, 13.1, 19 i 20.  
**Rationale:** Te systemy przesunęłyby grę z logicznego unikania/manipulacji w stronę statystyk i grindu, maskując problemy czytelności.  
**Obecne zachowanie:** Konflikt opiera się na pozycji, zasobach, intentach i narzędziach użytkowych.  
**Oczekiwany rezultat:** Brak implementacji bez jawnej zmiany kontraktu i prototypu dowodzącego wartości.  
**Zakres:** Tylko przyszły design spike.  
**Non-goals:** Damage numbers, loot rarity, skill tree w bieżącej roadmapie.  
**Zależności:** Osobna zmiana `GameDesignContract.md`.  
**Dozwolony obszar plików:** Brak do czasu nowej karty.  
**Kryteria akceptacji:** Muszą zostać napisane po jawnej zmianie decyzji; ticket nie może wcześniej przejść na `Ready`.  
**Plan testów:** Najpierw izolowany prototype/playtest porównujący decyzje logiczne z wariantem walki; produkcyjne testy dopiero po ADR.  
**Wpływ na save:** Prawdopodobnie wysoki; wymaga planu migracji.  
**Wymagany handoff:** ADR z alternatywami i wpływem na core promise.

## HB-095 — Publiczny leaderboard

**Status:** `Deferred` / `Blocked` przez O-005 i model bezpieczeństwa  
**Powiązany kontrakt:** sekcje 19, 21.5, O-005 i Appendix A.  
**Rationale:** Wynik liczby dni traci sens przy skończonym finale, a bezpośredni zapis z klienta nie może bezpiecznie przechowywać prywatnego credentialu.  
**Obecne zachowanie:** Stara integracja Dreamlo wymaga kwarantanny HB-000H.  
**Oczekiwany rezultat:** Jeśli dane uzasadnią funkcję, nowa metryka i kontrolowany backend bez sekretu w kliencie.  
**Zakres:** Osobny projekt po MVP.  
**Non-goals:** Ponowne osadzenie prywatnego klucza.  
**Zależności:** O-005, HB-085 i decyzja infrastrukturalna.  
**Dozwolony obszar plików:** Niedookreślony.  
**Kryteria akceptacji:** Zaakceptowany threat model, brak sekretu w kliencie, offline fallback i jawna polityka danych.  
**Plan testów:** Security review, abuse/rate-limit tests, awaria sieci i brak zależności profilu od usługi.  
**Wpływ na save:** Lokalny profil nie może zależeć od dostępności usługi.  
**Wymagany handoff:** Security review przed pierwszym requestem produkcyjnym.

---

# Rejestr decyzji blokujących

| Decyzja | Najpóźniej przed | Działanie Coordinatora |
| --- | --- | --- |
| O-001 — fikcja atlasu | HB-083/HB-084 | uzgodnić z użytkownikiem, zaktualizować kontrakt i teksty |
| O-002 — exit i śmierć w tej samej akcji | HB-034 | uzgodnić, dopisać jednoznaczny priorytet i test kontraktowy |
| O-003 — semantyka pickup | HB-033 jeśli zawiera pickup, bezwzględnie HB-065 | uzgodnić osobno jedzenie i narzędzia |
| O-004 — platforma referencyjna | HB-014 dla gwarancji filesystemu; najpóźniej HB-025 dla UI | potwierdzić PC/mobile, zapis i kryteria inputu/layoutu |
| O-005 — los leaderboardu | HB-000H i HB-084 | najpierw rotacja credentialu; potem decyzja produktowa |

### Rationale — dlaczego decyzje mają deadline

`Open` nie oznacza „agent wybierze rekomendację, gdy dojdzie do kodu”. Deadline zatrzymuje kartę przed utrwaleniem nieuzgodnionej semantyki, a jednocześnie nie blokuje niezależnych fundamentów.

# Kolejka wykonawcza

Najbliższa bezpieczna sekwencja to:

1. HB-000A — projektowy `.gitignore`;
2. HB-000B — wersjonowany baseline read-only;
3. decyzja o archiwum starych buildów i HB-000C — tylko index cleanup;
4. checkpoint właściciela;
5. HB-000D — `Force Text`;
6. HB-000E — izolowana reserializacja;
7. HB-000F — test harness;
8. HB-000G — regresja pojedynczego ruchu;
9. HB-010 — `RunState`.

HB-000H może być realizowany wcześniej po stronie właściciela jako rotacja credentialu, ale agent kodowy nie może wykonywać zewnętrznych operacji ani ujawniać wartości.

### Rationale — dlaczego tylko pierwsza spełniona zależność przechodzi na `Ready`

Po akceptacji HB-000A następną kartą `Ready` jest HB-000B. Utrzymanie jednej najbliższej karty w tym stanie ogranicza przypadkowe pominięcie baseline'u lub rozpoczęcie reserializacji przed uporządkowaniem indeksu i checkpointem właściciela.

# Zasada aktualizacji roadmapy

Po `Accept` Coordinator aktualizuje status karty oraz — tylko jeśli wynik zmienił faktyczne zależności — tę kolejkę. Nie dopisuje „przy okazji” nowej mechaniki do aktywnej karty. Nowe ustalenie projektowe najpierw trafia do `GameDesignContract.md` lub ADR, następnie do osobnej karty tutaj.

### Rationale — dlaczego roadmapa pozostaje żywa

Playtest może obalić hipotezę atlasu, intentów lub narzędzi. Roadmapa ma pozwalać zatrzymać inwestycję na bramce i zmienić kierunek jawnie, zamiast traktować pierwotną listę funkcji jak zobowiązanie niezależne od wyników.
