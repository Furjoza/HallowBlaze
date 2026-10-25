using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;

namespace HallowBlaze.Core.Generation.Landmarks
{
    /// <summary>Distinguishes a found path, exhaustive failure and incomplete bounded searches.</summary>
    public enum LandmarkSolverStatus
    {
        /// <summary>The real controller emitted the selected exit.</summary>
        Solved,
        /// <summary>All reachable nonterminal modeled states were exhausted.</summary>
        NoSolution,
        /// <summary>The unique-state budget prevented further search.</summary>
        StateLimit,
        /// <summary>The accepted-command depth prevented further search.</summary>
        DepthLimit,
        /// <summary>The elapsed-time budget prevented further search.</summary>
        TimeLimit
    }

    /// <summary>One in-memory puzzle decision, not a portable replay command or hint.</summary>
    public sealed class LandmarkSolverStep
    {
        private readonly PlayerCommand command;
        internal LandmarkSolverStep(PlayerCommand command) { this.command = command; }

        /// <summary>Rebinds pickup identity to an equivalent fresh board; other immutable decisions retain their preconditions.</summary>
        /// <exception cref="InvalidOperationException">The fresh state no longer offers the exact pickup decision.</exception>
        public PlayerCommand CreateCommand(BoardState board, RunState run, EntityId playerId)
        {
            if (!(command is PickupToolCommand pickup)) return command;
            foreach (ToolPickupOption option in new ToolPickupQuery().GetOptions(board, run, playerId))
                foreach (PickupToolCommand choice in option.Choices)
                    if (choice.Tool.EntityId.Equals(pickup.Tool.EntityId) && choice.Tool.InstanceId == pickup.Tool.InstanceId &&
                        choice.Tool.DefinitionId == pickup.Tool.DefinitionId && choice.Tool.Charges == pickup.Tool.Charges &&
                        choice.Tool.SourceId == pickup.Tool.SourceId && choice.TargetPosition.Equals(pickup.TargetPosition) &&
                        choice.SlotIndex == pickup.SlotIndex && choice.ExpectedSlotInstanceId == pickup.ExpectedSlotInstanceId &&
                        choice.ExpectedSlotDefinitionId == pickup.ExpectedSlotDefinitionId && choice.ExpectedSlotCharges == pickup.ExpectedSlotCharges)
                        return choice;
            throw new InvalidOperationException("The solver pickup decision is not available on this state.");
        }

        /// <summary>Describes the selected command for a local validation handoff.</summary>
        public override string ToString()
        {
            if (command is MoveCommand move) return "Move " + move.Direction;
            if (command is InteractCommand interact) return interact.ActionId + " " + interact.TargetId;
            if (command is UseToolCommand tool) return tool.ActionId + " " + tool.TargetId + " slot=" + tool.SlotIndex;
            if (command is PickupToolCommand pickup) return "Pickup " + pickup.Tool.EntityId + " slot=" + pickup.SlotIndex;
            return "Wait";
        }
    }

    /// <summary>Detached search evidence retaining reproducible template/address context and an executable local path.</summary>
    public sealed class LandmarkSolverResult
    {
        internal LandmarkSolverResult(LandmarkBoard landmark, string outcomeId, LandmarkSolverStatus status,
            int visited, int expanded, long elapsed, IReadOnlyList<LandmarkSolverStep> path)
        {
            TemplateId = landmark.TemplateId;
            TemplateVersion = landmark.TemplateVersion;
            VariantId = landmark.VariantId;
            Address = landmark.Blueprint.Address;
            OutcomeId = outcomeId;
            Status = status;
            VisitedStates = visited;
            ExpandedStates = expanded;
            ElapsedMilliseconds = elapsed;
            SolutionPath = path;
        }

