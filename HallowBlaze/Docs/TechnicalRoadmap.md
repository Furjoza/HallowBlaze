# HallowBlaze — Technical Roadmap

> Status dokumentu: **Accepted / execution roadmap v0.6 — M3.6 next; route-leg amendments accepted; M9 deferred**
> Data ostatniej weryfikacji: 2026-09-29
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

### 2.1. Numeracja i granice kart

- Bieżące karty używają formatu `M<kamień milowy>.<numer>`, na przykład `M0.7` albo `M3.2`.
- Każda karta zaczyna się poziomą linią i nagłówkiem z wyróżnionym numerem, aby jej początek i koniec były widoczne zarówno w edytorze, jak i w podglądzie Markdown.
- Cały dokument używa wyłącznie bieżących identyfikatorów `M*.*`, także w zależnościach, historycznych handoffach, kolejkach i nazwach planowanych raportów walidacyjnych.

### Rationale — dlaczego tylko jeden aktywny writer

Agenci współdzielą ten sam katalog, a Unity może automatycznie modyfikować assety, pliki `.meta`, `Library` i ustawienia. Jeden writer oraz jawny handoff redukują konflikty, przypadkową reserializację i pomieszanie zmian kilku ticketów.

## 3. Stan wyjściowy i założenia

- Repozytorium Git ma root w `E:\Repos\HallowBlaze`, a projekt Unity w `E:\Repos\HallowBlaze\HallowBlaze`.
- Projekt używa Unity `6000.3.21f1`.
- Unity Test Framework `1.6.0` jest zadeklarowany. Dwa test assemblies są zaakceptowane; końcowa bramka M0 wykonała EditMode `1/1` i PlayMode `5/5`, a Player build nie zawierał assembly testowych.
- `Visible Meta Files` jest włączone i bieżący audyt nie wykazał brakujących ani osieroconych plików `.meta`.
- Po `M0.5` aktywne są `Force Text` i `Visible Meta Files`; wszystkie 57 wspieranych scen/prefabów/animacji/kontrolerów jest w YAML, a sceny mają osobne assety `LightingSettings`. Po `M0.6` `ProjectSettings/NetworkManager.asset` nie istnieje; audyt nie wykazał binarnego pliku w objętej tymi kartami grupie.
- Rootowy `.gitignore` nadal jest źle zakotwiczony dla zagnieżdżonego projektu, ale projektowy `/.gitignore` z `M0.1` skutecznie kompensuje ten problem.
- Wyniki M0 zostały scalone do `master` przez PR #1. Preflight przed rozpoczęciem M1 wykazał czysty working tree bez zmian staged, unstaged i nieignorowanych untracked; każdą późniejszą zmianę nadal należy traktować jako własność jej autora i chronić zgodnie z `AGENTS.md`.
- Branch `M1/StateFoundation` zawiera wcześniejszą, niezaakceptowaną próbę `RunState` z commita `3d10d54`. Jej obecność nie zmienia statusu żadnej karty i nie stanowi dowodu spełnienia kryteriów `M1.2`.
- Po `M0.10` bieżący klient nie zawiera wartości Dreamlo ani transportu leaderboardu i działa wyłącznie offline. Historyczna wartość pozostaje w Git; nie wolno jej cytować, kopiować, ponownie używać ani testować przez sieć.

### 3.1. Operacyjny pre-flight Git

Git jest dostępny bez zmiany globalnego `safe.directory`, a rzeczywisty root to `E:/Repos/HallowBlaze`. Dnia 2026-08-29 wykonano `fetch --prune`: `origin/master` wskazuje `68080fa`, czyli merge PR #1 zawierający zaakceptowany tip M0 `4813b5a`. Lokalny `master` został zaktualizowany wyłącznie przez fast-forward, następnie scalony do `M1/StateFoundation` jako `b36c689`; merge zachowuje także wcześniejszy `3d10d54`. Jedyny konflikt tekstowy w `PlayerScript.cs` połączył zaakceptowane pojedyncze rozstrzygnięcie ruchu z zastaną delegacją kosztu do `RunState`; po merge nie pozostały wpisy unmerged ani zmiany w working tree.

Jeżeli błąd `unsafe repository` powróci, agent zatrzymuje zapis, zgłasza dokładny katalog i nie ustawia samodzielnie globalnego zaufania. Właściciel może jawnie zaufać wyłącznie dokładnej ścieżce repo, nigdy wildcardowi `*`.

### 3.2. Audyt zmian po baseline — 2026-08-25

Audyt objął status i diff Git, karty M0, powiązane sekcje kontraktu, statyczną walidację YAML/GUID oraz niezależną próbę EditMode w Unity `6000.3.21f1`. Nie wykonano stagingu, commita, operacji sieciowej ani rewrite'u historii.

- Repo-wide working tree ma `78` zmodyfikowanych plików, `1` usunięty i `17` untracked; brak zmian staged i konfliktów. Całość od `M0.4` wzwyż nadal nie ma osobnego checkpointu Git.
- Potwierdzono statycznie `57/57` docelowych assetów w YAML, `163/163` unikalnych GUID-ów, po jednej poprawnej referencji do każdego nowego `LightingSettings`, brak tekstowych wpisów `Missing Script` oraz usunięcie `NetworkManager.asset`.
- Niezależny EditMode batch zakończył się kodem `1` przed uruchomieniem testów. `ManageRecords.cs` zgłosił wiele `CS0106` i końcowe `CS1513` z powodu niezbilansowanych klamer; nie powstał wynik XML. PlayMode i smoke nie zostały uruchomione, ponieważ projekt nie kompiluje się.
- Próba testowa nie zmieniła hasha żadnego śledzonego ani nieignorowanego pliku. Utworzyła wyłącznie ignorowany log pod `Temp/TestResults/`.
- `M0.10` usunęło transport i wartość Dreamlo z bieżącego klienta, przywróciło kompilację oraz potwierdziło offline smoke. Właściciel 2026-08-27 odłożył leaderboard online i rotację historycznej wartości poza bieżący projekt, jawnie akceptując pozostające ryzyko historii bez deklarowania technicznego unieważnienia klucza.
- `PlayerScript.cs` usuwa drugie wywołanie `Move`, ale wraz z nim znika jedyne odtworzenie dźwięku poprawnego ruchu. Obecny test sprawdza tylko końcową pozycję po stałym czasie; nie odróżnia jednej próby od dwóch, nie sprawdza kosztu, blokady ani wyjścia.
- `com.unity.ai.assistant` został poza zakresem kart podniesiony z `2.17.0-pre.1` do `2.18.0-pre.2`. Rejestr Unity sprawdzony 2026-08-27 wskazuje `2.18.0-pre.2` jako `latest`, zgodne od Unity `6000.0`; manifest, lock i HEAD są ze sobą spójne. `M0.11` waliduje i formalnie przyjmuje ten stan bez sztucznej zmiany wersji.
- W root repo znajduje się nieśledzony `bfg-1.15.0.jar`. Brak `refs/original` i wpisów refloga wskazujących rewrite; pliku nie uruchomiono ani nie usunięto. Każda zmiana historii wymaga osobnej, jawnej autoryzacji i planu rotacji credentialu.
- `GameDesignContract.md` Appendix A nadal zawiera historyczne opisy podwójnego `Move` i pozostającego `NetworkManager.asset`; wymaga osobnej korekty po przyjęciu odpowiednich wyników, a nie może służyć jako dowód bieżącej kompilacji.

**Wniosek Coordinatora 2026-08-28:** blokady audytu zostały zamknięte: `M0.7`, `M0.8` i `M0.11` przeszły niezależne review, a pełna bramka M0 jest zielona. `M0.9` pozostaje świadomie odłożone po ukończeniu lokalnej kwarantanny `M0.10`; nie blokuje development milestone, ale zachowuje jawne ryzyko historii Git.

### Rationale — dlaczego higiena poprzedza gameplay

Nowy atlas, resolver tur i save system dotkną wielu plików. Bez poprawnego ignorowania katalogów generowanych, tekstowej serializacji i testów każda kolejna zmiana będzie trudniejsza do review, a konflikty mogą uszkodzić referencje Unity. Te zadania nie są kosmetyką — tworzą warunki do bezpiecznej rozbudowy gry i pracy agentowej.

## 4. Mapa etapów

| Etap | Rezultat | Bramka przejścia |
| --- | --- | --- |
| M0 — Safe Repository | repo jest czytelne dla Git, Unity i agentów; istnieje baseline oraz test runner | brak katalogów generowanych w statusie, tekstowe assety, zielony smoke test |
| M1 — State Foundation | pauza nie mutuje planszy, a `RunState` i `ProfileState` mają jednego właściciela i bezpieczny zapis | pause/exit są bezpieczne; restart sceny i aplikacji nie miesza profilu z runem |
| M2 — Persistent Atlas Prototype | stały graf około pięciu dni zachowuje odkrycia pomiędzy runami | tester używa wiedzy z pierwszej próby w drugiej |
| M3 — Deterministic Tactical Core | a command has one result/turn, intents are explicit, and multi-board travel has stable segment identity | replay gives the same hash and route legs advance only at valid boundaries |
| M4 — Generation, Noise and Enemies | model-first generator jest walidowany, a hałas tworzy decyzje | 10 000 seedów bez softlocka; Listener jest przewidywalny |
| M5 — Tools | dwa narzędzia tworzą odmienne decyzje i alternatywne trasy | brak losowego softlocka; koszty narzędzi są czytelne |
| M6 — Landmark | systemowa zagadka łączy pchanie, płyty i bramy | każdy wariant jest rozwiązywalny; co najmniej dwa sensowne rezultaty |
| M7 — Ten-day Vertical Slice | the two-biome journey has a finale, historical route-supply memory, fixed tool-source knowledge, and a summary | the player understands atlas evidence/intents/tools and wants another run |
| M8 — Optional Post-slice Work | explicitly deferred capabilities remain isolated from the validated core | each item requires its own product decision and evidence |
| M9 — Spatial Inventory and Resource Pressure | a 3×3 bag, two quick pockets, and satiety turn route knowledge into resource decisions | no duplication/loss; players choose routes from current need and learned supply |

Etapy są sekwencyjne jako bramki jakości, ale nie oznaczają masowego refaktoru. Każda karta ma pozostawić projekt w uruchamialnym stanie.

## 5. Reguły wspólne dla wszystkich kart

### 5.1. Domyślny obszar zmian

Dozwolony obszar plików w karcie jest zamkniętą listą. Pliki `.meta` odpowiadające nowym, przeniesionym lub usuniętym assetom są domyślnie częścią tego samego obszaru. `ProjectSettings`, `Packages`, sceny i prefaby są zabronione, jeśli karta nie wymienia ich jawnie.

### 5.2. Save compatibility

Do czasu powstania `M1.5` nie ma gwarancji kompatybilności prototypowych `PlayerPrefs`. Od `M1.5` każda zmiana DTO musi:

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

### 5.5. Accepted future-feature readiness audit — 2026-09-27

This audit reviewed every existing milestone against the accepted multi-board route, atlas-observation, and deferred inventory contract in `GameDesignContract.md` section 27. Completed cards remain historical evidence; follow-up cards supersede their intentionally narrower assumptions.

| Existing scope | Required preparation or conclusion | Owning follow-up |
| --- | --- | --- |
| M0 | No gameplay structure is affected. Repository and Unity safety rules remain unchanged. | none |
| M1.1–M1.11 (`Done`) | Do not rewrite history. Current run schema, `Food`, node-ID route, node/day seed, and profile discovery lists are migrated explicitly rather than extended opportunistically. Pause/menu behavior remains compatible; new route/inventory state is migrated explicitly rather than added opportunistically. | M3.11, M7.4, M7.5, M9.2–M9.3 |
| M2.1–M2.8 (`Done`) | Preserve the accepted atlas prototype. Edge selection must later begin travel instead of immediately reaching its destination; node-based `BoardRequest` is replaced by a stable segment address. | M3.11–M3.12 |
| M3.1–M3.4 (`Done`) | Grid/item layers and the one-turn result remain valid. Automatic food and cost `1` are transitional behavior, explicitly replaced only by M9. | M9.2 and M9.5 |
| M3.5, M3.6, M3.10 | Keep orchestration command-agnostic, modal input draft-only, and replay/version/hash extensible. M3.7–M3.9 need no inventory-specific redesign. | amendments in those cards |
| M4.1–M4.6 | Generate by stable board address; represent generic items/source IDs and a read-only resource manifest; measure segment and aggregate route supply. M4.7–M4.9 remain structurally unchanged. | amendments in those cards, M7.4–M7.5 |
| M5.1–M5.7 | Preserve ground instances until atomic commit; keep generic tool equipment separate from the bag; distinguish tool source, definition, and instance IDs. O-003 is resolved and transitional. | amendments in M5.1–M5.7, M9.5 |
| M6.1–M6.6 | Landmark/template addressing must not assume node equals one board; rewards use stable item IDs. Puzzle, solver, and gate semantics otherwise remain unchanged. | M6.1/M6.4 readiness notes, M3.11 |
| M7.1–M7.3 | Author variable segment sequences, route-aware biome/config data, truthful resource bands, and route length. | amended cards |
| M7 finale/summary/gate | Add resource observations and fixed tool-source knowledge before the finale; summaries use structured leg history; the final gate makes a separate M9 decision. | M7.4–M7.8 |
| M8.1 | A future snapshot includes active leg state, observation accumulator, and—if M9 has shipped—satiety, item instances, bag, and pockets. | amended card |
| M8.2 and M8.5 | Campaign budgets count boards/turns as well as days; accepted inventory is not part of the deferred crafting/combat/rarity proposal. M8.3, M8.4, and M8.6 require no inventory redesign. | amended cards |
| New inventory work | Do not pull backpack UI or economy into M5. Implement it only after a no-backpack playtest and an explicit owner checkpoint. | M9.1–M9.8 |

#### Rationale

The route-leg model changes identity and persistence before the bag changes UI. Establishing stable edge/segment addresses before generator work avoids later rewrites of seeds, replay, tool locations, and atlas samples. Keeping M9 last still lets the owner play the complete no-backpack game before committing to its new economy.

---

# M0 — Safe Repository

---

## `M0.1` — Project-local Unity `.gitignore`

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

---

## `M0.2` — Audyt baseline po migracji

**Status:** `Done`  
**Ukończono:** 2026-08-20 — review `Pass`; raport baseline zweryfikowany bez ustaleń P0–P3.  
**Priorytet:** P0  
**Powiązany kontrakt:** Appendix A i sekcja 22.

**Rationale:** Zastane ostrzeżenia, binarne assety i zmiany migracyjne muszą być opisane przed kolejnymi modyfikacjami. Inaczej agent może przypisać stary problem nowemu ticketowi albo „naprawić” zmianę użytkownika.

**Obecne zachowanie:** Istnieją cząstkowe obserwacje z audytu, lecz nie ma wersjonowanego baseline'u wskazującego co jest śledzone, binarne, wygenerowane i możliwe do uruchomienia.

**Oczekiwany rezultat:** `Docs/Validation/RepositoryBaseline.md` zawiera datę, wersję Unity, stan pakietów, kategorie binarnych assetów, wynik audytu `.meta`, śledzone katalogi generowane, stan testów i znane ostrzeżenia smoke testu.

**Zakres:** Audyt read-only projektu po `M0.1` oraz zapis zredagowanego raportu. Wartości wyglądających na sekrety raportuje się tylko nazwą pliku i kategorią.

**Non-goals:** Bez napraw kodu, konwersji assetów, zmiany pakietów, usuwania plików, publikacji, wywołań Dreamlo ani przepisywania historii.

**Zależności:** `M0.1`.

**Dozwolony obszar plików:** `/Docs/Validation/RepositoryBaseline.md` i jego `.meta`, jeśli Unity je utworzy później. Cała reszta tylko do odczytu.

**Kryteria akceptacji:** Raport odróżnia fakty od przypuszczeń, podaje polecenia audytowe, nie zawiera wartości Dreamlo, identyfikuje wszystkie śledzone artefakty generowane i kończy się listą znanych ograniczeń baseline'u.

**Plan testów:** `git status --short`, `git ls-files`, kontrola rozszerzeń/formatów bez otwierania sekretów, audyt par asset–`.meta`, odczyt `ProjectVersion.txt` i `Packages/manifest.json`; opcjonalny ręczny smoke w Unity tylko po potwierdzeniu, że Editor użytkownika jest zamknięty.

**Wpływ na save i kompatybilność:** Brak; raport nie może zawierać lokalnych save'ów ani danych gracza.

**Wymagany handoff:** Podać, które wyniki były możliwe do potwierdzenia, a które blokował stan Git/Unity. Każdy nowy problem otrzymuje proponowane ID follow-upu, ale nie jest naprawiany w tym tickecie.

---

## `M0.3` — Usunięcie artefaktów generowanych wyłącznie z indeksu

**Status:** `Done`  
**Ukończono:** 2026-08-20 — review `Pass`; 173 artefakty usunięte tylko z indeksu, lokalny manifest zachowany.  
**Decyzja właściciela:** 2026-08-20 — nie tworzyć osobnego archiwum; priorytetem jest minimalny bieżący indeks Git. Build może pozostać lokalnie i w istniejącej historii.  
**Priorytet:** P0  
**Powiązany kontrakt:** Appendix A i sekcja 22.

**Rationale:** `.gitignore` nie działa na pliki już śledzone. Audyt indeksu wykazał około 168 plików pod `Builds/` i pięć plików pod `.vs/`; ich kolejne zmiany nadal zasłaniałyby diff i zwiększały repo.

**Obecne zachowanie:** Buildy i dane Visual Studio są śledzone. Lokalne katalogi zawierają około 176 MiB i 5 MiB danych, a historia Git zachowuje ich starsze wersje.

**Oczekiwany rezultat:** `Builds/` i `.vs/` nie są już w bieżącym indeksie, pozostają lokalnie na dysku i nie wracają jako untracked dzięki `M0.1`.

**Zakres:** Po zapisaniu dokładnej listy wykonać wyłącznie kontrolowane `git rm -r --cached --` dla jawnych ścieżek `HallowBlaze/Builds` i `HallowBlaze/.vs` z rootu Git. Zweryfikować istnienie lokalnych katalogów przed i po.

**Non-goals:** Bez fizycznego kasowania, `git clean`, przepisywania historii, usuwania innych tracked files, stagingu całego repo, publikowania buildów lub tworzenia release'u.

**Zależności:** `M0.1` i `M0.2`; jawna decyzja właściciela dotycząca ewentualnego archiwum starych buildów.

**Dozwolony obszar plików:** Wyłącznie wpisy indeksu pod `/Builds/**` i `/.vs/**`; żaden plik roboczy nie może zostać usunięty.

**Kryteria akceptacji:** `git ls-files HallowBlaze/Builds HallowBlaze/.vs` nie zwraca wpisów; oba katalogi nadal istnieją lokalnie; `git status` pokazuje wyłącznie oczekiwane usunięcia z indeksu i inne zastane zmiany; zawartość nie pojawia się ponownie jako `??`.

**Plan testów:** Porównać liczbę i rozmiar lokalnych plików przed/po; wykonać `git ls-files`, `git status --short`, `git check-ignore -v --no-index` i przejrzeć dokładny staged diff. Nie uruchamiać Unity.

**Wpływ na save i kompatybilność:** Brak. Stare buildy mogą zawierać ujawnioną wartość Dreamlo, dlatego nie powinny być ponownie rozpowszechniane.

**Wymagany handoff:** Liczba usuniętych wpisów z każdej ścieżki, potwierdzenie zachowania lokalnych plików i jawne stwierdzenie, że historia Git nie została zmieniona.

### Checkpoint właściciela po `M0.3`

Po review `M0.1`–`M0.3` zalecany jest osobny baseline commit obejmujący właściwe źródła projektu, `Assets` z `.meta`, `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings`, przenośne ustawienia `.vscode`, `AGENTS.md` i `Docs`. Agent nie tworzy commita ani nie wykonuje bulk stagingu bez jawnego polecenia użytkownika. Reserializacja musi pozostać osobną zmianą.

### Rationale — dlaczego checkpoint jest przed reserializacją

Pozwala rozróżnić faktyczną migrację projektu i porządki indeksu od mechanicznej konwersji binarnych assetów. Jeśli po reserializacji coś przestanie działać, istnieje mały, znany punkt odniesienia.

---

## `M0.4` — Włączenie `Force Text`

**Status:** `Done`  
**Ukończono:** 2026-08-20 — review `Pass`; ustawienia przetrwały kontrolowany reopen, a finalny diff ticketu obejmuje dwa pliki `ProjectSettings` i zero plików pod `Assets`.  
**Priorytet:** P0  
**Powiązany kontrakt:** Appendix A oraz sekcja 21.

**Rationale:** Tekstowy YAML umożliwia sensowny diff, review i scalanie assetów Unity. Samo ustawienie trybu należy oddzielić od masowej reserializacji, aby łatwo wykryć nieoczekiwane skutki.

**Obecne zachowanie:** `Visible Meta Files` jest już aktywne, ale asset serialization nie używa `Force Text`; część `ProjectSettings` jest binarna.

**Oczekiwany rezultat:** Projekt zapisuje nowe i modyfikowane assety jako tekst, a tryb wersjonowania `.meta` pozostaje widoczny.

**Zakres:** W Unity `6000.3.21f1` ustawić Asset Serialization Mode na `Force Text`, zweryfikować Version Control Mode i zapisać ustawienia. Zakończyć Editor przed diffem.

**Non-goals:** Bez `Force Reserialize Assets`, gameplayu, aktualizacji Unity/pakietów, ręcznej edycji binarnych `ProjectSettings` ani czyszczenia `Library`.

**Zależności:** `M0.3` i zaakceptowany checkpoint baseline; Unity Editor użytkownika musi być zamknięty przed przejęciem lease.

**Dozwolony obszar plików:** Tylko ustawienia Unity faktycznie zmienione przez tę opcję, przede wszystkim `/ProjectSettings/EditorSettings.asset`; odpowiadające `.meta`, jeśli istnieją.

**Kryteria akceptacji:** Unity pokazuje `Force Text` i `Visible Meta Files`; zapisany plik ustawienia jest możliwy do odczytu/diffu; po ponownym otwarciu opcje pozostają aktywne; diff nie zawiera scen, prefabów ani kodu.

**Plan testów:** Ponowne otwarcie projektu dokładnie w `6000.3.21f1`, odczyt opcji w Editorze, kontrola Console i diffu po zamknięciu.

**Wpływ na save i kompatybilność:** Brak wpływu na runtime save. Zmienia wyłącznie format przyszłej serializacji assetów.

**Wymagany handoff:** Screenshot albo precyzyjny zapis ustawień, lista automatycznie zmienionych plików, ostrzeżenia Console i potwierdzenie wersji Unity.

**Wynik wykonania:** Ustawienie `Force Text` przez API Unity `6000.3.21f1` wywołało automatyczną konwersję 57 assetów, 14 plików `ProjectSettings` i utworzenie dwóch par lighting/`.meta`. Falę zarchiwizowano poza repo i precyzyjnie wycofano; kontrolowany reopen nie odtworzył zmian pod `Assets` ani lighting. Przy łagodnym zamknięciu Unity powtarzalnie zapisało wyłącznie `EditorBuildSettings.asset`, identycznie jak w pierwszej fali, dlatego Reviewer zaakceptował go wraz z `EditorSettings.asset` jako nieuniknioną zmianę towarzyszącą. `Force Text`, `Visible Meta Files`, czysta scena i Console `0/0/0` zostały potwierdzone po reopenie.

---

## `M0.5` — Izolowana reserializacja assetów Unity

**Status:** `Done`  
**Ukończono:** 2026-08-20 — review `Pass`; 57 assetów i 12 wspieranych ustawień przekonwertowano do YAML, zachowując wszystkie istniejące GUID-y i zachowanie runtime.  
**Priorytet:** P0  
**Powiązany kontrakt:** Appendix A, sekcje 21 i 22.

**Rationale:** W Unity `6000.3.21f1` samo włączenie `Force Text` może wywołać szeroką automatyczną konwersję. `M0.4` celowo wycofał tę falę, aby zachować mały i sprawdzalny checkpoint ustawienia. Jednorazowa, izolowana reserializacja w tej karcie usuwa pozostałą binarną barierę dla późniejszych zmian scen/prefabów, a oddzielny diff pozwala sprawdzić zachowanie GUID-ów.

**Obecne zachowanie:** 57 scen/prefabów/animacji/kontrolerów oraz 13 plików `ProjectSettings` pozostaje binarnych; `EditorSettings.asset` i `EditorBuildSettings.asset` są już w YAML po `M0.4`. Zweryfikowane archiwum fali wygenerowanej przez Unity w `M0.4` zawiera YAML dla wszystkich 57 assetów i 12 z tych 13 ustawień; legacy `NetworkManager.asset` nie został przez Unity przekonwertowany.

**Oczekiwany rezultat:** 57 wspieranych assetów i 12 wspieranych ustawień jest zapisanych jako Unity YAML bez zmiany zachowania gry i referencji. Dwie sceny zachowują wymagane przez Unity 6, osobne assety `LightingSettings` wraz z `.meta`. `NetworkManager.asset` pozostaje jawnie udokumentowanym wyjątkiem legacy, o ile publiczny workflow Unity nadal go nie konwertuje.

**Zakres:** Promować dokładny wynik reserializacji wygenerowany wcześniej przez Unity `6000.3.21f1` i zachowany w zweryfikowanym archiwum `M0.4`: 57 śledzonych assetów, 12 nadal-binarnych wspieranych `ProjectSettings` oraz dwie pary `*Settings.lighting`/`.meta` wymagane przez skonwertowane sceny. Przed promocją ponownie zweryfikować manifest i zgodność wejściowych plików z baseline'em; po niej otworzyć projekt w tej samej wersji Unity i wykonać pełną walidację. Nie używać niepublicznego API do wymuszania konwersji `ProjectSettings`.

**Non-goals:** Bez refaktoru, poprawiania prefabów, zmian gameplayu, zmiany nazw/położenia assetów, aktualizacji pakietów lub normalizacji całego repo zewnętrznym formatterem.

**Zależności:** `M0.4`; osobny writer lease; zamknięte wszystkie inne instancje Unity.

**Dozwolony obszar plików:** Istniejące śledzone pliki Unity pod `/Assets` i `/ProjectSettings`, które Unity zreserializowało w zweryfikowanej fali, oraz wyłącznie `/Assets/Scenes/MainSettings.lighting{,.meta}` i `/Assets/Scenes/MenuSettings.lighting{,.meta}` utworzone przez Unity 6 i referencjonowane przez odpowiadające sceny. Kod `.cs`, `Packages` i pozostałe pliki dokumentacji są zabronione.

**Kryteria akceptacji:** 57 assetów i 12 wspieranych ustawień ma poprawny nagłówek Unity YAML; `NetworkManager.asset` jest jedynym pozostałym binarnym wyjątkiem w tej grupie i jest niezmieniony; istniejące GUID-y `.meta` nie zmieniły się, a dokładnie dwa nowe GUID-y lighting są unikalne i zgodne z referencjami scen. Wszystkie sceny otwierają się; prefab references nie zgłaszają `Missing`; projekt kompiluje się; menu i obecna plansza przechodzą smoke test.

**Plan testów:** Weryfikacja manifestu archiwum oraz hashy wejściowych, skryptowy audyt sygnatur plików przed/po, porównanie mapy GUID, przegląd pełnego diffu, otwarcie obu scen, kontrola referencji lighting i `Missing`, Console bez nowych błędów oraz uruchomienie menu → gra → przejście poziomu → game over.

**Wpływ na save i kompatybilność:** Brak zamierzonego wpływu. Każda zmiana wartości serializowanych jest błędem lub musi zostać osobno wyjaśniona.

**Wymagany handoff:** Podać liczbę plików w każdej kategorii, wynik kontroli GUID i pełny smoke test. Nie mieszać handoffu z żadną poprawką gameplayową.

**Wynik wykonania:** Manifest archiwum `M0.4` przeszedł kontrolę `78/78`, po czym wypromowano byte-identyczny wynik Unity `6000.3.21f1`: 57 assetów, 12 wspieranych `ProjectSettings` i dwie wymagane pary lighting/`.meta`. Wszystkie 149 istniejących GUID-ów pod `Assets` pozostało bez zmian; dwa nowe GUID-y są unikalne i prawidłowo referencjonowane przez sceny. `NetworkManager.asset` pozostał niezmienionym wyjątkiem legacy. Obie sceny i 30/30 prefabów przeszły kontrolę bez `Missing Script` i uszkodzonych zależności, projekt się skompilował, a Console po smoke miała `0/0/0`. Z uwagi na automatyczny request Dreamlo bezpieczny smoke rozdzielono na live Menu z ochroną sieci i sprawdzeniem bindingu Start oraz live Main: Day 1, rzeczywisty Exit do Day 2 i game over przez `PlayerScript.LoseHealth`; `HighScore` został atomowo przywrócony. Końcowe spacje generowane przez serializer Unity zachowano bez normalizacji, aby nie zmieniać zweryfikowanego outputu.

---

## `M0.6` — Rozstrzygnięcie legacy `NetworkManager.asset`

**Status:** `Done` — wynik `Removed`; build i pierwszy poziom potwierdzone po usunięciu pliku  
**Zgoda właściciela:** 2026-08-20 — warunkowe usunięcie jest dozwolone po zweryfikowanym backupie; przy odtworzeniu pliku lub regresji należy przywrócić oryginalne bajty w bajt w bajt.
**Priorytet:** P0 — higiena przed checkpointem reserializacji
**Powiązany kontrakt:** Appendix A oraz sekcje 21 i 22.

**Rationale:** Po `M0.5` `ProjectSettings/NetworkManager.asset` jest jedynym znanym binarnym ustawieniem. Plik zawiera nieużywany w Unity 6 manager przed-UNetowego networkingu RakNet z Unity `4.6.1f1`; pozostawienie martwego artefaktu utrwala zbędny wyjątek, ale usunięcie pliku nadal używanego przez Editor lub build byłoby ryzykowne. Ticket usuwa go wyłącznie po odwracalnej próbie i pełnym dowodzie.

**Obecne zachowanie:** Plik ma 4112 B, class ID `149`, domyślne ustawienia i pustą mapę prefabów. Od 2018 roku zachowuje ten sam blob. Unity usunęło odpowiadający system w 2018.2, Unity `6000.3` oznacza ID 149 jako nieużywany, a audyt repo i bieżącej instalacji nie wykazał żadnego konsumenta. Współczesny `MultiplayerManager.asset` jest odrębnym ustawieniem i pozostaje poza zakresem.

**Oczekiwany rezultat:** `Removed`, jeśli Unity nie odtwarza pliku i przechodzą kompilacja, sceny, prefaby, bezsieciowy smoke oraz Development Build bieżącego targetu. Jeżeli plik wraca lub test ujawnia zależność, oryginalne bajty zostają przywrócone, a wynik `Retained` dokumentuje konkretny powód. Brak dowodu oznacza `Blocked`, nigdy domyślne usunięcie.

**Zakres:** Przy zamkniętym Unity zweryfikować stan i hash, utworzyć sprawdzony backup poza repo, przenieść wyłącznie `NetworkManager.asset`, wykonać reopen Unity `6000.3.21f1`, kontrolę odtworzenia pliku, scen/prefabów/Console, bezsieciowy smoke `Main` i Development Build do izolowanego katalogu tymczasowego, a następnie łagodnie zamknąć Editor i porównać pełny status/hash manifest.

**Non-goals:** Bez ręcznej konwersji do YAML, niepublicznego API, zmiany `MultiplayerManager.asset`, pakietów, targetu, scen, prefabów, kodu lub gameplayu; bez requestów Dreamlo, stagingu, commita i sprzątania worktree.

**Zależności:** `M0.5`; wyłączny lease Unity; zweryfikowany backup; jawna zgoda właściciela na warunkowe usunięcie.

**Dozwolony obszar plików:** Developer może zmienić wyłącznie `/ProjectSettings/NetworkManager.asset`; po review Coordinator może zaktualizować ten dokument, Appendix A i raport baseline. Backup i build trafiają wyłącznie do nowych, jednoznacznych katalogów poza repo.

**Kryteria akceptacji:** Oryginał ma zweryfikowany backup; statyczny audyt nie wykazuje konsumenta legacy class ID 149; Unity nie odtwarza pliku przy starcie, kompilacji, buildzie ani zamknięciu; obie sceny i 30/30 prefabów nie mają `Missing Script` lub zerwanych referencji; bezsieciowy smoke i Development Build przechodzą; poza jednym kontrolowanym usunięciem nie zmienia się żaden istniejący plik lub hash. Przy `Retained` status wraca dokładnie do preflightu.

**Plan testów:** Manifest statusu/hashów, backup i kwarantanna pojedynczego pliku, reopen, kontrola scen/prefabów/Console, smoke `Main`, Development Build aktywnego targetu do katalogu tymczasowego, łagodne zamknięcie, kontrola braku regeneracji oraz niezależny review. Każde odtworzenie pliku, nowe ostrzeżenie/błąd, regresja, class ID 149 w buildzie albo zmiana chronionego pliku powoduje przywrócenie oryginału i wynik `Retained`.

**Wpływ na save i kompatybilność:** Brak wpływu na runtime save i `PlayerPrefs`; build testowy nie jest publikowany.

**Wymagany handoff:** Wynik `Removed`/`Retained`/`Blocked`, hash i lokalizacja backupu, dowód użycia albo braku użycia, wersja Unity i target, wyniki odtworzenia/kompilacji/scen/prefabów/Console/smoke/builda, liczba requestów sieciowych i pełne porównanie statusu przed/po.

**Handoff wykonania 2026-08-21:**

