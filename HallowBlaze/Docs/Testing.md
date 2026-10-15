# HallowBlaze Testing

## M4.7 Domain Noise

Propagation rule: emission freezes a board-local immutable `BoardNoise` footprint. The source
is always audible; each orthogonal step costs one strength point, including detours. A reached
cell requires existing walkable terrain and no impassable obstacle. Missing/non-walkable ground
and impassable obstacles are excluded and stop that path; walkable obstacles, actors and items
do not block. A breadth-first search finds every cell within the inclusive path budget, with
coordinate-sorted output and no random hearing roll. Later terrain changes never recompute
that event's footprint. `NoiseEvent.Noise`, the active `BoardState` snapshots and debug display
share these immutable cells; future Listener code must use `IsAudible`, not a new distance rule.

`ActionNoiseProfile` is immutable configuration. The production prototype uses movement strength
**2**, interaction strength **4**, and lifetime **2**. Movement emits at its resulting cell;
interaction emits at the actor's cell. Wait is silent, and zero in either configured strength
suppresses that action's emission. Standalone `TurnResolver` defaults remain silent unless
configured. These prototype values are not axe balance or Listener behavior.

After an accepted player phase, `TurnController` ages only noise present before that command,
before executing locked enemy intents and planning the next batch. Newly emitted noise retains
its full lifetime. Thus lifetime 2 survives the emitting command and one subsequent accepted
command, then expires on the next accepted command. Rejection and blocked input do not age or
emit noise and do not change costs. An accepted terminal action still emits before the terminal
event. Old event/snapshot values remain immutable while active state receives aged replacements.
Clone copies the emission sequence and owns independent collections; each fresh board starts
empty. Noise is not added to save DTOs or carried to another board.

The board-owned debug view draws a thin outline at every audible cell plus `S<strength>` and
`T<remaining>` at the source. It reads snapshots only, without gameplay mutation or audio, and
is refreshed at noise replay, recovery and command completion (including silent Wait). Terminal,
replacement and disposal close its lifetime. Existing scenes, prefabs and audio assets are unchanged.

Run focused gates from the Unity project root, with one Unity process at a time:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter HallowBlaze.Tests.EditMode.BoardNoiseTests --output "$env:TEMP/HB-M47-model.xml" -- -nographics -logFile "$env:TEMP/HB-M47-model.log"
unity test $project --mode EditMode --filter HallowBlaze.Tests.EditMode.NoiseTurnTests --output "$env:TEMP/HB-M47-turns.xml" -- -nographics -logFile "$env:TEMP/HB-M47-turns.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.NoisePresentationTests --output "$env:TEMP/HB-M47-view.xml" -- -logFile "$env:TEMP/HB-M47-view.log"
```

The visual gate needs graphics, so do **not** add `-nographics` to its command. It compares
displayed cells with hearing queries, exercises production replay/Wait/disposal, and renders
strengths **1 / 3 / 5** side by side. Pixel checks require a nonempty image in every panel. The
test output prints `%TEMP%/HB-M47-visual-<unique-id>/noise-strengths-1-3-5.png`; this screenshot is
external evidence, not a source asset. Full EditMode and graphics-enabled PlayMode also guard
the existing command gate, resource costs, intent execution, animation parity and terminal flow.
Require a fresh nonzero passed XML report with the expected test identity, not only CLI success.

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
