# HallowBlaze Testing

## M6.2 Boulder Pushing

Only the exact `BoulderPushAction.Definition` (`landmark.boulder`, blocking interactive
obstacle) opts into `push-boulder`. Both runtime composers use the same interaction catalog
and resolver. The old opt-in `push` hook remains target-only for compatibility; it is not
the boulder action. Neither contextual feedback nor axe/shovel strength is a push alternative.
Published generators and scenes do not acquire new placements through this card.

`BoardState.CanPush` is a pure preflight. `TryPush` checks the complete operation before
replacing both immutable snapshots and actor/obstacle indices. The player advances into the
boulder's former cell and the boulder moves exactly one cardinal cell away. No view, physics,
RNG, resource charge or later phase participates in that commit.

| Condition | Result |
| --- | --- |
| Existing player and explicitly supported adjacent cardinal boulder | Eligible for remaining checks |
| Missing participant, wrong layer, diagonal or same-cell target | Reject, unchanged state |
| Destination outside bounds or coordinate overflow | Reject, unchanged state |
| Terrain missing, non-walkable or an exit under the boulder or its destination | Reject, unchanged state |
| Actor or item on the boulder's current cell | Reject; no collision or collection on player advance |
| Any obstacle, actor or item at the destination | Reject; no chain, crushing or automatic collection |
| Both terrain cells walkable non-exits and required layers empty | Commit both positions atomically |
| Walkable authored plate-like terrain | Same occupancy rule; no plate activation or gate behavior before M6.3 |
| Captured target moved/disappeared, or destination became blocked after preview | Reject without cost, noise aging or later phases |
| Picker cancel or repeated confirm without a new selection | No additional command or cost |

The resolver applies the shared ordinary action-cost policy once (currently one Food),
spends no tool charge, and emits the existing interaction noise (strength 4, lifetime 2)
from the advanced player's cell against the new occupancy. Player-phase event order is:

```text
InteractionPerformed -> BoulderPushed -> ActionCostApplied -> Noise -> [PlayerStarved]
```

`BoulderPushedEvent` carries both origins and destinations as one grouped effect, not two
independent moves. Existing pre-command noise/pit aging and terminal handling remain
controller-owned. If nonterminal, locked enemies, environment and next planning run once
in their existing order. A retained enemy step into the moved boulder emits `EntityWaited`
without replanning; a fresh path query immediately reads the new occupancy/player cell.
Starvation retains the committed push but skips later phases. Rejection preserves resources,
entity snapshots, noise lifetimes and enemy opportunities.

The presenter interpolates both registered views with one shared progress value, or snaps
both with animations disabled. Replay cancellation recovers both from committed state;
it does not refund or resolve again. The one-cell `Assets/Prefabs/Tiles/Boulder.prefab`
contains only Transform and SpriteRenderer. Its PNG and metadata are Unity-authored;
`HallowBlaze/Authoring/Rebuild Boulder View` is an explicit opt-in Editor operation, never
normal gameplay or test setup. The domain never infers rules from this sprite or prefab.

Run focused checks sequentially from the active Unity project, with graphics for PlayMode:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.BoulderPushTests;HallowBlaze.Tests.EditMode.BoulderPrefabTests;HallowBlaze.Tests.EditMode.BoardRuntimeCompositionTests' --output "$env:TEMP/HB-M62-state.xml" -- -nographics -logFile "$env:TEMP/HB-M62-state.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.BoulderPushPresentationTests --output "$env:TEMP/HB-M62-presentation.xml" -- -logFile "$env:TEMP/HB-M62-presentation.log"
```

Coverage includes all four directions, bounds/overflow, missing/blocked terrain, actors/items,
single-chain rejection, clone ownership, stale/replayed commands, cost/noise/terminal ordering,
immediate path queries, retained enemy blocking, both composers, synchronized animation,
rapid input, independent modal ownership and cancellation recovery. Real-sprite pixel/framing
checks write before/after captures outside the project as
`%TEMP%/HB-M62-push-<before|after>-<width>x<height>.png` at 1280x720 and 320x640.
Require full EditMode and graphics PlayMode plus fresh nonzero XML with zero failures.
Run v4/profile v2 are unchanged; there is no mid-board save, M6.3 activation or M4.9 human verdict.

## M6.1 Landmark Authoring

The engine-free template loader uses externally supplied `BoardValidationCatalog` definitions;
it never infers traits from names, reads a prefab or mutates the active run/profile. See the
[schema and sanity fixture](BoardGeneration.md#m61-authored-landmark-templates) for fields,
two named goal declarations, the mandatory-tool guarantee and the deferred activation boundary.

Run the focused data and compatibility gate from the active Unity project:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.LandmarkTemplateTests;HallowBlaze.Tests.EditMode.BoardBlueprintTests;HallowBlaze.Tests.EditMode.WorldDefinitionTests' --output "$env:TEMP/HB-M61-structural.xml" -- -nographics -logFile "$env:TEMP/HB-M61-structural.log"
```

