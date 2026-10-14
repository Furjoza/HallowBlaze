using System;
using System.Collections.Generic;
using System.Globalization;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;

namespace HallowBlaze.Core.Generation.Validation
{
    /// <summary>Checks initial ordinary-board data without RNG, scene objects, optional tools or runtime mutation.</summary>
    public static class BoardValidator
    {
        private static readonly Direction[] Directions =
        {
            Direction.North, Direction.East, Direction.West, Direction.South
        };

        /// <summary>Collects structural failures before blueprint construction instead of throwing on the first invalid entry.</summary>
        /// <param name="address">Full board identity; missing identity is reported as an error.</param>
        /// <param name="config">Immutable generation targets; missing configuration is reported as an error.</param>
        /// <param name="entities">Initial entities; null entries and invalid occupancy are reported.</param>
        /// <param name="items">Initial generic item descriptors; null and inconsistent bindings are reported.</param>
        /// <param name="catalog">Authoritative content/source catalog; absence is reported and never treated as valid.</param>
        /// <returns>All structural failures, or full semantic validation when structure is sound.</returns>
        public static BoardValidationReport Validate(BoardAddress address, BoardGenerationConfig config,
            IEnumerable<BoardEntityState> entities, IEnumerable<BoardItemSpawn> items, BoardValidationCatalog catalog)
        {
            var errors = new List<BoardValidationError>();
            BoardStructureValidation structure = BoardStructureValidation.Inspect(address, config, entities, items, errors);
            if (errors.Count > 0)
            {
                CheckCatalog(config, structure.SuppliedEntities, structure.SuppliedItems, catalog, errors,
                    BoardValidationReport.Context(address, config));
                return new BoardValidationReport(address, config, errors, null);
            }
            return Validate(new BoardBlueprint(address, config, structure.State.GetEntities(), structure.Items), catalog);
        }

        /// <summary>
        /// Checks static topology, catalogs, budgets and the current Shambler opening without simulating later turns.
        /// Reachability ignores actors and optional loot/tools; opening threats use the actual initial locked-intent planner.
        /// </summary>
        /// <param name="blueprint">A structurally constructed initial layout; all logical exits are required.</param>
        /// <param name="catalog">Authoritative definitions and fixed sources, independent of the candidate's claims.</param>
        /// <returns>All failures with full board identity and detached measurements.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="blueprint"/> is null.</exception>
        public static BoardValidationReport Validate(BoardBlueprint blueprint, BoardValidationCatalog catalog)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
            var errors = new List<BoardValidationError>();
            string context = BoardValidationReport.Context(blueprint.Address, blueprint.Config);
            CheckCatalog(blueprint.Config, blueprint.Entities, blueprint.Items, catalog, errors, context);
            BoardState state = blueprint.CreateState();
            var passable = new HashSet<GridPosition>();
            int obstacles = 0;
            int enemies = 0;
            foreach (BoardEntityState entity in blueprint.Entities)
            {
                if (entity.Definition.Layer == BoardLayer.Obstacle) obstacles++;
                if (entity.Definition.Layer == BoardLayer.Actor && entity.Definition.Kind.Equals(EntityKind.Enemy)) enemies++;
                if (entity.Definition.Layer != BoardLayer.Terrain || !entity.Definition.Traits.IsWalkable) continue;
                if (!state.TryGetEntity(BoardLayer.Obstacle, entity.Position, out BoardEntityState obstacle) ||
                    obstacle.Definition.Traits.IsWalkable)
                    passable.Add(entity.Position);
            }

            var distances = new Dictionary<GridPosition, int>();
            var frontier = new Queue<GridPosition>();
            if (passable.Contains(blueprint.Start))
            {
                distances.Add(blueprint.Start, 0);
                frontier.Enqueue(blueprint.Start);
            }
            else
                errors.Add(new BoardValidationError("BlockedStart", "Start requires walkable terrain and no blocking obstacle.", context, blueprint.Start));

            while (frontier.Count > 0)
            {
                GridPosition current = frontier.Dequeue();
                foreach (Direction direction in Directions)
                {
                    GridPosition next = current.Move(direction);
                    if (!passable.Contains(next) || distances.ContainsKey(next)) continue;
                    distances.Add(next, distances[current] + 1);
                    frontier.Enqueue(next);
                }
            }

            int branches = 0;
            foreach (GridPosition position in passable)
            {
                int neighbors = 0;
                foreach (Direction direction in Directions)
                    if (passable.Contains(position.Move(direction))) neighbors++;
                if (neighbors >= 3) branches++;
            }

