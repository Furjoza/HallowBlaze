# Board generation data contract

M4.2 defines initial data, not a new generator or final balance preset. The engine-free
`HallowBlaze.Core.Generation` assembly depends on the existing board primitives, board
state, and stable travel identity. Production generation and presentation are unchanged.

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
Those checks belong to M4.3; generation and view construction belong to M4.4.

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

All `GenerationRange` bounds are inclusive, nonnegative targets. Their joint feasibility
and the resulting board's compliance require the future validator/generator.

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

The dimensions have no automatic combined difficulty score. Exact balance values,
threat-metric implementation, catalog checks, and generator enforcement are not added
by M4.2. A candidate can be structurally valid while missing a target or being unreachable.

## Example

This small construction example is not a registered content preset or accepted balance
configuration. IDs are illustrative. A later generator must validate its own output.

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
[BoardBlueprintTests](../Assets/Tests/EditMode/BoardBlueprintTests.cs).