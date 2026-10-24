# Board generation data contract

M4.2 defines initial data, M4.3 adds an independent ordinary-board validator, and M4.4
connects a single-candidate model-first generator to production presentation. The engine-free
`HallowBlaze.Core.Generation` assembly uses board primitives/state, addressed PCG32 RNG,
stable travel identity, and the existing turn contracts and Shambler planner. M4.5 adds
bounded retries, authored fallbacks and recovery diagnostics under generation version 2.
M4.6 adds test-only daily/milestone seed sweeps and detached aggregate diagnostics.
This remains the first ordinary family, not final balance.

## Ownership and construction

- `BoardBlueprint` is an immutable initial ordinary 8x8 board with inclusive coordinates
  `0..7` on each axis. Its full `BoardAddress` carries run, leg, directed edge, stable
  segment, day, configuration identity/version, and board seed.
- `BoardGenerationConfig` is a detached immutable description of generation targets
  and allowed content. Its ID/version must match the address.
- `Entities` contains the initial terrain, obstacle, item, and actor layer entries,
  including their spawn positions. Immutable existing entity definitions are reused.
- `Start` is derived from exactly one player actor. `Exits` is the sorted set of
  positions marked by `IsExit`; it cannot contain duplicates or overlap the start.
  The legacy terrain/item pair at one exit position denotes one logical exit and both
  entities remain in the blueprint. Same-layer occupancy conflicts are always rejected.
- `CreateState()` builds an independent mutable `BoardState`. Subsequent moves, removals,
  and pickups cannot alter the initial blueprint, its fingerprint, or its resource counts.
  Generic spawn metadata remains on the blueprint; `BoardState` does not gain inventory,
  profile knowledge, or a second travel owner.

Construction rejects null inputs, unsupported bounds, duplicate entity IDs, layer-cell
conflicts, missing/duplicate player actors, missing exits, start/exit overlap, invalid
item bindings, and mismatched configuration identity. It deliberately does not prove
reachability, tactical fairness, catalog resolution, or compliance with budget targets.
M4.3 validates static reachability, catalogs, budgets, and the supported opening rules;
M4.4 generates and presents only a validated blueprint. Construction and raw-candidate validation
share one structural rule implementation rather than competing checks.

## M6.1 Authored Landmark Templates

`HallowBlaze.Core.Generation.Landmarks` adds an engine-free authoring path over the same
`BoardBlueprint` and `BoardState`. It does not invoke the ordinary generator, add a second
occupancy model, resolve puzzle goals, or grant rewards. M6.1 supports the existing 8x8 bounds;
larger authored boards are not silently squeezed into this format.

`LandmarkTemplateJson.Deserialize(json, catalog)` reads schema v1 against an externally supplied
`BoardValidationCatalog`. It returns an immutable `LandmarkTemplate` with a stable template ID,
positive content version, explicit generation configuration, at least one named variant and at
least two named outcomes, including a mandatory goal. It rejects duplicate/unknown fields,
incorrect field casing, scalar coercion, unsupported schemas, duplicate IDs, invalid layer/content
references, inconsistent item/family/source bindings, invalid board structure and missing goals.
Malformed input fails before publication; it is not repaired or replaced by ordinary generation.
JSON errors identify their field path; layout/goal errors include template and variant identity.

### Authoring Schema

The complete loadable example is [sanity-template.json](../Assets/GameData/Landmarks/sanity-template.json).
Every field below is required unless marked optional. Integers are Int32; IDs/names must be
nonblank strings without surrounding whitespace. Lists are explicit, including empty item or
resource-budget lists. Property names, layer names and textual identities are case-sensitive.

| Object | Fields | Contract |
| --- | --- | --- |
| Root | `schemaVersion`, `templateId`, `templateVersion`, `configuration`, `variants`, `outcomes` | Authoring schema 1; template identity is independent of any world node or segment. |
| Configuration | `id`, `version`, `pathLength`, `branchPoints`, `obstacleCount`, `enemyCount`, `maximumConcurrentThreats`, `minimumSafeStartMoves`, `allowedContentIds`, `resourceBudgets` | Existing `BoardGenerationConfig` contract; each range has integer `minimum`/`maximum`. Configuration ID/version must match the eventual `BoardAddress`. |
| Resource budget | `resourceFamilyId`, `itemCount` | Known family and the existing inclusive item-count range, not resource restoration amounts. |
| Variant | `id`, `entities`, `items`; optional `plates`, `gates` | Distinct stable ID; explicit detached layout and terrain-ID link bindings. No rotation, random selection or procedural puzzle creation is inferred. |
| Entity | `id`, `layer`, `contentId`, `x`, `y` | Board-local entity ID and exact existing catalog definition for `Terrain`, `Obstacle`, `Item` or `Actor`; no serialized traits, prefab or Unity ID. |
| Item | `instanceId`, `entityId`, `itemDefinitionId`, `resourceFamilyId`, `initiallyObservable`; optional nullable `sourceId` | Same generic spawn/binding rules as ordinary blueprints. A fixed source must resolve to this definition. |
| Outcome | `id`, `name`, `exitEntityId`, `optional`; optional nullable `requiredToolDefinitionId`, `guaranteedToolInstanceId` | Names a result and a distinct walkable terrain-exit marker present in every variant. It is not a solution script or an automatic reward grant. |

All public collections are read-only snapshots. Variant/outcome keys, rather than array indices,
identify content; the blueprint normalizes entity/item order. Repeated construction with the
same inputs is deterministic and does not consume gameplay or cosmetic RNG. Blueprint hashing
retains every address field and is still the existing comparison encoding, not portable replay.

### Goals And Tool Guarantees

An optional outcome may declare a required tool without promising it. A mandatory outcome may
not depend on random loot: a declared tool requirement must identify one concrete initial
`BoardItemSpawn` by `guaranteedToolInstanceId` in **every** permitted variant. Its definition must
match the requirement, resolve to the `tools` family, be allowed and initially observable. Its
cell must be statically reachable from the start without a tool, blocking obstacle or prior exit.
Merely naming a tool, claiming a source, or putting it behind its own exit is not a guarantee.
This static check ignores actor pressure and is not proof of tactical survival or puzzle solvability.

