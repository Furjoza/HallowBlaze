using System;
using System.Text;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;

namespace HallowBlaze.Core.Session
{
    /// <summary>Derives versioned segment identity without catalogue order, clock or cosmetic randomness.</summary>
    public static class BoardAddressFactory
    {
        /// <summary>Gets the frozen seed encoding domain; changes require explicit compatibility handling.</summary>
        public const string SeedDomain = "hallowblaze.route-board.v1";

        /// <summary>
        /// Validates published road/segment/configuration associations and derives the persisted address.
        /// Named publication upgrades from legacy v1 or ordinary version 1 to ordinary version 2 retain
        /// saved identity, seed and algorithm. Other drift is rejected, never substituted into an active leg.
        /// </summary>
        /// <exception cref="ArgumentException">The saved leg references foreign or changed published content.</exception>
        public static BoardAddress Create(RunState run, WorldDefinition world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            BoardAddress address = CreateFromState(run);
            ActiveRouteLegState leg = run.ActiveLeg;
            if (!world.TryGetEdge(leg.EdgeId, out WorldEdgeDefinition edge) ||
                edge.FromNodeId != leg.FromNodeId || edge.ToNodeId != leg.ToNodeId ||
                !world.TryGetNode(leg.FromNodeId, out _) || !world.TryGetNode(leg.ToNodeId, out _) ||
                edge.SegmentCount != leg.Segments.Count)
                throw new ArgumentException("Active leg does not match the published road.", nameof(run));
            foreach (RouteSegmentState saved in leg.Segments)
                if (!world.TryGetSegment(saved.SegmentId, out RouteSegmentDefinition segment, out WorldEdgeDefinition owner) ||
                    owner.EdgeId != edge.EdgeId || !MatchesPublishedGeneration(saved, segment))
                    throw new ArgumentException("Foreign segment generation identity: " + saved.SegmentId, nameof(run));
            return address;
        }

        private static bool MatchesPublishedGeneration(RouteSegmentState saved, RouteSegmentDefinition published)
        {
            return (published.GenerationVersion == saved.GenerationVersion &&
                published.GenerationConfigurationId == saved.GenerationConfigurationId) ||
                (saved.GenerationConfigurationId == "legacy.board.v1" && saved.GenerationVersion == 1 &&
                published.GenerationConfigurationId == "ordinary.board.v1" &&
                (published.GenerationVersion == 1 || published.GenerationVersion == 2)) ||
                (saved.GenerationConfigurationId == "ordinary.board.v1" && saved.GenerationVersion == 1 &&
                published.GenerationConfigurationId == "ordinary.board.v1" && published.GenerationVersion == 2);
        }

        /// <summary>
        /// Derives identity from validated run-owned values for borrowed-runtime guards.
        /// This does not validate published content; creation/loading owners must also use Create(run, world).
        /// Encoding is FNV-1a32 over UTF-8 strings framed by little-endian byte lengths and little-endian Int32s:
        /// domain, run ID, root seed, leg ID, edge ID, segment ID, day, configuration ID, generation version.
        /// </summary>
        public static BoardAddress CreateFromState(RunState run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            ActiveRouteLegState leg = run.ActiveLeg;
            if (leg == null || run.Status != RunStatus.Active)
                throw new InvalidOperationException("An active run leg is required.");
            RouteSegmentState segment = leg.CurrentSegment;
            uint hash = 2166136261;
            AppendString(ref hash, SeedDomain);
            AppendString(ref hash, run.RunId);
            AppendInt(ref hash, run.RunSeed);
            AppendString(ref hash, leg.LegId);
            AppendString(ref hash, leg.EdgeId);
            AppendString(ref hash, segment.SegmentId);
            AppendInt(ref hash, leg.DayNumber);
            AppendString(ref hash, segment.GenerationConfigurationId);
            AppendInt(ref hash, segment.GenerationVersion);
            return new BoardAddress(run.RunId, leg.LegId, leg.EdgeId, segment.SegmentId, leg.DayNumber,
                segment.GenerationConfigurationId, segment.GenerationVersion, unchecked((int)hash));
        }

        private static void AppendString(ref uint hash, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            AppendInt(ref hash, bytes.Length);
            foreach (byte valueByte in bytes)
                hash = unchecked((hash ^ valueByte) * 16777619);
        }
        private static void AppendInt(ref uint hash, int value)
        {
            for (int shift = 0; shift < 32; shift += 8)
                hash = unchecked((hash ^ (byte)(value >> shift)) * 16777619);
        }
    }
}