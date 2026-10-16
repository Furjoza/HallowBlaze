using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Optional deterministic preview and noise metadata, applied only by the central resolver.</summary>
    public interface IInteractionConsequences
    {
        /// <summary>Describes the direct effect using current domain progress, without mutation.</summary>
        string DescribeEffect(BoardState board, BoardEntityState target);
        /// <summary>Gets explicit emission strength; zero is silent rather than the generic interaction default.</summary>
        int NoiseStrength { get; }
        /// <summary>Gets the positive lifetime of that emission.</summary>
        int NoiseLifetimeTurns { get; }
    }

    /// <summary>Applies one manual work unit or one complete axe cut to an explicitly supported soft obstacle.</summary>
    public sealed class ClearObstacleAction : IInteractionAction, IInteractionConsequences
    {
        private readonly ObstacleClearingRules rules;
        private readonly bool usesAxe;

        /// <summary>Creates one pure-preview/synchronous-effect hook; costs, noise and charges remain resolver-owned.</summary>
        /// <param name="rules">Shared immutable content used by both alternatives.</param>
        /// <param name="usesAxe">False exposes manual work; true is registered only under the axe definition.</param>
        public ClearObstacleAction(ObstacleClearingRules rules, bool usesAxe)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.usesAxe = usesAxe;
        }

        /// <inheritdoc />
        public string Id => usesAxe ? "chop" : "clear";
        /// <inheritdoc />
        public int NoiseStrength => usesAxe ? rules.AxeNoiseStrength : rules.ManualNoiseStrength;
        /// <inheritdoc />
        public int NoiseLifetimeTurns => rules.NoiseLifetimeTurns;
        /// <inheritdoc />
        public bool Supports(BoardEntityState target) => rules.Supports(target);
        /// <inheritdoc />
        public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target)
            => Supports(target) ? InteractionAvailability.Available : InteractionAvailability.Unsupported;
        /// <inheritdoc />
        public string DescribeEffect(BoardState board, BoardEntityState target) => usesAxe ? "Chop: clear obstacle" :
            "Clear by hand: " + (rules.RequiredWork - board.GetObstacleWork(target.Id)) + " actions left";

        /// <inheritdoc />
        public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target, ICollection<GameEvent> events)
        {
            int appliedWork = usesAxe ? rules.RequiredWork : 1;
            if (!board.TryWorkObstacle(target.Id, appliedWork, rules.RequiredWork, out int remaining))
                throw new InvalidOperationException("A validated soft obstacle disappeared during clearing.");
            events.Add(new ObstacleWorkedEvent(player.Id, target.Id, target.Position,
                rules.RequiredWork - remaining, rules.RequiredWork, usesAxe));
        }
    }
}