Cases compare authored and ordinary blueprints, state export/reconstruction and immutable input
ownership. Invalid schemas/types/casing, duplicate keys/IDs, unresolved content/goals, foreign
placements and configuration drift reject before publication. A mandatory tool must be a concrete,
matching, observable and statically accessible spawn in every allowed variant, without crossing
an exit first. An optional tool-dependent outcome remains legal without a guarantee. This check
does not solve a future dynamic puzzle or claim tactical survival.

The separate fixture world passes graph validation for five edge-based days with eight boards;
the final road has an ordinary entry and a landmark arrival segment. The test uses actual
`RunState` travel and `BoardAddressFactory`, then current run v4 JSON round-trip to reconstruct the
same fixture blueprint/template/variant. The open fixture passes the existing board validator
with two tool-free exits and explicit budgets. Published world/generator content and run/profile
schemas remain unchanged. Named outcomes are declarations, not persisted completion results;
M6.4 owns activation and its compatible outcome-save boundary, M6.5/M6.6 solving and playtests.

Full EditMode protects the shared generation assembly and its unchanged ordinary-board encoding;
graphics PlayMode protects existing runtime composition and gameplay. Require fresh nonzero XML
with zero failures, not only a CLI completion line. No M4.9 human result is inferred from this gate.

## M5.7 Field Notes

The immutable authored registry is `Assets/GameData/Facts/Resources/SystemFacts.json`.
`FactKnowledge` owns observation-to-profile discovery outside Unity/UI, while
`FactDiscoveryRecorder` checkpoints only new facts before releasing feedback. Failed checkpoints
retain pending events for a subsequent observation or explicit commit retry. Profile v2/run v4
are unchanged; pre-existing axe/pit IDs become notes without another discovery notification.

| Stable ID | Required Presented Observation | Title / Text | Icon / Source System |
| --- | --- | --- | --- |
| `AXE_CREATES_LOUD_NOISE` | Actual axe `NoiseEvent` after its footprint/feedback, not manual clearing or preview | **A loud shortcut**: The axe clears soft wood quickly, but its loud noise travels farther than working by hand. | `axe` / Tools / Noise |
| `PIT_TRAPS_SHAMBLER` | Actual `ShamblerTrappedEvent` after pit/hold feedback, not pit creation | **Held by the ground**: A Shambler that steps into a pit waits there until the pit expires. The pit causes no damage. | `pit` / Shovel / Shambler |
| `LISTENER_FOLLOWS_NOISE` | `ListenerInvestigatedEvent` after the preceding actual movement; blocked/expired/losing-initiative Wait emits none | **Following the sound**: The Listener follows a heard noise toward its source. Its announced step stays fixed; a new sound can change the next plan. | `hearing` / Listener / Noise |

These are mechanical observations, not M7.5 tool-source sightings or atlas location knowledge.
The reader displays only profile-owned entries, with a neutral placeholder for a missing
definition and no unknown checklist/count/percentage. Reading or selecting an unknown ID never
discovers anything. The book control (or `N`) opens notes; close/`Escape` releases only their own
input owner after the current frame. Discovery feedback persists until acknowledged, queues later
discoveries and remains readable with animations disabled. It hides behind the open reader and
returns after closing. Death and a new run retain notes; explicit New Game resets them.

