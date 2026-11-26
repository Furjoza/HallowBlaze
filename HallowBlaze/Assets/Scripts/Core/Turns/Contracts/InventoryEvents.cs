using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>One resolved Use or Discard, identified by carried rather than ground ownership.</summary>
    public sealed class InventoryItemActionEvent : GameEvent
    {
        /// <summary>Stable bounded-health resource identity for carried restoration feedback.</summary>
        public const string Health = "health";
        /// <summary>Stable bounded-satiety resource identity for carried restoration feedback.</summary>
        public const string Satiety = "satiety";
        /// <summary>Captures actual applied/wasted restoration; discard has no effect/resource and zero amounts.</summary>
        /// <exception cref="ArgumentException">The action/resource pairing or restoration accounting is invalid.</exception>
        public InventoryItemActionEvent(EntityId playerId, string instanceId, string definitionId,
            InventoryItemActionKind kind, string effectId = null, string resourceId = null, int applied = 0, int wasted = 0)
        {
            if (kind != InventoryItemActionKind.Use && kind != InventoryItemActionKind.Discard ||
                applied < 0 || wasted < 0 || applied > int.MaxValue - wasted ||
                kind == InventoryItemActionKind.Discard && (effectId != null || resourceId != null || applied != 0 || wasted != 0) ||
                kind == InventoryItemActionKind.Use && (string.IsNullOrWhiteSpace(effectId) ||
                    resourceId != Health && resourceId != Satiety || applied == 0 && wasted == 0))
                throw new ArgumentException("Invalid carried-item action accounting.");
            PlayerId = playerId; InstanceId = instanceId; DefinitionId = definitionId; Kind = kind;
            EffectId = effectId; ResourceId = resourceId; Applied = applied; Wasted = wasted;
        }
        /// <summary>Gets the stable event tag for ordered feedback.</summary>
        public override string EventType => "InventoryItemAction";
        /// <summary>Gets the board-local acting player.</summary>
        public EntityId PlayerId { get; }
        /// <summary>Gets the destroyed carried instance's exact identity.</summary>
        public string InstanceId { get; }
        /// <summary>Gets the trusted definition resolved from the carried instance.</summary>
        public string DefinitionId { get; }
        /// <summary>Gets Use or permanent Discard; both remove the carried instance exactly once.</summary>
        public InventoryItemActionKind Kind { get; }
        /// <summary>Gets the trusted use-effect identity, or null for discard.</summary>
        public string EffectId { get; }
        /// <summary>Gets health/satiety for use, or null for discard.</summary>
        public string ResourceId { get; }
        /// <summary>Gets actual restored resource, excluding the later turn cost and enemy damage.</summary>
        public int Applied { get; }
        /// <summary>Gets discarded excess restoration, never stored above the cap.</summary>
        public int Wasted { get; }
        /// <summary>Gets the trusted requested restoration; zero for discard.</summary>
        public int Requested => Applied + Wasted;
    }

    /// <summary>One committed final layout, emitted exactly once before the bag turn's single resource cost.</summary>
    public sealed class InventoryCommittedEvent : GameEvent
    {
        /// <summary>Copies the final ownership for presentation; observing the event cannot mutate the run.</summary>
        public InventoryCommittedEvent(EntityId playerId, int revision, IEnumerable<InventoryLayoutEntry> layout)
        { PlayerId = playerId; Revision = revision; Layout = new List<InventoryLayoutEntry>(layout).AsReadOnly(); }
        /// <summary>Gets the stable event tag.</summary>
        public override string EventType => "InventoryCommitted";
        /// <summary>Gets the board-local actor whose bag was committed.</summary>
        public EntityId PlayerId { get; }
        /// <summary>Gets the accepted new revision, never the draft's previous revision.</summary>
        public int Revision { get; }
        /// <summary>Gets all surviving items' committed owners and anchors.</summary>
        public IReadOnlyList<InventoryLayoutEntry> Layout { get; }
    }
}