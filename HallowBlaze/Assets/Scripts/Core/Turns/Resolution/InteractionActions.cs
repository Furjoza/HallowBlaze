using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Preserves automatic board food until M10.5 while using the same capped consumable-effect contract.</summary>
    public static class AutomaticFoodCompatibility
    {
        /// <summary>Applies one already-validated board reward without item transfer or action cost.</summary>
        /// <exception cref="InvalidOperationException">The live run cannot receive a consumable effect.</exception>
        public static ResourceGain Apply(RunState run, int reward)
        {
            var effect = new ConsumableEffectDefinition("legacy.automatic-food.restore", ConsumableResource.Satiety, reward);
            if (effect.TryApply(run, out ResourceGain gain) != ConsumableEffectRejection.None)
                throw new InvalidOperationException("The validated automatic-food effect cannot be applied.");
            return gain;
        }
    }

    /// <summary>Preserves the existing feedback-only interaction for explicitly interactive non-food entities.</summary>
    public sealed class ContextInteractionAction : IInteractionAction
    {
        private readonly string excludedContentId;
        private readonly string additionalExcludedContentId;

        /// <summary>Preserves generic feedback except for content owned by an explicitly registered action.</summary>
        public ContextInteractionAction(string excludedContentId = null, string additionalExcludedContentId = null)
        {
            this.excludedContentId = excludedContentId;
            this.additionalExcludedContentId = additionalExcludedContentId;
        }

        /// <inheritdoc />
        public string Id => "interact";
        /// <inheritdoc />
        public bool Supports(BoardEntityState target) => target.Definition.Traits.IsInteractable &&
            target.Definition.AutomaticFoodReward == 0 && target.Definition.ContentId != excludedContentId &&
            target.Definition.ContentId != additionalExcludedContentId && target.Definition.ContentId != FirstAidInteractionAction.ContentId;
        /// <inheritdoc />
        public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target)
            => InteractionAvailability.Available;
        /// <inheritdoc />
        public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target,
            ICollection<GameEvent> events) { }
    }

    /// <summary>
    /// Collects an explicitly interactive food item at commit only. Ordinary automatic food remains
    /// movement-owned; tools and future decision-required inventory pickups need separate actions.
    /// </summary>
    public sealed class CollectInteractionAction : IInteractionAction
    {
        /// <inheritdoc />
        public string Id => "collect";
        /// <inheritdoc />
        public bool Supports(BoardEntityState target) => target.Definition.Layer == BoardLayer.Item &&
            target.Definition.Traits.IsInteractable && target.Definition.AutomaticFoodReward > 0;
        /// <inheritdoc />
        public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target)
            => InteractionAvailability.Available;
        /// <inheritdoc />
        public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target,
            ICollection<GameEvent> events)
        {
            if (!board.TryRemove(target.Id))
                throw new InvalidOperationException("A validated collect target disappeared during commit.");
            ResourceGain gain = AutomaticFoodCompatibility.Apply(run, target.Definition.AutomaticFoodReward);
            events.Add(new ItemCollectedEvent(player.Id, target.Id));
            events.Add(new FoodRestoredEvent(player.Id, target.Id, gain.Applied, gain.Wasted));
        }
    }

    /// <summary>Consumes an exact first-aid kit for capped healing; a full-health attempt still rejects for free.</summary>
    public sealed class FirstAidInteractionAction : IInteractionAction, IInteractionConsequences
    {
        private static readonly ConsumableEffectCatalog effects = new ConsumableEffectCatalog(new[]
        {
            new ConsumableEffectDefinition("health.first-aid.v1", ConsumableResource.Health, HealAmount)
        });
        /// <summary>Identifies the existing medical item independently of artwork or Unity tags.</summary>
        public const string ContentId = "legacy.aid";
        /// <summary>Identifies the conscious use command, not automatic pickup or inventory storage.</summary>
        public const string ActionId = "use-first-aid";
        /// <summary>Gets the kit's fixed maximum restoration before its own Health ceiling.</summary>
        public const int HealAmount = 10;
        /// <summary>Gets the initial content balance ceiling; runtime availability uses the run's persisted health cap.</summary>
        public const int HealthCeiling = 100;

        /// <inheritdoc />
        public string Id => ActionId;
        /// <inheritdoc />
        public int NoiseStrength => ActionNoiseProfile.Prototype.InteractionStrength;
        /// <inheritdoc />
        public int NoiseLifetimeTurns => ActionNoiseProfile.Prototype.LifetimeTurns;
        /// <inheritdoc />
        public string DescribeEffect(BoardState board, BoardEntityState target) => "First aid: up to +10 Health";
        /// <inheritdoc />
        public bool Supports(BoardEntityState target) => target.Definition.Layer == BoardLayer.Item &&
            target.Definition.ContentId == ContentId && target.Definition.Traits.IsInteractable;
        /// <inheritdoc />
        public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target)
            => run.Health > 0 && run.Health < run.ResourceRules.MaximumHealth ? InteractionAvailability.Available : InteractionAvailability.InvalidState;
        /// <inheritdoc />
        public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target, ICollection<GameEvent> events)
        {
            if (run.Health >= run.ResourceRules.MaximumHealth || !board.TryRemove(target.Id))
                throw new InvalidOperationException("A validated medical item changed during commit.");
            if (effects.TryApply("health.first-aid.v1", run, out ResourceGain gain) != ConsumableEffectRejection.None)
                throw new InvalidOperationException("The validated medical effect cannot be applied.");
            events.Add(new ItemCollectedEvent(player.Id, target.Id));
            events.Add(new HealthRestoredEvent(player.Id, target.Id, gain.Applied, gain.Wasted));
        }
    }

    /// <summary>Opt-in hook for pushing one authored obstacle one cardinal cell away, without moving the player.</summary>
    public sealed class PushInteractionAction : IInteractionAction
    {
        private readonly string contentId;

        /// <summary>Opts one definition into pushing without changing existing obstacle behavior.</summary>
        /// <param name="contentId">The exact authored obstacle definition; no new content is created.</param>
        public PushInteractionAction(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId)) throw new ArgumentException("Content ID is required.", nameof(contentId));
            this.contentId = contentId;
        }

        /// <inheritdoc />
        public string Id => "push";
        /// <inheritdoc />
        public bool Supports(BoardEntityState target) => target.Definition.Layer == BoardLayer.Obstacle &&
            target.Definition.ContentId == contentId && target.Definition.Traits.IsInteractable;
        /// <inheritdoc />
        public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target)
        {
            if (!TryDestination(player, target, out GridPosition destination) || !board.Bounds.Contains(destination))
                return InteractionAvailability.Blocked;
            if (!board.TryGetEntity(BoardLayer.Terrain, destination, out BoardEntityState terrain) ||
                !terrain.Definition.Traits.IsWalkable || terrain.Definition.Traits.IsExit ||
                board.TryGetEntity(BoardLayer.Obstacle, destination, out _) ||
                board.TryGetEntity(BoardLayer.Actor, destination, out _) ||
                board.TryGetEntity(BoardLayer.Item, destination, out _))
                return InteractionAvailability.Blocked;
            return InteractionAvailability.Available;
        }

        /// <inheritdoc />
        public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target,
            ICollection<GameEvent> events)
        {
            if (!TryDestination(player, target, out GridPosition destination) || !board.TryMove(target.Id, destination))
                throw new InvalidOperationException("A validated push destination changed during commit.");
            events.Add(new EntityMovedEvent(target.Id, target.Position, destination));
        }

        private static bool TryDestination(BoardEntityState player, BoardEntityState target, out GridPosition destination)
        {
            destination = default;
            if (player.Position.ManhattanDistance(target.Position) != 1) return false;
            Direction direction = target.Position.X > player.Position.X ? Direction.East :
                target.Position.X < player.Position.X ? Direction.West :
                target.Position.Y > player.Position.Y ? Direction.North : Direction.South;
            try { destination = target.Position.Move(direction); return true; }
            catch (OverflowException) { return false; }
        }
    }
}