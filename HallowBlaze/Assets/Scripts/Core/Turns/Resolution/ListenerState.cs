using System;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    /// <summary>Immutable noise-only investigation rule; it grants no player knowledge, attacks or random perception.</summary>
    public sealed class ListenerDefinition
    {
        /// <summary>Gets the stable rule name for the prototype Listener.</summary>
        public string RuleName => "Investigate the latest audible emission; otherwise wait";
    }

    /// <summary>Retains one Listener opportunity. Position and heard-target memory belong exclusively to BoardState.</summary>
    public sealed class ListenerState
    {
        /// <summary>Creates a fresh board-local intent owner without hearing, planning or consuming a turn.</summary>
        /// <param name="actorId">The board-local Listener actor identity.</param>
        /// <param name="definition">Immutable noise-only behavior configuration.</param>
        /// <exception cref="ArgumentNullException">The definition is null.</exception>
        public ListenerState(EntityId actorId, ListenerDefinition definition)
        {
            ActorId = actorId;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        /// <summary>Gets this enemy's stable board-local identity.</summary>
        public EntityId ActorId { get; }
        /// <summary>Gets immutable behavior configuration.</summary>
        public ListenerDefinition Definition { get; }
        /// <summary>Gets the immutable announced opportunity, or null before planning/after consumption.</summary>
        public EnemyIntent LockedIntent { get; private set; }

        /// <summary>Retains one investigation or wait until execution; new noise cannot replace it.</summary>
        /// <param name="intent">A matching actor's investigation or wait.</param>
        /// <exception cref="ArgumentException">The intent is null, belongs to another actor or can attack.</exception>
        /// <exception cref="InvalidOperationException">An opportunity is already retained.</exception>
        public void LockIntent(EnemyIntent intent)
        {
            if (intent == null || !intent.ActorId.Equals(ActorId) ||
                intent.Kind != EnemyIntentKind.Wait && intent.Kind != EnemyIntentKind.Investigate)
                throw new ArgumentException("A Listener retains only its own investigation or wait.", nameof(intent));
            if (LockedIntent != null)
                throw new InvalidOperationException("Consume the announced opportunity before planning another.");
            LockedIntent = intent;
        }

        /// <summary>Consumes exactly one opportunity, including blocked investigations and waits, without replanning.</summary>
        /// <returns>The exact announced opportunity.</returns>
        /// <exception cref="InvalidOperationException">No opportunity is retained.</exception>
        public EnemyIntent ConsumeIntent()
        {
            EnemyIntent intent = LockedIntent ?? throw new InvalidOperationException("No Listener intent is retained.");
            LockedIntent = null;
            return intent;
        }
    }
}