using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    public sealed partial class TurnResolver
    {
        private static TurnResult ResolveManageInventory(RunState run, EntityId playerId,
            ManageInventoryCommand command, int selectedCost, int satietyAtStart)
        {
            if (command.ExpectedRevision != run.InventoryRevision)
                return new RejectedTurnResult(CommandRejectionCode.StaleInventory);
            if (command.Actions == null || command.Actions.Count > 2 || command.FinalLayout == null ||
                run.InventoryRevision == int.MaxValue)
                return new RejectedTurnResult(CommandRejectionCode.InvalidInventory);
            var removed = new HashSet<string>(StringComparer.Ordinal);
            var effects = new List<ConsumableEffectDefinition>();
            var events = new List<GameEvent>();
            int projectedHealth = run.Health;
            int projectedSatiety = run.Satiety;
            foreach (InventoryItemAction action in command.Actions)
            {
                if (action == null || action.Kind != InventoryItemActionKind.Use && action.Kind != InventoryItemActionKind.Discard ||
                    !run.Inventory.TryGetPlacement(action.InstanceId, out InventoryPlacement owned) || !removed.Add(action.InstanceId))
                    return new RejectedTurnResult(CommandRejectionCode.InvalidInventory);
                if (action.Kind == InventoryItemActionKind.Discard)
                {
                    events.Add(new InventoryItemActionEvent(playerId, owned.Item.InstanceId, owned.Item.DefinitionId, action.Kind));
                    continue;
                }
                if (!run.InventoryContent.Items.TryGetDefinition(owned.Item.DefinitionId, out ItemDefinition definition) ||
                    !run.InventoryContent.Effects.TryGetDefinition(definition.EffectId, out ConsumableEffectDefinition effect))
                    return new RejectedTurnResult(CommandRejectionCode.InvalidInventory);
                bool health = effect.Resource == ConsumableResource.Health;
                var gain = new ResourceGain(health ? projectedHealth : projectedSatiety, effect.Amount,
                    health ? run.ResourceRules.MaximumHealth : run.ResourceRules.MaximumSatiety);
                if (health) projectedHealth += gain.Applied;
                else projectedSatiety += gain.Applied;
                effects.Add(effect);
                events.Add(new InventoryItemActionEvent(playerId, owned.Item.InstanceId, owned.Item.DefinitionId,
                    action.Kind, effect.Id, health ? InventoryItemActionEvent.Health : InventoryItemActionEvent.Satiety,
                    gain.Applied, gain.Wasted));
            }
            if (command.FinalLayout.Count != run.Inventory.Placements.Count - removed.Count)
                return new RejectedTurnResult(CommandRejectionCode.InvalidInventory);
            var placements = new List<InventoryPlacement>();
            foreach (InventoryLayoutEntry entry in command.FinalLayout)
            {
                if (entry == null || removed.Contains(entry.InstanceId) ||
                    !run.Inventory.TryGetPlacement(entry.InstanceId, out InventoryPlacement owned))
                    return new RejectedTurnResult(CommandRejectionCode.InvalidInventory);
                InventoryContainer container;
                switch (entry.ContainerId)
                {
                    case InventoryLayoutEntry.MainGrid: container = InventoryContainer.MainGrid; break;
                    case InventoryLayoutEntry.LeftPocket: container = InventoryContainer.LeftPocket; break;
                    case InventoryLayoutEntry.RightPocket: container = InventoryContainer.RightPocket; break;
                    default: return new RejectedTurnResult(CommandRejectionCode.InvalidInventory);
                }
                placements.Add(new InventoryPlacement(owned.Item, container, entry.Column, entry.Row));
            }
            if (!run.InventoryContent.TryCreateInventory(placements, out _, out _) ||
                !run.TryCommitInventory(command.ExpectedRevision, placements, out _))
                return new RejectedTurnResult(CommandRejectionCode.InvalidInventory);
            foreach (ConsumableEffectDefinition effect in effects)
                if (effect.TryApply(run, out _) != ConsumableEffectRejection.None)
                    throw new InvalidOperationException("Prevalidated synchronous bag effect could not apply.");
            events.Add(new InventoryCommittedEvent(playerId, run.InventoryRevision, command.FinalLayout));
            ApplyCost(run, playerId, events, selectedCost, satietyAtStart);
            if (run.Satiety == 0) events.Add(new PlayerStarvedEvent(playerId));
            return new AcceptedTurnResult(events);
        }
    }
}