using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies explicit digging targets, concrete cache rewards and temporary Shambler trapping through normal turns.</summary>
    public sealed class ShovelTests
    {
        private static readonly EntityId PlayerId = new EntityId(1);
        private static readonly EntityId EnemyId = new EntityId(2);

        /// <summary>The announced move enters the pit, subsequent announcements wait, and only accepted commands age the trap.</summary>
        [Test]
        public void PitTrapsOnlyAfterActualEntryAndReleasesAfterPublishedLifetime()
        {
            BoardState board = CreateBoard();
            Add(board, 2, BoardLayer.Actor, EntityKind.Enemy, "shambler", 2);
            var run = CreateRun();
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));
            var phases = new MixedEnemyTurnPhases(new[] { enemy }, new ListenerState[0]);
            phases.PlanInitialIntents(board, PlayerId);
            Assert.That(enemy.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Move));
            Assert.That(board.TryCreatePit(new EntityId(11), 4), Is.True);
            var controller = new TurnController(board, run, PlayerId, phases: phases.Handlers);
            TurnResult result = controller.Resolve(new WaitCommand());
            Assert.That(result.Events.OfType<ShamblerTrappedEvent>().Single().ActorId, Is.EqualTo(EnemyId));
            Assert.That(board.IsTrappedShambler(EnemyId), Is.True);
            Assert.That(enemy.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Wait));
            Assert.That(board.GetPits().Single().RemainingTurns, Is.EqualTo(3));
            Assert.That(controller.Resolve(new MoveCommand(Direction.West)).Accepted, Is.False);
            Assert.That(board.GetPits().Single().RemainingTurns, Is.EqualTo(3));
            for (int turn = 0; turn < 3; turn++)
            {
                TurnResult waiting = controller.Resolve(new WaitCommand());
                Assert.That(waiting.Events.OfType<EnemyAttackResolvedEvent>(), Is.Empty);
                Assert.That(waiting.Events.OfType<EntityMovedEvent>(), Is.Empty);
            }
            Assert.That(board.GetPits(), Is.Empty);
            Assert.That(board.IsTrappedShambler(EnemyId), Is.False);
            Assert.That(run.Health, Is.EqualTo(100));
        }

        /// <summary>Both routes create one identifiable reward before reward transfer, charge and cost; the reward can prevent starvation.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CacheCreatesExactlyOneRewardAndResolvesItBeforeCost(bool shovel)
        {
            BoardState board = CreateBoard();
            Add(board, 3, BoardLayer.Item, EntityKind.Item, "marked.cache", 1);
            RunState run = CreateRun();
            run.ConsumeFood(shovel ? 19 : 17);
            ShovelRules rules = Rules();
            var manual = new InteractionQuery(new[] { new CacheDigAction(rules, false) });
            ToolUseQuery tools = Tools(rules);
            if (shovel) run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("shovel.basic", "Shovel", 3)));
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(interactions: manual, tools: tools));
            TurnResult last = null;
            for (int step = 0; step < (shovel ? 1 : 3); step++)
            {
                PlayerCommand command = shovel ? (PlayerCommand)tools.GetOptions(board, run, PlayerId).Single(option => option.ActionId == "unearth").CreateCommand() :
                    manual.GetOptions(board, run, PlayerId).Single().CreateCommand();
                last = controller.Resolve(command);
                Assert.That(last.Accepted, Is.True);
                Assert.That(last.Events.OfType<CacheWorkedEvent>().Single().Reward != null, Is.EqualTo(shovel || step == 2));
            }
            CacheRewardItem reward = last.Events.OfType<CacheWorkedEvent>().Single().Reward;
            Assert.That(reward.InstanceId, Is.EqualTo("cache.reward.3"));
            Assert.That(reward.DefinitionId, Is.EqualTo("cache.ration"));
            Assert.That(reward.SourceEntityId, Is.EqualTo(new EntityId(3)));
            Assert.That(last.Events.OfType<ItemCollectedEvent>().Count(), Is.EqualTo(1));
            Assert.That(last.Events.Select(gameEvent => gameEvent.EventType).ToList().IndexOf("FoodRestored"),
                Is.LessThan(last.Events.Select(gameEvent => gameEvent.EventType).ToList().IndexOf("ActionCostApplied")));
            Assert.That(run.Food, Is.EqualTo(10));
            Assert.That(board.HasRevealedCache(new EntityId(3)), Is.True);
            Assert.That(board.TryGetEntity(new EntityId(3), out _), Is.False);
            Assert.That(board.GetCacheWork(new EntityId(3)), Is.Zero);
            if (shovel) Assert.That(run.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
        }

        /// <summary>Without the transitional transfer the exact revealed instance remains cloneable ground state, not a raw resource change.</summary>
        [Test]
        public void RevealedRewardCanRemainOnGroundWithoutDuplicationOrResourceMutation()
        {
            BoardState board = CreateBoard();
            Add(board, 3, BoardLayer.Item, EntityKind.Item, "marked.cache", 1);
            RunState run = CreateRun();
            ShovelRules rules = Rules();
            var tools = new ToolUseQuery(new[] { new ToolAction("shovel.basic", new CacheDigAction(rules, true, false), "Unearth", 2, 2) });
            run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("shovel.basic", "Shovel", 3)));
            var resolver = new TurnResolver(tools: tools);
            UseToolCommand command = tools.GetOptions(board, run, PlayerId).Single().CreateCommand();
            TurnResult result = resolver.Resolve(board, run, PlayerId, command);
            CacheRewardItem reward = result.Events.OfType<CacheWorkedEvent>().Single().Reward;
            Assert.That(board.TryGetCacheReward(new EntityId(3), out CacheRewardItem ground), Is.True);
            Assert.That(ground, Is.SameAs(reward));
            Assert.That(run.Food, Is.EqualTo(19));
            BoardState clone = board.Clone();
            clone.TryRemove(ground.Entity.Id);
            Assert.That(board.TryGetCacheReward(new EntityId(3), out ground), Is.True);
            Assert.That(resolver.Resolve(board, run, PlayerId, command).Accepted, Is.False);
            Assert.That(board.TryWorkCache(new EntityId(3), 3, 3, rules.RewardDefinition, out _), Is.False);
            Assert.That(run.Food, Is.EqualTo(19));
        }

        /// <summary>The ordinary tool path creates a fresh full-lifetime pit, spends one charge and rejects repeated excavation for free.</summary>
        [Test]
        public void PitCommandSpendsOneChargeAndDoesNotAgeItsNewPit()
        {
            BoardState board = CreateBoard();
            RunState run = CreateRun();
            ToolUseQuery tools = Tools(Rules());
            run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("shovel.basic", "Shovel", 3)));
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(tools: tools));
            UseToolCommand command = tools.GetOptions(board, run, PlayerId).Single(option => option.TargetId.Equals(new EntityId(11))).CreateCommand();
            TurnResult result = controller.Resolve(command);
            Assert.That(result.Events.Select(gameEvent => gameEvent.EventType), Is.EqualTo(new[]
                { "ToolUsed", "PitCreated", "ToolChargeSpent", "ActionCostApplied", "Noise" }));
            Assert.That(board.GetPits().Single().RemainingTurns, Is.EqualTo(4));
            Assert.That(controller.Resolve(command).Accepted, Is.False);
            Assert.That(board.GetPits().Single().RemainingTurns, Is.EqualTo(4));
            Assert.That(run.ToolSlots[0].Instance.Charges, Is.EqualTo(2));
        }

        /// <summary>Every rejected target/equipment case preserves turn, food, charges, noise and existing pit lifetime.</summary>
        [TestCase("floor")]
        [TestCase("exit")]
        [TestCase("impassable")]
        [TestCase("item")]
        [TestCase("obstacle")]
        [TestCase("actor")]
        [TestCase("pit")]
        [TestCase("depleted")]
        [TestCase("stale")]
        [TestCase("missing")]
        [TestCase("displaced")]
        [TestCase("range")]
        [TestCase("empty")]
        public void InvalidDiggingIsFreeAndDoesNotAgeExistingPits(string invalid)
        {
            BoardState board = CreateBoard();
            RunState run = CreateRun();
            ToolUseQuery tools = Tools(Rules());
            run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("shovel.basic", "Shovel", 3)));
            ToolUseOption preview = tools.GetOptions(board, run, PlayerId).Single(option => option.TargetId.Equals(new EntityId(11)));
            UseToolCommand command = preview.CreateCommand();
            if (invalid == "floor" || invalid == "exit" || invalid == "impassable")
            {
                board.TryRemove(new EntityId(11));
                Assert.That(board.TryAdd(new BoardEntityState(new EntityId(11), new BoardEntityDefinition(BoardLayer.Terrain,
                    new EntityKind("terrain"), invalid == "floor" ? "ordinary.floor" : "diggable",
                    new BoardEntityTraits(invalid != "impassable", invalid == "exit", false)), new GridPosition(1, 0))), Is.True);
            }
            if (invalid == "item") Add(board, 3, BoardLayer.Item, EntityKind.Item, "occupied", 1);
            if (invalid == "obstacle") Add(board, 3, BoardLayer.Obstacle, new EntityKind("obstacle"), "occupied", 1);
            if (invalid == "actor") Add(board, 3, BoardLayer.Actor, EntityKind.Enemy, "occupied", 1);
            if (invalid == "pit") Assert.That(board.TryCreatePit(new EntityId(11), 4), Is.True);
            if (invalid == "depleted")
            {
                for (int charge = 0; charge < 3; charge++) run.TrySpendToolCharge(0, preview.InstanceId);
                command = new UseToolCommand(0, preview.InstanceId, 0, preview.TargetId, preview.TargetPosition, preview.ActionId);
            }
            if (invalid == "stale") run.TrySpendToolCharge(0, preview.InstanceId);
            if (invalid == "missing") board.TryRemove(preview.TargetId);
            if (invalid == "displaced") { board.TryRemove(new EntityId(13)); board.TryMove(preview.TargetId, new GridPosition(3, 0)); }
            if (invalid == "range") command = new UseToolCommand(0, preview.InstanceId, 3, new EntityId(13), new GridPosition(3, 0), "dig-pit");
            if (invalid == "empty") run.RemoveTool(0);
            if (invalid != "displaced" && invalid != "range") Assert.That(board.TryCreatePit(new EntityId(13), 4), Is.True);
            var controller = new TurnController(board, run, PlayerId, new TurnResolver(tools: tools));
            int charges = run.ToolSlots[0].Instance?.Charges ?? 0;
            TurnResult rejected = controller.Resolve(command);
            Assert.That(rejected.Accepted, Is.False);
            Assert.That(rejected.Events, Is.Empty);
            Assert.That(run.Food, Is.EqualTo(20));
            Assert.That(run.ToolSlots[0].Instance?.Charges ?? 0, Is.EqualTo(charges));
            Assert.That(board.GetActiveNoises(), Is.Empty);
            Assert.That(board.GetPits().All(pit => pit.RemainingTurns == 4), Is.True);
        }

        /// <summary>Completion overflow rejects before progress/reveal/charge mutation; partial work clones independently.</summary>
        [Test]
        public void CacheOverflowIsAtomicAndPartialWorkClonesIndependently()
        {
            BoardState board = CreateBoard();
            Add(board, 3, BoardLayer.Item, EntityKind.Item, "marked.cache", 1);
            RunState run = CreateRun();
            ShovelRules rules = Rules();
            var manual = new InteractionQuery(new[] { new CacheDigAction(rules, false) });
            ToolUseQuery tools = Tools(rules);
            run.EquipTool(0, run.CreateToolInstance(new ToolDefinition("shovel.basic", "Shovel", 3)));
            var resolver = new TurnResolver(interactions: manual, tools: tools);
            Assert.That(resolver.Resolve(board, run, PlayerId, manual.GetOptions(board, run, PlayerId).Single().CreateCommand()).Accepted, Is.True);
            BoardState clone = board.Clone();
            Assert.That(clone.TryWorkCache(new EntityId(3), 1, 3, rules.RewardDefinition, out _), Is.True);
            Assert.That(board.GetCacheWork(new EntityId(3)), Is.EqualTo(1));
            Assert.That(clone.GetCacheWork(new EntityId(3)), Is.EqualTo(2));
            run.RestoreFood(int.MaxValue - run.Food);
            UseToolCommand command = tools.GetOptions(board, run, PlayerId).Single(option => option.ActionId == "unearth").CreateCommand();
            Assert.That(resolver.Resolve(board, run, PlayerId, command).Accepted, Is.False);
            Assert.That(board.GetCacheWork(new EntityId(3)), Is.EqualTo(1));
            Assert.That(board.HasRevealedCache(new EntityId(3)), Is.False);
            Assert.That(run.Food, Is.EqualTo(int.MaxValue));
            Assert.That(run.ToolSlots[0].Instance.Charges, Is.EqualTo(3));
            board.TryRemove(new EntityId(3));
            Assert.That(board.GetCacheWork(new EntityId(3)), Is.Zero);
        }

        /// <summary>Neither digging nor ignoring a cache is required to reach the exit; a pit remains walkable even with no shovel equipped.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MandatoryExitRemainsReachableWithOrWithoutPit(bool pit)
        {
            BoardState board = CreateBoard();
            Add(board, 3, BoardLayer.Item, EntityKind.Item, "marked.cache", 2);
            if (pit) Assert.That(board.TryCreatePit(new EntityId(11), 4), Is.True);
            RunState run = CreateRun();
            var controller = new TurnController(board, run, PlayerId);
            TurnResult result = null;
            for (int step = 0; step < 4; step++) result = controller.Resolve(new MoveCommand(Direction.East));
            Assert.That(result.Events.OfType<ExitReachedEvent>().Count(), Is.EqualTo(1));
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(run.Food, Is.EqualTo(16));
            Assert.That(run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
            Assert.That(board.HasRevealedCache(new EntityId(3)), Is.False);
            Assert.That(board.IsTrappedShambler(PlayerId), Is.False);
        }

        /// <summary>A Listener enters the same pit normally; only the Shambler executor can emit the trap observation.</summary>
        [Test]
        public void ListenerIsNotTrappedByPit()
        {
            BoardState board = CreateBoard();
            Add(board, 2, BoardLayer.Actor, EntityKind.Enemy, "listener", 2);
            board.RegisterListener(EnemyId);
            board.EmitNoise(PlayerId, new GridPosition(0, 0), 4, 4);
            Assert.That(board.TryCreatePit(new EntityId(11), 4), Is.True);
            var listener = new ListenerState(EnemyId, new ListenerDefinition());
            var phases = new MixedEnemyTurnPhases(new ShamblerState[0], new[] { listener });
            phases.PlanInitialIntents(board, PlayerId);
            var controller = new TurnController(board, CreateRun(), PlayerId, phases: phases.Handlers);
            TurnResult result = controller.Resolve(new WaitCommand());
            Assert.That(result.Events.OfType<EntityMovedEvent>().Single().To, Is.EqualTo(new GridPosition(1, 0)));
            Assert.That(result.Events.OfType<ShamblerTrappedEvent>(), Is.Empty);
            Assert.That(board.IsTrappedShambler(EnemyId), Is.False);
            Assert.That(board.GetPits().Single().TrappedActorId, Is.Null);
        }

        /// <summary>A published move blocked at execution cannot trap an adjacent enemy or fabricate an observation.</summary>
        [Test]
        public void BlockedEntryDoesNotTrapShamblerAndCloneOwnsItsHold()
        {
            BoardState board = CreateBoard();
            Assert.That(board.TryCreatePit(new EntityId(11), 4), Is.True);
            Add(board, 2, BoardLayer.Actor, EntityKind.Enemy, "shambler", 2);
            var enemy = new ShamblerState(EnemyId, new ShamblerDefinition(10));
            var phases = new MixedEnemyTurnPhases(new[] { enemy }, new ListenerState[0]);
            phases.PlanInitialIntents(board, PlayerId);
            Add(board, 3, BoardLayer.Actor, EntityKind.Enemy, "blocking.actor", 1);
            var controller = new TurnController(board, CreateRun(), PlayerId, phases: phases.Handlers);
            TurnResult blocked = controller.Resolve(new WaitCommand());
            Assert.That(blocked.Events.OfType<ShamblerTrappedEvent>(), Is.Empty);
            Assert.That(board.GetPits().Single().TrappedActorId, Is.Null);
            board.TryRemove(new EntityId(3));
            board.TryMove(EnemyId, new GridPosition(1, 0));
            Assert.That(board.TryTrapShambler(EnemyId, out _), Is.True);
            BoardState clone = board.Clone();
            clone.TryRemove(EnemyId);
            Assert.That(clone.GetPits().Single().TrappedActorId, Is.Null);
            Assert.That(board.IsTrappedShambler(EnemyId), Is.True);
        }

        /// <summary>Invalid authored work, lifetime and emission values cannot publish partial or tool-only rules.</summary>
        [TestCase(1, 4, 2, 2)]
        [TestCase(3, 1, 2, 2)]
        [TestCase(3, 4, -1, 2)]
        [TestCase(3, 4, 2, 0)]
        public void InvalidRulesAreRejected(int work, int lifetime, int noise, int noiseLifetime)
            => Assert.Throws<System.ArgumentOutOfRangeException>(() => new ShovelRules("cache", "ground", work,
                Rules().RewardDefinition, lifetime, noise, noiseLifetime));

        /// <summary>The concrete reward must be a separate food item, never the source marker or a non-item resource shortcut.</summary>
        [Test]
        public void RewardDefinitionMustBeConcreteAndDistinct()
        {
            Assert.Throws<System.ArgumentException>(() => new ShovelRules("cache.ration", "ground", 3, Rules().RewardDefinition, 4, 2, 2));
            Assert.Throws<System.ArgumentException>(() => new ShovelRules("cache", "ground", 3,
                new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "empty.reward"), 4, 2, 2));
        }

        private static ShovelRules Rules() => new ShovelRules("marked.cache", "diggable", 3,
            new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "cache.ration", new BoardEntityTraits(false, false, true), 10), 4, 2, 2);

        private static ToolUseQuery Tools(ShovelRules rules) => new ToolUseQuery(new[]
        {
            new ToolAction("shovel.basic", new CacheDigAction(rules, true), "Unearth", 2, 2),
            new ToolAction("shovel.basic", new PitDigAction(rules), "Dig pit", 2, 2)
        });

        private static BoardState CreateBoard()
        {
            var board = new BoardState(new GridBounds(0, 0, 4, 0));
            for (int horizontal = 0; horizontal <= 4; horizontal++)
                Assert.That(board.TryAdd(new BoardEntityState(new EntityId(10 + horizontal),
                    new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), "diggable",
                        new BoardEntityTraits(true, horizontal == 4, false)), new GridPosition(horizontal, 0))), Is.True);
            Add(board, 1, BoardLayer.Actor, EntityKind.Player, "player", 0);
            return board;
        }

        private static void Add(BoardState board, long id, BoardLayer layer, EntityKind kind, string content, int horizontal)
            => Assert.That(board.TryAdd(new BoardEntityState(new EntityId(id), new BoardEntityDefinition(layer, kind, content),
                new GridPosition(horizontal, 0))), Is.True);

        private static RunState CreateRun() => new RunState("m55", 55, new RunStateConfiguration(100, 20, 0, "forest.start"));
    }
}