        /// <summary>Gets the stable template ID, including on incomplete or failed search.</summary>
        public string TemplateId { get; }
        /// <summary>Gets the authored template version.</summary>
        public int TemplateVersion { get; }
        /// <summary>Gets the selected variant ID.</summary>
        public string VariantId { get; }
        /// <summary>Gets the complete board identity, including its seed.</summary>
        public BoardAddress Address { get; }
        /// <summary>Gets the requested named exit result.</summary>
        public string OutcomeId { get; }
        /// <summary>Gets whether this is a proof of existence, exhaustive failure, or an incomplete search.</summary>
        public LandmarkSolverStatus Status { get; }
        /// <summary>Gets the number of unique admitted nonterminal states, including the root.</summary>
        public int VisitedStates { get; }
        /// <summary>Gets the number of dequeued states whose commands were considered.</summary>
        public int ExpandedStates { get; }
        /// <summary>Gets measured wall time; this is diagnostic, not deterministic puzzle state.</summary>
        public long ElapsedMilliseconds { get; }
        /// <summary>Gets accepted decisions in execution order; empty on failure.</summary>
        public IReadOnlyList<LandmarkSolverStep> SolutionPath { get; }
        /// <summary>Formats a minimal reproduction identity without exporting player saves.</summary>
        public override string ToString() => TemplateId + "/" + TemplateVersion + "/" + VariantId + " " +
            Address + " outcome=" + OutcomeId + " " + Status + " visited=" + VisitedStates +
            " expanded=" + ExpandedStates + " elapsedMs=" + ElapsedMilliseconds;
    }

    /// <summary>Offline bounded BFS through real turn rules, with independent board/run/enemy owners per branch.</summary>
    public sealed class LandmarkSolver
    {
        private static readonly Direction[] directions = { Direction.North, Direction.East, Direction.West, Direction.South };
        private readonly LandmarkBoard landmark;
        private readonly EntityId playerId;
        private readonly Node root;
        private readonly InteractionQuery interactions;
        private readonly ToolUseQuery tools;
        private readonly ToolPickupQuery pickups = new ToolPickupQuery();