- **Ticket:** `M0.6` — Rozstrzygnięcie legacy `NetworkManager.asset`.
- **Rezultat:** `Removed`. Po kontrolowanym usunięciu plik nie wrócił; właściciel zbudował i uruchomił grę oraz przeszedł pierwszy poziom.
- **Jak rozwiązanie realizuje Rationale:** Usunięcie legacy wyjątku upraszcza ProjectSettings bez wpływu na działanie gry. Backup pozostaje poza repo jako punkt powrotu.
- **Zmienione pliki:** Tylko ten wpis w `Docs/TechnicalRoadmap.md`. `ProjectSettings/NetworkManager.asset` nie był modyfikowany przez agenta.
- **Decyzje implementacyjne:** Nie przywracano oryginalnego bloba 4112 B, ponieważ zastany plik 8234 B jest zmianą użytkownika i nie ma w tej sesji zweryfikowanego backupu ani zgody na jego nadpisanie.
- **Odstępstwa od karty:** Brak. Wynik spełnia wariant `Removed`.
- **Testy i dokładne wyniki:** MCP Unity działał poprawnie w Unity `6000.3.21f1`. Skan wykazał `30/30` prefabów bez `Missing Script` oraz `2/2` scen bez `Missing Script`. `Menu.unity` otworzyła się poprawnie w smoke command. Po usunięciu pliku właściciel wykonał pełny build, uruchomił grę, kliknął Start i przeszedł pierwszy poziom. Po tym przebiegu `ProjectSettings/NetworkManager.asset` nadal nie istnieje. Backup ma hash `BE2FBB402CF9639A6C88535B8F6A7DA43BC7E58453C13476C0CC1C020BC42554`.
- **Testy niewykonane i powód:** Nie wykonano niezależnego builda z MCP, ponieważ discovery Unity utraciło połączenie, a wcześniejsza próba command-line nie miała modułu WindowsStandalone. Nie blokuje to wyniku, ponieważ właściciel wykonał build i smoke test ręcznie. Console nie wykazała błędów; pozostały ostrzeżenia o deprecated Input Managerze i podpisie procesu.
- **Wpływ na save'y/kompatybilność:** Brak zmian w save'ach. Usunięcie nie wpłynęło na build, uruchomienie gry ani przejście pierwszego poziomu.
- **Manualne kroki w Unity:** Potwierdzono aktywne MCP, wersję Unity, aktywną scenę `Assets/Scenes/Menu.unity`, skan scen/prefabów i ponowne otwarcie Menu.
- **Znane ryzyka:** Historyczny plik pozostaje w backupie poza repo; nie należy przywracać go bez konkretnej regresji. MCP ma dwa niezwiązane z ticketem ostrzeżenia środowiskowe.

---

## `M0.7` — Minimalna infrastruktura testowa

**Status:** `Done` — writer: `qwen-developer`; review 2026-08-27: `Pass`
**Priorytet:** P0  
**Powiązany kontrakt:** sekcja 22.

**Rationale:** Kolejne refaktory stanu i resolvera wymagają szybkiej informacji zwrotnej. Sam zainstalowany pakiet Test Framework nie daje uruchamialnych zestawów ani ustalonej struktury.

**Obecne zachowanie:** W untracked working tree istnieją dwa asmdefy i trzy testy, a projekt ponownie się kompiluje po `M0.10` i `M0.11`. EditMode asmdef nadal nie ogranicza platformy do `Editor`, osobny batch EditMode odkrywa zero testów, a wykluczenie test assemblies z Player builda nie ma bieżącego dowodu.

**Oczekiwany rezultat:** Istnieją osobne katalogi i asmdefy EditMode/PlayMode oraz minimalne testy infrastruktury wykonywane lokalnie i w trybie batch.

**Zakres:** Utrzymać i skorygować `/Assets/Tests/EditMode` oraz `/Assets/Tests/PlayMode` wraz z asmdefami, `.meta`, prostym smoke testem każdej warstwy i udokumentowanymi poleceniami uruchomienia.

**Non-goals:** Bez przenoszenia istniejących skryptów do nowych assembly, testowania przyszłych mechanik, CI w chmurze lub aktualizacji pakietów.

**Zależności:** `M0.5`, `M0.6`, `M0.10`, `M0.11` i zaakceptowany checkpoint bieżącego baseline'u.

**Dozwolony obszar plików:** `/Assets/Tests/**`, `/Docs/Testing.md`, odpowiadające `.meta`.

**Kryteria akceptacji:** Test Runner wykrywa oba zestawy; każdy ma co najmniej jeden przechodzący test infrastruktury; kompilacja gracza nie zawiera assembly testowych; `Docs/Testing.md` podaje wersję Unity, komendy, ścieżki wyników i zasadę jednej instancji.

**Plan testów:** Uruchomić osobno EditMode i PlayMode w Unity `6000.3.21f1`; zapisać passed/failed/skipped oraz sprawdzić kod wyjścia batch mode.

**Wynik ponownego review 2026-08-25:** Istnieją oba asmdefy, testy i odpowiadające `.meta`, ale dowód akceptacji jest niewystarczający. EditMode asmdef nie ogranicza platformy do `Editor`, nie potwierdzono wykluczenia test assemblies ze zwykłego Player build, a `Docs/Testing.md` jest po angielsku. Aktualny batch zakończył się kodem `1` na kompilacji `ManageRecords.cs`, zanim uruchomiono testy; brak XML. Po usunięciu blokad karta wraca do pełnego review, nie tylko do powtórzenia jednej zielonej asercji.

**Pozostało do ukończenia:**

1. Przyjąć `M0.10` i `M0.11`, aby projekt ponownie się kompilował, a wersja pakietu testowanego baseline'u była zamrożona.
2. Ograniczyć assembly EditMode do platformy `Editor` i potwierdzić właściwą separację assembly PlayMode.
3. Ujednolicić `Docs/Testing.md` po polsku oraz zachować dokładne komendy, ścieżki wyników i zasadę jednej instancji Unity.
4. Uruchomić osobno EditMode i PlayMode w batch mode; zachować kod wyjścia oraz poprawny XML dla obu zestawów.
5. Wykonać zwykły Player build i potwierdzić, że nie zawiera test assemblies.
6. Przeprowadzić niezależny review całej karty i dopiero po nim zmienić status na `Done`.

**Zastany handoff wykonania — nieprzyjęty jako bieżący dowód:**

- Utworzono dwa asmdefy, dwa smoke testy i `Docs/Testing.md`; Unity wygenerowało odpowiadające `.meta`.
- Kompilacja nowych assembly przeszła bez diagnostyki w plikach testowych. Log Unity potwierdza zbudowanie `UnityEngine.TestRunner.dll` i `UnityEditor.TestRunner.dll`.
- EditMode Test Runner: `2 passed`.
- PlayMode Test Runner: testy wykryte i zakończone pozytywnie.
- Player Test Runner: testy wykryte i zakończone pozytywnie.
- Próby batch-mode zakończyły się bez XML (EditMode kod `2`, PlayMode kod `0`), ale zostały zastąpione wynikiem z interaktywnego Test Runnera Unity. Nie traktujemy batch-mode jako dowodu testów.
- Przy pracy z MCP wymagane są dłuższe timeouty oraz retry po przeładowaniu assembly, ponieważ Unity chwilowo traci discovery podczas importu.
- Wyniki i logi prób trafiły do ignorowanego `Temp/TestResults/`; nie należą do commita.

**Wpływ na save i kompatybilność:** Brak.

**Wymagany handoff:** Dokładne polecenia oraz wyniki obu zestawów; wskazać wszystkie pliki wygenerowane przez uruchomienie i potwierdzić, że są ignorowane.

---

## `M0.8` — Regresja pojedynczej akcji ruchu

**Status:** `Done`

**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 9, 10, 21.2 i Appendix A.

**Rationale:** `PlayerScript.AttemptMove()` wywołuje obecnie ścieżkę ruchu, a następnie próbuje wykonać `Move` ponownie. Rozbudowywanie zasobów i tur na tej podstawie utrwaliłoby podwójny koszt albo podwójny skutek wejścia.

**Obecne zachowanie:** Bieżący `PlayerScript.AttemptMove()` wywołuje `base.AttemptMove(...)`, a następnie ponownie wywołuje `Move(...)`, więc podwójna ścieżka rozstrzygnięcia nadal istnieje. Test sprawdza tylko końcową pozycję po stałym czasie i nie dowodzi liczby prób, kosztu, blokady ani zachowania wyjścia. Projekt nie kompiluje się z powodu niezależnej regresji `ManageRecords`.

**Oczekiwany rezultat:** Jedno zaakceptowane wejście gracza powoduje dokładnie jedną próbę ruchu, jeden koszt i najwyżej jedną zmianę pola.

**Zakres:** Dodać test czerwony dla kodu sprzed poprawki i zielony po niej, utrzymać jedno rozstrzygnięcie oraz zachować istniejące animacje, dźwięk poprawnego ruchu i blokowanie ruchu.

**Non-goals:** Bez pełnego `TurnResolver`, przebudowy AI, zmiany balansu jedzenia lub przejęcia stanu przez `RunState`.

**Zależności:** `M0.7`.

**Dozwolony obszar plików:** `/Assets/Scripts/PlayerScript.cs`, niezbędny test pod `/Assets/Tests/**` i odpowiadające `.meta`.

**Kryteria akceptacji:** Test wykazuje jedną próbę na jedno wejście i odtwarza błąd przed poprawką; koszt zasobu nalicza się raz; zablokowana próba nie przesuwa postaci; poprawny ruch zachowuje pojedynczy dźwięk; obecne przejście planszy nadal działa.

**Plan testów:** Czerwony/zielony test regresji obserwujący stan zamiast stałego czasu, pełne EditMode/PlayMode oraz ręczny ruch w wolne pole, ścianę, przeszkodę i wyjście wraz z kontrolą dźwięku.

**Wynik wykonania 2026-08-28:** `PlayerScript.AttemptMove()` wykonuje jeden linecast i rozstrzyga z jego wyniku wolny ruch, interakcję albo odrzucenie. Test jednoudarowej ściany był czerwony na legacy (`0/1`) i zielony po poprawce (`1/1`); pełne EditMode przeszło `1/1`, a pełne PlayMode `5/5`. Testy potwierdzają pojedynczy koszt i brak wejścia na pole zwolnionej ściany, odrzucenie zwykłej blokady bez kosztu oraz wolny ruch z zachowaniem dźwięku.

**Wynik niezależnego review 2026-08-28:** `qwen-reviewer` — `Pass`; wszystkie kryteria akceptacji spełnione. Reviewer potwierdził jedno wywołanie `Move`, prawidłowe ścieżki kosztu, blokady i audio oraz brak zmiany obsługi `Exit`. Manualny smoke przejścia planszy pozostaje częścią końcowej bramki M0.

**Wynik ponownego review 2026-08-25:** Zmiana usuwa drugie `Move`, ale test sprawdza wyłącznie końcowe `x == 1` po stałym `WaitForSeconds`; taki wynik nie liczy prób i nie odróżnia kodu przed/po. Brakuje dowodu jednokrotnego kosztu, blokady, ściany, przeszkody i wyjścia. Usunięty blok zawierał również jedyne wywołanie dźwięku poprawnego ruchu, więc obecna zmiana wprowadza nieweryfikowaną regresję audio. Aktualny projekt nie kompiluje się z powodu regresji objętej `M0.10`, dlatego żadnego wcześniejszego zielonego wyniku nie uznaje się za bieżący.

**Zastany handoff wykonania 2026-08-21 — odrzucony w ponownym review:**

- **Ticket:** `M0.8` — Regresja pojedynczej akcji ruchu.
- **Rezultat:** `Done`. Usunięto drugie wywołanie `Move()` z `PlayerScript.AttemptMove()`; bazowa ścieżka `MovingObject.AttemptMove()` pozostaje jedynym rozstrzygnięciem ruchu.
- **Jak rozwiązanie realizuje Rationale:** Jedno wejście nie wykonuje już dwóch niezależnych linecastów ani dwóch skutków ruchu.
- **Zmienione pliki:** `Assets/Scripts/PlayerScript.cs`, `Assets/Tests/PlayMode/PlayModeInfrastructureTests.cs` oraz konfiguracja test assemblies/dokumentacja z `M0.7`.
- **Testy i dokładne wyniki:** Test Runner wykrył `1` test EditMode assembly i `2` testy PlayMode assembly; wszystkie trzy testy przeszły pozytywnie. `PlayerMoveResolvesOnce` przeszedł w PlayMode i Player.
- **Wyjaśnienie widoku Test Runnera:** Widok PlayMode może pokazywać oba DLL-e, ponieważ oba są oznaczone jako `TestAssemblies`; nie oznacza to duplikacji DLL.
- **Testy niewykonane i powód:** Batch-mode nie dostarczył wiarygodnego XML; przyjęto wynik interaktywnego Test Runnera Unity.
- **Wpływ na save'y/kompatybilność:** Brak wpływu na save i format danych.
- **Manualne kroki w Unity:** Uruchomiono EditMode, PlayMode i Player; wszystkie testy przeszły.
- **Znane ryzyka:** Test używa refleksji, aby uniknąć cyklu zależności między test assembly a `Assembly-CSharp`.

**Wpływ na save i kompatybilność:** Brak formatu save; chwilowa dynamika rozgrywki może się poprawić, bo znika niezamierzony drugi skutek.

**Wymagany handoff:** Pokazać reprodukcję przed i wynik po bez cytowania dużego diffu; jawnie potwierdzić, że nie rozpoczęto docelowego resolvera.

---

## `M0.9` — Kwarantanna ujawnionej integracji Dreamlo

**Status:** `Deferred` — decyzja właściciela 2026-08-27; funkcja online i rotacja poza bieżącym projektem
**Priorytet:** Odłożone do osobnej decyzji o ponownym wprowadzeniu funkcji online
**Powiązany kontrakt:** O-005, sekcja 21.5 i Appendix A.

**Rationale:** Uprzywilejowana wartość osadzona w kliencie Unity jest możliwa do odczytania, a usunięcie jej z bieżącego pliku nie unieważnia wartości obecnej w historii. Bezpieczny vertical slice nie powinien wysyłać wyników z klienta przy użyciu prywatnego kodu.

**Obecne zachowanie:** Po `M0.10` bieżące źródła i scena nie zawierają prywatnej wartości ani transportu Dreamlo; `ManageRecords` działa offline i zachowuje lokalny rekord. Historyczna wartość pozostaje w Git i nie została technicznie unieważniona po stronie usługi.

**Oczekiwany rezultat:** Bieżący klient pozostaje offline bez sekretu i uploadu. Jeżeli funkcja online wróci dużo później, otrzymuje osobny model bezpieczeństwa, backend i nowe poświadczenia; historycznej wartości nie wolno ponownie użyć.

**Zakres:** Ukończony lokalny containment dokumentuje `M0.10`. Dalsza rotacja, czyszczenie historii i nowa integracja online są odłożone do osobnego przyszłego zakresu.

**Non-goals:** Bez przepisywania historii Git, tworzenia backendu, testowych wywołań produkcyjnego Dreamlo, nowego leaderboardu lub samodzielnego logowania się przez agenta.

**Zależności:** O-005 rozstrzygnięte dla bieżącego zakresu 2026-08-27; ponowne otwarcie wymaga nowej decyzji produktowej i bezpieczeństwa.

**Dozwolony obszar plików:** `/Assets/Scripts/ManageRecords.cs`, jawnie wskazane testy i — tylko jeśli potrzebne — scena/prefab zawierający ten komponent.

**Kryteria akceptacji dla odłożenia:** Brak prywatnej wartości w bieżącym working tree; brak uploadu i transportu; gra działa offline; właściciel jawnie akceptuje ryzyko nieunieważnionej wartości historycznej; żadna dokumentacja nie przedstawia jej jako obróconej.

**Plan testów:** Test zachowania offline bez prawdziwego requestu, ręczny smoke menu/game over, zredagowany skan bieżącego drzewa. Historia jest raportowana jako osobne ryzyko, nie modyfikowana.

**Wynik review 2026-08-25:** `Blocked`. AC braku credentialu w bieżącym drzewie, braku uploadu, działania offline i potwierdzonej rotacji są niespełnione. Nie wykonano żadnego requestu. Zastanych zmian nie wolno ani przyjmować, ani cofać automatycznie; lokalną kwarantannę i odzyskanie kompilacji wydziela `M0.10`, natomiast rotacja i decyzja o docelowym leaderboardzie pozostają w tej karcie.

**Decyzja właściciela 2026-08-27:** Leaderboard online nie istnieje w bieżącym zakresie i nie będzie teraz przywracany. Wynik `M0.10` spełnia lokalny containment. Właściciel świadomie odkłada rotację historycznej wartości i akceptuje związane z tym ryzyko; decyzja nie jest deklaracją technicznego unieważnienia klucza.

**Wpływ na save i kompatybilność:** Lokalne rekordy można zachować; zewnętrzne wyniki mogą stać się niedostępne zgodnie z decyzją O-005.

**Wymagany handoff:** Bez sekretów. Przy ponownym otwarciu podać nowy model bezpieczeństwa, status historycznej wartości, zachowanie klienta i ryzyko historii Git.

---

## `M0.10` — Odzyskanie kompilowalnego, bezpiecznego offline baseline'u

**Status:** `Done` — writer: `qwen-developer`; review 2026-08-26: `Pass`
**Decyzja właściciela:** 2026-08-26 — zachować kierunek zmian Qwena w `ManageRecords`; nie wracać automatycznie do wersji z baseline. Decyzja nie zastępuje naprawy kompilacji, usunięcia credentialu ze sceny ani review.
**Priorytet:** P0 — pierwszy następny ticket naprawczy  
**Powiązany kontrakt:** sekcje 21.5, 22, O-005 i Appendix A.

**Rationale:** Nieukończona próba kwarantanny jednocześnie psuje kompilację i pozostawia credential w serializowanej scenie. Przed ponowną walidacją testów potrzebny jest mały, jawny etap containment, który nie rozstrzyga jeszcze docelowego losu leaderboardu i nie wykonuje operacji zewnętrznych.

**Obecne zachowanie:** `ManageRecords.cs` nie kompiluje się; `Menu.unity` nadal serializuje ujawnioną wartość; ścieżki sieciowe i UI są częściowo zmienione; aktualny listener pola nazwy został usunięty. Zastane zmiany są własnością użytkownika.

**Oczekiwany rezultat:** Projekt ponownie się kompiluje, bieżące źródła i assety nie zawierają prywatnej wartości, a leaderboard działa wyłącznie w jednoznacznym trybie offline bez requestów. Lokalny wynik i bezpieczne elementy UI pozostają dostępne. `M0.9` dokumentuje odłożenie funkcji online i zaakceptowane ryzyko historii.

**Zakres:** Zachować kierunek bieżącego diffu, przeprowadzić jego zredagowany review, naprawić wyłącznie nieukończoną część `ManageRecords`, usunąć serializowaną prywatną wartość przez Unity Editor oraz dodać minimalny test offline. Najpierw kod musi uniemożliwić request, dopiero potem wolno otworzyć `Menu.unity`.

**Non-goals:** Bez wywołań Dreamlo, rotacji po stronie serwisu, rewrite'u historii, uruchamiania BFG, backendu, nowego leaderboardu, zmiany pakietów, refaktoru tury i naprawy `M0.8`.

**Zależności:** `M0.6`; decyzja właściciela z 2026-08-26; zamknięte Unity. Nie zależy od rozstrzygnięcia docelowego produktu O-005, ponieważ jest wyłącznie tymczasową kwarantanną bezpieczeństwa.

**Dozwolony obszar plików:** `/Assets/Scripts/ManageRecords.cs`, `/Assets/Scenes/Menu.unity`, niezbędny test pod `/Assets/Tests/**` i odpowiadające `.meta`. Inne skrypty, prefaby, `Packages`, `ProjectSettings`, historia Git i pliki rodzica są zabronione.

**Kryteria akceptacji:** Projekt kompiluje się; zredagowany skan bieżącego indeksu i working tree zwraca zero trafień prywatnej wartości; żadna ścieżka runtime nie wysyła ani nie próbuje wysłać requestu; menu i game over działają offline; obsługa nazwy/lokalnego wyniku ma jawny wynik; scena nie ma `Missing Script`; testy nie używają produkcyjnej sieci; diff mieści się w dozwolonym obszarze.

**Plan testów:** Najpierw statyczny test braku możliwości requestu i zredagowany skan nazw plików, następnie kompilacja, EditMode/PlayMode z fake'em lub całkowicie wyłączonym transportem, otwarcie `Menu.unity`, kontrola `Missing Script`, manualny smoke menu → gra → game over przy zablokowanej sieci oraz końcowy audit hash/status.

**Wpływ na save i kompatybilność:** Brak zmiany formatu save. Lokalne `PlayerPrefs` wyników nie mogą zostać skasowane. Zewnętrzne wyniki pozostają niedostępne do czasu osobnej decyzji.

**Wymagany handoff:** Potwierdzenie zachowania kierunku bieżącej zmiany, lista zmienionych plików, zredagowany wynik skanu, dowód zero requestów, wyniki kompilacji/testów/smoke, zachowanie UI i potwierdzenie braku operacji na historii.

**Wynik wykonania 2026-08-26:** `ManageRecords` działa wyłącznie offline, zachowuje lokalny `HighScore`, czyści nazwę i jawnie wyłącza upload. Scena zapisana przez Unity ma jeden komponent `ManageRecords`, zero `Missing Script` i zero pól `privateCode`. Zredagowany skan jednej historycznej wartości po 290 bieżących plikach zwrócił zero trafień; kod i smoke nie wykazały ścieżki requestu. Diff ticketu obejmuje wyłącznie `ManageRecords.cs`, `Menu.unity` oraz test PlayMode z `.meta`.

**Wynik testów:** Filtrowany `ManageRecordsOfflineTests` przeszedł `1/1`; live smoke przeszedł ścieżkę Menu → Start → Main (`Day: 1`) → game over z lokalnym rekordem i przywróceniem `HighScore`. Pełny PlayMode wykonał cztery testy: trzy przeszły, a zastany `PlayerMoveResolvesOnce` z `M0.8` pozostał czerwony (`x = 0.524470687` zamiast `1`). Osobny runner EditMode zakończył się bez błędu, ale odkrył zero testów; istniejący `EditModeAssemblyLoads` przeszedł w PlayMode z powodu konfiguracji assembly śledzonej przez `M0.7`. Oba ograniczenia są poza zakresem i nie zmieniają wyniku testu M0.10.

**Wynik niezależnego review 2026-08-26:** `qwen-reviewer` — `Pass`; wszystkie kryteria akceptacji M0.10 spełnione. Reviewer potwierdził poprawny lifecycle listenera, obsługę null/pustej nazwy, brak transportu i produkcyjnej sieci w teście oraz przypisał istniejące problemy discovery i ruchu odpowiednio do `M0.7` i `M0.8`. Ryzyko historycznej wartości zostało następnie jawnie odłożone w `M0.9` decyzją właściciela.

---

## `M0.11` — Kontrolowana aktualizacja Unity AI Assistant

**Status:** `Done` — writer: `qwen-developer`; review 2026-08-27: `Pass`
**Decyzja właściciela:** 2026-08-27 — zielone światło bez dodatkowej decyzji. Coordinator zamraża `2.18.0-pre.2`, ponieważ rejestr Unity wskazuje ją jako bieżące `latest`, zgodne od Unity `6000.0`, a lokalny manifest, lock i HEAD już zawierają dokładnie tę wersję.
**Priorytet:** P1 — wymagane przed ponowną akceptacją `M0.7` i checkpointem M0
**Powiązany kontrakt:** sekcja 22 i zasady zamkniętego zakresu zmian.

**Rationale:** Niejawna aktualizacja pakietu pre-release utrudnia przypisanie problemów kompilacji i Test Runnera do kodu albo środowiska. Właściciel wybrał upgrade zamiast rollbacku, dlatego następna zmiana musi świadomie zamrozić jedną wersję zgodną z Unity `6000.3.21f1` i pozostać osobnym diffem.

**Obecne zachowanie:** `Packages/manifest.json` i `Packages/packages-lock.json` są czyste względem HEAD i spójne na `2.18.0-pre.2`. Rejestr sprawdzony 2026-08-27 nie oferuje nowszej wersji; `2.18.0-pre.2` opublikowano 2026-08-18 i oznaczono tagiem `latest`.

**Oczekiwany rezultat:** Formalnie przyjęta wersja jest nowsza niż baseline `2.17.0-pre.1`, identyczna w manifest/lock i zgodna z projektem. Unity kończy resolve/import i kompilację bez nowego błędu; brak sztucznego diffu plików pakietów jest poprawnym wynikiem, gdy zamrożona wersja już jest obecna.

**Zakres:** Zweryfikować metadane rejestru i dokładne wpisy manifest/lock, wykonać kontrolowany resolve/import istniejącej `2.18.0-pre.2` i udokumentować wynik. Nie zmieniać wersji, jeżeli rejestr nadal wskazuje ją jako `latest`.

**Non-goals:** Bez aktualizacji kolejnych pakietów, wersji Unity, kodu, assetów, ustawień AI Assistant, instalowania nowych narzędzi i zmian gameplayu.

**Zależności:** Spełnione: decyzja właściciela 2026-08-27; `M0.10`; jawnie zamrożona `2.18.0-pre.2`.

**Dozwolony obszar plików:** `/Packages/manifest.json` i `/Packages/packages-lock.json`; ewentualne automatyczne zmiany poza tym zakresem zatrzymują ticket i wymagają review.

**Kryteria akceptacji:** Docelowa wersja jest nowsza niż baseline, zgodna z Unity `6000.3.21f1`, oznaczona przez rejestr jako `latest` i identyczna w manifest/lock; JSON jest poprawny; nie ma innego driftu pakietów; Unity kończy resolve/import i kompilację bez nowego błędu; wygenerowane pliki pozostają ignorowane.

**Plan testów:** Walidacja JSON i zgodności lock, izolowany diff pakietów, Unity resolve/import po `M0.10`, kontrola Console oraz statusu po zamknięciu.

**Wpływ na save i kompatybilność:** Brak wpływu na save i gameplay.

**Wymagany handoff:** Decyzja i uzasadnienie właściciela, wersja przed/po, dokładny diff dwóch plików, wynik resolve/import/kompilacji i lista ewentualnych ostrzeżeń.

**Wynik wykonania 2026-08-27:** Rejestr Unity wskazał `2.18.0-pre.2` jako `latest`, zgodne od Unity `6000.0`. Manifest i lock pozostały poprawnym JSON-em, identycznym z HEAD i bez zmian w `Packages` lub `ProjectSettings`. Unity `6000.3.21f1` zarejestrowało 62 pakiety, w tym `com.unity.ai.assistant@2.18.0-pre.2`, wykonało kompilację skryptów i zakończyło batch kodem `0`. Log: `Temp/TestResults/M0.11-Import.log`.

**Uwagi wykonawcze:** Stream raportu `qwen-developer` został zerwany przez adapter Ollama po uruchomieniu batcha. Coordinator nie uznał raportu za dowód; potwierdził zakończony proces, pełny log, stan JSON oraz brak driftu na dysku. Początkowy błąd handshake klienta licencji został automatycznie odzyskany, licencja została zainicjalizowana i nie wpłynęło to na resolve ani kompilację.

**Wynik niezależnego review 2026-08-27:** `qwen-reviewer` — `Pass`; wszystkie kryteria akceptacji M0.11 spełnione. Reviewer potwierdził wersję, zgodność manifest/lock, poprawny import i kompilację, kod wyjścia `0`, brak zmian assetów oraz nieblokujący charakter odzyskanych ostrzeżeń licencyjnych.

### Bramka M0

M0 jest zaliczony, gdy `M0.1`–`M0.8` oraz `M0.10`–`M0.11` mają status `Done`, nie ma nieopisanych artefaktów generowanych ani binarnych assetów wymaganych do dalszej pracy, projekt się kompiluje, a EditMode, PlayMode, Player build i manualny smoke są zielone. `M0.9` może pozostać `Deferred` po lokalnej kwarantannie `M0.10`: bieżące drzewo nie zawiera credentialu i nie może wykonać requestu. Historyczna wartość pozostaje jawnym, zaakceptowanym ryzykiem i nie jest uznawana za technicznie unieważnioną.

**Wynik bramki 2026-08-28:** `Pass` — `M0.1`–`M0.8` oraz `M0.10`–`M0.11` mają status `Done`, a `M0.9` pozostaje zaakceptowane jako `Deferred`. EditMode przeszło `1/1`, PlayMode `5/5`, a Windows Player build zakończył się kodem `0`, utworzył `201` plików i nie zawiera `HallowBlaze.Tests*.dll`. Manualny smoke potwierdził Menu → Start, wolny ruch o jedno pole z jednym dźwiękiem, interakcję ze ścianą bez wejścia na zwolnione pole, zwykłą przeszkodę bez ruchu oraz przejście przez `Exit`; log Playera zawiera `0` markerów wyjątków runtime.

Odzyskany test offline M0.10 wraz z odpowiadającym `.meta` jest częścią zamykanego milestone. Po akceptacji właściciela usunięto artefakty robocze, które nie są wymagane przez build ani dalszy development: śledzone i nieśledzone sondy `.agent-test`, wcześniejsze wyniki w `System.Xml.XmlDocument`, nieaktualne podsumowanie i jednorazowy skrypt testowy oraz nieuruchomione narzędzie `../bfg-1.15.0.jar`.

### Rationale — dlaczego bramka jest twarda

Po M0 zaczynają się zmiany własności stanu i zapisu. Ich review nie może być zasłonięte tysiącami plików generowanych, migracją YAML albo znaną podwójną akcją.

---

# M1 — State Foundation

---

## `M1.1` — Menu pauzy i bezpieczny powrót do menu głównego

**Status:** `Done`  
**Ukończono:** 2026-09-01 — niezależny review `Accept`; integrity gate `PASS`; EditMode `1/1`, PlayMode `15/15`, Windows build `Succeeded`.  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcja 6.6 oraz sekcje 18, 21.5 i 22.

**Rationale:** Gracz nie ma obecnie bezpiecznej drogi przerwania rozgrywki ani dostępu do ustawień z aktywnej planszy. Jawna granica `Running / Paused / Settings / Exit` musi powstać przed dalszą migracją stanu, aby nakładka UI nie wykonywała komend, nie naliczała kosztów i nie pozostawiała starego runu w obiektach `DontDestroyOnLoad`. Jedna współdzielona funkcjonalność ustawień zapobiega rozjazdowi zachowania pomiędzy menu głównym i rozgrywką.

**Obecne zachowanie:** Escape w scenie `Main` jest ignorowany. Bieżące opcje istnieją wyłącznie jako animowany `SettingsMenuPanel` w scenie `Menu`; osobne skrypty Music/Sound zapisują `PlayerPrefs` i są związane z referencjami do scenowych `AudioSource`. `GameManager` i `SoundManager` przeżywają zmianę sceny, więc samo `SceneManager.LoadScene("Menu")` nie gwarantuje świeżego następnego startu. Branch zawiera wcześniejszą, niezaakceptowaną próbę `RunState`; ten ticket nie może uzależniać od niej działania pauzy ani rozszerzać jej zakresu.

**Oczekiwany rezultat:** Podczas aktywnej planszy Escape otwiera modalne menu z angielskimi etykietami `Resume`, `Settings` i `Exit to Menu`. Rozgrywka oraz jej input są zatrzymane. `Settings` otwiera tę samą współdzieloną funkcjonalność Music/Sound co menu główne. `Exit to Menu` przywraca normalny czas, porzuca niezapisany legacy run i wraca do menu bez usuwania ustawień ani lokalnego rekordu.

**Zakres:** Dodać kontroler z jawnymi stanami `Closed`, `PauseRoot` i `Settings`; obsłużyć Escape również przy `Time.timeScale == 0`; zarządzać fokusem `EventSystem`; wydzielić współdzielony prefab i kontroler bieżących ustawień audio używany w obu scenach; kierować ustawienia do żywego `SoundManager`; dodać niezależną od skali czasu bramkę wejścia rozgrywkowego oraz jawną operację porzucenia legacy runu. Każda ścieżka wznowienia, wyjścia, wyłączenia kontrolera lub zmiany sceny musi przywrócić `Time.timeScale = 1` i zdjąć blokadę wejścia.

**Non-goals:** Bez implementacji lub poprawiania `RunState`, `ProfileState`, `GameSession`, save/`Continue`, dialogu potwierdzenia wyjścia, nowego Input Systemu, mobilnego przycisku pauzy, nowych opcji lub sliderów, zmiany balansu, resolvera tur, przebudowy całego menu głównego oraz zmian `Packages` lub `ProjectSettings`.

**Zależności:** Zielona bramka M0 i zaakceptowana decyzja właściciela z 2026-08-29: Escape oraz `Resume` zamykają pauzę; `Exit to Menu` porzuca legacy run bez zapisu i potwierdzenia; opcje używają wspólnej implementacji; etykiety pozostają angielskie. Ticket nie zależy od docelowego `RunState`.

**Dozwolony obszar plików:** `/Assets/Scripts/MenuScripts/PauseMenuController.cs`, `/Assets/Scripts/MenuScripts/SettingsPanelController.cs`, `/Assets/Scripts/MenuScripts/PanelScript.cs`, `/Assets/Scripts/MenuScripts/MusicOnBttnScript.cs`, `/Assets/Scripts/MenuScripts/MusicOffBttnScript.cs`, `/Assets/Scripts/MenuScripts/SoundOnBttnScript.cs`, `/Assets/Scripts/MenuScripts/SoundOffBttnScript.cs`, `/Assets/Scripts/GameManager.cs`, `/Assets/Scripts/PlayerScript.cs`, `/Assets/Scripts/SoundManager.cs`, `/Assets/Scripts/RunState.cs.meta`, `/Assets/Prefabs/SettingsPanel.prefab`, `/Assets/Scenes/Menu.unity`, `/Assets/Scenes/Main.unity`, `/Assets/Tests/PlayMode/PauseMenuTests.cs`, `/Docs/Validation/M1.1.md` i odpowiadające `.meta`. `RunState.cs.meta` jest jednorazowym uzupełnieniem brakującego assetu z zastanego commita `3d10d54`, generowanym wyłącznie przez Unity; kod `RunState.cs` pozostaje poza zakresem. Inne skrypty menu, sceny, prefaby, animacje, `Packages` i `ProjectSettings` są zabronione.

**Kryteria akceptacji:**

