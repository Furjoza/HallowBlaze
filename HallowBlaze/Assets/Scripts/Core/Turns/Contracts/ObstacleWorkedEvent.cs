using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Describes authoritative clearing progress; completion has already removed the exact obstacle and occupancy.</summary>
    public sealed class ObstacleWorkedEvent : GameEvent
    {
        /// <summary>Captures a completed manual or axe work step without depending on a Unity view or wall HP.</summary>
        public ObstacleWorkedEvent(EntityId actorId, EntityId obstacleId, GridPosition position,
            int completedWork, int requiredWork, bool usedAxe)
        {
            if (requiredWork <= 0) throw new ArgumentOutOfRangeException(nameof(requiredWork));
            if (completedWork <= 0 || completedWork > requiredWork) throw new ArgumentOutOfRangeException(nameof(completedWork));
            ActorId = actorId;
            ObstacleId = obstacleId;
            Position = position;
            CompletedWork = completedWork;
            RequiredWork = requiredWork;
            UsedAxe = usedAxe;
        }
        /// <summary>Gets the player who performed the accepted action.</summary>
        public EntityId ActorId { get; }
        /// <summary>Gets the exact obstacle instance, not its definition or source identity.</summary>
        public EntityId ObstacleId { get; }
        /// <summary>Gets the affected cell for visual and audio feedback after removal.</summary>
        public GridPosition Position { get; }
        /// <summary>Gets completed work after this action.</summary>
        public int CompletedWork { get; }
        /// <summary>Gets configured total work for this obstacle.</summary>
        public int RequiredWork { get; }
        /// <summary>Gets whether this was the charge-consuming axe alternative.</summary>
        public bool UsedAxe { get; }
        /// <summary>Gets whether the obstacle has been removed, making its cell traversable.</summary>
        public bool IsCleared => CompletedWork == RequiredWork;
        /// <inheritdoc />
        public override string EventType => "ObstacleWorked";
    }
}