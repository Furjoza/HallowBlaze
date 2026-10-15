using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Generation.Validation
{
    internal sealed class BoardStructureValidation
    {
        internal BoardState State { get; } = new BoardState();
        internal BoardEntityState Player { get; private set; }
        internal IReadOnlyList<GridPosition> Exits { get; private set; }
        internal IReadOnlyList<BoardItemSpawn> Items { get; private set; }
        internal List<BoardEntityState> SuppliedEntities { get; } = new List<BoardEntityState>();
        internal List<BoardItemSpawn> SuppliedItems { get; } = new List<BoardItemSpawn>();

        internal static BoardStructureValidation Inspect(BoardAddress address, BoardGenerationConfig config,
            IEnumerable<BoardEntityState> entities, IEnumerable<BoardItemSpawn> items, List<BoardValidationError> errors)
        {
            var result = new BoardStructureValidation();
            string context = BoardValidationReport.Context(address, config);
            if (address == null) Add(errors, "MissingAddress", "Full board identity is required.", context);
            if (config == null) Add(errors, "MissingConfiguration", "Generation configuration is required.", context);
            if (address != null && config != null &&
                (address.GenerationConfigurationId != config.Id || address.GenerationVersion != config.Version))
                Add(errors, "ConfigurationMismatch", "Address and configuration ID/version must agree.", context);
            if (entities == null) Add(errors, "MissingEntities", "Entity collection is required.", context);
            if (items == null) Add(errors, "MissingItems", "Item collection is required, even when empty.", context);

            var identifiers = new HashSet<EntityId>();
            var occupancy = new HashSet<(BoardLayer, GridPosition)>();
            var exits = new SortedSet<GridPosition>();
            int players = 0;
            foreach (BoardEntityState entity in entities ?? Array.Empty<BoardEntityState>())
            {
                if (entity == null)
                {
                    Add(errors, "NullEntity", "Entity entries cannot be null.", context);
                    continue;
                }
                result.SuppliedEntities.Add(entity);
                if (!result.State.Bounds.Contains(entity.Position))
                    Add(errors, "OutOfBounds", "Entity " + entity.Id + " is outside ordinary 8x8 bounds.", context, entity.Position);
                if (!identifiers.Add(entity.Id))
                    Add(errors, "DuplicateEntity", "Entity ID " + entity.Id + " is repeated.", context, entity.Position);
                if (!occupancy.Add((entity.Definition.Layer, entity.Position)))
                    Add(errors, "LayerConflict", "Multiple entities occupy layer " + entity.Definition.Layer + ".", context, entity.Position);
                result.State.TryAdd(entity);
                if (entity.Definition.Kind.Equals(EntityKind.Player))
                {
                    players++;
                    result.Player = entity;
                    if (entity.Definition.Layer != BoardLayer.Actor)
                        Add(errors, "InvalidPlayerLayer", "Player start must occupy the actor layer.", context, entity.Position);
                }
                if (entity.Definition.Traits.IsExit) exits.Add(entity.Position);
            }
            if (players == 0) Add(errors, "MissingStart", "Exactly one player actor is required.", context);
            if (players > 1) Add(errors, "MultipleStarts", "More than one player start was supplied.", context);
            if (exits.Count == 0) Add(errors, "MissingExit", "At least one logical exit is required.", context);
            if (result.Player != null && exits.Contains(result.Player.Position))
                Add(errors, "StartExitOverlap", "Start and exit must occupy different cells.", context, result.Player.Position);
            result.Exits = new List<GridPosition>(exits).AsReadOnly();

            var ordered = new SortedDictionary<string, BoardItemSpawn>(StringComparer.Ordinal);
            var boundEntities = new HashSet<EntityId>();
            var sources = new HashSet<string>(StringComparer.Ordinal);
            var families = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (BoardItemSpawn item in items ?? Array.Empty<BoardItemSpawn>())
            {
                if (item == null)
                {
                    Add(errors, "NullItem", "Item spawn entries cannot be null.", context);
                    continue;
                }
                result.SuppliedItems.Add(item);
                if (ordered.ContainsKey(item.InstanceId))
                    Add(errors, "DuplicateItemInstance", "Item instance " + item.InstanceId + " is repeated.", context);
                else
                    ordered.Add(item.InstanceId, item);
                if (!boundEntities.Add(item.EntityId))
                    Add(errors, "DuplicateItemEntity", "Multiple item spawns bind entity " + item.EntityId + ".", context);
                if (item.SourceId != null && !sources.Add(item.SourceId))
                    Add(errors, "DuplicateSource", "Fixed-source ID " + item.SourceId + " is repeated.", context);
                if (!result.State.TryGetEntity(item.EntityId, out BoardEntityState entity) ||
                    entity.Definition.Layer != BoardLayer.Item || entity.Definition.Traits.IsExit ||
                    entity.Definition.ContentId != item.ItemDefinitionId)
                    Add(errors, "InvalidItemBinding", "Spawn " + item.InstanceId + " must bind one non-exit item of definition " + item.ItemDefinitionId + ".", context);
                if (families.TryGetValue(item.ItemDefinitionId, out string family) && family != item.ResourceFamilyId)
                    Add(errors, "ItemFamilyConflict", "Definition " + item.ItemDefinitionId + " has conflicting resource families.", context);
                families[item.ItemDefinitionId] = item.ResourceFamilyId;
            }
            foreach (BoardEntityState entity in result.State.GetEntities())
                if (entity.Definition.Layer == BoardLayer.Item && !entity.Definition.Traits.IsExit && !boundEntities.Contains(entity.Id))
                    Add(errors, "MissingItemSpawn", "Item entity " + entity.Id + " requires generic spawn metadata.", context, entity.Position);
            result.Items = new List<BoardItemSpawn>(ordered.Values).AsReadOnly();
            return result;
        }

        private static void Add(List<BoardValidationError> errors, string code, string message, string context, GridPosition? position = null)
        {
            errors.Add(new BoardValidationError(code, message, context, position));
        }
    }
}