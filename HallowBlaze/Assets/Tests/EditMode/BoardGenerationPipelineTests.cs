using System;
using System.Collections.Generic;
using System.Diagnostics;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Pipeline;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Core.State;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies bounded orchestration using explicit candidate/fallback outcomes rather than chance.</summary>
    public sealed class BoardGenerationPipelineTests
    {
        /// <summary>Always-failing candidates stop at the configured bound and select the same authored result on repeat.</summary>
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(8)]
        [Timeout(5000)]
        public void AlwaysFailStopsAtBoundAndUsesValidatedFallback(int limit)
        {
            BoardGenerationRequest request = Request();
            BoardBlueprint good = Good(request);
            var library = new[] { new BoardGenerationFallback("fixture", _ => good) };
            var seeds = new List<int>();
            Stopwatch elapsed = Stopwatch.StartNew();
            BoardGenerationResult first = BoardGenerationPipeline.Generate(request, (_, seed) => { seeds.Add(seed); return null; }, library, limit);
            Assert.That(seeds.Count, Is.EqualTo(limit));
            Assert.That(new HashSet<int>(seeds).Count, Is.EqualTo(limit));
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(first.UsedFallback, Is.True);
            Assert.That(first.FallbackId, Is.EqualTo("fixture"));
            Assert.That(first.SelectedAttempt, Is.Null);
            Assert.That(first.Blueprint, Is.SameAs(good));
            Assert.That(first.Attempts.Count, Is.EqualTo(limit + 1));
            BoardGenerationResult second = BoardGenerationPipeline.Generate(request, (_, __) => null, library, limit);
            Assert.That(second.ToString(), Is.EqualTo(first.ToString()));
            Assert.That(elapsed.Elapsed.TotalSeconds, Is.LessThan(5));
            TestContext.WriteLine(first.ToString());
        }

        /// <summary>Success on the Nth call neither generates another candidate nor touches a fallback.</summary>
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(8)]
        public void SuccessOnNthAttemptStopsImmediately(int successfulCall)
        {
            BoardGenerationRequest request = Request();
            BoardBlueprint good = Good(request);
            int calls = 0;
            BoardGenerationResult result = BoardGenerationPipeline.Generate(request,
                (_, __) => ++calls == successfulCall ? good : null,
                new[] { new BoardGenerationFallback("must-not-run", _ => throw new InvalidOperationException()) });
            Assert.That(calls, Is.EqualTo(successfulCall));
            Assert.That(result.SelectedAttempt, Is.EqualTo(successfulCall - 1));
            Assert.That(result.UsedFallback, Is.False);
            Assert.That(result.Attempts.Count, Is.EqualTo(successfulCall));
        }

        /// <summary>Corrupt fallback cannot become a blueprint, and exhaustion retains the full reproducible context.</summary>
        [Test]
        public void BrokenFallbackReturnsExplicitFailure()
        {
            BoardGenerationRequest request = Request();
            BoardGenerationResult result = BoardGenerationPipeline.Generate(request, (_, __) => null,
                new[] { new BoardGenerationFallback("broken", _ => null) }, 2);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Blueprint, Is.Null);
            Assert.That(result.Attempts.Count, Is.EqualTo(3));
            Assert.That(result.Attempts[2].Report.IsValid, Is.False);
            foreach (string context in new[] { "run=run", "leg=leg", "edge=road", "segment=segment", "day=1", "seed=42", "config=ordinary.board.v1/1", "fallback=broken", "MissingEntities" })
                Assert.That(result.ToString(), Does.Contain(context));
        }

        /// <summary>Invalid limits cannot invoke a builder; null callbacks and malformed fallback registrations are API misuse.</summary>
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(9)]
        [TestCase(int.MaxValue)]
        public void InvalidLimitNeverInvokesGenerator(int limit)
        {
            int calls = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => BoardGenerationPipeline.Generate(Request(),
                (_, __) => { calls++; return null; }, Array.Empty<BoardGenerationFallback>(), limit));
            Assert.That(calls, Is.Zero);
        }

        /// <summary>Foreign addresses and changed budget envelopes are rejected even when the provided blueprint is valid.</summary>
        [Test]
        public void ForeignBlueprintCannotChangeRequestedIdentity()
        {
            BoardGenerationRequest request = Request();
            BoardGenerationRequest other = Request("foreign");
            BoardGenerationResult result = BoardGenerationPipeline.Generate(request, (_, __) => Good(other),
                Array.Empty<BoardGenerationFallback>(), 1);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ToString(), Does.Contain("CandidateIdentityMismatch"));
        }

        /// <summary>Sub-seeds are stable across requests and depend on full identity without modifying the saved seed.</summary>
        [Test]
        public void SubSeedsAreAddressedAndRestartable()
        {
            BoardGenerationRequest request = Request();
            for (int index = 0; index < 8; index++)
            {
                Assert.That(BoardGenerationPipeline.DeriveSubSeed(request, index),
                    Is.EqualTo(BoardGenerationPipeline.DeriveSubSeed(Request(), index)));
                Assert.That(BoardGenerationPipeline.DeriveSubSeed(request, index),
                    Is.Not.EqualTo(BoardGenerationPipeline.DeriveSubSeed(Request("other"), index)));
            }
            Assert.That(request.Address.BoardSeed, Is.EqualTo(42));
        }

        /// <summary>Each authored topology passes independently for every production pressure band, including enemy variants.</summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(7)]
        [TestCase(10)]
        public void EveryAuthoredFallbackPassesProductionBudgets(int day)
        {
            var address = new BoardAddress("run", "leg", "road", "segment", day, OrdinaryBoardGenerator.ConfigurationId, 1, 42);
            var request = new BoardGenerationRequest(address, OrdinaryBoardContent.CreateConfiguration(address), OrdinaryBoardContent.CreateCatalog(), 77);
            foreach (BoardGenerationFallback fallback in OrdinaryBoardFallbacks.CreateLibrary())
            {
                BoardGenerationResult result = BoardGenerationPipeline.Generate(request, (_, __) => null, new[] { fallback }, 1);
                Assert.That(result.IsSuccess, Is.True, result.ToString());
                Assert.That(result.UsedFallback, Is.True);
                Assert.That(result.Blueprint.Address, Is.SameAs(address));
                Assert.That(result.Blueprint.Config, Is.SameAs(request.Configuration));
                Assert.That(result.Blueprint.InitialResources[0].ItemCount, Is.EqualTo(2));
                Assert.That(BoardValidator.Validate(result.Blueprint, request.Catalog).IsValid, Is.True);
                TestContext.WriteLine(result.ToString());
            }
        }

        /// <summary>A fallback honors an exact supply band and hidden fixed source without mutating caller collections.</summary>
        [Test]
        public void FallbackPreservesResourceBandAndAuthoredSource()
        {
            BoardGenerationRequest original = Request();
            BoardGenerationConfig config = original.Configuration;
            config = new BoardGenerationConfig(config.Id, config.Version, config.PathLength, config.BranchPoints,
                config.ObstacleCount, config.EnemyCount, config.MaximumConcurrentThreats, config.MinimumSafeStartMoves,
                config.AllowedContentIds, new[] { new ResourceGenerationBudget("food", new GenerationRange(4, 4)) });
            var definition = new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "legacy.food", BoardEntityTraits.Default, 10);
            BoardBlueprint ordinary = Good(original);
            var definitions = new List<BoardEntityDefinition>();
            var keys = new HashSet<(BoardLayer, string)>();
            foreach (BoardEntityState entity in ordinary.Entities)
                if (keys.Add((entity.Definition.Layer, entity.Definition.ContentId))) definitions.Add(entity.Definition);
            foreach (string itemId in new[] { "legacy.food", "legacy.soda" })
                if (keys.Add((BoardLayer.Item, itemId))) definitions.Add(new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item,
                    itemId, BoardEntityTraits.Default, itemId == "legacy.food" ? 10 : 20));
            foreach (string enemyId in new[] { OrdinaryBoardContent.Enemy10Id, OrdinaryBoardContent.Enemy20Id })
                definitions.Add(new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, enemyId));
            var catalog = new BoardValidationCatalog(definitions,
                new Dictionary<string, string> { { "legacy.food", "food" }, { "legacy.soda", "food" } },
                new Dictionary<string, string> { { "fixed-food", "legacy.food" } },
                new Dictionary<string, ShamblerDefinition> { { OrdinaryBoardContent.Enemy10Id, new ShamblerDefinition(10) },
                    { OrdinaryBoardContent.Enemy20Id, new ShamblerDefinition(20) } });
            var features = new List<BoardRequiredFeature> { new BoardRequiredFeature(definition, new GridPosition(2, 1), "fixed-food", false) };
            var request = new BoardGenerationRequest(original.Address, config, catalog, 77, features);
            features.Clear();
            BoardBlueprint withoutSource = OrdinaryBoardGenerator.Generate(request.Address, config, catalog, 77);
            BoardGenerationResult result = BoardGenerationPipeline.Generate(request, (_, __) => withoutSource, OrdinaryBoardFallbacks.CreateLibrary(), 2);
            Assert.That(result.Attempts[0].Report.Errors[0].Code, Is.EqualTo("RequiredFeature"));
            Assert.That(result.IsSuccess, Is.True, result.ToString());
            Assert.That(result.UsedFallback, Is.True);
            Assert.That(result.Blueprint.InitialResources[0].ItemCount, Is.EqualTo(4));
            Assert.That(result.Blueprint.InitialResources[0].ObservableItemCount, Is.EqualTo(3));
            BoardItemSpawn fixedItem = null;
            foreach (BoardItemSpawn item in result.Blueprint.Items) if (item.SourceId == "fixed-food") fixedItem = item;
            Assert.That(fixedItem, Is.Not.Null);
            foreach (BoardEntityState entity in result.Blueprint.Entities)
                if (entity.Id.Equals(fixedItem.EntityId)) Assert.That(entity.Position, Is.EqualTo(new GridPosition(2, 1)));
        }

        /// <summary>No fallback can discard an impossible exact feature or relax a forbidden resource band.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void IncompatibleRequirementsFailWithoutRelaxation(bool resourceConflict)
        {
            BoardGenerationRequest original = Request();
            BoardGenerationConfig config = original.Configuration;
            if (resourceConflict) config = new BoardGenerationConfig(config.Id, config.Version, config.PathLength, config.BranchPoints,
                config.ObstacleCount, config.EnemyCount, config.MaximumConcurrentThreats, config.MinimumSafeStartMoves,
                config.AllowedContentIds, new[] { new ResourceGenerationBudget("food", new GenerationRange(0, 0)) });
            var food = new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "legacy.food", BoardEntityTraits.Default, 10);
            var feature = resourceConflict ? new BoardRequiredFeature(food, new GridPosition(2, 1)) :
                new BoardRequiredFeature(new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "legacy.player"), new GridPosition(3, 3));
            var request = new BoardGenerationRequest(original.Address, config, original.Catalog, 77, new[] { feature });
            BoardGenerationResult result = BoardGenerationPipeline.Generate(request, (_, __) => null, OrdinaryBoardFallbacks.CreateLibrary(), 1);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Blueprint, Is.Null);
            Assert.That(result.Attempts.Count, Is.EqualTo(3));
            Assert.That(result.ToString(), Does.Contain(resourceConflict ? "ResourceBudget" : "MultipleStarts"));
        }

        /// <summary>Production version 2 reproduces both selected attempt and full blueprint without changing the persisted address.</summary>
        [TestCase(1)]
        [TestCase(4)]
        [TestCase(10)]
        public void ProductionPipelineSelectsDeterministically(int day)
        {
            var address = new BoardAddress("run", "leg", "road", "segment", day, OrdinaryBoardGenerator.ConfigurationId, 2, 42);
            var request = new BoardGenerationRequest(address, OrdinaryBoardContent.CreateConfiguration(address), OrdinaryBoardContent.CreateCatalog(), 77);
            BoardGenerationResult first = BoardGenerationPipeline.GenerateOrdinary(request);
            BoardGenerationResult second = BoardGenerationPipeline.GenerateOrdinary(request);
            Assert.That(first.IsSuccess, Is.True, first.ToString());
            Assert.That(first.Blueprint, Is.EqualTo(second.Blueprint));
            Assert.That(first.ToString(), Is.EqualTo(second.ToString()));
            Assert.That(first.Blueprint.Address, Is.SameAs(address));
            Assert.That(first.Attempts.Count, Is.InRange(1, 10));
            Assert.Throws<ArgumentException>(() => BoardGenerationPipeline.GenerateOrdinary(Request()));
        }

        /// <summary>A structurally valid but blocked exit is independently rejected before the next authored template is selected.</summary>
        [Test]
        public void CorruptedTopologyIsRejectedAndNextFallbackIsValidated()
        {
            BoardGenerationRequest request = Request();
            BoardBlueprint good = Good(request);
            var entities = new List<BoardEntityState>(good.Entities)
            {
                new BoardEntityState(new EntityId(999), new BoardEntityDefinition(BoardLayer.Obstacle,
                    EntityKind.Obstacle, "legacy.wall"), good.Exits[0])
            };
            var broken = new BoardBlueprint(request.Address, request.Configuration, entities, good.Items);
            BoardGenerationResult result = BoardGenerationPipeline.Generate(request, (_, __) => null,
                new[] { new BoardGenerationFallback("blocked-exit", _ => broken), new BoardGenerationFallback("good", _ => good) }, 1);
            Assert.That(result.FallbackId, Is.EqualTo("good"));
            Assert.That(result.Attempts.Count, Is.EqualTo(3));
            Assert.That(result.Attempts[1].Report.IsValid, Is.False);
            Assert.That(result.Attempts[1].ToString(), Does.Contain("UnreachableExit"));
            BoardGenerationResult exhausted = BoardGenerationPipeline.Generate(request, (_, __) => null,
                new[] { new BoardGenerationFallback("blocked-exit", _ => broken) }, 1);
            Assert.That(exhausted.IsSuccess, Is.False);
        }

        /// <summary>The maximum library also terminates, and duplicate/null/oversized registrations fail before candidate generation.</summary>
        [Test]
        [Timeout(5000)]
        public void LibraryLimitAndRegistrationsAreEnforced()
        {
            var library = new List<BoardGenerationFallback>();
            for (int index = 0; index < 8; index++) library.Add(new BoardGenerationFallback("broken-" + index, _ => null));
            int calls = 0;
            BoardGenerationResult result = BoardGenerationPipeline.Generate(Request(), (_, __) => { calls++; return null; }, library);
            Assert.That(calls, Is.EqualTo(8));
            Assert.That(result.Attempts.Count, Is.EqualTo(16));
            Assert.That(result.IsSuccess, Is.False);
            Assert.Throws<ArgumentException>(() => BoardGenerationPipeline.Generate(Request(), (_, __) => null,
                new[] { library[0], library[0] }));
            Assert.Throws<ArgumentException>(() => BoardGenerationPipeline.Generate(Request(), (_, __) => null,
                new BoardGenerationFallback[] { null }));
            library.Add(new BoardGenerationFallback("ninth", _ => null));
            Assert.Throws<ArgumentException>(() => BoardGenerationPipeline.Generate(Request(), (_, __) => { calls++; return null; }, library));
            Assert.That(calls, Is.EqualTo(8));
            Assert.Throws<ArgumentNullException>(() => BoardGenerationPipeline.Generate(null, (_, __) => null, Array.Empty<BoardGenerationFallback>()));
            Assert.Throws<ArgumentNullException>(() => BoardGenerationPipeline.Generate(Request(), null, Array.Empty<BoardGenerationFallback>()));
            Assert.Throws<ArgumentNullException>(() => BoardGenerationPipeline.Generate(Request(), (_, __) => null, null));
            Assert.Throws<NotSupportedException>(() => ((IList<BoardGenerationAttempt>)result.Attempts).Clear());
        }

        /// <summary>Expected argument rejection becomes diagnostics, while implementation exceptions are never hidden as fallback success.</summary>
        [Test]
        public void ExpectedRejectionIsReportedButUnexpectedExceptionsPropagate()
        {
            BoardGenerationResult result = BoardGenerationPipeline.Generate(Request(), (_, __) => throw new ArgumentException("missing content"),
                OrdinaryBoardFallbacks.CreateLibrary(), 1);
            Assert.That(result.UsedFallback, Is.True);
            Assert.That(result.Attempts[0].Report.Errors[0].Message, Does.Contain("missing content"));
            Assert.Throws<InvalidOperationException>(() => BoardGenerationPipeline.Generate(Request(),
                (_, __) => throw new InvalidOperationException("implementation failure"), OrdinaryBoardFallbacks.CreateLibrary()));
        }

        private static BoardGenerationRequest Request(string segment = "segment")
        {
            var address = new BoardAddress("run", "leg", "road", segment, 1, OrdinaryBoardGenerator.ConfigurationId, 1, 42);
            return new BoardGenerationRequest(address, OrdinaryBoardContent.CreateConfiguration(address),
                OrdinaryBoardContent.CreateCatalog(), 77);
        }

        private static BoardBlueprint Good(BoardGenerationRequest request) => OrdinaryBoardGenerator.Generate(
            request.Address, request.Configuration, request.Catalog, request.RunSeed);
    }
}