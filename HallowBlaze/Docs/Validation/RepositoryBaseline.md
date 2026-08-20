# HallowBlaze — baseline repozytorium po migracji

> Ticket: **HB-000B**
> Data audytu: **2026-08-20**
> Repozytorium Git: `E:/Repos/HallowBlaze`
> Projekt Unity: `E:/Repos/HallowBlaze/HallowBlaze`
> Kontrakt: [`../GameDesignContract.md`](../GameDesignContract.md), sekcja 22 i Appendix A
> Roadmapa: [`../TechnicalRoadmap.md`](../TechnicalRoadmap.md), karta HB-000B

## 1. Cel i zakres

Ten dokument zapisuje stan zastany po migracji projektu, zanim kolejne tickety zaczną usuwać artefakty z indeksu, zmieniać serializację Unity albo kod rozgrywki. Pozwala odróżnić problemy istniejące przed daną zmianą od regresji powstałych w jej wyniku.

Audyt był read-only z wyjątkiem utworzenia tego raportu. Nie uruchomiono Unity, nie zmieniono indeksu Git, nie wykonano stagingu ani commita, nie usunięto i nie przeniesiono plików. Nie odczytywano lokalnych save'ów i nie wykonano żadnego połączenia sieciowego.

### Oznaczenia dowodu

- **Fakt** — wynik polecenia read-only albo odczytu jawnego pliku konfiguracyjnego.
- **Wniosek** — interpretacja faktów; nie jest równoważna sprawdzeniu ustawienia w Unity Editor.
- **Zgłoszenie właściciela** — informacja podana wcześniej przez właściciela projektu, bez niezależnego powtórzenia w tym audycie.
- **Not run** — kontrola nie została wykonana i nie jest raportowana jako zaliczona.

## 2. Tożsamość repozytorium i HEAD

| Pole | Wynik | Rodzaj dowodu |
| --- | --- | --- |
| Git root | `E:/Repos/HallowBlaze` | Fakt: `git rev-parse --show-toplevel` |
| Branch | `master` | Fakt: `git branch --show-current` |
| HEAD | `7c734297d4f7` | Fakt: `git rev-parse --short=12 HEAD` |
| Upstream widoczny lokalnie | `origin/master`, bez znacznika ahead/behind | Fakt: `git status --short --branch`; nie wykonano `fetch` |
| Liczba wpisów w indeksie | 400, z czego 398 pod `HallowBlaze/` | Fakt: `git ls-files` |
| Zmiany staged | 0 | Fakt: `git diff --cached --name-only` |

## 3. Working tree należący do właściciela

Poniższy snapshot wykonano przed utworzeniem tego raportu. Sam raport zwiększa później liczbę plików untracked o jeden. Żadnego z wymienionych plików nie wolno cofać ani automatycznie „porządkować”.

### 3.1. Zmodyfikowane pliki śledzone

**Fakt:** 14 plików, zgrupowanych jako 11 pod `Assets/` i trzy pod `ProjectSettings/`:

```text
Assets/Prefabs/Enemy1.prefab
Assets/Prefabs/Enemy2.prefab
Assets/Scenes/Menu.unity
Assets/Scripts/Enemy.cs
Assets/Scripts/GameManager.cs
Assets/Scripts/Loader.cs
Assets/Scripts/ManageRecords.cs
Assets/Scripts/MenuScripts/RestartBttnScript.cs
Assets/Scripts/MovingObject.cs
Assets/Scripts/PlayerScript.cs
Assets/Sprites/Scavengers_SpriteSheet.png.meta
ProjectSettings/EditorBuildSettings.asset
ProjectSettings/ProjectSettings.asset
ProjectSettings/ProjectVersion.txt
```

### 3.2. Pliki nieśledzone

**Fakt:** przed utworzeniem raportu `git ls-files --others --exclude-standard` zwracał 93 pliki:

