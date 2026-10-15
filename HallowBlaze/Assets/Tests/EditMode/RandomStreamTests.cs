using System;
using HallowBlaze.Core.Random;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class RandomStreamTests
    {
        [Test]
        public void VersionOneMatchesIndependentlyCalculatedFixedVectors()
        {
            AssertVector(1729, "board-layout", 8250716158690206571UL, 10194976890081063435UL,
                new uint[] { 0x00e54039, 0xf2dfb39c, 0x94967634, 0xd553a672, 0xa9ea5748, 0xebb7ec5e });
            AssertVector(int.MinValue, "12:board-layout4:edge9:segment.2", 3935458352312881294UL,
                6883247668132142753UL,
                new uint[] { 0x2f4ec191, 0x26bd1600, 0x3543e6b9, 0x9104041d, 0x2e0bd4e8, 0x3e90b074 });
        }

        [Test]
        public void SameIdentityRestartsWithoutSharingACursor()
        {
            RandomStreamProvider provider = new RandomStreamProvider(1729);
            DeterministicRandomStream first = provider.Create("board-layout");
            DeterministicRandomStream second = provider.Create("board-layout");
            Assert.That(first.DebugLabel, Is.EqualTo("board-layout"));
            Assert.That(first.DrawCount, Is.Zero);
            for (int draw = 0; draw < 100; draw++)
                Assert.That(first.NextUInt32(), Is.EqualTo(second.NextUInt32()));
            Assert.That(first.State, Is.EqualTo(second.State));
            Assert.That(first.DrawCount, Is.EqualTo(100));
            Assert.That(provider.Create("board-layout").DrawCount, Is.Zero);
        }

        [Test]
        public void CosmeticDrawsCannotAdvanceTheBoardStream()
        {
            RandomStreamProvider provider = new RandomStreamProvider(1729);
            DeterministicRandomStream baseline = provider.Create("board-layout");
            DeterministicRandomStream actual = provider.Create("board-layout");
            DeterministicRandomStream cosmetic = provider.Create("cosmetic");
            for (int draw = 0; draw < 100; draw++)
            {
                for (int extra = 0; extra <= draw; extra++)
                    cosmetic.NextUInt32();
                Assert.That(actual.NextUInt32(), Is.EqualTo(baseline.NextUInt32()));
            }
            Assert.That(cosmetic.Increment, Is.Not.EqualTo(actual.Increment));
        }

        [Test]
        public void StableBoardScopesSeparateSegmentsEdgesLegsAndSourcesEvenWhenBoardSeedsMatch()
        {
            RandomStreamProvider provider = new RandomStreamProvider(1729);
            BoardRequest first = AddressedRequest("leg.1", "edge.a", "segment.1");
            BoardRequest second = AddressedRequest("leg.1", "edge.a", "segment.2");
            BoardRequest otherEdge = AddressedRequest("leg.1", "edge.b", "segment.1");
            BoardRequest laterLeg = AddressedRequest("leg.2", "edge.a", "segment.1");
            string layout = BoardRandomStreams.LayoutId(first);
            foreach (BoardRequest other in new[] { second, otherEdge, laterLeg })
            {
                Assert.That(other.CurrentDay, Is.EqualTo(first.CurrentDay));
                Assert.That(other.BoardSeed, Is.EqualTo(first.BoardSeed));
                Assert.That(BoardRandomStreams.LayoutId(other), Is.Not.EqualTo(layout));
                Assert.That(provider.Create(BoardRandomStreams.LayoutId(other)).State,
                    Is.Not.EqualTo(provider.Create(layout).State));
            }
            Assert.That(BoardRandomStreams.RouteSupplyId(second), Is.EqualTo(BoardRandomStreams.RouteSupplyId(first)));
            Assert.That(BoardRandomStreams.RouteSupplyId(laterLeg), Is.Not.EqualTo(BoardRandomStreams.RouteSupplyId(first)));
            Assert.That(BoardRandomStreams.RouteSupplyId(otherEdge), Is.Not.EqualTo(BoardRandomStreams.RouteSupplyId(first)));
            Assert.That(BoardRandomStreams.CosmeticId(first), Is.Not.EqualTo(layout));
            string placement = BoardRandomStreams.AuthoredPlacementId(first, "orchard", "tree.1");
            Assert.That(BoardRandomStreams.AuthoredPlacementId(first, "orchard", "tree.2"), Is.Not.EqualTo(placement));
            Assert.That(BoardRandomStreams.AuthoredPlacementId(first, "cemetery", "tree.1"), Is.Not.EqualTo(placement));
            Assert.That(BoardRandomStreams.AuthoredPlacementId(second, "orchard", "tree.1"), Is.Not.EqualTo(placement));
            DeterministicRandomStream baseline = provider.Create(layout);
            DeterministicRandomStream actual = provider.Create(layout);
            foreach (string independentId in new[] { BoardRandomStreams.RouteSupplyId(first), placement,
                BoardRandomStreams.CosmeticId(first), RandomStreamProvider.StreamId("world-profile", "profile.1"),
                RandomStreamProvider.StreamId("run", "run.1"), RandomStreamProvider.StreamId("puzzle", layout, "puzzle.1") })
            {
                DeterministicRandomStream independent = provider.Create(independentId);
                for (int draw = 0; draw < 50; draw++)
                    independent.NextUInt32();
                Assert.That(actual.NextUInt32(), Is.EqualTo(baseline.NextUInt32()));
            }
        }

        [Test]
        public void IdentityFramingDistinguishesPartsAndPurposes()
        {
            string first = RandomStreamProvider.StreamId("board-layout", "ab", "c");
            string second = RandomStreamProvider.StreamId("board-layout", "a", "bc");
            string separators = RandomStreamProvider.StreamId("board-layout", "a:1:b", "c");
            Assert.That(first, Is.EqualTo("12:board-layout2:ab1:c"));
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(separators, Is.Not.EqualTo(second));
            Assert.That(RandomStreamProvider.StreamId("route-supply", "ab", "c"), Is.Not.EqualTo(first));
            Assert.That(new RandomStreamProvider(7).Create(first).State,
                Is.Not.EqualTo(new RandomStreamProvider(8).Create(first).State));
        }

        [Test]
        public void InvalidIdentitiesAndUnsupportedVersionsAreRejected()
        {
            RandomStreamProvider provider = new RandomStreamProvider(0);
            Assert.Throws<ArgumentException>(() => provider.Create(" "));
            Assert.Throws<ArgumentException>(() => provider.Create(" padded"));
            Assert.Throws<ArgumentException>(() => RandomStreamProvider.StreamId("board", ""));
            Assert.Throws<ArgumentNullException>(() => RandomStreamProvider.StreamId("board", null));
            Assert.Throws<ArgumentException>(() => new RandomStreamProvider(0, "unknown", 1));
            Assert.Throws<ArgumentException>(() => new RandomStreamProvider(0, RandomStreamProvider.AlgorithmId, 2));
        }

        [Test]
        public void RangeBoundariesIncludeSingletonsAndFullInt32WithoutOverflow()
        {
            DeterministicRandomStream stream = new RandomStreamProvider(-1).Create("ranges");
            for (int draw = 0; draw < 1000; draw++)
            {
                Assert.That(stream.NextInt(-3, 4), Is.InRange(-3, 3));
                Assert.That(stream.NextInt(int.MinValue, int.MaxValue), Is.LessThan(int.MaxValue));
                Assert.That(stream.NextInt(int.MaxValue - 1, int.MaxValue), Is.EqualTo(int.MaxValue - 1));
                Assert.That(stream.NextIntInclusive(int.MaxValue, int.MaxValue), Is.EqualTo(int.MaxValue));
                Assert.That(stream.NextIntInclusive(int.MinValue, int.MinValue), Is.EqualTo(int.MinValue));
                Assert.That(stream.NextIntInclusive(int.MinValue, int.MaxValue), Is.InRange(int.MinValue, int.MaxValue));
                Assert.That(stream.NextUnitDouble(), Is.GreaterThanOrEqualTo(0).And.LessThan(1));
            }
            ulong cursor = stream.State;
            ulong draws = stream.DrawCount;
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.NextInt(1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.NextInt(2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.NextIntInclusive(2, 1));
            Assert.That(stream.State, Is.EqualTo(cursor));
            Assert.That(stream.DrawCount, Is.EqualTo(draws));
        }

        [Test]
        public void BoundedDrawRejectsTheBiasedPrefixInsteadOfOnlyTakingModulo()
        {
            RandomStreamProvider provider = new RandomStreamProvider(19);
            DeterministicRandomStream raw = provider.Create("rejection");
            DeterministicRandomStream bounded = provider.Create("rejection");
            const uint width = 2147483649U;
            uint threshold = unchecked(0U - width) % width;
            bool rejected = false;
            for (int draw = 0; draw < 100; draw++)
            {
                uint sample;
                do
                {
                    sample = raw.NextUInt32();
                    rejected |= sample < threshold;
                }
                while (sample < threshold);
                Assert.That(bounded.NextInt(int.MinValue, 1), Is.EqualTo((int)(int.MinValue + (long)(sample % width))));
                Assert.That(bounded.State, Is.EqualTo(raw.State));
                Assert.That(bounded.DrawCount, Is.EqualTo(raw.DrawCount));
            }
            Assert.That(rejected, Is.True);
        }

        private static void AssertVector(int rootSeed, string streamId, ulong initialState,
            ulong increment, uint[] expected)
        {
            DeterministicRandomStream stream = new RandomStreamProvider(rootSeed).Create(streamId);
            Assert.That(stream.State, Is.EqualTo(initialState));
            Assert.That(stream.Increment, Is.EqualTo(increment));
            foreach (uint value in expected)
                Assert.That(stream.NextUInt32(), Is.EqualTo(value));
            Assert.That(stream.DrawCount, Is.EqualTo(expected.Length));
        }

        private static BoardRequest AddressedRequest(string legId, string edgeId, string segmentId)
        {
            var address = new BoardAddress("run.1", legId, edgeId, segmentId, 1, "legacy.board.v1", 1, 42);
            return new BoardRequest(address, 1729, "checkpoint", "forest", "temperate", 1);
        }
    }
}