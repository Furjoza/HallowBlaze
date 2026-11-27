using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Resolution;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Publishes only existing consumables and rewards as trusted carried content; no supply, rolls or equipment are added.</summary>
    public static class GroundPickupContent
    {
        private static InventoryContent content;

        /// <summary>Loads immutable fixed 1x1 pickup definitions with the existing resource amounts and explicit effect IDs.</summary>
        public static InventoryContent Load()
        {
            if (content != null) return content;
            var items = new List<ItemDefinition>();
            var effects = new List<ConsumableEffectDefinition>();
            foreach (LegacyBoardContentKind kind in new[] { LegacyBoardContentKind.Food, LegacyBoardContentKind.Soda,
                LegacyBoardContentKind.BushFood, LegacyBoardContentKind.Aid })
            {
                if (!LegacyBoardContentCatalog.TryGetDefinition(kind, out LegacyBoardContentDefinition definition))
                    throw new InvalidOperationException("Existing consumable content is missing: " + kind);
                BoardEntityDefinition board = definition.CreateBoardDefinition();
                Add(board.ContentId, kind == LegacyBoardContentKind.Aid ? ConsumableResource.Health : ConsumableResource.Satiety,
                    kind == LegacyBoardContentKind.Aid ? FirstAidInteractionAction.HealAmount : board.AutomaticFoodReward, items, effects);
            }
            Add(OrdinaryBoardContent.BerryFoodId, ConsumableResource.Satiety, OrdinaryBoardContent.BlueberryFoodReward, items, effects);
            Add(OrdinaryBoardContent.CarrotFoodId, ConsumableResource.Satiety, 15, items, effects);
            Add(RadioTowerLandmark.RationsId, ConsumableResource.Satiety, OrdinaryBoardContent.RaspberryFoodReward, items, effects);
            BoardEntityDefinition ration = ShovelContent.LoadRules().RewardDefinition;
            Add(ration.ContentId, ConsumableResource.Satiety, ration.AutomaticFoodReward, items, effects);
            content = new InventoryContent(new ItemCatalog(items), new ConsumableEffectCatalog(effects));
            return content;
        }

        /// <summary>Binds existing entities once to deterministic entry-local identities without granting or consuming any item.</summary>
        public static void Bind(BoardState board, RunState run)
        {
            if (ReferenceEquals(run.InventoryContent, InventoryContent.Empty))
                run.RestoreInventoryState(Load(), run.InventoryRevision, run.Inventory.Placements);
            foreach (BoardEntityState entity in board.GetEntities())
            {
                if (!Load().Items.TryGetDefinition(entity.Definition.ContentId, out _) || board.TryGetGroundTool(entity.Id, out _)) continue;
                if (!board.TryBindGroundItem(new GroundItemState(entity.Id, GroundItemIdentity.Create(run, entity.Id), entity.Definition.ContentId)))
                    throw new InvalidOperationException("A consumable must have exactly one ground binding: " + entity.Id);
            }
        }

        private static void Add(string id, ConsumableResource resource, int amount, List<ItemDefinition> items,
            List<ConsumableEffectDefinition> effects)
        {
            string effectId = "pickup." + id + ".v1";
            items.Add(new ItemDefinition(id, 1, 1, resource == ConsumableResource.Health ? "medical" : "food", "consumable", true, effectId));
            effects.Add(new ConsumableEffectDefinition(effectId, resource, amount));
        }
    }
}