| Grupa | Liczba | Uwagi |
| --- | ---: | --- |
| `.gitignore` | 1 | rezultat HB-000A; reguły już działają mimo braku wpisu w indeksie |
| `.vscode/` | 3 | konfiguracja przenośna, celowo nieignorowana |
| `.vsconfig` | 1 | konfiguracja przenośna, celowo nieignorowana |
| `AGENTS.md` | 1 | zasady pracy agentów |
| `Assets/` | 74 | Audio: 6; MobileDependencyResolver wraz z `.meta`: 21; Resources wraz z `.meta`: 3; Sprites: 44 |
| `Docs/` | 2 | `GameDesignContract.md` i `TechnicalRoadmap.md` przed dodaniem tego raportu |
| `Packages/` | 2 | `manifest.json` i `packages-lock.json` |
| `ProjectSettings/` | 9 | nowe ustawienia powstałe przy migracji |
| **Razem przed raportem** | **93** | brak untracked files w root repo poza `HallowBlaze/` |

Po utworzeniu `Docs/Validation/RepositoryBaseline.md` oczekiwana liczba wynosi 94. Katalog `Docs/Validation/` nie otrzymuje ręcznie tworzonego `.meta`, ponieważ leży poza `Assets/`.

## 4. Wersja Unity i migracja

**Fakty:**

- bieżący `ProjectSettings/ProjectVersion.txt` wskazuje `6000.3.21f1` z rewizją `c02631ffc030`;
- wersja tego samego pliku w `HEAD` wskazuje `2019.4.37f1` z rewizją `019e31cfdb15`;
- plik jest obecnie zmodyfikowany względem `HEAD`.

**Wniosek:** dirty working tree obejmuje niezacommitowaną migrację pomiędzy tymi wersjami. Audyt nie rozstrzyga, które automatyczne zmiany Unity są konieczne; wszystkie pozostają własnością właściciela.

## 5. Pakiety

### 5.1. Spójność manifestu i lockfile

**Fakty:**

- `Packages/manifest.json` i `Packages/packages-lock.json` istnieją, są poprawnym JSON-em i oba są untracked;
- manifest deklaruje 50 bezpośrednich zależności: 34 moduły `com.unity.modules.*` i 16 pozostałych pakietów;
- lockfile zawiera 62 wpisy łącznie;
- każda bezpośrednia zależność manifestu występuje w lockfile z tą samą wersją; brak brakujących wpisów i rozbieżności wersji.

Bezpośrednie pakiety inne niż moduły wbudowane:

| Pakiet | Wersja |
| --- | --- |
| `com.unity.2d.sprite` | `1.0.0` |
| `com.unity.2d.tilemap` | `1.0.0` |
| `com.unity.ads` | `4.19.0` |
| `com.unity.ai.assistant` | `2.17.0-pre.1` |
| `com.unity.ai.inference` | `2.6.1` |
| `com.unity.ai.navigation` | `2.0.14` |
| `com.unity.analytics` | `3.8.2` |
| `com.unity.collab-proxy` | `2.12.4` |
| `com.unity.ide.rider` | `3.0.40` |
| `com.unity.ide.visualstudio` | `2.0.27` |
| `com.unity.multiplayer.center` | `1.0.1` |
| `com.unity.purchasing` | `5.4.2` |
| `com.unity.test-framework` | `1.6.0` |
| `com.unity.timeline` | `1.8.12` |
| `com.unity.ugui` | `2.0.0` |
| `com.unity.xr.legacyinputhelpers` | `2.1.13` |

Spójność JSON-u nie dowodzi, że Unity pobrało i skompilowało wszystkie pakiety. Tę część może potwierdzić dopiero uruchomienie Editora po bezpiecznym checkpointcie.

### 5.2. Stan testów projektu

| Kontrola | Wynik |
| --- | --- |
| Unity Test Framework w manifest/lock | `1.6.0` / `1.6.0` |
| `.asmdef` i `.asmref` pod `Assets/` | 0 |
| pliki C# pasujące do `*Test*.cs` albo `*Tests*.cs` pod `Assets/` | 0 |
| katalogi `Test`, `Tests`, `EditMode` albo `PlayMode` pod `Assets/` | 0 |
| uruchomienie Test Runnera | **Not run** |

