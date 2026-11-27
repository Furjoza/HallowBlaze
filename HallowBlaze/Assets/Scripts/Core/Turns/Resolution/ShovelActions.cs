using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Uncovers one explicitly marked cache by manual work or one shovel use, always producing a concrete item first.</summary>
    public sealed class CacheDigAction : IInteractionAction, IInteractionConsequences
    {
        private readonly ShovelRules rules;
        private readonly bool useShovel;

        /// <summary>Creates the shared excavation behavior without granting a tool or revealing hidden content during preview.</summary>
        /// <param name="rules">Immutable opt-in targets and concrete reward definition.</param>
        /// <param name="useShovel">True completes all work in one charge-consuming tool action; false is the slower manual route.</param>
        public CacheDigAction(ShovelRules rules, bool useShovel)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.useShovel = useShovel;
        }
        /// <inheritdoc />
        public string Id => useShovel ? "unearth" : "uncover";
        /// <inheritdoc />
        public int NoiseStrength => rules.NoiseStrength;
        /// <inheritdoc />
        public int NoiseLifetimeTurns => rules.NoiseLifetimeTurns;
        /// <inheritdoc />
        public bool Supports(BoardEntityState target) => rules.IsCache(target);
        /// <inheritdoc />
        public string DescribeEffect(BoardState board, BoardEntityState target) =>
            (useShovel ? "Unearth cache: 1 action" : "Uncover by hand: " + (rules.CacheWork - board.GetCacheWork(target.Id)) + " actions left") +
            "; " + rules.RewardDefinition.ContentId + " Satiety +" + rules.RewardDefinition.AutomaticFoodReward;
        /// <inheritdoc />
        public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target)
        {
            if (!Supports(target) || board.HasRevealedCache(target.Id)) return InteractionAvailability.InvalidTarget;
            if (!board.TryGetEntity(BoardLayer.Terrain, target.Position, out BoardEntityState terrain) ||
                !terrain.Definition.Traits.IsWalkable || terrain.Definition.Traits.IsExit ||
                board.TryGetEntity(BoardLayer.Obstacle, target.Position, out _) || board.TryGetPit(target.Position, out _) ||
                board.TryGetEntity(BoardLayer.Actor, target.Position, out BoardEntityState actor) && !actor.Id.Equals(player.Id))
                return InteractionAvailability.Blocked;
            return InteractionAvailability.Available;
        }
        /// <summary>Predicts the exact reward only for the final work step without changing work, resources or RNG.</summary>
        public bool TryPreviewReward(BoardState board, RunState run, BoardEntityState target, out GroundItemState item)
        {
            item = null;
            if (!Supports(target) || board.HasRevealedCache(target.Id) ||
                rules.CacheWork - board.GetCacheWork(target.Id) > (useShovel ? rules.CacheWork : 1)) return false;
            item = new GroundItemState(target.Id, GroundItemIdentity.Create(run, target.Id), rules.RewardDefinition.ContentId);
            return true;
        }

        /// <inheritdoc />
        public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target, ICollection<GameEvent> events)
        {
            if (!board.TryWorkCache(target.Id, useShovel ? rules.CacheWork : 1, rules.CacheWork, rules.RewardDefinition,
                out CacheRewardItem reward, GroundItemIdentity.Create(run, target.Id)))
                throw new InvalidOperationException("The validated marked cache disappeared.");
            events.Add(new CacheWorkedEvent(target.Id, reward == null ? board.GetCacheWork(target.Id) : rules.CacheWork, rules.CacheWork, reward));
            if (reward != null && !board.TryBindGroundItem(new GroundItemState(reward.Entity.Id, reward.InstanceId, reward.DefinitionId)))
                throw new InvalidOperationException("A revealed cache must own its exact ground item.");
        }
    }

    /// <summary>Creates a visible temporary pit only on an explicitly authored, empty diggable terrain cell.</summary>
    public sealed class PitDigAction : IInteractionAction, IInteractionConsequences
    {
        private readonly ShovelRules rules;
        /// <summary>Creates the opt-in terrain behavior; player and Listener movement are never trapped by it.</summary>
        public PitDigAction(ShovelRules rules) { this.rules = rules ?? throw new ArgumentNullException(nameof(rules)); }
        /// <inheritdoc />
        public string Id => "dig-pit";
        /// <inheritdoc />
        public int NoiseStrength => rules.NoiseStrength;
        /// <inheritdoc />
        public int NoiseLifetimeTurns => rules.NoiseLifetimeTurns;
        /// <inheritdoc />
        public bool Supports(BoardEntityState target) => rules.IsDiggable(target);
        /// <inheritdoc />
        public string DescribeEffect(BoardState board, BoardEntityState target) => "Dig pit: " + rules.PitLifetimeTurns +
            " turns; Shambler waits until expiry";
        /// <inheritdoc />
        public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target)
            => Supports(target) && board.CanCreatePit(target.Id) ? InteractionAvailability.Available : InteractionAvailability.Blocked;
        /// <inheritdoc />
        public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target, ICollection<GameEvent> events)
        {
            if (!board.TryCreatePit(target.Id, rules.PitLifetimeTurns)) throw new InvalidOperationException("The validated diggable cell changed.");
            board.TryGetPit(target.Position, out BoardPit pit);
            events.Add(new PitCreatedEvent(pit));
        }
    }
}