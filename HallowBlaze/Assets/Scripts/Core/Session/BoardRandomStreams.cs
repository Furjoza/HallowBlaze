using System;
using System.Globalization;
using HallowBlaze.Core.Random;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Session
{
    /// <summary>Names random responsibilities by stable travel identity, never by node/day alone or list position.</summary>
    public static class BoardRandomStreams
    {
        /// <summary>Gets the layout stream ID, including edge, segment, traversal and generation identity.</summary>
        public static string LayoutId(BoardRequest request) => BoardId("board-layout", request);

        /// <summary>Gets the separately addressed cosmetic stream; its draws never advance gameplay.</summary>
        public static string CosmeticId(BoardRequest request) => BoardId("cosmetic-board", request);

        /// <summary>Gets a leg-wide supply stream shared by its segments, but not by a later traversal of that edge.</summary>
        /// <exception cref="ArgumentException">A checkpoint-only request has no route-leg supply identity.</exception>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        public static string RouteSupplyId(BoardRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            BoardAddress address = request.Address;
            if (address == null)
                throw new ArgumentException("Route supply requires a stable leg address.", nameof(request));
            return RandomStreamProvider.StreamId("route-supply", address.RunId, address.LegId, address.EdgeId);
        }

        /// <summary>Gets independent placement for one authored feature/source on a specific board.</summary>
        /// <param name="request">The full board generation identity.</param>
        /// <param name="featureId">The stable authored feature ID, not its catalogue position.</param>
        /// <param name="sourceId">The stable resource/source ID within that feature.</param>
        /// <returns>A restartable placement identity; another source cannot consume this stream's cursor.</returns>
        public static string AuthoredPlacementId(BoardRequest request, string featureId, string sourceId)
        {
            return RandomStreamProvider.StreamId("authored-placement", LayoutId(request), featureId, sourceId);
        }

        private static string BoardId(string purpose, BoardRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            BoardAddress address = request.Address;
            if (address == null)
                return RandomStreamProvider.StreamId(purpose, "checkpoint", request.RunId, request.WorldNodeId,
                    Number(request.CurrentDay), Number(request.BoardSeed));
            return RandomStreamProvider.StreamId(purpose, "segment", address.RunId, address.LegId, address.EdgeId,
                address.SegmentId, Number(address.DayNumber), address.GenerationConfigurationId,
                Number(address.GenerationVersion), Number(address.BoardSeed));
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}