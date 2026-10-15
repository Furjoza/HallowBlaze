using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Board.State
{
    /// <summary>
    /// Owns the authoritative entity positions and occupancy indexes for one board.
    /// Terrain, obstacles, items, and actors occupy independent layers.
    /// </summary>
    public sealed class BoardState
    {
        private static readonly BoardLayer[] Layers =
        {
            BoardLayer.Terrain,
            BoardLayer.Obstacle,
            BoardLayer.Item,
            BoardLayer.Actor
        };

        private readonly Dictionary<EntityId, BoardEntityState> entitiesById;
        private readonly Dictionary<BoardLayer, Dictionary<GridPosition, EntityId>> entityIdsByLayer;
        private readonly List<BoardNoise> activeNoises;
        private long nextNoiseId;

        /// <summary>
        /// Creates an empty board using the default inclusive 8x8 bounds.
        /// </summary>
        public BoardState()
            : this(GridPosition.DefaultBoardBounds)
        {
        }

        /// <summary>
        /// Creates an empty board using explicit inclusive bounds.
        /// </summary>
        /// <param name="bounds">The legal positions for every board layer.</param>
        public BoardState(GridBounds bounds)
        {
            Bounds = bounds;
            entitiesById = new Dictionary<EntityId, BoardEntityState>();
            entityIdsByLayer = new Dictionary<BoardLayer, Dictionary<GridPosition, EntityId>>
            {
                { BoardLayer.Terrain, new Dictionary<GridPosition, EntityId>() },
                { BoardLayer.Obstacle, new Dictionary<GridPosition, EntityId>() },
                { BoardLayer.Item, new Dictionary<GridPosition, EntityId>() },
                { BoardLayer.Actor, new Dictionary<GridPosition, EntityId>() }
            };
            activeNoises = new List<BoardNoise>();
            nextNoiseId = 0;
        }

        /// <summary>Gets the inclusive legal bounds of the board.</summary>
        public GridBounds Bounds { get; }

        /// <summary>Gets the number of entities across all layers.</summary>
        public int Count => entitiesById.Count;

        /// <summary>
        /// Adds an entity when its ID is unique, its position is in bounds, and its layer cell is empty.
        /// A rejected add leaves every board index unchanged.
        /// </summary>
        /// <param name="entity">The immutable entity state to add.</param>
        /// <returns><see langword="true"/> when the entity was added; otherwise, <see langword="false"/>.</returns>
        public bool TryAdd(BoardEntityState entity)
        {
            if (entity == null ||
                !Bounds.Contains(entity.Position) ||
                entitiesById.ContainsKey(entity.Id) ||
                !entityIdsByLayer.TryGetValue(entity.Definition.Layer, out Dictionary<GridPosition, EntityId> layer) ||
                layer.ContainsKey(entity.Position))
            {
                return false;
            }

            entitiesById.Add(entity.Id, entity);
            layer.Add(entity.Position, entity.Id);
            return true;
        }

        /// <summary>
        /// Removes an entity and its layer occupancy entry as one operation.
        /// </summary>
        /// <param name="id">The board-local entity identifier.</param>
        /// <returns><see langword="true"/> when an entity was removed; otherwise, <see langword="false"/>.</returns>
        public bool TryRemove(EntityId id)
        {
            if (!entitiesById.TryGetValue(id, out BoardEntityState entity))
                return false;

            Dictionary<GridPosition, EntityId> layer = entityIdsByLayer[entity.Definition.Layer];
            layer.Remove(entity.Position);
            entitiesById.Remove(id);
            return true;
        }

        /// <summary>
        /// Moves an entity within its current layer when the destination is legal and unoccupied.
        /// A rejected move leaves both indexes and the entity state unchanged.
        /// </summary>
        /// <param name="id">The board-local entity identifier.</param>
        /// <param name="destination">The requested destination.</param>
        /// <returns><see langword="true"/> when the entity occupies the destination; otherwise, <see langword="false"/>.</returns>
        public bool TryMove(EntityId id, GridPosition destination)
        {
            if (!Bounds.Contains(destination) ||
                !entitiesById.TryGetValue(id, out BoardEntityState entity))
            {
                return false;
            }

            if (entity.Position.Equals(destination))
                return true;

            Dictionary<GridPosition, EntityId> layer = entityIdsByLayer[entity.Definition.Layer];
            if (layer.ContainsKey(destination))
                return false;

            BoardEntityState movedEntity = entity.WithPosition(destination);
            layer.Remove(entity.Position);
            layer.Add(destination, id);
            entitiesById[id] = movedEntity;
            return true;
        }

        /// <summary>
        /// Finds an entity by its board-local identifier.
        /// </summary>
        /// <param name="id">The board-local entity identifier.</param>
        /// <param name="entity">The entity state when found; otherwise, <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the entity exists.</returns>
        public bool TryGetEntity(EntityId id, out BoardEntityState entity)
        {
            return entitiesById.TryGetValue(id, out entity);
        }

        /// <summary>
        /// Finds the entity occupying one layer at a position.
        /// </summary>
        /// <param name="layer">The layer to query.</param>
        /// <param name="position">The position to query.</param>
        /// <param name="entity">The entity state when found; otherwise, <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when that layer cell is occupied.</returns>
        public bool TryGetEntity(BoardLayer layer, GridPosition position, out BoardEntityState entity)
        {
            entity = null;
            return Bounds.Contains(position) &&
                entityIdsByLayer.TryGetValue(layer, out Dictionary<GridPosition, EntityId> layerEntities) &&
                layerEntities.TryGetValue(position, out EntityId id) &&
                entitiesById.TryGetValue(id, out entity);
        }

        /// <summary>
        /// Gets a detached read-only snapshot of entities at a position in terrain, obstacle, item, actor order.
        /// </summary>
        /// <param name="position">The legal board position to query.</param>
        /// <returns>A fixed-order read-only snapshot containing at most one entity per layer.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is outside the board.</exception>
        public IReadOnlyList<BoardEntityState> GetEntitiesAt(GridPosition position)
        {
            Bounds.RequireContains(position);
            var result = new List<BoardEntityState>(Layers.Length);
            foreach (BoardLayer layer in Layers)
            {
                if (TryGetEntity(layer, position, out BoardEntityState entity))
                    result.Add(entity);
            }

            return result.AsReadOnly();
        }

        /// <summary>
        /// Gets a detached read-only snapshot of all entities ordered by identifier.
        /// </summary>
        /// <returns>A read-only snapshot whose order is independent of insertion order.</returns>
        public IReadOnlyList<BoardEntityState> GetEntities()
        {
            var result = new List<BoardEntityState>(entitiesById.Values);
            result.Sort((left, right) => left.Id.CompareTo(right.Id));
            return result.AsReadOnly();
        }

        /// <summary>
        /// Creates an independent board with equivalent values and no shared mutable collections.
        /// </summary>
        /// <returns>An independent board snapshot.</returns>
        public BoardState Clone()
        {
            var clone = new BoardState(Bounds);
            foreach (BoardEntityState entity in GetEntities())
            {
                clone.TryAdd(new BoardEntityState(entity.Id, entity.Definition, entity.Position));
            }

            foreach (var noise in activeNoises)
            {
                clone.activeNoises.Add(noise);
            }
            clone.nextNoiseId = nextNoiseId;

            return clone;
        }

        /// <summary>
        /// Emits a noise event from the specified position with the given strength and lifetime.
        /// The source is always audible. Orthogonal paths cost one per cell and require walkable
        /// terrain with no impassable obstacle; actors and items do not block. The footprint is
        /// frozen at emission and remains stable after board changes or removal of the source.
        /// </summary>
        /// <param name="sourceId">The source event identifier.</param>
        /// <param name="position">The emission position.</param>
        /// <param name="strength">The maximum number of orthogonal steps from the source.</param>
        /// <param name="lifetimeTurns">The number of explicit lifetime advances before expiration; at least one.</param>
        /// <returns>The immutable noise snapshot.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="strength"/> is negative or <paramref name="lifetimeTurns"/> is less than 1.
        /// -or-
        /// <paramref name="position"/> is outside the board.
        /// </exception>
        public BoardNoise EmitNoise(EntityId sourceId, GridPosition position, int strength, int lifetimeTurns)
        {
            Bounds.RequireContains(position);
            if (strength < 0)
                throw new ArgumentOutOfRangeException(nameof(strength), "Strength must be non-negative.");
            if (lifetimeTurns <= 0)
                throw new ArgumentOutOfRangeException(nameof(lifetimeTurns), "Lifetime turns must be at least 1.");
            if (nextNoiseId == long.MaxValue)
                throw new InvalidOperationException("The board noise identifier sequence is exhausted.");

            var audibleCells = new HashSet<GridPosition> { position };
            var queue = new Queue<GridPosition>();
            queue.Enqueue(position);
            var visited = new HashSet<GridPosition> { position };
            var directions = new[] { Direction.North, Direction.East, Direction.West, Direction.South };
            int remainingDistance = strength;
            while (queue.Count > 0 && remainingDistance > 0)
            {
                int levelSize = queue.Count;
                for (int cellIndex = 0; cellIndex < levelSize; cellIndex++)
                {
                    GridPosition current = queue.Dequeue();
                    foreach (Direction direction in directions)
                    {
                        GridPosition neighbor;
                        try
                        {
                            neighbor = current.Move(direction);
                        }
                        catch (OverflowException)
                        {
                            continue;
                        }
                        if (!Bounds.Contains(neighbor) || !visited.Add(neighbor))
                            continue;
                        if (!TryGetEntity(BoardLayer.Terrain, neighbor, out BoardEntityState terrain) ||
                            !terrain.Definition.Traits.IsWalkable)
                        {
                            continue;
                        }
                        if (TryGetEntity(BoardLayer.Obstacle, neighbor, out BoardEntityState obstacle) &&
                            !obstacle.Definition.Traits.IsWalkable)
                        {
                            continue;
                        }
                        audibleCells.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
                remainingDistance--;
            }

            var noise = new BoardNoise(nextNoiseId, sourceId, position, strength, lifetimeTurns, audibleCells);
            activeNoises.Add(noise);
            nextNoiseId++;
            return noise;
        }

        /// <summary>
        /// Gets a detached read-only snapshot of active noise events ordered by emission.
        /// </summary>
        /// <returns>A read-only snapshot whose order is the emission order.</returns>
        public IReadOnlyList<BoardNoise> GetActiveNoises()
        {
            return new List<BoardNoise>(activeNoises).AsReadOnly();
        }

        /// <summary>
        /// Advances the lifetime of all active noise events by one turn.
        /// Noise events with RemainingTurns reaching zero are removed. Newer emissions can be
        /// excluded so a command never ages noise that it has just produced.
        /// </summary>
        /// <param name="newestIncludedId">Last pre-command emission ID, or the default for all active noise.</param>
        public void AdvanceNoiseLifetime(long newestIncludedId = long.MaxValue)
        {
            for (int noiseIndex = activeNoises.Count - 1; noiseIndex >= 0; noiseIndex--)
            {
                var noise = activeNoises[noiseIndex];
                if (noise.Id > newestIncludedId)
                    continue;
                if (noise.RemainingTurns > 1)
                {
                    activeNoises[noiseIndex] = new BoardNoise(noise.Id, noise.SourceId, noise.Position, noise.Strength, noise.RemainingTurns - 1, noise.AudibleCells);
                }
                else if (noise.RemainingTurns == 1)
                {
                    activeNoises.RemoveAt(noiseIndex);
                }
            }
        }
    }
}