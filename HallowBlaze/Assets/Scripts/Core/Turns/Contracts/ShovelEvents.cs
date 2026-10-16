using System;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Records a visible pit created by an accepted shovel use, before charge and action costs.</summary>
    public sealed class PitCreatedEvent : GameEvent
    {
        /// <summary>Captures the exact newly created pit snapshot.</summary>
        public PitCreatedEvent(BoardPit pit) { Pit = pit ?? throw new ArgumentNullException(nameof(pit)); }
        /// <summary>Gets the pit whose full lifetime is retained on its creation command.</summary>
        public BoardPit Pit { get; }
        /// <inheritdoc />
        public override string EventType => "PitCreated";
    }

    /// <summary>Records a Shambler's actual pit entry, not a preview, construction or predicted future effect.</summary>
    public sealed class ShamblerTrappedEvent : GameEvent
    {
        /// <summary>Stable fact documented only after presenting this observed outcome.</summary>
        public const string FactId = "PIT_TRAPS_SHAMBLER";
        /// <summary>Captures a validated pit entry after the actor's actual movement event.</summary>
        public ShamblerTrappedEvent(BoardPit pit)
        {
            Pit = pit ?? throw new ArgumentNullException(nameof(pit));
            if (!pit.TrappedActorId.HasValue) throw new ArgumentException("A trap event requires the held Shambler.", nameof(pit));
        }
        /// <summary>Gets the immutable entered pit and held actor.</summary>
        public BoardPit Pit { get; }
        /// <summary>Gets the supported enemy actually held by the pit.</summary>
        public EntityId ActorId => Pit.TrappedActorId.Value;
        /// <inheritdoc />
        public override string EventType => "ShamblerTrapped";
    }

    /// <summary>Records a cache step and, on completion, its one concrete item before transfer or any action cost.</summary>
    public sealed class CacheWorkedEvent : GameEvent
    {
        /// <summary>Captures validated excavation progress without resolving resource rewards.</summary>
        public CacheWorkedEvent(EntityId cacheId, int completedWork, int requiredWork, CacheRewardItem reward)
        {
            if (requiredWork <= 0 || completedWork <= 0 || completedWork > requiredWork)
                throw new ArgumentOutOfRangeException(nameof(completedWork));
            CacheId = cacheId;
            CompletedWork = completedWork;
            RequiredWork = requiredWork;
            Reward = reward;
        }
        /// <summary>Gets the exact marked source whose reward is never duplicated.</summary>
        public EntityId CacheId { get; }
        /// <summary>Gets completed excavation work after this accepted step.</summary>
        public int CompletedWork { get; }
        /// <summary>Gets configured total work.</summary>
        public int RequiredWork { get; }
        /// <summary>Gets the concrete new ground item on completion, or null for partial work.</summary>
        public CacheRewardItem Reward { get; }
        /// <inheritdoc />
        public override string EventType => "CacheWorked";
    }
}