Run sequentially from the active project, with a graphics device for the UI gate:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.FactKnowledgeTests;HallowBlaze.Tests.EditMode.PersistenceDtoTests;HallowBlaze.Tests.EditMode.PersistenceStorageTests;HallowBlaze.Tests.EditMode.ListenerTurnTests' --output "$env:TEMP/HB-M57-state.xml" -- -nographics -logFile "$env:TEMP/HB-M57-state.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.FieldNotesPresentationTests --output "$env:TEMP/HB-M57-notes-play.xml" -- -logFile "$env:TEMP/HB-M57-notes-play.log"
```

Tests cover typed triggers, idempotency, unknown/missing projections, isolated checkpoint
failure/retry, pre-Field-Notes v2 saves and round-trip/new-run/death retention. Production
`GameManager`/composer/presenter scenarios cover actual axe/pit/Listener replay versus preview or
unpresented execution, independent interaction/pause ownership, free reading, queued feedback,
animation parity and New Game replacement. Render checks include text bounds, distinct bitmap
icons and nonblank pixels at 1280x720 and 320x480; captures are written outside the project as
`%TEMP%/HB-M57-notes-<width>x<height>.png`. Full EditMode and graphics PlayMode remain required.
Read fresh nonzero XML; CLI completion alone is not PASS. This does not close M4.9's human gate.

## M5.6 Conscious Tool Pickup

Only concrete board-owned tools enter the equipment picker; movement never collects them.
An accepted pickup fills the first free generic slot. With full equipment, opening the comparison
and selecting a slot are free drafts; only confirmed `Replace` submits a complete command.
The old tool remains at the original ground binding with the same ID and charges, without
inheriting the incoming authored source ID. `Leave`, Escape, cancellation, stale choices and
full-slot refusal preserve ground/equipment without cost. Preview/pickup allocates no new ID,
spends no charge and never refills a depleted tool. O-003 automatic food remains reward-before-cost.

Run focused verification sequentially from the active Unity project root:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.ToolPickupTests;HallowBlaze.Tests.EditMode.BoardRuntimeCompositionTests' --output "$env:TEMP/HB-M56-domain.xml" -- -nographics -logFile "$env:TEMP/HB-M56-domain.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.ToolPickupPresentationTests --output "$env:TEMP/HB-M56-view.xml" -- -logFile "$env:TEMP/HB-M56-view.log"
```

Domain cases cover first-free and full-slot transfer, exact source/instance identity, stale and
round-trip replay, duplicate/unallocated/already-equipped bindings, invalid targets, clone,
depleted tools, single noise/cost, authored traits, isolated run save round-trip and unchanged
automatic food. Composition covers both legacy descriptors and explicit blueprint/source bindings.
PlayMode uses the production picker/coordinator and checks comparisons, independent input blocks,
free cancellation/Leave, rapid confirmation, stale replacement, animation parity and actual scene
unload followed by save/reload and HUD recomposition. Pixel checks render current ground/equipment
labels at 960x540 and 320x640, writing `%TEMP%/HB-M56-exchange-<width>x<height>.png` outside the project.
These captures cover the ground/HUD, not the IMGUI modal itself; modal labels and behavior are
checked through the real picker. Full EditMode and graphics-enabled PlayMode remain regression gates.
Require fresh nonzero XML with zero failures, not only a CLI completion message.

Run schema v4 and profile schema v2 are unchanged. Ground instances remain transient board state;
no mid-board save system, source knowledge, consumable modal, bag/pockets or generic discard/drop
is implemented here. This automated gate does not replace M4.9 human prediction/readability evidence.

## M5.5 Shovel

Marked `legacy.buried-food` caches require three manual actions or one shovel use. Completion
creates one concrete `shovel.cache-ration` item with a source entity and stable board-local
instance ID before the pre-M9 compatibility adapter collects it. Food +10 precedes action cost;
overflow rejects before work, reveal or charge mutation. The same reward can remain ground
state without changing its identity when immediate transfer is disabled.

Only explicitly authored `LegacyBoardContentKind.DiggableGround` / `shovel.diggable` terrain
supports pits. Published generator configurations do not place this new content automatically.
A fresh visible pit retains four subsequent accepted commands; pre-command pits age before
enemy execution, never on rejection. Actual Shambler entry holds it until expiry without damage.
An already announced Wait still executes when the pit expires. Player and Listener movement
remain unaffected, so the exit never requires a shovel. Creation alone grants no knowledge;
only ordered presentation of `ShamblerTrappedEvent` records `PIT_TRAPS_SHAMBLER`.