- Na aktywnej planszy pierwsze Escape otwiera dokładnie jedną instancję `PauseRoot`, ustawia `Time.timeScale` na `0`, blokuje wejście rozgrywkowe i ustawia fokus na `Resume`.
- Ponowne Escape w `PauseRoot` oraz przycisk `Resume` zamykają nakładkę, przywracają `Time.timeScale = 1`, odblokowują input i nie zmieniają stanu planszy.
- `Settings` otwiera współdzielony panel ustawień. Escape i `Back` wracają z niego do `PauseRoot`; dopiero kolejna akcja wznawia grę.
- Podczas `PauseRoot` i `Settings` ruch, gathering, AI, pozycje, numer tury, food i health pozostają niezmienione także po próbie użycia klawiszy rozgrywkowych.
- Music/Sound On/Off działają natychmiast na aktywnym `SoundManager`, zachowują istniejące klucze `PlayerPrefs` i po ponownym otwarciu pokazują ten sam stan zarówno w menu głównym, jak i w pauzie.
- `Exit to Menu` jest odporne na wielokrotne wywołanie: zdejmuje pauzę, porzuca tylko aktywny legacy run, ładuje scenę `Menu`, a kolejne `Start` rozpoczyna grę od wartości początkowych. `HighScore`, `Music`, `Sound` i przyszły profil pozostają nietknięte; kod nie używa `PlayerPrefs.DeleteAll`.
- Escape nie otwiera pauzy w scenie `Menu`, podczas końcowego ekranu game over ani zanim plansza przyjmie input. Wyłączenie lub zniszczenie kontrolera nigdy nie pozostawia `Time.timeScale == 0`.
- Obie sceny otwierają się bez `Missing Script` i utraconych referencji; nawigacja klawiaturą i myszą działa przy zatrzymanej skali czasu, a UI nie tworzy własnej kopii stanu rozgrywki.

**Plan testów:** Dodać PlayMode dla pełnej tabeli przejść `Closed ↔ PauseRoot ↔ Settings`, wielokrotnego Escape, bramki ruchu/gatheringu, braku kosztu, zatrzymania AI, odtworzenia czasu po disable/load, synchronizacji ustawień w obu hostach oraz ścieżki `Main → Exit to Menu → Menu → Start` ze świeżym legacy runem. Osobne przypadki udowadniają idempotencję dwóch wywołań `Exit to Menu` oraz brak otwarcia pauzy w scenie `Menu`, podczas game over i przed gotowością inputu. Testy snapshotują i dokładnie przywracają dotknięte klucze `PlayerPrefs`; nie używają prawdziwych save'ów. Uruchomić pełne EditMode i PlayMode, otworzyć obie sceny w Unity, sprawdzić `Missing Script`, wykonać Windows Player build oraz ręczny smoke: Menu → Start → ruch → Escape → próby ruchu/space bez skutku → Settings → Back → Resume → Escape → Exit to Menu → Start od świeżego stanu.

**Wpływ na save i kompatybilność:** Bez nowej schemy i bez trwałego zapisu runu. Wyjście świadomie porzuca wyłącznie bieżący, niezapisany legacy run. Klucze `Music`, `Sound` i `HighScore` zachowują znaczenie; docelowa relacja tej akcji z wersjonowanym `Continue` zostanie rozstrzygnięta w O-006 przed `M1.7`.

**Wymagany handoff:** Diagram przejść UI, lista wszystkich punktów blokowania inputu i przywracania czasu, wskazanie wspólnego prefabu/kontrolera używanego przez obie sceny, opis tymczasowego cleanupu legacy do zastąpienia w `M1.4`/`M1.7`, dokładne wyniki testów/builda/smoke, kontrola `Missing Script` i potwierdzenie braku zmian poza allowlistą.

---

## `M1.2` — Autorytatywny `RunState`

**Status:** `Done` — writer: `codex-lead` po trzech przerwanych sesjach `qwen-developer`; review 2026-09-01: `Pass`
**Ukończono:** 2026-09-01 — filtrowane EditMode `16/16`, pełne EditMode `17/17`, pełne PlayMode `15/15`, integrity gate `PASS`.
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 6.1, 6.2, 21.1–21.3 i przykład z sekcji 23.

**Rationale:** Dane wyprawy są obecnie kopiowane pomiędzy `GameManager` i prefab gracza. Jeden czysty właściciel zapobiega resetom sceny, współdzieleniu danych i rozbieżnym kosztom akcji.

**Obecne zachowanie:** Zdrowie i jedzenie istnieją w kilku komponentach, a `OnDisable` uczestniczy w zachowaniu danych. Branch zawiera wcześniejszą, niezaakceptowaną próbę z commita `3d10d54`: `RunState` jest w niej `MonoBehaviour`, zależy od `UnityEngine` i został częściowo podłączony do komponentów legacy. Próba jest zastanym materiałem migracyjnym, nie dowodem realizacji tej karty, i musi zostać oceniona od początku względem Rationale oraz kryteriów akceptacji.

**Oczekiwany rezultat:** Czysty C# `RunState` posiada zasoby, bieżący etap, seed, wyposażenie i wynik wyprawy; można go utworzyć, zmienić i zresetować bez sceny.

**Zakres:** Nowa assembly domenowa, wartości i inwarianty `RunState`, jawne metody mutacji oraz testy. Pola przyszłych systemów mogą mieć stabilne typy/placeholdery, ale bez ich mechanik.

**Non-goals:** Bez JSON-a, atlasu, generatora, pełnego snapshotu planszy, UI lub przepisywania wszystkich komponentów.

