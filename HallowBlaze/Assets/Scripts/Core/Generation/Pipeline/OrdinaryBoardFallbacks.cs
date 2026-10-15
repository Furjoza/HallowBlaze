using System;
using System.Collections.Generic;
using System.Globalization;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation.Validation;

namespace HallowBlaze.Core.Generation.Pipeline
{
    /// <summary>Owns two bounded authored ordinary templates; neither template invokes random candidate generation.</summary>
    public static class OrdinaryBoardFallbacks
    {
        /// <summary>Returns a fresh ordered library of southwest and mirrored southeast authored templates.</summary>
        /// <returns>Two fixed-topology builders; requested content, resource bands and exact features remain mandatory.</returns>
        public static IReadOnlyList<BoardGenerationFallback> CreateLibrary() => Array.AsReadOnly(new[]
        {
            new BoardGenerationFallback("ordinary.southwest.v1", request => Build(request, false)),
            new BoardGenerationFallback("ordinary.southeast.v1", request => Build(request, true))
        });

        private static BoardBlueprint Build(BoardGenerationRequest request, bool mirrored)
        {
            BoardGenerationConfig config = request.Configuration;
            var allowed = new HashSet<string>(config.AllowedContentIds, StringComparer.Ordinal);
            var definitions = new List<BoardEntityDefinition>();
            foreach (BoardEntityDefinition definition in request.Catalog.GetDefinitions())
                if (allowed.Contains(definition.ContentId)) definitions.Add(definition);
            BoardEntityDefinition ground = Find(definitions, definition => definition.Layer == BoardLayer.Terrain &&
                definition.Traits.IsWalkable && !definition.Traits.IsExit);
            BoardEntityDefinition exitTerrain = Find(definitions, definition => definition.Layer == BoardLayer.Terrain &&
                definition.Traits.IsWalkable && definition.Traits.IsExit);
            BoardEntityDefinition player = Find(definitions, definition => definition.Layer == BoardLayer.Actor && definition.Kind.Equals(EntityKind.Player));
            var entities = new List<BoardEntityState>();
            var items = new List<BoardItemSpawn>();
            long nextId = 1;
            GridPosition start = Cell(0, 0, mirrored);
            GridPosition exit = Cell(7, 7, mirrored);
            for (int coordinateX = 0; coordinateX < 8; coordinateX++)
                for (int coordinateY = 0; coordinateY < 8; coordinateY++)
                {
                    GridPosition position = Cell(coordinateX, coordinateY, mirrored);
                    entities.Add(new BoardEntityState(new EntityId(nextId++), position.Equals(exit) ? exitTerrain : ground, position));
                }
            entities.Add(new BoardEntityState(new EntityId(nextId++), player, start));

            var obstacleSlots = new[] { Cell(7, 0, mirrored), Cell(3, 2, mirrored), Cell(3, 3, mirrored),
                Cell(4, 3, mirrored), Cell(4, 4, mirrored), Cell(5, 4, mirrored), Cell(5, 5, mirrored),
                Cell(6, 5, mirrored), Cell(6, 6, mirrored) };
            int obstacleCount = Math.Max(1, config.ObstacleCount.Minimum);
            if (obstacleCount > obstacleSlots.Length || config.EnemyCount.Minimum > 3)
                throw new ArgumentException("Authored template capacity does not cover the requested obstacle/enemy budget.");
            BoardEntityDefinition wall = Find(definitions, definition => definition.Layer == BoardLayer.Obstacle && !definition.Traits.IsWalkable);
            for (int index = 0; index < obstacleCount; index++)
                entities.Add(new BoardEntityState(new EntityId(nextId++), wall, obstacleSlots[index]));
            var enemySlots = new[] { Cell(6, 1, mirrored), Cell(2, 5, mirrored), Cell(5, 6, mirrored) };
            if (config.EnemyCount.Minimum > 0)
            {
                BoardEntityDefinition enemy = Find(definitions, definition => definition.Layer == BoardLayer.Actor && definition.Kind.Equals(EntityKind.Enemy));
                for (int index = 0; index < config.EnemyCount.Minimum; index++)
                    entities.Add(new BoardEntityState(new EntityId(nextId++), enemy, enemySlots[index]));
            }

            foreach (BoardRequiredFeature feature in request.RequiredFeatures)
            {
                BoardEntityState matched = null;
                foreach (BoardEntityState entity in entities)
                    if (entity.Definition.Layer == feature.Definition.Layer && entity.Position.Equals(feature.Position)) matched = entity;
                if (matched != null && !matched.Definition.Equals(feature.Definition))
                    throw new ArgumentException("Authored template conflicts with required feature: " + feature.Definition.ContentId + "@" + feature.Position);
                if (matched == null)
                {
                    matched = new BoardEntityState(new EntityId(nextId++), feature.Definition, feature.Position);
                    entities.Add(matched);
                }
                if (feature.Definition.Layer == BoardLayer.Item && !feature.Definition.Traits.IsExit)
                {
                    if (!request.Catalog.TryGetItemFamily(feature.Definition.ContentId, out string family))
                        throw new ArgumentException("Required item has no registered family: " + feature.Definition.ContentId);
                    items.Add(new BoardItemSpawn(ItemId(feature.Position), matched.Id, feature.Definition.ContentId,
                        family, feature.SourceId, feature.InitiallyObservable));
                }
            }
            foreach (ResourceGenerationBudget budget in config.ResourceBudgets)
            {
                int count = 0;
                foreach (BoardItemSpawn item in items) if (item.ResourceFamilyId == budget.ResourceFamilyId) count++;
                if (count >= budget.ItemCount.Minimum) continue;
                BoardEntityDefinition definition = Find(definitions, candidate => candidate.Layer == BoardLayer.Item &&
                    !candidate.Traits.IsExit && request.Catalog.TryGetItemFamily(candidate.ContentId, out string family) && family == budget.ResourceFamilyId);
                for (int coordinateY = 0; coordinateY < 8 && count < budget.ItemCount.Minimum; coordinateY++)
                    for (int coordinateX = 0; coordinateX < 8 && count < budget.ItemCount.Minimum; coordinateX++)
                    {
                        GridPosition position = Cell(coordinateX, coordinateY, mirrored);
                        if (position.Equals(exit) || !CanPlaceItem(entities, position)) continue;
                        var id = new EntityId(nextId++);
                        entities.Add(new BoardEntityState(id, definition, position));
                        items.Add(new BoardItemSpawn(ItemId(position), id, definition.ContentId, budget.ResourceFamilyId));
                        count++;
                    }
            }
            BoardValidationReport report = BoardValidator.Validate(request.Address, config, entities, items, request.Catalog);
            if (!report.IsValid) throw new BoardGenerationException(report);
            return new BoardBlueprint(request.Address, config, entities, items);
        }

        private static bool CanPlaceItem(List<BoardEntityState> entities, GridPosition position)
        {
            foreach (BoardEntityState entity in entities)
                if (entity.Position.Equals(position) && (entity.Definition.Layer == BoardLayer.Actor ||
                    entity.Definition.Layer == BoardLayer.Item ||
                    (entity.Definition.Layer == BoardLayer.Obstacle && !entity.Definition.Traits.IsWalkable) ||
                    (entity.Definition.Layer == BoardLayer.Terrain && !entity.Definition.Traits.IsWalkable))) return false;
            return true;
        }

        private static BoardEntityDefinition Find(List<BoardEntityDefinition> definitions, Predicate<BoardEntityDefinition> predicate)
        {
            BoardEntityDefinition match = definitions.Find(predicate);
            return match ?? throw new ArgumentException("Authored fallback requires missing or disallowed content.");
        }

        private static GridPosition Cell(int coordinateX, int coordinateY, bool mirrored) =>
            new GridPosition(mirrored ? 7 - coordinateX : coordinateX, coordinateY);
        private static string ItemId(GridPosition position) => "fallback-item-" + position.X.ToString(CultureInfo.InvariantCulture) +
            "-" + position.Y.ToString(CultureInfo.InvariantCulture);
    }
}