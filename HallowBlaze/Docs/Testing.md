# HallowBlaze Testing

## M4.6 Generation Gates

Current generation automation uses Unity CLI against the active project, not the old
M0.7 paths below. Follow `AGENTS.md` and the `unity-validate` skill for Editor connection
diagnosis. Use one Unity process at a time; no package or Editor-version change is needed.

From the Unity project root, run the default daily 1,000-board gate:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter HallowBlaze.Tests.EditMode.BoardGenerationSeedSeriesTests.Daily1000SeedsHaveNoInvalidBoardsAndRepeatExactly --output "$env:TEMP/HB-M46-daily.xml" -- -nographics -logFile "$env:TEMP/HB-M46-daily.log"
```

Run the explicitly selected 10,000-board gate before a generation milestone:

```powershell
unity test $project --mode EditMode --filter HallowBlaze.Tests.EditMode.BoardGenerationSeedSeriesTests.Milestone10000SeedsHaveNoInvalidBoardsAndRepeatExactly --output "$env:TEMP/HB-M46-milestone.xml" -- -nographics -logFile "$env:TEMP/HB-M46-milestone.log"
```

Both tests generate the entire series twice, compare deterministic summaries and hashes,
and persist both runtime measurements outside the project. Thresholds are fixed in the
test before execution: **0 invalid boards, 0 statically unreachable exits, at most 1%
fallbacks**, and **60 seconds / 300 seconds per daily / milestone pass**, respectively.
The daily gate runs in the ordinary EditMode suite. The milestone gate is NUnit `Explicit`
and must have its own fresh nonzero passed result; an expected skip in a full-suite run
does not satisfy milestone acceptance.

For focused verification of the measurement/failure recorder, use the fixture filter
`HallowBlaze.Tests.EditMode.SeedSeriesHarnessTests`. It tests known histograms and population
variance, independent repeated runs/culture, corrupt configs, threshold and timeout failures,
complete versus partial route totals, authored resource bands and fixed sources. Expected
negative-control reports contain deliberately invalid cases; they are not production failures.

The tests print external `%TEMP%/HB-M46-<unique-id>` directories. Each contains:

- `summary.json`: runtime, deterministic fingerprint, all histograms, counts, segment supply,
  whole-traversal edge totals/variance, builder selection and rejection-code counts;
- `failures.json`: every rejected or unexecuted case, including full address/seed/configuration,
  root seed, exact requirements, fixture/segment identifiers, reasons and attempt history.

Require a fresh Unity XML report with the expected test identity, executed count greater
than zero and no failures. Keep reports outside the project; do not commit generated JSON,
XML or logs. There is no player profile, save or external-service access in these tests.
For measured results, fixture coverage and limits, see
[M4.6 seed series](BoardGeneration.md#m46-seed-series-and-measurements).

## M0.7

Projekt używa Unity `6000.3.21f1` i Unity Test Framework `1.6.0`.

Zestawy testów:

- `Assets/Tests/EditMode/HallowBlaze.Tests.EditMode.asmdef`
- `Assets/Tests/PlayMode/HallowBlaze.Tests.PlayMode.asmdef`

Oba zestawy są oznaczone jako Unity Test Assemblies. EditMode działa w Edytorze; PlayMode może działać w Edytorze i w celu testowym Playera.

Test Runner może wyświetlać ten sam zestaw testów w więcej niż jednym widoku platformy. Standardowy NUnit `[Test]` może być wyświetlany w drzewie PlayMode oraz EditMode; nazwa zestawu identyfikuje jego właściciela. Widok Playera to osobny cel wykonania, nie osobna kopia testów.

## Unity Test Runner

Uruchamiaj każdą platformę testową w osobnym procesie Unity. Nie uruchamiaj dwóch instancji Unity dla tego projektu jednocześnie.

Po każdej komendzie trzeba sprawdzić `$LASTEXITCODE` i nie uruchamiać następnej instancji, zanim poprzednia się zakończy.

Z katalogu głównego repozytorium:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Unity.exe'
$project = 'E:\Repos\HallowBlaze\HallowBlaze'
& $unity -batchmode -nographics -projectPath $project -runTests -testPlatform editmode -testResults "$project\Temp\TestResults\M0.7-EditMode.xml" -logFile "$project\Temp\TestResults\M0.7-EditMode.log"
```

```powershell
& $unity -batchmode -nographics -projectPath $project -runTests -testPlatform playmode -testResults "$project\Temp\TestResults\M0.7-PlayMode.xml" -logFile "$project\Temp\TestResults\M0.7-PlayMode.log"
```

Dla filtrowanych testów infrastruktury użyj:
```powershell
& $unity -batchmode -nographics -projectPath $project -runTests -testPlatform editmode -testResults "$project\Temp\TestResults\M0.7-EditModeInfrastructure.xml" -logFile "$project\Temp\TestResults\M0.7-EditModeInfrastructure.log" -testFilter "HallowBlaze.Tests.EditMode.EditModeInfrastructureTests.EditModeAssemblyLoads"
```

```powershell
& $unity -batchmode -nographics -projectPath $project -runTests -testPlatform playmode -testResults "$project\Temp\TestResults\M0.7-PlayModeInfrastructure.xml" -logFile "$project\Temp\TestResults\M0.7-PlayModeInfrastructure.log" -testFilter "HallowBlaze.Tests.PlayMode.PlayModeInfrastructureTests.PlayModeAssemblyLoads"
```

## Build Playera dla M0.7

```powershell
& $unity -batchmode -nographics -projectPath $project -buildWindows64Player "$project\Build\M0.7Validation\HallowBlaze.exe" -logFile "$project\Temp\TestResults\M0.7-PlayerBuild.log" -quit
```

## Ścieżki logów i XML dla M0.7

- Log EditMode: `Temp/TestResults/M0.7-EditMode.log`
- XML EditMode: `Temp/TestResults/M0.7-EditMode.xml`
- Log PlayMode: `Temp/TestResults/M0.7-PlayMode.log`
- XML PlayMode: `Temp/TestResults/M0.7-PlayMode.xml`
- Log infrastruktury PlayMode: `Temp/TestResults/M0.7-PlayModeInfrastructure.log`
- XML infrastruktury PlayMode: `Temp/TestResults/M0.7-PlayModeInfrastructure.xml`
- Log infrastruktury EditMode: `Temp/TestResults/M0.7-EditModeInfrastructure.log`
- XML infrastruktury EditMode: `Temp/TestResults/M0.7-EditModeInfrastructure.xml`
- Log Playera: `Temp/TestResults/M0.7-PlayerBuild.log`
- Wykonanie: `Build/M0.7Validation/HallowBlaze.exe`

## Sprawdzenie buildu

```powershell
$LASTEXITCODE # Oczekiwany: 0
Get-ChildItem -Path "$project\Build\M0.7Validation" -Recurse -File | Measure-Object # Oczekiwany: liczba wszystkich plików builda > 0
Get-ChildItem -Path "$project\Build\M0.7Validation" -Recurse -File -Filter "HallowBlaze.Tests*.dll" | Measure-Object # Oczekiwany: count HallowBlaze.Tests*.dll = 0
```
