using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.State;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class InventoryStateTests
    {
        [Test]
        public void NewBackpacksHaveNineEmptyCellsTwoEmptyPocketsAndNoToolContainers()
        {
            ItemCatalog catalog = Catalog();
            InventoryState first = InventoryState.CreateEmpty(catalog);
            InventoryState second = InventoryState.CreateEmpty(catalog);
            Assert.That(InventoryState.MainGridWidth, Is.EqualTo(3));
            Assert.That(InventoryState.MainGridHeight, Is.EqualTo(3));
            Assert.That(InventoryState.PocketCount, Is.EqualTo(2));
            Assert.That(Enum.GetValues(typeof(InventoryContainer)), Has.Length.EqualTo(3));
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Placements, Is.Empty);
            for (int column = 0; column < 3; column++)
                for (int row = 0; row < 3; row++)
                    Assert.That(first.GetMainGridItem(column, row), Is.Null);
            Assert.That(first.GetPocketItem(InventoryContainer.LeftPocket), Is.Null);
            Assert.That(first.GetPocketItem(InventoryContainer.RightPocket), Is.Null);
            Assert.That(first.TryPlace(At("berry", "berries"), out InventoryState next, out _), Is.True);
            Assert.That(next.Placements, Has.Count.EqualTo(1));
            Assert.That(first.Placements, Is.Empty);
            Assert.That(second.Placements, Is.Empty);
        }

        [Test]
        public void EveryFootprintAndAnchorHasExactNonRotatingBoundsAndCellCoverage()
        {
            for (int width = 1; width <= 4; width++)
                for (int height = 1; height <= 4; height++)
                    for (int column = -1; column <= 3; column++)
                        for (int row = -1; row <= 3; row++)
                        {
                            ItemCatalog catalog = new ItemCatalog(new[] { Definition("item", width, height) });
                            InventoryState empty = InventoryState.CreateEmpty(catalog);
                            InventoryPlacement placement = At("instance", "item", column, row);
                            bool expected = column >= 0 && row >= 0 && column + width <= 3 && row + height <= 3;
                            bool accepted = empty.TryPlace(placement, out InventoryState next, out InventoryDiagnostic error);
                            string context = width + "x" + height + " at " + column + "." + row;
                            Assert.That(accepted, Is.EqualTo(expected), context);
                            Assert.That(error.Code, Is.EqualTo(expected ? InventoryErrorCode.None : InventoryErrorCode.OutOfBounds), context);
                            if (!expected)
                            {
                                Assert.That(next, Is.SameAs(empty), context);
                                continue;
                            }
                            for (int cellColumn = 0; cellColumn < 3; cellColumn++)
                                for (int cellRow = 0; cellRow < 3; cellRow++)
                                {
                                    bool covered = cellColumn >= column && cellColumn < column + width &&
                                        cellRow >= row && cellRow < row + height;
                                    Assert.That(next.GetMainGridItem(cellColumn, cellRow),
                                        covered ? Is.SameAs(placement.Item) : Is.Null, context);
                                }
                        }
        }

        [Test]
        public void RejectedOverlapOrDuplicateCannotMutateTheOriginalSnapshot()
        {
            InventoryState original = Create(At("drink", "drink"));
            Assert.That(original.TryPlace(At("berry", "berries", 0, 1), out InventoryState next,
                out InventoryDiagnostic error), Is.False);
            Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.Overlap));
            Assert.That(error.InstanceId, Is.EqualTo("berry"));
            Assert.That(next, Is.SameAs(original));
            Assert.That(original.TryPlace(new InventoryPlacement(new ItemInstanceState("drink", "drink"),
                InventoryContainer.LeftPocket), out next, out error), Is.False);
            Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.DuplicateInstanceId));
            Assert.That(next, Is.SameAs(original));
            Assert.That(original.Placements, Has.Count.EqualTo(1));
            Assert.That(original.GetPocketItem(InventoryContainer.LeftPocket), Is.Null);
            Assert.That(original.GetMainGridItem(0, 1).InstanceId, Is.EqualTo("drink"));
        }

        [Test]
        public void RepackCanSwapWholeLayoutWithoutTransientCollisionsOrChangingOwnedItems()
        {
            InventoryState original = Create(At("berry", "berries"), At("drink", "drink", 1));
            InventoryPlacement[] repacked = { At("berry", "berries", 1), At("drink", "drink") };
            Assert.That(original.TryRepack(repacked, out InventoryState next, out InventoryDiagnostic error), Is.True);
            Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.None));
            Assert.That(original.GetMainGridItem(0, 0).InstanceId, Is.EqualTo("berry"));
            Assert.That(next.GetMainGridItem(0, 0).InstanceId, Is.EqualTo("drink"));
            Assert.That(next.GetMainGridItem(1, 0).InstanceId, Is.EqualTo("berry"));
            Assert.That(next.TryRepack(new[] { At("berry", "berries") }, out InventoryState rejected, out error), Is.False);
            Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.ItemSetMismatch));
            Assert.That(rejected, Is.SameAs(next));
            Assert.That(next.TryRepack(new[] { At("berry", "drink"), At("drink", "drink", 1) },
                out rejected, out error), Is.False);
            Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.ItemSetMismatch));
            Assert.That(rejected, Is.SameAs(next));
        }

        [Test]
        public void CatalogueRejectsNullAndDuplicateEntriesWithTypedDiagnostics()
        {
            InventoryValidationException duplicate = Assert.Throws<InventoryValidationException>(() =>
                new ItemCatalog(new[] { Definition("berries"), Definition("berries", 1, 2) }));
            Assert.That(duplicate.Diagnostic.Code, Is.EqualTo(InventoryErrorCode.DuplicateDefinitionId));
            Assert.That(duplicate.Diagnostic.DefinitionId, Is.EqualTo("berries"));
            InventoryValidationException missing = Assert.Throws<InventoryValidationException>(() =>
                new ItemCatalog(new ItemDefinition[] { null }));
            Assert.That(missing.Diagnostic.Code, Is.EqualTo(InventoryErrorCode.NullDefinition));
            Assert.Throws<ArgumentNullException>(() => new ItemCatalog(null));
        }

        [Test]
        public void UnknownDefinitionCannotEnterInventoryAndRetainsDiagnosticIdentity()
        {
            InventoryState original = InventoryState.CreateEmpty(Catalog());
            Assert.That(original.TryPlace(At("lost", "unknown"), out InventoryState next,
                out InventoryDiagnostic error), Is.False);
            Assert.That(next, Is.SameAs(original));
            Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.UnknownDefinitionId));
            Assert.That(error.InstanceId, Is.EqualTo("lost"));
            Assert.That(error.DefinitionId, Is.EqualTo("unknown"));
            Assert.That(original.Placements, Is.Empty);
        }

        [TestCase(InventoryContainer.LeftPocket)]
        [TestCase(InventoryContainer.RightPocket)]
        public void PocketAcceptsExactlyOneEligibleOneWideItemOfHeightOneToThree(InventoryContainer pocket)
        {
            for (int width = 1; width <= 4; width++)
                for (int height = 1; height <= 4; height++)
                    foreach (bool eligible in new[] { false, true })
                    {
                        ItemCatalog catalog = new ItemCatalog(new[] { Definition("item", width, height, eligible) });
                        InventoryState empty = InventoryState.CreateEmpty(catalog);
                        InventoryPlacement placement = new InventoryPlacement(new ItemInstanceState("first", "item"), pocket);
                        bool expected = eligible && width == 1 && height <= 3;
                        string context = pocket + " " + width + "x" + height + " eligible=" + eligible;
                        Assert.That(empty.TryPlace(placement, out InventoryState next, out InventoryDiagnostic error),
                            Is.EqualTo(expected), context);
                        Assert.That(error.Code, Is.EqualTo(expected ? InventoryErrorCode.None : InventoryErrorCode.IncompatiblePocket), context);
                        if (!expected)
                        {
                            Assert.That(next, Is.SameAs(empty), context);
                            continue;
                        }
                        Assert.That(next.GetPocketItem(pocket), Is.SameAs(placement.Item), context);
                        Assert.That(next.TryPlace(new InventoryPlacement(new ItemInstanceState("second", "item"), pocket),
                            out InventoryState rejected, out error), Is.False, context);
                        Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.PocketOccupied), context);
                        Assert.That(rejected, Is.SameAs(next), context);
                        for (int column = 0; column < 3; column++)
                            for (int row = 0; row < 3; row++)
                                Assert.That(next.GetMainGridItem(column, row), Is.Null, context);
                    }
        }

        [Test]
        public void NineBagItemsAndBothPocketsAreIndependentSingleItemContainers()
        {
            List<InventoryPlacement> placements = new List<InventoryPlacement>();
            for (int column = 0; column < 3; column++)
                for (int row = 0; row < 3; row++)
                    placements.Add(At("berry." + column + "." + row, "berries", column, row));
            placements.Add(new InventoryPlacement(new ItemInstanceState("left", "berries"), InventoryContainer.LeftPocket));
            placements.Add(new InventoryPlacement(new ItemInstanceState("right", "drink"), InventoryContainer.RightPocket));
            InventoryState full = Create(placements.ToArray());
            Assert.That(full.Placements, Has.Count.EqualTo(11));
            Assert.That(full.GetPocketItem(InventoryContainer.LeftPocket).InstanceId, Is.EqualTo("left"));
            Assert.That(full.GetPocketItem(InventoryContainer.RightPocket).InstanceId, Is.EqualTo("right"));
            Assert.That(full.TryPlace(At("extra", "berries"), out InventoryState rejected,
                out InventoryDiagnostic error), Is.False);
            Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.Overlap));
            Assert.That(rejected, Is.SameAs(full));
        }

        [Test]
        public void DuplicateIdentityIsRejectedAcrossEveryContainerPairIncludingDifferentDefinitions()
        {
            foreach (InventoryContainer first in Enum.GetValues(typeof(InventoryContainer)))
                foreach (InventoryContainer second in Enum.GetValues(typeof(InventoryContainer)))
                {
                    InventoryPlacement[] placements =
                    {
                        new InventoryPlacement(new ItemInstanceState("same", "berries"), first),
                        new InventoryPlacement(new ItemInstanceState("same", "drink"), second)
                    };
                    Assert.That(InventoryState.TryCreate(Catalog(), placements, out InventoryState inventory,
                        out InventoryDiagnostic error), Is.False, first + " -> " + second);
                    Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.DuplicateInstanceId));
                    Assert.That(error.InstanceId, Is.EqualTo("same"));
                    Assert.That(inventory, Is.Null);
                }
        }

        [Test]
        public void RepackMovesBetweenPocketsAndBagButCannotAddRemoveOrRenameInstances()
        {
            InventoryState original = Create(At("berry", "berries"),
                new InventoryPlacement(new ItemInstanceState("drink", "drink"), InventoryContainer.LeftPocket));
            InventoryPlacement[] proposed =
            {
                new InventoryPlacement(new ItemInstanceState("berry", "berries"), InventoryContainer.RightPocket),
                At("drink", "drink", 2, 1)
            };
            Assert.That(original.TryRepack(proposed, out InventoryState moved, out _), Is.True);
            Assert.That(moved.GetPocketItem(InventoryContainer.LeftPocket), Is.Null);
            Assert.That(moved.GetPocketItem(InventoryContainer.RightPocket).InstanceId, Is.EqualTo("berry"));
            Assert.That(original.GetPocketItem(InventoryContainer.LeftPocket).InstanceId, Is.EqualTo("drink"));
            InventoryPlacement[][] invalidSets =
            {
                Array.Empty<InventoryPlacement>(),
                new[] { At("berry", "berries") },
                new[] { At("berry", "berries"), At("drink", "drink", 1), At("extra", "berries", 2) },
                new[] { At("renamed", "berries"), At("drink", "drink", 1) }
            };
            foreach (InventoryPlacement[] invalid in invalidSets)
            {
                Assert.That(original.TryRepack(invalid, out InventoryState rejected, out InventoryDiagnostic error), Is.False);
                Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.ItemSetMismatch));
                Assert.That(rejected, Is.SameAs(original));
            }
            Assert.That(original.TryRepack(new[] { At("berry", "berries"), At("drink", "drink") },
                out InventoryState collision, out InventoryDiagnostic collisionError), Is.False);
            Assert.That(collisionError.Code, Is.EqualTo(InventoryErrorCode.Overlap));
            Assert.That(collision, Is.SameAs(original));
            InventoryState empty = InventoryState.CreateEmpty(Catalog());
            Assert.That(empty.TryRepack(Array.Empty<InventoryPlacement>(), out InventoryState stillEmpty, out _), Is.True);
            Assert.That(stillEmpty.Placements, Is.Empty);
        }

        [Test]
        public void EveryPairOfInBoundsRectanglesRejectsExactlyTheOverlappingPairs()
        {
            List<ItemDefinition> definitions = new List<ItemDefinition>();
            List<InventoryPlacement> firstCandidates = new List<InventoryPlacement>();
            for (int width = 1; width <= 3; width++)
                for (int height = 1; height <= 3; height++)
                {
                    string definitionId = "shape." + width + "." + height;
                    definitions.Add(Definition(definitionId, width, height));
                    for (int column = 0; column <= 3 - width; column++)
                        for (int row = 0; row <= 3 - height; row++)
                            firstCandidates.Add(At("first", definitionId, column, row));
                }
            ItemCatalog catalog = new ItemCatalog(definitions);
            foreach (InventoryPlacement first in firstCandidates)
                foreach (InventoryPlacement candidate in firstCandidates)
                {
                    InventoryPlacement second = At("second", candidate.Item.DefinitionId, candidate.Column, candidate.Row);
                    HashSet<int> occupied = CoveredCells(catalog, first);
                    bool overlaps = CoveredCells(catalog, second).Any(occupied.Contains);
                    bool accepted = InventoryState.TryCreate(catalog, new[] { first, second }, out InventoryState inventory,
                        out InventoryDiagnostic error);
                    string context = first.Item.DefinitionId + "@" + first.Column + "." + first.Row + " / " +
                        second.Item.DefinitionId + "@" + second.Column + "." + second.Row;
                    Assert.That(accepted, Is.EqualTo(!overlaps), context);
                    Assert.That(error.Code, Is.EqualTo(overlaps ? InventoryErrorCode.Overlap : InventoryErrorCode.None), context);
                    Assert.That(inventory == null, Is.EqualTo(overlaps), context);
                }
        }

        [Test]
        public void ConstructionCopiesCollectionsAndAllPublishedContentIsReadOnly()
        {
            List<ItemDefinition> definitions = new List<ItemDefinition> { Definition("berries"), Definition("drink", 1, 2) };
            ItemCatalog catalog = new ItemCatalog(definitions);
            List<InventoryPlacement> placements = new List<InventoryPlacement> { At("berry", "berries") };
            Assert.That(InventoryState.TryCreate(catalog, placements, out InventoryState first, out _), Is.True);
            Assert.That(InventoryState.TryCreate(catalog, placements, out InventoryState second, out _), Is.True);
            definitions.Clear();
            placements.Clear();
            Assert.That(catalog.Definitions, Has.Count.EqualTo(2));
            Assert.That(first.Placements, Has.Count.EqualTo(1));
            Assert.That(second.Placements, Has.Count.EqualTo(1));
            Assert.Throws<NotSupportedException>(() => ((IList<InventoryPlacement>)first.Placements).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<ItemDefinition>)catalog.Definitions).Clear());
            foreach (Type type in new[] { typeof(ItemDefinition), typeof(ItemInstanceState), typeof(InventoryPlacement) })
                Assert.That(type.GetProperties().All(property => !property.CanWrite), Is.True, type.Name);
            Assert.That(first.TryRepack(new[] { At("berry", "berries", 2, 2) }, out InventoryState moved, out _), Is.True);
            Assert.That(moved.GetMainGridItem(2, 2).InstanceId, Is.EqualTo("berry"));
            Assert.That(second.GetMainGridItem(2, 2), Is.Null);
            Assert.That(first.GetMainGridItem(0, 0).InstanceId, Is.EqualTo("berry"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase(" item")]
        [TestCase("item ")]
        public void InvalidContentAndInstanceIdentifiersFailWithTypedDiagnostics(string invalid)
        {
            TestDelegate[] constructions =
            {
                () => Definition(invalid),
                () => new ItemDefinition("valid", 1, 1, invalid, "category", true, "effect"),
                () => new ItemDefinition("valid", 1, 1, "family", invalid, true, "effect"),
                () => new ItemDefinition("valid", 1, 1, "family", "category", true, invalid),
                () => new ItemInstanceState(invalid, "berries"),
                () => new ItemInstanceState("instance", invalid)
            };
            foreach (TestDelegate construction in constructions)
            {
                InventoryValidationException error = Assert.Throws<InventoryValidationException>(construction);
                Assert.That(error.Diagnostic.Code, Is.EqualTo(InventoryErrorCode.InvalidId));
            }
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(-1, 1)]
        [TestCase(1, -1)]
        [TestCase(int.MinValue, int.MinValue)]
        public void DefinitionRejectsNonpositiveDimensions(int width, int height)
        {
            InventoryValidationException error = Assert.Throws<InventoryValidationException>(() => Definition("invalid", width, height));
            Assert.That(error.Diagnostic.Code, Is.EqualTo(InventoryErrorCode.InvalidFootprint));
            Assert.That(error.Diagnostic.DefinitionId, Is.EqualTo("invalid"));
        }

        [Test]
        public void ExtremeDimensionsAndAnchorsCannotOverflowIntoAnAcceptedPlacement()
        {
            ItemCatalog catalog = new ItemCatalog(new[] { Definition("wide", int.MaxValue, 1),
                Definition("tall", 1, int.MaxValue), Definition("huge", int.MaxValue, int.MaxValue), Definition("small") });
            foreach (ItemDefinition definition in catalog.Definitions)
                foreach (int column in new[] { int.MinValue, -1, 0, int.MaxValue })
                    foreach (int row in new[] { int.MinValue, -1, 0, int.MaxValue })
                    {
                        bool expected = definition.Id == "small" && column == 0 && row == 0;
                        Assert.That(InventoryState.TryCreate(catalog, new[] { At("instance", definition.Id, column, row) },
                            out _, out InventoryDiagnostic error), Is.EqualTo(expected));
                        Assert.That(error.Code, Is.EqualTo(expected ? InventoryErrorCode.None : InventoryErrorCode.OutOfBounds));
                    }
        }

        [Test]
        public void ExactOrdinalIdsAreNotSilentlyTrimmedFoldedOrReplaced()
        {
            ItemCatalog catalog = new ItemCatalog(new[] { Definition("berries"), Definition("Berries") });
            Assert.That(catalog.TryGetDefinition("berries", out ItemDefinition lower), Is.True);
            Assert.That(catalog.TryGetDefinition("Berries", out ItemDefinition upper), Is.True);
            Assert.That(upper, Is.Not.SameAs(lower));
            Assert.That(catalog.TryGetDefinition("BERRIES", out _), Is.False);
            Assert.That(catalog.TryGetDefinition(null, out _), Is.False);
            Assert.That(InventoryState.TryCreate(catalog, new[] { At("berry", "berries"), At("Berry", "Berries", 1) },
                out InventoryState inventory, out _), Is.True);
            Assert.That(inventory.TryGetPlacement("Berry", out InventoryPlacement found), Is.True);
            Assert.That(found.Item.DefinitionId, Is.EqualTo("Berries"));
            Assert.That(inventory.TryGetPlacement("BERRY", out found), Is.False);
            Assert.That(found, Is.Null);
            Assert.That(inventory.TryGetPlacement(null, out found), Is.False);
        }

        [Test]
        public void InvalidLayoutsContainersAndPocketAnchorsHaveTypedRejections()
        {
            InventoryState original = Create(At("berry", "berries"));
            Assert.That(InventoryState.TryCreate(Catalog(), null, out InventoryState absent,
                out InventoryDiagnostic error), Is.False);
            Assert.That(absent, Is.Null);
            Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.NullLayout));
            Assert.That(original.TryRepack(null, out InventoryState rejected, out error), Is.False);
            Assert.That(rejected, Is.SameAs(original));
            Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.NullLayout));
            Assert.That(original.TryPlace(null, out rejected, out error), Is.False);
            Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.NullPlacement));
            Assert.That(rejected, Is.SameAs(original));
            Assert.That(original.TryPlace(new InventoryPlacement(new ItemInstanceState("other", "berries"),
                (InventoryContainer)999), out rejected, out error), Is.False);
            Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.InvalidContainer));
            Assert.That(rejected, Is.SameAs(original));
            foreach (InventoryContainer pocket in new[] { InventoryContainer.LeftPocket, InventoryContainer.RightPocket })
                foreach (int column in new[] { -1, 0, 1 })
                    foreach (int row in new[] { -1, 0, 1 })
                    {
                        if (column == 0 && row == 0) continue;
                        Assert.That(original.TryPlace(new InventoryPlacement(new ItemInstanceState("other", "berries"),
                            pocket, column, row), out rejected, out error), Is.False);
                        Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.OutOfBounds));
                        Assert.That(rejected, Is.SameAs(original));
                    }
        }

        [Test]
        public void InvalidQueriesAndNullDependenciesCannotAlterOwnedItems()
        {
            InventoryState inventory = Create(At("berry", "berries"));
            Assert.Throws<ArgumentNullException>(() => InventoryState.CreateEmpty(null));
            Assert.Throws<ArgumentNullException>(() => InventoryState.TryCreate(null,
                Array.Empty<InventoryPlacement>(), out _, out _));
            Assert.Throws<ArgumentNullException>(() => new InventoryPlacement(null, InventoryContainer.MainGrid));
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.GetMainGridItem(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.GetMainGridItem(3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.GetMainGridItem(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.GetMainGridItem(0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.GetPocketItem(InventoryContainer.MainGrid));
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.GetPocketItem((InventoryContainer)(-1)));
            Assert.That(inventory.GetMainGridItem(0, 0).InstanceId, Is.EqualTo("berry"));
        }

        [TestCase(101)]
        [TestCase(1729)]
        [TestCase(20261125)]
        public void SeededRandomLayoutsMatchIndependentOwnershipAndGeometryOracle(int seed)
        {
            System.Random random = new System.Random(seed);
            ItemCatalog catalog = new ItemCatalog(new[] { Definition("small"), Definition("drink", 1, 2),
                Definition("long", 1, 3), Definition("wide", 2, 1), Definition("large", 2, 2),
                Definition("ineligible", 1, 1, false), Definition("too-tall", 1, 4) });
            for (int sample = 0; sample < 1500; sample++)
            {
                List<InventoryPlacement> layout = new List<InventoryPlacement>();
                int count = random.Next(0, 13);
                for (int index = 0; index < count; index++)
                {
                    string definitionId = random.Next(12) == 0 ? "unknown" :
                        catalog.Definitions[random.Next(catalog.Definitions.Count)].Id;
                    string instanceId = "item." + (random.Next(6) == 0 ? 0 : index);
                    InventoryContainer container = (InventoryContainer)random.Next(0, 3);
                    layout.Add(new InventoryPlacement(new ItemInstanceState(instanceId, definitionId), container,
                        container == InventoryContainer.MainGrid ? random.Next(-1, 4) : 0,
                        container == InventoryContainer.MainGrid ? random.Next(-1, 4) : 0));
                }
                bool expected = IsValidLayout(catalog, layout);
                bool accepted = InventoryState.TryCreate(catalog, layout, out InventoryState inventory,
                    out InventoryDiagnostic error);
                string context = "seed=" + seed + " sample=" + sample + " layout=" + string.Join(";", layout.Select(
                    placement => placement.Item.InstanceId + ":" + placement.Item.DefinitionId + "@" +
                        placement.Container + ":" + placement.Column + "." + placement.Row));
                Assert.That(accepted, Is.EqualTo(expected), context);
                Assert.That(error.Code == InventoryErrorCode.None, Is.EqualTo(expected), context);
                if (!accepted)
                {
                    Assert.That(inventory, Is.Null, context);
                    continue;
                }
                Assert.That(inventory.Placements.Select(placement => placement.Item.InstanceId),
                    Is.EqualTo(layout.Select(placement => placement.Item.InstanceId)), context);
                Assert.That(inventory.TryRepack(layout.AsEnumerable().Reverse(), out InventoryState reordered, out _), Is.True, context);
                for (int column = 0; column < 3; column++)
                    for (int row = 0; row < 3; row++)
                    {
                        InventoryPlacement expectedPlacement = layout.SingleOrDefault(placement =>
                            placement.Container == InventoryContainer.MainGrid && CoveredCells(catalog, placement).Contains(column * 3 + row));
                        Assert.That(inventory.GetMainGridItem(column, row)?.InstanceId,
                            Is.EqualTo(expectedPlacement?.Item.InstanceId), context);
                        Assert.That(reordered.GetMainGridItem(column, row)?.InstanceId,
                            Is.EqualTo(expectedPlacement?.Item.InstanceId), context);
                    }
            }
        }

        private static bool IsValidLayout(ItemCatalog catalog, IEnumerable<InventoryPlacement> layout)
        {
            HashSet<string> instanceIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> cells = new HashSet<int>();
            HashSet<InventoryContainer> pockets = new HashSet<InventoryContainer>();
            foreach (InventoryPlacement placement in layout)
            {
                if (!instanceIds.Add(placement.Item.InstanceId) ||
                    !catalog.TryGetDefinition(placement.Item.DefinitionId, out ItemDefinition definition)) return false;
                if (placement.Container != InventoryContainer.MainGrid)
                {
                    if (!definition.QuickPocketEligible || definition.Width != 1 || definition.Height > 3 ||
                        placement.Column != 0 || placement.Row != 0 || !pockets.Add(placement.Container)) return false;
                    continue;
                }
                for (int horizontal = 0; horizontal < definition.Width; horizontal++)
                    for (int vertical = 0; vertical < definition.Height; vertical++)
                    {
                        int column = placement.Column + horizontal;
                        int row = placement.Row + vertical;
                        if (column < 0 || column >= 3 || row < 0 || row >= 3 || !cells.Add(column * 3 + row)) return false;
                    }
            }
            return true;
        }

        private static HashSet<int> CoveredCells(ItemCatalog catalog, InventoryPlacement placement)
        {
            Assert.That(catalog.TryGetDefinition(placement.Item.DefinitionId, out ItemDefinition definition), Is.True);
            HashSet<int> cells = new HashSet<int>();
            for (int horizontal = 0; horizontal < definition.Width; horizontal++)
                for (int vertical = 0; vertical < definition.Height; vertical++)
                    cells.Add((placement.Column + horizontal) * 3 + placement.Row + vertical);
            return cells;
        }

        private static ItemDefinition Definition(string id, int width = 1, int height = 1, bool eligible = true)
        {
            return new ItemDefinition(id, width, height, "satiety", "consumable", eligible, "restore.satiety");
        }

        private static ItemCatalog Catalog()
        {
            return new ItemCatalog(new[] { Definition("berries"), Definition("drink", 1, 2) });
        }

        private static InventoryPlacement At(string instanceId, string definitionId, int column = 0, int row = 0)
        {
            return new InventoryPlacement(new ItemInstanceState(instanceId, definitionId), InventoryContainer.MainGrid, column, row);
        }

        private static InventoryState Create(params InventoryPlacement[] placements)
        {
            Assert.That(InventoryState.TryCreate(Catalog(), placements, out InventoryState inventory,
                out InventoryDiagnostic error), Is.True, error?.Code.ToString());
            return inventory;
        }
    }
}