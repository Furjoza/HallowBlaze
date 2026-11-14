using System;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Immutable action noise configuration; zero suppresses emission and Wait is always silent.</summary>
    public sealed class ActionNoiseProfile
    {
        /// <summary>Gets movement strength 2 for its own turn only, and interaction strength 4 with lifetime 2.</summary>
        public static ActionNoiseProfile Prototype { get; } = new ActionNoiseProfile(2, 4, 2, 1);

        /// <summary>Creates a profile without changing costs, effects, or command validity.</summary>
        /// <param name="movementStrength">Orthogonal path budget for accepted movement; zero is silent.</param>
        /// <param name="interactionStrength">Path budget at the player's position for accepted interaction; zero is silent.</param>
        /// <param name="lifetimeTurns">Subsequent accepted-command boundaries before expiration; at least one.</param>
        /// <exception cref="ArgumentOutOfRangeException">A strength is negative or lifetime is less than one.</exception>
        public ActionNoiseProfile(int movementStrength, int interactionStrength, int lifetimeTurns)
            : this(movementStrength, interactionStrength, lifetimeTurns, lifetimeTurns)
        {
        }

        /// <summary>Creates independent movement and interaction lifetimes without changing action costs or validity.</summary>
        /// <param name="movementStrength">Orthogonal movement noise budget; zero is silent.</param>
        /// <param name="interactionStrength">Interaction noise budget; zero is silent.</param>
        /// <param name="lifetimeTurns">Accepted-command boundaries before interaction noise expires; at least one.</param>
        /// <param name="movementLifetimeTurns">Accepted-command boundaries before movement noise expires; one expires before the next enemy phase.</param>
        /// <exception cref="ArgumentOutOfRangeException">A strength is negative or either lifetime is less than one.</exception>
        public ActionNoiseProfile(int movementStrength, int interactionStrength, int lifetimeTurns, int movementLifetimeTurns)
        {
            if (movementStrength < 0)
                throw new ArgumentOutOfRangeException(nameof(movementStrength));
            if (interactionStrength < 0)
                throw new ArgumentOutOfRangeException(nameof(interactionStrength));
            if (lifetimeTurns < 1)
                throw new ArgumentOutOfRangeException(nameof(lifetimeTurns));
            if (movementLifetimeTurns < 1)
                throw new ArgumentOutOfRangeException(nameof(movementLifetimeTurns));
            MovementStrength = movementStrength;
            InteractionStrength = interactionStrength;
            LifetimeTurns = lifetimeTurns;
            MovementLifetimeTurns = movementLifetimeTurns;
        }

        /// <summary>Gets the movement budget; zero means no event.</summary>
        public int MovementStrength { get; }
        /// <summary>Gets the interaction budget; zero means no event.</summary>
        public int InteractionStrength { get; }
        /// <summary>Gets the number of subsequent accepted-command boundaries before expiration.</summary>
        public int LifetimeTurns { get; }
        /// <summary>Gets movement-noise lifetime; one keeps it in the emitting turn but not the next accepted enemy phase.</summary>
        public int MovementLifetimeTurns { get; }
    }
}