        /// <summary>Copies a concrete board boundary and its retained Shambler plans without planning again or touching caller state.</summary>
        /// <param name="landmark">Template identity and exit declarations for the concrete fixture.</param>
        /// <param name="board">Nonterminal concrete board, including allocated ground tools and derived gates; the player must not already occupy an exit.</param>
        /// <param name="run">Active resources/equipment; travel remains frozen throughout search.</param>
        /// <param name="playerId">The board-local player identity.</param>
        /// <param name="enemies">Exact Shambler definitions, phases and already retained intents.</param>
        /// <param name="catalog">Required for enemies; resolves their actual Shambler rule, never inferring it from kind or damage defaults.</param>
        /// <param name="clearingRules">Shared manual/axe rules, or null when the fixture contains no clearable obstacles.</param>
        /// <exception cref="NotSupportedException">The fixture contains mechanics outside movement, food, boulders, gates, clearing, axe pickup and Shambler rules.</exception>
        /// <exception cref="ArgumentException">Concrete identity, tool ownership, bindings or retained enemy state is inconsistent.</exception>
        public LandmarkSolver(LandmarkBoard landmark, BoardState board, RunState run, EntityId playerId,
            IEnumerable<ShamblerState> enemies, BoardValidationCatalog catalog = null, ObstacleClearingRules clearingRules = null)
        {
            this.landmark = landmark ?? throw new ArgumentNullException(nameof(landmark));
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            this.playerId = playerId;
            var actions = new List<IInteractionAction> { new CollectInteractionAction(), new BoulderPushAction() };
            if (clearingRules != null) actions.Add(new ClearObstacleAction(clearingRules, false));
            interactions = new InteractionQuery(actions);
            tools = clearingRules == null ? new ToolUseQuery() : new ToolUseQuery(new[]
            {
                new ToolAction(ObstacleClearingRules.AxeDefinitionId, new ClearObstacleAction(clearingRules, true),
                    "Chop: clear obstacle", clearingRules.AxeNoiseStrength, clearingRules.NoiseLifetimeTurns,
                    ObstacleClearingRules.AxeNoiseFactId)
            });
            root = new Node(board, run, enemies.ToArray(), null, null);
            if (run.Status != RunStatus.Active || run.Health <= 0 || run.Food <= 0 ||
                run.RunId != landmark.Blueprint.Address.RunId || !board.TryGetEntity(playerId, out BoardEntityState player) ||
                player.Definition.Layer != BoardLayer.Actor || !player.Definition.Kind.Equals(EntityKind.Player) ||
                board.TryGetEntity(BoardLayer.Terrain, player.Position, out BoardEntityState groundUnderPlayer) && groundUnderPlayer.Definition.Traits.IsExit ||
                !board.Bounds.Equals(landmark.Blueprint.CreateState().Bounds))
                throw new ArgumentException("An active matching run and nonterminal player outside an exit are required.");
            if (root.Enemies.Any(enemy => enemy == null || enemy.LockedIntent == null) ||
                root.Enemies.Select(enemy => enemy.ActorId).Distinct().Count() != root.Enemies.Length ||
                board.GetEntities().Count(entity => entity.Definition.Kind.Equals(EntityKind.Enemy)) != root.Enemies.Length)
                throw new ArgumentException("Every enemy requires one explicit Shambler state and a retained intent.");
            foreach (ShamblerState enemy in root.Enemies)
            {
                if (!board.TryGetEntity(enemy.ActorId, out BoardEntityState actor) || actor.Definition.Layer != BoardLayer.Actor ||
                    !actor.Definition.Kind.Equals(EntityKind.Enemy)) throw new ArgumentException("Enemy roster does not match board actors.");
                if (catalog == null || !catalog.TryGetShambler(actor.Definition.ContentId, out ShamblerDefinition definition))
                    throw new NotSupportedException("An enemy has no supported Shambler rule: " + actor.Definition.ContentId);
                if (definition.Damage != enemy.Definition.Damage || enemy.LockedIntent.Kind == EnemyIntentKind.Investigate ||
                    enemy.LockedIntent.TargetId.HasValue && !enemy.LockedIntent.TargetId.Value.Equals(playerId))
                    throw new ArgumentException("Enemy definition or retained intent does not match the fixture.");
            }
            if (board.GetPits().Count != 0 || board.GetEntities().Any(entity =>
                board.GetCacheWork(entity.Id) != 0 || board.HasRevealedCache(entity.Id)))
                throw new NotSupportedException("This solver does not model pits or caches.");
            foreach (ToolSlotState slot in run.ToolSlots)
                if (!slot.IsEmpty && slot.Instance.DefinitionId != ObstacleClearingRules.AxeDefinitionId)
                    throw new NotSupportedException("Only axe equipment is supported.");
            foreach (BoardEntityState entity in board.GetEntities())
            {
                if (entity.Definition.Layer == BoardLayer.Actor && !entity.Id.Equals(playerId) &&
                    !root.Enemies.Any(enemy => enemy.ActorId.Equals(entity.Id)))
                    throw new NotSupportedException("Unsupported actor: " + entity.Id);
                if (entity.Definition.ContentId == "legacy.buried-food" || entity.Definition.ContentId == "shovel.diggable" ||
                    entity.Definition.Traits.IsInteractable && entity.Definition.Layer != BoardLayer.Item &&
                    !actions.Any(action => action.Supports(entity)))
                    throw new NotSupportedException("Unsupported interaction: " + entity.Definition.ContentId);
                if (entity.Definition.Layer != BoardLayer.Item) continue;
                if (!board.TryGetGroundTool(entity.Id, out GroundToolState ground))
                {
                    if (entity.Definition.AutomaticFoodReward <= 0)
                        throw new NotSupportedException("Every non-food item requires a concrete axe binding.");
                    continue;
                }
                if (ground.DefinitionId != ObstacleClearingRules.AxeDefinitionId)
                    throw new NotSupportedException("Only ground axes are supported.");
                if (!run.CanReceiveTool(0, new ToolInstanceState(ground.InstanceId, ground.DefinitionId, ground.Charges)))
                    throw new ArgumentException("Ground tool must be allocated by this run and not equipped.");
            }
            if (board.GetPressurePlates().Count != landmark.Plates.Count || board.GetGates().Count != landmark.Gates.Count ||
                !board.GetPressurePlates().All(plate => landmark.Plates.Any(expected => expected.TerrainId.Equals(plate.Definition.TerrainId) &&
                    expected.LinkId == plate.Definition.LinkId && expected.Occupants == plate.Definition.Occupants)) ||
                !board.GetGates().All(gate => landmark.Gates.Any(expected => expected.TerrainId.Equals(gate.Definition.TerrainId) &&
                    expected.LinkId == gate.Definition.LinkId && expected.Rule == gate.Definition.Rule)))
                throw new ArgumentException("Concrete pressure/gate bindings must match the selected template.");
            root = root.Copy();
        }

