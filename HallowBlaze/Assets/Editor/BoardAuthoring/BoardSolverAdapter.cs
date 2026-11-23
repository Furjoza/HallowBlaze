using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Separates bounded search evidence from independent real-controller replay and invalid/unsupported input.</summary>
    public sealed class BoardSolverReport
    {
        internal BoardSolverReport(string status, string message, LandmarkSolverResult result = null, bool replayed = false)
        { Status = status; Message = message; Result = result; Replayed = replayed; }
        /// <summary>Gets Solved, NoSolution, StateLimit, DepthLimit, TimeLimit, Invalid, Unsupported or ReplayFailed.</summary>
        public string Status { get; }
        /// <summary>Gets reproduction context or a controlled refusal, never a portable replay export.</summary>
        public string Message { get; }
        /// <summary>Gets the existing solver's detached evidence, or null before search could run.</summary>
        public LandmarkSolverResult Result { get; }
        /// <summary>Gets whether every found command was accepted and the requested exit was reached on fresh owners.</summary>
        public bool Replayed { get; }
    }

    /// <summary>Runs the existing bounded landmark solver without launching Play Mode or reading/writing saves.</summary>
    public static class BoardSolverAdapter
    {
        /// <summary>Solves an immutable template snapshot and checks any solution against independent production turn owners.</summary>
        /// <param name="json">Strict schema-1 landmark or schema-2 shared board snapshot.</param>
        /// <param name="variantId">Explicit stable variant ID, never a random selection.</param>
        /// <param name="outcomeId">Exact requested mandatory or optional outcome.</param>
        /// <param name="settings">Local resources/equipment/seed; no live run is borrowed.</param>
        /// <param name="maxStates">Positive unique-state cap.</param>
        /// <param name="maxDepth">Nonnegative accepted-command cap.</param>
        /// <param name="maxMilliseconds">Nonnegative elapsed-time cap; zero reports TimeLimit.</param>
        public static BoardSolverReport Solve(string json, string variantId, string outcomeId, BoardTestPlaySettings settings,
            int maxStates = 50000, int maxDepth = 32, int maxMilliseconds = 10000)
        {
            try
            {
                if (settings == null || settings.Health < 1 || settings.Food < 1)
                    throw new ArgumentException("Starting resources must be positive.");
                JToken schema = JObject.Parse(json)["schemaVersion"];
                if (schema?.Type == JTokenType.Integer && (int)schema != LandmarkTemplateJson.CurrentSchemaVersion && (int)schema != BoardTemplateJson.CurrentSchemaVersion)
                    throw new NotSupportedException("Unsupported landmark schema version: " + schema);
                LandmarkTemplate template = BoardTemplateJson.ReadCompatible(json, OrdinaryBoardContent.CreateAuthoringCatalog());
                var inputs = new BoardTestPlaySettings
                { Seed = settings.Seed, Health = settings.Health, Food = settings.Food, EquipAxe = settings.EquipAxe };
                var source = new Owners(template, variantId, inputs);
                var result = new LandmarkSolver(source.Landmark, source.Board, source.Run, source.Player, source.Enemies,
                    OrdinaryBoardContent.CreateAuthoringCatalog(), AxeContent.LoadRules()).Solve(outcomeId, maxStates, maxDepth, maxMilliseconds);
                if (result.Status != LandmarkSolverStatus.Solved)
                    return new BoardSolverReport(result.Status.ToString(), result.ToString(), result);
                var replay = new Owners(template, variantId, inputs);
                EntityId goal = replay.Landmark.Outcomes.Single(outcome => outcome.Id == outcomeId).ExitEntityId;
                bool reached = false;
                foreach (LandmarkSolverStep step in result.SolutionPath)
                {
                    TurnResult turn = replay.Controller.Resolve(step.CreateCommand(replay.Board, replay.Run, replay.Player));
                    if (!turn.Accepted) return new BoardSolverReport("ReplayFailed", result + " rejected=" + step, result);
                    if (turn.Events.OfType<ExitReachedEvent>().Any(exit => exit.ExitId.Equals(goal))) reached = true;
                }
                if (!reached || !replay.Controller.IsTerminal || replay.Run.Health <= 0)
                    return new BoardSolverReport("ReplayFailed", result + " requested exit was not reproduced", result);
                return new BoardSolverReport("Solved", result + " commands=" + result.SolutionPath.Count + " replay=Passed", result, true);
            }
            catch (NotSupportedException exception) { return new BoardSolverReport("Unsupported", exception.Message); }
            catch (Exception exception) { return new BoardSolverReport("Invalid", exception.Message); }
        }

        /// <summary>Proves at least one shared exit with real inputs before Test; an incomplete search is reported honestly, not as NoSolution.</summary>
        public static BoardSolverReport CheckPlayable(string json, string variantId, BoardTestPlaySettings settings)
        {
            LandmarkTemplate template = BoardTemplateJson.ReadCompatible(json, OrdinaryBoardContent.CreateAuthoringCatalog());
            var outcomes = template.Outcomes.Where(goal => template.IsShared || !goal.IsOptional).ToArray();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            BoardSolverReport report = null;
            BoardSolverReport incomplete = null;
            foreach (var outcome in outcomes)
            {
                if (ReplayDirectPath(new Owners(template, variantId, settings), outcome.ExitEntityId))
                    return new BoardSolverReport("Solved", "Direct route replay=Passed", replayed: true);
            }
            foreach (var outcome in outcomes)
            {
                int remainingMilliseconds = Math.Max(0, 10000 - (int)Math.Min(10000, clock.ElapsedMilliseconds));
                report = Solve(json, variantId, outcome.Id, settings, maxDepth: 128, maxMilliseconds: remainingMilliseconds);
                if (report.Status == "Solved" && report.Replayed) return report;
                if (report.Status != "NoSolution" && incomplete == null) incomplete = report;
                if (remainingMilliseconds == 0 || clock.ElapsedMilliseconds >= 10000)
                    return incomplete ?? new BoardSolverReport("TimeLimit", "The board-wide playability check exhausted its time budget.");
            }
            return incomplete ?? report ?? new BoardSolverReport("Invalid", "No eligible exit is declared.");
        }

        private static bool ReplayDirectPath(Owners owners, EntityId exitId)
        {
            if (!owners.Board.TryGetEntity(exitId, out BoardEntityState exit)) return false;
            GridPosition start = owners.Landmark.Blueprint.Start;
            var parents = new Dictionary<GridPosition, GridPosition>();
            var frontier = new Queue<GridPosition>();
            parents.Add(start, start);
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                GridPosition current = frontier.Dequeue();
                if (current.Equals(exit.Position)) break;
                foreach (Direction direction in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
                {
                    GridPosition next = current.Move(direction);
                    if (parents.ContainsKey(next) || !owners.Board.TryGetEntity(BoardLayer.Terrain, next, out BoardEntityState terrain) ||
                        !terrain.Definition.Traits.IsWalkable || !next.Equals(exit.Position) && terrain.Definition.Traits.IsExit ||
                        owners.Board.TryGetEntity(BoardLayer.Obstacle, next, out BoardEntityState obstacle) && !obstacle.Definition.Traits.IsWalkable) continue;
                    parents.Add(next, current);
                    frontier.Enqueue(next);
                }
            }
            if (!parents.ContainsKey(exit.Position)) return false;
            var path = new Stack<GridPosition>();
            for (GridPosition cell = exit.Position; !cell.Equals(start); cell = parents[cell]) path.Push(cell);
            GridPosition position = start;
            while (path.Count > 0)
            {
                GridPosition next = path.Pop();
                Direction direction = next.X > position.X ? Direction.East : next.X < position.X ? Direction.West :
                    next.Y > position.Y ? Direction.North : Direction.South;
                TurnResult turn = owners.Controller.Resolve(new MoveCommand(direction));
                if (!turn.Accepted || owners.Run.Health <= 0) return false;
                if (turn.Events.OfType<ExitReachedEvent>().Any(reached => reached.ExitId.Equals(exitId))) return true;
                if (owners.Controller.IsTerminal) return false;
                position = next;
            }
            return false;
        }

        private sealed class Owners
        {
            internal readonly RunState Run;
            internal readonly LandmarkBoard Landmark;
            internal readonly BoardState Board;
            internal readonly EntityId Player;
            internal readonly ShamblerState[] Enemies;
            internal readonly TurnController Controller;

            internal Owners(LandmarkTemplate template, string variantId, BoardTestPlaySettings settings)
            {
                Run = new RunState("authoring-solver", settings.Seed, new RunStateConfiguration(settings.Health, settings.Food, 0, "authoring.start"));
                Run.StartLeg("authoring.route", "authoring.start", "authoring.goal",
                    new[] { new RouteSegmentState("authoring.segment", template.Config.Id, template.Config.Version) });
                if (settings.EquipAxe) Run.EquipTool(0, Run.CreateToolInstance(ToolContentCatalog.Load()["axe.basic"]));
                Landmark = template.CreateBoard(BoardAddressFactory.CreateFromState(Run), variantId);
                if (!Landmark.Validate(OrdinaryBoardContent.CreateAuthoringCatalog()).IsValid)
                    throw new ArgumentException("The selected variant is invalid.");
                Board = Landmark.CreateState();
                Player = Board.GetEntities().Single(entity => entity.Definition.Kind.Equals(EntityKind.Player)).Id;
                foreach (var spawn in Landmark.Blueprint.Items.Where(item => item.ResourceFamilyId == "tools"))
                {
                    ToolInstanceState tool = Run.CreateToolInstance(ToolContentCatalog.Load()[spawn.ItemDefinitionId]);
                    if (!Board.TryBindGroundTool(new GroundToolState(spawn.EntityId, tool.InstanceId, tool.DefinitionId, tool.Charges, spawn.SourceId)))
                        throw new ArgumentException("Ground tool binding failed.");
                }
                Enemies = Board.GetEntities().Where(entity => entity.Definition.Kind.Equals(EntityKind.Enemy)).Select(entity =>
                {
                    if (!OrdinaryBoardContent.CreateAuthoringCatalog().TryGetShambler(entity.Definition.ContentId, out ShamblerDefinition rule))
                        throw new NotSupportedException("Unsupported enemy: " + entity.Definition.ContentId);
                    return new ShamblerState(entity.Id, rule);
                }).ToArray();
                var phases = new MixedEnemyTurnPhases(Enemies, Array.Empty<ListenerState>());
                phases.PlanInitialIntents(Board, Player);
                var clearing = AxeContent.LoadRules();
                var digging = ShovelContent.LoadRules();
                Controller = new TurnController(Board, Run, Player, new TurnResolver(ActionNoiseProfile.Prototype,
                    ShovelContent.CreateInteractions(clearing, digging), ShovelContent.CreateTools(clearing, digging), new ToolPickupQuery()), phases.Handlers);
            }
        }
    }
}