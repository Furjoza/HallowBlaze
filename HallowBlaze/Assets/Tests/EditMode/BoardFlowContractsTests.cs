using System;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    public sealed class BoardFlowContractsTests
    {
        [Test]
        public void RequestMapsActiveRunAndWorldNode()
        {
            RunState run = CreateRun(currentDay: 0);
            WorldDefinition world = CreateWorld();

            BoardRequest request = BoardRequest.CreateFromRun(run, world);

            Assert.That(request.RunId, Is.EqualTo(run.RunId));
            Assert.That(request.RunSeed, Is.EqualTo(run.RunSeed));
            Assert.That(request.WorldNodeId, Is.EqualTo("forest.start"));
            Assert.That(request.CurrentDay, Is.Zero);
            Assert.That(request.BoardSeed, Is.EqualTo(run.GetBoardSeed()));
            Assert.That(request.PlaceKind, Is.EqualTo("shelter"));
            Assert.That(request.BiomeFamily, Is.EqualTo("temperate-forest"));
            Assert.That(request.LegacyDifficultyLevel, Is.EqualTo(1));
        }

        [Test]
        public void LegacyDifficultyClampsOnlyTheInitialDay()
        {
            BoardRequest dayZero = BoardRequest.CreateFromRun(CreateRun(currentDay: 0), CreateWorld());
            BoardRequest dayOne = BoardRequest.CreateFromRun(CreateRun(currentDay: 1), CreateWorld());
            BoardRequest dayFour = BoardRequest.CreateFromRun(CreateRun(currentDay: 4), CreateWorld());

            Assert.That(dayZero.LegacyDifficultyLevel, Is.EqualTo(1));
            Assert.That(dayOne.LegacyDifficultyLevel, Is.EqualTo(1));
            Assert.That(dayFour.LegacyDifficultyLevel, Is.EqualTo(4));
        }

        [Test]
        public void RequestIsReproducedAtTheSamePersistedBoundary()
        {
            RunState firstRun = CreateRun(runId: "run-stable", runSeed: 1729, currentDay: 2);
            RunState restoredRun = CreateRun(runId: "run-stable", runSeed: 1729, currentDay: 2);

            BoardRequest first = BoardRequest.CreateFromRun(firstRun, CreateWorld());
            BoardRequest restored = BoardRequest.CreateFromRun(restoredRun, CreateWorld());

            Assert.That(first.HasSameIdentity(restored), Is.True);
            Assert.That(restored.BoardSeed, Is.EqualTo(first.BoardSeed));
        }

        [Test]
        public void RequestIdentityChangesWithRunNodeDayOrBoardSeed()
        {
            BoardRequest baseline = CreateRequest();

            Assert.That(baseline.HasSameIdentity(CreateRequest(runId: "run-other")), Is.False);
            Assert.That(baseline.HasSameIdentity(CreateRequest(worldNodeId: "forest.goal")), Is.False);
            Assert.That(baseline.HasSameIdentity(CreateRequest(currentDay: 3)), Is.False);
            Assert.That(baseline.HasSameIdentity(CreateRequest(boardSeed: 99)), Is.False);
            Assert.That(baseline.HasSameIdentity(null), Is.False);
        }

        [Test]
        public void FactoryRejectsInactiveRunAndUnknownNode()
        {
            RunState inactiveRun = CreateRun();
            inactiveRun.MarkDead();
            RunState unknownNodeRun = CreateRun(worldNodeId: "forest.unknown");

            Assert.Throws<InvalidOperationException>(
                () => BoardRequest.CreateFromRun(inactiveRun, CreateWorld()));
            Assert.Throws<ArgumentException>(
                () => BoardRequest.CreateFromRun(unknownNodeRun, CreateWorld()));
        }

        [Test]
        public void RequestRejectsInvalidStableValuesAndRanges()
        {
            Assert.Throws<ArgumentException>(() => CreateRequest(runId: " run "));
            Assert.Throws<ArgumentException>(() => CreateRequest(worldNodeId: " "));
            Assert.Throws<ArgumentException>(() => CreateRequest(placeKind: " place"));
            Assert.Throws<ArgumentException>(() => CreateRequest(biomeFamily: "biome "));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateRequest(currentDay: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateRequest(legacyDifficultyLevel: 0));
        }

        [Test]
        public void OutcomesCarryTheCompleteBoardIdentityAndDeathReason()
        {
            BoardRequest request = CreateRequest();

            BoardOutcome exit = BoardOutcome.ExitReached(request);
            BoardOutcome starvation = BoardOutcome.PlayerDied(request, DeathReason.Starvation);
            BoardOutcome health = BoardOutcome.PlayerDied(request, DeathReason.HealthDepleted);

            Assert.That(exit.Type, Is.EqualTo(BoardOutcomeType.ExitReached));
            Assert.That(exit.Request.HasSameIdentity(request), Is.True);
            Assert.That(exit.DeathReason, Is.Null);
            Assert.That(starvation.Type, Is.EqualTo(BoardOutcomeType.PlayerDied));
            Assert.That(starvation.DeathReason, Is.EqualTo(DeathReason.Starvation));
            Assert.That(health.DeathReason, Is.EqualTo(DeathReason.HealthDepleted));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BoardOutcome.PlayerDied(request, (DeathReason)99));
        }

        private static RunState CreateRun(
            string runId = "run-001",
            int runSeed = 1729,
            int currentDay = 0,
            string worldNodeId = "forest.start")
        {
            return new RunState(
                runId,
                runSeed,
                new RunStateConfiguration(100, 100, currentDay, worldNodeId));
        }

        private static WorldDefinition CreateWorld()
        {
            return new WorldDefinition(
                "world-1",
                1,
                "forest.start",
                "forest.goal",
                new[]
                {
                    new WorldNodeDefinition(
                        "forest.start",
                        0,
                        0,
                        0,
                        "shelter",
                        "temperate-forest"),
                    new WorldNodeDefinition(
                        "forest.goal",
                        0,
                        1,
                        1,
                        "landmark",
                        "cold-forest")
                },
                new WorldEdgeDefinition[0]);
        }

        private static BoardRequest CreateRequest(
            string runId = "run-001",
            int runSeed = 1729,
            string worldNodeId = "forest.start",
            int currentDay = 2,
            int boardSeed = 42,
            string placeKind = "shelter",
            string biomeFamily = "temperate-forest",
            int legacyDifficultyLevel = 2)
        {
            return new BoardRequest(
                runId,
                runSeed,
                worldNodeId,
                currentDay,
                boardSeed,
                placeKind,
                biomeFamily,
                legacyDifficultyLevel);
        }
    }
}
