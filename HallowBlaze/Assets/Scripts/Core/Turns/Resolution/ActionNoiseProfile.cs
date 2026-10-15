using System;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Immutable action noise configuration; zero suppresses emission and Wait is always silent.</summary>
    public sealed class ActionNoiseProfile
    {
        /// <summary>Gets the initial prototype profile: movement 2, interaction 4, lifetime 2 accepted commands.</summary>
        public static ActionNoiseProfile Prototype { get; } = new ActionNoiseProfile(2, 4, 2);

        /// <summary>Creates a profile without changing costs, effects, or command validity.</summary>
        /// <param name="movementStrength">Orthogonal path budget for accepted movement; zero is silent.</param>
        /// <param name="interactionStrength">Path budget at the player's position for accepted interaction; zero is silent.</param>
        /// <param name="lifetimeTurns">Subsequent accepted-command boundaries before expiration; at least one.</param>
        /// <exception cref="ArgumentOutOfRangeException">A strength is negative or lifetime is less than one.</exception>
        public ActionNoiseProfile(int movementStrength, int interactionStrength, int lifetimeTurns)
        {
            if (movementStrength < 0)
                throw new ArgumentOutOfRangeException(nameof(movementStrength));
            if (interactionStrength < 0)
                throw new ArgumentOutOfRangeException(nameof(interactionStrength));
            if (lifetimeTurns < 1)
                throw new ArgumentOutOfRangeException(nameof(lifetimeTurns));
            MovementStrength = movementStrength;
            InteractionStrength = interactionStrength;
            LifetimeTurns = lifetimeTurns;
        }

        /// <summary>Gets the movement budget; zero means no event.</summary>
        public int MovementStrength { get; }
        /// <summary>Gets the interaction budget; zero means no event.</summary>
        public int InteractionStrength { get; }
        /// <summary>Gets the number of subsequent accepted-command boundaries before expiration.</summary>
        public int LifetimeTurns { get; }
    }
}