Structural template validation uses the existing blueprint rules. It does not prove target-budget
compliance or dynamically solve gates, boulders and plates. The open sanity fixture also passes
the ordinary `BoardValidator`, including tool-free exit reachability and its explicit budgets.
M6.2/M6.3 own mechanical rules; M6.4 owns meaningful costs/rewards and executed outcomes;
M6.5/M6.6 own solving and curated variants. The two sanity declarations are:

| Outcome ID | Working name | Terrain goal | Requirement |
| --- | --- | --- | --- |
| `arrival.safe` | Safe arrival | Entity `2`, cell `(6,6)` | Mandatory, tool-free |
| `arrival.alternate` | Alternate arrival | Entity `3`, cell `(6,1)` | Optional, tool-free |

These are plumbing examples, not two validated final puzzle solutions or accepted balance.

### Segment Assignment And Compatibility

[sanity-world.json](../Assets/GameData/Landmarks/sanity-world.json) is a separate authoring fixture,
not a replacement for the published prototype world. Its five directed roads contain eight boards
in total. The fifth inbound road contains an ordinary entry segment and final landmark segment
`segment.sanity.day5.arrival`; the destination already has `placeKind: landmark`.

[sanity-placement.json](../Assets/GameData/Landmarks/sanity-placement.json) uses authoring schema 1
with required `worldDefinitionId`, `worldVersion`, `segmentId`, `templateId`, `templateVersion`
and `variantId`. `DeserializePlacement(json, world, template)` validates both publication versions,
an existing source and landmark destination, final inbound segment, matching generation
configuration and a permitted variant. Node IDs, ordinal positions and `day % 5` never route to a
landmark. The assignment does not add an extra day or mark arrival before the final board ends.

`placement.CreateBoard(actualAddress)` rejects a different edge/segment/configuration and returns
`LandmarkBoard`: template ID/version, variant ID and the ordinary blueprint with the exact full
address. `LandmarkBoard.CreateState()` builds independent runtime state with explicit pressure
links; `Blueprint.CreateState()` alone does not install those bindings. M6.4 connects the actual
resolved exit to a named persistent result for the production template described below.

M6.1 adds no run/profile DTO fields, migration, reset or mid-board save. Existing run v4 travel
data round-trips the fixture's active segment/configuration/seed and reconstructs the same board
against the same frozen placement. M6.4 adds compatible run v5 selection/outcome fields as
described below; M6.6 must also pin any future random selection, not reroll it on load.
Do not change a published mapping/layout under the same generation contract. The authoring
fixture is not registered with `GameManager`, the scene bootstrap or ordinary fallback selection.

## Radio Tower Landmark

M6.4 publishes one authored `landmark.radio-tower` template, content version `1`, variant `base`:
[layout](../Assets/GameData/Landmarks/radio-tower-template.json) and
[assignment](../Assets/GameData/Landmarks/radio-tower-placement.json).
It replaces only `segment.ridge-radio-tower.arrival`, the fifth and final board of
`road.ridge-radio-tower` in `world-1/1`. The four inbound approach boards remain ordinary v2;
all intended atlas routes reach this edge on day five. No day/modulo test or extra travel day
selects the landmark. The earlier sanity fixture remains separate.

### Layout

Coordinates use the same lower-left grid origin as commands. `#` is absent/nonwalkable terrain,
not a destructible obstacle. Entity IDs and content are explicit in the template, not inferred
from this diagram.

```text
     x 0 1 2 3 4 5 6 7
  y
  7    # # # # # # # #
  6    # . . X # # # #
  5    # . . . # R Y #
  4    # . . . G W . #
  3    # . B A # # Z #
  2    # . . . # # # #
  1    # P T # # # # #
  0    # # # # # # # #
```

| Marker | Content / Meaning |
| --- | --- |
| `P` | Player `(1,1)`, entity `1` |
| `B` | Shared pushable `landmark.boulder`, entity `10` |
| `A` | Plate entity `127`, link `A`, `Boulders` pressure only |
| `G` | Gate entity `136`, link `A`, `All`; initially closed |
| `W` | Shared clearable `legacy.wall`, entity `11`; three manual actions or one axe use |
| `Z` | Shared `ordinary.shambler.10`, entity `2`, retained intent and alternating rest |
| `T` | Optional `axe.basic`, spawn `radio-tower.axe`, source `source.radio-tower.axe` |
| `R` | Ration entity `21`, spawn `radio-tower.rations`, definition `landmark.radio-tower.rations`, source `source.radio-tower.rations` |
| `X` | Mandatory `tower.arrival`, terrain exit `151` at `(3,6)` |
| `Y` | Optional `tower.supplies`, terrain exit `146` at `(6,5)` |

### Routes And Costs

The exact test notation is `N/E/W/S` for cardinal moves, `P` for the adjacent boulder interaction,
`C` for one manual clearing action, `T` for conscious axe pickup, and `A` for axe use on the wall.
These are ordinary commands submitted to the same `TurnController`, including live zombie phases.
They are developer documentation, not a separate in-game input language.

| Result / Route | Command Sequence | Accepted Actions / Food Cost | Charges | Concrete Reward | Food From 100 |
| --- | --- | --- | --- | --- | --- |
| `tower.arrival`, tool-free western bypass | `NNNNNEE` | 7 / 7 | 0 | None | 93 |
| `tower.supplies`, tool-free manual route | `NNPNEECCCENE` | 12 / 12 | 0 | One ration, +20 Food | 108 |
| `tower.supplies`, optional axe route | `TNNPNEEAENE` | 11 / 11, including pickup | 1 axe use | Same single ration, +20 Food | 109 |

The ration is collected by the existing item effect before the action cost, never by the
arrival callback. Picking up the axe is optional and uses the ordinary two-slot comparison/
replacement flow. Manual clearing emits shared strength-1 noise per action; the axe emits
strength-6 noise, and pushing uses the existing strength-4 profile. Costs and retained intent
are exposed through the existing picker/intent UI; plate/gate labels show their link, pressure,
combination and current state. Exit labels name the radio tower and supplies. There is no
landmark-only movement, reward command, zombie behavior, latch or minigame.

