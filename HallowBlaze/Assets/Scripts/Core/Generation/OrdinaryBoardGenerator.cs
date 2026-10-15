using System;
using System.Collections.Generic;
using System.Globalization;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Random;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Generation
{
    /// <summary>Reports a rejected single candidate; retry and fallback orchestration are not performed here.</summary>
    public sealed class BoardGenerationException : InvalidOperationException
    {
        /// <summary>Retains the detached validator report, including full board identity and rejection reasons.</summary>
        public BoardGenerationException(BoardValidationReport report)
            : base(Describe(report))
        {
            Report = report;
        }

        /// <summary>Gets all errors and measurements for the rejected candidate.</summary>
        public BoardValidationReport Report { get; }

        private static string Describe(BoardValidationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var messages = new List<string>();
            foreach (BoardValidationError error in report.Errors) messages.Add(error.Message);
            return string.Join(Environment.NewLine, messages);
        }
    }

    /// <summary>Generates one engine-free ordinary 8x8 candidate and publishes only a validated immutable blueprint.</summary>
    public static class OrdinaryBoardGenerator
    {
        /// <summary>Identifies the first ordinary model-first generation family, distinct from persisted legacy layouts.</summary>
        public const string ConfigurationId = "ordinary.board.v1";

        /// <summary>Versions the topology algorithm and its default content/budget contract.</summary>
        public const int ConfigurationVersion = 1;

        /// <summary>
        /// Reserves a south-to-north cardinal route and opening, places perimeter/interior obstacles,
        /// then separated enemies and generic reachable items. Every loop is bounded by board/content capacity.
        /// Unsupported joint budgets reject the candidate rather than relaxing targets or repairing scene objects.
        /// </summary>
        /// <param name="address">Full stable board identity; every address field participates in the layout stream.</param>
        /// <param name="config">Targets and allowed IDs matching the address, including a blocking obstacle budget.</param>
        /// <param name="catalog">Detached authoritative content, item families and current enemy rules.</param>
        /// <param name="runSeed">Persisted root seed; a fresh addressed PCG32 stream is used on each call.</param>
        /// <returns>A validated initial blueprint; no profile, run, scene or global RNG state is modified.</returns>
        /// <exception cref="ArgumentNullException">An input other than the seed is null.</exception>
        /// <exception cref="ArgumentException">Required allowed ground, terrain exit or player content is missing.</exception>
        /// <exception cref="BoardGenerationException">The candidate fails the independent validator.</exception>
        public static BoardBlueprint Generate(BoardAddress address, BoardGenerationConfig config,
            BoardValidationCatalog catalog, int runSeed)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var random = new RandomStreamProvider(runSeed).Create(RandomStreamProvider.StreamId(
                "ordinary-board-v1", address.RunId, address.LegId, address.EdgeId, address.SegmentId,
                Number(address.DayNumber), address.GenerationConfigurationId,
                Number(address.GenerationVersion), Number(address.BoardSeed)));
            var allowed = new HashSet<string>(config.AllowedContentIds, StringComparer.Ordinal);
            var ground = new List<BoardEntityDefinition>();
            var exits = new List<BoardEntityDefinition>();
            var players = new List<BoardEntityDefinition>();
            var obstacles = new List<BoardEntityDefinition>();
            var enemies = new List<BoardEntityDefinition>();
            var itemDefinitions = new List<BoardEntityDefinition>();
            foreach (BoardEntityDefinition definition in catalog.GetDefinitions())
            {
                if (!allowed.Contains(definition.ContentId)) continue;
                if (definition.Layer == BoardLayer.Terrain && definition.Traits.IsWalkable)
                    (definition.Traits.IsExit ? exits : ground).Add(definition);
                else if (definition.Layer == BoardLayer.Actor && definition.Kind.Equals(EntityKind.Player))
                    players.Add(definition);
                else if (definition.Layer == BoardLayer.Obstacle && !definition.Traits.IsWalkable)
                    obstacles.Add(definition);
                else if (definition.Layer == BoardLayer.Actor && definition.Kind.Equals(EntityKind.Enemy))
                    enemies.Add(definition);
                else if (definition.Layer == BoardLayer.Item && !definition.Traits.IsExit)
                    itemDefinitions.Add(definition);
            }
            if (ground.Count == 0 || exits.Count == 0 || players.Count == 0)
                throw new ArgumentException(BoardValidationReport.Context(address, config) +
                    " | Ordinary generation requires allowed walkable ground, terrain exit and player definitions.");

            var start = new GridPosition(random.NextInt(0, 8), 0);
            var exit = new GridPosition(7 - start.X, 7);
            var reserved = ReserveRoute(start, exit, random);
            foreach (GridPosition neighbor in Neighbors(start)) reserved.Add(neighbor);
            var entities = new List<BoardEntityState>();
            long nextId = 1;
            for (int coordinateX = 0; coordinateX < 8; coordinateX++)
                for (int coordinateY = 0; coordinateY < 8; coordinateY++)
                {
                    var cell = new GridPosition(coordinateX, coordinateY);
                    BoardEntityDefinition terrain = cell.Equals(exit) ? exits[0] : ground[random.NextInt(0, ground.Count)];
                    entities.Add(new BoardEntityState(new EntityId(nextId++), terrain, cell));
                }
            entities.Add(new BoardEntityState(new EntityId(nextId++), players[0], start));

            List<GridPosition> free = CellsExcept(reserved);
            Shuffle(free, random);
            int obstacleTarget = Math.Max(1, random.NextIntInclusive(config.ObstacleCount.Minimum, config.ObstacleCount.Maximum));
            var blocked = new HashSet<GridPosition>();
            if (obstacles.Count > 0)
            {
                foreach (GridPosition cell in free)
                    if (IsPerimeter(cell))
                    {
                        AddObstacle(cell, obstacles, random, blocked, entities, ref nextId);
                        break;
                    }
                foreach (GridPosition cell in free)
                {
                    if (blocked.Count >= obstacleTarget) break;
                    if (!blocked.Contains(cell)) AddObstacle(cell, obstacles, random, blocked, entities, ref nextId);
                }
            }

            var occupiedActors = new List<GridPosition> { start };
            var reachable = Reachable(start, blocked);
            free = new List<GridPosition>(reachable);
            free.Sort();
            Shuffle(free, random);
            int enemyTarget = random.NextIntInclusive(config.EnemyCount.Minimum, config.EnemyCount.Maximum);
            if (enemies.Count > 0)
                foreach (GridPosition cell in free)
                {
                    if (occupiedActors.Count - 1 >= enemyTarget) break;
                    if (cell.Equals(exit) || Distance(cell, start) < 4 || !Separated(cell, occupiedActors)) continue;
                    entities.Add(new BoardEntityState(new EntityId(nextId++), enemies[random.NextInt(0, enemies.Count)], cell));
                    occupiedActors.Add(cell);
                }

            free.RemoveAll(cell => cell.Equals(exit) || occupiedActors.Contains(cell));
            var items = new List<BoardItemSpawn>();
            foreach (ResourceGenerationBudget budget in config.ResourceBudgets)
            {
                var choices = new List<BoardEntityDefinition>();
                foreach (BoardEntityDefinition definition in itemDefinitions)
                    if (catalog.TryGetItemFamily(definition.ContentId, out string family) && family == budget.ResourceFamilyId)
                        choices.Add(definition);
                int count = random.NextIntInclusive(budget.ItemCount.Minimum, budget.ItemCount.Maximum);
                for (int index = 0; index < count && free.Count > 0 && choices.Count > 0; index++)
                {
                    GridPosition cell = free[free.Count - 1];
                    free.RemoveAt(free.Count - 1);
                    BoardEntityDefinition definition = choices[random.NextInt(0, choices.Count)];
                    var id = new EntityId(nextId++);
                    entities.Add(new BoardEntityState(id, definition, cell));
                    items.Add(new BoardItemSpawn("item-" + Number(cell.X) + "-" + Number(cell.Y),
                        id, definition.ContentId, budget.ResourceFamilyId));
                }
            }

            BoardValidationReport report = BoardValidator.Validate(address, config, entities, items, catalog);
            if (!report.IsValid) throw new BoardGenerationException(report);
            return new BoardBlueprint(address, config, entities, items);
        }

        private static HashSet<GridPosition> ReserveRoute(GridPosition start, GridPosition exit, DeterministicRandomStream random)
        {
            var reserved = new HashSet<GridPosition> { start };
            GridPosition cursor = start;
            while (!cursor.Equals(exit))
            {
                bool horizontal = cursor.X != exit.X && (cursor.Y == exit.Y || random.NextInt(0, 2) == 0);
                cursor = horizontal ? new GridPosition(cursor.X + Math.Sign(exit.X - cursor.X), cursor.Y)
                    : new GridPosition(cursor.X, cursor.Y + 1);
                reserved.Add(cursor);
            }
            return reserved;
        }

        private static List<GridPosition> CellsExcept(HashSet<GridPosition> excluded)
        {
            var cells = new List<GridPosition>();
            for (int coordinateX = 0; coordinateX < 8; coordinateX++)
                for (int coordinateY = 0; coordinateY < 8; coordinateY++)
                {
                    var cell = new GridPosition(coordinateX, coordinateY);
                    if (!excluded.Contains(cell)) cells.Add(cell);
                }
            return cells;
        }

        private static void AddObstacle(GridPosition cell, List<BoardEntityDefinition> choices,
            DeterministicRandomStream random, HashSet<GridPosition> blocked, List<BoardEntityState> entities, ref long nextId)
        {
            blocked.Add(cell);
            entities.Add(new BoardEntityState(new EntityId(nextId++), choices[random.NextInt(0, choices.Count)], cell));
        }

        private static HashSet<GridPosition> Reachable(GridPosition start, HashSet<GridPosition> blocked)
        {
            var seen = new HashSet<GridPosition> { start };
            var pending = new Queue<GridPosition>();
            pending.Enqueue(start);
            while (pending.Count > 0)
                foreach (GridPosition cell in Neighbors(pending.Dequeue()))
                    if (!blocked.Contains(cell) && seen.Add(cell)) pending.Enqueue(cell);
            return seen;
        }

        private static IEnumerable<GridPosition> Neighbors(GridPosition cell)
        {
            foreach (GridPosition neighbor in new[] { new GridPosition(cell.X - 1, cell.Y),
                new GridPosition(cell.X + 1, cell.Y), new GridPosition(cell.X, cell.Y - 1), new GridPosition(cell.X, cell.Y + 1) })
                if (GridPosition.DefaultBoardBounds.Contains(neighbor)) yield return neighbor;
        }

        private static bool Separated(GridPosition cell, List<GridPosition> occupied)
        {
            foreach (GridPosition other in occupied) if (Distance(cell, other) < 3) return false;
            return true;
        }

        private static int Distance(GridPosition left, GridPosition right) => Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);
        private static bool IsPerimeter(GridPosition cell) => cell.X == 0 || cell.X == 7 || cell.Y == 0 || cell.Y == 7;
        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static void Shuffle(List<GridPosition> cells, DeterministicRandomStream random)
        {
            for (int index = cells.Count - 1; index > 0; index--)
            {
                int selected = random.NextInt(0, index + 1);
                GridPosition previous = cells[index];
                cells[index] = cells[selected];
                cells[selected] = previous;
            }
        }
    }
}