Run focused checks sequentially against the active project:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.ShovelTests;HallowBlaze.Tests.EditMode.BoardStateTests;HallowBlaze.Tests.EditMode.BoardRuntimeCompositionTests' --output "$env:TEMP/HB-M55-domain.xml" -- -nographics -logFile "$env:TEMP/HB-M55-domain.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.ShovelPresentationTests --output "$env:TEMP/HB-M55-view.xml" -- -logFile "$env:TEMP/HB-M55-view.log"
```

The graphics gate exercises both cache routes and animation modes, visible work/lifetime/charge
labels, ordered observation, isolated profile/run round-trips and reset. Pixel-checked real-sprite
captures are written outside the project as `%TEMP%/HB-M55-cache.png` and `%TEMP%/HB-M55-pit.png`.
Full EditMode and graphics PlayMode also protect the existing axe, Listener, picker and lifecycle
paths. Require fresh nonzero XML with zero failures. These automated checks do not close M4.9's
human prediction/readability gate. Pits and uncollected cache rewards remain transient board state;
run schema v4 and profile schema v2 are unchanged.

## M4.8 Listener

**Rule:** The Listener remembers the latest noise audible at emission and takes the announced
shortest-path step toward its fixed source while that signal is active; otherwise it waits.

The [accepted rule](GameDesignContract.md#1132-accepted-listener-rule---m48) specifies event-time
hearing, emission-ID tie-breaking, lifetime and blocked-step semantics. Memory belongs to
`BoardState`, not a view or a save DTO. Source actors need not remain present. Planning is read-only;
new noise cannot replace a locked opportunity. Investigation is not an unannounced attack, including
when a player occupies its next cell. No player knowledge, perception RNG or hidden alert state is used.

| Example | Announced / actual outcome |
| --- | --- |
| No audible noise, even next to the player | Target-free Wait; no pursuit or attack |
| Older loud event followed by a quieter audible event | Higher emission ID becomes the remembered source |
| New event is outside the Listener's footprint | Previous heard target remains unchanged |
| Listener moves into an older footprint after emission | No retroactive hearing |
| Equal shortest routes toward a source | North, East, West, South first-step priority |
| New noise after an investigation was announced | Execute the old fixed step; new source controls next planning |
| Source actor leaves or is removed | Keep the original source cell while the event is active |
| Signal expires before execution or step becomes occupied/impassable | Consume the opportunity as Wait, without replanning |
| Latest signal expires while an older event remains active | Forget the target; never revive the older event |
| Several actors claim one destination | Ascending actor ID executes first; later actors see live occupancy |
| Player command is rejected, or terminates before enemy phase | Do not consume or replace the retained intent |

The production runtime accepts explicit `LegacyBoardContentKind.Listener` descriptors without a
damage variant, producing stable `enemy.listener` actors and initial Wait intents. `Enemy1` and
`Enemy2` remain Shamblers; published ordinary/legacy generation configurations are unchanged.
The intent display publishes both archetypes in the same actor-ID order. An investigation shows
a magnifier at the immediate step and a linked diamond at the heard source. Both remain frozen
until replay/recovery completes; expiration and board lifetime boundaries clear them normally.

Run focused verification from the Unity project root:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.(ListenerNoiseMemoryTests|ListenerTests|ListenerTurnTests)' --output "$env:TEMP/HB-M48-domain.xml" -- -nographics -logFile "$env:TEMP/HB-M48-domain.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.ListenerPresentationTests --output "$env:TEMP/HB-M48-view.xml" -- -logFile "$env:TEMP/HB-M48-view.log"
```

The PlayMode scenario uses the real composer, controller and event presenter: an adjacent
interaction emits noise, the initial Listener Wait executes, Investigate is shown before input,
and the player then leaves while the Listener moves to the exact previously announced step.
Both animation modes check source/step markers, rejection stability, resource costs, expiration
and disposal. Full EditMode and graphics-enabled PlayMode remain regression gates for Shambler
paths/cadence/attacks, mixed initiative and presentation. Keep fresh XML/log output outside the
project. M4.9 human prediction/playtests and M8.7 portable replay are not claimed by these tests.

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