The western bypass never needs a tool, gate, clearing action or loot. The mandatory exit has
two approaches, `(2,6)` and `(3,5)`, so one abandoned boulder cannot seal both. Tests include
`NEP` followed by `WNNNEE`, and the upper-boundary mistake `NEPPP` followed by `EN`.
The optional supply route can be forfeited by bad pushes; the mandatory objective remains
available. Death/starvation cannot emit success, record an outcome or visit the destination.
The tested costs are working authored balance, not a human playtest verdict or an exhaustive
dynamic solvability proof. M6.5 owns the bounded solver; M6.6 owns the variant/playtest gate.

### Validation And Persistence

Strict authoring schema 1 now accepts optional `plates` (`terrainId`, `linkId`, `occupants`)
and `gates` (`terrainId`, `linkId`, `rule`) arrays. Absence means empty for old templates;
explicit null, unknown/case-variant/numeric rules, wrong terrain and orphan/duplicate links
are rejected. Every outcome has a distinct terrain-exit ID. `LandmarkBoard.Validate` retains
ordinary catalog, resource, opening and topology checks; only static `UnreachableExit` errors
for explicitly optional outcomes are exempt. Mandatory exits retain static tool-free reachability.
Executable command sequences, not that exemption, demonstrate the optional route.

Run schema v5 adds required `activeLandmark` (nullable) and `completedLandmarks`. Records contain
the full `BoardAddress`, template ID/version, variant ID and completed outcome ID (null only for
an active selection). Profile schema remains v2. Readers retain v1-v4 migrations, resources,
equipment, RNG and travel identity and initialize empty landmark state; there is no reset.

The selection is saved before board presentation. The optional ground tool is allocated from
that boundary's existing run allocator; Continue recreates the same instance without equipping
or refilling it. No mutable board, gate state, animation or ground-tool snapshot is persisted.
An unsupported/missing content identity fails startup rather than rerolling or substituting art.
Failure while composing a board restores its tool allocator and previous player view position.

The actual resolver exit maps to exactly one outcome. `RouteTravelService` saves the result and
final travel arrival atomically. A failed run write restores both records, leaving resolved board
effects intact for retry. Profile `Visited`/`Traversed` discovery follows the committed run history;
failed profile writes use existing arrival reconciliation without replaying the run save, turn
or reward. Duplicate/stale/unresolved/foreign results are rejected. Starting a new run clears
landmark state, not profile knowledge.

The exact named publication upgrade preserves already-saved legacy v1 and ordinary v1/v2
generation at this arrival segment. Those active legs keep their old layouts, seeds and RNG;
they are not silently converted to the new puzzle. Newly started inbound legs use the authored
identity. Published world/node/edge/segment IDs and world version remain unchanged.

`RadioTowerLandmark` is a build-included Resources asset referencing the two JSON files and
the existing boulder/plate/gate prefabs plus one visual-only axe prefab. The explicit Unity menu
`HallowBlaze/Authoring/Rebuild Radio Tower References` rebuilds only these references and axe art
through AssetDatabase/PrefabUtility. Normal tests/gameplay never run the authoring operation.

## Generic items and initial supply

`BoardItemSpawn` binds one non-exit item-layer entity to one textual instance ID,
item-definition ID, resource-family ID, optional authored fixed-source ID, and initial
observability flag. The definition ID must match the entity's `ContentId`. Instance IDs,
entity bindings, and non-null fixed-source IDs are unique within the board. Repeated
instances of one definition must agree on the resource family. The full instance identity
is the blueprint's `BoardAddress` plus the textual instance ID; it is not a Unity ID.

`InitialResources` is derived from the spawn descriptors and sorted by ordinal family ID:

- `ItemCount` counts all initial item instances in the family, including hidden content.
- `ObservableItemCount` counts only initially observable instances. Future observation
  logic must track subsequent actual reveals; total hidden supply must not be shown as
  observed atlas knowledge.

Counts are neither Food restoration amounts nor inventory stacks. One berry bundle is
one item. Exit markers never count as items. `AutomaticFoodReward` remains only existing
compatibility data and is not extended into the final item model. This module neither
collects items nor writes resource observations, taken-source state, or profile data.

## Budget dimensions

All `GenerationRange` bounds are inclusive, nonnegative targets. Construction alone does
not prove joint feasibility; M4.3 checks a candidate's measured compliance. M4.4 generates
a candidate against these targets and explicitly rejects unsupported joint budgets.

| Field | Unit / contract | Intended pressure |
| --- | --- | --- |
| `PathLength` | Shortest required start-to-exit path in cardinal moves, ignoring actors; `1..63` | Travel/resource cost without increasing board size |
| `BranchPoints` | Traversable cells with at least three traversable cardinal neighbors, ignoring actors; `0..64` | Alternate lines and maneuvering space |
| `ObstacleCount` | Occupied obstacle-layer cells; `0..64` | Constrained movement and choke points |
| `EnemyCount` | Initial enemy actors; `0..63`, reserving one actor for the player | Threat density, never the sole difficulty control |
| `MaximumConcurrentThreats` | Upper target for distinct enemies threatening one cell in one intent phase; `0..EnemyCount.Maximum` | Overlapping readable intents |
| `MinimumSafeStartMoves` | Lower target for legal non-threatened cardinal first-step destinations; `0..4` | Opening safety and available choices |
| `AllowedContentIds` | Non-empty unique set of stable entity/item-definition IDs | Which terrain, obstacles, items, and enemy content may be combined |
| `ResourceBudgets` | One initial item-count range per stable family; each `0..64` | Available supply independent of its use-effect magnitude |

The dimensions have no automatic combined difficulty score or final balance preset.
M4.3 measures threats only in the initial intent phase, not throughout future play.
A candidate can be structurally valid while missing a target or being unreachable.

## Example

This small construction/export example is not a registered content preset, a traversable
validated board, or an accepted balance configuration. IDs are illustrative. It omits
ordinary terrain cells and therefore fails semantic validation. A later generator must
validate its own complete output.