        /// <summary>Finds a shortest accepted-command path within budgets; a budget stop never means NoSolution.</summary>
        /// <param name="outcomeId">One declared outcome, including optional exits when explicitly requested.</param>
        /// <param name="maxVisitedStates">Positive cap on admitted nonterminal states, including the root.</param>
        /// <param name="maxPathDepth">Nonnegative maximum accepted-command count.</param>
        /// <param name="maxElapsedMilliseconds">Nonnegative wall-time budget; zero deliberately stops before expansion.</param>
        public LandmarkSolverResult Solve(string outcomeId, int maxVisitedStates = 50000,
            int maxPathDepth = 32, int maxElapsedMilliseconds = 10000)
        {
            LandmarkOutcomeDefinition goal = landmark.Outcomes.SingleOrDefault(outcome => outcome.Id == outcomeId);
            if (goal == null) throw new ArgumentException("Unknown landmark outcome.", nameof(outcomeId));
            if (maxVisitedStates < 1) throw new ArgumentOutOfRangeException(nameof(maxVisitedStates));
            if (maxPathDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxPathDepth));
            if (maxElapsedMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(maxElapsedMilliseconds));
            var clock = Stopwatch.StartNew();
            var frontier = new Queue<Node>();
            var visited = new HashSet<LandmarkSolverStateKey>();
            frontier.Enqueue(root);
            visited.Add(Key(root));
            int expanded = 0;
            bool depthLimited = false;
            LandmarkSolverResult Finish(LandmarkSolverStatus status, Node solution = null) =>
                new LandmarkSolverResult(landmark, outcomeId, status, visited.Count, expanded,
                    clock.ElapsedMilliseconds, Path(solution));
            while (frontier.Count > 0)
            {
                if (clock.ElapsedMilliseconds >= maxElapsedMilliseconds) return Finish(LandmarkSolverStatus.TimeLimit);
                Node parent = frontier.Dequeue();
                expanded++;
                foreach (PlayerCommand command in Commands(parent))
                {
                    if (clock.ElapsedMilliseconds >= maxElapsedMilliseconds) return Finish(LandmarkSolverStatus.TimeLimit);
                    Node child = parent.Copy(new LandmarkSolverStep(command));
                    var phases = new EnemyBatchTurnPhases(child.Enemies);
                    var controller = new TurnController(child.Board, child.Run, playerId,
                        new TurnResolver(ActionNoiseProfile.Prototype, interactions, tools, pickups), phases.Handlers);
                    TurnResult result = controller.Resolve(child.Step.CreateCommand(child.Board, child.Run, playerId));
                    if (clock.ElapsedMilliseconds >= maxElapsedMilliseconds) return Finish(LandmarkSolverStatus.TimeLimit);
                    if (!result.Accepted) continue;
                    bool solved = result.Events.OfType<ExitReachedEvent>().Any(exit =>
                        exit.PlayerId.Equals(playerId) && exit.ExitId.Equals(goal.ExitEntityId));
                    if (solved && child.Depth <= maxPathDepth) return Finish(LandmarkSolverStatus.Solved, child);
                    if (controller.IsTerminal)
                    {
                        depthLimited |= solved && child.Depth > maxPathDepth;
                        continue;
                    }
                    LandmarkSolverStateKey key = Key(child);
                    if (visited.Contains(key)) continue;
                    if (child.Depth > maxPathDepth) { depthLimited = true; continue; }
                    if (visited.Count == maxVisitedStates) return Finish(LandmarkSolverStatus.StateLimit);
                    visited.Add(key);
                    frontier.Enqueue(child);
                }
            }
            return Finish(depthLimited ? LandmarkSolverStatus.DepthLimit : LandmarkSolverStatus.NoSolution);
        }

