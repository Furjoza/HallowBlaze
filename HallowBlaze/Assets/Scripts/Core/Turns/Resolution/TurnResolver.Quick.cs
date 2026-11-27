using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    public sealed partial class TurnResolver
    {
        private sealed class QuickUsePlan
        {
            internal QuickPocketOption Option;
            internal List<InventoryPlacement> Placements;
        }

        private static CommandRejectionCode ValidateQuick(BoardState board, RunState run, BoardEntityState player,
            QuickPocketCommand command, out QuickUsePlan plan)
        {
            plan = null;
            if (!QuickPocketQuery.Supports(command.Primary) || command.Items == null || command.Items.Count != 1 || command.Items[0] == null)
                return CommandRejectionCode.InvalidCommand;
            if (command.ExpectedRevision != run.InventoryRevision) return CommandRejectionCode.StaleInventory;
            if (!command.PlayerPosition.Equals(player.Position)) return CommandRejectionCode.InvalidTarget;
            QuickItemUse requested = command.Items[0];
            QuickPocketOption option = new QuickPocketQuery().GetOption(board, run, player.Id, requested.PocketId);
            if (option == null || requested.InstanceId != option.Item.InstanceId || requested.DefinitionId != option.Item.DefinitionId)
                return CommandRejectionCode.InvalidInventory;
            if (!option.TryProjectInventory(run, out InventoryState projected)) return CommandRejectionCode.InvalidInventory;
            plan = new QuickUsePlan { Option = option, Placements = new List<InventoryPlacement>(projected.Placements) };
            return CommandRejectionCode.None;
        }

        private static void ApplyQuick(RunState run, EntityId playerId, QuickUsePlan plan, ICollection<GameEvent> events)
        {
            if (plan == null) return;
            if (!run.TryCommitInventory(plan.Option.Revision, plan.Placements, out _) ||
                plan.Option.Effect.TryApply(run, out ResourceGain gain) != ConsumableEffectRejection.None)
                throw new InvalidOperationException("Prevalidated synchronous quick use changed during commit.");
            events.Add(new QuickItemUsedEvent(playerId, plan.Option.Item, plan.Option.Effect.Id,
                plan.Option.Effect.Resource == ConsumableResource.Health ? InventoryItemActionEvent.Health : InventoryItemActionEvent.Satiety,
                gain.Applied, gain.Wasted));
            var layout = new List<InventoryLayoutEntry>();
            foreach (InventoryPlacement placement in plan.Placements)
                layout.Add(new InventoryLayoutEntry(placement.Item.InstanceId,
                    placement.Container == InventoryContainer.MainGrid ? InventoryLayoutEntry.MainGrid :
                    placement.Container == InventoryContainer.LeftPocket ? InventoryLayoutEntry.LeftPocket : InventoryLayoutEntry.RightPocket,
                    placement.Column, placement.Row));
            events.Add(new InventoryCommittedEvent(playerId, run.InventoryRevision, layout));
        }
    }
}