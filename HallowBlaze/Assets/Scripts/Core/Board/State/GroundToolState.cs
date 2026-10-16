using System;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Board.State
{
    /// <summary>A concrete board-owned tool, distinct from its entity, definition and optional authored source.</summary>
    public sealed class GroundToolState
    {
        /// <summary>Captures an existing run-allocated identity without equipping it or allocating during preview.</summary>
        /// <param name="entityId">The exact board item binding; position remains owned by BoardState.</param>
        /// <param name="instanceId">Stable run-local tool identity, preserved through pickup and exchange.</param>
        /// <param name="definitionId">Immutable tool content ID.</param>
        /// <param name="charges">Nonnegative uses, including a depleted but still concrete tool.</param>
        /// <param name="sourceId">Optional authored source location ID, never a tool or entity identity.</param>
        public GroundToolState(EntityId entityId, string instanceId, string definitionId, int charges, string sourceId = null)
        {
            RequireId(instanceId, nameof(instanceId));
            RequireId(definitionId, nameof(definitionId));
            if (sourceId != null) RequireId(sourceId, nameof(sourceId));
            if (charges < 0) throw new ArgumentOutOfRangeException(nameof(charges));
            EntityId = entityId;
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Charges = charges;
            SourceId = sourceId;
        }

        /// <summary>Gets the board item identity, not a Unity object ID.</summary>
        public EntityId EntityId { get; }
        /// <summary>Gets the concrete identity transferred unchanged to equipment.</summary>
        public string InstanceId { get; }
        /// <summary>Gets stable content used for comparison and behavior dispatch.</summary>
        public string DefinitionId { get; }
        /// <summary>Gets remaining uses; pickup never spends or refills a charge.</summary>
        public int Charges { get; }
        /// <summary>Gets optional source metadata; observing or picking up a tool does not discover a mechanical fact.</summary>
        public string SourceId { get; }

        private static void RequireId(string id, string parameter)
        {
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim()) throw new ArgumentException("A stable ID is required.", parameter);
        }
    }
}