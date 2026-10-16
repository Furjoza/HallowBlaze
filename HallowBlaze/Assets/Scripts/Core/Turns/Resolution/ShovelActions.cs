using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Pre-M9 transfer adapter: consumes the exact revealed ground item and resolves its configured reward before action cost.</summary>
    public static class CacheRewardCompatibility
    {
        /// <summary>Resolves a validated concrete item once; M9.5 replaces this transfer, not cache/source/item identity.</summary>
        /// <exception cref="InvalidOperationException">The exact item is no longer available or its reward would overflow.</exception>
        public static void Collect(BoardState board, RunState run, EntityId playerId, CacheRewardItem reward, ICollection<GameEvent> events)
        {
            if (reward == null) throw new ArgumentNullException(nameof(reward));
            if (!board.TryGetCacheReward(reward.Entity.Id, out CacheRewardItem current) || !ReferenceEquals(current, reward) ||
                run.Food > int.MaxValue - reward.Entity.Definition.AutomaticFoodReward)
                throw new InvalidOperationException("The validated concrete cache reward cannot be transferred.");
            if (!board.TryRemove(reward.Entity.Id)) throw new InvalidOperationException("The exact cache reward disappeared.");
            run.RestoreFood(reward.Entity.Definition.AutomaticFoodReward);
            events.Add(new ItemCollectedEvent(playerId, reward.Entity.Id));
            events.Add(new FoodRestoredEvent(playerId, reward.Entity.Id, reward.Entity.Definition.AutomaticFoodReward));
        }
    }

    /// <summary>Uncovers one explicitly marked cache by manual work or one shovel use, always producing a concrete item first.</summary>
    public sealed class CacheDigAction : IInteractionAction, IInteractionConsequences
    {
        private readonly ShovelRules rules;
        private readonly bool useShovel;
        private readonly bool collectImmediately;

        /// <summary>Creates the shared excavation behavior without granting a tool or revealing hidden content during preview.</summary>
        /// <param name="rules">Immutable opt-in targets and concrete reward definition.</param>
        /// <param name="useShovel">True completes all work in one charge-consuming tool action; false is the slower manual route.</param>
        /// <param name="collectImmediately">Current pre-M9 compatibility transfer; false retains the same revealed ground item.</param>
        public CacheDigAction(ShovelRules rules, bool useShovel, bool collectImmediately = true)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.useShovel = useShovel;
            this.collectImmediately = collectImmediately;
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
            "; " + rules.RewardDefinition.ContentId + " Food +" + rules.RewardDefinition.AutomaticFoodReward;
        /// <inheritdoc />
        public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target)
        {
            if (!Supports(target) || board.HasRevealedCache(target.Id)) return InteractionAvailability.InvalidTarget;
            if (!board.TryGetEntity(BoardLayer.Terrain, target.Position, out BoardEntityState terrain) ||
                !terrain.Definition.Traits.IsWalkable || terrain.Definition.Traits.IsExit ||
                board.TryGetEntity(BoardLayer.Obstacle, target.Position, out _) || board.TryGetPit(target.Position, out _) ||
                board.TryGetEntity(BoardLayer.Actor, target.Position, out BoardEntityState actor) && !actor.Id.Equals(player.Id))
                return InteractionAvailability.Blocked;
            bool completes = useShovel || board.GetCacheWork(target.Id) >= rules.CacheWork - 1;
            return collectImmediately && completes && run.Food > int.MaxValue - rules.RewardDefinition.AutomaticFoodReward
                ? InteractionAvailability.InvalidState : InteractionAvailability.Available;
        }
        /// <inheritdoc />
        public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target, ICollection<GameEvent> events)
        {
            if (!board.TryWorkCache(target.Id, useShovel ? rules.CacheWork : 1, rules.CacheWork, rules.RewardDefinition,
                out CacheRewardItem reward)) throw new InvalidOperationException("The validated marked cache disappeared.");
            events.Add(new CacheWorkedEvent(target.Id, reward == null ? board.GetCacheWork(target.Id) : rules.CacheWork, rules.CacheWork, reward));
            if (reward != null && collectImmediately) CacheRewardCompatibility.Collect(board, run, player.Id, reward, events);
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