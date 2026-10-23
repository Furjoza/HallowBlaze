using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Opts only authored boulders into a single atomic push and player advance through the shared interaction resolver.</summary>
    public sealed class BoulderPushAction : IInteractionAction, IInteractionConsequences
    {
        /// <summary>Stable content key shared by authored blueprints and the explicit legacy descriptor; never inferred from art or a collider.</summary>
        public const string ContentId = "landmark.boulder";
        /// <summary>Stable explicit interaction choice, separate from the older target-only opt-in push hook.</summary>
        public const string ActionId = "push-boulder";
        /// <summary>Gets the immutable obstacle definition used by authoring and runtime catalogs.</summary>
        public static BoardEntityDefinition Definition { get; } = new BoardEntityDefinition(BoardLayer.Obstacle,
            EntityKind.Obstacle, ContentId, new BoardEntityTraits(false, false, true));
        /// <inheritdoc />
        public string Id => ActionId;
        /// <inheritdoc />
        public int NoiseStrength => ActionNoiseProfile.Prototype.InteractionStrength;
        /// <inheritdoc />
        public int NoiseLifetimeTurns => ActionNoiseProfile.Prototype.LifetimeTurns;
        /// <inheritdoc />
        public string DescribeEffect(BoardState board, BoardEntityState target) => "Push boulder: advance one cell";
        /// <inheritdoc />
        public bool Supports(BoardEntityState target) => target != null && target.Definition.Equals(Definition);
        /// <inheritdoc />
        public InteractionAvailability Evaluate(BoardState board, RunState run, BoardEntityState player, BoardEntityState target) =>
            board.CanPush(player.Id, target.Id) ? InteractionAvailability.Available : InteractionAvailability.Blocked;
        /// <inheritdoc />
        public void Apply(BoardState board, RunState run, BoardEntityState player, BoardEntityState target, ICollection<GameEvent> events)
        {
            if (!board.TryPush(player.Id, target.Id))
                throw new InvalidOperationException("A validated boulder push changed during synchronous commit.");
            board.TryGetEntity(target.Id, out BoardEntityState moved);
            events.Add(new BoulderPushedEvent(player.Id, target.Id, player.Position, target.Position, moved.Position));
        }
    }
}