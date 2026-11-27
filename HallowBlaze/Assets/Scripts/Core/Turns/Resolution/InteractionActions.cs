using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
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
    /// Retains the legacy action identity as disabled; existing consumables require entry, not a stationary retry.
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
            => board.TryGetGroundItem(target.Id, out _) ? InteractionAvailability.DecisionRequired : InteractionAvailability.InvalidTarget;
        /// <inheritdoc />
        public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target,
            ICollection<GameEvent> events)
        {
            throw new InvalidOperationException("Consumables require a complete ground pickup command.");
        }
    }

    /// <summary>Retains the medical action identity as disabled; entry owns Use/Store/Leave, including deliberate cap waste.</summary>
    public sealed class FirstAidInteractionAction : IInteractionAction, IInteractionConsequences
    {
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
            => board.TryGetGroundItem(target.Id, out _) ? InteractionAvailability.DecisionRequired : InteractionAvailability.InvalidTarget;
        /// <inheritdoc />
        public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target, ICollection<GameEvent> events)
        {
            throw new InvalidOperationException("Medical items require a complete ground pickup command.");
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