**Wniosek:** sam framework jest zadeklarowany, ale repo nie ma jeszcze uruchamialnej infrastruktury testowej projektu. Odpowiada za nią istniejący ticket HB-000F.

## 6. Serializacja assetów i `ProjectSettings`

### 6.1. Metoda rozpoznania

Audyt czytał wyłącznie pierwsze 512 bajtów każdego badanego pliku. Sygnatura `%YAML` oznacza Unity YAML; obecność bajtu NUL bez nagłówka YAML jest raportowana konserwatywnie jako `binary-or-null`. Taka kontrola rozpoznaje format, ale nie dowodzi, że Unity poprawnie deserializuje asset ani że wszystkie referencje są zachowane.

### 6.2. Kluczowe assety Unity

Wszystkie 57 plików jest śledzonych. Żaden nie ma sygnatury Unity YAML; każdy został sklasyfikowany jako `binary-or-null`.

| Kategoria | Łącznie | Unity YAML | `binary-or-null` |
| --- | ---: | ---: | ---: |
| sceny `.unity` | 2 | 0 | 2 |
| prefaby `.prefab` | 30 | 0 | 30 |
| animacje `.anim` | 21 | 0 | 21 |
| kontrolery `.controller` | 3 | 0 | 3 |
| override controller `.overrideController` | 1 | 0 | 1 |
| **Razem** | **57** | **0** | **57** |

### 6.3. `ProjectSettings`

**Fakt:** pod `ProjectSettings/` znajduje się 26 plików:

| Stan Git i sygnatura | Liczba |
| --- | ---: |
| tracked, `binary-or-null` | 15 |
| tracked, inny tekst | 2 |
| untracked, Unity YAML | 6 |
| untracked, JSON | 3 |

Piętnaście binarnych ustawień to:

```text
AudioManager.asset
ClusterInputManager.asset
DynamicsManager.asset
EditorBuildSettings.asset
EditorSettings.asset
GraphicsSettings.asset
InputManager.asset
NavMeshAreas.asset
NetworkManager.asset
Physics2DSettings.asset
ProjectSettings.asset
QualitySettings.asset
TagManager.asset
TimeManager.asset
UnityConnectSettings.asset
```

**Fakt:** `ProjectSettings/VersionControlSettings.asset` jest tekstowym YAML-em i zawiera tryb `Visible Meta Files`. Plik ten jest obecnie untracked.

**Fakt:** `ProjectSettings/EditorSettings.asset` jest binarny, dlatego jego pola konfiguracyjnego nie odczytano tekstowo.

**Wniosek zgodny z roadmapą:** brak YAML w 57 kluczowych assetach oraz binarny `EditorSettings.asset` wskazują, że projekt nie ma jeszcze utrwalonego baseline'u `Force Text`. Dokładnej wartości opcji nie potwierdzono w Editorze. Ustawienie i reserializacja są celowo rozdzielone na HB-000D oraz HB-000E.

## 7. Kompletność par asset–`.meta`

Audyt objął wyłącznie `Assets/`, bez odczytu katalogów generowanych.

| Kontrola | Wynik |
| --- | ---: |
| pliki inne niż `.meta` | 138 |
| katalogi wewnątrz `Assets/` | 16 |
| pliki `.meta` | 154 |
| obiekty bez odpowiadającego `.meta` | 0 |
| osierocone `.meta` bez obiektu docelowego | 0 |

**Fakt:** każda z 154 pozycji plik/katalog pod `Assets/` ma odpowiadający plik `.meta`, a każdy `.meta` ma istniejący cel.

**Ograniczenie:** kontrola nie bada unikalności GUID-ów, referencji między assetami ani stanu Unity Asset Database. Te własności muszą zostać sprawdzone podczas izolowanej reserializacji HB-000E.

## 8. Artefakty generowane nadal śledzone przez Git

Matcher został zbudowany z pełnego projektowego `.gitignore` utworzonego w HB-000A. **Fakt:** dokładnie 173 wpisy indeksu pasują do jego reguł dla artefaktów generowanych; wszystkie 173 istnieją lokalnie. Nie wykryto innych śledzonych wpisów pasujących do tych reguł.