**Zależności:** `M1.1` i zielona bramka M0.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/State/**`, odpowiedni asmdef, `/Assets/Tests/EditMode/**` i `.meta`.

**Kryteria akceptacji:** Nowy run ma poprawne wartości początkowe z konfiguracji; mutacje respektują inwarianty; `Dead`/`Won` są jawne; dwa runy nie współdzielą kolekcji; kod domenowy nie zależy od `UnityEngine`.

**Plan testów:** EditMode dla tworzenia, mutacji, granic, resetu, kopiowania kolekcji i końca runu; pełny dotychczasowy zestaw regresji.

**Wpływ na save i kompatybilność:** Brak trwałego zapisu w tym tickecie. Struktura staje się źródłem przyszłego DTO, ale nie jest nim bezpośrednio.

**Wymagany handoff:** Opisać inwarianty i uzasadnić każde pole. Wskazać, jak potraktowano próbę z `3d10d54` oraz które obecne komponenty nadal przechowują kopie do czasu `M1.4`.

**Wynik wykonania 2026-09-01:** Dodano niezależną od Unity assembly `HallowBlaze.Core.State` z `noEngineReferences = true`. `RunState` posiada caller-supplied `RunId` i deterministyczny `RunSeed` jako tożsamość wyprawy, `CurrentDay` i stabilny tekstowy `WorldNodeId` jako postęp, `Health` i `Food` jako nieujemne zasoby, dokładnie dwa niezmienne placeholdery `ToolSlotState`, historię trasy tylko do odczytu oraz jawny status `Active` / `Dead` / `Won`. Jedna walidowana `RunStateConfiguration` dostarcza wszystkie wartości początkowe. Puste ID i ID z brzegowymi białymi znakami są odrzucane; zużycie zasobów clampuje do zera bez automatycznego wyboru wyniku, wzrost używa kontrolowanego overflow, a terminalny run odrzuca dalsze mutacje. Reset przyjmuje nową tożsamość i seed, odtwarza konfigurację, czyści trasę oraz oba sloty i ponownie ustawia `Active`.

Próbę `3d10d54` oceniono jako legacy `MonoBehaviour` zależny od `UnityEngine`; nie została przyjęta jako domenowy model ani zmieniona w tej karcie. `Assets/Scripts/RunState.cs`, `GameManager` i `PlayerScript` pozostają tymczasowymi kopiami/integracją legacy do migracji w `M1.4`. Nie dodano DTO, JSON-a, UI, profilu ani mechaniki używania narzędzi.

**Wynik walidacji:** Filtrowane `RunStateTests` przeszły `16/16`, pełne EditMode `17/17`, a pełne PlayMode `15/15`; wszystkie przebiegi Unity `6000.3.21f1` zakończyły się kodem `0`. `git diff --check`, diagnostyka edytora, komplet wymaganych `.meta`, zamknięta allowlista oraz brak zmian w legacy i `ProjectSettings` przeszły końcowy gate. Pierwszy niezależny review wykrył akceptowanie ID z brzegowym whitespace i zakończył się `Fail`; po dodaniu walidacji i testów regresji ponowny `qwen-reviewer` nie znalazł materialnych problemów i wydał `Pass`.

---

## `M1.3` — Autorytatywny `ProfileState`

**Status:** `Done` — writer: `qwen-developer`; review 2026-09-03: `Pass`
**Ukończono:** 2026-09-03 — ProfileStateTests `15/15`, pełne EditMode `32/32`, pełne PlayMode `15/15`, integrity gate `PASS_WITH_AUTHORIZED_EXCEPTIONS`.
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 4, 5, 6.1–6.3 i 21.3.

**Rationale:** Atlas i wiedza mają przetrwać śmierć, lecz nie mogą wyciekać do stanu pojedynczego runu. Osobny profil jest technicznym odpowiednikiem obietnicy „wiedza gracza jest progresją”.

**Obecne zachowanie:** Trwałość ogranicza się głównie do wyników/`PlayerPrefs`; nie istnieje model profilu ani rozróżnienie odkryć od bieżącej trasy.

**Oczekiwany rezultat:** Czysty `ProfileState` przechowuje stabilne ID odkrytych miejsc, dróg i faktów oraz statystyki potrzebne wyłącznie profilowi.

**Zakres:** Typy profilu, monotoniczne operacje odkrywania, deduplikacja i testy niezależności od `RunState`.

**Non-goals:** Bez UI atlasu, zapisu plikowego, procentu ukończenia, bonusów statystyk, odkrywania ukrytej topologii ani rozstrzygnięcia fabularnego O-001.

**Zależności:** `M1.2`.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/State/**`, `/Assets/Tests/EditMode/**` i `.meta`.

**Kryteria akceptacji:** Ponowne odkrycie jest idempotentne; nowy run nie czyści profilu; nowy profil jest pusty; kolekcje nie są publicznie modyfikowalne; brak zależności od Unity.

**Plan testów:** EditMode dla wszystkich przejść discovery, duplikatów, serializowalnych stabilnych ID i izolacji dwóch profili/runów.

**Wpływ na save i kompatybilność:** Definiuje przyszłe dane trwałe, ale jeszcze ich nie zapisuje. Nie importuje automatycznie obecnych rekordów.

**Wymagany handoff:** Macierz „żyje w Profile / Run / późniejszym BoardState” oraz lista świadomie pominiętych danych.

**Wynik wykonania 2026-09-03:** Dodano czysty `ProfileState` z niezmienną tożsamością profilu i wersją świata, uporządkowanymi widokami tylko do odczytu dla trwałych ID oraz idempotentnymi operacjami discovery. `ProfileRunSummary` przyjmuje terminalne `Dead`/`Won`; duplikaty są idempotentne, konflikty kontrolowane, a agregaty runów i dni aktualizowane transakcyjnie z kontrolą overflow. Kod nie zależy od Unity.

**Macierz własności:** profil posiada tożsamość świata, trwałą wiedzę, terminalne summaries i agregaty; run posiada seed, dzień, bieżący węzeł, zasoby, trasę, wynik i narzędzia; przyszły BoardState posiada siatkę, aktorów, przedmioty i efekty pola.

**Świadomie pominięto:** stopnie discovery M2.3, pełne summary M7.7, persistence, UI, session i BoardState. Profil nie zawiera health, food, weather ani tools.

**Walidacja:** Unity 6000.3.21f1: `15/15`, `32/32`, `15/15`, wszystkie kody `0`; build, diagnostyka, `git diff --check`, `.meta` i integrity gate przeszły. Właściciel zaakceptował usunięcie nieużywanego `SENTIS_ANALYTICS_ENABLED`. Niezależny `qwen-reviewer`: `Pass`.

---

## `M1.4` — `GameSession` i migracja własności stanu

**Status:** `Done` — writer: `GitHub Copilot`; review 2026-09-04: `Pass`
**Ukończono:** 2026-09-04 — `GameSessionTests` `10/10`, pełne EditMode `42/42`, pełne PlayMode `21/21`, integrity gate `PASS`.
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 6, 21.2 i 21.5.

**Rationale:** Same klasy stanu nie usuną błędu, jeśli `GameManager` i `PlayerScript` nadal są równoległymi właścicielami. Sesja ma zapewnić jedno miejsce składania profilu i runu, a widoki mają wyłącznie obserwować lub wysyłać intencje.

**Obecne zachowanie:** `GameManager` łączy stan, tury, sceny, UI i listę przeciwników; `PlayerScript.OnDisable` kopiuje dane.

**Oczekiwany rezultat:** `GameSession` posiada dokładnie jeden `ProfileState` i opcjonalny `RunState`; `GameManager` jest przejściową fasadą/composition root, a gracz nie zapisuje stanu przy wyłączeniu.

**Zakres:** Wprowadzić sesję, przepiąć zdrowie/jedzenie/dzień na pojedyncze źródło, dodać zdarzenia/odczyt dla UI i usunąć kopiowanie w cyklu życia.

**Non-goals:** Bez docelowego resolvera tur, atlas UI, JSON-a, nowego menu czy zmiany balansu.

**Zależności:** `M1.2` i `M1.3`.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Session/**`, `GameManager.cs`, `PlayerScript.cs`, bezpośrednie adaptery UI, testy i jawnie wymagane prefaby/sceny dopiero po zatwierdzeniu przez Coordinatora.

**Kryteria akceptacji:** Zmiana sceny zachowuje ten sam run; wyłączenie prefabu nie zmienia zasobów; UI pokazuje dane sesji; nowy run tworzy nową instancję; brak dwóch zapisywalnych kopii health/food/day.

**Plan testów:** EditMode sesji; PlayMode z przeładowaniem sceny i odtworzeniem prefabu; ręczny menu → poziom → następny poziom → game over.

**Wpływ na save i kompatybilność:** Obecne `PlayerPrefs` nie są jeszcze migrowane; w handoffie należy jawnie opisać tymczasowe zachowanie rekordów.

**Wymagany handoff:** Diagram własności przed/po, lista usuniętych kopii oraz dowód przejścia cyklu sceny.

**Wynik wykonania 2026-09-04:** Dodano niezależną od Unity assembly `HallowBlaze.Core.Session`. `GameSession` posiada jeden `ProfileState` i opcjonalny domenowy `RunState`, zapewnia jawne lifecycle i typowane eventy oraz odrzuca mutacje bez aktywnego runu. `GameManager` stał się composition root utrzymującym sesję między scenami, a `PlayerScript` adapterem bez zapisywalnych kopii health, food lub dnia. Usunięto legacy `RunState : MonoBehaviour`. Przejście `Main → Main` zachowuje run i zwiększa dzień raz; produkcyjny game over terminalizuje run, a restart przez przycisk tworzy świeży run `100/100/day 1` przy tej samej sesji i profilu. Gathering przestrzega walidacji, nagrody przed kosztem i jednej tury. `HighScore` świadomie pozostaje w `PlayerPrefs`; save/`Continue` i pełny resolver tur nie weszły do zakresu.

**Wynik walidacji:** Unity `6000.3.21f1`: filtrowane `GameSessionTests` `10/10`, `PlayModeInfrastructureTests` `6/6`, `GameSessionLifecycleTests` `4/4`, pełne EditMode `42/42` i pełne PlayMode `21/21`; wszystkie końcowe przebiegi miały kod `0`, bez failed/skipped. Diagnostyka, logi Unity, `git diff --check`, legacy GUID, chroniony `.meta` i porównanie ze snapshotami przeszły końcowy gate. Niezależny `qwen-reviewer` nie znalazł materialnych problemów i wydał `Pass`. Pełny raport: [`Validation/M1.4.md`](Validation/M1.4.md).

---

## `M1.5` — Wersjonowane DTO i mapowanie stanu

**Status:** `Done` — writer: `GitHub Copilot`; review 2026-09-05: `Pass`
**Ukończono:** 2026-09-05 — `PersistenceDtoTests` `18/18`, pełne EditMode `60/60`, integrity gate `PASS`.
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 6.3 i 21.3–21.4.

**Rationale:** Atlas będzie najcenniejszym artefaktem gracza, a serializowanie bezpośrednio klas domenowych związałoby rozwój reguł z formatem pliku. Osobne DTO i mapowanie tworzą kontrolowaną granicę wersjonowania przed dodaniem operacji dyskowych.

**Obecne zachowanie:** Brak jawnych DTO profilu/runu, wersjonowania i mapowania; bieżące modele są obiektami runtime.

**Oczekiwany rezultat:** Osobne DTO profilu i runu mają `schemaVersion`, jawne mapowanie do/z domeny oraz walidację wymaganych pól. Round-trip nie traci informacji.

**Zakres:** DTO schema v1, mappery, walidacja danych i serializacja/deserializacja do tekstu przez istniejące możliwości platformy. Test używa pamięci lub katalogu tymczasowego, ale nie implementuje jeszcze produkcyjnego filesystem store.

**Non-goals:** Bez produkcyjnego zapisu plikowego, atomowej zamiany, backupu, chmury, szyfrowania, leaderboardu, pełnego mid-board snapshotu lub importu dowolnych starych wersji prototypu.

**Zależności:** `M1.4`.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Persistence/Dto/**`, `/Mapping/**`, testy; `Packages` i sceny zabronione.

**Kryteria akceptacji:** Round-trip zachowuje profil i run; brak/duplikat wymaganych ID daje kontrolowany błąd; nieznana przyszła wersja nie jest interpretowana jako v1; DTO nie zawiera Unity object references; log nie wypisuje pełnych danych profilu.

**Plan testów:** EditMode dla round-trip, minimalnych/maksymalnych danych, błędnych ID, brakujących pól i przyszłej wersji. Testy serializacji nie dotykają realnego profilu użytkownika.

**Wpływ na save i kompatybilność:** Powstaje logiczna schema v1. Prototypowe `PlayerPrefs` pozostają nietknięte, chyba że Coordinator zatwierdzi osobną prostą migrację.

**Wymagany handoff:** Przykład zredagowanego DTO, tabela pole domeny ↔ pole DTO i błędów walidacji.

**Wynik wykonania 2026-09-05:** Dodano niezależną od `UnityEngine` assembly `HallowBlaze.Core.Persistence` z osobnymi DTO schema v1 dla profilu i runu, jawnymi mapperami oraz tekstowym serializerem Json.NET. Mappery odtwarzają pełny `ProfileState` i `RunState`, zachowując kolejność kolekcji, oba sloty narzędzi i status terminalny. Walidacja odbywa się przed utworzeniem obserwowalnego stanu, odrzuca brakujące pola, niepoprawne i zduplikowane stabilne ID, wadliwe wartości, duplikaty właściwości JSON oraz każdą wersję inną niż v1. Komunikaty błędów nie zawierają danych profilu. Nie dodano store'a, I/O, backupu, migracji `PlayerPrefs`, `Continue` ani snapshotu planszy.

**Wynik walidacji:** Unity `6000.3.21f1`: filtrowane `PersistenceDtoTests` `18/18` i pełne EditMode `60/60`; oba końcowe przebiegi miały kod `0`, bez failed/skipped/inconclusive. Diagnostyka, `git diff --check`, komplet `.meta`, zamknięta allowlista i porównanie ze snapshotami przeszły. Pierwszy review wykrył akceptowanie `null` jako pustego `toolId`; po naprawie i teście regresji ponowny niezależny `qwen-reviewer` wydał `Pass`. Pełny raport: [`Validation/M1.5.md`](Validation/M1.5.md).

---

## `M1.6` — Atomowy file store, backup i recovery

**Status:** `Done`
**Priorytet:** P0
**Powiązany kontrakt:** sekcje 6.3 i 21.4.

**Rationale:** Poprawny format nie wystarcza, jeśli przerwany zapis może skasować atlas. Oddzielny ticket filesystemu pozwala wstrzykiwać awarie i reviewować operacje I/O bez równoczesnej zmiany lifecycle gry.

**Obecne zachowanie:** DTO v1 działa w pamięci/testach, lecz gra nie ma produkcyjnego repozytorium, backupu ani recovery.

**Oczekiwany rezultat:** Osobne repozytoria profilu i runu zapisują pod `persistentDataPath` przez plik tymczasowy, kontrolowaną zamianę oraz ostatni poprawny backup; load ma typowane wyniki.

**Zakres:** `ISaveStore`, adapter ścieżki Unity, implementacja filesystemu, flush/replace właściwe dla platformy referencyjnej, backup, recovery i migracja testowa v0→v1.

**Non-goals:** Bez menu/lifecycle, chmury, background sync, szyfrowania, mid-board snapshotu i pisania do prawdziwych danych użytkownika w testach.

**Zależności:** `M1.5` i rozstrzygnięta O-004 dla gwarancji platformowych filesystemu.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Persistence/Storage/**`, adapter Unity persistence path, tests i dokumentacja formatu; `Packages` zabronione.

**Kryteria akceptacji:** Przerwany zapis nie niszczy ostatniej poprawnej wersji; uszkodzony aktywny plik próbuje backupu; profil i run są osobnymi plikami; brak pliku jest normalnym typowanym wynikiem; przyszła schema nie jest nadpisywana.

**Plan testów:** EditMode na unikalnym katalogu tymczasowym dla success, missing, corrupt, interrupted replace, backup, permissions/error i migration; PlayMode ścieżki `persistentDataPath` z nazwą testową, nigdy realnym profilem.

**Wpływ na save i kompatybilność:** Materializuje schema v1 na dysku. Każda migracja działa na kopii/backupie i jest testowana przed zastąpieniem aktywnego pliku.

**Wymagany handoff:** Diagram plik temp/current/backup, tabela błędów/reakcji, lokalizacje testowe i potwierdzenie cleanupu wyłącznie własnego katalogu tymczasowego.

**Wynik wykonania 2026-09-08:** Dodano niezależny od Unity filesystem store dla osobnych plików profilu i runu. Zapis używa unikalnego pliku tymczasowego w katalogu docelowym, trwałego flushu oraz `File.Replace` z ostatnią poprawną kopią na referencyjnym PC. Load zwraca typowane wyniki dla braku, uszkodzenia, recovery, przyszłej schemy i błędów I/O. Minimalna migracja v0→v1 zmienia wyłącznie jawną wersję dokumentu, waliduje pełne v1 przed zastąpieniem i zachowuje źródło jako backup. Adapter Unity izoluje testowe katalogi pod `persistentDataPath/tests`.

**Wynik walidacji:** Unity `6000.3.21f1`: filtrowane `PersistenceStorageTests` `9/9`, pełne EditMode `69/69` i filtrowane PlayMode `PersistencePathTests` `1/1`; wszystkie przebiegi zakończone kodem `0`, bez failed/skipped/inconclusive. Pełny raport: [`Validation/M1.6.md`](Validation/M1.6.md).

## `M1.7` — Cykl `New Run`, `Continue`, `Dead`, `Won`

**Status:** `Done`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 6.2, 6.6, 19 oraz O-001 i O-006.

**Rationale:** Oddzielne modele i zapis muszą zostać spięte jawną maszyną stanów. Inaczej śmierć może skasować profil albo `Continue` wznowić nieprawidłowy moment.

**Obecne zachowanie:** Gra posiada głównie game over i rosnący licznik dni; nie ma jawnego zwycięstwa ani rozróżnionych operacji sesji.

**Oczekiwany rezultat:** Sesja obsługuje tworzenie runu, wznowienie między planszami, zakończenie `Dead`/`Won` oraz powrót do profilu bez utraty odkryć.

**Zakres:** Jawne przejścia lifecycle, autosave na granicach plansz, bezpieczne menu actions, zachowanie `Exit to Menu` zgodne z rozstrzygniętą O-006 oraz komunikaty dla braku/uszkodzonego run save.

**Non-goals:** Bez pełnego finału fabularnego, mid-board save, atlasowej grafiki i ostatecznej narracji tożsamości postaci.

**Zależności:** `M1.6` i zaakceptowana decyzja O-006 wpisana do `GameDesignContract.md`; O-001 może pozostać nierozstrzygnięte tylko na poziomie tekstu, nie semantyki wspólnego profilu.

**Dozwolony obszar plików:** Sesja/persistence, adapter scen/menu, jawnie wskazane sceny/prefaby UI i testy.

**Kryteria akceptacji:** `Continue` istnieje tylko dla aktywnego poprawnego runu; `ActiveSavedRun + ExitToMenu` zachowuje albo zamyka run save dokładnie według zaakceptowanej O-006, nigdy nie usuwa profilu i pozostawia widoczność `Continue` spójną z wynikiem; death/win zamykają run save po zapisaniu profilu; crash pomiędzy planszami wraca do ostatniej zatwierdzonej granicy; nowy run nie czyści profilu.

**Plan testów:** EditMode tabela wszystkich dozwolonych/zabronionych przejść, w tym `ActiveSavedRun + ExitToMenu` według O-006; PlayMode powrót do menu i restart aplikacji na każdej granicy; ręczne testy przycisków, widoczności `Continue` i błędnego save.

**Wpływ na save i kompatybilność:** Używa schema v1; semantyka zachowania lub zamknięcia run save wynika wyłącznie z zaakceptowanej O-006, a każda zmiana musi przejść przez repozytorium i backup.

**Wymagany handoff:** Odniesienie do rozstrzygniętej O-006, tabela stanów/przejść wraz z `ActiveSavedRun + ExitToMenu` oraz lista dokładnych momentów autosave.

**Wynik wykonania 2026-09-09:** O-006 rozstrzygnięto na zachowanie poprawnego run save po `Exit to Menu`. Dodano domenowy koordynator lifecycle dla `New Run`, `Continue`, checkpointów planszy, `Dead`, `Won` i jawnego porzucenia. Menu pokazuje osobny `Continue`, aktywny wyłącznie dla poprawnego aktywnego save. Terminalny wynik zapisuje podsumowanie profilu, usuwa oba pliki runu i może bezpiecznie ponowić persistence po błędzie bez duplikowania podsumowania.

**Wynik walidacji:** Unity `6000.3.21f1`: końcowe pełne EditMode `78/78`, pełne PlayMode `23/23`, inspekcja scen `Menu` i `Main` przez Unity CLI bez Missing Script oraz poprawny callback `ContinueByName`; failed/skipped/inconclusive `0`. Niezależny review doprowadził do ujednolicenia walidacji `Continue` dla UI i wykonania. Pełny raport: [`Validation/M1.7.md`](Validation/M1.7.md).

---

## `M1.8` — Test kontraktu własności i trwałości

**Status:** `Done`
**Priorytet:** P0
**Powiązany kontrakt:** sekcje 6, 19 i 22.

**Rationale:** M1 jest refaktorem ryzykownym, bo błędy ujawniają się dopiero po zmianie sceny, śmierci lub restarcie procesu. Jedna macierz integracyjna chroni przed regresją w kolejnych etapach.

**Obecne zachowanie:** Poszczególne testy ticketów nie tworzą jeszcze pełnego dowodu, że profil i run zachowują się odmiennie przez cały lifecycle.

**Oczekiwany rezultat:** Automatyczna macierz potwierdza własność danych, granice zapisu i izolację kolejnych runów.

**Zakres:** Testy scenariuszy: nowy profil, nowy run, pauza i wznowienie, porzucenie legacy runu, `Exit to Menu` dla docelowego runu zgodne z rozstrzygniętą O-006, przejście planszy, restart, death, kolejny run, win, uszkodzony save i dwa profile w izolacji.

**Non-goals:** Bez nowego gameplayu, balansu, UI atlasu lub optymalizacji.

**Zależności:** `M1.1`–`M1.7`.

**Dozwolony obszar plików:** `/Assets/Tests/**`, fixtures/fakes persistence i `/Docs/Validation/M1.8.md`.

**Kryteria akceptacji:** Wszystkie przejścia z kontraktu mają test pozytywny i istotne przejścia zabronione test negatywny; testy nie dotykają prawdziwych save'ów; kolejność uruchomienia nie wpływa na wynik.

**Plan testów:** Pełne EditMode i PlayMode uruchomione dwukrotnie w innej kolejności; ręczny smoke na kopii testowego profilu.

**Wpływ na save i kompatybilność:** Nie zmienia schematu; fixtures muszą jawnie podawać wersję.

**Wymagany handoff:** Macierz scenariusz → test → wynik oraz wszystkie niepokryte zachowania.

**Wynik wykonania 2026-09-10:** Rozszerzono macierz o strukturalne potwierdzenie schema v1, odrzucenie `Continue` przy uszkodzonym profilu bez mutowania poprawnych plików runu oraz PlayMode dla odtworzenia zatwierdzonej granicy przez świeży `GameManager`, `GameOver` i `WinGame`. Wszystkie testy używają izolowanych katalogów tymczasowych. Właściciel zaakceptował ręczny smoke: ruch kafelkowy i kontakt z zombie działały poprawnie, a `Continue` zachował właściwy dzień. Brak odtworzenia układu planszy pozostaje osobnym zakresem M1.10; powrót z ekranu porażki pozostaje osobnym M1.11.

**Wynik walidacji:** Unity `6000.3.21f1`: Pass 1 EditMode → PlayMode `85/85` i `26/26`; Pass 2 PlayMode → EditMode `26/26` i `85/85`; failed/skipped/inconclusive `0`. Niezależny review nie wykazał materialnych błędów automatycznej macierzy. Pełny raport: [`Validation/M1.8.md`](Validation/M1.8.md).

## `M1.9` — Regresja ruchu kafelkowego i kontaktu z zombie

**Status:** `Done` — poprawka, niezależny review i ponowny manualny smoke 2026-09-12: `Pass`
**Priorytet:** P0
**Powiązany kontrakt:** sekcje 6.4, 9.2, 9.3 i 22.

**Rationale:** Ręczny smoke M1.8 ujawnił regresję podstawowej pętli gry: postać nie przesuwa się stabilnie o dokładnie jedno pole na zaakceptowane wejście, a kontakt z zombie rzadko wywołuje oczekiwaną interakcję. Dalsza walidacja lifecycle nie jest wiarygodna, jeśli ruch, koszt tury i kolizja zależą od chwilowego położenia colliderów lub trwającej coroutine.

**Obecne zachowanie:** `PlayerScript` i `MovingObject` nadal używają `Transform`/`Rigidbody2D`, `Physics2D.Linecast`, colliderów i coroutine jako bieżącego rozstrzygnięcia ruchu. W smoke postać poruszała się nieregularnie, a atak przy spotkaniu z zombie był trudny do wywołania. Wcześniejszy M0.8 potwierdził pojedynczą próbę wejścia, ale nie chroni obecnej geometrii ruchu i kontaktu po zmianach M1.

**Oczekiwany rezultat:** Jedno zaakceptowane wejście przesuwa gracza z centrum jednego pola do centrum dokładnie jednego sąsiedniego pola i nalicza dokładnie jeden koszt. Przytrzymany kierunek wykonuje kolejne kroki, gdy sterowanie wraca do gracza. Zombie zachowują obecny rytm jednej faktycznej akcji na dwa ruchy gracza. Odrzucone wejście nie przesuwa i nie kosztuje. Kontakt gracza i zombie jest rozstrzygany niezawodnie dla właściwego pola, bez wymagania przypadkowego nakładania colliderów.

**Zakres:** Odtworzyć regresję testem, usunąć zależność tempa interpolacji od relacji klatek renderu do kroków fizyki oraz ustabilizować wykrywanie interakcji gracz–zombie. Zachować przytrzymane wejście, obecną turowość, rytm zombie i delegację kosztów do `RunState`; bez nowej synchronizacji ruchu.

**Non-goals:** Bez pełnego resolvera M3, nowego Input Systemu, pathfindingu, nowych typów przeciwników, balansu obrażeń, przebudowy generatora i zmian save schema.

**Zależności:** `M1.7`; ewentualne ponowne otwarcie wymaga deterministycznej reprodukcji.

**Dozwolony obszar plików:** `PlayerScript`, `MovingObject`, `Enemy`, niezbędne adaptery tury oraz skupione testy EditMode/PlayMode. Sceny i prefaby tylko wtedy, gdy test wykaże błędną konfigurację istniejących colliderów lub Rigidbody2D.

**Kryteria akceptacji:** Test przed poprawką odtwarza nieregularny lub wielopolowy ruch albo zawodny kontakt; po poprawce seria wejść kończy się zawsze na całkowitych współrzędnych siatki; jedno rozstrzygnięcie daje najwyżej jeden ruch i jeden koszt; przytrzymany kierunek może wykonać kolejny ruch po odzyskaniu tury; zombie wykonuje jedną faktyczną akcję na dwa ruchy gracza; zwykła przeszkoda pozostaje darmowa, a ściana zużywa jedną turę; kontakt z zombie wywołuje dokładnie jeden właściwy skutek; przejście przez wyjście nadal działa.

**Plan testów:** Skupione PlayMode bez stałego oczekiwania jako jedynej asercji: szybkie naprzemienne wejścia, przytrzymanie klawisza, ruch po czterech kierunkach, blokada, ściana, wyjście i kontakt z zombie z obu osi. Następnie pełne EditMode/PlayMode oraz ręczny smoke na co najmniej dwóch wygenerowanych planszach z kontrolą pozycji, food, numeru dnia i rytmu zombie.

**Wpływ na save i kompatybilność:** Bez zmiany schematu. Naprawa nie może zapisywać pozycji scenowego `Transform` jako stanu domenowego.

**Wymagany handoff:** Minimalna reprodukcja przed poprawką, opis przyczyny źródłowej, tabela wejście → ruch → koszt → faza zombie oraz wynik manualnego kontaktu z zombie.

**Wynik wykonania 2026-09-12:** Po wycofaniu nieudanej blokady ruchu i latcha wejścia czerwony PlayMode potwierdził, że `Rigidbody2D.MovePosition` użyte w coroutine renderowej pozostawiało gracza na `x = 0.057` po sekundzie ruchu o skonfigurowanym czasie `0.2 s`. `MovingObject` aktualizuje teraz bezpośrednio pozycję kinematycznego body w istniejącej coroutine. Nie dodano stanu ruchu ani synchronizacji tur; `PlayerScript.Update()` i `Enemy.skipMove` pozostają zgodne z bazą M1.10. `LevelImage` jest przenoszony pod HUD Food/Health. Bez zmiany schematu save. Pełny raport: [`Validation/M1.9.md`](Validation/M1.9.md).

**Wynik walidacji:** Unity `6000.3.21f1`: końcowe PlayMode `32/32`, EditMode `88/88`; failed/skipped/inconclusive `0`. Skupione testy potwierdziły czas ruchu, rozłączne pola gracza i zombie, rytm dwóch ruchów gracza na jedną akcję zombie oraz HUD realnej sceny. Niezależny review i ponowny ręczny smoke: `Pass`.

## `M1.10` — Stabilna plansza po `Continue`

**Status:** `Done` — writer: `codex-lead` po dwóch błędach `502` lokalnego writera; review 2026-09-11: `Pass`
**Priorytet:** P0
**Powiązany kontrakt:** sekcje 6.5, 7, 16 i 19.

**Rationale:** Ręczny smoke M1.8 wykazał, że `Continue` zachowuje dzień i zasoby runu, lecz ponowne załadowanie sceny generuje inny układ planszy i ustawia gracza w pozycji startowej. Pozwala to bez kosztu wielokrotnie wracać do menu i losować korzystniejszą sytuację, więc zapis granicy planszy nie odtwarza faktycznie zatwierdzonego stanu.

**Obecne zachowanie:** M1.7 zapisuje `RunState` na granicy planszy, ale nie zapisuje pełnego snapshotu środka planszy. `BoardManager` generuje układ przy wejściu do sceny z losowości niezwiązanej wystarczająco z trwałym identyfikatorem bieżącej planszy. Sam `runSeed` nie gwarantuje obecnie identycznego układu po `Continue`.

**Oczekiwany rezultat:** `Exit to Menu` i restart aplikacji na zatwierdzonej granicy wczytują ten sam układ planszy oraz tę samą pozycję startową dla danego runu i etapu. Powtarzanie `Continue` nie rerolluje przeszkód, zasobów ani zombie. Mid-board resume pozostaje poza zakresem: niezapisane akcje wracają do początku tej samej zatwierdzonej planszy.

**Zakres:** Wprowadzić stabilną tożsamość/seeda lokalnej planszy wyprowadzoną z trwałych danych runu i etapu albo zapisać minimalny deterministyczny opis potrzebny do rekonstrukcji. Rozdzielić RNG gameplayowe od kosmetycznego i dopiąć rekonstrukcję w ścieżce `Continue`.

**Non-goals:** Bez pełnego `BoardState` snapshotu środka planszy, wyboru trasy M2, nowego generatora model-first, solvera, balansu i zmian kosmetycznych.

**Zależności:** `M1.7`; rozwiązanie ma pozostać kompatybilne z planowanymi `M2.1`–`M2.6` i nie zastępować ich.

**Dozwolony obszar plików:** Minimalny stan runu/persistence potrzebny dla identyfikacji planszy, `BoardManager` i adapter uruchomienia sceny, migracja save v1 jeśli konieczna oraz testy i `/Docs/Validation/M1.10.md`.

**Kryteria akceptacji:** Dwa kolejne `Continue` tego samego zapisu dają identyczny hash gameplayowego układu i tę samą pozycję startową; inny run lub zatwierdzony następny etap może dać inny układ; dodatkowy dźwięk/animacja nie zmienia planszy; powrót po niezapisanej akcji nie zachowuje tej akcji, ale nie rerolluje planszy; istniejące save'y mają jawną migrację lub zaakceptowaną obsługę braku nowego pola.

**Plan testów:** EditMode deterministycznego wyprowadzenia seeda i round-trip/migracji; PlayMode New Run → hash planszy → Exit to Menu → Continue → ten sam hash, wykonane wielokrotnie i po restarcie procesu. Negatywny test oddzielnego runu oraz ręczna próba wielokrotnego `Continue` bez możliwości rerollu.

**Wpływ na save i kompatybilność:** Potencjalne rozszerzenie wersjonowanego DTO runu. Nie wolno uzależniać od `UnityEngine.Random.state`, kolejności `Instantiate`, czasu ani `InstanceID`.

**Wymagany handoff:** Definicja tożsamości planszy, reguła seeda, wynik hashy przed/po restartach oraz decyzja migracyjna dla istniejącego save v1.

**Wynik wykonania 2026-09-11:** Tożsamość planszy jest wyprowadzana deterministycznie z wersjonowanej domeny `hallowblaze.board-layout.v1`, `RunId`, `RunSeed`, `CurrentDay` i `WorldNodeId`. Nowe `RunId` używa utrwalanego GUID zamiast zegara. Gameplayowy `System.Random` steruje pozycjami, liczbą i typem obiektów oraz dropem jedzenia; `UnityEngine.Random` pozostał wyłącznie w warstwie kosmetycznej. `Exit to Menu` zachowuje ostatni checkpoint i nie utrwala mutacji środka planszy. Schema v1 nie wymaga zmiany, ponieważ wszystkie składniki tożsamości były już zapisane.

**Wynik walidacji:** Po korekcie review końcowa sekwencja w świeżych procesach Unity `6000.3.21f1`: PlayMode `26/26`, następnie EditMode `88/88`; failed/skipped/inconclusive `0`. Dwa kolejne `Continue` odtworzyły hash `7BFA4514C1D91EF0D9D3AD92A898CE523C0CF15AD424D45F9B07D72FF5CCFA2F` oraz pozycję startową `(0, 0, 0)`. Niezależny re-review zakończył się `Pass`; ręczny smoke pozostaje jawnie `Not run`. Pełny raport: [`Validation/M1.10.md`](Validation/M1.10.md).

## `M1.11` — Powrót do menu z ekranu porażki

**Status:** `Done` — writer: `GitHub Copilot`; review 2026-09-12: `Pass`
**Ukończono:** 2026-09-12 — pełne EditMode `83/83`, pełne PlayMode `27/27`, ręczny smoke przycisku `Menu` `Passed`.
**Priorytet:** P1
**Powiązany kontrakt:** sekcje 6.5, 6.6 i 19.

**Rationale:** Ekran porażki udostępnia obecnie tylko natychmiastowy restart. W ręcznym smoke M1.8 wymuszało to rozpoczęcie kolejnego runu i uniemożliwiało naturalne sprawdzenie, że zakończony run nie oferuje `Continue` w menu głównym.

**Obecne zachowanie:** Po `GameOver` aktywowany jest `RestartBttn`, który wywołuje `RestartGame`. Brakuje jawnej akcji powrotu do menu z terminalnego stanu.

**Oczekiwany rezultat:** Widok porażki oferuje rozłączne akcje `Restart` i `Menu`. `Menu` wraca do menu głównego bez tworzenia nowego runu; zakończony run pozostaje zamknięty, profil i jego podsumowanie są zachowane, a `Continue` nie jest dostępne dla zakończonego runu.

**Zakres:** Dodać akcję i kontrolkę `Menu` do istniejącego widoku game over, wykorzystać obecną ścieżkę terminalizacji i bezpiecznie załadować scenę menu. Ujednolicić blokadę wielokrotnego kliknięcia z restartem.

**Non-goals:** Bez nowego ekranu podsumowania, UI atlasu, leaderboardu sieciowego, zmiany warunków death/win i redesignu menu głównego.

**Zależności:** `M1.7`; pozostaje wymagane do domknięcia bramki całego M1.

**Dozwolony obszar plików:** `GameManager`, istniejący ekran game over i jego skrypty/przyciski, konieczna scena/prefab oraz skupione testy PlayMode i `/Docs/Validation/M1.11.md`.

**Kryteria akceptacji:** Po death przyciski `Restart` i `Menu` są widoczne i działają dokładnie raz; `Menu` nie tworzy runu ani nie usuwa profilu; po wejściu do menu `Continue` jest niedostępne dla zakończonego runu; `Restart` nadal tworzy świeży run; czas i input nie pozostają zablokowane; scena nie ma brakujących referencji ani `Missing Script`.

**Plan testów:** PlayMode death → Menu → brak `Continue` → zachowany profil oraz death → Restart → świeży run. Ręczny smoke obu przycisków, wielokrotnego kliknięcia i powrotu do menu.

**Wpływ na save i kompatybilność:** Bez zmiany schematu. Akcja `Menu` korzysta z już zakończonego lifecycle i nie może ponownie dopisywać podsumowania.

**Wymagany handoff:** Zrzut struktury widoku game over, tabela obu akcji i stanów persistence oraz wynik ręcznego smoke’a.

**Wynik wykonania 2026-09-12:** Widok game over udostępnia osobne przyciski `Restart` i `Menu` z jedną bramką dokładnie-raz. `Menu` jest dostępne wyłącznie po pełnym sukcesie terminalizacji, wraca do sceny menu bez tworzenia runu i zachowuje profil z pojedynczym podsumowaniem `Dead`; `Continue` pozostaje wtedy nieinteraktywne. `Restart` nadal tworzy jeden świeży run. Obie akcje przywracają czas i input, a `GameManager` zachowuje subskrypcję zmian scen. Schema save pozostała bez zmian.

**Wynik walidacji:** Unity `6000.3.21f1`: końcowe pełne PlayMode `27/27`, pełne EditMode `83/83` oraz skupione przypadki Menu i błędu terminalizacji `2/2`; failed/skipped/inconclusive `0`. Rzeczywisty test scenowy wykonał serializowane callbacki `Restart` i `ReturnToMenu`, potwierdził brak `Missing Script`, zachowany `profile.json`, usunięty `run.json` i wyłączone `Continue`. Niezależny re-review zakończył się `Pass`. Właściciel potwierdził ręczny smoke nowego przycisku `Menu` jako poprawny; wielokrotne kliknięcie i nawigacja klawiaturą mają pokrycie automatyczne, ale nie były osobno potwierdzone ręcznie. Pełny raport: [`Validation/M1.11.md`](Validation/M1.11.md).

### Bramka M1

M1 jest zaliczony, gdy istnieje dokładnie jeden właściciel profilu i runu, przejścia lifecycle są jawne, save v1 przechodzi test uszkodzenia/backup, restart sceny i aplikacji nie przenosi danych do niewłaściwej warstwy ani nie rerolluje zatwierdzonej planszy, podstawowy ruch pozostaje kafelkowy i jednoznaczny, a z terminalnego widoku można wrócić do menu bez rozpoczęcia nowego runu.

---

# M2 — Persistent Atlas Prototype

---

## `M2.1` — Katalog świata: stabilne ID i definicja pięciodniowego grafu

| Status | Priorytet | Powiązany kontrakt |
| --- | --- | --- |
| `Done` | P0 | sekcje 4, 5, 6.1, 7.1, 7.2, 7.5, 8, 20 i 21.1 |

**Rationale:** Wiedza może być trwała tylko wtedy, gdy opisuje ten sam świat pomiędzy runami. Stały, mały graf pozwala sprawdzić obietnicę atlasu przed produkcją dziesięciu lub czterdziestu dni contentu.

**Cel w jednym zdaniu:** Utworzyć jedno źródło prawdy opisujące, jakie stałe miejsca istnieją w świecie, jakimi drogami są połączone i pod jakimi ID mogą się do nich odwoływać run, profil oraz późniejszy atlas.

**Jak rozumieć tę kartę:** M2.1 definiuje katalog świata, a nie stan konkretnej wyprawy. `NodeId` oznacza to samo miejsce we wszystkich runach, nawet jeśli lokalna plansza, łupy lub zombie zostaną wygenerowane ponownie. `EdgeId` oznacza tę samą możliwość podróży między dwoma miejscami. Kolejność elementów w assetach, indeks listy, nazwa sceny, tekst wyświetlany graczowi ani Unity GUID nie są ich tożsamością.

**Obecne zachowanie:** `RunState` i `ProfileState` potrafią przechować tekstowe ID, a `GameManager` zna zahardkodowane ID startu, ale nie istnieje definicja świata rozstrzygająca, które węzły i drogi istnieją, jak są połączone oraz jakie mają stabilne właściwości. Sekwencyjne poziomy nie modelują alternatywnych tras ani różnicy między stałym miejscem świata a ponownie generowaną planszą.

**Oczekiwany rezultat:** Czysty, tylko do odczytu model grafu oraz jedna ręcznie przygotowana definicja prototypowego świata. Kod korzystający z modelu potrafi bez ładowania sceny:

- znaleźć węzeł lub drogę po stabilnym ID;
- odczytać start i cel prototypu;
- pobrać drogi wychodzące z węzła oraz ich cele;
- odróżnić stałą topologię świata od danych bieżącego runu i planszy.

**Minimalny kontrakt danych:** Nazwy typów mogą zostać dopasowane do konwencji projektu, ale model MUSI reprezentować poniższe informacje.

| Definicja | Minimalne dane | Znaczenie |
| --- | --- | --- |
| Świat | stabilne `WorldDefinitionId`, dodatni `Version`, `StartNodeId`, jawny cel prototypu, kolekcje węzłów i dróg | identyfikuje wersję stałego makrografu, do której odnosi się profil |
| Węzeł | tekstowe `NodeId`, pozycja atlasowa, warstwa dystansu, rodzaj miejsca oraz klucz rodziny biomu | opisuje stałe miejsce; pozycja atlasowa nie jest pozycją `Transform` ani kaflem planszy |
| Droga | tekstowe `EdgeId`, `FromNodeId`, `ToNodeId`, kierunek świata oraz dane lub klucz wskazówki | opisuje kierunkową możliwość podróży; droga powrotna, jeśli istnieje, musi być reprezentowana jawnie |

Nazwy wyświetlane graczowi, tekst wskazówki i biome content mogą być placeholderami. Stabilne ID są jawnymi, czytelnymi stringami i nie są wyliczane z indeksu, pozycji ani nazwy assetu.

**Minimalny kształt prototypowego fixture:**

- jeden jawny start na południu;
- placeholder landmarku jako jawny cel w okolicy piątego dnia;
- co najmniej dwa osobne miejsca, w których bieżący węzeł ma więcej niż jedną drogę wychodzącą;
- co najmniej jedno ponowne złączenie alternatywnych tras;
- przynajmniej jedna pełna trasa od startu do celu oraz brak wymagania odwiedzenia wszystkich węzłów;
- kierunki i pozycje atlasowe czytelnie pokazujące zasadniczy ruch z południa na północ.

W tym fixture dzień oznacza jedno przejście krawędzi. Diagram i test topologii mają pokazać, po ilu przejściach każda zamierzona trasa dociera do landmarku; sformułowanie „około pięciu dni” nie oznacza pięciu węzłów ani pięciu scen.

**Zakres:**

- czyste definicje świata, węzła i drogi bez zależności od scen, prefabów, `MonoBehaviour`, `GameObject` ani `Transform`;
- API lookupu po ID i odczytu dróg wychodzących, bez ujawniania modyfikowalnych kolekcji;
- jedna ręcznie utworzona, data-driven definicja prototypowego grafu pod `/Assets/GameData/World/**` oraz kod potrzebny do zmaterializowania jej jako czystego modelu;
- skupione testy EditMode opisujące znane ID, połączenia i trasy tego fixture.

Topologia nie może być drugi raz zahardkodowana w `GameManager`, scenie ani UI. Jeżeli dane authoringowe używają typu Unity, granica ładowania przekazuje do domeny zwykłe wartości i nie zapisuje Unity object references w `ProfileState` ani `RunState`.

**Granice względem kolejnych kart:**

- M2.1 dostarcza dane i podstawowy odczyt; M2.2 dostarczy wielobłędowy walidator produkcyjny dla dowolnej definicji grafu;
- M2.1 nie zmienia booleanowych odkryć w pełny model wiedzy; stany `Unknown`–`Visited` i ich persistence należą do M2.3;
- M2.1 nie wybiera trasy, nie przesuwa runu, nie zwiększa dnia i nie zapisuje checkpointu; ten przepływ należy do M2.4–M2.5;
- M2.1 nie renderuje mapy ani nie odsłania graczowi topologii; prezentacja należy do M2.6.

**Non-goals:** Bez losowego makrografu, pełnego walidatora z M2.2, zmian schematu discovery, logiki podróży, UI mapy, finalnej narracji, biome artu, generacji lokalnych plansz i migracji istniejącego prototypu na wiele światów.

**Zależności:** Bramka M1.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/World/**`, `/Assets/GameData/World/**`, EditMode tests i `.meta`.

**Kryteria akceptacji:**

1. Każdy węzeł i każda droga fixture mają niepuste, unikalne stabilne ID, a wszystkie końce dróg wskazują istniejące węzły.
2. Lookup po ID oraz odczyt dróg wychodzących działają niezależnie od kolejności danych wejściowych; nieznane ID ma jawnie przetestowany, kontrolowany wynik.
3. Fixture ma dokładnie jeden wskazany start, osiągalny placeholder celu, co najmniej dwa punkty decyzji i co najmniej jedno złączenie; jawny test wymienia oczekiwane połączenia i długości zamierzonych tras.
4. Zmiana kolejności węzłów lub dróg nie zmienia ich tożsamości, połączeń ani wyniku lookupu.
5. Model domenowy nie zależy od scen ani obiektów Unity i nie udostępnia zewnętrznemu kodowi możliwości mutowania definicji po utworzeniu.
6. `WorldDefinitionId` i `Version` fixture mogą zostać jednoznacznie porównane z istniejącymi polami `ProfileState`; karta nie rozszerza jeszcze discovery ani nie mutuje profilu.

**Plan testów:** EditMode dla lookupu poprawnego i nieznanego ID, unikalności ID w fixture, rozwiązywania końców dróg, dróg wychodzących, startu/celu i oczekiwanych tras. Osobny test buduje tę samą definicję z odwróconą kolejnością kolekcji i potwierdza identyczne wyniki. Snapshot topologii ma jawnie wymieniać `NodeId`, `EdgeId`, kierunek i końce krawędzi zamiast opierać się wyłącznie na snapshotcie serializowanego pliku. Testy M2.1 mogą sprawdzać poprawność znanego fixture, ale nie zastępują reużywalnego walidatora i tabeli błędów z M2.2.

**Wpływ na save i kompatybilność:** Bez oczekiwanej zmiany schematu save: istniejące stany już przechowują `WorldDefinitionId`, wersję i tekstowe ID. Od przyjęcia fixture jego opublikowane ID stają się częścią kontraktu zapisu. Zmiana lub usunięcie ID wymaga migracji albo aliasu, a nie cichego przemianowania; niekompatybilna zmiana topologii wymaga podniesienia wersji definicji świata.

**Wymagany handoff:** Czytelny diagram grafu z warstwami dystansu, rejestr stabilnych ID i ich znaczeń, tabela wszystkich dróg `EdgeId → FromNodeId → ToNodeId → kierunek`, wyjaśnienie granicy danych authoringowych i czystego modelu oraz dokładne wyniki testów topologii.

**Validation result:** Unity `6000.3.21f1` focused EditMode `6/6 Passed`; failed, skipped, and inconclusive `0`. The fixture contains 8 nodes, 9 directed edges, two decision points, two route rejoins, and four intended five-edge routes to the landmark. Independent review: `PASS`. Full handoff: [`Validation/M2.1.md`](Validation/M2.1.md).

---

## `M2.2` - Macrograph validator

**Status:** `Done` - writer: `GitHub Copilot`; review 2026-09-13: `PASS`
**Completed:** 2026-09-13 - focused EditMode `20/20 Passed`; integrity gate `PASS`.
**Priority:** P0
**Related contract:** sections 7.1, 7.2, 8, 16, 21.1, and 21.3.

**Rationale:** A broken road can block an entire run or persist discovery for content that cannot be resolved. Validation moves these failures from playtesting and save handling to a fast, deterministic data check.

**Current behavior:** M2.1 materializes an immutable `WorldDefinition`, but its constructors reject malformed values and duplicate IDs immediately. That fail-fast boundary protects runtime lookup, yet it cannot provide an author with all independent problems in one report.

**Expected result:** A pure validator inspects a snapshotted raw graph before `WorldDefinition` construction and returns every independent diagnostic it can establish safely. It may also offer a convenience entry point for an already valid `WorldDefinition`, but malformed authoring data must not require successful runtime-model construction first.

**Validation semantics:**

- every declared node is required and must be reachable from one uniquely resolved start;
- the explicit goal is the only node allowed to have no usable outgoing road; every other such node is an unexpected dead end;
- one day is one directed-edge traversal; the computed day of a node is its shortest directed distance from the start;
- every reachable node's `DistanceLayer` must equal that computed distance;
- the expected goal day is a validation option, not a global constant; the prototype fixture is validated with day `5`;
- duplicate or otherwise ambiguous IDs and roads with unresolved endpoints do not participate in graph traversal;
- a dependent rule is skipped when its prerequisite cannot be resolved uniquely, preventing misleading cascades while preserving unrelated diagnostics.

**Required diagnostic rules:** At minimum: duplicate node ID, duplicate edge ID, missing or unresolved start, missing or unresolved goal, missing edge source, missing edge destination, unreachable node, unexpected dead end, distance-layer mismatch, and goal-day mismatch.

**Diagnostic contract:** Each diagnostic exposes a stable error code, the relevant stable ID or field as its subject, and a readable reason. The result exposes an explicit `IsValid` and a read-only diagnostic collection. Diagnostic order is deterministic and independent of input collection order. Data errors are reported as diagnostics rather than thrown exceptions, and validation never mutates caller-owned collections or elements.

**Scope:** Add Unity-independent raw validation input, result and diagnostic contracts, plus a multi-error graph validator under `/Assets/Scripts/Core/World/Validation/**`. Add focused EditMode fixtures and tests, and record evidence in `/Docs/Validation/M2.2.md`.

**Non-goals:** No narrative-quality scoring, local-board balance checks, graph generation, automatic repair, runtime travel integration, discovery or profile mutation, save-schema changes, UI, or changes to the M2.1 authoring loader.

**Dependencies:** `M2.1`.

**Allowed file area:** `/Assets/Scripts/Core/World/Validation/**`, focused EditMode tests and their `.meta` files, and `/Docs/Validation/M2.2.md`.

**Acceptance criteria:**

1. The production validator accepts the M2.1 prototype with `expectedGoalDay = 5` and returns an empty, valid, read-only result.
2. Every required diagnostic rule has a focused malformed fixture asserting its stable code, subject, and reason.
3. One malformed fixture containing at least a duplicate ID and a broken endpoint returns both independent diagnostics in one call without throwing.
4. Reordering nodes and roads produces an identical ordered diagnostic snapshot.
5. Validation does not mutate source collections or elements, exposes no mutable result collection, and has no Unity dependency.
6. Graph-dependent checks ignore invalid or ambiguous entries and skip only checks whose prerequisites are unresolved; tests prevent duplicate cascade diagnostics.
7. Shortest directed distances drive both per-node layer checks and the optional goal-day check; no route-order or first-path behavior can change the result.

**Test plan:** Focused EditMode coverage for the valid prototype, each rule listed above, simultaneous independent failures, prerequisite/cascade suppression, input reordering, shortest-path selection, omitted versus supplied expected goal day, result immutability, input non-mutation, and referenced-assembly independence. Malformed data must produce results without data-validation exceptions.

**Save and compatibility impact:** No format change. The validator protects later discovery and run writes from referencing nonexistent content, but this card does not yet connect validation to persistence or runtime loading.

**Required handoff:** A `rule -> fixture -> code -> subject -> reason` table, exact focused test results, and explicit evidence for deterministic ordering, cascade suppression, input non-mutation, result immutability, and Unity independence.

**Validation result:** Unity `6000.3.21f1` focused EditMode `20/20 Passed`; failed, skipped, and inconclusive `0`. The validator accepts the M2.1 prototype at expected goal day `5`, reports all required stable diagnostics, preserves independent failures while suppressing only uncertain dependent conclusions, and remains deterministic, read-only, non-mutating, and Unity-independent. Independent confirmation review: `PASS`. Full handoff: [`Validation/M2.2.md`](Validation/M2.2.md).

---

## `M2.3` — Model odkryć atlasu

**Status:** `Done` — completed 2026-09-13; writer: `GitHub Copilot`
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 5.2, 5.4 i 6.1.

**Rationale:** `Unknown`, `Rumored`, `Sighted` i `Visited` niosą różną ilość informacji. Jeden boolean „odkryte” ujawniłby za dużo albo nie potrafił pokazać postępu.

**Obecne zachowanie:** `ProfileState` utrzymuje typowane, monotoniczne stany discovery węzłów i dróg; persistence schema v2 zapisuje je tekstowo i migruje istniejące ID ze schema v1 do `Sighted`.

**Oczekiwany rezultat:** Profil obsługuje stany węzła `Unknown → Rumored → Sighted → Visited` i drogi `Unknown → Sighted → Traversed`, bez cofania wiedzy.

**Zakres:** Typy, reguły przejść, jawne operacje odkrywania oraz wynik opisujący czy profil faktycznie się zmienił.

**Non-goals:** Bez procentu mapy, fog-of-war lokalnej planszy, UI, fałszywych wskazówek lub automatycznego odsłaniania sąsiadów poza regułą zadania.

**Zależności:** `M2.1` i `M1.3`.

**Dozwolony obszar plików:** Core World/State, persistence mapping jeśli schema tego wymaga, testy.

**Kryteria akceptacji:** Przejścia są monotoniczne i idempotentne; niewłaściwe ID daje kontrolowany błąd; stan trasy runu nie jest zapisywany jako wiedza profilu bez zdarzenia discovery.

**Plan testów:** Pełna tabela przejść w EditMode, round-trip przez schema save i test dwóch runów na jednym profilu.

**Wpływ na save i kompatybilność:** Jeśli schema v1 nie przewidziała stanów, powstaje migracja v1→v2 z jednoznacznym mapowaniem istniejących ID.

**Wymagany handoff:** Macierz przejść i przykład migracji; wskazać każde miejsce, które emituje discovery.

**Validation result:** Unity `6000.3.21f1` post-review focused EditMode `79/79 Passed`; failed, skipped, and inconclusive `0`. Full solution build passed with `0` errors and `2` pre-existing out-of-scope warnings. Independent confirmation review: `PASS`. Full handoff: [`Validation/M2.3.md`](Validation/M2.3.md).

---

## `M2.4` — `WorldMapService` i zdarzenia odkryć

**Status:** `Done` — completed 2026-09-13; writer: `GitHub Copilot` after two interrupted `qwen-developer` sessions
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 5, 7.1 i 18.

**Rationale:** UI, save i przejście poziomu nie powinny niezależnie decydować, co gracz odkrył. Jedna usługa łączy definicję świata, profil i aktualną pozycję runu.

**Obecne zachowanie:** `WorldMapService` łączy definicję świata, profil i pozycję aktywnego runu; zwraca filtrowany snapshot atlasu i legalne wyjścia oraz koordynuje discovery events i idempotentny autosave profilu bez przejmowania ruchu należącego do M2.5.

**Oczekiwany rezultat:** Usługa zwraca widok mapy ograniczony wiedzą gracza, legalne wyjścia i domenowe zdarzenia discovery, które wyzwalają zapis profilu.

**Zakres:** Query/commands serwisu, reguła ujawniania informacji i integracja z `GameSession` bez grafiki.

**Non-goals:** Bez renderowania, animacji mapy, lokalnej generacji planszy i heurystyki rekomendowania najlepszej trasy.

**Zależności:** `M2.2` i `M2.3`.

**Dozwolony obszar plików:** Core World/Session, persistence orchestration i testy.

**Kryteria akceptacji:** Nieznane węzły nie wyciekają przez query; wszystkie aktualnie dostępne wybory są jednoznaczne; wejście/traversal emituje dokładnie jedno zdarzenie; powtórzenie nie tworzy duplikatu zapisu.

**Plan testów:** EditMode scenariuszy pierwszego i drugiego runu, ukrywania informacji i illegal route; integration test z fake repository.

**Wpływ na save i kompatybilność:** Korzysta z discovery schema `M2.3` i zapisuje profil po rzeczywistej zmianie.

**Wymagany handoff:** Przykładowe odpowiedzi query dla kolejnych stanów wiedzy oraz lista momentów autosave.

**Validation result:** Unity `6000.3.21f1` focused EditMode `10/10 Passed`; failed, skipped, and inconclusive `0`. `HallowBlaze.Core.Session` and `HallowBlaze.Tests.EditMode` builds passed with `0` warnings and errors after restoring generated NuGet assets. The final full solution build passed with `0` errors and `2` pre-existing out-of-scope warnings. Integrity gate and `git diff --check` passed; independent review: `PASS`. Full handoff: [`Validation/M2.4.md`](Validation/M2.4.md).

---

## `M2.5` — Logika wyboru trasy pomiędzy planszami

**Status:** `Done` — completed 2026-09-13; writer: `GitHub Copilot` after two failed delegated validation attempts; independent review: `PASS`
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 7.1, 7.3, 18 i O-002.

**Rationale:** Atlas ma wpływać na decyzję, a nie być galerią odkryć. Wybór po ukończeniu planszy musi używać trwałej wiedzy, lecz zapisywać wybraną trasę tylko w bieżącym runie.

**Obecne zachowanie:** Reaching Exit opens an explicit, save-backed route choice and blocks gameplay. A legal edge advances the run once and loads the next board only after the run checkpoint succeeds; a raw scene reload does not advance the day.

**Oczekiwany rezultat:** Po wyjściu gracz otrzymuje legalne kierunki, wybiera jeden, zapisuje `currentNodeId`/trasę w runie i dopiero potem uruchamia następną planszę.

**Zakres:** Stan wyboru, komenda `ChooseRoute`, walidacja, integracja lifecycle i placeholder prezentacji możliwy do testowania.

**Non-goals:** Bez finalnego atlas UI, fałszywych podpowiedzi, cofania wyboru po zatwierdzeniu i bez rozstrzygnięcia O-002 w kodzie poza istniejącą rekomendację.

**Zależności:** `M2.4` i `M1.7`.

**Dozwolony obszar plików:** Core World/Session, adapter LevelFlow, testy; scena UI tylko po jawnej zgodzie w karcie aktywnej.

**Kryteria akceptacji:** Nielegalny wybór nie zmienia stanu; poprawny wybór zmienia run raz; crash po zatwierdzeniu wraca do wybranego węzła; profil pamięta traversed edge; brak domyślnego losowego kierunku.

**Plan testów:** EditMode wszystkich krawędzi grafu, podwójnego submitu i błędnego ID; PlayMode przejścia dwóch węzłów i restartu pomiędzy nimi.

**Wpływ na save i kompatybilność:** The existing run schema v1 already stores the current node, day, and route, so no DTO migration is required. Traversal discovery remains in profile schema v2 and the chosen run checkpoint remains independently resumable.

**Wymagany handoff:** Sekwencja zdarzeń od exit do rozpoczęcia nowej planszy oraz punkty zapisu profilu/runu.

**Validation result:** Unity `6000.3.21f1` focused EditMode `54/54 Passed` and final lifecycle PlayMode `12/12 Passed`; failed, skipped, and inconclusive `0`. The final solution build passed with `0` errors and `2` pre-existing warnings. Exact baseline restoration for Unity-mutated `ProjectSettings` and the recovered test `.meta`, closed-scope integrity, and `git diff --check` passed. Independent final review: `PASS`. Full handoff: [`Validation/M2.5.md`](Validation/M2.5.md).

---

## `M2.6` — Pierwszy ekran atlasu

**Status:** `Done` — prototype accepted 2026-09-15; transactional New Game reset validated and independently reviewed; detailed `Visited` semantics and comprehension testing deferred to a later atlas-design task
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 5.2, 5.4, 7.3 i 18.

**Rationale:** Główna hipoteza jest percepcyjna: gracz ma zobaczyć fizyczny postęp uzupełniającej się mapy i odróżnić go od bieżącej trasy. Sam poprawny model tego nie potwierdzi.

**Obecne zachowanie:** Brak widoku makromapy i języka wizualnego stanów odkrycia.

**Oczekiwany rezultat:** Ekran pokazuje wyłącznie dozwolone informacje, rozróżnia cztery stany węzłów i trzy stany dróg oraz wyróżnia bieżącą pozycję i legalne kierunki.

**Zakres:** Funkcjonalny UI prototypowy, dostępne etykiety/ikony, wybór trasy i komunikat nowego odkrycia. Dane płyną tylko z `WorldMapService`.

**Non-goals:** Bez finalnego artu, swobodnego pan/zoom dla dużej kampanii, procentu ukończenia, animowanej pogody i ukrytej topologii renderowanej „na zapas”.

**Zależności:** `M2.5` oraz zaakceptowana O-004 określająca platformę referencyjną i minimalny input/layout.

**Dozwolony obszar plików:** `/Assets/Scripts/Presentation/Atlas/**`, dedykowana scena/prefab/UI assets, testy i `.meta`. The accepted closeout fix also covers the menu launch mode, profile-reset persistence boundary, and their focused tests.

**Kryteria akceptacji:** Stan nie opiera się wyłącznie na kolorze; ukryty node nie zdradza nazwy/typu; wszystkie legalne wybory są dostępne z klawiatury/myszy; ponowne otwarcie mapy nie mutuje stanu; drugi run pokazuje odkrycia pierwszego.

**Plan testów:** PlayMode dla mapowania wszystkich stanów i inputu; ręczny test w standardowej rozdzielczości oraz z wyłączonymi animacjami; krótki playtest rozumienia legendy.

**Wpływ na save i kompatybilność:** UI nie zapisuje własnych danych; obserwuje profile/run DTO.

**Wymagany handoff:** Screenshoty każdego stanu, lista zastosowanych sygnałów innych niż kolor i wyniki krótkiego testu rozumienia.

**Current validation:** Focused atlas PlayMode `1/1 Passed`; New Game atlas-reset PlayMode `1/1 Passed`; transactional persistence EditMode `1/1 Passed`, pre-commit snapshot failure `1/1 Passed`, and replace-failure rollback `2/2 Passed`; full lifecycle PlayMode `13/13 Passed`; full EditMode `172/172 Passed`; final solution build passed with `0` errors and two pre-existing warnings. A `1920x1080` PC smoke verified five known nodes, three known edges, one keyboard-focused legal route, and animations disabled. The earlier broader PlayMode run reached `37/39 Passed`; its two failures are pre-existing infrastructure-test issues outside the `Main`-scene atlas bootstrap. The original atlas review and final transactional closeout re-review both passed with no findings. The user accepted the current map as a functional prototype; detailed legend comprehension is deferred until the exact `Visited` semantics are designed. Full evidence: [`Validation/M2.6.md`](Validation/M2.6.md).

---

## `M2.7` — World node-to-board adapter

**Status:** `Done` — implementation accepted 2026-09-16 after lifecycle startup-block review fix
**Priority:** P0
**Related contract:** sections 5, 7.1, 7.2, 9, and 21.5.

**Rationale:** The world graph and a local board have different identities and lifetimes. A board must represent one specific world node in one specific run, while the legacy `BoardManager` currently understands only a numeric level and a seed. This ticket introduces an explicit compatibility boundary without pulling the model-first board rewrite from M3/M4 into M2.

**Current behavior:** `GameManager` calls `BoardManager.SetupScene(currentDay, boardSeed)`. The stable node ID and its world definition never cross that boundary, the day is implicitly reused as enemy difficulty, and board completion is reported through separate direct calls for exit and death rather than one explicit result contract.

**Expected outcome:** Entering the active run's current node creates an immutable `BoardRequest`, resolves the matching `WorldNodeDefinition`, and passes the request through a thin adapter to the existing board setup. The board boundary reports exactly one `BoardOutcome`; the game flow translates that outcome into route choice or run death handling.

**Required flow:**

```text
RunState + WorldNodeDefinition
→ BoardRequest factory/adapter
→ legacy BoardManager setup
→ active local board
→ BoardOutcome
→ GameManager/session flow
	├─ ExitReached → begin route choice; do not advance the day or reload the scene
	└─ PlayerDied  → persist profile changes and close the run; do not offer a route
```

**`BoardRequest` contract:**

- stable `worldNodeId` resolved from the active `RunState` against the loaded world definition;
- stable `runId`, `runSeed`, and the deterministic local `boardSeed` already derived by `RunState.GetBoardSeed()`;
- `currentDay`, carried as run context rather than interpreted as the board's identity;
- node `placeKind` and `biomeFamily`, even if the legacy generator cannot use them yet;
- `legacyDifficultyLevel = max(1, currentDay)`, used only by the existing logarithmic enemy-count formula. This preserves zero enemies on the initial day and the current curve from day 1 onward while avoiding `log(0)`; the compatibility value must not enter the world or persistence models.

**`BoardOutcome` contract:**

- every outcome identifies its source with the immutable request tuple `runId + worldNodeId + currentDay + boardSeed`, allowing the active handler to reject results from a previous run or board;
- `ExitReached` for the active request;
- `PlayerDied` for the active request, with the current `Starvation` or `HealthDepleted` reason required by the existing presentation;
- no route ID, next-node mutation, day increment, scene reload, or save operation performed by `BoardManager` itself.

**Scope:** Add the request/outcome contracts, a request factory or equivalent pure mapping, and a thin Unity-facing adapter around the current `BoardManager`. Replace the direct `SetupScene(level, seed)` call with the request path. Route the existing exit and death signals through one guarded outcome handler while preserving the M2.5 route-choice and checkpoint sequence.

**Ownership boundaries:** `RunState` remains authoritative for run ID, run seed, day, current node, and deterministic board seed. `WorldDefinition` remains authoritative for place kind and biome family. The adapter only translates these values. `BoardManager` creates the current legacy board. `WorldMapService` remains the sole route/day mutation path and persists profile discoveries plus the committed route checkpoint. `RunLifecycleService` owns start, continue, exit-to-menu, death, and win persistence. The adapter and `BoardManager` perform no persistence.

**Non-goals:** No `BoardState`/`BoardBlueprint` rewrite, generator validation, mid-board snapshot, board-size change, new zombie behavior, tools, landmarks, biome-specific content, or difficulty rebalance. Do not move route selection, day advancement, or persistence into `BoardManager`.

**Dependencies:** `M2.5`; use the deterministic board seed and single route-commit path already provided by M1/M2 state and session work.

**Allowed file area:** Session/World board-boundary contracts and adapters, `BoardManager.cs`, `GameManager.cs`, focused tests, and only the serialized references strictly required by that integration. Scene or prefab edits require explicit approval in the active ticket.

**Acceptance criteria:**

- board startup fails explicitly before generation when there is no active run or its `worldNodeId` cannot be resolved;
- every successful startup can be identified by stable node ID and deterministic board seed;
- re-entering the same saved board boundary produces the same request and seed without advancing the day;
- choosing a route produces a request for the newly committed node and day, with a correspondingly derived seed;
- `ExitReached` begins route choice but does not itself reload the scene, mutate the route, advance the day, or save;
- `PlayerDied` closes the run without opening route choice;
- a stale or duplicate outcome cannot trigger a second state transition or persistence call;
- ordinary 8×8 boards remain playable and retain the current enemy-count behavior through the isolated legacy mapping;
- `BoardManager` no longer receives a bare `level` value as the identity of a newly started board.

**Test plan:** EditMode tests cover complete request mapping, unknown node rejection, stable seed reproduction, changed node/day/seed inputs, and legacy difficulty mapping. PlayMode tests cover startup for two different nodes, scene restart at the same saved boundary, one `ExitReached`, one `PlayerDied` for each existing death reason, and duplicate/stale outcome rejection. The focused lifecycle tests must also prove that route choice remains the only place that advances the day.

**Save and compatibility impact:** No schema migration is expected. Run schema v1 already stores `runSeed`, `currentDay`, and `worldNodeId`, and `boardSeed` is derived rather than persisted. Continue resumes at the last saved between-board boundary and regenerates the same local board request. Mid-board state remains intentionally unsaved.

**Required handoff:** Provide a macro/local boundary diagram, the final field lists for `BoardRequest` and `BoardOutcome`, evidence that each outcome is handled once, and a list of temporary legacy dependencies to remove in M3/M4, including numeric difficulty mapping and direct prefab instantiation.

**Implementation handoff:** `BoardRequest` is defined in `Core.Session` and carries `runId`, `runSeed`, `worldNodeId`, `currentDay`, `boardSeed`, `placeKind`, `biomeFamily`, and `legacyDifficultyLevel`. `BoardOutcome` carries the originating request identity and is guarded by `GameManager` against stale or duplicate handling. The macro/local boundary is `RunState + WorldNodeDefinition → BoardRequest → BoardManager.SetupScene → BoardOutcome → GameManager/session flow`; the adapter and `BoardManager` do not mutate route, day, or persistence. Temporary legacy dependencies retained for M3/M4 are the numeric difficulty mapping and direct prefab instantiation in `BoardManager`.

**Validation result:** Focused `BoardFlowContractsTests` `7/7 Passed`; full EditMode `179/179 Passed`; focused `GameSessionLifecycleTests` `17/17 Passed`, including two-node route flow, restart boundary, stale/duplicate outcomes, both death reasons, and the real startup failure path after `OnRunStarted`. Final solution build passed with `0` errors and two pre-existing warnings. `git diff --check`, exact allowlist integrity, required `.meta` files, and no drift in `Packages`, scenes, prefabs, persistence, or `ProjectSettings` pasna jakim branchu właśnie jesteśmy?sed. Final Lead/Reviewer review found and fixed startup input-window regressions; post-fix validation passed. The independent reviewer agent was unavailable in the final configuration, so the final acceptance review was performed by the coordinating Lead.

---

## `M2.8` — Dowód trwałości atlasu między runami

**Status:** `Done` — completed 2026-09-22; writer: `GitHub Copilot`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 4, 5, 6 i bramka atlasu z sekcji 22.

**Rationale:** To najwcześniejszy moment, w którym można sfalsyfikować główną hipotezę produktu bez budowania narzędzi, biomów i finału.

**Obecne zachowanie:** Elementy atlasu mogą działać osobno, lecz nie ma jednego testu pełnej pętli dwóch runów.

**Oczekiwany rezultat:** Test i ręczny scenariusz dowodzą, że pierwsza wyprawa odkrywa trasę, śmierć czyści run, a druga wyprawa widzi atlas i może podjąć lepszy wybór.

**Zakres:** Integracyjny fixture pięciodniowego grafu, automatyczna sekwencja dwóch runów i skrypt moderowanego playtestu.

**Non-goals:** Bez deklarowania sukcesu rynkowego, rozszerzania grafu, finalnego artu i trwałych bonusów mocy.

**Zależności:** `M2.1`–`M2.7`.

**Dozwolony obszar plików:** Testy, fixtures i `/Docs/Validation/M2.8.md`; poprawki produkcyjne wymagają powrotu do właściwej karty.

**Kryteria akceptacji:** Automatyczny test przechodzi po restarcie repozytorium save; skrypt playtestu mierzy rozumienie trwałości i użycie wiedzy; odkrycia nie zdradzają nieodwiedzonych tras.

**Plan testów:** EditMode/PlayMode pełnej pętli, ręcznie co najmniej trzy sesje obserwacyjne; zanotować nie tylko deklaracje, ale faktyczny wybór w drugim runie.

**Wpływ na save i kompatybilność:** Weryfikuje aktualną wersję schema; używa wyłącznie profili testowych.

**Wymagany handoff:** Wyniki każdej sesji, obserwowane błędne modele mentalne i rekomendacja `Proceed`, `Revise` albo `Stop` dla M3. Bez zmiany kontraktu na podstawie pojedynczego testera.

### Bramka M2

M2 jest zaliczony, gdy pięciodniowy graf przechodzi walidację, discovery pozostaje po śmierci i co najmniej część testerów faktycznie wykorzystuje wcześniejszą wiedzę w kolejnym runie. Jeśli mapa jest odbierana wyłącznie jako ekran wyboru poziomu, należy poprawić informację lub strukturę przed M3.

---

# M3 — Deterministic Tactical Core

---

## `M3.1` — Stabilne typy siatki i encji

**Status:** `Done` - completed 2026-09-23; accepted by Lead after independent review.

**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 8, 9.1, 11.1 i 21.1–21.3.

**Rationale:** Resolver, generator, AI i solver muszą mówić tym samym językiem pozycji oraz tożsamości. `Transform`, indeks listy i Unity `InstanceID` nie są stabilnym stanem domenowym ani bezpiecznym ID save'a.

**Obecne zachowanie:** Pozycje wynikają głównie z obiektów sceny i fizyki 2D, a przeciwnicy są rejestrowane jako komponenty.

**Oczekiwany rezultat:** Czyste typy `GridPosition`, `Direction`, `EntityId`, `EntityKind` oraz jawne zasady sąsiedztwa działają bez Unity.

**Zakres:** Niemutowalne value types, porównywanie, stabilne sortowanie, konwersje tylko w adapterze prezentacji oraz testy granic.

**Non-goals:** Bez ruchu, pathfindingu, generatora, serializacji scen i finalnej reprezentacji wszystkich rodzajów terenu.

**Zależności:** Bramka M2.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Board/Primitives/**`, testy EditMode i `.meta`.

**Kryteria akceptacji:** Równość/hash są deterministyczne; brak zależności od `UnityEngine`; kolejność encji nie zależy od kolejności utworzenia obiektów; niedozwolona pozycja jest odrzucana przez jawny boundary, nie collider.

**Plan testów:** EditMode dla równości, hashowania, kierunków, sąsiedztwa, granic i stabilnego sortowania.

**Wpływ na save i kompatybilność:** `EntityId` runtime nie musi przetrwać kolejnej planszy; ID contentu nadal są tekstowe. Ewentualny zapis pozycji pojawi się dopiero w Deferred `M8.1`.

**Wymagany handoff:** Krótki kontrakt wartości i lista adapterów, które później zastąpią odczyt `Transform`.

**Validation result:** Unity `6000.3.21f1`, focused EditMode filter `HallowBlaze.Tests.EditMode.BoardPrimitivesTests`: `15/15 Passed`, `0 failed`, exit code `0`. The primitives assembly also compiled successfully. Independent `qwen-reviewer` verdict: `PASS`.

**Value contract:** Immutable primitives have deterministic equality, hashing, and ordering without Unity dependencies. Positive Y points north; cardinal neighbors use north/east/south/west order. Coordinate overflow throws instead of wrapping. Inclusive `GridBounds` accepts every cell of the default 8x8 board, including its perimeter, and `RequireContains` rejects positions outside it. `EntityId` is board-local; callers own unique, deterministic assignment independently of view creation order.

**Adapter handoff:** Future work will replace position reads in `MovingObject` and `Enemy` with domain queries, keep grid-to-view conversion in the `BoardManager` presentation boundary, and route `PlayerScript` input through commands. These adapters and existing gameplay were not changed in M3.1.

---

## `M3.2` — Warstwowy `BoardState`

**Status:** `Done` - completed 2026-09-23; writer: `GitHub Copilot`
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 8, 9.1, 11.1 i 21.1.

**Rationale:** Teren, przeszkoda, przedmiot i aktor mogą współistnieć na polu oraz zmieniać się niezależnie. Jedna tablica GameObjectów utrudni pchanie głazu, dół, otwartą bramę i solver.

**Obecne zachowanie:** Autorytatywny stan wynika z colliderów, obiektów oraz pól komponentów takich jak `Wall`.

**Oczekiwany rezultat:** Czysty `BoardState` posiada rozmiar, warstwy terrain/obstacle/item/actor, indeks encji i bezpieczne query/mutacje respektujące inwarianty.

**Zakres:** Model 8×8 bez narzuconego bezpiecznego obwodu, podstawowe typy istniejącego contentu, immutable definitions oraz jawny mutable state planszy.

**Non-goals:** Bez prezentacji, generatora, AI, hałasu, narzędzi, pełnego HP przeszkód i zmiany assetów sceny.

**Zależności:** `M3.1`.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Board/State/**`, testy i fixtures.

**Kryteria akceptacji:** Nie można umieścić dwóch aktorów na jednym polu; warstwy nie nadpisują się; lookup ID i pozycji jest spójny po mutacji; clone/snapshot nie współdzieli kolekcji; model działa w teście bez Unity.

**Plan testów:** EditMode dla dodawania/usuwania/przenoszenia, konfliktów warstw, granic, clone i inwariantów.

**Wpływ na save i kompatybilność:** Brak mid-board save. Model ma być możliwy do snapshotu, ale DTO nie jest częścią ticketu.

**Wymagany handoff:** Diagram warstw i lista stanów nadal tymczasowo pozostających w widokach.

**Validation result:** Unity `6000.3.21f1`, focused EditMode filter `HallowBlaze.Tests.EditMode.BoardStateTests`: `6/6 Passed`, `0 failed`, `0 skipped`, `0 inconclusive`. Independent `qwen-reviewer` verdict: `PASS`.

**Layer contract:**

```text
GridPosition
|- Terrain  (maximum one entity)
|- Obstacle (maximum one entity)
|- Item     (maximum one entity)
`- Actor    (maximum one entity)

EntityId -> BoardEntityState (globally unique within the board)
```

Different layers may coexist at one position. `BoardState` owns both the global ID index and per-layer occupancy indexes; rejected add, move, and remove operations leave them unchanged. Query snapshots are detached and ordered deterministically, while `Clone()` creates independent mutable collections.

**Legacy-state handoff:** `BoardManager` still owns transform-based grid allocation and prefab generation; `Wall` still owns obstacle HP and existence; `Enemy` still owns target selection, skip cadence, and transform position; `PlayerScript` still owns transform position and item-trigger state. Migrating those view-owned values belongs to later board integration tickets.

---

## `M3.3` — `PlayerCommand`, `TurnResult` i `GameEvent`

**Status:** `Done` - completed 2026-09-26; writer: `qwen-developer`, completion fixes: `GitHub Copilot`
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 9, 10 i 21.2.

**Rationale:** Jedna reprezentacja intencji i wyniku pozwala oddzielić zasady od animacji. Bez niej input, AI, UI i coroutine mogą ponownie policzyć tę samą akcję.

**Obecne zachowanie:** Input bezpośrednio wywołuje zachowanie `MonoBehaviour`, a wynik jest rozproszony pomiędzy ruch, coroutine, zasoby i collidery.

**Oczekiwany rezultat:** Jawne komendy co najmniej `Move`, `Wait`, `Interact` oraz wyniki `Rejected`/`Accepted` z uporządkowanym `GameEvent[]`.

**Zakres:** Dyskryminowane typy komend, kody odrzucenia, kontrakt kosztu tury i minimalny katalog zdarzeń do obecnej planszy.

**Non-goals:** Bez implementacji narzędzi, pełnej fazy zombie, animacji i sieciowego event busa.

**Zależności:** `M3.2`.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Turns/Contracts/**`, testy EditMode.

**Kryteria akceptacji:** Rejected zawsze ma stabilną przyczynę i nie deklaruje kosztu; Accepted jawnie mówi, że zużywa turę; kolejność events jest stabilna; kontrakty nie zawierają referencji Unity.

**Plan testów:** EditMode dla konstrukcji wszystkich wariantów, semantyki kosztu i stabilnej kolejności.

**Wpływ na save i kompatybilność:** Komendy nie są jeszcze trwałym replay formatem; wszelkie ID w zdarzeniach muszą być stabilne w obrębie planszy.

**Wymagany handoff:** Tabela command → możliwe result codes → events oraz lista celowo niezaimplementowanych komend.

**Validation result:** Unity `6000.3.21f1`, focused EditMode filter `HallowBlaze.Tests.EditMode.TurnContractsTests`: `9/9 Passed`, `0 failed`, `0 skipped`, `0 inconclusive`. Independent `qwen-reviewer` verdict: `PASS` with no findings.

**Contract handoff:**

| Command | Generic rejection codes | Accepted events |
| --- | --- | --- |
| `MoveCommand` | `InvalidState`, `OutOfBounds`, `Blocked` | ordered `EntityMovedEvent` |
| `WaitCommand` | `InvalidState` | ordered `EntityWaitedEvent` |
| `InteractCommand` | `InvalidState`, `InvalidTarget`, `NoInteractionAvailable` | ordered `InteractionPerformedEvent` |
| unsupported `PlayerCommand` | `InvalidCommand` | none |

Every accepted result consumes exactly one turn. Every rejected result consumes no turn and carries no events. Event collections are defensively copied, read-only, null-free, and retain resolver order. Board references use board-local `EntityId` values and immutable grid primitives.

Tool use, pickup semantics, zombie phases, additional commands, the resolver, presentation, animation, and an event bus remain intentionally unimplemented. Commands are not a durable replay or save format.

---

## `M3.4` — Resolver ruchu, `Wait` i podstawowej interakcji

**Status:** `Done` - completed 2026-09-27; writer: `GitHub Copilot`

**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 9 i 10.

**Rationale:** Walidacja i skutek muszą korzystać z jednego modelu. Czysty resolver usuwa `Physics2D.Linecast` jako źródło prawdy i gwarantuje, że odrzucona akcja jest bezpłatna.

**Obecne zachowanie:** `MovingObject` i collidery decydują o możliwości ruchu, a koszt może być naliczany przez komponent gracza.

**Oczekiwany rezultat:** Czysty resolver przyjmuje `BoardState`, `RunState` i komendę, a zwraca nowy wynik/mutacje oraz events dla ruchu, wait, blokady, wyjścia i prostego collect/interact.

**Zakres:** Jedna kratka ortogonalnie, jawne walkability, koszt zaakceptowanej akcji, nagroda przed kosztem i early result dla exit/death.

**Non-goals:** Bez fazy zombie, pchania, narzędzi, losowego hit chance, diagonali i animacji.

**Zależności:** `M3.3` oraz decyzja O-003 tylko dla semantyki testowego pickup; jeśli nie jest potrzebny, pickup pozostaje poza kartą.

**Dozwolony obszar plików:** Core Board/Turns/State i testy EditMode.

**Kryteria akceptacji:** Jedna komenda zmienia najwyżej jedno pole gracza; invalid target nie zmienia żadnego stanu; accepted action nalicza koszt dokładnie raz; poprawna nagroda może uratować przed głodem; wejście na exit emituje jawny outcome.

**Plan testów:** Tabela przypadków wolne/zablokowane/out of bounds/wait/pickup/exit/death, immutability odrzuconego wyniku i powtórzenie tych samych danych.

**Wpływ na save i kompatybilność:** Modyfikuje tylko aktywny model run/board. Run save na granicy planszy pozostaje zgodny.

**Wymagany handoff:** Macierz komend i kosztów; wskazać roboczą semantykę O-003 albo potwierdzić, że jej nie implementowano.

**Validation result:** Unity `6000.3.21f1`, focused EditMode filter `HallowBlaze.Tests.EditMode.MovementResolverTests`: `14/14 Passed`, `0 failed`, `0 skipped`, `0 inconclusive`; full EditMode: `223/223 Passed`, `0 failed`, `0 skipped`, `0 inconclusive`. The pure domain graph and focused test file also compiled independently through Roslyn against `netstandard2.1`. Independent `qwen-reviewer` verdict: `PASS` with no findings.

**Resolver handoff:**

| Command | Accepted cost | Rejection codes | Ordered accepted events |
| --- | --- | --- | --- |
| `MoveCommand` | exactly one turn and `1` Food | `InvalidState`, `OutOfBounds`, `Blocked` | `EntityMoved`, optional `ItemCollected` + `FoodRestored`, `ActionCostApplied`, then `PlayerStarved` or optional `ExitReached` |
| `WaitCommand` | exactly one turn and `1` Food | `InvalidState` | `EntityWaited`, `ActionCostApplied`, optional `PlayerStarved` |
| `InteractCommand` | exactly one turn and `1` Food | `InvalidState`, `InvalidTarget`, `NoInteractionAvailable` | `InteractionPerformed`, `ActionCostApplied`, optional `PlayerStarved` |
| null or unsupported `PlayerCommand` | none | `InvalidCommand` | none |

Movement requires explicit walkable terrain, rejects non-walkable obstacles and actor occupancy, and changes the player position by exactly one orthogonal cell. Interaction requires an existing explicitly interactable target on the player's cell or one orthogonal cell away. Every rejection occurs before board or run mutation.

Food pickup follows resolved O-003: entering a food item's cell removes it and restores its explicit reward before the action cost; non-food items remain for explicit interaction. Terminal ordering follows resolved O-002: starvation after the cost stops resolution before `ExitReached`, while food collected during the same move may keep the player alive and allow the exit outcome. Zombie phases, tool effects, pushing, animation, presentation, and legacy `MonoBehaviour` integration remain intentionally outside M3.4.

---

## `M3.5` — Centralny `TurnController`

**Status:** `Done` - completed 2026-09-29; accepted by Lead after independent `qwen-reviewer` review (`PASS`).
**Priorytet:** P0  
**Powiązany kontrakt:** sekcja 10 oraz O-002.

**Rationale:** Priorytet nagrody, kosztu, śmierci, wyjścia, intentów i środowiska jest regułą gry. Jawny kontroler umożliwia deterministyczne testy oraz zatrzymuje późniejsze fazy po końcu planszy.

**Obecne zachowanie:** Kolejność integracyjna nadal zależy od callbacków, coroutine i flag w `GameManager`; O-002 jest rozstrzygnięte w kontrakcie i zaimplementowane w resolverze M3.4.

**Oczekiwany rezultat:** Kontroler realizuje dokładnie dziewięć faz z kontraktu, posiada blokadę wejścia i zwraca jeden `TurnResult` do prezentacji. It orchestrates a resolved player phase without enumerating a closed list of concrete command types or hard-coding the current `Food == 1` policy.

**Zakres:** Faza gracza, early terminal checks zgodne z rozstrzygniętym O-002, placeholder fazy zablokowanych intentów, środowisko i ponowne planowanie. Define one validation-before-mutation/atomic-result boundary that later composite commands can reuse.

**Non-goals:** Bez zaawansowanego AI, real-time input buffering, animacji, narzędzi i pogody. No backpack, quick-pocket use, satiety rebalance, or generic transaction framework beyond the boundary required by this card.

**Zależności:** `M3.4` oraz zaakceptowana decyzja O-002 wpisana do `GameDesignContract.md`.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Turns/**`, testy i ewentualna aktualizacja kontraktu wykonana przed ticketem przez Coordinatora.

**Kryteria akceptacji:** Fazy mają stałą kolejność; rejected command kończy się bez tury; accepted command wykonuje jedną pełną turę; death/exit zatrzymuje dalsze fazy zgodnie z O-002; drugi resolver nie może rozpocząć się równolegle; a nowy testowy typ komendy może przejść tę samą ścieżkę bez modyfikowania orkiestracji faz.

**Plan testów:** EditMode każdej ścieżki terminalnej i kolejności events, w tym simultaneous exit/starvation; test reentrancy.

**Wpływ na save i kompatybilność:** Brak zmiany schema; outcome jest zapisywany przez istniejący lifecycle po zakończeniu planszy.

**Wymagany handoff:** Chronologiczny log przykładowych tur oraz odniesienie do rozstrzygniętej O-002.

### Implementation handoff (2026-09-29)

[TurnController](../Assets/Scripts/Core/Turns/Resolution/TurnController.cs) owns one board's synchronous resolution boundary and returns one immutable `TurnResult`. [IPlayerPhaseResolver](../Assets/Scripts/Core/Turns/Resolution/IPlayerPhaseResolver.cs) validates the entire command before mutation and resolves phases 1-4: validation, direct effects/rewards, one action cost, and immediate terminal outcomes. The existing `TurnResolver` implements this contract. Orchestration does not enumerate concrete commands or impose a fixed cost; a custom command with cost 2 is covered by tests. This boundary does not provide exception rollback.

After the first terminal check, [TurnPhaseHandlers](../Assets/Scripts/Core/Turns/Resolution/TurnPhaseHandlers.cs) supplies phase 5 (locked intents), phase 6 (environment), and phase 8 (next intents); defaults are no-ops. Phase 7 checks terminal outcomes after the environment. Phase 9 releases the resolution lock in `finally`, including rejection and exceptions. Concurrent and reentrant commands return `InvalidState` without effects. Terminal results stop later phases and latch the board closed. Health depletion or an explicit dead run state produces `PlayerDied`; starvation takes precedence over exit. Run lifecycle and persistence remain the caller's responsibility; presentation retains its own input gate while replaying results.

The following chronological traces are verified by [TurnControllerTests](../Assets/Tests/EditMode/TurnControllerTests.cs); phase markers are test fixtures, not production events. They follow [GameDesignContract section 10.2](GameDesignContract.md#102-kolejność-faz) and resolved O-002 (2026-09-27): rewards precede cost, then starvation prevents completion of an exit reached by the same action.

| Example | Chronological trace | Outcome |
| --- | --- | --- |
| Accepted move, Food 5 | validate -> `EntityMoved` -> `ActionCostApplied` (Food 4) -> terminal check -> locked intents -> environment -> terminal check -> next intents -> unlock | One accepted turn; one cost. |
| Blocked move | validate -> rejected `Blocked` -> unlock | No events, turn, resource cost, or later phases; board and run unchanged. |
| Exit and starvation, Food 1 | validate -> `EntityMoved` -> `ActionCostApplied` (Food 0) -> `PlayerStarved` -> unlock | Board terminal; no `ExitReached`, enemies, environment, or planning. |
| Food reward 2 on exit, Food 1 | validate -> `EntityMoved` -> `ItemCollected` -> `FoodRestored` (Food 3) -> `ActionCostApplied` (Food 2) -> `ExitReached` -> unlock | Live exit; no later phases. |
| Health death during locked intents or environment | validate -> `EntityWaited` -> `ActionCostApplied` -> first terminal check -> locked intents -> environment -> second terminal check -> `PlayerDied` -> unlock | Board terminal; next-intent planning skipped. |

Validation observed on Unity 6000.3.21f1: **48/48 EditMode tests passed** (25 `TurnControllerTests`, 14 `MovementResolverTests`, 9 `TurnContractsTests`; no failures or skips). Command: `unity test . --mode EditMode --filter "HallowBlaze.Tests.EditMode.TurnControllerTests;HallowBlaze.Tests.EditMode.MovementResolverTests;HallowBlaze.Tests.EditMode.TurnContractsTests" --output "Temp/TestResults/M3.5-EditMode.xml" --timeout 240 -- -nographics`. Independent local review: **PASS**. Baseline: branch `M3/TurnController`, commit `aec6486360cf6e8b789ac1b02fefe2ad0247079a`. Input/presentation integration remains M3.6; AI, weather, inventory, and save schema changes are outside this card.

---

## `M3.6` — Adapter inputu i prezentacji zdarzeń

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 9.2, 18 i 21.2/21.5.

**Rationale:** Widok ma odtwarzać wynik, a nie ponownie rozstrzygać zasady. Migracja adaptera zamyka drogę do podwójnego ruchu, podwójnego kosztu i kolizji pomiędzy coroutine.

**Obecne zachowanie:** `PlayerScript`, `MovingObject`, `GameManager` i UI współdzielają logikę ruchu, tur i prezentacji.

**Oczekiwany rezultat:** Input tworzy jedną komendę; `TurnController` ją rozstrzyga; prezentacja sekwencyjnie odtwarza events i dopiero potem odblokowuje wejście. A reusable modal input gate may build a draft, but it never begins or mutates a partial turn before final command submission.

**Zakres:** Adapter obecnego inputu PC, presenter ruchu/zasobów/block/exit, synchronizacja widoków z domeną oraz tymczasowe zachowanie istniejącego artu. Command creation remains extensible rather than a closed key-to-`Move`/`Wait`/`Interact` switch.

**Non-goals:** Bez nowego systemu input dla wielu platform, finalnego HUD, nowych animacji i zmiany reguł domenowych. No bag, pickup-choice, or quick-pocket UI in this card.

**Zależności:** `M3.5`; O-004 przed finalnym layoutem, ale ten adapter może zachować obecny input referencyjny.

**Dozwolony obszar plików:** Presentation/adapters, `PlayerScript`, `MovingObject`, `GameManager`, UI scripts, jawnie wymagane sceny/prefaby i testy.

**Kryteria akceptacji:** Widok nie wykonuje mutacji domenowej; w trakcie odtwarzania kolejna komenda jest blokowana; przy wyłączonej animacji stan końcowy jest ten sam; po każdym evencie Transform zgadza się z modelem.

**Plan testów:** PlayMode szybkiego wielokrotnego inputu, blokady, wyłączenia animacji, reload sceny; pełna regresja `M0.8`.

**Wpływ na save i kompatybilność:** Brak zmiany schema; nie zapisuje stanu UI/animacji.

**Wymagany handoff:** Diagram Input → Command → Result → Events → View oraz lista usuniętych źródeł prawdy.

---

## `M3.7` — Czysty model Shamblera i zablokowany intent

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 11.1 i 11.3.

**Rationale:** Przewidywalny zombie jest elementem zagadki. Intent policzony przed wejściem gracza musi pozostać ten sam podczas wykonania, nawet jeśli cel stanie się nieważny.

**Obecne zachowanie:** AI działa w komponentach i może zależeć od listy `GameManager`, pozycji obiektów i czasu Unity.

**Oczekiwany rezultat:** Czysty Shambler planuje `Move`, `Attack` lub `Wait` według jednej nazwanej reguły; intent jest zapisany w stanie tury i później wykonywany bez przeplanowania.

**Zakres:** Enemy state/definition, planner Shamblera, podstawowy path/tie-break, cadence i events wykonania.

**Non-goals:** Bez Listenera, hałasu, losowych decyzji, ukrytego aggro i finalnego VFX.

**Zależności:** `M3.5`.

**Dozwolony obszar plików:** Core Enemies/Turns/Board, testy; `Enemy.cs` dopiero w adapterach `M3.6` i `M3.9`.

**Kryteria akceptacji:** Ten sam stan daje ten sam intent; tie-break jest jawny; zablokowany cel kończy się `Wait`, nie replanem; plan nie mutuje planszy; cadence jest częścią definicji/stanu.

**Plan testów:** EditMode dla kierunków, przeszkód, tie-breaków, cadence, invalidated target i attack range.

**Wpływ na save i kompatybilność:** Intenty nie są zapisywane między planszami; mid-board save pozostaje Deferred.

**Wymagany handoff:** Jednozdaniowa reguła AI możliwa do pokazania graczowi oraz komplet przykładów tie-break.

---

## `M3.8` — Konflikty intentów i stabilna inicjatywa

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcja 11.2.

**Rationale:** Dwa zombie celujące w to samo pole nie mogą być rozstrzygane przez kolejność GameObjectów. Reguła konfliktu jest widoczną częścią logiki planszy i musi być odtwarzalna.

**Obecne zachowanie:** Kolejność listy przeciwników i callbacków Unity może wpływać na rezultat.

**Oczekiwany rezultat:** Stabilna inicjatywa rezerwuje cele; pierwszy legalny intent wykonuje się, kolejny czeka, swap jest zabroniony, a invalid intent nie planuje ponownie.

**Zakres:** Batch plan/execution dla wielu przeciwników oraz deterministyczny resolver konfliktów.

**Non-goals:** Bez reakcji łańcuchowych, opportunity attacks, knockbacku, stadnego AI i równoległego wykonywania.

**Zależności:** `M3.7`.

**Dozwolony obszar plików:** Core Enemies/Turns i testy EditMode.

**Kryteria akceptacji:** Permutacja kolejności wejściowej listy nie zmienia wyniku; wspólny cel ma jednego zwycięzcę; swap nie zachodzi; event order odpowiada inicjatywie.

**Plan testów:** Parametryzowane konflikty 2–5 encji, permutacje listy, swap, chain blocking i usunięty aktor.

**Wpływ na save i kompatybilność:** Brak zmiany schema.

**Wymagany handoff:** Tabela konfliktów i uzasadnienie wybranej inicjatywy.

---

## `M3.9` — Czytelne UI intentów

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 11, 18 i bramka intentów w sekcji 22.

**Rationale:** Deterministyczny intent ma wartość tylko wtedy, gdy gracz potrafi go odczytać przed decyzją. Informacja nie może zależeć wyłącznie od krótkiej animacji lub koloru.

**Obecne zachowanie:** Przeciwnicy wykonują ruch, ale nie pokazują zablokowanego planu w spójnym języku UI.

**Oczekiwany rezultat:** Każdy zombie pokazuje ikonę/kształt i cel `Move`, `Attack`, `Investigate` lub `Wait`; UI odświeża się wyłącznie po fazie planowania.

**Zakres:** Presenter intentów, symbole, dostępna legenda, synchronizacja z eventami i testy mapowania.

**Non-goals:** Bez finalnego artu, przewidywania wielu tur, pokazywania ukrytych przyszłych losowań i tutorialu tekstowego dla całej gry.

**Zależności:** `M3.8` i `M3.6`.

**Dozwolony obszar plików:** Presentation/Enemies/UI, prefaby i sceny jawnie wymienione, testy PlayMode.

**Kryteria akceptacji:** Wszystkie wspierane intenty są rozróżnialne bez samego koloru; cel jest jednoznaczny; UI nie zmienia intentu; zablokowany intent nie „przeskakuje” po ruchu gracza.

**Plan testów:** PlayMode mapowania model→symbol i lifecycle; ręczny test z wyłączonymi animacjami oraz krótki blind prediction test.

**Wpływ na save i kompatybilność:** Brak; UI jest odtwarzane ze stanu planszy.

**Wymagany handoff:** Screenshot każdego intentu i odsetek poprawnych przewidywań z testu jakościowego.

---

## `M3.10` — Replay komend i hash deterministyczności

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 16, 21.2 i 22.

**Rationale:** Seed bez historii komend nie odtwarza decyzji, a screenshot nie wyjaśnia rozbieżności modelu. Lekki replay jest narzędziem debugowania i fundamentem solvera, nie funkcją dla gracza.

**Obecne zachowanie:** Błędu tury nie można jednoznacznie odtworzyć z małego zestawu danych.

**Oczekiwany rezultat:** Testowy recorder zapisuje wersję zasad, seed, board blueprint i komendy; replay daje stabilny hash kanonicznego stanu po każdej turze. Commands use an explicitly versioned tagged representation, and unknown tags fail with a diagnostic instead of being ignored.

**Zakres:** Kanoniczna serializacja do testów/debugu, state hash, runner replay i raport pierwszej rozbieżności. Document the extension rule: every new authoritative gameplay field or command type must update the replay-rules version, canonical hash coverage, and focused tests in its owning ticket.

**Non-goals:** Bez publicznego formatu replay, synchronizacji sieciowej, anty-cheatu, nagrywania animacji i kompatybilności między dowolnymi wersjami gry.

**Zależności:** `M3.1`–`M3.9`.

**Dozwolony obszar plików:** Core Diagnostics/Turns, testy i ignorowany katalog wyników.

**Kryteria akceptacji:** Dwa uruchomienia tych samych danych mają identyczne hashe; zmiana komendy daje kontrolowany różnicę; hash nie zależy od kolejności słowników, Transform ani czasu; unknown command tags fail explicitly; a contract test fails when a registered authoritative state contributor is omitted from hashing.

**Plan testów:** EditMode powtarzalności, permutacji kolekcji i raportu divergence; PlayMode porównania modelu z końcową prezentacją.

**Wpływ na save i kompatybilność:** Replay jest artefaktem diagnostycznym i nie zastępuje save. Zawiera własny jawny numer wersji.

**Wymagany handoff:** Minimalny zredagowany przykład replay i hashów; lokalizacja artefaktów oraz potwierdzenie, że są ignorowane.

---

## `M3.11` — Route-leg state and stable board address

**Status:** `Planned`
**Priority:** P0 architecture gate
**Related contract:** sections 4, 5, 6, 7, 8, 16, 21, and 27.1–27.3.

**Rationale:** The current model equates one destination node with one board and derives board identity from node/day. A multi-board edge would therefore repeat seeds and could accept a stale outcome from another segment. Stable travel and board identity must exist before the generator, atlas samples, and fixed tool sources depend on the wrong boundary.

**Current behavior:** `RunState` stores `WorldNodeId`, `CurrentDay`, and a list of node IDs. Route selection immediately reaches the destination and marks the edge traversed. `BoardRequest` and stale-outcome guards use run + node + day + seed, while `GetBoardSeed()` has no edge or segment identity.

**Expected outcome:** World edges own ordered, non-empty stable `RouteSegmentDefinition` IDs. `RunState` distinguishes the last reached checkpoint from an optional `ActiveRouteLegState` and stores structured travel entries. The active leg includes durable `Pending`/`Presented` Day-transition state. One immutable `BoardAddress` identifies the active local board by run, edge, segment, day, generator/config version, and deterministic seed.

**Scope:**

- Extend `WorldEdgeDefinition` with an ordered segment list; route length is derived from this list rather than duplicated as an independent count.
- Add `ActiveRouteLegState` with edge/from/to, 1-based day number, current segment identity/index, completed segment count, durable Day-transition status, and an extension point for a route observation accumulator.
- Replace the node-ID-only route history with structured entries that can distinguish different edges reaching the same node.
- Introduce `BoardAddress`; update `BoardRequest`, `BoardOutcome`, seed derivation, same-identity guards, and diagnostics to use it.
- Separate completed-day count from the current leg's displayed day.
- Version and migrate run persistence and replay/hash coverage.

**Non-goals:** No multi-board scene flow or Day transition presentation yet; no route-resource observations, fixed tool sources, generator rewrite, backpack, satiety, or mid-board snapshot.

**Dependencies:** `M2.7`, `M3.10`, and the accepted O-007/O-008 contract.

**Allowed file area:** Core State/World/Session/Persistence/Diagnostics/Random contracts, world data required for the existing prototype, focused tests, and migration fixtures. Scene and prefab changes are forbidden.

**Acceptance criteria:**

- every edge has at least one segment and all segment IDs are stable and unique within the published world;
- two segments on the same edge produce different deterministic board addresses/seeds;
- reorder of definitions does not change the identity or seed of an unchanged stable segment;
- a result from another segment, edge, day, or run is rejected as stale;
- day is player-facing from `1`, while a fresh checkpoint may still have zero completed days;
- a new active leg persists `Pending`; marking the intro `Presented` is an idempotent state transition included in run round-trip/hash coverage;
- structured travel distinguishes parallel edges with the same destination;
- old run schema migrates to a reached-node checkpoint with no fabricated active leg, and profile data is never discarded;
- canonical replay/hash includes active leg and board address.

**Test plan:** EditMode validation of segment definitions, address equality, deterministic seed vectors, parallel edges, stale outcomes, route-log invariants, schema round-trip/migration/corruption, and replay/hash expansion. Run full EditMode after focused tests.

**Save and compatibility impact:** New run schema. An old node-based active run migrates as a safe reached checkpoint with `completedDayCount = old currentDay` and no active leg; its next chosen edge starts the next displayed day. If a particular legacy save cannot satisfy the checkpoint invariants, reject only that run with a clear recovery path and preserve the profile.

**Required handoff:** State diagram, complete field/owner table, old→new schema mapping, fixed seed vectors, world segment-ID registry, and proof that stale outcomes cannot cross segment boundaries.

---

## `M3.12` — Multi-board route flow and immersive Day transition

**Status:** `Planned`
**Priority:** P0 architecture gate
**Related contract:** sections 5, 7.3, 7.5, 8, 18, and 27.2.

**Rationale:** A correct data model is not enough if route selection still teleports to the destination or every local exit reopens the atlas. One owner must advance segment, day, discovery, persistence, and presentation in the accepted order.

**Current behavior:** `WorldMapService.ChooseRoute()` immediately advances `RunState` to the target node, marks the edge `Traversed`, increments day, and checkpoints. Any active board represents that reached node; its exit opens route choice.

**Expected outcome:** A dedicated travel service starts a persisted route leg with a `Pending` intro, shows `Day N` once while gameplay input is blocked, atomically persists `Presented` before enabling the first board input, enters each stable segment in order, and treats intermediate exits as continuation. Only the final exit reaches the destination, marks discovery, completes the travel entry, checkpoints the leg, and opens route choice.

**Required flow:**

```text
Reached checkpoint + chosen edge
→ persist ActiveRouteLegState
→ show Day N once with gameplay HUD hidden
→ load segment 1 ... segment N
→ intermediate ExitReached: checkpoint next segment, no atlas and no day increment
→ final ExitReached: complete leg, Traversed + Visited, persist, open atlas
```

**Scope:** Add `RouteTravelService` or equivalent focused owner; adapt session flow and route selection; checkpoint at leg start, after the one-time Day transition is consumed, and at each stable between-board boundary; display the full-screen transition; keep route queries in the map service/read model; update summaries needed to distinguish completed days from death during a day.

**Non-goals:** No historical resource aggregation, tool-source icons, final route-choice redesign, biome expansion, landmark content, bag, quick pockets, or mid-board snapshot.

**Dependencies:** `M3.11`, `M3.6`, and the accepted persistence/lifecycle from M1/M2.

**Allowed file area:** Core Travel/Session/World/State/Persistence, atlas route-flow adapter, Day transition presentation, focused scenes/prefabs only when explicitly allowlisted, and tests.

**Acceptance criteria:**

- choosing an edge starts but does not complete it;
- the destination node remains unreached and the edge untraversed through every intermediate segment;
- segment exits advance exactly once and cannot be replayed or double-saved;
- the last exit commits destination `Visited` and edge `Traversed` once, then opens route choice;
- all boards in one leg show the same day number and the next leg increments it exactly once;
- `Day 1` appears before the first leg, never `Day 0`, and the transition hides health/resource/tool HUD;
- `Continue` with `Pending` shows the transition, while `Continue` with `Presented` restores the same segment without replaying it; no path enables gameplay input before `Presented` is durably checkpointed;
- different edges may contain different positive segment counts without code changes.

**Test plan:** EditMode state-machine and idempotency matrix; PlayMode two- and five-segment legs, death on an intermediate segment, final arrival, duplicate outcome, route choice, HUD-hidden Day transition, restart/`Continue` from both `Pending` and `Presented`, crash injection around the presentation checkpoint, and animations disabled; full lifecycle regression.

**Save and compatibility impact:** Uses the M3.11 run schema and writes only stable completed boundaries. Mid-board resume remains deferred; a restart of the current board must keep the same segment address and must not duplicate leg progress.

**Required handoff:** Travel sequence diagram, checkpoint matrix for start/intermediate/final/death/continue, screenshots of Day transition with hidden HUD, and proof that atlas discovery changes only on final arrival.

### Bramka M3

M3 is complete when each input creates at most one command, rejected actions are free, accepted actions resolve one full turn, intents are locked and readable, identical seed/state/commands yield identical hashes, and a variable-length route leg advances through stable board addresses without changing day or discovery until final arrival.

---

# M4 — Generator, hałas i drugi archetyp zombie

---

## `M4.1` — Niezależne strumienie deterministycznego RNG

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcja 16.

**Rationale:** Globalny `UnityEngine.Random` sprawia, że dodatkowy efekt kosmetyczny może zmienić układ następnej planszy. Nazwane strumienie izolują świat, run, planszę, puzzle i kosmetykę.

**Obecne zachowanie:** Gameplay i generacja korzystają z globalnego RNG Unity.

**Oczekiwany rezultat:** Czysty provider tworzy powtarzalne strumienie z root seed oraz stabilnego stream ID; logika domenowa nie wywołuje `UnityEngine.Random`. Gameplay streams include stable-addressed `route-supply`, `board-layout(edgeId, segmentId)`, and authored feature/source placement; cosmetic randomness remains isolated.

**Zakres:** Algorytm/abstrakcja RNG, derivation seedów, API zakresów bez modulo bias, stan/debug label i test vectors.

**Non-goals:** Bez kryptografii, zabezpieczenia seedów przed graczem, daily run i zmiany kosmetycznego RNG poza potrzebnym adapterem.

**Zależności:** Bramka M3.

**Dozwolony obszar plików:** `/Assets/Scripts/Core/Random/**`, adaptery obecnej generacji, testy.

**Kryteria akceptacji:** Ten sam root/stream daje tę samą sekwencję; pobranie z kosmetyki nie zmienia board stream; zakresy są udokumentowane; brak globalnego RNG w zmigrowanym gameplayu; two route segments never alias a gameplay stream merely because they share node/day context.

**Plan testów:** Stałe test vectors, niezależność strumieni, granice range, replay M3.

**Wpływ na save i kompatybilność:** Run DTO przechowuje root seed oraz identyfikatory/wersję algorytmu niezbędne do odtworzenia granicy planszy.

**Wymagany handoff:** Schemat derivation oraz repo-wide lista pozostałych dozwolonych wywołań `UnityEngine.Random`.

---

## `M4.2` — `BoardBlueprint` i konfiguracja budżetu

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 7.2, 8, 16 i 17.

**Rationale:** Generator powinien wytworzyć dane możliwe do walidacji przed `Instantiate`. Blueprint jest też wejściem replayu, testów i prezentera.

**Obecne zachowanie:** `BoardManager` losuje oraz natychmiast tworzy GameObjecty, a trudność wynika głównie z dnia/liczby zombie.

**Oczekiwany rezultat:** Niemutowalny `BoardBlueprint` opisuje rozmiar 8×8, start, exit, warstwy, spawn points i metadane seed/config; `BoardGenerationConfig` opisuje budżet sytuacji. The blueprint carries its `BoardAddress`, stable generic item-definition/source IDs, and a read-only initial resource manifest grouped by resource family.

**Zakres:** Typy danych, validation-friendly construction, mapping do `BoardState` oraz tymczasowy exporter istniejącej planszy do porównań. Define the generic item spawn seam without implementing carried inventory or atlas persistence; do not grow `AutomaticFoodReward` into the final item model.

**Non-goals:** Bez algorytmu generacji, presenter instancjonującego cały content, większych map i balansu finalnego.

**Zależności:** `M4.1` i `M3.2`.

**Dozwolony obszar plików:** Core Generation/Board, GameData configs, tests.

**Kryteria akceptacji:** Blueprint nie zawiera GameObjectów; jest deterministycznie porównywalny/haszowalny; nie może mieć duplikatu start/exit; konfiguracja nie opiera trudności wyłącznie na rozmiarze/liczbie zombie; every item/source entry has one stable identity and the resource manifest is derived from blueprint data rather than maintained as a second mutable total.

**Plan testów:** EditMode construction, round-trip blueprint→state, hash i invalid examples.

**Wpływ na save i kompatybilność:** Blueprint może wejść do replay, ale nie do normalnego save między planszami.

**Wymagany handoff:** Przykład jednego blueprintu i mapping pól budżetu na zamierzoną presję.

---

## `M4.3` — Walidator lokalnej planszy

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 8, 14, 16 i 17.

**Rationale:** Losowa plansza musi gwarantować legalny start i osiągalne wyjście zanim gracz straci run. Walidator jest niezależny od algorytmu, aby sprawdzać też ręczne i landmarkowe blueprinty.

**Obecne zachowanie:** Osiągalność wynika pośrednio z bezpiecznego obwodu i nie jest dowodzona dla danych.

**Oczekiwany rezultat:** Czysty walidator raportuje out-of-bounds, konflikty warstw, brak start/exit, nieosiągalność bez opcjonalnego loot/tool, duplicate/unknown item or fixed-source IDs, and critical-budget violations.

**Zakres:** Reachability dla podstawowych reguł, lista wszystkich błędów z kontekstem, metryki ścieżki i branching.

**Non-goals:** Bez pełnego solvera tur z zombie, automatycznej poprawy, oceny „fun” i landmark mechanics jeszcze nieistniejących.

**Zależności:** `M4.2`.

**Dozwolony obszar plików:** Core Generation/Validation, test fixtures.

**Kryteria akceptacji:** Poprawna plansza przechodzi; każdy błąd ma test; wymagane wyjście jest osiągalne bez losowego narzędzia; stable item/source identities are unique and resolvable; komunikat zawiera pełny board address/seed/config, gdy dostępne.

**Plan testów:** EditMode dla ręcznych dobrych/złych blueprintów, granic i metryk.

**Wpływ na save i kompatybilność:** Brak.

**Wymagany handoff:** Katalog reguł walidacji i znane ograniczenia względem przyszłego dynamicznego AI.

---

## `M4.4` — Generator model-first zwykłej planszy 8×8

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 7.2, 8, 16 i Appendix A.

**Rationale:** Docelowa różnorodność ma pochodzić z decyzji/topologii, nie z wydłużania mapy lub automatycznie bezpiecznej ramki. Generacja danych przed widokiem umożliwia walidację i deterministyczność.

**Obecne zachowanie:** Generator zostawia wolny obwód, używa globalnego RNG i łączy losowanie z `Instantiate`.

**Oczekiwany rezultat:** Dla request/config/seed generator zwraca blueprint 8×8 bez gwarantowanego bezpiecznego obwodu, z legalnym startem, exit i kontrolowaną topologią. Item spawns are generic content instances and expose deterministic initial counts by resource family without writing to profile/atlas state.

**Zakres:** Pierwsza rodzina zwykłej planszy, podstawowe terrain/obstacles/items/enemies, jawne fazy generacji i adapter prezentacji blueprintu. Legacy automatic-food mapping may remain an adapter, but generated data uses stable item definitions.

**Non-goals:** Bez biomów finalnych, landmarku, pełnego tool contentu, większych rozmiarów i ręcznego „naprawiania” GameObjectów po walidacji.

**Zależności:** `M4.1`–`M4.3`.

**Dozwolony obszar plików:** Core Generation, `BoardManager` jako fasada/presenter, configs, testy i jawne prefaby tylko do mapowania widoku.

**Kryteria akceptacji:** Ten sam input daje identyczny blueprint; każdy wynik przechodzi walidator; outer ring może zawierać meaningful terrain/obstacles, ale start nie jest unfair; prezentacja nie zmienia danych; generic item instances and the derived resource manifest are deterministic for the full board address.

**Plan testów:** Snapshot determinism, set znanych seedów, PlayMode blueprint→widok i porównanie liczby/pozycji encji.

**Wpływ na save i kompatybilność:** Run przechowuje seed i config ID, nie pełny środek planszy.

**Wymagany handoff:** Fazy algorytmu, przykładowe blueprinty i lista usuniętych użyć globalnego RNG.

---

## `M4.5` — Ograniczone próby i poprawny fallback generatora

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcja 16.

**Rationale:** Generator nie może wisieć ani wypuścić błędnej planszy, gdy losowy kandydat nie przechodzi walidacji. Limit i przygotowany fallback dają kontrolowany koszt oraz pewność ukończenia.

**Obecne zachowanie:** Brak formalnego protokołu generate → validate → retry → fallback i raportowania przyczyn.

**Oczekiwany rezultat:** Pipeline wykonuje skończoną liczbę deterministycznych prób, raportuje odrzucenia i używa poprawnego fallback blueprintu zgodnego z requestem.

**Zakres:** Orkiestrator prób, sub-seedy, telemetryka diagnostyczna, biblioteka minimalnych fallbacków i ich walidacja przy starcie/testach.

**Non-goals:** Bez nieskończonego retry, pobierania contentu, ukrywania fallback rate i automatycznego obniżania trudności runu bez komunikacji.

**Zależności:** `M4.4`.

**Dozwolony obszar plików:** Core Generation/Pipeline, GameData fallback, tests.

**Kryteria akceptacji:** Limit zawsze obowiązuje; ten sam input wybiera tę samą próbę/fallback; każdy fallback przechodzi walidator; fallback preserves any authored resource-band and fixed-feature requirements carried by the request; failure report contains full board address, seed, config, and concise reasons.

**Plan testów:** Wymuszony generator zawsze-fail, sukces na N-tej próbie, uszkodzony fallback oraz timeout/performance bound.

**Wpływ na save i kompatybilność:** Wybrany board seed/config zostaje w run boundary data; zmiana algorytmu może zmienić planszę po aktualizacji i wymaga version label dla replay.

**Wymagany handoff:** Maksymalna liczba prób, zasada sub-seedów i raport z wymuszonego fallbacku.

---

## `M4.6` — Testy 1 000/10 000 seedów i metryki trudności

**Status:** `Planned`  
**Priorytet:** P0  
**Powiązany kontrakt:** sekcje 16, 17 i 22.

**Rationale:** Kilka ręcznych plansz nie ujawni rzadkich softlocków ani dryfu trudności. Batch test ma zatrzymać błędny generator przed produkcją contentu.

**Obecne zachowanie:** Nie ma automatycznej dystrybucji metryk ani rejestrowania seedów porażki.

**Oczekiwany rezultat:** Szybki test codzienny sprawdza co najmniej 1 000 seedów, milestone gate 10 000; raportuje reachability, długość/gałęzie ścieżki, gęstość przeszkód, presję, fallback rate, and resource-family supply per segment plus aggregate variance per edge fixture.

**Zakres:** Headless batch harness, limity czasu, histogramy/summary i zapis minimalnych failing cases poza źródłami.

**Non-goals:** Bez udowadniania „fun”, publicznej telemetrii, arbitralnego automatycznego balansu i ukrywania outlierów średnią.

**Zależności:** `M4.5`.

**Dozwolony obszar plików:** Tests/Generation, diagnostics/reporting, ignorowany output.

**Kryteria akceptacji:** 10 000 aktywnych seedów ma zero invalid/softlock; failure podaje reprodukowalny board address/seed; fallback rate mieści się w jawnym progu ustalonym przed testem; runtime jest zapisany; reports can later prove an authored rumor band is never violated and a guaranteed fixed source is present.

**Plan testów:** Dwa identyczne batch runy porównują summary/hash; celowo uszkodzona config dowodzi, że harness wykrywa problem.

**Wpływ na save i kompatybilność:** Brak; używa synthetic runs.

**Wymagany handoff:** Raport zbiorczy, wszystkie failing seeds i rekomendacja budżetu; bez selektywnego pomijania wyników.

---

## `M4.7` — Hałas jako zdarzenie domenowe

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcja 12.

**Rationale:** Hałas ma połączyć narzędzia, trasę i manipulowanie zombie. Musi być tym samym zdarzeniem, które interpretuje AI i pokazuje UI, a nie tylko klipem audio.

**Obecne zachowanie:** Dźwięki nie tworzą jawnego, deterministycznego stanu rozgrywki.

**Oczekiwany rezultat:** Akcja może emitować `NoiseEvent` ze źródłem, pozycją, siłą/zasięgiem i czasem; query słyszalności jest deterministyczne i prezentowalne.

**Zakres:** Model hałasu, propagacja w podstawowym terenie, lifetime, events i prosta wizualizacja debug/UI.

**Non-goals:** Bez fizycznej symulacji akustyki, losowego perception check, finalnego audio mixu i reakcji Listenera.

**Zależności:** `M3.10` i `M4.4`.

**Dozwolony obszar plików:** Core Noise/Turns/Board, Presentation/Noise, tests.

**Kryteria akceptacji:** Ten sam event daje ten sam obszar; UI i AI czytają ten sam model; wygasanie jest jawne; brak ukrytego rzutu; sygnał jest czytelny bez audio.

**Plan testów:** EditMode zasięg/przeszkody/lifetime i replay; PlayMode zgodności wizualizacji.

**Wpływ na save i kompatybilność:** Bieżący hałas jest BoardState i nie jest zapisowany między planszami.

**Wymagany handoff:** Reguła propagacji w jednym akapicie i screenshot/debug view dla kilku sił.

---

## `M4.8` — Listener reagujący na ostatni słyszany hałas

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 11.3 i 12.

**Rationale:** Listener zmienia hałas z kary w narzędzie planowania: gracz może zaakceptować ryzyko albo zwabić zombie. Jego zachowanie musi pozostać proste do przewidzenia.

**Obecne zachowanie:** Istnieje tylko obecny typ przeciwnika; żaden archetyp nie ma jawnej pamięci celu hałasu.

**Oczekiwany rezultat:** Listener zapamiętuje ostatni słyszany, jednoznacznie wybrany hałas i planuje `Investigate`; przy braku sygnału używa jawnego `Wait`/fallback zachowania.

**Zakres:** Definition/state/planner Listenera, tie-break wielu hałasów, utrata celu, execution i presenter intentu.

**Non-goals:** Bez sight cone, ukrytego alert level, grupowej komunikacji, losowej percepcji i trzeciego archetypu.

**Zależności:** `M4.7` i `M3.9`.

**Dozwolony obszar plików:** Core Enemies/Noise, presentation mapping/prefab, tests.

**Kryteria akceptacji:** Reguła wyboru hałasu jest stabilna; `Investigate` wskazuje cel przed ruchem gracza; unieważniony intent nie replanowuje; gracz może w teście odciągnąć Listenera.

**Plan testów:** EditMode dla zasięgu, wielu źródeł, tie-break, wygasania i konfliktów; PlayMode intent→wykonanie.

**Wpływ na save i kompatybilność:** Pamięć hałasu żyje tylko w BoardState.

**Wymagany handoff:** Jednozdaniowa reguła Listenera i macierz przykładów.

---

## `M4.9` — Bramka czytelności intentów i hałasu

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** bramka intentów z sekcji 22.

**Rationale:** Poprawne testy jednostkowe nie dowodzą, że człowiek rozumie ikonę, zasięg i moment blokowania planu. Content narzędzi nie powinien powstać, dopóki ta informacja jest nieczytelna.

**Obecne zachowanie:** Shambler, Listener i noise UI mogą przejść testy techniczne, ale nie mają wspólnego wyniku playtestu.

**Oczekiwany rezultat:** Moderowany scenariusz mierzy przewidywanie intentów oraz świadome użycie hałasu; wyniki prowadzą do `Proceed`, `Revise` albo `Stop`.

**Zakres:** 5–8 krótkich sesji, scenariusze bez/ze wskazówką, zapis przewidywań przed turą i analiza błędów.

**Non-goals:** Bez nowych mechanik, publicznej analityki, testu marketingowego i poprawiania produkcyjnych plików w tej samej karcie.

**Zależności:** `M4.7` i `M4.8`.

**Dozwolony obszar plików:** `/Docs/Validation/M4.9.md` oraz test-only fixtures; produkcja read-only.

**Kryteria akceptacji:** Większość uczestników przewiduje znanego zombie; błędy są sklasyfikowane UI/reguła/tutorial; porażkę można wyjaśnić bez ukrytego RNG; istnieje decyzja bramki.

**Plan testów:** Playtest według jednego skryptu, osobne wyniki pierwszego kontaktu i po nauczeniu, porównanie z automatycznym replayem.

**Wpływ na save i kompatybilność:** Brak; profile testowe są izolowane.

**Wymagany handoff:** Zanonimizowana tabela wyników, obserwacje i dokładna rekomendacja dalszego działania.

### Bramka M4

M4 jest zaliczony, gdy 10 000 seedów aktywnej konfiguracji nie zawiera invalid boardów, replay jest stabilny, globalny RNG nie steruje logiką, a testerzy potrafią przewidzieć Shamblera i Listenera oraz świadomie wykorzystać hałas.

---

# M5 — Narzędzia i systemowa interakcja

---

## `M5.1` — Wspólny model celowanej interakcji

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 9, 13.1 i 18.

**Rationale:** Siekiera, łopata, pickup i pchanie nie powinny tworzyć czterech osobnych ścieżek inputu. Wspólny model celu pozwala walidować akcję przed kosztem i jasno pokazać możliwe pola.

**Obecne zachowanie:** Interakcje wynikają głównie z wejścia na collider i metod konkretnego komponentu.

**Oczekiwany rezultat:** `InteractionQuery` zwraca legalne akcje/cel, a `InteractCommand` jednoznacznie wskazuje wybraną możliwość; brak legalnego celu nie zużywa tury. Query and modal preview retain the exact ground-entity identity and never remove or mutate it before an accepted command commits.

**Zakres:** Query model, target IDs/positions, kody dostępności, podstawowe collect/push hooki i presenter podświetlenia. The option model may represent a future decision-required pickup without implementing the M9 `Use now / Store / Leave` flow.

**Non-goals:** Bez narzędzi, rozbudowanego inventory, menu radialnego, craftingu i automatycznego wyboru „najlepszej” interakcji.

**Zależności:** Bramka M4.

**Dozwolony obszar plików:** Core Interaction/Turns/Board, Presentation/Interaction i testy.

**Kryteria akceptacji:** Query nie mutuje stanu; komenda odwołuje się do stabilnego celu; invalid/stale target jest darmowy; cancel/preview leaves the same ground instance in `BoardState`; wszystkie legalne cele są widoczne bez polegania tylko na kolorze.

**Plan testów:** EditMode empty/single/multiple/stale target; PlayMode selection, cancel i disabled animation.

**Wpływ na save i kompatybilność:** Brak nowego trwałego stanu.

**Wymagany handoff:** Tabela typ celu → legalna komenda → koszt oraz przykład konfliktu wielu interakcji.

---

## `M5.2` — Dwa sloty i stan instancji narzędzia

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcja 13.1.

**Rationale:** Dwa generic equipment slots tworzą czytelny koszt okazji. They remain separate from the future spatial backpack rather than acting as a substitute for or becoming cells inside it. Definition, run instance, and authored atlas source need different identities.

**Obecne zachowanie:** Run nie ma docelowego modelu slotów, definicji tool contentu ani ograniczonych użyć.

**Oczekiwany rezultat:** `ToolDefinition` ma stabilne ID i niezmienne dane, `ToolInstanceState` has a run-local instance ID, definition ID, and charges, while `RunState` owns exactly two generic slots. A future `toolSourceId` identifies where an instance came from and is never used as the instance ID.

**Zakres:** Model, equip/replace/remove, inwarianty charges, konfiguracje placeholder axe/shovel i podstawowy read-only HUD.

**Non-goals:** Bez użycia narzędzia, spatial bag/pockets, craftingu, stacków, napraw, ulepszeń, losowych affixów i trwałego przenoszenia narzędzi między runami.

**Zależności:** `M5.1` i `M1.2`.

**Dozwolony obszar plików:** Core Tools/State, GameData/Tools, Presentation/HUD, tests.

**Kryteria akceptacji:** Są dokładnie dwa sloty; definitions nie są mutowane; dwa runy nie współdzielą charges; UI obserwuje stan; nie można utworzyć ujemnych charges.

**Plan testów:** EditMode equip/replace/empty/charges/copy; PlayMode HUD po reload sceny.

**Wpływ na save i kompatybilność:** Run DTO otrzymuje sloty i stable tool IDs; wymaga migracji schema z pustymi slotami.

**Wymagany handoff:** Model definition vs instance oraz zachowanie nieznanego tool ID przy load.

---

## `M5.3` — `UseToolCommand` i atomowe zużycie charges

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 9, 10 i 13.1.

**Rationale:** Narzędzie nie może zużyć ładunku lub tury, jeśli cel stał się nieważny. Walidacja, efekt, hałas, koszt i charge muszą być jednym wynikiem resolvera.

**Obecne zachowanie:** Sloty istnieją, ale nie ma wspólnego kontraktu użycia w turze.

**Oczekiwany rezultat:** `UseToolCommand(slot, target)` jest najpierw w pełni walidowane, a accepted result atomowo emituje efekt, noise, charge spend i bieżący policy-driven koszt tury; it does not own a permanent assumption that every future action costs exactly one `Food`.

**Zakres:** Command/result, dispatch do zachowania tool definition, kody błędu, eventy i podgląd konsekwencji dostępnych przed potwierdzeniem.

**Non-goals:** Bez konkretnych efektów axe/shovel, rollbacku po animacji, durability repair i dowolnych skryptów narzędzi z dostępem do Unity.

**Zależności:** `M5.2` i `M4.7`.

**Dozwolony obszar plików:** Core Tools/Turns/Interaction, presentation preview, tests.

**Kryteria akceptacji:** Invalid target/empty slot/zero charges nie zmieniają stanu; accepted use zużywa dokładnie jeden charge i jedną turę; noise jest częścią tego samego result; podwójny submit jest blokowany.

**Plan testów:** EditMode pełna macierz walidacji i atomicity; PlayMode preview/confirm/cancel/rapid input.

**Wpływ na save i kompatybilność:** Zmiana charges jest zapisywana w runie na istniejących granicach autosave.

**Wymagany handoff:** Chronologiczny event list poprawnego i odrzuconego użycia.

---

## `M5.4` — Siekiera: szybsza trasa za duży hałas

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 12, 13.2 i 13.4.

**Rationale:** Samo „szybciej usuwa krzak” jest płaską premią. Duży hałas zamienia oszczędność czasu w przestrzenne ryzyko i łączy siekierę z Listenerem.

**Obecne zachowanie:** Przeszkody mogą mieć HP w widoku, ale nie istnieje domenowy kontrast ręczne karczowanie vs użycie axe.

**Oczekiwany rezultat:** Krzew/miękka drewniana przeszkoda ma wolną alternatywę bez narzędzia; axe usuwa ją szybciej, zużywa charge i emituje czytelny głośny noise.

**Zakres:** Domenowy stan przeszkody/progress, akcja ręczna, efekt axe, konfiguracja liczb, events, audio/visual feedback i fakt `AXE_CREATES_LOUD_NOISE`. Axe board instances use stable tool definitions and may expose a distinct authored source ID for M7.5.

**Non-goals:** Bez walki siekierą, ścinania każdego drzewa, craftingu, trwałych ulepszeń i drugiego zastosowania „barykada”, dopóki pierwsze nie przejdzie bramki.

**Zależności:** `M5.3` i `M4.8`.

**Dozwolony obszar plików:** Core Tools/Obstacles/Knowledge, configs, presentation/prefabs jawnie wymienione, tests.

**Kryteria akceptacji:** Obie trasy są legalne; axe zawsze jest szybsza według config; noise i charge są pokazane przed/po akcji; brak axe nie softlockuje; Listener reaguje na dokładnie ten noise event.

**Plan testów:** EditMode porównania kosztów/charges/noise i replay; PlayMode przeszkoda z/bez axe oraz reakcja Listenera.

**Wpływ na save i kompatybilność:** Tool charges w runie; fact discovery w profilu może wymagać rozszerzenia/migracji listy stabilnych fact IDs.

**Wymagany handoff:** Porównanie kosztu obu rozwiązań i nagranie/screenshot czytelności noise.

---

## `M5.5` — Łopata: schowek i dół

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 13.3 i 13.4.

**Rationale:** Łopata ma łączyć eksplorację z manipulowaniem planszą, a nie wyłącznie skracać animację zbierania. Schowek daje korzyść ekonomiczną, dół daje taktyczną kontrolę pola.

**Obecne zachowanie:** Brak oznaczonych schowków, domenowego digging progress i tymczasowej pułapki na zombie.

**Oczekiwany rezultat:** A marked cache has a slow alternative or explicitly optional reward; the shovel reveals it quickly. The reward is represented by one concrete stable item definition/instance rather than a raw `RestoreFood` call. Before M9, a compatibility adapter may resolve that item immediately under O-003; M9.5 replaces the adapter with the shared item decision. The second shovel use creates a temporary pit for a supported archetype.

**Zakres:** Dig targets/progress, cache reward through a concrete item instance and stable definition ID, transitional pre-M9 reward adapter, pit state/lifetime, interakcja Shamblera, charges, events i fact `PIT_TRAPS_SHAMBLER`. Shovel board instances may expose a distinct authored source ID for M7.5.

**Non-goals:** Bez dowolnego kopania każdego pola, terraformingu, losowej ukrytej pułapki, obrażeń jako walki i obowiązkowego celu wymagającego losowego łopaty.

**Zależności:** `M5.3`, `M3.7` i `M4.3`.

**Dozwolony obszar plików:** Core Tools/Terrain/Enemies/Knowledge, configs, presentation, tests.

**Kryteria akceptacji:** Invalid terrain is free; the pit has explicit lifetime/effect; exit remains reachable without the shovel; the cache creates exactly one identifiable reward item and the transitional adapter resolves its reward before the cost under the active contract; facts unlock only after observation; no code path bypasses the item identity with a direct resource mutation.

**Plan testów:** EditMode cache/pit/lifetime/enemy/softlock validation; PlayMode oba zastosowania i UI pozostałych tur/charges.

**Wpływ na save i kompatybilność:** Charges/facts as in M5.4; pit and an uncollected revealed cache item are `BoardState`. M9.5 later owns the item transfer choice without changing the cache/source identity.

**Wymagany handoff:** Macierz zastosowanie → korzyść → koszt/ryzyko → alternatywa bez narzędzia.

---

## `M5.6` — Conscious tool pickup and slot replacement

**Status:** `Planned` — O-003 resolved 2026-09-27
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 13.1, 18 i O-003.

**Rationale:** Przy dwóch pełnych slotach znalezione narzędzie tworzy ważny wybór, ale przypadkowa zamiana byłaby karą za wejście na pole. O-003 now fixes the pre-M9 split: food remains automatic and transitional, while a tool requires conscious pickup and routing to the separate equipment bar.

**Obecne zachowanie:** Nie ma docelowego flow podnoszenia tool instance ani modalnej zamiany.

**Oczekiwany rezultat:** A consciously accepted tool pickup automatically fills a free generic tool slot. With both slots occupied it presents `Replace` or `Leave`; cancel/leave is free, the ground instance remains, and no equipped tool is silently destroyed. Transitional automatic food remains unchanged until M9.

**Zakres:** Tool pickup command, stable board entity/tool instance and optional source IDs, modal slot choice, UI comparison of definition/charges, equipment presentation, and turn integration.

**Non-goals:** No generic consumable prompt, bag, side pockets, spatial placement, carried-item discard, stacks, trade, crafting, or automatic destruction of an equipped tool. Those consumable semantics belong to M9.

**Zależności:** `M5.2`, `M5.3`, `M5.1`, and accepted O-003 in the contract.

**Dozwolony obszar plików:** Core Items/Tools/Turns, Presentation/Equipment/Interaction, configs/prefabs i tests.

**Kryteria akceptacji:** Conscious pickup into an empty slot and confirmed replacement each mutate exactly one slot and resolve the accepted turn once; `Leave`, cancel, invalid/stale replacement, and full-slot refusal cost nothing and keep the same ground instance; rapid input cannot duplicate a tool; UI shows both definitions and charges.

**Plan testów:** EditMode empty/full/replace/leave/cancel/stale/source-instance identity; PlayMode modal/input/reload; regression proving accepted O-003 automatic food is unchanged before M9.

**Wpływ na save i kompatybilność:** Run DTO zachowuje wynik zamiany; brak nowych danych profilu.

**Wymagany handoff:** Reference resolved O-003, provide the full tool modal flow, and list the seams intentionally reserved for M9 without implementing them.

---

## `M5.7` — Field Notes dla faktów systemowych

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 4, 5.4, 13.4 i 18.

**Rationale:** Druga forma progresu to zrozumienie reguł. Dziennik ma utrwalać zaobserwowany fakt i wspierać pamięć, ale nie zdradzać skutku przed eksperymentem.

**Obecne zachowanie:** `ProfileState` potrafi przechować IDs faktów, lecz gracz nie ma uporządkowanego widoku ani momentu odkrycia.

**Oczekiwany rezultat:** Po pierwszej obserwacji fakt trafia do profilu, pojawia się jednoznaczny feedback i jest dostępny w Field Notes z tekstem/ikoną; nieodkryte fakty nie zdradzają treści. Mechanical `FactDiscovery` is a separate event/model from M7.5 `ToolSourceSighted` location knowledge.

**Zakres:** `FactDefinition`, event discovery, lokalizacja robocza, ekran/lista notesów i pierwsze fakty axe/shovel/Listener.

**Non-goals:** Bez pełnego bestiariusza, checklisty procentowej, quizów, automatycznego tutorialu i bonusów statystyk.

**Zależności:** `M5.4`–`M5.6` i `M2.3`.

**Dozwolony obszar plików:** Core Knowledge/Profile, GameData/Facts, Presentation/FieldNotes, persistence, tests.

**Kryteria akceptacji:** Fact odkrywa się tylko po zdefiniowanej obserwacji, dokładnie raz; trwa przez nowy run; unknown nie pokazuje treści; UI nie jest jedynym właścicielem danych.

**Plan testów:** EditMode trigger/idempotency/save migration; PlayMode feedback i ponowne otwarcie po death/new run.

**Wpływ na save i kompatybilność:** Profil zapisuje stable fact IDs; brakująca definicja ma kontrolowany placeholder, nie niszczy profilu.

**Wymagany handoff:** Rejestr facts: ID, warunek odkrycia, tekst, system źródłowy.

### Bramka M5

M5 jest zaliczony, gdy oba narzędzia tworzą różne decyzje, każde ma koszt lub ryzyko, brak narzędzia nie tworzy losowego softlocka, a odkryte fakty przeżywają śmierć bez zdradzania niepoznanych mechanik.

---

# M6 — Landmark z głazami, płytami i bramami

---

## `M6.1` — Format ręcznie przygotowanego szablonu landmarku

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 7.1, 14 i 16.

**Rationale:** Pierwsza zagadka ma być kuratorowana i automatycznie sprawdzalna, nie generowana jak pełny Sokoban. Szablon danych pozwala tworzyć warianty bez osobnej minigry i bez ręcznej logiki sceny.

**Obecne zachowanie:** Brak typu node `Landmark`, formatu zagadki i mappingu do wspólnego BoardState.

**Oczekiwany rezultat:** `LandmarkTemplate` opisuje blueprint, cele/rezultaty, dozwolone warianty i wymagania walidacji przy użyciu stable IDs. Template identity is independent from node identity and can be assigned to a stable route segment.

**Zakres:** Data format, loader, validator strukturalny i jeden pusty/sanity fixture w grafie dnia około 5. Do not encode the assumption that every node owns exactly one board instance.

**Non-goals:** Bez mechaniki głazów/bram, proceduralnego układania puzzla, osobnej sceny minigry i finalnego contentu.

**Zależności:** `M4.6` i `M2.1`.

**Dozwolony obszar plików:** Core Landmarks/World/Generation, GameData/Landmarks, tests.

**Kryteria akceptacji:** Szablon buduje ten sam BoardState co zwykła plansza; ma co najmniej dwa nazwane outcomes; invalid references są raportowane; wymagane narzędzie musi być guaranteed albo opcjonalne.

**Plan testów:** EditMode load/validation/stable ID/round-trip template→blueprint.

**Wpływ na save i kompatybilność:** Run/replay identify the landmark by its active `BoardAddress` plus stable template/variant ID and record the outcome at the completed-board boundary. Destination node arrival remains the route-flow consequence of completing the final segment; no node ID is used as board identity.

**Wymagany handoff:** Schemat szablonu i przykład dwóch outcomes bez implementowania rozwiązania.

---

## `M6.2` — Pchanie głazów w wspólnym resolverze

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 9, 10 i 14.

**Rationale:** Głaz ma być systemową przeszkodą używaną w normalnej siatce, a nie skryptem poziomu. Pchanie musi respektować tę samą walidację, koszt i kolejność tury.

**Obecne zachowanie:** Przeszkody są statyczne lub posiadają HP w widoku; brak domenowego akcji push.

**Oczekiwany rezultat:** `PushCommand` albo jednoznaczny wariant `Interact` atomowo przesuwa gracza i głaz, jeśli pole za nim jest legalne.

**Zakres:** Boulder state, walidacja łańcucha o długości jeden, events, presenter i wpływ na path/reachability.

**Non-goals:** Bez pchania wielu głazów, ciągnięcia, fizyki rigidbody, bezwładności i narzędziowej siły.

**Zależności:** `M5.1` i `M3.2`–`M3.6`.

**Dozwolony obszar plików:** Core Obstacles/Turns/Board, Presentation, prefab boulder i tests.

**Kryteria akceptacji:** Zablokowane pchnięcie jest darmowe; poprawne zużywa jedną turę; gracz/głaz nie zajmują konfliktowych pól; AI/path query widzi nową pozycję natychmiast.

**Plan testów:** EditMode wolne/blokada/krawędź/aktor/płyta; PlayMode synchronizacji dwóch animacji i rapid input.

**Wpływ na save i kompatybilność:** Tylko BoardState; brak mid-board save.

**Wymagany handoff:** Macierz legalności pchania i event order.

---

## `M6.3` — Płyty i bramy jako stan domenowy

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 10 i 14.

**Rationale:** Stan bramy musi wynikać z planszy po każdej mutacji i być znany resolverowi przed wykonaniem intentu. Animator nie może decydować, czy pole jest przechodnie.

**Obecne zachowanie:** Brak plates/gates; analogiczne przeszkody opierają stan na komponentach widoku.

**Oczekiwany rezultat:** Płyta obserwuje aktora/głaz według jawnej reguły, brama ma stable link ID i stan open/closed, a zmiana emituje event przed kolejną fazą.

**Zakres:** Definitions/state, recompute trigger, wiele płyt do jednej bramy według wybranej konfiguracji, walkability i presentation.

**Non-goals:** Bez złożonych obwodów logicznych, timerów, kolorowych kluczy, fizycznych jointów i ukrytych połączeń.

**Zależności:** `M6.2`.

**Dozwolony obszar plików:** Core Landmarks/Board/Turns, presentation prefabs, tests.

**Kryteria akceptacji:** Model i widok zawsze zgadzają się; zejście z płyty aktualizuje bramę; intent w zamknięte pole staje się `Wait` bez replanu; link errors wykrywa validator.

**Plan testów:** EditMode actor/boulder enter/leave, multi-plate rule, intent invalidation; PlayMode animation disabled/enabled.

**Wpływ na save i kompatybilność:** Tylko BoardState.

**Wymagany handoff:** Jawna logika wielu płyt i chronologia events względem faz tury.

---

## `M6.4` — Pierwszy landmark dnia około 5

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 14 i 20.

**Rationale:** Landmark ma sprawdzić, czy wspólne systemy tworzą ciekawą logiczną kulminację. Jeden starannie przygotowany układ dostarcza więcej wiedzy niż wiele niesprawdzonych losowych puzzli.

**Obecne zachowanie:** Mechaniki są dostępne, lecz nie istnieje pełny authored puzzle z trasą podstawową i alternatywnym kosztem.

**Oczekiwany rezultat:** Landmark uses boulders, plates, gates, at least one zombie, and an optional tool; it has at least two meaningful outcomes/solutions and a path that does not require random loot. In the route-leg model it is authored as the final segment of the inbound edge unless a later contract explicitly requires a separate day.

**Zakres:** One template, rewards/consequences through stable content/item IDs, segment entry/final arrival at the landmark node, presentation, and clear goal information.

**Non-goals:** Bez pełnej proceduralności, dziesiątek wariantów, jedynego poprawnego rozwiązania i nowych reguł działających tylko na tym poziomie.

**Zależności:** `M6.1`–`M6.3` oraz `M5.4` i `M5.5`.

**Dozwolony obszar plików:** GameData/Landmarks, potrzebne art/prefabs/scene presentation, localization/UI i tests.

**Kryteria akceptacji:** Co najmniej dwa outcomes mają różny koszt; brak narzędzia nie blokuje celu obowiązkowego; zasady są komunikowane przez istniejące UI; ukończenie zapisuje outcome w runie i discovery w profilu, jeśli dotyczy.

**Plan testów:** Ręczne przejście każdą zamierzoną drogą, automated validator, replay wszystkich rozwiązań i test death/exit.

**Wpływ na save i kompatybilność:** Stable template/outcome IDs w run/profile; zmiana ID wymaga migracji/aliasu.

**Wymagany handoff:** Diagram układu, lista zamierzonych rozwiązań, kosztów i awaryjnej ścieżki.

---

## `M6.5` — Solver rozwiązywalności landmarku

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 14, 16 i 22.

**Rationale:** Pchanie głazu może bezpowrotnie zablokować stan. Solver wykorzystujący czysty resolver pozwala sprawdzić każdy wspierany wariant bez ręcznego testowania wszystkich sekwencji.

**Obecne zachowanie:** Validator zna strukturę, ale nie dowodzi istnienia sekwencji prowadzącej do celu.

**Oczekiwany rezultat:** Ograniczony BFS/A* enumeruje domenowe stany dla landmarku, znajduje co najmniej jedno rozwiązanie każdego wymaganej rezultatu albo raportuje minimalny failing fixture.

**Zakres:** Kanoniczny hash stanu, legal command enumeration, limity czasu/stanów, reconstruction path i integration z testami template.

**Non-goals:** Bez AI podpowiadającego graczowi, optymalnego solvera dowolnego poziomu, generowania puzzli i uwzględniania animacji.

**Zależności:** `M6.4` i `M3.10`.

**Dozwolony obszar plików:** Core Solver/Diagnostics, tests i ignorowane raporty.

**Kryteria akceptacji:** Każdy wymagany outcome ma znalezioną ścieżkę w limicie; celowo zablokowany wariant failuje; hash deduplikuje stany; raport zawiera template ID/variant/seed.

**Plan testów:** Znane małe puzzle, no-solution fixture, determinism i performance budget.

**Wpływ na save i kompatybilność:** Brak; solver używa snapshotów testowych.

**Wymagany handoff:** Liczba odwiedzonych stanów, czas i przykładowa sekwencja dla każdego outcome.

---

## `M6.6` — Kuratorowane warianty i bramka landmarku

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** bramka landmarku z sekcji 22.

**Rationale:** Jeden znany układ traci regrywalność, ale pełna proceduralność jest zbyt ryzykowna. Mała rodzina ręcznie sprawdzonych wariantów daje różnorodność i zachowuje kontrolę jakości.

**Obecne zachowanie:** Jeden landmark może przejść solver, lecz jego decyzje i czytelność nie są jeszcze sprawdzone na wariantach.

**Oczekiwany rezultat:** 3–5 wariantów zmienia początkowe położenia, presję lub reward, każdy przechodzi solver; playtest potwierdza rozumienie kosztu rozwiązania.

**Zakres:** Wariant data, selection przez landmark RNG stream, batch solve i 5–8 sesji playtestowych.

**Non-goals:** Bez losowego przestawiania dowolnego obiektu, nowej mechaniki na wariant, content farm i skalowania planszy.

**Zależności:** `M6.5`.

**Dozwolony obszar plików:** GameData/Landmarks variants, tests i `/Docs/Validation/M6.6.md`.

**Kryteria akceptacji:** Wszystkie warianty mają rozwiązanie i dwa sensowne rezultaty tam, gdzie wymagane; gracz potrafi wyjaśnić konsekwencję; selection jest deterministyczny; brak znanego softlocka.

**Plan testów:** Solver dla każdego wariantu, replay, ręczny playtest bez instrukcji rozwiązania.

**Wpływ na save i kompatybilność:** Run/replay zapisuje variant ID/seed; profil nie zaznacza automatycznie wszystkich wariantów jako odwiedzone.

**Wymagany handoff:** Macierz wariant → rozwiązania → koszt → wynik solvera/playtestu.

### Bramka M6

M6 jest zaliczony, gdy każdy wspierany wariant przechodzi solver, brak losowego narzędzia nie blokuje celu, istnieją co najmniej dwa sensowne rezultaty, a testerzy rozumieją konsekwencje własnej trasy.

---

# M7 — Dziesięciodniowy vertical slice

---

## `M7.1` — Rozszerzenie stałego grafu do około 10 dni

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sections 5, 7.1, 8, 19, 20, and 27.2.

**Rationale:** Dopiero pozytywny wynik pięciodniowego atlasu uzasadnia podwojenie contentu. Stały graf utrzymuje wartość trwałej wiedzy i pozwala zaprojektować pacing do finału.

**Obecne zachowanie:** Prototyp kończy się około dnia 5 i zawiera placeholder landmarku/celu.

**Oczekiwany rezultat:** The validated graph has about ten edge-based days, multiple routes, a landmark around day five, reconnections, reusable information, and a final node. Every edge authors a positive ordered segment list; shorter and longer routes coexist and five boards is only an initial target.

**Zakres:** Expand world data, stable node/edge/segment IDs, variable route lengths, hint placeholders, difficulty and supply budgets, and migrations/aliases only where necessary. The pacing table counts boards, expected turns, and estimated minutes in addition to days.

**Non-goals:** Bez kampanii 20–40 dni, losowego makrografu, obowiązku odwiedzenia wszystkiego i procentu ukończenia.

**Zależności:** M3 gate including `M3.11`–`M3.12`, M6 gate, and `M2.8` recommendation `Proceed`.

**Dozwolony obszar plików:** GameData/World, validator tests, docs diagram.

**Kryteria akceptacji:** All mandatory goals are reachable; meaningful route choices include different positive segment counts; first victory does not require a complete atlas; at least one later attempt benefits from information; the expected full-run board/turn/minute budget is explicitly reviewed rather than inferred from ten days.

**Plan testów:** Graph validator, enumeracja tras, save migration ID i design walkthrough pacingu.

**Wpływ na save i kompatybilność:** Stare discovery IDs pozostają prawidłowe; usunięte ID wymagają aliasu lub kontrolowanej migracji.

**Wymagany handoff:** Ten-day edge graph, stable segment registry, pacing table with segment/turn/minute totals, and rationale for every branch.

---

## `M7.2` — Dwa biomy różniące się regułą

**Status:** `Planned` — dokładne reguły pozostają `Hypothesis`, zanim playtest je zatwierdzi  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 15, 17 i 20.

**Rationale:** Podróż na północ potrzebuje widocznego postępu geograficznego, ale sam reskin nie zmienia decyzji. Dwa biomy wystarczą do sprawdzenia uczenia i kombinowania reguł bez produkcji pór roku.

**Obecne zachowanie:** Plansze używają jednego zestawu terenu i nie posiadają biome definition.

**Oczekiwany rezultat:** Las i zimna strefa mają po jednej–dwóch wyraźnych regułach wpływających na ruch, ślady, widoczność lub hałas; graf prowadzi ku chłodowi. Biome/config context may belong to a route segment and is not inferred only from the destination node.

**Zakres:** `BiomeDefinition`, palety rodziny generatora, mechaniczny modifier każdego biomu, presentation i intro→combine pacing.

**Non-goals:** Bez pełnych pór roku, temperatury survival-sim, wielu skinów bez reguły i jednoczesnego wprowadzania wszystkich mechanik.

**Zależności:** `M7.1` i `M4.6`; Coordinator wybiera eksperymentalne reguły na podstawie prototypu.

**Dozwolony obszar plików:** Core/GameData Biomes/Generation, presentation art/audio, tests.

**Kryteria akceptacji:** Tester potrafi opisać różnicę reguły; ten sam modifier jest widoczny dla generatora/resolvera/UI; pierwsze wystąpienie izoluje nową regułę; biom nie jest tylko sprite setem.

**Plan testów:** EditMode modifierów i generator constraints, batch seeds osobno na biom, PlayMode readability oraz playtest kontrastowy.

**Wpływ na save i kompatybilność:** Stable segment or node definitions reference biome/config IDs as authored; active board address already persists the exact segment. No dynamic season is stored in profile.

**Wymagany handoff:** Hypothesis → mechanic → expected decision → observed playtest dla obu biomów.

---

## `M7.3` — Archetypy miejsc i prawdziwe wskazówki tras

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sections 5, 7.3–7.5, 17, 18, and 27.3.

**Rationale:** Wybór trasy jest ciekawy, gdy informacja jest niepełna, ale wiarygodna. Stabilne archetypy pozwalają wykorzystać pamięć atlasu bez fałszywych opisów.

**Obecne zachowanie:** Node może mieć nazwę/typ, ale nie istnieje system hints powiązany ze stabilnymi cechami contentu.

**Oczekiwany rezultat:** Location and route archetypes have explicit tags/features. A `Rumored` edge may expose truthful resource-family `low`/`medium`/`high` bands and route length; `Sighted` may reveal family/icon without pretending an exact future total.

**Zakres:** Definitions/tags, per-resource-family qualitative thresholds, generator constraints proving the authored band, route-length presentation, truthful hint generation, localization, and examples per biome. Historical numeric samples belong to M7.4.

**Non-goals:** Bez proceduralnej narracji LLM, ukrytego procentu prawdy, perfekcyjnej informacji i dziesiątek archetypów.

**Zależności:** `M7.2` i `M2.4`.

**Dozwolony obszar plików:** Core/GameData World/Locations/Hints, Atlas UI, tests.

**Kryteria akceptacji:** Every hint is true for a stable feature or guaranteed generation band; it may be incomplete but not contradictory; `Unknown` leaks nothing; route length is not confused with supply; a tester uses at least one hint in a choice.

**Plan testów:** EditMode all hint→definition mappings, property tests across generator seeds proving no advertised band lies, PlayMode atlas states/length, and route-choice playtest.

**Wpływ na save i kompatybilność:** Profil zapisuje discovery/hint IDs tylko jeśli potrzebne; definitions używają stable IDs.

**Wymagany handoff:** Rejestr hintów z dowodem źródłowej cechy oraz obserwowane decyzje testerów.

---

## `M7.4` — Historical route-resource observations

**Status:** `Planned`
**Priority:** P1 product pillar
**Related contract:** sections 3.1, 6.3, 7.3–7.6, 18, 22, and 27.3.

**Rationale:** A persistent atlas becomes useful metaprogression when repeated travel improves an honest memory of route supply. Storing current loot as a promise would lie; storing only prose would fail to support the player's food-versus-healing decisions.

**Current behavior:** `ProfileState` stores node/edge discovery, free-form notes/facts, and summaries, but no typed resource observation. Generated resources are board/run state, and reload/revisit has no sample identity.

**Expected outcome:** The profile stores typed observations keyed by edge and resource-family ID. One completed run/edge traversal contributes one initial-observable total; repeated complete samples produce min/max/count. Partial travel may store a separate monotonic lower bound and observed-segment count without changing the completed range.

**Scope:**

- Add immutable observation value types and idempotent merge operations.
- Add an active-leg accumulator that records each segment's initially observable supply once before collection changes it.
- Commit a complete sample only on final arrival; commit an optional partial lower bound on death/abandon according to the contract.
- Add profile DTO migration, missing-definition handling, atlas query model, and UI states for rumor versus partial versus historical range.
- Show route segment count beside aggregate supply and current health/resource context without computing a “best” route.
- Bump replay rules/version and canonical hash coverage for the active-leg accumulator and deterministic observation events; profile projection checks compare the resulting aggregate explicitly.

**Non-goals:** No guarantee of ordinary loot on the next run, hidden-loot leakage, per-tile memory, cloud analytics, inventory, or automatic route recommendation.

**Dependencies:** `M7.3`, `M3.12`, `M4.6`, and accepted O-008.

**Allowed file area:** Core Profile/Travel/Knowledge/Persistence, Atlas query/presentation, resource definitions/configs, tests, and migration fixtures.

**Acceptance criteria:**

- first completed total `10` displays `Observed: 10`; a later completed total `9` displays `Observed so far: 9–10 · 2 visits`;
- reload, board revisit, collection, or leftover count cannot add or alter a sample;
- a run/edge observation key is idempotent;
- death after `k/n` segments may display `At least X · observed k/n` but never changes completed min/max/count;
- only observable/revealed supply contributes; hidden content remains unknown;
- profile observations survive death/new run and actual loot remains run/board state;
- unknown resource-family IDs degrade to a controlled placeholder without destroying other knowledge.
- replay of the same leg produces the same accumulator, observation event, profile aggregate, and canonical hashes; omitted observation state fails the extension contract from M3.10.

**Test plan:** EditMode merge/idempotency/min-max/partial/hidden/reload/revisit tests; schema round-trip and v2 migration; replay/version/hash expansion and divergence injection; PlayMode complete and partial multi-board routes; atlas readability test comparing rumor, one sample, range, and taken current-run resources.

**Save and compatibility impact:** New additive profile schema plus active-leg accumulator in run schema. Existing profiles migrate with empty observations. Published edge/resource IDs require alias/migration when renamed.

**Required handoff:** Owner matrix, exact sample event chronology, DTO mapping, screenshot of every knowledge state, and reproducible proof that a depleted revisit cannot narrow or duplicate history.

---

## `M7.5` — Fixed tool sources and persistent atlas icons

**Status:** `Planned`
**Priority:** P1
**Related contract:** sections 7.4–7.6, 13, 18, 21.3, and 27.3.

**Rationale:** A remembered tool location is a strong, concrete reward for exploration, but it is only fair if the icon corresponds to a source that reliably exists. Separating permanent discovery from current-run availability preserves both trust and run tension.

**Current behavior:** Tool definitions/instances are planned in M5, but the world has no stable source identity, profile discovery, or atlas marker. A board entity ID cannot safely identify a location across runs.

**Expected outcome:** Authored `ToolSourceDefinition` records stable source ID, tool-definition ID, physical edge-segment binding, and an optional atlas anchor for presentation. First sight permanently reveals the correct atlas icon. Each new run restores one instance at every fixed source; pickup marks it `taken` only for the current run.

**Scope:** Source definitions and validation, deterministic `BoardAddress` binding, optional node/segment atlas anchor, sight event, profile discovered-source IDs, current-run taken state, atlas query/presentation, M5 tool-pickup integration, replay/version/hash expansion, and content examples with multiple axe/shovel sources.

**Non-goals:** No guarantee for ordinary random consumables, tool respawn within the same run, permanent carried tools, shop, crafting, repair, or mandatory exit requiring a tool.

**Dependencies:** `M5.6`, `M7.3`, `M3.11`, and accepted O-008.

**Allowed file area:** Core/GameData World/Tools/Profile/Travel/Persistence, Generation fixed-feature binding, Atlas presentation, tests, and migration fixtures.

**Acceptance criteria:**

- source ID, tool definition ID, tool run-instance ID, and board entity ID remain distinct;
- a node-anchored icon still resolves its physical source through one stable edge segment, identifies the owning inbound route unambiguously, and never implies a node-local board or availability from another edge;
- sight without pickup persists the exact icon across death/new run;
- taking a source shows `taken` for the current run, does not erase profile knowledge, and cannot duplicate on reload/re-entry;
- the same source returns once in the next run and every authored fixed source is present for supported seeds/fallbacks;
- multiple sources of the same tool remain independently discoverable;
- validator proves every mandatory exit has a tool-free path.
- replay of sight/take/reload reproduces discovered/taken state and canonical hashes; omitting either new authoritative state fails the M3.10 extension contract.

**Test plan:** Definition/ID validation, profile migration/idempotency, replay/version/hash expansion, generator and fallback presence across representative seeds, PlayMode sight/pickup/reload/death/new run, atlas icon/taken-state readability, and softlock validation.

**Save and compatibility impact:** New additive profile list of discovered source IDs and run state for taken source IDs; missing definitions use controlled placeholders/diagnostics. World-definition version changes when published source bindings change.

**Required handoff:** Source registry with exact stable bindings, owner/ID table, atlas screenshots before sight/after sight/after pickup/new run, and generator proof for each fixed source.

---

## `M7.6` — Finał i jawne zwycięstwo

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 19, 20 i O-001.

**Rationale:** Skończony cel nadaje podróży sens i umożliwia ocenę pacingu. Sam rosnący licznik dni nie mówi, czy gracz opanował systemy ani kiedy run jest kompletny.

**Obecne zachowanie:** Istnieje game over, lecz brak docelowego `Won` i finałowego node z zakończeniem.

**Oczekiwany rezultat:** The finale is the authored final route segment around day ten. It tests known rules without an unannounced mechanic, emits `Won` from its exact `BoardAddress`, closes the run, and preserves the profile.

**Zakres:** Final edge-segment definition/template and `BoardAddress`, destination/final-node narrative context, win condition, lifecycle, and minimal presentation/narrative consistent with resolved O-001.

**Non-goals:** Bez cutsceny wysokobudżetowej, new game+, boss fight jako tradycyjna walka i obowiązku kompletnego atlasu.

**Zależności:** `M7.1`–`M7.5`, bramki M3–M6 oraz rozstrzygnięcie O-001 przed finalnym tekstem.

**Dozwolony obszar plików:** Core outcome/session, final GameData/scene/presentation, tests.

**Kryteria akceptacji:** Finał jest osiągalny co najmniej jedną trasą bez kompletowania mapy; win i death są rozłączne zgodnie z O-002; po win `Continue` nie wznawia zakończonego runu; atlas pozostaje.

**Plan testów:** Automated route/final validation, PlayMode win lifecycle/restart, ręczne przejście minimalnej i alternatywnej trasy.

**Wpływ na save i kompatybilność:** Run zapisuje terminal `Won`, następnie jest archiwizowany/usuwany zgodnie z lifecycle; profil zachowuje discovery.

**Wymagany handoff:** Warunek zwycięstwa, minimalna ścieżka, zachowanie save przed/w trakcie/po finale.

---

## `M7.7` — Podsumowanie wyprawy i dziennik trasy

**Status:** `Planned`  
**Priorytet:** P1  
**Powiązany kontrakt:** sekcje 4, 5.4, 19 i O-005.

**Rationale:** Po śmierci lub zwycięstwie gracz powinien rozumieć przyczynę, przebytą trasę i trwałe odkrycia. To wzmacnia chęć kolejnego runu lepiej niż leaderboard surowej liczby dni.

**Obecne zachowanie:** Game over koncentruje się na wyniku/liczbie dni i zewnętrznych rekordach.

**Oczekiwany rezultat:** The local screen shows structured completed and interrupted route legs, terminal cause, used tools, key decisions, new node/edge/facts, new resource observations, and newly sighted fixed sources; it can continue to atlas or a new run.

**Zakres:** Run summary model from structured travel/events, UI, Field Notes/Atlas integration, and a bounded private local history if schema cost remains reasonable. Reserve event categories for future item collected/used/discarded without adding inventory now.

**Non-goals:** Bez publicznego leaderboardu, oceniania „optymalności”, trwałych bonusów mocy i śledzenia danych bez zgody.

**Zależności:** `M7.6`, `M7.4`, `M7.5`, and `M5.7`; O-005 resolves that private summary precedes any online feature.

**Dozwolony obszar plików:** Core Summary/Profile, Presentation Summary/Atlas/Notes, persistence, tests.

**Kryteria akceptacji:** Przyczyna końca odpowiada events; nowe odkrycia są odróżnione od starych; summary nie zmienia stanu; restart zachowuje profil; można rozpocząć kolejny run bez zewnętrznej sieci.

**Plan testów:** EditMode aggregation różnych outcomes, PlayMode death/win/restart, ręczny test czytelności.

**Wpływ na save i kompatybilność:** Opcjonalna historia ma limit i schema migration; brak danych osobowych lub sieciowego uploadu.

**Wymagany handoff:** Screenshoty death/win oraz mapping każdego pola summary do źródłowego event/state.

---

## `M7.8` — Stabilizacja, dostępność i playtest vertical slice

**Status:** `Planned`  
**Priorytet:** P0 release gate  
**Powiązany kontrakt:** sekcje 18–22.

**Rationale:** Vertical slice ma odpowiedzieć, czy pełna pętla jest zrozumiała i regrywalna, nie tylko czy każda mechanika działa osobno. Stabilizacja musi poprzedzić rozszerzanie kampanii.

**Obecne zachowanie:** Wszystkie systemy istnieją, ale nie przeszły wspólnej macierzy jakości, dostępności, performance i obserwacji wielu runów.

**Oczekiwany rezultat:** Kandydat vertical slice przechodzi automatyczne bramki, pełny smoke, checklistę dostępności i 8–12 obserwowanych sesji; powstaje decyzja dalszego zakresu.

**Zakres:** Regression pass, seed gate, save corruption/restart, input/UI scaling, sygnały nie tylko kolorem, możliwość wyłączenia/przyspieszenia animacji, lokalna opt-in telemetryka testowa lub ręczne arkusze oraz triage.

**Non-goals:** Bez nowych głównych mechanik, monetyzacji, kampanii 40-dniowej, publicznego uploadu telemetryki i poprawiania wszystkich sugestii testerów bez priorytetu.

**Zależności:** `M7.1`–`M7.7` oraz wszystkie wcześniejsze bramki.

**Dozwolony obszar plików:** Najpierw tests/docs/config; każda poprawka produkcyjna powstaje jako osobny child ticket z własnym zakresem i review.

**Kryteria akceptacji:** Zero P0/P1 defects; 10 000 seeds green; save recovery green; most testers understand atlas persistence, rumor versus observation, route length, and intents; some voluntarily begin another run and use prior supply/tool knowledge; finale reachable; no secret or required network.

**Plan testów:** Pełne EditMode/PlayMode, dwa powtórne batch runs, manual smoke macierzy rozdzielczości/inputu, 8–12 playtestów z wcześniej zdefiniowanymi pytaniami.

**Wpływ na save i kompatybilność:** Test aktualizacji z ostatniego wspieranego schema; backup i recovery obowiązkowe.

**Wymagany handoff:** Release report with results, defects, and qualitative metrics; one core verdict (`expand`, `iterate core loop`, or `stop/pivot`) plus a separate inventory verdict (`proceed to M9`, `defer M9`, or `reject/revise M9`).

### Bramka M7

Vertical slice is complete only after `Accept` of `M7.8`. Implementing the feature list is insufficient: observed players must understand atlas knowledge, route observations, predictable turns, and why they would begin another run. M9 remains deferred until its separate verdict is `proceed to M9` or the owner explicitly overrides that gate.

---

# M8 — Po vertical slice: zadania świadomie odroczone

---

## `M8.1` — Snapshot i wznowienie środka planszy

**Status:** `Deferred`

**Powiązany kontrakt:** sekcje 6.2, 10, 16 i 20.

**Rationale:** Wymaga stabilnego `BoardState`, resolvera, wersjonowanego replayu i wszystkich dynamicznych systemów; wcześniejszy snapshot podwoiłby koszt migracji. Bez niego gracz może celowo wyjść do menu lub zamknąć grę, a następnie użyć `Continue`, aby cofnąć niekorzystne akcje i odtworzyć początek tej samej planszy.

**Obecne zachowanie:** M1.10 odtwarza deterministycznie ten sam początkowy układ planszy i ostatnią zatwierdzoną granicę runu. Nie zachowuje mutacji środka planszy: pozycji aktorów, zebranych przedmiotów, uszkodzonych lub zniszczonych przeszkód, stanu przeciwników, zasobów po turach, hałasu ani efektów pola. `Exit to Menu → Continue` działa więc jak reset bieżącej planszy.

**Oczekiwany rezultat:** `Continue` po wyjściu do menu, kontrolowanym zamknięciu aplikacji lub awarii odtwarza ostatni atomowo zapisany, zakończony stan tury. Ponowne wczytywanie nie przywraca zużytych zasobów, zebranych łupów, pokonanych przeciwników ani zniszczonych przeszkód i nie pozwala rerollować wyniku RNG.

**Zakres:** Versioned DTO for full `BoardState`, entity identities/state, actors, run resources, completed turn number/phase, active route address and observation accumulator, tools, obstacles, ground items, effects, noise, and deterministic RNG streams. If activated after M9, it also includes satiety, carried item instances, bag placements, and both pockets. Atomic checkpoint at a completed-turn boundary, mandatory flush before `Exit to Menu`/controlled shutdown, migration, backup, recovery, and exact-turn resume.

**Non-goals:** Cloud sync, zapis w trakcie animacji lub nierozstrzygniętej komendy, serializacja `GameObject`, `Transform`, colliderów, prefabów albo Unity `InstanceID`.

**Zależności:** `M3.2`, `M3.5`, `M3.10`, `M3.11`–`M3.12`, all dynamic systems included in the snapshot, and `M7.8`. If M9 ships before this card is activated, M9.1–M9.6 are additional dependencies; otherwise M8.1 must be reopened when M9 adds inventory state.

**Dozwolony obszar plików:** Core Persistence/Board/Turns i testy.

**Kryteria akceptacji:** After representative turns, `Exit to Menu → Continue` and process restart produce the same canonical board/run/completed-turn hash. Repeated `Continue` restores neither loot, health, satiety/legacy food, tool charges, enemies, obstacles, nor route observations and does not change gameplay RNG. If M9 is present, it cannot restore consumed/discarded items or an earlier bag/pocket layout. A snapshot never represents half a command or modal draft; corruption returns to one coherent backup. Missing/older snapshots have an explicit migration or accepted fallback to the safe M1.10 boundary.

**Plan testów:** Round-trip każdego typu encji i dynamicznego pola; hash przed wyjściem i po `Continue`; scenariusze po zebraniu łupu, zniszczeniu przeszkody, użyciu narzędzia, obrażeniach i ruchu/śmierci przeciwnika; wielokrotne `Exit to Menu → Continue`; restart procesu; crash injection przed zapisem, pomiędzy temp-write i replace oraz po replace; zgodność replay/hash i test migracji poprzedniej schemy.

**Wpływ na save:** Nowa wersja schema z migracją. Dotychczasowy zapis granicy M1.10 pozostaje jednoznacznym fallbackiem dla save'ów bez snapshotu, jeśli migracja nie może odtworzyć stanu środka planszy.

**Wymagany handoff:** Tabela wszystkich pól snapshotu i ich właścicieli, punkty checkpointu/flush, macierz exploitów resetu oraz wyniki round-trip, restart i crash injection.

---

## `M8.2` — Rozbudowa kampanii do 20–25 dni

**Status:** `Deferred`  
**Powiązany kontrakt:** sekcje 7.1, 17, 19 i 20.  
**Rationale:** Więcej contentu ma sens dopiero, gdy 10-dniowa pętla generuje dobrowolne powtórki; długość nie naprawi słabej decyzji.  
**Obecne zachowanie:** Vertical slice kończy się około dnia 10.  
**Oczekiwany rezultat:** Dłuższy pacing bez rozmycia wiedzy atlasu.  
**Zakres:** Graph, variable route-segment lists, content/supply budgets, and combinations of learned rules; pacing includes total boards, turns, and estimated session time rather than day count alone.
**Non-goals:** Automatyczne 30–40 dni i losowy makrograf.  
**Zależności:** Pozytywna decyzja `expand` z `M7.8`.
**Dozwolony obszar plików:** Zostanie określony przed `Ready`.  
**Kryteria akceptacji:** Każdy nowy odcinek wnosi kombinację decyzji, graf pozostaje osiągalny, a pacing nie wymaga kompletowania atlasu.  
**Plan testów:** Walidacja całego grafu, seed gate per biome oraz playtest długości/retencji sesji.  
**Wpływ na save:** Stable IDs i migracje.  
**Wymagany handoff:** Pacing/content budget i dowód potrzeby rozszerzenia.

---

## `M8.3` — Pory roku

**Status:** `Deferred`  
**Powiązany kontrakt:** sekcja 15.  
**Rationale:** Pory roku dodają drugą oś czasu i dużą macierz contentu, która może mylić geograficzne ochładzanie podróży na północ.  
**Obecne zachowanie:** Biomy i ewentualna pogoda opisują przestrzeń/bieżący run.  
**Oczekiwany rezultat:** Tylko po osobnym prototypie mechanicznie znaczący cykl sezonowy.  
**Zakres:** Do ustalenia po playtestcie.
**Non-goals:** Sezon jako reskin.  
**Zależności:** `M8.2` i osobna decyzja kontraktu.
**Dozwolony obszar plików:** Niedookreślony — karta nie może przejść na `Ready`.  
**Kryteria akceptacji:** Sezon tworzy odrębne decyzje i nie zaciera kierunku podróży.  
**Plan testów:** Osobny prototyp A/B oraz playtest rozumienia osi geografia–czas przed produkcją contentu.  
**Wpływ na save:** Prawdopodobna nowa oś profile/world state.  
**Wymagany handoff:** Prototyp i wynik playtestu przed produkcją contentu.

---

## `M8.4` — Daily/Endless Mode

**Status:** `Deferred`  
**Powiązany kontrakt:** sekcja 19.  
**Rationale:** Tryby regrywalności powinny wzmacniać działającą kampanię, a nie zastępować brak finału.  
**Obecne zachowanie:** Jedna skończona kampania vertical slice.  
**Oczekiwany rezultat:** Osobne, jasno oznaczone tryby po stabilizacji core.  
**Zakres:** Seed rules, scoring i oddzielny lifecycle do zaprojektowania.  
**Non-goals:** Włączenie ich do MVP.  
**Zależności:** `M7.8` i osobna decyzja produktu.
**Dozwolony obszar plików:** Niedookreślony.  
**Kryteria akceptacji:** Reprodukowalny daily, brak uszkodzenia profilu kampanii i jasny cel endless.  
**Plan testów:** Cross-machine seed vectors, lifecycle isolation, offline mode i playtest motywacji.  
**Wpływ na save:** Oddzielne run types/schema fields.  
**Wymagany handoff:** Projekt trybu i dowód, że rozwiązuje obserwowaną potrzebę.

---

## `M8.5` — Tradycyjna walka, crafting i trwała moc

**Status:** `Deferred`  
**Powiązany kontrakt:** sekcje 1, 3, 13.1, 19 i 20.  
**Rationale:** Te systemy przesunęłyby grę z logicznego unikania/manipulacji w stronę statystyk i grindu, maskując problemy czytelności.  
**Obecne zachowanie:** Konflikt opiera się na pozycji, zasobach, intentach i narzędziach użytkowych.  
**Oczekiwany rezultat:** Brak implementacji bez jawnej zmiany kontraktu i prototypu dowodzącego wartości.  
**Zakres:** Tylko przyszły design spike.  
**Non-goals:** Damage numbers, loot rarity, skill tree w bieżącej roadmapie. The accepted M9 spatial bag, consumables, and satiety pressure are explicitly not part of this deferred combat/crafting/permanent-power proposal.
**Zależności:** Osobna zmiana `GameDesignContract.md`.  
**Dozwolony obszar plików:** Brak do czasu nowej karty.  
**Kryteria akceptacji:** Muszą zostać napisane po jawnej zmianie decyzji; ticket nie może wcześniej przejść na `Ready`.  
**Plan testów:** Najpierw izolowany prototype/playtest porównujący decyzje logiczne z wariantem walki; produkcyjne testy dopiero po ADR.  
**Wpływ na save:** Prawdopodobnie wysoki; wymaga planu migracji.  
**Wymagany handoff:** ADR z alternatywami i wpływem na core promise.

---

## `M8.6` — Publiczny leaderboard

**Status:** `Deferred` — wymaga przyszłej, osobnej decyzji produktowej i modelu bezpieczeństwa
**Powiązany kontrakt:** sekcje 19, 21.5, O-005 i Appendix A.  
**Rationale:** Wynik liczby dni traci sens przy skończonym finale, a bezpośredni zapis z klienta nie może bezpiecznie przechowywać prywatnego credentialu.  
**Obecne zachowanie:** Stara integracja Dreamlo została usunięta z bieżącego klienta w `M0.10`; `M0.9` dokumentuje odłożenie funkcji i zaakceptowane ryzyko historycznej wartości.
**Oczekiwany rezultat:** Jeśli dane uzasadnią funkcję, nowa metryka i kontrolowany backend bez sekretu w kliencie.  
**Zakres:** Osobny projekt po MVP.  
**Non-goals:** Ponowne osadzenie prywatnego klucza.  
**Zależności:** `M7.8`, ponowne otwarcie O-005 oraz nowa decyzja infrastrukturalna i bezpieczeństwa.
**Dozwolony obszar plików:** Niedookreślony.  
**Kryteria akceptacji:** Zaakceptowany threat model, brak sekretu w kliencie, offline fallback i jawna polityka danych.  
**Plan testów:** Security review, abuse/rate-limit tests, awaria sieci i brak zależności profilu od usługi.  
**Wpływ na save:** Lokalny profil nie może zależeć od dostępności usługi.  
**Wymagany handoff:** Security review przed pierwszym requestem produkcyjnym.

---

# M9 — Spatial Inventory and Resource Pressure

M9 is intentionally listed last and remains **Deferred** until the owner has played the complete no-backpack vertical slice. It may begin only after an explicit `proceed to M9` verdict from `M7.8` or a later explicit owner override. It does not require optional M8 work.

---

## `M9.1` — Item catalogue and spatial `InventoryState`

**Status:** `Deferred`
**Priority:** P1 after inventory gate
**Related contract:** sections 3.2, 6, 13, 21.3, and 27.1/27.4.

**Rationale:** Item footprint and ownership are domain rules, not UI coordinates. A pure model must prove that items cannot overlap, escape the bag, occupy two containers, or become confused with tool equipment before pickup and presentation depend on it.

**Current behavior:** Food is a special `AutomaticFoodReward` on a board entity. There is no generic carried item definition/instance, spatial placement, or pocket state. Tool slots are the only run equipment.

**Expected outcome:** Immutable `ItemDefinition` data uses stable textual IDs, rectangular width/height, resource family/category, quick-pocket eligibility, and effect ID. Run-local `ItemInstanceState` has a distinct stable instance ID. A pure `InventoryState` models a `3×3` main grid and two one-item pockets while leaving two tool slots separate.

**Scope:** Definition/instance/placement value types, catalogue validation, bag/pocket queries, atomic pure placement/repack validation, new-run factory for an empty built-in backpack, and representative test definitions including `1×1` berries and a `1×2` drink.

**Non-goals:** No `RunState` integration or save migration yet; no use effects, pickup, UI, turn command, rotation, irregular shapes, stacks, weight, expansion, crafting, spoilage, rarity, or drop-to-ground.

**Dependencies:** Positive M9 verdict, `M4.4`, `M5.2`, `M5.6`, and accepted O-009.

**Allowed file area:** Core Items/Inventory definitions and tests; GameData item definitions only if required for validation. No scenes or prefabs.

**Acceptance criteria:**

- main grid is exactly `3×3` in the initial configuration;
- fixed-orientation rectangular items reject overlap and out-of-bounds placement;
- each item instance exists in at most one main-grid placement or one pocket;
- each pocket accepts exactly one `QuickPocketEligible` item with width `1` and height `1..3`; even `1×1` occupies the pocket;
- two inventories/runs never share mutable item instances;
- tool slots are neither bag cells nor pockets;
- duplicate/unknown definition and instance IDs fail with typed diagnostics.

**Test plan:** Exhaustive placement tests for all anchors/footprints on `3×3`, overlap/duplicate/pocket compatibility, immutability/copy isolation, catalogue validation, and property-based random layout validation.

**Save and compatibility impact:** None in this card because the model is not yet attached to authoritative `RunState`. DTO shape and migration are owned by M9.3.

**Required handoff:** Item definition/instance/container ownership diagram, placement invariants, rejected-case catalogue, and proof that tool equipment remains separate.

---

## `M9.2` — Health cap, satiety, and consumable effects

**Status:** `Deferred`
**Priority:** P1 after M9.1
**Related contract:** sections 10, 19, 25, and 27.5.

**Rationale:** The bag matters only when carried resources create decisions. Health and satiety need one bounded, deterministic policy before item use, bag transactions, and route UI can reason about their outcomes.

**Current behavior:** `RunState` owns unbounded `Health` and `Food`; restore operations can exceed any cap, and `TurnResolver` hard-codes action cost `1`. Food pickup is automatic.

**Expected outcome:** `RunState` has one authoritative `Satiety` value in `0..200`, initially `100`, and health in `0..100`. An action-cost policy samples satiety at command start (`1` at `<=100`, `2` above `100`). Pure, data-driven consumable effects resolve before the selected cost and report capped/wasted amounts.

**Scope:** Resource configuration/invariants, `Food`→`Satiety` domain migration, health cap, cost policy, item-effect definitions/registry, effect events, terminal ordering, run DTO migration for resources, replay-rules/version and canonical-hash expansion, and a temporary adapter preserving automatic board-food behavior until M9.5.

**Non-goals:** No bag integration, manual consumable pickup, inventory UI, quick use, spoilage, buffs, combat stats, or balancing a large item catalogue.

**Dependencies:** `M9.1`, `M3.5`, and accepted O-002/O-009.

**Allowed file area:** Core State/Resources/Items/Turns/Persistence, legacy automatic-food adapter, focused presentation labels, migration fixtures, and tests.

**Acceptance criteria:**

- health and satiety cannot leave their configured ranges;
- at command start satiety `100` selects cost `1` and `101` selects cost `2`;
- eating from `100` to `150` applies the already-selected cost `1`, while the next accepted action selects `2`;
- rejected commands and rejected effects change nothing;
- excess heal/satiety is reported as waste and never stored above cap;
- starvation/exit priority still follows O-002;
- runtime has no synchronized duplicate `Food` and `Satiety` fields;
- legacy run saves migrate deterministically and current no-bag play remains functional through the temporary adapter.
- replay captures satiety, health caps, selected cost tier, and consumable effects; omitting the renamed/new state fails the M3.10 extension contract.

**Test plan:** Boundary table for health/satiety/cost, effect-before-cost sequences, starvation/exit, rejected actions, caps/waste events, schema round-trip and legacy migration, replay/version/hash expansion with divergence injection, plus M3 turn regression.

**Save and compatibility impact:** New run schema maps old `food` to clamped satiety and adds explicit configuration/version semantics. Invalid legacy values produce a controlled migration result; profile save is unchanged.

**Required handoff:** Resource transition table, exact event chronology, old→new DTO mapping, and proof that one runtime owner replaced `Food` rather than shadowing it.

---

## `M9.3` — Inventory persistence, replay, and `RunState` integration

**Status:** `Deferred`
**Priority:** P1
**Related contract:** sections 6, 16, 21.3–21.4, and 27.4/27.9.

**Rationale:** A carried item is a run resource. Integrating it without versioned save and canonical replay would permit duplication, loss, or different layouts after `Continue`, even before the first bag command exists.

**Current behavior:** `RunState`/run DTO store resources and two tool slots but no item instances, spatial placements, pockets, or inventory revision. Replay/hash does not include them.

**Expected outcome:** Every active run owns one empty-or-populated `InventoryState` and a monotonic inventory revision. Any accepted operation that changes inventory advances it, and every accepted `ManageInventoryCommand` advances it even when its final layout/actions are a no-op. Run DTO, validation, backup/recovery, canonical serialization, replay version, and state hash preserve item instances, main-grid placements, pocket references, and revision using stable IDs only.

**Scope:** Attach inventory to `RunState`; mapper/DTO/schema migration; load validation; unknown/missing item handling policy; canonical hash/replay contributor; two-run isolation; save-boundary integration. Old saves receive an empty backpack and pockets.

**Non-goals:** No bag/pickup/quick-use command, UI, mid-board snapshot, profile inventory, item transfer between profiles, or persistence of modal drafts.

**Dependencies:** `M9.1`, `M9.2`, `M3.10`, and accepted atomic persistence.

**Allowed file area:** Core State/Inventory/Persistence/Diagnostics/Session, migration fixtures, and tests. No scenes/prefabs.

**Acceptance criteria:**

- round-trip preserves every item instance, placement, pocket, revision, satiety, health, and separate tool slot;
- old run schema migrates with empty inventory without changing profile knowledge;
- load rejects overlap, out-of-bounds, duplicate placement, incompatible pocket, duplicate instance ID, and unknown required effect safely;
- two runs share no mutable inventory collection or instance;
- replay/hash changes when any authoritative inventory field changes and unknown command/state versions fail explicitly;
- repeating an already accepted no-op management payload with the old expected revision is rejected as stale and cannot consume another turn;
- failed write/replace recovers one coherent pre- or post-change state, never a mixed inventory.

**Test plan:** DTO round-trip matrix, legacy migration, corrupt/unknown ID fixtures, backup/crash injection, canonical ordering/hash, two-run isolation, and repeated save/load with full inventory.

**Save and compatibility impact:** New run schema. Profile schema is unchanged. Mid-board modal/draft state is intentionally absent; M8.1 must include committed inventory if it is implemented later.

**Required handoff:** Complete domain↔DTO field map, migration/recovery matrix, canonical hash proof, and redacted sample save with every container represented.

---

## `M9.4` — Atomic one-turn backpack session

**Status:** `Deferred`
**Priority:** P1
**Related contract:** sections 10, 18, 21.2, and 27.7.

**Rationale:** Charging separately for opening and using would make a berry irrationally expensive, while unlimited actions would let the player heal and eat without enemy response. One atomic session with a two-action budget creates a clear tactical compromise.

**Current behavior:** There is no inventory command. Opening ordinary map/selection UI is free, and item use cannot be proposed together with a final spatial layout.

**Expected outcome:** `ManageInventoryCommand(expectedRevision, actions[0..2], finalLayout)` validates a complete draft and atomically commits up to two ordered `Use`/`Discard` actions plus arbitrary repacking. Any accepted finish—including no-op—advances the inventory revision exactly once, costs exactly one turn/resource payment, and triggers one hostile/environment phase.

**Scope:** Command/result/events, draft DTO separate from authoritative state, full prevalidation, use/discard effect ordering, permanent discard, final-layout commit, stale revision handling, resolver/controller integration, and a minimal test/debug presenter. Final art and HUD belong to M9.7.

**Non-goals:** No quick-pocket modifier, ground pickup, drop-to-ground, third action, per-drag turn cost, partial commit, persisted draft, or pause-time item use.

**Dependencies:** `M9.3`, `M3.5`, `M3.6`, and accepted O-009.

**Allowed file area:** Core Inventory/Items/Turns/State, minimal Presentation/Inventory adapter, tests, and only explicitly allowlisted UI assets.

**Acceptance criteria:**

- zero, one, or two ordered `Use`/`Discard` actions are legal; a third is rejected before mutation;
- any number of valid moves between grid/pockets is included without extra turns or action-budget spend;
- accepted finish produces one turn, one selected satiety cost, and one enemy/environment phase;
- accepted no-op finish advances the revision; resubmitting the same expected revision is stale and free rather than a second paid turn;
- stale revision, invalid effect/target, invalid layout, duplicate action, or rapid resubmit applies nothing and costs nothing;
- discarded carried items are destroyed and do not appear in `BoardState`;
- quick use cannot be attached, and newly pocketed items are unavailable until the command fully resolves;
- replay reproduces ordered effects and the final layout.

**Test plan:** EditMode action/layout cross-product, ordering/caps/starvation, stale/invalid atomic rollback, no-op finish, discard destruction, replay/hash; PlayMode modal input lock, rapid submit, one enemy phase, and animations disabled.

**Save and compatibility impact:** Only the committed post-turn inventory is saved. Drafts are never saved. Existing M9.3 schema should not change unless events require a separately justified version.

**Required handoff:** Command payload, validation/commit chronology, state-before/state-after examples for all action mixes, proof of one hostile phase, and complete rejection matrix.

---

## `M9.5` — Ground pickup decision and atomic board-to-run transfer

**Status:** `Deferred`
**Priority:** P1
**Related contract:** sections 6.4, 9, 10, 21.2, and 27.6.

**Rationale:** A reward should create `use now versus carry versus leave`, not automatic consumption or silent loss when the bag is full. The move and decision must still be one atomic turn so no modal can pause the resolver after partial mutation.

**Current behavior:** Entering food removes it and applies `AutomaticFoodReward`; non-food/tool interaction is separate. A left item can remain in `BoardState`, but no generic choice or carried transfer exists.

**Expected outcome:** Entering a consumable tile presents `Use now`, `Store`, or `Leave` before final command submission. Item-producing interactions such as a shovel cache use the same decision within their interaction command. The accepted composite command either uses/removes, stores/removes, or leaves the exact same ground instance. Tool pickup continues to use M5.6's separate equipment flow.

**Scope:** Pickup query/options and availability reasons, move- and interaction-based composite payloads, placement choice/preview, all-or-nothing BoardState→RunState transfer, entry-trigger behavior, M5.5 cache-adapter migration, removal of the legacy automatic-food path, presentation modal, and replay/events.

**Non-goals:** No automatic best placement, drop-to-ground, bag rearrangement during pickup, multiple ground items on one cell, shopping, crafting, or tool storage in the bag.

**Dependencies:** `M9.4`, `M5.1`, `M5.6`, and the M9.2 temporary adapter, which this card removes.

**Allowed file area:** Core Board/Interaction/Items/Inventory/Turns, Presentation Pickup/Inventory, legacy food adapter removal, tests, and explicit UI assets.

**Acceptance criteria:**

- `Use now` remains available with a full bag when its effect is legal; `Store` is disabled/rejected without a legal placement;
- successful use/store removes exactly one matching board instance; `Leave`, cancel, invalid, or stale choice removes none;
- after `Leave` or no capacity, standing still does not prompt again; stepping off and re-entering does;
- the unchanged ground instance does not reroll on re-entry or reload;
- accepted movement and choice produce one turn/hostile phase; rejected composite command is free and leaves both states unchanged;
- carried discard still destroys rather than drops;
- a revealed shovel-cache reward follows the same choice, remains the same ground instance on `Leave`, and never restores a resource through a bypass;
- automatic food pickup no longer exists after this card, and O-003 transition is documented in handoff.

**Test plan:** EditMode every option with empty/full/fragmented bag, capped/illegal effect, leave/re-entry, stale IDs, transfer rollback, duplicate submit, and replay; PlayMode prompt/input/re-entry/reload plus regression for tool pickup.

**Save and compatibility impact:** Uses M9.3 inventory and existing board-boundary persistence. If M8.1 exists, the same exact ground/carried instance transition must survive mid-board snapshot without duplication.

**Required handoff:** Option availability table, atomic transfer sequence, before/after entity identity evidence, O-003 migration note, and video/screenshots of leave then re-entry.

---

## `M9.6` — Quick-pocket composite turn

**Status:** `Deferred`
**Priority:** P1
**Related contract:** sections 10, 18, 21.2, and 27.8.

**Rationale:** Side pockets are valuable because preparation avoids a separate bag turn. Allowing both pockets—or a pocket plus bag actions—before one enemy response would erase the tactical cost, so quick use must be one modifier on one ordinary command.

**Current behavior:** Pockets can be persisted after M9.3 but have no runtime use path. Every effect is otherwise a standalone part of its owning command.

**Expected outcome:** An optional `QuickItemUse` from either pocket may wrap one accepted non-inventory primary command. At most one item resolves, then the primary action/resource/hostile phases complete as one atomic turn. `Wait` supports intentional quick use without movement.

**Scope:** Command envelope/modifier, compatibility query for primary command types, validation and ordering, pocket removal/effect events, rejection atomicity, replay/hash, input selection, and minimal feedback.

**Non-goals:** No two-pocket use in one turn, quick use with `ManageInventoryCommand`, free standalone use, automatic emergency consumption, action queue, or moving an item into a pocket and using it in the same bag turn.

**Dependencies:** `M9.4`, `M9.5`, `M3.5`, and accepted O-009.

**Allowed file area:** Core Turns/Inventory/Items/Interaction/Tools, Presentation HUD/Input, replay/tests, and explicitly allowlisted UI assets.

**Acceptance criteria:**

- exactly zero or one pocket item can accompany a supported primary command;
- accepted composite command produces one turn, one resource cost, and one hostile/environment phase;
- rejected/stale primary command consumes neither item, satiety, tool charge, nor turn;
- bag command, empty/ineligible pocket, duplicate submit, and two-item payload are rejected atomically;
- either pocket can be used on consecutive turns, and `Wait` is a legal primary action;
- replay/hash reproduces the exact pocket, item, effect, primary command, and resulting state.

**Test plan:** Matrix of both pockets × supported primary commands × accepted/rejected/stale, two-item and bag incompatibility, starvation/exit/tool charge order, rapid input, replay, and PlayMode enemy-phase count.

**Save and compatibility impact:** No new persistent shape beyond M9.3 unless the command replay tag requires a version bump. Save boundaries contain only the completed result, never a pending modifier.

**Required handoff:** Supported-command matrix, exact event chronology, rejected-command state hashes, and proof that two quick items cannot resolve in one turn.

---

## `M9.7` — Backpack, hotbar, and route-context UI

**Status:** `Deferred`
**Priority:** P1
**Related contract:** sections 7.5, 18, 22, and 27.7–27.9.

**Rationale:** Spatial rules only create useful decisions when capacity, action budget, quick access, and route knowledge are legible before commitment. UI must expose the domain contract without becoming another owner of item state.

**Current behavior:** The HUD shows health/food and planned tool slots. There is no `B` flow, grid, pocket selection, action counter, pickup placement preview, or route context for capacity/satiety.

**Expected outcome:** `B` opens a modal draft for the `3×3` bag and two pockets; `Finish` clearly communicates its one-turn cost and `0/2..2/2` action budget. The bottom HUD order is `Left pocket | Tool slot 1 | Tool slot 2 | Right pocket`. Pickup, quick use, discard confirmation, invalid placement, caps/waste, and route context are readable without relying on color alone.

**Scope:** Final PC input/focus, bag/pocket grid and footprint preview, use/discard/action counter, confirm/error states, hotbar, quick-use selection, pickup placement, satiety/health/cost display, and route-choice panel showing length, rumor/history, capacity, pockets, and tool charges. Preserve the Day transition with all gameplay HUD hidden.

**Non-goals:** No final mobile layout, controller radial menu, drag physics as game logic, automatic packing, optimal-route score, full art polish, or new inventory rules.

**Dependencies:** `M9.4`–`M9.6`, `M3.6`, `M7.4`, `M7.5`, and accepted PC reference platform O-004.

**Allowed file area:** Presentation Inventory/HUD/Atlas/Input, required scenes/prefabs/assets under a closed allowlist, localization, and PlayMode tests. Domain changes require returning to the owning M9 card.

**Acceptance criteria:**

- UI is a projection/draft and cannot mutate authoritative inventory before accepted commit;
- all placements/actions and their costs are understandable by keyboard/mouse, with non-color signals and disabled animations;
- no-op `Finish` still warns/costs one turn; stale/invalid commit returns to a coherent state;
- the bag and route UI warn when the projected accepted command would reach starvation after all allowed item effects and the selected cost;
- quick-use selection permits one pocket and never appears for bag command;
- hotbar uses generic tool slots rather than hard-coded axe/shovel labels;
- route choice simultaneously exposes current need/capacity and atlas rumor/history/length without recommending one answer;
- Day screen displays only `Day N` and no health/satiety/tool/pocket HUD.

**Test plan:** PlayMode focus/input/modal/rapid-submit/reload/resolution matrix, non-color/animation-disabled checks, representative window sizes, keyboard/mouse smoke, route-panel states, and short moderated comprehension test.

**Save and compatibility impact:** UI stores no gameplay state. Settings may persist accessibility preferences only; bag drafts and selection/focus never enter run/profile saves.

**Required handoff:** Screenshots/video of empty/full/invalid/two-action/pickup/quick/route states, input map, accessibility checklist, and mapping of every displayed value to its domain query/event.

---

## `M9.8` — Inventory content, economy integration, and release gate

**Status:** `Deferred`
**Priority:** P0 M9 gate
**Related contract:** sections 2, 3, 7, 17, 19, 22, 25, and 27.

**Rationale:** Correct inventory code can still make the game slower, encourage obsessive cleanup, or trivialize route decisions. M9 succeeds only if the complete economy creates understandable trade-offs and preserves the logic-first identity.

**Current behavior:** Individual M9 systems may pass focused tests, but there is no representative item set, route supply tuning, complete migration/replay gate, or evidence that players use the bag and atlas as intended.

**Expected outcome:** A small data-driven content set and controlled route supply produce food-versus-healing and carry-versus-use choices. Full automated validation, smoke, migration/recovery, and comparative playtests yield an explicit `Accept`, `Revise`, or `Remove/Defer` decision for M9.

**Scope:** Representative items (including `1×1` berries, `1×2` drink, medicine, at least one elongated quick item, and one intentionally weak but legible item), supply budgets/variation, fixed-source/tool interaction regression, full tests, performance/accessibility, and 8–12 observed sessions compared with the no-backpack baseline.

**Non-goals:** No large loot catalogue, rarity treadmill, crafting, shops, combat weapons, permanent inventory upgrades, telemetry upload, or content expansion before the gate passes.

**Dependencies:** `M9.1`–`M9.7` and accepted M7/M9 gate inputs.

**Allowed file area:** First tests/docs/config/GameData; production fixes are separate child tickets with closed allowlists and independent review.

**Acceptance criteria:**

- zero known duplication/silent-loss paths and no P0/P1 defects;
- full EditMode/PlayMode, replay/hash, save migration/recovery, generator supply, fixed-source, and deterministic batch gates pass twice;
- caps, cost tier, pickup, two-action bag budget, quick-use limit, and one-hostile-phase atomicity match section 27;
- route rumors never lie and observed ranges are not mistaken for guarantees;
- testers make at least some route choices from current health/satiety/capacity plus atlas knowledge;
- observed behavior shows less compulsion to clear every board and no dominant “always overeat/always hoard/always discard” policy;
- session length and interaction time remain acceptable relative to the no-backpack baseline;
- the owner records `Accept`, `Revise`, or `Remove/Defer` with evidence.

**Test plan:** Complete automated suites twice, 10,000-seed supply/softlock gate, old-save migration and crash recovery, full PC smoke, accessibility/input matrix, replay divergence injection, and pre-scripted comparative playtests with behavioral observations rather than preference-only surveys.

**Save and compatibility impact:** Validate upgrade from the last supported pre-M9 schemas and all intermediate M9 schemas; backup/recovery is mandatory. Failure may disable/retire the active run only through an explicit safe policy and must never erase the profile/atlas.

**Required handoff:** M9 release report, item/supply registry, all exact test results, migration matrix, unresolved defects, anonymized playtest observations, comparison to no-bag baseline, and final owner verdict.

### M9 gate

M9 is complete only after `Accept` of `M9.8`. A technically functional bag is not sufficient: the feature must improve route/resource decisions without item loss, hidden action costs, excessive session friction, or erosion of the game's knowledge-first progression.

---

# Rejestr decyzji blokujących

| Decyzja | Najpóźniej przed | Działanie Coordinatora |
| --- | --- | --- |
| O-001 — atlas fiction/ownership | `M7.6` final narrative and `M7.7` summary | agree with the owner, then update the contract and player-facing text |

O-002, O-003, O-004, O-005, O-006, O-007, O-008, and O-009 are resolved in `GameDesignContract.md` for their stated scopes and are not execution blockers. A card may reopen one only through an explicit product decision; it must not keep a stale “unresolved” status.

### Rationale — dlaczego decyzje mają deadline

`Open` nie oznacza „agent wybierze rekomendację, gdy dojdzie do kodu”. Deadline zatrzymuje kartę przed utrwaleniem nieuzgodnionej semantyki, a jednocześnie nie blokuje niezależnych fundamentów.

# Kolejka wykonawcza

M0, M1, and M2 are complete; M3.1–M3.5 are `Done`, M3.6 is next, and no card is currently `Active`. The historical planning baseline was branch `feature/AddingBackpackPlan` at `87f135e8590b89d57ce84b225ce7d94fcf5e4e02`. M3.5 was implemented from the clean baseline `M3/TurnController` at `aec6486360cf6e8b789ac1b02fefe2ad0247079a`. The user must checkpoint the accepted M3.5 code and documentation before a new implementation writer begins.

The next safe sequence is:

1. checkpoint the accepted M3.5 code and documentation to establish a clean worktree;
2. continue `M3.6`–`M3.10` through their normal reviews;
3. execute `M3.11` and `M3.12` before M4 so generation, replay, atlas observations, and tool sources share stable route-segment identity;
4. complete the no-backpack vertical slice through `M7.8`, then make the explicit M9 verdict;
5. keep M9 deferred and last unless the owner explicitly changes that order.

`M0.9` remains deferred by owner decision. No implementation agent performs external history rewriting, exposes historical values, or runs BFG without a separate explicit request.

### Rationale — why M3.6 is next

The accepted M3.5 controller provides a tested, command-agnostic resolution boundary. M3.6 can now route input through that boundary and replay its results. M3.11/M3.12 and M9 can reuse the same boundary, while inserting route identity before M4 prevents generator-era rework.

# Zasada aktualizacji roadmapy

Po `Accept` Coordinator aktualizuje status karty oraz — tylko jeśli wynik zmienił faktyczne zależności — tę kolejkę. Nie dopisuje „przy okazji” nowej mechaniki do aktywnej karty. Nowe ustalenie projektowe najpierw trafia do `GameDesignContract.md` lub ADR, następnie do osobnej karty tutaj.

### Rationale — dlaczego roadmapa pozostaje żywa

Playtest może obalić hipotezę atlasu, intentów lub narzędzi. Roadmapa ma pozwalać zatrzymać inwestycję na bramce i zmienić kierunek jawnie, zamiast traktować pierwotną listę funkcji jak zobowiązanie niezależne od wyników.