        private LandmarkSolverStateKey Key(Node node) => new LandmarkSolverStateKey(node.Board, node.Run, playerId, node.Enemies);

        private IEnumerable<PlayerCommand> Commands(Node node)
        {
            foreach (Direction direction in directions) yield return new MoveCommand(direction);
            yield return new WaitCommand();
            foreach (InteractionOption option in interactions.GetOptions(node.Board, node.Run, playerId))
                if (option.IsAvailable) yield return option.CreateCommand();
            foreach (ToolUseOption option in tools.GetOptions(node.Board, node.Run, playerId))
                if (option.IsAvailable) yield return option.CreateCommand();
            foreach (ToolPickupOption option in pickups.GetOptions(node.Board, node.Run, playerId))
                foreach (PickupToolCommand choice in option.Choices) yield return choice;
        }

        private static IReadOnlyList<LandmarkSolverStep> Path(Node node)
        {
            var path = new List<LandmarkSolverStep>();
            for (; node?.Parent != null; node = node.Parent) path.Add(node.Step);
            path.Reverse();
            return path.AsReadOnly();
        }

        private sealed class Node
        {
            internal readonly BoardState Board;
            internal readonly RunState Run;
            internal readonly ShamblerState[] Enemies;
            internal readonly Node Parent;
            internal readonly LandmarkSolverStep Step;
            internal readonly int Depth;
            internal Node(BoardState board, RunState run, ShamblerState[] enemies, Node parent, LandmarkSolverStep step)
            {
                Board = board; Run = run; Enemies = enemies; Parent = parent; Step = step;
                Depth = parent == null ? 0 : parent.Depth + 1;
            }

            internal Node Copy(LandmarkSolverStep step = null)
            {
                var run = new RunState(Run.RunId, Run.RunSeed,
                    new RunStateConfiguration(Run.Health, Run.Food, Run.CompletedDayCount, Run.WorldNodeId),
                    Run.RandomAlgorithmId, Run.RandomAlgorithmVersion);
                run.RestoreTravelState(Run.CompletedDayCount, Run.WorldNodeId, Run.TraversalSequence, Run.TravelHistory, Run.ActiveLeg);
                run.RestoreLandmarkState(Run.ActiveLandmark, Run.CompletedLandmarks);
                run.RestoreToolState(Run.ToolInstanceSequence, Run.ToolSlots.Select(slot => slot.Instance));
                ShamblerState[] enemies = Enemies.Select(source =>
                {
                    var copy = new ShamblerState(source.ActorId, source.Definition);
                    if (source.Phase == ShamblerPhase.Rest)
                    {
                        copy.LockIntent(new EnemyIntent(source.ActorId, EnemyIntentKind.Wait));
                        copy.ConsumeIntent();
                    }
                    if (source.LockedIntent != null) copy.LockIntent(source.LockedIntent);
                    return copy;
                }).ToArray();
                return new Node(Board.Clone(), run, enemies, step == null ? null : this, step);
            }
        }
    }
}