            var exits = new Dictionary<GridPosition, int>();
            foreach (GridPosition exit in blueprint.Exits)
            {
                if (!state.TryGetEntity(BoardLayer.Terrain, exit, out BoardEntityState terrain) || !terrain.Definition.Traits.IsExit)
                    errors.Add(new BoardValidationError("NonExecutableExit", "A required exit must be marked on terrain so the movement resolver can emit completion.", context, exit));
                int distance = distances.TryGetValue(exit, out int measured) ? measured : -1;
                exits.Add(exit, distance);
                if (distance < 0)
                    errors.Add(new BoardValidationError("UnreachableExit", "Required exit is unreachable without optional loot or tools.", context, exit));
                else
                    CheckRange(errors, "PathLengthBudget", distance, blueprint.Config.PathLength, context, exit);
            }
            CheckRange(errors, "BranchPointBudget", branches, blueprint.Config.BranchPoints, context);
            CheckRange(errors, "ObstacleBudget", obstacles, blueprint.Config.ObstacleCount, context);
            CheckRange(errors, "EnemyBudget", enemies, blueprint.Config.EnemyCount, context);
            bool openPerimeter = true;
            GridBounds bounds = blueprint.Bounds;
            for (int horizontal = bounds.MinX; horizontal <= bounds.MaxX; horizontal++)
                for (int vertical = bounds.MinY; vertical <= bounds.MaxY; vertical++)
                    if ((horizontal == bounds.MinX || horizontal == bounds.MaxX || vertical == bounds.MinY || vertical == bounds.MaxY) &&
                        !passable.Contains(new GridPosition(horizontal, vertical))) openPerimeter = false;
            if (openPerimeter)
                errors.Add(new BoardValidationError("UnbrokenPerimeter", "All perimeter cells form an actor-independent walkable bypass.", context));

            var initialThreats = new Dictionary<GridPosition, int>();
            bool completeThreats = catalog != null;
            int? minimumEnemyDistance = null;
            state.TryGetEntity(BoardLayer.Actor, blueprint.Start, out BoardEntityState player);
            var planner = new ShamblerPlanner();
            foreach (BoardEntityState entity in blueprint.Entities)
            {
                if (entity.Definition.Layer != BoardLayer.Actor || entity.Definition.Kind.Equals(EntityKind.Player)) continue;
                if (!entity.Definition.Kind.Equals(EntityKind.Enemy))
                {
                    completeThreats = false;
                    errors.Add(new BoardValidationError("UnsupportedActor", "Ordinary-board opening validation supports player and enemy actors only.", context, entity.Position));
                    continue;
                }
                int distance = (int)entity.Position.ManhattanDistance(blueprint.Start);
                minimumEnemyDistance = minimumEnemyDistance.HasValue ? Math.Min(minimumEnemyDistance.Value, distance) : distance;
                if (!passable.Contains(entity.Position))
                    errors.Add(new BoardValidationError("BlockedEnemySpawn", "Enemy spawn requires walkable terrain and no blocking obstacle.", context, entity.Position));
                if (catalog == null || !catalog.TryGetShambler(entity.Definition.ContentId, out ShamblerDefinition definition))
                {
                    completeThreats = false;
                    errors.Add(new BoardValidationError("UnsupportedEnemyRule", "No supported initial enemy rule resolves " + entity.Definition.ContentId + ".", context, entity.Position));
                    continue;
                }
                if (distance <= definition.AttackRange)
                    errors.Add(new BoardValidationError("UnsafeEnemyStart", "Enemy starts within its initial orthogonal attack range of the player.", context, entity.Position));
                EnemyIntent intent = planner.Plan(state, new ShamblerState(entity.Id, definition), player.Id);
                if (intent.Kind == EnemyIntentKind.Wait || !intent.TargetPosition.HasValue) continue;
                GridPosition target = intent.TargetPosition.Value;
                initialThreats.TryGetValue(target, out int count);
                initialThreats[target] = count + 1;
            }
            int maximumThreats = 0;
            foreach (int count in initialThreats.Values) maximumThreats = Math.Max(maximumThreats, count);
            int safeMoves = 0;
            if (passable.Contains(blueprint.Start))
                foreach (Direction direction in Directions)
                {
                    GridPosition next = blueprint.Start.Move(direction);
                    if (passable.Contains(next) && !state.TryGetEntity(BoardLayer.Actor, next, out _) && !initialThreats.ContainsKey(next)) safeMoves++;
                }
            if (completeThreats)
            {
                if (safeMoves < blueprint.Config.MinimumSafeStartMoves)
                    errors.Add(new BoardValidationError("SafeStartMovesBudget", string.Format(CultureInfo.InvariantCulture,
                        "Only {0} non-threatened legal opening moves; at least {1} required.", safeMoves, blueprint.Config.MinimumSafeStartMoves), context, blueprint.Start));
                if (maximumThreats > blueprint.Config.MaximumConcurrentThreats)
                    errors.Add(new BoardValidationError("ConcurrentThreatBudget", string.Format(CultureInfo.InvariantCulture,
                        "Initial overlap {0} exceeds maximum {1}.", maximumThreats, blueprint.Config.MaximumConcurrentThreats), context));
            }
            foreach (ResourceGenerationBudget budget in blueprint.Config.ResourceBudgets)
            {
                int count = 0;
                foreach (InitialResourceManifestEntry entry in blueprint.InitialResources)
                    if (entry.ResourceFamilyId == budget.ResourceFamilyId) count = entry.ItemCount;
                CheckRange(errors, "ResourceBudget", count, budget.ItemCount, context, detail: budget.ResourceFamilyId);
            }
            foreach (InitialResourceManifestEntry entry in blueprint.InitialResources)
            {
                bool budgeted = false;
                foreach (ResourceGenerationBudget budget in blueprint.Config.ResourceBudgets)
                    if (budget.ResourceFamilyId == entry.ResourceFamilyId) budgeted = true;
                if (!budgeted)
                    errors.Add(new BoardValidationError("UnbudgetedResource", "No initial item-count budget exists for family " + entry.ResourceFamilyId + ".", context));
            }
            return new BoardValidationReport(blueprint.Address, blueprint.Config, errors,
                new BoardValidationMetrics(distances.Count, branches, obstacles, enemies, exits,
                    completeThreats ? safeMoves : (int?)null, completeThreats ? maximumThreats : (int?)null,
                    minimumEnemyDistance, initialThreats));
        }

