using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;

namespace HallowBlaze.Core.Generation.Validation
{
    /// <summary>Contains a real-controller escape witness, or only failure of the bounded entry criterion, never a whole-board unsolvability claim.</summary>
    public sealed class BoardEntrySafetyResult
    {
        internal BoardEntrySafetyResult(IReadOnlyList<PlayerCommand> escape, int evaluated)
        {
            EscapeCommands = escape;
            EvaluatedCommands = evaluated;
        }

        /// <summary>Gets whether one surviving path reaches Manhattan distance three from the start or completes an exit within four accepted commands.</summary>
        public bool HasEscape => EscapeCommands != null;
        /// <summary>Gets the executable movement/Wait witness, or null when no path met the bounded criterion.</summary>
        public IReadOnlyList<PlayerCommand> EscapeCommands { get; }
        /// <summary>Gets the deterministic work count, bounded above by 780 controller calls.</summary>
        public int EvaluatedCommands { get; }
    }

    /// <summary>Checks only the first four Move/Wait commands with actual locked Shambler intents and costs; no optional tool or full-run survival is assumed.</summary>
    public static class BoardEntrySafety
    {
        /// <summary>Maximum accepted commands in the witness; exhaustion rejects this criterion rather than proving no later solution exists.</summary>
        public const int Horizon = 4;
        private static readonly PlayerCommand[] choices = { new MoveCommand(Direction.North), new MoveCommand(Direction.East),
            new MoveCommand(Direction.West), new MoveCommand(Direction.South), new WaitCommand() };

        /// <summary>Copies the initial board and explores a deterministic bounded command tree through the real resolver and enemy phases.</summary>
        /// <param name="blueprint">Initial ordinary layout; never modified.</param>
        /// <param name="catalog">Authoritative initial Shambler definitions.</param>
        /// <param name="health">Actual health for diagnostics; the production criterion uses one HP, so any hit fails.</param>
        /// <param name="food">Starting Food; production uses five, enough to pay all four accepted costs without mandatory loot.</param>
        /// <returns>A legal surviving witness or an explicit bounded-criterion failure with its work count.</returns>
        /// <exception cref="ArgumentNullException">A required input is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Starting resources are not positive.</exception>
        /// <exception cref="ArgumentException">An actor lacks the ordinary Shambler rule.</exception>
        public static BoardEntrySafetyResult Check(BoardBlueprint blueprint, BoardValidationCatalog catalog, int health = 1, int food = 5)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (health <= 0) throw new ArgumentOutOfRangeException(nameof(health));
            if (food <= 0) throw new ArgumentOutOfRangeException(nameof(food));
            BoardState board = blueprint.CreateState();
            BoardEntityState player = board.GetEntities().Single(entity => entity.Definition.Kind.Equals(EntityKind.Player));
            var enemies = new List<ShamblerState>();
            var planner = new ShamblerPlanner();
            foreach (BoardEntityState actor in board.GetEntities())
                if (actor.Definition.Layer == BoardLayer.Actor && !actor.Id.Equals(player.Id))
                {
                    if (!actor.Definition.Kind.Equals(EntityKind.Enemy) ||
                        !catalog.TryGetShambler(actor.Definition.ContentId, out ShamblerDefinition definition))
                        throw new ArgumentException("Entry safety requires a known Shambler for every enemy.", nameof(catalog));
                    var enemy = new ShamblerState(actor.Id, definition);
                    enemy.LockIntent(planner.Plan(board, enemy, player.Id));
                    enemies.Add(enemy);
                }
            int evaluated = 0;
            IReadOnlyList<PlayerCommand> Search(BoardState parentBoard, int parentHealth, int parentFood,
                IReadOnlyList<ShamblerState> parentEnemies, List<PlayerCommand> path)
            {
                foreach (PlayerCommand command in choices)
                {
                    BoardState child = parentBoard.Clone();
                    var run = new RunState(blueprint.Address.RunId, 0,
                        new RunStateConfiguration(parentHealth, parentFood, 0, "entry-safety"));
                    ShamblerState[] roster = parentEnemies.Select(source =>
                    {
                        var copy = new ShamblerState(source.ActorId, source.Definition);
                        if (source.Phase == ShamblerPhase.Rest)
                        {
                            copy.LockIntent(new EnemyIntent(source.ActorId, EnemyIntentKind.Wait));
                            copy.ConsumeIntent();
                        }
                        copy.LockIntent(source.LockedIntent);
                        return copy;
                    }).ToArray();
                    var phases = new EnemyBatchTurnPhases(roster);
                    var controller = new TurnController(child, run, player.Id, new TurnResolver(), phases.Handlers);
                    evaluated++;
                    TurnResult result = controller.Resolve(command);
                    if (!result.Accepted || run.Health <= 0 || run.Food <= 0) continue;
                    var next = new List<PlayerCommand>(path) { command };
                    if (result.Events.OfType<ExitReachedEvent>().Any() ||
                        child.TryGetEntity(player.Id, out BoardEntityState moved) && moved.Position.ManhattanDistance(blueprint.Start) >= 3)
                        return next.AsReadOnly();
                    if (controller.IsTerminal || next.Count == Horizon) continue;
                    IReadOnlyList<PlayerCommand> witness = Search(child, run.Health, run.Food, roster, next);
                    if (witness != null) return witness;
                }
                return null;
            }
            IReadOnlyList<PlayerCommand> escape = Search(board, health, food, enemies, new List<PlayerCommand>());
            return new BoardEntrySafetyResult(escape, evaluated);
        }
    }
}