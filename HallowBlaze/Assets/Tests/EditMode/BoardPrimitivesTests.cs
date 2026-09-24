using System;
using HallowBlaze.Core.Board.Primitives;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>
    /// Verifies deterministic board primitive contracts without scene or physics state.
    /// </summary>
    public class BoardPrimitivesTests
    {
        /// <summary>
        /// Default EntityKind has hash code 0 and ToString returns empty string.
        /// </summary>
        [Test]
        public void DefaultEntityKind_HashCodeIsZero_And_ToStringIsEmpty()
        {
            var kind = default(EntityKind);
            Assert.That(kind.GetHashCode(), Is.EqualTo(0));
            Assert.That(kind.ToString(), Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// EntityKind constructor throws ArgumentException for null, empty, or whitespace id.
        /// </summary>
        [Test]
        public void EntityKind_Constructor_ThrowsArgumentException_ForInvalidIds()
        {
            Assert.Throws<ArgumentException>(() => new EntityKind(null));
            Assert.Throws<ArgumentException>(() => new EntityKind(string.Empty));
            Assert.Throws<ArgumentException>(() => new EntityKind("   "));
            Assert.Throws<ArgumentException>(() => new EntityKind("\t\n"));
        }

        /// <summary>
        /// EntityKind equality uses ordinal string comparison.
        /// </summary>
        [Test]
        public void EntityKind_Equality_UsesOrdinalComparison()
        {
            var kind1 = new EntityKind("player");
            var kind2 = new EntityKind("player");
            var kind3 = new EntityKind("Player");
            
            Assert.That(kind1, Is.EqualTo(kind2));
            Assert.That(kind1, Is.Not.EqualTo(kind3));
            Assert.That(kind1.Equals(kind2), Is.True);
            Assert.That(kind1.Equals((object)kind2), Is.True);
            Assert.That(kind1.Equals((object)kind3), Is.False);
        }

        /// <summary>
        /// EntityKind CompareTo uses ordinal string comparison.
        /// </summary>
        [Test]
        public void EntityKind_CompareTo_UsesOrdinalComparison()
        {
            var appleKind = new EntityKind("apple");
            var bananaKind = new EntityKind("banana");
            var equalAppleKind = new EntityKind("apple");
            
            Assert.That(appleKind.CompareTo(bananaKind), Is.LessThan(0));
            Assert.That(bananaKind.CompareTo(appleKind), Is.GreaterThan(0));
            Assert.That(appleKind.CompareTo(equalAppleKind), Is.EqualTo(0));
            Assert.That(appleKind.CompareTo(default(EntityKind)), Is.GreaterThan(0));
        }

        /// <summary>
        /// EntityKind GetHashCode uses unchecked hash with seed 17 and multiplier 31 over UTF-16.
        /// </summary>
        [Test]
        public void EntityKind_GetHashCode_UsesDeterministicHash()
        {
            var kind = new EntityKind("player");
            const int expectedHash = 1216907826;
            Assert.That(kind.GetHashCode(), Is.EqualTo(expectedHash));
        }

        /// <summary>
        /// EntityKind with identical text have equal hash codes.
        /// </summary>
        [Test]
        public void EntityKind_IdenticalText_HaveEqualHashCodes()
        {
            var kind1 = new EntityKind("test");
            var kind2 = new EntityKind("test");
            
            Assert.That(kind1.GetHashCode(), Is.EqualTo(kind2.GetHashCode()));
        }

        /// <summary>
        /// Cardinal directions expose consistent offsets and opposites.
        /// </summary>
        [Test]
        public void CardinalDirectionsHaveConsistentOffsets()
        {
            Assert.That(Direction.North, Is.EqualTo(new Direction(0, 1)));
            Assert.That(Direction.East, Is.EqualTo(new Direction(1, 0)));
            Assert.That(Direction.South, Is.EqualTo(new Direction(0, -1)));
            Assert.That(Direction.West, Is.EqualTo(new Direction(-1, 0)));
            foreach (Direction direction in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
            {
                Assert.That(direction.IsValid, Is.True);
                Assert.That(direction.Opposite().Opposite(), Is.EqualTo(direction));
            }
        }

        /// <summary>
        /// Invalid offsets and the default sentinel cannot represent an actionable direction.
        /// </summary>
        [Test]
        public void InvalidDirectionsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new Direction(0, 0));
            Assert.Throws<ArgumentException>(() => new Direction(1, 1));
            Assert.Throws<ArgumentException>(() => new Direction(int.MinValue, 0));
            Assert.Throws<ArgumentException>(() => new Direction(0, int.MaxValue));
            Assert.That(default(Direction).IsValid, Is.False);
            Assert.Throws<InvalidOperationException>(() => default(Direction).Opposite());
        }

        /// <summary>
        /// Cardinal neighbor ordering and offsets agree with named directions.
        /// </summary>
        [Test]
        public void NeighborsUseNorthEastSouthWestOrder()
        {
            var origin = new GridPosition(2, 3);
            Assert.That(origin.GetCardinalNeighbors(), Is.EqualTo(new[]
            {
                new GridPosition(2, 4), new GridPosition(3, 3),
                new GridPosition(2, 2), new GridPosition(1, 3)
            }));
            Assert.That(origin.Move(Direction.North), Is.EqualTo(new GridPosition(2, 4)));
            Assert.That(origin, Is.EqualTo(new GridPosition(2, 3)));
            Assert.That(Direction.North.Opposite(), Is.EqualTo(Direction.South));
            Assert.That(Direction.East.Opposite(), Is.EqualTo(Direction.West));
            Assert.That(Direction.North.ToString(), Is.EqualTo("North"));
            Assert.That(Direction.South.ToString(), Is.EqualTo("South"));
        }

        /// <summary>
        /// Offsets reject invalid directions and overflow, while distances cover the full range.
        /// </summary>
        [Test]
        public void CoordinateArithmeticNeverWraps()
        {
            Assert.Throws<ArgumentException>(() => default(GridPosition).Move(default(Direction)));
            Assert.Throws<OverflowException>(() => new GridPosition(int.MaxValue, 0).Move(Direction.East));
            Assert.Throws<OverflowException>(() => new GridPosition(int.MinValue, 0).Move(Direction.West));
            Assert.Throws<OverflowException>(() => new GridPosition(0, int.MaxValue).Move(Direction.North));
            Assert.Throws<OverflowException>(() => new GridPosition(0, int.MinValue).Move(Direction.South));
            Assert.Throws<OverflowException>(() => new GridPosition(int.MaxValue, 0).GetCardinalNeighbors());
            Assert.That(new GridPosition(int.MinValue, int.MinValue)
                .ManhattanDistance(new GridPosition(int.MaxValue, int.MaxValue)), Is.EqualTo(8589934590L));
        }

        /// <summary>
        /// Every cell of the default board, including the perimeter, is legal.
        /// </summary>
        [Test]
        public void BoardBoundaryAcceptsAllCellsAndRejectsOutside()
        {
            GridBounds bounds = GridPosition.DefaultBoardBounds;
            Assert.That(bounds.Width, Is.EqualTo(8));
            Assert.That(bounds.Height, Is.EqualTo(8));
            for (int column = 0; column < 8; column++)
            {
                for (int row = 0; row < 8; row++)
                {
                    var position = new GridPosition(column, row);
                    Assert.That(position.IsWithin(bounds), Is.True);
                    Assert.DoesNotThrow(() => bounds.RequireContains(position));
                }
            }
            foreach (GridPosition outside in new[]
            {
                new GridPosition(-1, 0), new GridPosition(8, 0),
                new GridPosition(0, -1), new GridPosition(0, 8)
            })
            {
                Assert.That(bounds.Contains(outside), Is.False);
                Assert.Throws<ArgumentOutOfRangeException>(() => bounds.RequireContains(outside));
            }
        }

        /// <summary>
        /// Bounds defaults, invalid ranges, and extreme dimensions have explicit semantics.
        /// </summary>
        [Test]
        public void BoundsHandleDefaultAndExtremeCoordinates()
        {
            Assert.Throws<ArgumentException>(() => new GridBounds(1, 0, 0, 0));
            Assert.Throws<ArgumentException>(() => new GridBounds(0, 1, 0, 0));
            Assert.That(default(GridBounds).Contains(default(GridPosition)), Is.True);
            Assert.That(default(GridBounds).Width, Is.EqualTo(1));
            Assert.That(default(GridBounds).Height, Is.EqualTo(1));
            var fullRange = new GridBounds(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);
            Assert.That(fullRange.Width, Is.EqualTo(4294967296L));
            Assert.That(fullRange.Height, Is.EqualTo(4294967296L));
            Assert.That(fullRange.Center, Is.EqualTo(default(GridPosition)));
            Assert.That(fullRange.Contains(GridPosition.DefaultBoardBounds), Is.True);
            Assert.That(new GridBounds(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue).Center,
                Is.EqualTo(new GridPosition(int.MaxValue, int.MaxValue)));
            Assert.That(new GridBounds(-5, -5, -2, -2).Center, Is.EqualTo(new GridPosition(-3, -3)));
            Assert.That(default(GridBounds).Intersects(new GridBounds(1, 1, 2, 2)), Is.False);
            Assert.That(default(GridBounds).Intersects(GridPosition.DefaultBoardBounds), Is.True);
        }

        /// <summary>
        /// Equal values and fixed vectors retain deterministic hashes.
        /// </summary>
        [Test]
        public void PrimitiveEqualityAndHashVectorsAreStable()
        {
            Assert.That(default(GridPosition), Is.EqualTo(new GridPosition(0, 0)));
            Assert.That(new GridPosition(2, 3).GetHashCode(), Is.EqualTo(9042));
            Assert.That(new GridPosition(2, 3).Equals((object)new GridPosition(2, 3)), Is.True);
            Assert.That(new GridPosition(2, 3).Equals(new GridPosition(3, 2)), Is.False);
            Assert.That(Direction.North.GetHashCode(), Is.EqualTo(8994));
            Assert.That(Direction.North.Equals((object)new Direction(0, 1)), Is.True);
            Assert.That(Direction.North.Equals(Direction.South), Is.False);
            Assert.That(default(GridBounds).GetHashCode(), Is.EqualTo(4757297));
            Assert.That(default(GridBounds).Equals((object)new GridBounds(0, 0, 0, 0)), Is.True);
            Assert.That(default(GridBounds).Equals(GridPosition.DefaultBoardBounds), Is.False);
            Assert.That(default(EntityId), Is.EqualTo(new EntityId(0)));
            Assert.That(new EntityId(4294967298L).GetHashCode(), Is.EqualTo(3));
            Assert.That(new EntityId(42).Equals((object)new EntityId(42)), Is.True);
            Assert.That(new EntityId(42).Equals(new EntityId(43)), Is.False);
        }

        /// <summary>
        /// Numeric and lexicographic sorting do not depend on construction or input order.
        /// </summary>
        [Test]
        public void SortingIsIndependentOfInputOrder()
        {
            var expectedIds = new[] { new EntityId(long.MinValue), new EntityId(0), new EntityId(7), new EntityId(long.MaxValue) };
            var forwardIds = new[] { new EntityId(7), new EntityId(long.MaxValue), new EntityId(0), new EntityId(long.MinValue) };
            var reverseIds = new[] { new EntityId(long.MaxValue), new EntityId(7), new EntityId(0), new EntityId(long.MinValue) };
            Array.Sort(forwardIds);
            Array.Sort(reverseIds);
            Assert.That(forwardIds, Is.EqualTo(expectedIds));
            Assert.That(reverseIds, Is.EqualTo(expectedIds));
            var positions = new[] { new GridPosition(1, 0), new GridPosition(0, 1), new GridPosition(0, -1) };
            Array.Sort(positions);
            Assert.That(positions, Is.EqualTo(new[] { new GridPosition(0, -1), new GridPosition(0, 1), new GridPosition(1, 0) }));
            var directions = new[] { Direction.North, Direction.East, Direction.West, Direction.South };
            Array.Sort(directions);
            Assert.That(directions, Is.EqualTo(new[] { Direction.West, Direction.South, Direction.North, Direction.East }));
        }

        /// <summary>
        /// The domain assembly has no Unity runtime dependencies.
        /// </summary>
        [Test]
        public void DomainAssemblyDoesNotReferenceUnity()
        {
            foreach (var reference in typeof(GridPosition).Assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False, reference.FullName);
            }
        }
    }
}