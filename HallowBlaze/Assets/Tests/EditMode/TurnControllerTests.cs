using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using NUnit.Framework;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Verifies complete turn ordering, terminal boundaries, and exclusive resolution.</summary>
    public sealed class TurnControllerTests
    {
        private static readonly EntityId PlayerId = new EntityId(1);
        private static readonly GridPosition Start = new GridPosition(1, 1);
        private static readonly GridPosition Destination = new GridPosition(2, 1);

        /// <summary>
        /// The batch adapter retains initial plans, executes only permitted old batches, and plans
        /// once after environment only for nonterminal accepted turns, including the subsequent rest.
        /// </summary>
        [TestCase("accepted")]
        [TestCase("rejected")]
        [TestCase("exit")]
        [TestCase("starvation")]
        [TestCase("enemy-death")]
        [TestCase("environment-death")]
        public void EnemyBatchPhases_RespectControllerBoundaries(string scenario)
        {
            bool enemyDeath = scenario == "enemy-death";
            bool earlyTerminal = scenario == "exit" || scenario == "starvation";
            bool lateTerminal = enemyDeath || scenario == "environment-death";
            GridPosition firstSource = enemyDeath ? Destination : new GridPosition(3, 1);
            GridPosition secondSource = enemyDeath ? new GridPosition(1, 2) : new GridPosition(3, 2);
            BoardState board = CreateShamblerBoard(firstSource, scenario == "exit");
            Add(board, 3, BoardLayer.Actor, EntityKind.Enemy, secondSource, BoardEntityTraits.Default);
            RunState run = CreateRun(scenario == "starvation" ? 1 : 5);
            if (!enemyDeath)
                run.RestoreHealth(90);
            var first = new ShamblerState(new EntityId(2), new ShamblerDefinition(20));
            var second = new ShamblerState(new EntityId(3), new ShamblerDefinition(10));
            ShamblerState[] enemies = { first, second };
            var input = new List<ShamblerState> { second, first };
            var trace = new List<string>();
            int executionCalls = 0;
            int environmentCalls = 0;
            int planningCalls = 0;
            string before = Snapshot(board, run);
            var adapter = new EnemyBatchTurnPhases(input, (boardState, runState, playerId, events) =>
            {
                trace.Add("Environment");
                environmentCalls++;
                Assert.That(enemies.All(enemy => enemy.LockedIntent == null), Is.True);
                Assert.That(enemies.All(enemy => enemy.Phase ==
                    (environmentCalls == 1 ? ShamblerPhase.Rest : ShamblerPhase.Active)), Is.True);
                if (scenario == "environment-death")
                    runState.TakeDamage(runState.Health);
                events.Add(new MarkerEvent("Environment"));
            });
            Assert.That(Snapshot(board, run), Is.EqualTo(before));
            Assert.That(enemies.All(enemy => enemy.LockedIntent == null && enemy.Phase == ShamblerPhase.Active), Is.True);
            CollectionAssert.AreEqual(new[] { second, first }, input);
            input.Clear();

            adapter.PlanInitialIntents(board, PlayerId);

            EnemyIntent[] initial = enemies.Select(enemy => enemy.LockedIntent).ToArray();
            Assert.That(Snapshot(board, run), Is.EqualTo(before));
            Assert.That(initial.Select(intent => intent.Kind), Is.EqualTo(new[] {
                enemyDeath ? EnemyIntentKind.Attack : EnemyIntentKind.Move,
                enemyDeath ? EnemyIntentKind.Attack : EnemyIntentKind.Move }));
            Assert.That(initial[0].TargetPosition, Is.EqualTo(enemyDeath ? Start : Destination));
            Assert.That(initial[1].TargetPosition, Is.EqualTo(enemyDeath ? Start : new GridPosition(2, 2)));
            Assert.That(initial.All(intent => intent.TargetId.Equals(PlayerId)), Is.True);
            Assert.That(enemies.All(enemy => enemy.Phase == ShamblerPhase.Active), Is.True);
            Assert.Throws<InvalidOperationException>(() => adapter.PlanInitialIntents(board, PlayerId));
            for (int index = 0; index < enemies.Length; index++)
                Assert.That(enemies[index].LockedIntent, Is.SameAs(initial[index]));
            EnemyIntent[] expectedLocked = initial;
            var phases = new TurnPhaseHandlers(
                (boardState, runState, playerId, events) =>
                {
                    trace.Add("ExecuteLockedIntents");
                    executionCalls++;
                    Assert.That(runState.Food, Is.EqualTo(5 - executionCalls));
                    for (int index = 0; index < enemies.Length; index++)
                        Assert.That(enemies[index].LockedIntent, Is.SameAs(expectedLocked[index]));
                    adapter.Handlers.ExecuteLockedIntents(boardState, runState, playerId, events);
                },
                adapter.Handlers.ApplyEnvironment,
                (boardState, runState, playerId, events) =>
                {
                    trace.Add("PlanNextIntents");
                    planningCalls++;
                    Assert.That(enemies.All(enemy => enemy.LockedIntent == null), Is.True);
                    adapter.Handlers.PlanNextIntents(boardState, runState, playerId, events);
                    Assert.That(enemies.All(enemy => enemy.LockedIntent != null), Is.True);
                    events.Add(new MarkerEvent("NextIntents"));
                });
            var controller = new TurnController(board, run, PlayerId, phases: phases);
            PlayerCommand command = scenario == "rejected" ? (PlayerCommand)new MoveCommand(Direction.West) :
                scenario == "exit" ? new MoveCommand(Direction.East) : new WaitCommand();

            TurnResult result = controller.Resolve(command);

            Assert.That(controller.IsResolving, Is.False);
            if (scenario == "rejected")
            {
                AssertRejected(result, CommandRejectionCode.Blocked);
                Assert.That(Snapshot(board, run), Is.EqualTo(before));
                Assert.That(trace, Is.Empty);
                Assert.That(executionCalls + environmentCalls + planningCalls, Is.Zero);
                Assert.That(controller.IsTerminal, Is.False);
                for (int index = 0; index < enemies.Length; index++)
                {
                    Assert.That(enemies[index].LockedIntent, Is.SameAs(initial[index]));
                    Assert.That(enemies[index].Phase, Is.EqualTo(ShamblerPhase.Active));
                }
                return;
            }
            Assert.That(result.Accepted && result.ConsumesTurn, Is.True);
            Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Single().CostAmount, Is.EqualTo(1));
            Assert.That(run.Food, Is.EqualTo(scenario == "starvation" ? 0 : 4));
            if (earlyTerminal)
            {
                AssertEvents(result, scenario == "exit" ? "EntityMoved" : "EntityWaited", "ActionCostApplied",
                    scenario == "exit" ? "ExitReached" : "PlayerStarved");
                Assert.That(trace, Is.Empty);
                Assert.That(executionCalls + environmentCalls + planningCalls, Is.Zero);
                Assert.That(run.Health, Is.EqualTo(100));
                for (int index = 0; index < enemies.Length; index++)
                {
                    Assert.That(enemies[index].LockedIntent, Is.SameAs(initial[index]));
                    Assert.That(enemies[index].Phase, Is.EqualTo(ShamblerPhase.Active));
                }
            }
            else
            {
                Assert.That(executionCalls, Is.EqualTo(1));
                Assert.That(environmentCalls, Is.EqualTo(1));
                Assert.That(planningCalls, Is.EqualTo(lateTerminal ? 0 : 1));
                Assert.That(trace, Is.EqualTo(lateTerminal
                    ? new[] { "ExecuteLockedIntents", "Environment" }
                    : new[] { "ExecuteLockedIntents", "Environment", "PlanNextIntents" }));
                AssertEvents(result, "EntityWaited", "ActionCostApplied",
                    enemyDeath ? "EnemyAttackResolved" : "EntityMoved",
                    enemyDeath ? "EnemyAttackResolved" : "EntityMoved", "Environment",
                    lateTerminal ? "PlayerDied" : "NextIntents");
                for (int index = 0; index < enemies.Length; index++)
                {
                    Assert.That(enemies[index].Phase, Is.EqualTo(ShamblerPhase.Rest));
                    if (lateTerminal)
                        Assert.That(enemies[index].LockedIntent, Is.Null);
                    else
                    {
                        Assert.That(enemies[index].LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Wait));
                        Assert.That(enemies[index].LockedIntent, Is.Not.SameAs(initial[index]));
                    }
                    if (enemyDeath)
                    {
                        var attack = (EnemyAttackResolvedEvent)result.Events[index + 2];
                        Assert.That(attack.AttackerId, Is.EqualTo(enemies[index].ActorId));
                        Assert.That(attack.TargetPosition, Is.EqualTo(Start));
                        Assert.That(attack.IsHit, Is.True);
                        Assert.That(attack.AffectedTargetId, Is.EqualTo(PlayerId));
                        Assert.That(attack.HealthChange, Is.EqualTo(index == 0 ? -10 : 0));
                    }
                    else
                    {
                        var movement = (EntityMovedEvent)result.Events[index + 2];
                        Assert.That(movement.EntityId, Is.EqualTo(enemies[index].ActorId));
                        Assert.That(movement.From, Is.EqualTo(index == 0 ? firstSource : secondSource));
                        Assert.That(movement.To, Is.EqualTo(index == 0 ? Destination : new GridPosition(2, 2)));
                    }
                }
                Assert.That(run.Health, Is.EqualTo(lateTerminal ? 0 : 100));
            }
            Assert.That(controller.IsTerminal, Is.EqualTo(earlyTerminal || lateTerminal));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Active));
            if (earlyTerminal || lateTerminal)
            {
                string terminalSnapshot = Snapshot(board, run);
                string[] terminalTrace = trace.ToArray();
                AssertRejected(controller.Resolve(new WaitCommand()), CommandRejectionCode.InvalidState);
                Assert.That(Snapshot(board, run), Is.EqualTo(terminalSnapshot));
                CollectionAssert.AreEqual(terminalTrace, trace);
                return;
            }
            expectedLocked = enemies.Select(enemy => enemy.LockedIntent).ToArray();
            var beforeRest = board.GetEntities();

            TurnResult rest = controller.Resolve(new WaitCommand());

            AssertEvents(rest, "EntityWaited", "ActionCostApplied", "EntityWaited", "EntityWaited", "Environment", "NextIntents");
            Assert.That(rest.Events.OfType<EntityWaitedEvent>().Select(wait => wait.EntityId),
                Is.EqualTo(new[] { PlayerId, first.ActorId, second.ActorId }));
            Assert.That(rest.Events.OfType<ActionCostAppliedEvent>().Single().CostAmount, Is.EqualTo(1));
            Assert.That(rest.Accepted && rest.ConsumesTurn, Is.True);
            CollectionAssert.AreEqual(beforeRest, board.GetEntities());
            Assert.That(run.Food, Is.EqualTo(3));
            Assert.That(run.Health, Is.EqualTo(100));
            Assert.That(executionCalls, Is.EqualTo(2));
            Assert.That(environmentCalls, Is.EqualTo(2));
            Assert.That(planningCalls, Is.EqualTo(2));
            Assert.That(trace, Is.EqualTo(new[] { "ExecuteLockedIntents", "Environment", "PlanNextIntents",
                "ExecuteLockedIntents", "Environment", "PlanNextIntents" }));
            Assert.That(enemies.All(enemy => enemy.Phase == ShamblerPhase.Active && enemy.LockedIntent != null), Is.True);
            Assert.That(first.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Attack));
            Assert.That(first.LockedIntent.TargetPosition, Is.EqualTo(Start));
            Assert.That(second.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Move));
            Assert.That(second.LockedIntent.TargetPosition, Is.EqualTo(new GridPosition(1, 2)));
            Assert.That(controller.IsTerminal || controller.IsResolving, Is.False);
        }

        /// <summary>
        /// The controller executes the pre-input fixed-cell attack as a miss instead of replacing
        /// it with the pursuit movement that fresh active planning would choose after player movement.
        /// </summary>
        [Test]
        public void LockedShamblerAttack_PlayerLeavesTarget_MissesWithoutReplanning()
        {
            BoardState board = CreateShamblerBoard(Destination);
            RunState run = CreateRun();
            var enemy = new ShamblerState(new EntityId(2), new ShamblerDefinition(10));
            var adapter = new ShamblerTurnPhases(enemy);
            adapter.PlanInitialIntent(board, PlayerId);
            EnemyIntent initial = enemy.LockedIntent;
            Assert.That(initial.Kind, Is.EqualTo(EnemyIntentKind.Attack));
            Assert.That(initial.TargetPosition, Is.EqualTo(Start));
            Assert.That(initial.TargetId, Is.EqualTo(PlayerId));
            var trace = new List<string>();
            int planningCalls = 0;
            var phases = new TurnPhaseHandlers(
                (boardState, runState, playerId, events) =>
                {
                    trace.Add("ExecuteLockedIntents");
                    Assert.That(runState.Food, Is.EqualTo(4));
                    Assert.That(boardState.TryGetEntity(playerId, out BoardEntityState player), Is.True);
                    Assert.That(player.Position, Is.EqualTo(new GridPosition(1, 2)));
                    Assert.That(enemy.LockedIntent, Is.SameAs(initial));
                    var freshActive = new ShamblerState(enemy.ActorId, enemy.Definition);
                    EnemyIntent hypothetical = new ShamblerPlanner().Plan(boardState, freshActive, playerId);
                    Assert.That(hypothetical.Kind, Is.EqualTo(EnemyIntentKind.Move));
                    Assert.That(hypothetical.TargetPosition, Is.EqualTo(new GridPosition(2, 2)));
                    adapter.Handlers.ExecuteLockedIntents(boardState, runState, playerId, events);
                },
                (boardState, runState, playerId, events) =>
                {
                    trace.Add("Environment");
                    Assert.That(enemy.LockedIntent, Is.Null);
                    Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
                },
                (boardState, runState, playerId, events) =>
                {
                    trace.Add("PlanNextIntents");
                    planningCalls++;
                    Assert.That(enemy.LockedIntent, Is.Null);
                    adapter.Handlers.PlanNextIntents(boardState, runState, playerId, events);
                });
            var controller = new TurnController(board, run, PlayerId, phases: phases);

            TurnResult result = controller.Resolve(new MoveCommand(Direction.North));

            Assert.That(result.Accepted && result.ConsumesTurn, Is.True);
            AssertEvents(result, "EntityMoved", "ActionCostApplied", "EnemyAttackResolved");
            Assert.That(result.Events.OfType<ActionCostAppliedEvent>().Single().CostAmount, Is.EqualTo(1));
            Assert.That(run.Food, Is.EqualTo(4));
            Assert.That(run.Health, Is.EqualTo(10));
            Assert.That(board.TryGetEntity(enemy.ActorId, out BoardEntityState actor), Is.True);
            Assert.That(actor.Position, Is.EqualTo(Destination));
            var miss = result.Events.OfType<EnemyAttackResolvedEvent>().Single();
            Assert.That(miss.AttackerId, Is.EqualTo(enemy.ActorId));
            Assert.That(miss.TargetPosition, Is.EqualTo(Start));
            Assert.That(miss.IsHit, Is.False);
            Assert.That(miss.AffectedTargetId, Is.Null);
            Assert.That(miss.HealthChange, Is.Zero);
            Assert.That(initial.TargetPosition, Is.EqualTo(Start));
            Assert.That(trace, Is.EqualTo(new[] { "ExecuteLockedIntents", "Environment", "PlanNextIntents" }));
            Assert.That(planningCalls, Is.EqualTo(1));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.That(enemy.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Wait));
            Assert.That(enemy.LockedIntent, Is.Not.SameAs(initial));
            Assert.That(controller.IsTerminal || controller.IsResolving, Is.False);
        }

        /// <summary>
        /// Initial planning precedes input without board/run mutation or cadence advancement.
        /// </summary>
        [Test]
        public void ShamblerInitialPlanningRetainsOneActiveIntent()
        {
            BoardState board = CreateShamblerBoard(new GridPosition(3, 1));
            RunState run = CreateRun();
            var enemy = new ShamblerState(new EntityId(2), new ShamblerDefinition(10));
            var adapter = new ShamblerTurnPhases(enemy);
            string before = Snapshot(board, run);
            Assert.That(enemy.LockedIntent, Is.Null);

            adapter.PlanInitialIntent(board, PlayerId);

            EnemyIntent initial = enemy.LockedIntent;
            Assert.That(initial.Kind, Is.EqualTo(EnemyIntentKind.Move));
            Assert.That(initial.TargetPosition, Is.EqualTo(Destination));
            Assert.That(initial.AttackOnPlayerEntry, Is.True);
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(Snapshot(board, run), Is.EqualTo(before));
            Assert.Throws<InvalidOperationException>(() => adapter.PlanInitialIntent(board, PlayerId));
            Assert.That(enemy.LockedIntent, Is.SameAs(initial));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
        }

        /// <summary>
        /// Each active outcome executes before environment and is followed by one adjacent-safe rest turn.
        /// </summary>
        [TestCase("move")]
        [TestCase("blocked")]
        [TestCase("miss")]
        [TestCase("conditional-hit")]
        public void ShamblerPhasesExecuteLockedOutcomeThenPlanRest(string outcome)
        {
            GridPosition enemyPosition = outcome == "miss" ? Destination : new GridPosition(3, 1);
            BoardState board = CreateShamblerBoard(enemyPosition);
            RunState run = CreateRun();
            run.RestoreHealth(90);
            var enemy = new ShamblerState(new EntityId(2), new ShamblerDefinition(10));
            int environmentCalls = 0;
            var adapter = new ShamblerTurnPhases(enemy, (boardState, runState, playerId, events) =>
            {
                environmentCalls++;
                Assert.That(enemy.LockedIntent, Is.Null, "Execution consumed the old intent before environment.");
                Assert.That(enemy.Phase, Is.EqualTo(environmentCalls == 1 ? ShamblerPhase.Rest : ShamblerPhase.Active));
                events.Add(new MarkerEvent("Environment"));
            });
            adapter.PlanInitialIntent(board, PlayerId);
            EnemyIntent initial = enemy.LockedIntent;
            if (outcome == "blocked")
                Add(board, 300, BoardLayer.Obstacle, EntityKind.Obstacle, Destination, BoardEntityTraits.Default);
            PlayerCommand command = outcome == "conditional-hit" ? (PlayerCommand)new MoveCommand(Direction.East) :
                outcome == "miss" ? new MoveCommand(Direction.North) : new WaitCommand();
            var controller = new TurnController(board, run, PlayerId, phases: adapter.Handlers);

            TurnResult first = controller.Resolve(command);

            string enemyEvent = outcome == "move" ? "EntityMoved" : outcome == "blocked" ? "EntityWaited" : "EnemyAttackResolved";
            AssertEvents(first, command is WaitCommand ? "EntityWaited" : "EntityMoved", "ActionCostApplied", enemyEvent, "Environment");
            Assert.That(first.Accepted && first.ConsumesTurn, Is.True);
            Assert.That(first.Events.OfType<ActionCostAppliedEvent>().Count(), Is.EqualTo(1));
            Assert.That(run.Food, Is.EqualTo(4));
            Assert.That(run.Health, Is.EqualTo(outcome == "conditional-hit" ? 90 : 100));
            Assert.That(environmentCalls, Is.EqualTo(1));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.That(enemy.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Wait));
            Assert.That(enemy.LockedIntent, Is.Not.SameAs(initial));
            Assert.That(board.TryGetEntity(enemy.ActorId, out BoardEntityState actor), Is.True);
            Assert.That(actor.Position, Is.EqualTo(outcome == "move" ? Destination : enemyPosition));
            if (outcome == "miss" || outcome == "conditional-hit")
            {
                var attack = first.Events.OfType<EnemyAttackResolvedEvent>().Single();
                Assert.That(attack.TargetPosition, Is.EqualTo(outcome == "miss" ? Start : Destination));
                Assert.That(attack.IsHit, Is.EqualTo(outcome == "conditional-hit"));
                Assert.That(attack.HealthChange, Is.EqualTo(outcome == "conditional-hit" ? -10 : 0));
            }
            var beforeRest = board.GetEntities();

            TurnResult rest = controller.Resolve(new WaitCommand());

            AssertEvents(rest, "EntityWaited", "ActionCostApplied", "EntityWaited", "Environment");
            Assert.That(run.Food, Is.EqualTo(3));
            Assert.That(run.Health, Is.EqualTo(outcome == "conditional-hit" ? 90 : 100));
            Assert.That(environmentCalls, Is.EqualTo(2));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(enemy.LockedIntent, Is.Not.Null);
            CollectionAssert.AreEqual(beforeRest, board.GetEntities());
            Assert.That(controller.IsTerminal || controller.IsResolving, Is.False);
        }

        /// <summary>
        /// Rejection leaves the exact initial intent and cadence intact and produces no enemy effects.
        /// </summary>
        [Test]
        public void RejectedCommandPreservesShamblerIntentAndCadence()
        {
            BoardState board = CreateShamblerBoard(new GridPosition(3, 1));
            RunState run = CreateRun();
            var enemy = new ShamblerState(new EntityId(2), new ShamblerDefinition(10));
            var adapter = new ShamblerTurnPhases(enemy, FailPhase);
            adapter.PlanInitialIntent(board, PlayerId);
            EnemyIntent initial = enemy.LockedIntent;
            string before = Snapshot(board, run);
            var controller = new TurnController(board, run, PlayerId, phases: adapter.Handlers);

            AssertRejected(controller.Resolve(new MoveCommand(Direction.West)), CommandRejectionCode.Blocked);

            Assert.That(Snapshot(board, run), Is.EqualTo(before));
            Assert.That(enemy.LockedIntent, Is.SameAs(initial));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(controller.IsResolving || controller.IsTerminal, Is.False);
        }

        /// <summary>
        /// Phase 4 starvation or a live exit skips enemy execution and preserves its old intent.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ImmediateTerminalOutcomePreservesShamblerIntent(bool exit)
        {
            BoardState board = CreateShamblerBoard(new GridPosition(3, 1), exit);
            RunState run = CreateRun(exit ? 5 : 1);
            var enemy = new ShamblerState(new EntityId(2), new ShamblerDefinition(20));
            var adapter = new ShamblerTurnPhases(enemy, FailPhase);
            adapter.PlanInitialIntent(board, PlayerId);
            EnemyIntent initial = enemy.LockedIntent;
            var controller = new TurnController(board, run, PlayerId, phases: adapter.Handlers);

            TurnResult result = controller.Resolve(exit ? (PlayerCommand)new MoveCommand(Direction.East) : new WaitCommand());

            AssertEvents(result, exit ? "EntityMoved" : "EntityWaited", "ActionCostApplied", exit ? "ExitReached" : "PlayerStarved");
            Assert.That(enemy.LockedIntent, Is.SameAs(initial));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(run.Health, Is.EqualTo(10));
            Assert.That(controller.IsTerminal, Is.True);
            AssertRejected(controller.Resolve(new WaitCommand()), CommandRejectionCode.InvalidState);
        }

        /// <summary>
        /// Lethal enemy or environment damage reaches phase 7 and skips the next planning boundary.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LateTerminalOutcomeConsumesShamblerButDoesNotPlanAgain(bool environmentDeath)
        {
            BoardState board = CreateShamblerBoard(environmentDeath ? new GridPosition(3, 1) : Destination);
            RunState run = CreateRun();
            var enemy = new ShamblerState(new EntityId(2), new ShamblerDefinition(20));
            var adapter = new ShamblerTurnPhases(enemy, (boardState, runState, playerId, events) =>
            {
                Assert.That(enemy.LockedIntent, Is.Null);
                if (environmentDeath)
                    runState.TakeDamage(runState.Health);
                events.Add(new MarkerEvent("Environment"));
            });
            adapter.PlanInitialIntent(board, PlayerId);
            var controller = new TurnController(board, run, PlayerId, phases: adapter.Handlers);

            TurnResult result = controller.Resolve(new WaitCommand());

            AssertEvents(result, "EntityWaited", "ActionCostApplied", environmentDeath ? "EntityMoved" : "EnemyAttackResolved", "Environment", "PlayerDied");
            Assert.That(run.Food, Is.EqualTo(4));
            Assert.That(run.Health, Is.Zero);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Active), "Run lifecycle is not owned by the adapter.");
            Assert.That(enemy.LockedIntent, Is.Null);
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
            Assert.That(controller.IsTerminal, Is.True);
            Assert.That(controller.IsResolving, Is.False);
        }

        /// <summary>Player effects and one cost precede locked intents, environment, planning, and unlock.</summary>
        [Test]
        public void AcceptedTurnOrdersEveryPhaseAndChargesOnce()
        {
            BoardState board = CreateBoard();
            RunState run = CreateRun();
            TurnController controller = null;
            var phases = new TurnPhaseHandlers(
                (b, r, p, events) =>
                {
                    Assert.That(controller.IsResolving, Is.True);
                    Assert.That(r.Food, Is.EqualTo(4));
                    Assert.That(b.TryGetEntity(p, out BoardEntityState player), Is.True);
                    Assert.That(player.Position, Is.EqualTo(Destination));
                    events.Add(new MarkerEvent("LockedIntents"));
                },
                (b, r, p, events) =>
                {
                    Assert.That(controller.IsResolving, Is.True);
                    events.Add(new MarkerEvent("Environment"));
                },
                (b, r, p, events) =>
                {
                    Assert.That(controller.IsResolving, Is.True);
                    events.Add(new MarkerEvent("NextIntents"));
                });
            controller = new TurnController(board, run, PlayerId, phases: phases);

            TurnResult result = controller.Resolve(new MoveCommand(Direction.East));

            Assert.That(result.Accepted && result.ConsumesTurn, Is.True);
            AssertEvents(result, "EntityMoved", "ActionCostApplied", "LockedIntents", "Environment", "NextIntents");
            Assert.That(run.Food, Is.EqualTo(4));
            Assert.That(controller.IsResolving || controller.IsTerminal, Is.False);
        }

        /// <summary>Rejection preserves all state and unlocks input without invoking later phases.</summary>
        [Test]
        public void RejectedCommandHasNoEffectsAndAllowsNextCommand()
        {
            BoardState board = CreateBoard();
            RunState run = CreateRun();
            var calls = new List<string>();
            var controller = new TurnController(board, run, PlayerId, phases: RecordingPhases(calls));
            string before = Snapshot(board, run);

            TurnResult rejected = controller.Resolve(new MoveCommand(Direction.West));

            AssertRejected(rejected, CommandRejectionCode.Blocked);
            Assert.That(Snapshot(board, run), Is.EqualTo(before));
            Assert.That(calls, Is.Empty);
            Assert.That(controller.IsResolving || controller.IsTerminal, Is.False);
            Assert.That(controller.Resolve(new WaitCommand()).Accepted, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "LockedIntents", "Environment", "NextIntents" }));
            Assert.That(run.Food, Is.EqualTo(4));
        }

        /// <summary>Null input is rejected by the player boundary with no later phases.</summary>
        [Test]
        public void NullCommandIsRejectedWithoutCost()
        {
            RunState run = CreateRun();
            var controller = new TurnController(CreateBoard(), run, PlayerId, phases: ForbiddenPhases());
            AssertRejected(controller.Resolve(null), CommandRejectionCode.InvalidCommand);
            Assert.That(run.Food, Is.EqualTo(5));
            Assert.That(controller.IsResolving, Is.False);
        }

        /// <summary>Immediate starvation ends the board before enemies, environment, or planning.</summary>
        [Test]
        public void StarvationStopsLaterPhasesAndFurtherCommands()
        {
            RunState run = CreateRun(food: 1);
            var controller = new TurnController(CreateBoard(), run, PlayerId, phases: ForbiddenPhases());
            TurnResult result = controller.Resolve(new WaitCommand());
            AssertEvents(result, "EntityWaited", "ActionCostApplied", "PlayerStarved");
            Assert.That(controller.IsTerminal, Is.True);
            Assert.That(controller.IsResolving, Is.False);
            AssertRejected(controller.Resolve(new WaitCommand()), CommandRejectionCode.InvalidState);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Active), "Lifecycle owns run completion.");
        }

        /// <summary>A live exit ends the board before enemies even though the run remains active.</summary>
        [Test]
        public void ExitStopsLaterPhasesAndFurtherCommands()
        {
            RunState run = CreateRun();
            var controller = new TurnController(CreateBoard(exit: true), run, PlayerId, phases: ForbiddenPhases());
            AssertEvents(controller.Resolve(new MoveCommand(Direction.East)), "EntityMoved", "ActionCostApplied", "ExitReached");
            Assert.That(controller.IsTerminal, Is.True);
            AssertRejected(controller.Resolve(new WaitCommand()), CommandRejectionCode.InvalidState);
            Assert.That(run.Food, Is.EqualTo(4));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Active));
        }

        /// <summary>O-002 resolves starvation ahead of an exit reached by the same action.</summary>
        [Test]
        public void SimultaneousExitAndStarvationReportsOnlyStarvation()
        {
            var controller = new TurnController(CreateBoard(exit: true), CreateRun(1), PlayerId, phases: ForbiddenPhases());
            AssertEvents(controller.Resolve(new MoveCommand(Direction.East)), "EntityMoved", "ActionCostApplied", "PlayerStarved");
            Assert.That(controller.IsTerminal, Is.True);
        }

        /// <summary>A food reward precedes cost and can preserve a live exit.</summary>
        [Test]
        public void FoodOnExitRescuesPlayerBeforeActionCost()
        {
            BoardState board = CreateBoard(exit: true, reward: 2);
            RunState run = CreateRun(1);
            var controller = new TurnController(board, run, PlayerId, phases: ForbiddenPhases());
            AssertEvents(controller.Resolve(new MoveCommand(Direction.East)),
                "EntityMoved", "ItemCollected", "FoodRestored", "ActionCostApplied", "ExitReached");
            Assert.That(run.Food, Is.EqualTo(2));
            Assert.That(board.TryGetEntity(new EntityId(200), out _), Is.False);
        }

        /// <summary>Health death during either later phase is reported at phase 7 and prevents planning.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LaterHealthDeathSkipsPlanning(bool duringEnvironment)
        {
            var calls = new List<string>();
            var phases = new TurnPhaseHandlers(
                (b, r, p, events) =>
                {
                    calls.Add("LockedIntents");
                    if (!duringEnvironment) r.TakeDamage(r.Health);
                },
                (b, r, p, events) =>
                {
                    calls.Add("Environment");
                    if (duringEnvironment) r.TakeDamage(r.Health);
                },
                FailPhase);
            var controller = new TurnController(CreateBoard(), CreateRun(), PlayerId, phases: phases);
            TurnResult result = controller.Resolve(new WaitCommand());
            Assert.That(calls, Is.EqualTo(new[] { "LockedIntents", "Environment" }));
            AssertEvents(result, "EntityWaited", "ActionCostApplied", "PlayerDied");
            Assert.That(((PlayerDiedEvent)result.Events[2]).EntityId, Is.EqualTo(PlayerId));
            Assert.That(controller.IsTerminal, Is.True);
        }

        /// <summary>Starvation or an explicit dead status after environment is terminal without duplicate events.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LaterStarvationOrDeadStatusEndsBoard(bool explicitDeadStatus)
        {
            var phases = new TurnPhaseHandlers(applyEnvironment: (b, r, p, events) =>
            {
                if (explicitDeadStatus) r.MarkDead();
                else r.ConsumeFood(r.Food);
            }, planNextIntents: FailPhase);
            var controller = new TurnController(CreateBoard(), CreateRun(), PlayerId, phases: phases);
            AssertEvents(controller.Resolve(new WaitCommand()), "EntityWaited", "ActionCostApplied",
                explicitDeadStatus ? "PlayerDied" : "PlayerStarved");
            Assert.That(controller.IsTerminal, Is.True);
            AssertRejected(controller.Resolve(new WaitCommand()), CommandRejectionCode.InvalidState);
        }

        /// <summary>The second terminal check respects live exit and death precedence over a simultaneous exit.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LaterExitIsTerminalUnlessPlayerDies(bool dies)
        {
            var phases = new TurnPhaseHandlers(applyEnvironment: (b, r, p, events) =>
            {
                events.Add(new ExitReachedEvent(p, new EntityId(101)));
                if (dies) r.ConsumeFood(r.Food);
            }, planNextIntents: FailPhase);
            var controller = new TurnController(CreateBoard(), CreateRun(), PlayerId, phases: phases);
            AssertEvents(controller.Resolve(new WaitCommand()), "EntityWaited", "ActionCostApplied",
                dies ? "PlayerStarved" : "ExitReached");
            Assert.That(controller.IsTerminal, Is.True);
        }

        /// <summary>A future command uses the same pipeline with its own validated cost policy.</summary>
        [Test]
        public void NewCommandTypeAndDifferentCostNeedNoOrchestrationChange()
        {
            BoardState board = CreateBoard();
            RunState run = CreateRun();
            var calls = new List<string>();
            var resolver = new CallbackResolver((b, r, p, command) =>
            {
                if (!(command is CustomCommand custom) || !custom.Valid)
                    return new RejectedTurnResult(CommandRejectionCode.InvalidCommand);
                r.ConsumeFood(2);
                return new AcceptedTurnResult(new GameEvent[] { new MarkerEvent("CustomEffect"), new ActionCostAppliedEvent(p, 2) });
            });
            var controller = new TurnController(board, run, PlayerId, resolver, RecordingPhases(calls));
            string before = Snapshot(board, run);
            AssertRejected(controller.Resolve(new CustomCommand(false)), CommandRejectionCode.InvalidCommand);
            Assert.That(Snapshot(board, run), Is.EqualTo(before));
            Assert.That(calls, Is.Empty);

            TurnResult result = controller.Resolve(new CustomCommand(true));

            AssertEvents(result, "CustomEffect", "ActionCostApplied", "LockedIntents", "Environment", "NextIntents");
            Assert.That(run.Food, Is.EqualTo(3));
            Assert.That(((ActionCostAppliedEvent)result.Events[1]).CostAmount, Is.EqualTo(2));
        }

        /// <summary>Nested commands from the player resolver or later phases cannot start another turn.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ReentrantResolutionIsRejectedAcrossAllPhases(int stage)
        {
            RunState run = CreateRun();
            TurnController controller = null;
            Action tryNested = () =>
            {
                int foodBefore = run.Food;
                AssertRejected(controller.Resolve(new WaitCommand()), CommandRejectionCode.InvalidState);
                Assert.That(run.Food, Is.EqualTo(foodBefore));
                Assert.That(controller.IsResolving, Is.True);
            };
            var resolver = new CallbackResolver((b, r, p, c) =>
            {
                if (stage == 0) tryNested();
                return new TurnResolver().Resolve(b, r, p, c);
            });
            var phases = new TurnPhaseHandlers(
                (b, r, p, e) => { if (stage == 1) tryNested(); },
                (b, r, p, e) => { if (stage == 2) tryNested(); },
                (b, r, p, e) => { if (stage == 3) tryNested(); });
            controller = new TurnController(CreateBoard(), run, PlayerId, resolver, phases);
            Assert.That(controller.Resolve(new WaitCommand()).Accepted, Is.True);
            Assert.That(run.Food, Is.EqualTo(4));
            Assert.That(controller.IsResolving, Is.False);
        }

        /// <summary>A truly concurrent call is rejected while the first resolver owns the board.</summary>
        [Test]
        public void ConcurrentCallCannotEnterPlayerResolver()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                int count = 0;
                RunState run = CreateRun();
                var resolver = new CallbackResolver((b, r, p, c) =>
                {
                    Interlocked.Increment(ref count);
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                    return new TurnResolver().Resolve(b, r, p, c);
                });
                var controller = new TurnController(CreateBoard(), run, PlayerId, resolver);
                Task<TurnResult> first = Task.Run(() => controller.Resolve(new WaitCommand()));
                try
                {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
                    AssertRejected(controller.Resolve(new WaitCommand()), CommandRejectionCode.InvalidState);
                    Assert.That(count, Is.EqualTo(1));
                }
                finally
                {
                    release.Set();
                    Assert.That(first.Wait(TimeSpan.FromSeconds(10)), Is.True);
                }
                Assert.That(first.Result.Accepted, Is.True);
                Assert.That(run.Food, Is.EqualTo(4));
                Assert.That(controller.IsResolving, Is.False);
            }
        }

        /// <summary>Implementation exceptions propagate while the resolution lock is always released.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ExceptionInAnyPhaseReleasesLock(int stage)
        {
            bool fail = true;
            Action throwOnce = () => { if (fail) throw new InvalidOperationException("Test phase fault."); };
            var resolver = new CallbackResolver((b, r, p, c) =>
            {
                if (stage == 0) throwOnce();
                return new TurnResolver().Resolve(b, r, p, c);
            });
            var phases = new TurnPhaseHandlers(
                (b, r, p, e) => { if (stage == 1) throwOnce(); },
                (b, r, p, e) => { if (stage == 2) throwOnce(); },
                (b, r, p, e) => { if (stage == 3) throwOnce(); });
            var controller = new TurnController(CreateBoard(), CreateRun(), PlayerId, resolver, phases);
            Assert.Throws<InvalidOperationException>(() => controller.Resolve(new WaitCommand()));
            Assert.That(controller.IsResolving, Is.False);
            fail = false;
            Assert.That(controller.Resolve(new WaitCommand()).Accepted, Is.True);
        }

        /// <summary>Returned events are detached from the phase collection and subsequent turns.</summary>
        [Test]
        public void ResultOwnsImmutableEventSnapshot()
        {
            ICollection<GameEvent> retained = null;
            var phases = new TurnPhaseHandlers(planNextIntents: (b, r, p, e) => retained = e);
            var controller = new TurnController(CreateBoard(), CreateRun(), PlayerId, phases: phases);
            TurnResult first = controller.Resolve(new WaitCommand());
            retained.Add(new MarkerEvent("UnownedLateWrite"));
            controller.Resolve(new WaitCommand());
            AssertEvents(first, "EntityWaited", "ActionCostApplied");
        }

        /// <summary>The default phases give reproducible results without Unity or presentation.</summary>
        [Test]
        public void IdenticalInputsProduceIdenticalCompleteTurns()
        {
            var results = new List<string>();
            for (int repeat = 0; repeat < 2; repeat++)
            {
                BoardState board = CreateBoard();
                RunState run = CreateRun();
                var controller = new TurnController(board, run, PlayerId);
                TurnResult result = controller.Resolve(new MoveCommand(Direction.East));
                results.Add(Snapshot(board, run) + ";" + string.Join(",", result.Events.Select(e => e.EventType)));
            }
            Assert.That(results[0], Is.EqualTo(results[1]));
        }

        private static TurnPhaseHandlers ForbiddenPhases() => new TurnPhaseHandlers(FailPhase, FailPhase, FailPhase);

        private static void FailPhase(BoardState board, RunState run, EntityId playerId, ICollection<GameEvent> events)
        {
            Assert.Fail("This phase must not execute after rejection or the applicable terminal check.");
        }

        private static TurnPhaseHandlers RecordingPhases(List<string> calls)
        {
            TurnPhaseHandler Record(string name) => (b, r, p, events) =>
            {
                calls.Add(name);
                events.Add(new MarkerEvent(name));
            };
            return new TurnPhaseHandlers(Record("LockedIntents"), Record("Environment"), Record("NextIntents"));
        }

        private static BoardState CreateShamblerBoard(GridPosition enemyPosition, bool exit = false)
        {
            BoardState board = CreateBoard(exit);
            var extraFloor = new[] { new GridPosition(3, 1), new GridPosition(1, 2),
                new GridPosition(2, 2), new GridPosition(3, 2) };
            for (int index = 0; index < extraFloor.Length; index++)
                Add(board, 102 + index, BoardLayer.Terrain, new EntityKind("terrain"), extraFloor[index],
                    new BoardEntityTraits(true, false, false));
            Add(board, 2, BoardLayer.Actor, EntityKind.Enemy, enemyPosition, BoardEntityTraits.Default);
            return board;
        }

        private static BoardState CreateBoard(bool exit = false, int reward = 0)
        {
            var board = new BoardState();
            Add(board, 100, BoardLayer.Terrain, new EntityKind("terrain"), Start, new BoardEntityTraits(true, false, false));
            Add(board, 101, BoardLayer.Terrain, new EntityKind("terrain"), Destination, new BoardEntityTraits(true, exit, false));
            Add(board, 1, BoardLayer.Actor, EntityKind.Player, Start, BoardEntityTraits.Default);
            if (reward > 0) Add(board, 200, BoardLayer.Item, EntityKind.Item, Destination, BoardEntityTraits.Default, reward);
            return board;
        }

        private static void Add(BoardState board, int id, BoardLayer layer, EntityKind kind,
            GridPosition position, BoardEntityTraits traits, int reward = 0)
        {
            Assert.That(board.TryAdd(new BoardEntityState(new EntityId(id),
                new BoardEntityDefinition(layer, kind, "test." + id, traits, reward), position)), Is.True);
        }

        private static RunState CreateRun(int food = 5) =>
            new RunState("m3.5-test-run", 3500, new RunStateConfiguration(10, food, 0, "forest.start"));

        private static void AssertEvents(TurnResult result, params string[] names) =>
            Assert.That(result.Events.Select(e => e.EventType), Is.EqualTo(names));

        private static void AssertRejected(TurnResult result, CommandRejectionCode code)
        {
            Assert.That(result, Is.TypeOf<RejectedTurnResult>());
            Assert.That(((RejectedTurnResult)result).RejectionCode, Is.EqualTo(code));
            Assert.That(result.Accepted || result.ConsumesTurn, Is.False);
            Assert.That(result.Events, Is.Empty);
        }

        private static string Snapshot(BoardState board, RunState run) =>
            string.Join("|", board.GetEntities().Select(e => $"{e.Id.Value}:{e.Position.X}:{e.Position.Y}")) +
            $";{run.Food};{run.Health};{run.Status}";

        private sealed class CallbackResolver : IPlayerPhaseResolver
        {
            private readonly Func<BoardState, RunState, EntityId, PlayerCommand, TurnResult> callback;
            internal CallbackResolver(Func<BoardState, RunState, EntityId, PlayerCommand, TurnResult> callback) => this.callback = callback;
            /// <inheritdoc />
            public TurnResult Resolve(BoardState boardState, RunState runState, EntityId playerId, PlayerCommand command) =>
                callback(boardState, runState, playerId, command);
        }

        private sealed class MarkerEvent : GameEvent
        {
            internal MarkerEvent(string name) => EventType = name;
            /// <inheritdoc />
            public override string EventType { get; }
        }

        private sealed class CustomCommand : PlayerCommand
        {
            internal CustomCommand(bool valid) => Valid = valid;
            internal bool Valid { get; }
            /// <inheritdoc />
            public override string CommandType => "Custom";
        }
    }
}