```csharp
var config = new BoardGenerationConfig(
    "ordinary-example", 1,
    new GenerationRange(8, 24), new GenerationRange(0, 64),
    new GenerationRange(0, 0), new GenerationRange(0, 0),
    0, 1,
    new[] { "player", "exit", "item.berries" },
    new[] { new ResourceGenerationBudget("food", new GenerationRange(1, 4)) });

var address = new BoardAddress("run-1", "leg-1", "north-road", "segment-a",
    1, config.Id, config.Version, 123);

var entities = new[]
{
    new BoardEntityState(new EntityId(1),
        new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player"),
        new GridPosition(0, 0)),
    new BoardEntityState(new EntityId(2),
        new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("exit"),
            "exit", new BoardEntityTraits(true, true, false)),
        new GridPosition(7, 7)),
    new BoardEntityState(new EntityId(3),
        new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "item.berries"),
        new GridPosition(2, 2))
};
var items = new[]
{
    new BoardItemSpawn("berries-1", new EntityId(3), "item.berries", "food")
};

var blueprint = new BoardBlueprint(address, config, entities, items);
BoardState state = blueprint.CreateState();
BoardBlueprint exported = BoardBlueprint.FromState(address, config, state, items);
bool sameInitialLayout = blueprint.Equals(exported);
```

## M4.3 validation API and ownership

`HallowBlaze.Core.Generation.Validation.BoardValidator` has two entry points:

- `Validate(blueprint, catalog)` checks an already constructed immutable layout. Passing
  a null blueprint is API misuse and throws `ArgumentNullException`.
- `Validate(address, config, entities, items, catalog)` inspects a raw candidate before
  construction. Missing inputs and structural violations become report entries rather
  than first-error exceptions. It also collects catalog failures that remain meaningful.
  Topology and opening metrics are null if structural defects prevent their calculation.

Both return `BoardValidationReport`: `IsValid`, an immutable error list, the supplied
address/config, and detached metrics. Errors have stable textual `Code`, optional
`Position`, and `Message` with full run/leg/edge/segment/day/seed/address-config/config
context when supplied. Missing metadata is explicit. Messages are sorted ordinally.
Validation uses no RNG, scene objects, physics, saves, inventory or profile mutations.
It never repairs a candidate. Its raw entry point consumes input enumerations once.

`BoardValidationCatalog` is an explicit detached input, not a global content registry
or a catalog inferred from the candidate. The caller supplies:

- authoritative `BoardEntityDefinition` values, unique by layer and content ID;
- item-definition to resource-family mappings for known non-exit item definitions;
- board-local authored fixed-source to item-definition mappings;
- enemy content to `ShamblerDefinition` mappings for the currently supported rule.

All candidate definitions must match catalog kind, layer, traits and compatibility food
reward exactly. `AllowedContentIds` is an allowlist, not proof that an ID exists. Source
IDs are optional for ordinary random items, but any supplied ID must resolve to the same
item definition. The catalog copies its inputs and rejects duplicate or dangling
registrations. M4.4 connects generation to `OrdinaryBoardContent.CreateCatalog()` and this validator.

## Validation rule catalog

Every error code below has focused executable coverage in
[BoardValidatorTests](../Assets/Tests/EditMode/BoardValidatorTests.cs).

| Codes | Rejected condition |
| --- | --- |
| `MissingAddress`, `MissingConfiguration`, `MissingEntities`, `MissingItems`, `MissingCatalog` | A required candidate/catalog input is absent; empty collections are distinct from missing collections |
| `ConfigurationMismatch` | Address configuration ID or version differs from the supplied config |
| `NullEntity`, `OutOfBounds`, `DuplicateEntity`, `LayerConflict` | Null entities, coordinates outside inclusive `0..7`, repeated entity IDs, or shared same-layer cells |
| `InvalidPlayerLayer`, `MissingStart`, `MultipleStarts`, `MissingExit`, `StartExitOverlap` | Invalid markers; terrain/item exit layers at one cell remain one logical exit |
| `NullItem`, `DuplicateItemInstance`, `DuplicateItemEntity`, `DuplicateSource` | Null or repeated generic instance/entity/source identities |
| `InvalidItemBinding`, `MissingItemSpawn`, `ItemFamilyConflict` | Orphan, wrong-layer, exit-as-item or mismatched-definition binding, missing metadata, or conflicting families for one definition |
| `UnknownAllowedContent`, `DisallowedContent`, `UnknownContent`, `ContentDefinitionMismatch` | Unknown config content, forbidden placed content, unresolved layer/ID, or altered authoritative definition |
| `UnknownItemDefinition`, `ItemResourceFamilyMismatch`, `UnknownSource`, `SourceDefinitionMismatch` | Unresolved or incorrectly associated item/family/fixed-source identities |
| `UnknownResourceFamily`, `UnbudgetedResource`, `ResourceBudget` | Unknown budget family, placed family without a budget, or initial counts outside its inclusive range |
| `BlockedStart`, `BlockedEnemySpawn` | Player/enemy spawn lacks walkable terrain or shares a blocking obstacle |
| `NonExecutableExit`, `UnreachableExit` | Exit lacks the terrain marker used by the resolver, or has no static cardinal path from the start |
| `PathLengthBudget`, `BranchPointBudget`, `ObstacleBudget`, `EnemyBudget` | A measured static dimension is outside its inclusive range; each logical exit's distance is checked separately |
| `UnbrokenPerimeter` | All 28 perimeter cells form a walkable static bypass, ignoring actors |
| `UnsupportedActor`, `UnsupportedEnemyRule` | An actor is neither player nor enemy, or a spawned enemy has no supported registered rule |
| `UnsafeEnemyStart`, `SafeStartMovesBudget`, `ConcurrentThreatBudget` | Enemy starts in orthogonal attack range, too few non-threatened legal first moves exist, or initial intent overlap exceeds its maximum |

## Metrics and known limits

- Static passability exactly follows the movement resolver's terrain/obstacle test: an
  explicit walkable terrain entry is required and an obstacle must be absent or walkable.
  Item traits and optional loot/tools never open a route. Interactable blockers are not
  assumed removable; future mechanics need explicit validation support.
- Breadth-first search ignores actors. Every distinct logical exit is required, with
  its own shortest cardinal distance (`-1` when unreachable). This is not a turn solver.
  Optional loot may remain inaccessible without invalidating an otherwise valid board.
- `ReachableCells` counts the start-connected passable component. `BranchPoints` counts
  all passable cells with at least three passable cardinal neighbors, including disconnected
  components, matching the configuration's definition. Obstacles include walkable entries.
- `MinimumEnemyStartDistance` is the nearest enemy's Manhattan distance, or null without
  enemies. Current registered Shambler attack range is one; adjacent starts are rejected.
