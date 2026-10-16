using System.Globalization;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Board.State
{
    /// <summary>One concrete ground item revealed from a cache, with independent definition, instance and source identities.</summary>
    public sealed class CacheRewardItem
    {
        internal CacheRewardItem(BoardEntityState source, BoardEntityDefinition definition)
        {
            SourceEntityId = source.Id;
            Entity = new BoardEntityState(source.Id, definition, source.Position);
            InstanceId = "cache.reward." + source.Id.Value.ToString(CultureInfo.InvariantCulture);
        }
        /// <summary>Gets a stable textual board-local item identity, unchanged if the reward is left on the ground.</summary>
        public string InstanceId { get; }
        /// <summary>Gets the original marked cache entity; this is not an atlas tool-source ID.</summary>
        public EntityId SourceEntityId { get; }
        /// <summary>Gets the revealed ground entity; its cell binding replaces the marker, never a second overlapping item.</summary>
        public BoardEntityState Entity { get; }
        /// <summary>Gets the concrete reward definition, not the original cache marker's content ID.</summary>
        public string DefinitionId => Entity.Definition.ContentId;
    }
}