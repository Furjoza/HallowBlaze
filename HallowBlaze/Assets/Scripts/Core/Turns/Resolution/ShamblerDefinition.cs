using System;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>
    /// Identifies an opportunity in the board-local active/rest cycle without advancing it.
    /// </summary>
    public enum ShamblerPhase
    {
        /// <summary>
        /// Allows one planned move, attack, or wait before the next rest phase.
        /// </summary>
        Active = 1,

        /// <summary>
        /// Allows only wait, even when the player is adjacent.
        /// </summary>
        Rest = 2
    }

    /// <summary>
    /// Defines immutable parameters for the accepted shortest-path pursuit with alternating rest rule.
    /// Both current variants start active and alternate one active enemy phase with one rest phase.
    /// This is configuration only: it owns no enemy state, advances no cadence, mutates no board or run,
    /// and is not a persistence DTO. Planning and execution enforce legality separately.
    /// </summary>
    public sealed class ShamblerDefinition
    {
        /// <summary>
        /// Gets the single stable rule name shared by both current damage variants.
        /// </summary>
        public string RuleName => "Shortest-path pursuit with alternating rest";

        /// <summary>
        /// Gets the first phase on a fresh board; all current variants start active.
        /// </summary>
        public ShamblerPhase InitialPhase => ShamblerPhase.Active;

        /// <summary>
        /// Gets the number of executed enemy phases in each active part of the cycle, always one.
        /// An unsuccessful active opportunity still consumes that phase and is followed by rest.
        /// </summary>
        public int ActivePhaseTurns { get; }

        /// <summary>
        /// Gets the number of executed enemy phases in each rest part of the cycle, always one.
        /// Planning, presentation, and rejected commands do not consume these phases.
        /// </summary>
        public int RestPhaseTurns { get; }

        /// <summary>
        /// Gets the attack distance in grid cells, always one; only orthogonal adjacency is allowed.
        /// The target cell must also be legal according to the authoritative board.
        /// </summary>
        public int AttackRange { get; }

        /// <summary>
        /// Gets whether attacks exclude diagonal cells; this is always true for the accepted rule.
        /// </summary>
        public bool OrthogonalAttacksOnly => true;

        /// <summary>
        /// Gets deterministic damage in HP: 10 for the existing Enemy1 variant, 20 for Enemy2.
        /// </summary>
        public int Damage { get; }

        /// <summary>
        /// Creates definition data and rejects any configuration outside the accepted current rule.
        /// Rule identity, initial active phase, and orthogonal-only attacks cannot be overridden.
        /// No planning, execution, or gameplay-state mutation occurs during construction.
        /// </summary>
        /// <param name="damage">Damage in HP, exactly 10 or 20 for the existing variants.</param>
        /// <param name="activePhaseTurns">Executed enemy phases per active part of the cycle, exactly one.</param>
        /// <param name="restPhaseTurns">Executed enemy phases per rest part of the cycle, exactly one.</param>
        /// <param name="attackRange">Orthogonal attack distance in grid cells, exactly one.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A parameter differs from its accepted values; no definition is created.
        /// </exception>
        public ShamblerDefinition(int damage, int activePhaseTurns = 1, int restPhaseTurns = 1, int attackRange = 1)
        {
            if (damage != 10 && damage != 20)
            {
                throw new ArgumentOutOfRangeException(nameof(damage), damage, "Shambler damage must be 10 or 20 HP.");
            }
            if (activePhaseTurns != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(activePhaseTurns), activePhaseTurns,
                    "Shambler must have exactly one active enemy phase per cycle.");
            }
            if (restPhaseTurns != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(restPhaseTurns), restPhaseTurns,
                    "Shambler must have exactly one rest enemy phase per cycle.");
            }
            if (attackRange != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attackRange), attackRange,
                    "Shambler attacks only an orthogonally adjacent cell.");
            }

            Damage = damage;
            ActivePhaseTurns = activePhaseTurns;
            RestPhaseTurns = restPhaseTurns;
            AttackRange = attackRange;
        }
    }
}