- Each enemy uses a fresh active `ShamblerState` and the actual `ShamblerPlanner` against
  the initial board. `InitialThreatsByCell` counts distinct non-wait intents targeting a
  cell, including move's declared attack-on-player-entry condition. Conflict resolution
  does not remove these declared threats. `MaximumConcurrentThreats` is the maximum count.
- `SafeStartMoves` counts passable, unoccupied cardinal destinations not targeted by those
  initial intents. This is a conservative non-threatened-cell metric: it does not simulate
  terminal exit handling, damage, food, items, environment or later intent phases. Unknown
  actor/enemy rules reject the board and make the two opening budget metrics null, rather
  than reporting falsely safe zero-threat measurements.
- Resource counts use all initial generic instances, including hidden ones, not food
  restoration amounts or atlas observations. An absent budgeted family counts as zero.
- The perimeter rule prevents the legacy fully walkable ring; it does not score fun,
  ensure multiple meaningful tactical solutions, or prove fairness beyond these rules.
- Landmark state-space solving, later AI types, full-run resource solvability, retries,
  automatic repairs, fallback selection and large seed sweeps remain outside M4.3.

The canonical passing test board has terrain on all 64 cells, a player at `(0,0)`, terrain
exit at `(7,7)`, and one blocking obstacle at `(7,0)`. It measures path length `14`,
`63` reachable cells, `58` branch points, two safe opening moves and no threats.
The tests also contain disconnected exits, optional tool loot, multiple exits, hidden
supply, overlapping initial intents, null/duplicate IDs and coordinate extremes.

## Comparison and transitional export

Entity order is signed numeric ID order. Exit positions use X-then-Y order. Item instances,
allowed content, and resource budgets use ordinal textual order. Equality compares all
canonical bytes, not only a hash. `ContentHash` is lowercase SHA-256; `GetHashCode()` uses
its first four bytes as a little-endian signed integer. Neither uses randomized string
hashing, Unity state, current culture, or input enumeration order.

Canonical encoding starts with `hallowblaze.board-blueprint.v1`, then includes every
address field, all config targets/content sets, every entity value including legacy
traits/reward, and every item-spawn field. `BinaryWriter` writes little-endian integers,
single-byte booleans, and length-prefixed strict UTF-8 strings. Collection counts and an
explicit nullable-source flag preserve boundaries. Fixed dimensions and derived marker/
manifest values are implicit in the contract or their source data. This is an initial
layout comparison fingerprint, not a general runtime-state hash or portable replay format.

`FromState(address, config, state, items)` is the temporary exporter for an existing
authoritative board, including `BoardRuntime.BoardState`. Invoke it before gameplay to
compare initial layouts. Supply actual travel/configuration metadata and explicit generic
item descriptors; do not infer definitions/families from Food rewards or prefab names.
The exporter preserves the current legacy layered exit without counting it as supply.
It rejects non-8x8 bounds. It does not scrape a scene or alter the production generator.

Normal between-board saves still store their existing seed/configuration boundary rather
than a blueprint. M4.4 changes ordinary generation under a new explicit configuration;
save schemas, RNG algorithms, and carried inventory are unchanged. Focused data/validation coverage is in
[BoardBlueprintTests](../Assets/Tests/EditMode/BoardBlueprintTests.cs) and
[BoardValidatorTests](../Assets/Tests/EditMode/BoardValidatorTests.cs).

## M4.4 ordinary generation

`OrdinaryBoardGenerator.Generate(address, config, catalog, runSeed)` owns one candidate.
It uses a fresh `ordinary-board-v1` PCG32 stream framed with run, leg, edge, segment, day,
configuration ID/version and board seed. The run root seed also participates in derivation.
Definition enumeration is sorted by layer/content ID, and coordinates are sorted before
shuffling. Neither collection hash iteration, culture, prefab order nor cosmetic draws
can select gameplay placement. Stable textual item IDs plus the full address identify
each item instance; the resource manifest remains derived from those instances.

### Phases

1. Resolve allowed, authoritative ground, terrain exit, player, blocking obstacles,
  current enemies and generic item definitions from the detached catalog.
2. Choose a south-edge start `(x,0)` and north-edge exit `(7-x,7)`. Reserve a randomly
  interleaved monotone cardinal route plus every in-bounds first-step neighbor.
3. Fill all 64 terrain cells, replacing the exit cell with explicit exit terrain.
  Place budgeted obstacles only outside the reservation, including at least one
  blocking perimeter cell. No fully walkable 28-cell perimeter is retained.
4. Compute the start-connected passable component. Place enemies only there, off the
  exit, at Manhattan distance at least four from the player and three from each other.
  The actual validator still decides initial intent safety and concurrent threats.
5. Place budgeted item instances on reachable cells outside initial actors and the exit.
  Existing food/soda definitions map to family `food`; their automatic food rewards
  remain runtime compatibility effects, not generation counts or an inventory model.
6. Validate the raw complete candidate. A failed candidate throws
  `BoardGenerationException` with its detached report, full identity and all reasons.
  A passing candidate becomes an immutable blueprint before any view is instantiated.
7. `BoardManager` instantiates existing mapped views in an inactive staging holder.
  `BlueprintBoardRuntimeComposer` checks exact ID/count/position parity, creates state
  directly from the blueprint, and resolves enemy rules from the catalog. The exit's
  decorative marker and outer wall outside `0..7` are not authoritative resource entities.
  Publication is atomic; failure destroys staged views and restores the player pose.

### First Production Preset

`ordinary.board.v1/1` uses 8x8 bounds, shortest path `8..14`, branch points `20..58`,
two safe first steps, and `2..5` initial food-family instances. Obstacle targets are
`5..7` on days 1-3, `6..8` on days 4-6, and `7..9` afterwards. Enemy count is
`min(3, floor(log2(day)))`, with at most one enemy threatening a cell initially.
Obstacle topology, opening safety, path/branch targets and resource supply therefore
remain explicit dimensions rather than difficulty being only a zombie count.

The existing `GameManager` prefab maps both Shambler variants (10/20 HP), floor/wall
artwork and food/soda. Ordinary views may select cosmetic variants but cannot replace
an enemy rule, item definition, entity ID or cell. Missing mappings reject setup.
`BoardManager.ActiveBlueprint` exposes the accepted initial layout for inspection and
is null for legacy, failed and released boards. Runtime pickups/movement do not edit it.