| Ścieżka | Wpisy w indeksie | Pliki obecne lokalnie | Rozmiar tylko tych plików |
| --- | ---: | ---: | ---: |
| `HallowBlaze/Builds/` | 168 | 168 | 60 843 666 B (58,03 MiB) |
| `HallowBlaze/.vs/` | 5 | 5 | 5 304 278 B (5,06 MiB) |
| **Razem** | **173** | **173** | **66 147 944 B (63,08 MiB)** |

Rozkład wpisów: `Builds/` zawiera śledzone `v001.exe`, `UnityPlayer.dll` i 166 plików pod `v001_Data/`; wszystkie pięć wpisów `.vs/` leży pod `.vs/HallowBlaze/`.

`.gitignore` nie usuwa plików już znajdujących się w indeksie. Kontrolowane wycofanie wyłącznie tych dwóch ścieżek z indeksu jest zakresem HB-000C i wymaga wcześniej decyzji właściciela o archiwizacji historycznego buildu.

**Ograniczenie rozmiaru:** podane bajty są sumą istniejących plików wskazanych przez indeks. Zgodnie z bramką bezpieczeństwa nie wykonano rekurencyjnego skanu całych ignorowanych katalogów, więc liczby nie obejmują ewentualnych lokalnych plików nieśledzonych.

## 9. Znane ostrzeżenia i ryzyka baseline'u

| Obserwacja | Status dowodu | Dalsza obsługa |
| --- | --- | --- |
| 173 artefakty generowane pozostają w indeksie mimo działającego `.gitignore` | potwierdzony fakt | HB-000C |
| 57 kluczowych assetów oraz 15 ustawień ma format binarny lub zawiera NUL bez nagłówka YAML | potwierdzony fakt | HB-000D, następnie HB-000E |
| framework testowy jest zadeklarowany, ale brak test assemblies i testów projektu | potwierdzony fakt | HB-000F |
| jedna komenda ruchu może przechodzić przez dwie ścieżki rozstrzygnięcia | obserwacja przyjęta w Appendix A; nie reprodukowano jej w tym audycie | HB-000G |
| `Assets/Scripts/ManageRecords.cs` zawiera uprzywilejowaną wartość integracji leaderboardu po stronie klienta | kategoria ryzyka przyjęta w kontrakcie; wartości nie odczytano ani nie skopiowano | HB-000H; ticket pozostaje zablokowany do decyzji i rotacji właściciela |
| oba pliki `Packages/` i dziewięć plików `ProjectSettings/` są untracked | potwierdzony fakt | checkpoint właściciela po HB-000C; bez automatycznego stagingu |
| `git diff --check` raportuje 58 przypadków trailing whitespace w zastanej zmianie `Assets/Sprites/Scavengers_SpriteSheet.png.meta` | potwierdzony fakt; brak zmiany w HB-000B | zachować jako baseline i zweryfikować ponownie po HB-000E; bez ręcznej normalizacji assetu |
| Git ostrzega o przyszłej konwersji LF→CRLF dla `Scavengers_SpriteSheet.png.meta` i `ProjectVersion.txt`; brak `.gitattributes` w repo i projekcie | potwierdzony fakt; nie zmieniano zakończeń linii | nie naprawiać przy okazji; ocenić w izolowanym diffie serializacji/checkpointu |

Appendix A dokumentuje także legacy ownership stanu, szeroką odpowiedzialność `GameManager`, autorytatywny `Physics2D.Linecast`, generator łączący dane z widokiem, globalny `UnityEngine.Random`, HP ściany w komponencie widoku i brak jawnego zwycięstwa. HB-000B nie wykonywał ponownej analizy tych mechanik i nie tworzy dla nich nowych ticketów; ich migracja pozostaje w zaakceptowanej roadmapie po bramce M0.

## 10. Smoke test i ostrzeżenia Unity

