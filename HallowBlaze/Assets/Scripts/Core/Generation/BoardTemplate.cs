using System.Collections.Generic;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Generation
{
    /// <summary>Owns one immutable shared local-board template, independent of world roles and generation history.</summary>
    public sealed class BoardTemplate
    {
        internal BoardTemplate(LandmarkTemplate layout) { Layout = layout; }
        internal LandmarkTemplate Layout { get; }
        /// <summary>Gets the stable publication identity, not a route or node key.</summary>
        public string Id => Layout.Id;
        /// <summary>Gets the positive publication version; existing publications must not be overwritten.</summary>
        public int Version => Layout.Version;
        /// <summary>Gets retained configuration metadata; generator balance targets do not constrain manual edits.</summary>
        public BoardGenerationConfig Config => Layout.Config;
        /// <summary>Gets stable variant keys; selecting one never consumes gameplay randomness.</summary>
        public IReadOnlyList<string> VariantIds => Layout.VariantIds;
        /// <summary>Gets declared exits; one mandatory exit is sufficient and others may be optional.</summary>
        public IReadOnlyList<LandmarkOutcomeDefinition> Outcomes => Layout.Outcomes;
        /// <summary>Builds fresh state through the existing addressed blueprint/plate/gate contracts.</summary>
        public LandmarkBoard CreateBoard(BoardAddress address, string variantId) => Layout.CreateBoard(address, variantId);
    }
}