### Compatibility

The prototype's 45 published stable segment IDs and order are unchanged; M4.4 published
`ordinary.board.v1/1` (M4.5 now publishes version 2, described below). Starting a current-RNG leg persists this
identity before board creation. A saved `legacy.board.v1/1` leg remains explicitly legacy,
retaining its original seed, layout algorithm and draw order across all its segments.
`BoardAddressFactory` allows only this named legacy-to-ordinary publication upgrade;
unknown IDs, future versions, foreign roads and foreign segments remain rejected.
Imported legacy-RNG runs also retain legacy configuration when starting another leg.
Checkpoint-only boards retain their previous path. No save reset or schema change occurs.

### Checked Examples

These are generated and validated test outputs, not hand-authored fallbacks. Both use
root seed `77`, board seed `42`, and run/leg/edge/segment IDs `run/leg/road/segment`.
Rows are shown north to south. `P` is the player, `E` the exit, `#` a blocking obstacle,
`Z` an enemy and `f` one food-family instance (not its restoration amount).

```text
Day 1                 Day 4
......E.              E....#..
.....f#.              ........
......##              .#.....f
........              Z#f.....
...#.f..              ........
.....#f#              ##...f..
......f#              f..#f...
.P......              ..Z.##.P
```

The day-one fingerprint is
`a9400fb48d29fcb420826b067dc62f3ebe7b4b9770006d1e0125981a0aa03b37`;
the day-four fingerprint is
`8f729f25ec47bdcc7e68242aba732eff5ea269ac6e01340901d29841c11412b0`.
The separate construction fixture freezes a golden algorithm snapshot in
[OrdinaryBoardGeneratorTests](../Assets/Tests/EditMode/OrdinaryBoardGeneratorTests.cs).

### Limits And Verification

This family reserves only monotone south-to-north routes; it is not a solver for every
joint budget accepted by `BoardGenerationConfig`. It never publishes invalid data,
silently relaxes budgets, retries indefinitely, repairs GameObjects, or writes profile/
atlas observations. Rejected candidates are explicit. M4.5 adds the retry/sub-seed/fallback
policy below; 1,000/10,000-seed gates and distribution reporting remain M4.6. Static reachability
and opening safety are not proofs of fun or full-run survival. Final biomes, landmarks,
fixed-source guarantees, hidden-item presentation and carried inventory are not added.

M4.4 removes no further production global-RNG calls: M4.1 already removed them from
`BoardManager`. Its remaining seeded `System.Random` calls are legacy-save compatibility;
`SoundManager`'s global Unity RNG remains cosmetic. The new generator/presenter uses
separate addressed streams and never reads or advances global Unity RNG.

Acceptance on 2026-10-14: generator/composer EditMode `46/46`, travel EditMode `38/38`,
startup PlayMode `21/21`, full EditMode `827/827`, and graphics-enabled full PlayMode
`115/115`, all passed with zero failed, skipped or inconclusive cases. Coverage includes
known seeds, a golden snapshot, days 1/4/10 presets, changed full address/root seed,
catalog order/culture, rejection reports, view parity/invalid bindings, initial enemy
rules/one turn controller, failure cleanup, legacy seed vectors and real Main-scene
repeated Continue. Reports/logs remain outside the project. The owner authorized
combined Lead/Developer/Reviewer work for M4.4; this was self-review, not independent review.

## M4.5 bounded generation pipeline

The engine-free pipeline lives in
[BoardGenerationPipeline](../Assets/Scripts/Core/Generation/Pipeline/BoardGenerationPipeline.cs).
`GenerateOrdinary(request)` uses at most **8 candidate calls**, followed by at most
**2 authored fallback calls** in stable priority order. The generic `Generate` API
accepts a candidate limit in `1..8` and at most eight uniquely named fallbacks, giving
an absolute maximum of 16 builder calls. Invalid limits/registrations fail before
candidate generation. The first validated result stops all remaining work.

Builders are trusted synchronous bounded project code, not sandboxed plugins. The
pipeline bounds work counts rather than attempting to interrupt an arbitrary hung
callback. Actual ordinary builders have finite board/content loops; focused tests use
a five-second NUnit timeout and explicit elapsed-time assertions. These checks are not
a production latency SLA, a large-seed performance study, or an OS-level watchdog.

### Request And Selection

`BoardGenerationRequest` copies the optional exact `BoardRequiredFeature` list and owns
the original address, configuration, catalog and run root seed. The configuration's
inclusive resource-family budgets are also binding authored supply bands. Required
features specify an immutable definition, exact position and optional stable item-source
ID with initial observability. Neither candidates nor fallbacks may change these values.
Source IDs must still resolve in the authoritative catalog. This is a generation input
contract, not atlas knowledge, a rumor UI, route-wide supply allocation or inventory.

For zero-based attempt `i`, `DeriveSubSeed` creates an independent addressed PCG32 stream:

```text
provider root = persisted run seed
purpose       = board-generation-attempt-v1
identity      = run ID, leg ID, edge ID, segment ID, day,
                configuration ID, generation version, saved board seed, attempt i
sub-seed      = first UInt32 draw, reinterpreted as signed Int32
```

Identity parts use the existing length-framed `RandomStreamProvider.StreamId` with
invariant numeric formatting. The candidate receives the sub-seed as its private root
seed while its blueprint retains the original full `BoardAddress` and configuration.
The saved seed is never replaced by a selected attempt seed; repeating the boundary
recomputes the same selection. No clock, global RNG, mutable shared cursor or prefab
ordering participates. The retained M4.4 golden layout and seed derivation are unchanged.

Every returned candidate/fallback is independently passed through `BoardValidator`
against the original request, then checked for unchanged address/configuration and all
exact required features. A structurally constructible blueprint with an unreachable exit
is still rejected. Expected `BoardGenerationException` and argument rejection become
diagnostics; unexpected implementation exceptions propagate instead of being hidden by
a successful fallback. Exhaustion returns a failed result with no blueprint. Production
setup raises an explicit startup failure before creating views or publishing a runtime.

### Authored Fallback Library

[OrdinaryBoardFallbacks](../Assets/Scripts/Core/Generation/Pipeline/OrdinaryBoardFallbacks.cs)
contains two fixed, versioned topology templates in source-controlled data tables:

1. `ordinary.southwest.v1`: player `(0,0)`, exit `(7,7)`, reserved left/top route.
2. `ordinary.southeast.v1`: the horizontally mirrored layout and route.

Both include a perimeter blocker, up to nine authored obstacle slots and three separated
enemy slots. They do not call the random generator. Definitions resolve from the original
allowed catalog. Counts use the original inclusive budget's lower bound, with at least
one perimeter blocker, and item slots are filled deterministically. This selects within
the accepted band, not a rewritten difficulty configuration. Exact fixed items are counted
before filling supply; their source identity and hidden/observable status are retained.
Conflicts with an existing authored slot, missing content, capacity overrun or impossible
combined budgets reject the template. The next template is tried once; no requirement is
silently dropped. The finite library covers current production presets, not every
theoretically constructible configuration or future landmark rule set.

Each template is independently tested for all representative production pressure bands
(days 1, 2, 4, 7 and 10). Additional fixtures enforce exact four-item supply with a hidden
fixed source, reject conflicting features/resources, and prove that a corrupted fallback
cannot be published. There is no scene repair, network content fetch or profile write.

### Diagnostics And Forced Fallback Evidence

`BoardGenerationResult` exposes `Blueprint`, `IsSuccess`, `Attempts`, `SelectedAttempt`,
`UsedFallback` and `FallbackId`. Each immutable attempt records its zero-based index,
sub-seed or fallback ID, and detached validation report. Its summary includes complete
requested address/seed/configuration and distinct concise rejection codes. Candidate
validation errors retain their original rule codes; `CandidateInput`,
`CandidateIdentityMismatch` and `RequiredFeature` cover pipeline-specific rejection.

`BoardManager.ActiveGenerationResult` retains an accepted version-2 history for the board
lifetime and clears it with the runtime. More than one candidate/any fallback emits a
recovery warning; first-attempt success does not log. Exhaustion reports the full history.
The result exposes fallback usage for future M4.6 aggregation without recording player
telemetry, creating replay files, writing profile/atlas data or adding unbounded history.

Verified forced cases:

- An always-invalid generator stops after exactly 1, 3 or 8 calls. A repeat selects the
  identical fallback and history. The maximum generic all-fail case executes exactly
  eight candidates plus eight fallback builders and returns no blueprint.
- Each real authored template is forced independently after a rejected candidate and
  passes the unchanged production budgets. For `run/leg/road/segment`, day 1, root seed
  `77`, board seed `42`, configuration `ordinary.board.v1/1`, the candidate sub-seed is
  `-1221944210`; forced missing data reports
  `MissingEntities,MissingExit,MissingItems,MissingStart`, followed by an accepted
  `ordinary.southwest.v1` or `ordinary.southeast.v1` with its full address intact.
- The PlayMode `ForcedFallbackBuildsRuntimeWithoutLosingModelData` forces eight candidate
  rejections under version 2, then selects `ordinary.southwest.v1` on the ninth builder
  call. Model/view counts and positions, both initial enemy intents and one-controller
  turn execution are preserved.
- A blocked-exit fallback fails independent semantic validation; a following good
  fallback may succeed, but an exhausted corrupt library returns no blueprint.

### Publication And Compatibility

New prototype legs persist `ordinary.board.v1/2` before board creation. All 45 stable
segment IDs, ordering and resource/topology budgets remain unchanged. Version 1 still
uses the M4.4 single-candidate path with its original root seed and fingerprint; legacy
v1 boards and imported legacy-RNG future legs retain their original algorithm too.
Named publication upgrades allow these saved identities against the new published world
without rewriting the active leg, address or seed. Future versions and downgrades remain
rejected. Normal run schemas are unchanged and do not persist a blueprint or retry cursor.

Acceptance on 2026-10-15: focused pipeline EditMode **27/27**, travel EditMode **41/41**,
startup PlayMode **23/23**, full EditMode **857/857**, and graphics-enabled full PlayMode
**117/117** passed, with zero failed, skipped or inconclusive cases. Full PlayMode includes
the real Main scene and repeated Continue. Tests/logs are external temporary artifacts;
no separate success report was added to the repository. The owner's current autonomous
instruction authorized direct implementation and self-review for M4.5; no independent
review or user-run manual smoke is claimed. M4.6 owns the seed sweeps and fallback-rate
thresholds below; M4.5 itself makes no measured frequency or final balance claim.

## M4.6 Seed Series And Measurements

