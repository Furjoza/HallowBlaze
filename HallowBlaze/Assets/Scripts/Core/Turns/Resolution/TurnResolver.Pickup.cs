using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    public sealed partial class TurnResolver
    {
        private readonly GroundPickupQuery groundPickups;

        private TurnResult ResolveGroundPickup(BoardState board, RunState run, BoardEntityState player,
            GroundPickupCommand command, int cost, int satietyAtStart)
        {
            GroundPickupOption option = groundPickups.GetOption(board, run, player.Id, command.Primary);
            if (option == null || command.ExpectedRevision != run.InventoryRevision || command.ExpectedPickupRevision != board.PickupRevision ||
                board.PickupRevision == int.MaxValue || !command.PlayerPosition.Equals(player.Position) ||
                !command.EntityId.Equals(option.Item.EntityId) || command.InstanceId != option.Item.InstanceId ||
                command.DefinitionId != option.Item.DefinitionId || !command.ItemPosition.Equals(option.Position))
                return new RejectedTurnResult(CommandRejectionCode.StalePickup);
            InventoryPlacement placement = null;
            ConsumableEffectDefinition effect = null;
            if (command.Choice != GroundPickupChoice.Leave && run.Inventory.TryGetPlacement(command.InstanceId, out _))
                return new RejectedTurnResult(CommandRejectionCode.InvalidInventory);
            if (command.Choice != GroundPickupChoice.Leave &&
                (!run.InventoryContent.Items.TryGetDefinition(command.DefinitionId, out ItemDefinition definition) ||
                !run.InventoryContent.Effects.TryGetDefinition(definition.EffectId, out effect)))
                return new RejectedTurnResult(CommandRejectionCode.InvalidInventory);
            if (command.Choice == GroundPickupChoice.Store)
            {
                InventoryLayoutEntry entry = command.Placement;
                if (entry == null || entry.InstanceId != command.InstanceId || run.InventoryRevision == int.MaxValue)
                    return new RejectedTurnResult(CommandRejectionCode.InvalidInventory);
                InventoryContainer container;
                switch (entry.ContainerId)
                {
                    case InventoryLayoutEntry.MainGrid: container = InventoryContainer.MainGrid; break;
                    case InventoryLayoutEntry.LeftPocket: container = InventoryContainer.LeftPocket; break;
                    case InventoryLayoutEntry.RightPocket: container = InventoryContainer.RightPocket; break;
                    default: return new RejectedTurnResult(CommandRejectionCode.InvalidInventory);
                }
                placement = new InventoryPlacement(new ItemInstanceState(command.InstanceId, command.DefinitionId), container, entry.Column, entry.Row);
                if (!run.Inventory.TryPlace(placement, out _, out _)) return new RejectedTurnResult(CommandRejectionCode.InvalidInventory);
            }
            else if (command.Choice != GroundPickupChoice.UseNow && command.Choice != GroundPickupChoice.Leave || command.Placement != null)
                return new RejectedTurnResult(CommandRejectionCode.InvalidCommand);
            if (command.Primary is MoveCommand move)
                return ResolveMove(board, run, player, move, cost, satietyAtStart, command, placement, effect);
            var events = new List<GameEvent>();
            if (command.Primary is InteractCommand interact)
            {
                if (interactions.Validate(board, run, player.Id, interact, out BoardEntityState target, out IInteractionAction action) != InteractionAvailability.Available)
                    return new RejectedTurnResult(CommandRejectionCode.StalePickup);
                events.Add(new InteractionPerformedEvent(player.Id, target.Id));
                if (action is CacheDigAction) action.Apply(board, run, player, target, events);
                var consequences = action as IInteractionConsequences;
                ApplyPickup(board, run, player.Id, command, placement, effect, events);
                ApplyCost(run, player.Id, events, cost, satietyAtStart);
                int strength = consequences?.NoiseStrength ?? noiseProfile?.InteractionStrength ?? 0;
                if (strength > 0) events.Add(new NoiseEvent(board.EmitNoise(player.Id, player.Position, strength,
                    consequences?.NoiseLifetimeTurns ?? noiseProfile.LifetimeTurns)));
            }
            else
            {
                var use = (UseToolCommand)command.Primary;
                if (tools.Validate(board, run, player.Id, use, out BoardEntityState target, out ToolAction tool) != CommandRejectionCode.None)
                    return new RejectedTurnResult(CommandRejectionCode.StalePickup);
                events.Add(new ToolUsedEvent(player.Id, use, tool.DefinitionId));
                if (tool.Behavior is CacheDigAction) tool.Behavior.Apply(board, run, player, target, events);
                ApplyPickup(board, run, player.Id, command, placement, effect, events);
                if (tool.ChargeCost > 0)
                {
                    if (!run.TrySpendToolCharge(use.SlotIndex, use.InstanceId))
                        throw new System.InvalidOperationException("Prevalidated excavation charge changed during commit.");
                    events.Add(new ToolChargeSpentEvent(player.Id, use));
                }
                ApplyCost(run, player.Id, events, cost, satietyAtStart);
                if (tool.NoiseStrength > 0) events.Add(new NoiseEvent(board.EmitNoise(player.Id, player.Position,
                    tool.NoiseStrength, tool.NoiseLifetimeTurns), tool.ObservedNoiseFactId));
            }
            PlateGateEventRecorder.Append(board, events);
            if (run.Satiety == 0) events.Add(new PlayerStarvedEvent(player.Id));
            return new AcceptedTurnResult(events);
        }

        private static void ApplyPickup(BoardState board, RunState run, EntityId playerId, GroundPickupCommand command,
            InventoryPlacement placement, ConsumableEffectDefinition effect, ICollection<GameEvent> events)
        {
            ResourceGain gain = default;
            if (!board.TryCommitPickupRevision(command.ExpectedPickupRevision))
                throw new System.InvalidOperationException("Prevalidated pickup sequence changed during synchronous commit.");
            if (command.Choice == GroundPickupChoice.Store)
            {
                var all = new List<InventoryPlacement>(run.Inventory.Placements) { placement };
                if (!run.TryCommitInventory(command.ExpectedRevision, all, out _))
                    throw new System.InvalidOperationException("Prevalidated pickup inventory changed during commit.");
            }
            if (command.Choice != GroundPickupChoice.Leave)
            {
                if (!board.TryRemove(command.EntityId)) throw new System.InvalidOperationException("Prevalidated ground instance disappeared.");
                events.Add(new ItemCollectedEvent(playerId, command.EntityId));
                if (command.Choice == GroundPickupChoice.UseNow && effect.TryApply(run, out gain) != ConsumableEffectRejection.None)
                    throw new System.InvalidOperationException("Prevalidated pickup effect failed.");
            }
            events.Add(new GroundPickupResolvedEvent(playerId, command, command.Choice == GroundPickupChoice.UseNow
                ? effect.Resource == ConsumableResource.Health ? "health" : "satiety" : null, gain.Applied, gain.Wasted));
        }
    }
}