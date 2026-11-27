using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>One exact pocket identity observed before committing an ordinary command.</summary>
    public sealed class QuickItemUse
    {
        /// <summary>Captures an intent without trusting its eligibility, ownership or effect.</summary>
        public QuickItemUse(string pocketId, string instanceId, string definitionId)
        { PocketId = pocketId; InstanceId = instanceId; DefinitionId = definitionId; }
        /// <summary>Gets the stable left-pocket or right-pocket ID; the main grid is not quick access.</summary>
        public string PocketId { get; }
        /// <summary>Gets the exact carried instance; rejection never selects a replacement.</summary>
        public string InstanceId { get; }
        /// <summary>Gets the expected trusted definition identity, not a client-supplied effect.</summary>
        public string DefinitionId { get; }
    }

    /// <summary>Attaches exactly one pocket use to a supported primary intent, never to a bag session or another quick envelope.</summary>
    public sealed class QuickPocketCommand : PlayerCommand
    {
        /// <summary>Copies the selection sequence; malformed payloads remain representable for atomic resolver rejection.</summary>
        /// <param name="primary">An ordinary primary or its explicit ground-pickup decision; no command is executed separately.</param>
        /// <param name="playerPosition">The observed source cell; a displaced player cannot reuse this proposal.</param>
        /// <param name="expectedRevision">The inventory revision observed before choosing the pocket.</param>
        /// <param name="items">Exactly one selection. Zero means the caller should submit the unwrapped primary instead.</param>
        public QuickPocketCommand(PlayerCommand primary, GridPosition playerPosition, int expectedRevision,
            IEnumerable<QuickItemUse> items)
        {
            Primary = primary;
            PlayerPosition = playerPosition;
            ExpectedRevision = expectedRevision;
            Items = items == null ? null : new List<QuickItemUse>(items).AsReadOnly();
        }
        /// <summary>Gets the composite intent tag; it has no independently resolved zero-time item command.</summary>
        public override string CommandType => "QuickPocket";
        /// <summary>Gets the immutable primary snapshot; all of its preconditions must pass before consumption.</summary>
        public PlayerCommand Primary { get; }
        /// <summary>Gets the exact player source position that must still match.</summary>
        public GridPosition PlayerPosition { get; }
        /// <summary>Gets the revision that must still match; success increments it and invalidates duplicate submission.</summary>
        public int ExpectedRevision { get; }
        /// <summary>Gets the detached selection list; the resolver rejects null, empty, multiple or nested proposals.</summary>
        public IReadOnlyList<QuickItemUse> Items { get; }
    }

    /// <summary>One consumed pocket instance with actual capped restoration, before primary events and the single cost.</summary>
    public sealed class QuickItemUsedEvent : GameEvent
    {
        /// <summary>Captures the exact pocket/instance and trusted effect accounting from an accepted composite.</summary>
        public QuickItemUsedEvent(EntityId playerId, QuickItemUse item, string effectId, string resourceId, int applied, int wasted)
        {
            PlayerId = playerId; Item = item; EffectId = effectId; ResourceId = resourceId; Applied = applied; Wasted = wasted;
        }
        /// <summary>Gets the stable presentation tag.</summary>
        public override string EventType => "QuickItemUsed";
        /// <summary>Gets the acting board-local player.</summary>
        public EntityId PlayerId { get; }
        /// <summary>Gets the exact consumed pocket identity; no replacement is inferred during replay.</summary>
        public QuickItemUse Item { get; }
        /// <summary>Gets the trusted effect identity.</summary>
        public string EffectId { get; }
        /// <summary>Gets health or satiety, never a client-selected resource.</summary>
        public string ResourceId { get; }
        /// <summary>Gets the actual restored amount, excluding subsequent primary effects, cost and damage.</summary>
        public int Applied { get; }
        /// <summary>Gets excess restoration lost at the resource cap.</summary>
        public int Wasted { get; }
        /// <summary>Gets the original restoration amount.</summary>
        public int Requested => Applied + Wasted;
    }
}