        private static void CheckCatalog(BoardGenerationConfig config, IEnumerable<BoardEntityState> entities,
            IEnumerable<BoardItemSpawn> items, BoardValidationCatalog catalog, List<BoardValidationError> errors, string context)
        {
            if (catalog == null)
            {
                errors.Add(new BoardValidationError("MissingCatalog", "Content and fixed-source resolution requires an explicit catalog.", context));
                return;
            }
            var allowed = new HashSet<string>(config == null ? Array.Empty<string>() : config.AllowedContentIds, StringComparer.Ordinal);
            if (config != null)
            {
                foreach (string identifier in config.AllowedContentIds)
                    if (!catalog.ContainsContent(identifier))
                        errors.Add(new BoardValidationError("UnknownAllowedContent", "Allowed content " + identifier + " is absent from the catalog.", context));
                foreach (ResourceGenerationBudget budget in config.ResourceBudgets)
                    if (!catalog.ContainsFamily(budget.ResourceFamilyId))
                        errors.Add(new BoardValidationError("UnknownResourceFamily", "Budget family " + budget.ResourceFamilyId + " is absent from the catalog.", context));
            }
            foreach (BoardEntityState entity in entities)
            {
                if (config != null && !allowed.Contains(entity.Definition.ContentId))
                    errors.Add(new BoardValidationError("DisallowedContent", "Content " + entity.Definition.ContentId + " is not permitted by the configuration.", context, entity.Position));
                if (!catalog.TryGetDefinition(entity.Definition, out BoardEntityDefinition definition))
                    errors.Add(new BoardValidationError("UnknownContent", "No definition resolves " + entity.Definition + ".", context, entity.Position));
                else if (!definition.Equals(entity.Definition))
                    errors.Add(new BoardValidationError("ContentDefinitionMismatch", "Candidate traits/kind/reward differ from catalog definition " + entity.Definition.ContentId + ".", context, entity.Position));
            }
            foreach (BoardItemSpawn item in items)
            {
                if (!catalog.TryGetItemFamily(item.ItemDefinitionId, out string family))
                    errors.Add(new BoardValidationError("UnknownItemDefinition", "Item definition " + item.ItemDefinitionId + " has no registered resource family.", context));
                else if (family != item.ResourceFamilyId)
                    errors.Add(new BoardValidationError("ItemResourceFamilyMismatch", "Item " + item.InstanceId + " claims family " + item.ResourceFamilyId + " instead of " + family + ".", context));
                if (item.SourceId == null) continue;
                if (!catalog.TryGetSourceDefinition(item.SourceId, out string definition))
                    errors.Add(new BoardValidationError("UnknownSource", "Fixed-source ID " + item.SourceId + " is absent from the catalog.", context));
                else if (definition != item.ItemDefinitionId)
                    errors.Add(new BoardValidationError("SourceDefinitionMismatch", "Source " + item.SourceId + " belongs to " + definition + ", not " + item.ItemDefinitionId + ".", context));
            }
        }

        private static void CheckRange(List<BoardValidationError> errors, string code, int value,
            GenerationRange range, string context, GridPosition? position = null, string detail = null)
        {
            if (value < range.Minimum || value > range.Maximum)
                errors.Add(new BoardValidationError(code, (detail == null ? string.Empty : detail + ": ") +
                    string.Format(CultureInfo.InvariantCulture, "{0} is outside inclusive range {1}..{2}.",
                        value, range.Minimum, range.Maximum), context, position));
        }
    }
}