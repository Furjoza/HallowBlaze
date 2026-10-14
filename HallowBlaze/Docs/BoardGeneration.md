# Board generation data contract

M4.2 defines initial data; M4.3 adds an independent ordinary-board validator, not a new
generator or final balance preset. The engine-free `HallowBlaze.Core.Generation` assembly
uses board primitives/state, stable travel identity, and the existing turn contracts and
Shambler planner for opening measurements. Production generation and presentation are unchanged.

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
generation and view construction belong to M4.4. Construction and raw-candidate validation
share one structural rule implementation rather than competing checks.

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
not prove joint feasibility; M4.3 checks a candidate's measured compliance. M4.4 will
generate candidates against these targets.

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
registrations. M4.4 owns connecting generation to the actual content catalog and validator.

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
than a blueprint. Save schemas, RNG algorithms, generation behavior, and carried inventory
are unchanged. Focused executable coverage is in
[BoardBlueprintTests](../Assets/Tests/EditMode/BoardBlueprintTests.cs) and
[BoardValidatorTests](../Assets/Tests/EditMode/BoardValidatorTests.cs).