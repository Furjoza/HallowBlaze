using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Persistence.Mapping
{
    /// <summary>Maps all v7 boundary fields and validates exact inventory ownership against trusted runtime content.</summary>
    public static class RunStateV8Mapper
    {
        /// <summary>Projects detached DTOs; items and grid anchors are ordered by ordinal instance ID for stable repeated writes.</summary>
        /// <exception cref="ArgumentNullException">The run is null.</exception>
        /// <exception cref="PersistenceDataException">The boundary cannot be restored without loss.</exception>
        public static RunStateV8Dto ToDto(RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            InventoryPlacement[] placements = run.Inventory.Placements.OrderBy(value => value.Item.InstanceId, StringComparer.Ordinal).ToArray();
            var dto = new RunStateV8Dto
            {
                Inventory = new InventoryDto
                {
                    Revision = run.InventoryRevision,
                    Items = placements.Select(value => new ItemInstanceDto
                    { InstanceId = value.Item.InstanceId, DefinitionId = value.Item.DefinitionId }).ToArray(),
                    MainGrid = placements.Where(value => value.Container == InventoryContainer.MainGrid)
                        .Select(value => new InventoryAnchorDto
                        { InstanceId = value.Item.InstanceId, Column = value.Column, Row = value.Row }).ToArray(),
                    LeftPocket = run.Inventory.GetPocketItem(InventoryContainer.LeftPocket)?.InstanceId,
                    RightPocket = run.Inventory.GetPocketItem(InventoryContainer.RightPocket)?.InstanceId
                }
            };
            CopyBoundary(RunStateV7Mapper.ToDto(run), dto);
            FromDto(dto, run.InventoryContent);
            return dto;
        }

        /// <summary>Validates every container and required reference before exposing any restored run; unknown items/effects reject whole saves.</summary>
        /// <param name="dto">The current schema document.</param>
        /// <param name="content">Trusted immutable definitions and effects; never read from the document.</param>
        /// <returns>A detached boundary retaining inventory revision without executing effects or actions.</returns>
        /// <exception cref="ArgumentNullException">The document or trusted content is null.</exception>
        /// <exception cref="PersistenceDataException">Schema, retained boundary or inventory ownership is invalid.</exception>
        public static RunState FromDto(RunStateV8Dto dto, InventoryContent content)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (content == null) throw new ArgumentNullException(nameof(content));
            DtoValidation.RequireCurrentSchemaVersion(dto.SchemaVersion, RunStateV8Dto.CurrentSchemaVersion);
            InventoryDto values = dto.Inventory;
            if (values == null || values.Revision < 0 || values.Items == null || values.MainGrid == null ||
                values.MainGrid.Length > InventoryState.MainGridWidth * InventoryState.MainGridHeight ||
                values.Items.Length > InventoryState.MainGridWidth * InventoryState.MainGridHeight + InventoryState.PocketCount)
                DtoValidation.ThrowInvalidValue("inventory");
            try
            {
                var items = new Dictionary<string, ItemInstanceState>(StringComparer.Ordinal);
                foreach (ItemInstanceDto item in values.Items)
                {
                    if (item == null) DtoValidation.ThrowInvalidValue("inventory.items");
                    var instance = new ItemInstanceState(item.InstanceId, item.DefinitionId);
                    if (items.ContainsKey(instance.InstanceId)) DtoValidation.ThrowInvalidValue("inventory.items.instanceId");
                    if (!content.Items.TryGetDefinition(instance.DefinitionId, out ItemDefinition definition) ||
                        !content.Effects.TryGetDefinition(definition.EffectId, out _))
                        throw new PersistenceDataException(PersistenceDataError.MissingInventoryContent,
                            "inventory.items.definitionId", "Required inventory content is unavailable; the save was not substituted.");
                    items.Add(instance.InstanceId, instance);
                }
                var placements = new List<InventoryPlacement>();
                foreach (InventoryAnchorDto anchor in values.MainGrid)
                {
                    if (anchor == null) DtoValidation.ThrowInvalidValue("inventory.mainGrid");
                    placements.Add(new InventoryPlacement(Resolve(items, anchor.InstanceId), InventoryContainer.MainGrid,
                        anchor.Column, anchor.Row));
                }
                if (values.LeftPocket != null)
                    placements.Add(new InventoryPlacement(Resolve(items, values.LeftPocket), InventoryContainer.LeftPocket));
                if (values.RightPocket != null)
                    placements.Add(new InventoryPlacement(Resolve(items, values.RightPocket), InventoryContainer.RightPocket));
                if (placements.Count != items.Count) DtoValidation.ThrowInvalidValue("inventory.ownership");
                var previous = new RunStateV7Dto();
                CopyBoundary(dto, previous);
                RunState run = RunStateV7Mapper.FromDto(previous);
                run.RestoreInventoryState(content, values.Revision, placements);
                return run;
            }
            catch (InventoryValidationException exception)
            {
                DtoValidation.ThrowInvalidValue("inventory." + exception.Diagnostic.Code);
            }
            catch (ArgumentException) { DtoValidation.ThrowInvalidValue("inventory"); }
            return null;
        }

        private static ItemInstanceState Resolve(Dictionary<string, ItemInstanceState> items, string instanceId)
        {
            if (instanceId == null || !items.TryGetValue(instanceId, out ItemInstanceState item))
            {
                DtoValidation.ThrowInvalidValue("inventory.instanceReference");
                return null;
            }
            return item;
        }

        private static void CopyBoundary(RunStateV7Dto source, RunStateV7Dto destination)
        {
            destination.RunId = source.RunId; destination.RunSeed = source.RunSeed;
            destination.CompletedDayCount = source.CompletedDayCount; destination.ReachedCheckpointNodeId = source.ReachedCheckpointNodeId;
            destination.TraversalSequence = source.TraversalSequence; destination.Health = source.Health;
            destination.Satiety = source.Satiety; destination.Status = source.Status; destination.ToolSlots = source.ToolSlots;
            destination.TravelHistory = source.TravelHistory; destination.ActiveLeg = source.ActiveLeg;
            destination.RandomAlgorithmId = source.RandomAlgorithmId; destination.RandomAlgorithmVersion = source.RandomAlgorithmVersion;
            destination.ToolInstanceSequence = source.ToolInstanceSequence; destination.ActiveLandmark = source.ActiveLandmark;
            destination.CompletedLandmarks = source.CompletedLandmarks; destination.BoardSelectionPlan = source.BoardSelectionPlan;
            destination.ResourceRules = source.ResourceRules;
        }
    }
}