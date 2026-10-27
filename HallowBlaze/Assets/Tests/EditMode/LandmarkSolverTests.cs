using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using Application = UnityEngine.Application;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Checks bounded offline solving against independent real-controller execution.</summary>
    public sealed class LandmarkSolverTests
    {
        /// <summary>A tiny real template produces a shortest accepted path without changing its caller.</summary>
        [Test]
        public void TinyPuzzleFindsAndExecutesShortestPath()
        {
            LandmarkBoard landmark = Tiny();
            BoardState board = landmark.CreateState();
            RunState run = Run();
            var solver = new LandmarkSolver(landmark, board, run, new EntityId(1), Array.Empty<ShamblerState>());
            LandmarkSolverResult result = solver.Solve("arrival");
            Assert.That(result.Status, Is.EqualTo(LandmarkSolverStatus.Solved), result.ToString());
            Assert.That(result.SolutionPath.Count, Is.EqualTo(2));
            Assert.That(run.Food, Is.EqualTo(4));
            var controller = new TurnController(board, run, new EntityId(1), new TurnResolver());
            TurnResult turn = null;
            foreach (LandmarkSolverStep step in result.SolutionPath)
            {
                turn = controller.Resolve(step.CreateCommand(board, run, new EntityId(1)));
                Assert.That(turn.Accepted, Is.True);
            }
            Assert.That(turn.Events.OfType<ExitReachedEvent>().Single().ExitId, Is.EqualTo(new EntityId(12)));
            Assert.That(run.Food, Is.EqualTo(2));
        }

        /// <summary>Independent equal snapshots share a hash, and forced collisions do not merge changed resources.</summary>
        [Test]
        public void KeysDeduplicateByStructureNotHash()
        {
            BoardState board = Tiny().CreateState();
            var first = new LandmarkSolverStateKey(board, Run(), new EntityId(1), Array.Empty<ShamblerState>());
            var second = new LandmarkSolverStateKey(board.Clone(), Run(), new EntityId(1), Array.Empty<ShamblerState>());
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            var changed = new LandmarkSolverStateKey(board, Run(3), new EntityId(1), Array.Empty<ShamblerState>());
            var keys = new HashSet<LandmarkSolverStateKey>(new CollisionComparer()) { first, second, changed };
            Assert.That(keys.Count, Is.EqualTo(2));
        }

        /// <summary>An authored blocked variant exhausts its finite resources, rather than passing because a budget ran out.</summary>
        [Test]
        public void BlockedVariantExhaustsAndCarriesReproductionContext()
        {
            LandmarkBoard landmark = Tiny(true);
            var solver = new LandmarkSolver(landmark, landmark.CreateState(), Run(), new EntityId(1), Array.Empty<ShamblerState>());
            LandmarkSolverResult result = solver.Solve("arrival");
            Assert.That(result.Status, Is.EqualTo(LandmarkSolverStatus.NoSolution), result.ToString());
            Assert.That(result.VisitedStates, Is.EqualTo(result.ExpandedStates));
            Assert.That(result.SolutionPath, Is.Empty);
            Assert.That(result.TemplateId, Is.EqualTo("landmark.solver-test"));
            Assert.That(result.VariantId, Is.EqualTo("blocked"));
            Assert.That(result.Address.BoardSeed, Is.EqualTo(64));
        }

        /// <summary>Each budget has a distinct incomplete result, with exact caps and no accidental no-solution claim.</summary>
        [TestCase(1, 32, 10000, LandmarkSolverStatus.StateLimit)]
        [TestCase(50000, 0, 10000, LandmarkSolverStatus.DepthLimit)]
        [TestCase(50000, 32, 0, LandmarkSolverStatus.TimeLimit)]
        public void LimitsAreHonest(int states, int depth, int milliseconds, LandmarkSolverStatus status)
        {
            LandmarkBoard landmark = Tiny();
            var solver = new LandmarkSolver(landmark, landmark.CreateState(), Run(), new EntityId(1), Array.Empty<ShamblerState>());
            LandmarkSolverResult result = solver.Solve("arrival", states, depth, milliseconds);
            Assert.That(result.Status, Is.EqualTo(status));
            Assert.That(result.VisitedStates, Is.LessThanOrEqualTo(states));
            Assert.That(result.SolutionPath, Is.Empty);
        }

        /// <summary>Both real published outcomes are discovered dynamically, repeat deterministically, and execute on fresh owners.</summary>
        [TestCase("tower.arrival", 7)]
        [TestCase("tower.supplies", 11)]
        public void RadioTowerSolvesAndExecutesEveryOutcome(string outcomeId, int expectedLength)
        {
            var source = new Tower();
            var before = new LandmarkSolverStateKey(source.Board, source.Run, new EntityId(1), source.Enemies);
            var intent = source.Enemies[0].LockedIntent;
            var solver = source.Solver();
            LandmarkSolverResult result = solver.Solve(outcomeId);
            Assert.That(result.Status, Is.EqualTo(LandmarkSolverStatus.Solved), result.ToString());
            Assert.That(result.VisitedStates, Is.InRange(1, 50000));
            Assert.That(result.ElapsedMilliseconds, Is.LessThan(10000));
            Assert.That(result.SolutionPath.Count, Is.EqualTo(expectedLength));
            LandmarkSolverResult repeated = solver.Solve(outcomeId);
            Assert.That(repeated.Status, Is.EqualTo(result.Status));
            Assert.That(repeated.VisitedStates, Is.EqualTo(result.VisitedStates));
            Assert.That(repeated.ExpandedStates, Is.EqualTo(result.ExpandedStates));
            Assert.That(repeated.SolutionPath.Select(step => step.ToString()), Is.EqualTo(result.SolutionPath.Select(step => step.ToString())));
            Assert.That(new LandmarkSolverStateKey(source.Board, source.Run, new EntityId(1), source.Enemies), Is.EqualTo(before));
            Assert.That(source.Enemies[0].LockedIntent, Is.SameAs(intent));
            var fresh = new Tower();
            List<GameEvent> events = fresh.Execute(result);
            Assert.That(events.OfType<ExitReachedEvent>().Single().ExitId,
                Is.EqualTo(source.Landmark.Outcomes.Single(outcome => outcome.Id == outcomeId).ExitEntityId));
            Assert.That(fresh.Controller.IsTerminal, Is.True);
            Assert.That(fresh.Run.Health, Is.GreaterThan(0));
            Assert.That(fresh.Run.Food, Is.EqualTo(outcomeId == "tower.arrival" ? 93 : 109));
            if (outcomeId == "tower.supplies")
            {
                Assert.That(events.OfType<BoulderPushedEvent>().Count(), Is.EqualTo(1));
                Assert.That(events.OfType<ToolChargeSpentEvent>().Count(), Is.EqualTo(1));
                Assert.That(fresh.Run.ToolSlots[0].RemainingUses, Is.EqualTo(2));
                Assert.That(fresh.Board.TryGetEntity(new EntityId(21), out _), Is.False);
            }
            TestContext.WriteLine(result + " path=" + string.Join("; ", result.SolutionPath));
        }

        /// <summary>The actual optional supply outcome remains solvable by manual work with no ground or equipped axe.</summary>
        [Test]
        public void RadioTowerWithoutAxeUsesManualAlternative()
        {
            var source = new Tower();
            Assert.That(source.Board.TryRemove(new EntityId(20)), Is.True);
            LandmarkSolverResult result = source.Solver().Solve("tower.supplies");
            Assert.That(result.Status, Is.EqualTo(LandmarkSolverStatus.Solved), result.ToString());
            Assert.That(result.SolutionPath.Count, Is.EqualTo(12));
            var fresh = new Tower();
            Assert.That(fresh.Board.TryRemove(new EntityId(20)), Is.True);
            List<GameEvent> events = fresh.Execute(result);
            Assert.That(events.OfType<ExitReachedEvent>().Single().ExitId, Is.EqualTo(new EntityId(146)));
            Assert.That(events.OfType<ToolUsedEvent>(), Is.Empty);
            Assert.That(fresh.Run.Food, Is.EqualTo(108));
            TestContext.WriteLine(result + " path=" + string.Join("; ", result.SolutionPath));
        }

        /// <summary>Every curated variant and both tool-availability states solve each outcome, then execute on fresh production-rule owners.</summary>
        [TestCase("base", false)]
        [TestCase("base", true)]
        [TestCase("east-guard", false)]
        [TestCase("east-guard", true)]
        [TestCase("north-push", false)]
        [TestCase("north-push", true)]
        public void CuratedVariantsSolveAndExecuteBothOutcomes(string variantId, bool withoutAxe)
        {
            foreach (string outcomeId in new[] { "tower.arrival", "tower.supplies" })
            {
                var source = new Tower(variantId);
                Assert.That(source.Landmark.Validate(RadioTowerLandmark.CreateCatalog()).IsValid, Is.True, variantId);
                if (withoutAxe) Assert.That(source.Board.TryRemove(new EntityId(20)), Is.True);
                LandmarkSolverResult result = source.Solver().Solve(outcomeId);
                Assert.That(result.Status, Is.EqualTo(LandmarkSolverStatus.Solved), result.ToString());
                Assert.That(result.VisitedStates, Is.InRange(1, 50000));
                Assert.That(result.ElapsedMilliseconds, Is.LessThan(10000));
                Assert.That(result.SolutionPath.Count, Is.InRange(1, 32));
                var fresh = new Tower(variantId);
                if (withoutAxe) Assert.That(fresh.Board.TryRemove(new EntityId(20)), Is.True);
                List<GameEvent> events = fresh.Execute(result);
                Assert.That(events.OfType<ExitReachedEvent>().Single().ExitId,
                    Is.EqualTo(source.Landmark.Outcomes.Single(outcome => outcome.Id == outcomeId).ExitEntityId));
                Assert.That(fresh.Run.Health, Is.GreaterThan(0), result.ToString());
                Assert.That(fresh.Controller.IsTerminal, Is.True);
                int reward = outcomeId == "tower.supplies" ? 20 : 0;
                Assert.That(fresh.Run.Food, Is.EqualTo(100 + reward - result.SolutionPath.Count));
                if (outcomeId == "tower.arrival")
                {
                    Assert.That(events.OfType<BoulderPushedEvent>(), Is.Empty);
                    Assert.That(events.OfType<ToolUsedEvent>(), Is.Empty);
                    Assert.That(fresh.Run.ToolSlots.All(slot => slot.IsEmpty), Is.True);
                    Assert.That(fresh.Board.TryGetEntity(new EntityId(21), out _), Is.True);
                }
                else
                {
                    Assert.That(events.OfType<BoulderPushedEvent>(), Is.Not.Empty);
                    Assert.That(events.OfType<GateChangedEvent>().Any(change => change.Gate.IsOpen), Is.True);
                    Assert.That(fresh.Board.TryGetEntity(new EntityId(21), out _), Is.False);
                    Assert.That(fresh.Board.TryGetEntity(new EntityId(11), out _), Is.False);
                    if (withoutAxe) Assert.That(events.OfType<ToolUsedEvent>(), Is.Empty);
                    else Assert.That(events.OfType<ToolChargeSpentEvent>().Count(), Is.EqualTo(1));
                }
                TestContext.WriteLine(result + " noAxe=" + withoutAxe + " health=" + fresh.Run.Health +
                    " food=" + fresh.Run.Food + " path=" + string.Join("; ", result.SolutionPath));
            }
        }

        /// <summary>All reachable geometric player/boulder/work states retain a tool-free mandatory exit, independently of survival resources.</summary>
        [TestCase("base")]
        [TestCase("east-guard")]
        [TestCase("north-push")]
        public void CuratedBoulderOccupancyCannotSealMandatoryExit(string variantId)
        {
            var scenario = new Tower(variantId);
            BoardState initial = scenario.Board.Clone();
            foreach (BoardEntityState entity in initial.GetEntities())
                if (entity.Definition.Kind.Equals(EntityKind.Enemy) || entity.Definition.Layer == BoardLayer.Item)
                    Assert.That(initial.TryRemove(entity.Id), Is.True);
            var interactions = new InteractionQuery(new IInteractionAction[]
                { new BoulderPushAction(), new ClearObstacleAction(AxeContent.LoadRules(), false) });
            var states = new Dictionary<(GridPosition Player, GridPosition Boulder, int Work), BoardState>();
            var predecessors = new Dictionary<(GridPosition Player, GridPosition Boulder, int Work),
                HashSet<(GridPosition Player, GridPosition Boulder, int Work)>>();
            var winning = new HashSet<(GridPosition Player, GridPosition Boulder, int Work)>();
            var frontier = new Queue<(GridPosition Player, GridPosition Boulder, int Work)>();
            (GridPosition Player, GridPosition Boulder, int Work) Key(BoardState board)
            {
                board.TryGetEntity(new EntityId(1), out BoardEntityState player);
                board.TryGetEntity(new EntityId(10), out BoardEntityState boulder);
                return (player.Position, boulder.Position, board.TryGetEntity(new EntityId(11), out _) ? board.GetObstacleWork(new EntityId(11)) : -1);
            }
            var root = Key(initial);
            states.Add(root, initial);
            predecessors.Add(root, new HashSet<(GridPosition, GridPosition, int)>());
            frontier.Enqueue(root);
            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                BoardState board = states[current];
                var run = new RunState("geometry-only", 64, new RunStateConfiguration(100, 100, 0, "start"));
                var commands = new List<PlayerCommand>(new[] { Direction.North, Direction.East, Direction.West, Direction.South }
                    .Select(direction => new MoveCommand(direction)));
                commands.AddRange(interactions.GetOptions(board, run, new EntityId(1)).Where(option => option.IsAvailable).Select(option => option.CreateCommand()));
                foreach (PlayerCommand command in commands)
                {
                    BoardState next = board.Clone();
                    var edgeRun = new RunState("geometry-only", 64, new RunStateConfiguration(100, 100, 0, "start"));
                    var controller = new TurnController(next, edgeRun, new EntityId(1), new TurnResolver(interactions: interactions));
                    TurnResult result = controller.Resolve(command);
                    if (!result.Accepted) continue;
                    if (controller.IsTerminal)
                    {
                        if (result.Events.OfType<ExitReachedEvent>().Any(exit => exit.ExitId.Equals(new EntityId(151)))) winning.Add(current);
                        continue;
                    }
                    var nextKey = Key(next);
                    if (!states.ContainsKey(nextKey))
                    {
                        Assert.That(states.Count, Is.LessThan(16384), variantId + " geometry budget");
                        states.Add(nextKey, next);
                        predecessors.Add(nextKey, new HashSet<(GridPosition, GridPosition, int)>());
                        frontier.Enqueue(nextKey);
                    }
                    predecessors[nextKey].Add(current);
                }
            }
            foreach (var state in winning) frontier.Enqueue(state);
            while (frontier.Count > 0)
                foreach (var previous in predecessors[frontier.Dequeue()])
                    if (winning.Add(previous)) frontier.Enqueue(previous);
            var blocked = states.Keys.Where(state => !winning.Contains(state)).ToArray();
            Assert.That(blocked, Is.Empty, variantId + " tool-free geometry softlock: " + string.Join("; ", blocked.Take(5)));
            TestContext.WriteLine(variantId + " geometricStates=" + states.Count + " mandatoryReachable=" + winning.Count);
        }

        /// <summary>The copy keeps an explicitly retained Wait and its cadence; initial replanning would make the two-step escape lethal.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CopiesRetainExactInitialPlansAndRestCadence(bool resting)
        {
            LandmarkBoard landmark = Tiny();
            BoardState board = landmark.CreateState();
            var definition = new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "test.shambler");
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(30), new BoardEntityDefinition(BoardLayer.Terrain,
                new EntityKind("terrain"), "floor", new BoardEntityTraits(true, false, false)), new GridPosition(1, 1))), Is.True);
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(2), definition, new GridPosition(1, 1))), Is.True);
            var enemy = new ShamblerState(new EntityId(2), new ShamblerDefinition(10));
            if (resting) { enemy.LockIntent(new EnemyIntent(enemy.ActorId, EnemyIntentKind.Wait)); enemy.ConsumeIntent(); }
            var retained = new EnemyIntent(enemy.ActorId, EnemyIntentKind.Wait);
            enemy.LockIntent(retained);
            var catalog = new BoardValidationCatalog(new[] { definition }, Array.Empty<KeyValuePair<string, string>>(),
                Array.Empty<KeyValuePair<string, string>>(), new[] { new KeyValuePair<string, ShamblerDefinition>(definition.ContentId, enemy.Definition) });
            RunState run = Run();
            run.TakeDamage(90);
            var solver = new LandmarkSolver(landmark, board, run, new EntityId(1), new[] { enemy }, catalog);
            LandmarkSolverResult result = solver.Solve("arrival");
            Assert.That(result.Status, Is.EqualTo(LandmarkSolverStatus.Solved), result.ToString());
            Assert.That(result.SolutionPath.Count, Is.EqualTo(2));
            Assert.That(enemy.Phase, Is.EqualTo(resting ? ShamblerPhase.Rest : ShamblerPhase.Active));
            Assert.That(enemy.LockedIntent, Is.SameAs(retained));
            var controller = new TurnController(board, run, new EntityId(1), new TurnResolver(), new EnemyBatchTurnPhases(new[] { enemy }).Handlers);
            foreach (LandmarkSolverStep step in result.SolutionPath)
                Assert.That(controller.Resolve(step.CreateCommand(board, run, new EntityId(1))).Accepted, Is.True);
            Assert.That(controller.IsTerminal, Is.True);
            Assert.That(run.Health, Is.EqualTo(10));
        }

        /// <summary>Every mutable causal owner changes structural equality; the captured key itself stays immutable.</summary>
        [TestCase("health")]
        [TestCase("food")]
        [TestCase("allocator")]
        [TestCase("equipment")]
        [TestCase("charge")]
        [TestCase("ground")]
        [TestCase("work")]
        [TestCase("player")]
        [TestCase("enemy")]
        [TestCase("phase")]
        [TestCase("intent")]
        [TestCase("gate")]
        public void KeyTracksCausalStateAndIsDetached(string field)
        {
            var source = new Tower();
            var equivalent = new Tower();
            if (field == "charge")
            {
                source.Run.EquipTool(0, source.Run.CreateToolInstance(ToolContentCatalog.Load()["axe.basic"]));
                equivalent.Run.EquipTool(0, equivalent.Run.CreateToolInstance(ToolContentCatalog.Load()["axe.basic"]));
            }
            var before = new LandmarkSolverStateKey(source.Board, source.Run, new EntityId(1), source.Enemies);
            int beforeHash = before.GetHashCode();
            var equal = new LandmarkSolverStateKey(equivalent.Board, equivalent.Run, new EntityId(1), equivalent.Enemies);
            Assert.That(before, Is.EqualTo(equal));
            if (field == "health") source.Run.TakeDamage(10);
            else if (field == "food") source.Run.ConsumeFood(1);
            else if (field == "allocator") source.Run.CreateToolInstance(ToolContentCatalog.Load()["axe.basic"]);
            else if (field == "equipment")
            {
                source.Run.EquipTool(1, source.Run.CreateToolInstance(ToolContentCatalog.Load()["axe.basic"]));
            }
            else if (field == "charge") Assert.That(source.Run.TrySpendToolCharge(0, "tool.2"), Is.True);
            else if (field == "ground") Assert.That(source.Board.TryRemove(new EntityId(20)), Is.True);
            else if (field == "work") Assert.That(source.Board.TryWorkObstacle(new EntityId(11), 1, 3, out _), Is.True);
            else if (field == "player") Assert.That(source.Board.TryMove(new EntityId(1), new GridPosition(1, 2)), Is.True);
            else if (field == "enemy") Assert.That(source.Board.TryMove(new EntityId(2), new GridPosition(6, 4)), Is.True);
            else if (field == "phase") source.Enemies[0].ConsumeIntent();
            else if (field == "intent")
            {
                source.Enemies[0] = new ShamblerState(new EntityId(2), new ShamblerDefinition(10));
                source.Enemies[0].LockIntent(new EnemyIntent(new EntityId(2), EnemyIntentKind.Attack,
                    new GridPosition(1, 1), new EntityId(1)));
            }
            else Assert.That(source.Board.TryMove(new EntityId(10), new GridPosition(3, 3)), Is.True);
            var after = new LandmarkSolverStateKey(source.Board, source.Run, new EntityId(1), source.Enemies);
            Assert.That(before, Is.Not.EqualTo(after), field);
            Assert.That(before, Is.EqualTo(equal), field + " mutated a captured key");
            Assert.That(before.GetHashCode(), Is.EqualTo(beforeHash));
        }

        /// <summary>Input mutation after construction cannot change the solver's captured search root.</summary>
        [Test]
        public void SolverCapturesCallerStateBeforeLaterMutations()
        {
            var source = new Tower();
            var solver = source.Solver();
            source.Run.ConsumeFood(99);
            source.Run.TakeDamage(99);
            source.Enemies[0].ConsumeIntent();
            Assert.That(source.Board.TryRemove(new EntityId(20)), Is.True);
            LandmarkSolverResult result = solver.Solve("tower.supplies");
            Assert.That(result.Status, Is.EqualTo(LandmarkSolverStatus.Solved), result.ToString());
            Assert.That(result.SolutionPath.Count, Is.EqualTo(11));
            Assert.That(source.Run.Food, Is.EqualTo(1));
            Assert.That(source.Run.Health, Is.EqualTo(1));
        }

        /// <summary>Starvation on the desired exit is not a solution; another exit cannot be traversed after terminal admission.</summary>
        [Test]
        public void StarvationAndOtherExitsDoNotSolveSelectedOutcome()
        {
            LandmarkBoard landmark = Tiny();
            var solver = new LandmarkSolver(landmark, landmark.CreateState(), Run(2), new EntityId(1), Array.Empty<ShamblerState>());
            Assert.That(solver.Solve("arrival").Status, Is.EqualTo(LandmarkSolverStatus.NoSolution));
            BoardState board = landmark.CreateState();
            Assert.That(board.TryRemove(new EntityId(11)), Is.True);
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(31), new BoardEntityDefinition(BoardLayer.Terrain,
                new EntityKind("terrain"), "floor", new BoardEntityTraits(true, false, false)), new GridPosition(1, 1))), Is.True);
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(32), new BoardEntityDefinition(BoardLayer.Terrain,
                new EntityKind("terrain"), "floor", new BoardEntityTraits(true, false, false)), new GridPosition(2, 1))), Is.True);
            solver = new LandmarkSolver(landmark, board, Run(8), new EntityId(1), Array.Empty<ShamblerState>());
            Assert.That(solver.Solve("arrival").Status, Is.EqualTo(LandmarkSolverStatus.NoSolution));
        }

        /// <summary>Missing concrete tools, wider mechanics, invalid identity and invalid budgets fail explicitly before search.</summary>
        [Test]
        public void UnsupportedAndInvalidInputsFailClosed()
        {
            var source = new Tower();
            Assert.Throws<NotSupportedException>(() => new LandmarkSolver(source.Landmark, source.Board, source.Run,
                new EntityId(1), source.Enemies, clearingRules: AxeContent.LoadRules()));
            source.Run.EquipTool(0, source.Run.CreateToolInstance(ToolContentCatalog.Load()["shovel.basic"]));
            Assert.Throws<NotSupportedException>(() => source.Solver());
            LandmarkBoard landmark = Tiny();
            var solver = new LandmarkSolver(landmark, landmark.CreateState(), Run(), new EntityId(1), Array.Empty<ShamblerState>());
            Assert.Throws<ArgumentException>(() => solver.Solve("missing"));
            Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve("arrival", maxVisitedStates: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve("arrival", maxPathDepth: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve("arrival", maxElapsedMilliseconds: -1));
            BoardState terminal = landmark.CreateState();
            Assert.That(terminal.TryMove(new EntityId(1), new GridPosition(2, 0)), Is.True);
            Assert.Throws<ArgumentException>(() => new LandmarkSolver(landmark, terminal, Run(), new EntityId(1), Array.Empty<ShamblerState>()));
        }

        private sealed class Tower
        {
            internal readonly RunState Run = new RunState("solver-tower", 64, new RunStateConfiguration(100, 100, 4, "forest.ridge"));
            internal readonly LandmarkBoard Landmark;
            internal readonly BoardState Board;
            internal readonly ShamblerState[] Enemies = { new ShamblerState(new EntityId(2), new ShamblerDefinition(10)) };
            internal readonly TurnController Controller;
            private readonly ObstacleClearingRules clearing = AxeContent.LoadRules();

            internal Tower(string variantId = "base")
            {
                LandmarkTemplate template = LandmarkTemplateJson.Deserialize(File.ReadAllText(Path.Combine(Application.dataPath,
                    "GameData/Landmarks/radio-tower-template.json")), RadioTowerLandmark.CreateCatalog());
                Landmark = template.CreateBoard(new BoardAddress(Run.RunId, "leg.1", "road.ridge-radio-tower",
                    "segment.ridge-radio-tower.arrival", 5, template.Config.Id, 1, 64), variantId);
                Board = Landmark.CreateState();
                ToolInstanceState axe = Run.CreateToolInstance(ToolContentCatalog.Load()["axe.basic"]);
                Assert.That(Board.TryBindGroundTool(new GroundToolState(new EntityId(20), axe.InstanceId, axe.DefinitionId,
                    axe.Charges, RadioTowerLandmark.AxeSourceId)), Is.True);
                var phases = new MixedEnemyTurnPhases(Enemies, Array.Empty<ListenerState>());
                phases.PlanInitialIntents(Board, new EntityId(1));
                ShovelRules digging = ShovelContent.LoadRules();
                Controller = new TurnController(Board, Run, new EntityId(1), new TurnResolver(ActionNoiseProfile.Prototype,
                    ShovelContent.CreateInteractions(clearing, digging), ShovelContent.CreateTools(clearing, digging),
                    new ToolPickupQuery()), phases.Handlers);
            }

            internal LandmarkSolver Solver() => new LandmarkSolver(Landmark, Board, Run, new EntityId(1), Enemies,
                RadioTowerLandmark.CreateCatalog(), clearing);

            internal List<GameEvent> Execute(LandmarkSolverResult result)
            {
                var events = new List<GameEvent>();
                foreach (LandmarkSolverStep step in result.SolutionPath)
                {
                    TurnResult turn = Controller.Resolve(step.CreateCommand(Board, Run, new EntityId(1)));
                    Assert.That(turn.Accepted, Is.True, result + " step=" + step);
                    events.AddRange(turn.Events);
                }
                return events;
            }
        }

        private sealed class CollisionComparer : IEqualityComparer<LandmarkSolverStateKey>
        {
            public bool Equals(LandmarkSolverStateKey left, LandmarkSolverStateKey right) => left.Equals(right);
            public int GetHashCode(LandmarkSolverStateKey key) => 0;
        }

        private static RunState Run(int food = 4) => new RunState("solver-test", 64, new RunStateConfiguration(100, food, 0, "start"));

        private static LandmarkBoard Tiny(bool blocked = false)
        {
            var entities = new List<BoardEntityState>();
            for (int horizontal = 0; horizontal < 3; horizontal++)
                entities.Add(new BoardEntityState(new EntityId(10 + horizontal), new BoardEntityDefinition(BoardLayer.Terrain,
                    new EntityKind("floor"), horizontal == 2 ? "exit" : "floor", new BoardEntityTraits(!blocked || horizontal != 1, horizontal == 2, false)),
                    new GridPosition(horizontal, 0)));
            entities.Add(new BoardEntityState(new EntityId(20), new BoardEntityDefinition(BoardLayer.Terrain,
                new EntityKind("floor"), "exit", new BoardEntityTraits(true, true, false)), new GridPosition(0, 1)));
            entities.Add(new BoardEntityState(new EntityId(1), new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player"), new GridPosition(0, 0)));
            var config = new BoardGenerationConfig("solver.board", 1, new GenerationRange(1, 63), new GenerationRange(0, 64),
                new GenerationRange(0, 64), new GenerationRange(0, 0), 0, 0, new[] { "floor", "exit", "player" }, Array.Empty<ResourceGenerationBudget>());
            var template = new LandmarkTemplate("landmark.solver-test", 1, config,
                new[] { new LandmarkVariant(blocked ? "blocked" : "base", entities, Array.Empty<BoardItemSpawn>()) },
                new[] { new LandmarkOutcomeDefinition("arrival", "Arrival", new EntityId(12)),
                    new LandmarkOutcomeDefinition("alternate", "Alternate", new EntityId(20), true) });
            return template.CreateBoard(new BoardAddress("solver-test", "leg.1", "road.test", "segment.test", 1, config.Id, 1, 64), blocked ? "blocked" : "base");
        }
    }
}