The test-only [harness](../Assets/Tests/EditMode/SeedSeriesHarness.cs) and
[fixtures](../Assets/Tests/EditMode/BoardGenerationSeedSeriesTests.cs) execute the actual
`ordinary.board.v1/2` production pipeline and revalidate every result against its original
request. No runtime generator, preset, source content, world data, save or presentation
code is changed. [Testing instructions](Testing.md#m46-generation-gates) provide exact
CLI filters and external-artifact locations.

### Coverage And Gates

Thresholds were committed to the test before the first production measurement: zero
invalid boards, zero statically unreachable exits, fallback rate at most `0.01`, and
per-pass elapsed limits of 60 seconds for daily 1,000 boards / 300 seconds for milestone
10,000 boards. The harness itself caps a batch at 10,000 cases. It records time-limit
exhaustion and every unexecuted case rather than accepting a shortened sweep. The
synchronous generator is bounded by M4.5; this timer is not an OS-level interruption
mechanism for arbitrary hung code.

The daily/milestone fixtures load the current prototype registry and cover ordinary boards
on all nine edges, all 44 ordinary segment IDs and all days 1-10. M6.4's authored arrival is
not passed to the ordinary generator. The eight wholly ordinary roads retain complete
five-board synthetic legs. Five complete four-board inbound ordinary-prefix sweeps are
explicitly named `published-inbound-ordinary-prefix`; remaining samples use full ordinary
roads. Thus the current daily sample set has 196 full ordinary roads and five inbound
prefixes (1000 boards); the milestone set has 1996 full roads and five prefixes (10000).
These prefix totals do not claim to measure supply on the authored arrival. Each sample
retains the exact published generation identity. Edges cycle through the catalog and days
cycle 1-10 across traversals. Root seeds alternate `0,-1,1,-2,...`; run IDs include the
traversal number. The production `BoardAddressFactory` derives board seeds and addresses.
Both gates assert exactly 1000/10000 distinct board seeds, not only request count. Historical
measurements below used the earlier 45-board ordinary registry and are not relabeled.

Every sweep is run twice. Canonical, ordinally ordered JSON includes the input sequence,
blueprint/selection history hash, complete histograms, per-day measurements, per-segment
resource families and complete-traversal edge totals/variance. SHA-256 of that summary
must match, as must the entire deterministic summary. Elapsed time and random artifact
directory names are recorded separately and excluded from comparison.

Failed cases retain full address, root/board seed, configuration targets and allowed IDs,
exact feature/source requirements, fixture/segment identity, all concise failure reasons
and pipeline attempt history in external `failures.json`. No failing seed is filtered out
and no reroll is performed by the harness. Expected negative controls prove that corrupt
configuration, changed supply bands, dropped fixed sources, excess fallback usage and
incomplete traversals fail the gate. Every histogram retains all bins, including outliers.

### Observed Results

Measured on 2026-10-15 in headless Unity `6000.3.21f1`, on the active workspace:

| Gate | Boards Per Pass | Pass 1 | Pass 2 | Invalid / Static Softlocks / Fallbacks |
| --- | --- | --- | --- | --- |
| Daily | 1,000 | 4.62 s | 1.96 s | 0 / 0 / 0 in both |
| Milestone | 10,000 | 23.44 s | 21.17 s | 0 / 0 / 0 in both |

The daily deterministic summary hash is
`ec14cc44aeff8cd95a36cf50b87b58b0dd3feff100a3eb0e0e65bb8b1dac3bba`;
the milestone hash is
`e2175d0edfa6b6a6562c2041c3adf01bbef83927cad5e38f3bf25b5fe8eaaf83`.
All 10,000 milestone boards succeeded on candidate index zero. Observed fallback rate
was `0%` against the predeclared `1%` ceiling; this is fixture evidence, not a universal
zero-fallback guarantee. No production failing seeds or incomplete traversals were found.

Milestone distributions:

| Measurement | Minimum | Maximum | Mean |
| --- | --- | --- | --- |
| Start-connected passable cells | 46 | 59 | 56.7891 |
| Shortest required exit distance, cardinal moves | 8 | 14 | 10.9814 |
| Branch-point cells | 35 | 54 | 45.0219 |
| Obstacle occupancy / 64 | 0.078125 | 0.140625 | 0.11075 |
| Enemy actors | 0 | 3 | 1.9 |
| Safe first-step destinations | 2 | 3 | 2.7498 |
| Maximum initial concurrent threats | 0 | 1 | 0.9 |

Shortest paths have the full histogram `{8:2525, 10:2545, 12:2428, 14:2502}`. The smallest
reachable component (46 cells) occurred once and remains in the histogram, not discarded
as an outlier. Each of the 45 segment supply distributions has 222-223 observations,
food-family range `2..5`, and population variance between approximately `1.1312..1.4031`.
Observable and total supply are separately measured; production food is initially observable.

Each published road has five segments. Food-family edge totals are computed per complete
run/leg/edge traversal, not by pooling board observations. Below are independent samples
and population variance per stable edge suffix (all IDs start with `road.`):

| Edge | Samples | Minimum | Maximum | Mean | Variance |
| --- | --- | --- | --- | --- | --- |
| start-east-creek | 222 | 10 | 24 | 17.3333 | 6.2222 |
| start-west-trail | 222 | 10 | 23 | 17.3649 | 6.8624 |
| west-grove-ridge | 222 | 10 | 24 | 17.6081 | 6.0942 |
| east-quarry-ridge | 223 | 11 | 23 | 17.4978 | 6.6984 |
| ridge-radio-tower | 222 | 11 | 25 | 17.5946 | 6.8987 |
| east-creek-old-road | 223 | 11 | 23 | 17.1794 | 7.0620 |
| old-road-west-grove | 222 | 11 | 25 | 17.9414 | 6.5596 |
| west-trail-old-road | 222 | 11 | 23 | 17.2207 | 5.6945 |
| old-road-east-quarry | 222 | 11 | 24 | 17.4189 | 6.9912 |

### Authored Resource Evidence

Separate synthetic fixtures exercise eight traversals each of two- and five-segment roads
(56 boards total). Exact food bands `4..4` produce complete-traversal totals 8 and 20 with
zero variance. A hidden fixed axe source appears exactly once at its required cell per
traversal: total tool supply 1, observable tool supply 0. Source presence has its own
per-segment observation count. These fixtures use a test-only catalog and are not new
authored production content or a bag implementation. Their explicit `100%` allowed
fallback rate tests preservation, not the production gate's `1%` quality threshold.

Independent negative controls return a pipeline-successful but altered request: the
harness still catches a changed resource band or a missing source. Partial traversals
do not contribute to complete edge distributions and cause a failed batch. A separate
known-count fixture proves totals `{5,9}`, mean 7 and population variance 4. This supports
future truthful supply-band and fixed-source evidence without writing atlas knowledge.

### Recommendation And Limits

Keep the current production budgets provisionally unchanged: observed topology, opening
safety and supply are within every accepted bound, and neither rerolls nor fallback
inflated this sample. Do not infer equal tactical difficulty from the means. Current
paths are exclusively even-length and branching remains high; these facts follow the
first topology family and are not evidence of varied decision quality. Use M4.9 and
later playtests to evaluate pressure/fun before adjusting budgets. Keep the `1%` fallback
ceiling and rerun both gates whenever generation identity, definitions or targets change.

Zero static softlocks means every required exit is reachable under the validator's
terrain/obstacle rules without optional tools. It does not prove survival against all
future enemy turns, full-run food solvability, landmark state-space solvability or fun.
These tests never access the player's profile or external services. JSON/XML/logs remain
external temporary diagnostics, not replay captures or persistent gameplay telemetry.

Mechanical evidence: focused harness **9/9**, daily gate **1/1**, explicit milestone
gate **1/1**, and full EditMode **867 passed, 0 failed, 1 expected Explicit skip**. The
skipped milestone test was executed separately on the final source and passed. PlayMode
was not rerun because changes are exclusively test/diagnostic/documentation code; M4.5's
production PlayMode baseline remains unchanged. Independent Reviewer returned **PASS**
with no material findings; M4.6 was accepted on 2026-10-15 in the owning roadmap card.