- **Not run:** Unity Editor, kompilacja skryptów, Test Runner, otwarcie scen, PlayMode oraz przebieg menu → plansza.
- Powód: HB-000B jest audytem repozytorium, a właściciel poinformował, że Editor i Unity MCP są wyłączone. Coordinator jawnie zlecił niewłączanie Unity dla tego ticketu.
- **Zgłoszenie właściciela:** przed rozpoczęciem prac właściciel podał, że gra poprawnie ładuje się w nowej wersji Unity. Jest to użyteczny kontekst, ale nie zastępuje aktualnego smoke testu ani logu Console.
- **Fakt:** ten audyt nie ma aktualnego wyniku Console, dlatego nie raportuje żadnego ostrzeżenia runtime jako potwierdzonego ani nie twierdzi, że Console jest czysta.

## 11. Bezpieczne polecenia odtworzenia audytu

Polecenia są read-only. Pierwsza grupa zakłada katalog projektu Unity jako bieżący:

```powershell
git rev-parse --show-toplevel
git branch --show-current
git rev-parse --short=12 HEAD
git status --short --branch
git diff --cached --name-only
git diff --name-only
git ls-files --others --exclude-standard
git diff --check
```

Pełną listę 173 śledzonych artefaktów generowanych można uzyskać bez skanowania katalogów z dysku:

```powershell
git -C .. ls-files -- HallowBlaze/Builds HallowBlaze/.vs
```

Działanie reguł ignorowania można sprawdzić na nieistniejących ścieżkach-probach, bez tworzenia plików:

```powershell
git check-ignore -v --no-index -- Library/probe.tmp Temp/probe.tmp obj/probe.tmp Logs/probe.log Builds/probe.exe UserSettings/probe.asset .vs/probe.bin
```

Wersję Unity, składnię JSON oraz obecność infrastruktury testowej sprawdzono następująco:

```powershell
Get-Content -LiteralPath ProjectSettings/ProjectVersion.txt -Encoding UTF8
Get-Content -LiteralPath Packages/manifest.json -Encoding UTF8 -Raw | ConvertFrom-Json
Get-Content -LiteralPath Packages/packages-lock.json -Encoding UTF8 -Raw | ConvertFrom-Json
rg --files Assets -g '*.asmdef' -g '*.asmref'
rg --files Assets -g '*Test*.cs' -g '*Tests*.cs'
```

Audyt `.meta` porównywał każdą pozycję zwróconą przez `Get-ChildItem -LiteralPath Assets -Force -Recurse` z dokładną ścieżką `<pozycja>.meta`, a każdy znaleziony `.meta` ze ścieżką po usunięciu końcówki `.meta`. Audyt formatów używał `System.IO.File.ReadAllBytes` tylko dla plików z jawnie wybranych kategorii i klasyfikował pierwsze 512 bajtów; nie drukował zawartości plików.

## 12. Ograniczenia dowodu i otwarte działania

- Nie wykonano `fetch`; relacja z `origin/master` odzwierciedla wyłącznie lokalnie znany remote-tracking ref.
- Nie uruchomiono Unity, więc nie potwierdzono kompilacji, importu pakietów, scen, referencji, Console ani ustawienia `Force Text` w UI.
- Nie skanowano rekurencyjnie ignorowanych katalogów. Zbadano wyłącznie ich wpisy obecne w indeksie Git.
- Nie analizowano historii Git pod kątem sekretów i nie można na tej podstawie twierdzić, że historyczne buildy albo commity są bezpieczne do publikacji.
- Nie odczytano ani nie zacytowano wartości z integracji Dreamlo; audyt potwierdza wyłącznie nazwę pliku i kategorię ryzyka z kontraktu.
- Nie badano lokalnych danych gracza, cache Unity ani ustawień użytkownika.
- Audyt sygnatur nie zastępuje otwarcia assetów w Unity i kontroli `Missing Script`, GUID-ów oraz referencji.
- Następne działania pozostają istniejącymi kartami HB-000C–HB-000H. Ten raport nie otwiera nowych feature ticketów i nie rozpoczyna HB-000C.
