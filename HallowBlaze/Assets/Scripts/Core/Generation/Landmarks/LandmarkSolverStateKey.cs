using System;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Resolution;

namespace HallowBlaze.Core.Generation.Landmarks
{
    /// <summary>
    /// Detached structural reachability key for one solver's fixed rules/travel context.
    /// Noise is excluded because supported Shambler rules never read it; Listeners, caches and pits are unsupported.
    /// This is neither a save snapshot nor a portable replay hash. Hash equality alone never establishes equality.
    /// </summary>
    public sealed class LandmarkSolverStateKey : IEquatable<LandmarkSolverStateKey>
    {
        private readonly object[] values;
        private readonly int hash;

        /// <summary>Copies all mutable inputs affecting the supported model's next command or exit reachability.</summary>
        /// <remarks>Compare keys only within the same fixed template, clearing/cost rules and frozen travel context.</remarks>
        public LandmarkSolverStateKey(BoardState board, RunState run, EntityId playerId, IEnumerable<ShamblerState> enemies)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            var data = new List<object> { board.Bounds, playerId, run.Health, run.Food, run.Status, run.ToolInstanceSequence };
            foreach (ToolSlotState slot in run.ToolSlots)
            {
                data.Add(slot.Instance?.InstanceId);
                data.Add(slot.Instance?.DefinitionId);
                data.Add(slot.Instance?.Charges);
            }
            IReadOnlyList<BoardEntityState> entities = board.GetEntities();
            data.Add(entities.Count);
            foreach (BoardEntityState entity in entities)
            {
                data.Add(entity.Id);
                data.Add(entity.Position);
                data.Add(entity.Definition);
                data.Add(board.GetObstacleWork(entity.Id));
                bool ground = board.TryGetGroundTool(entity.Id, out GroundToolState tool);
                data.Add(ground);
                if (!ground) continue;
                data.Add(tool.InstanceId); data.Add(tool.DefinitionId); data.Add(tool.Charges); data.Add(tool.SourceId);
            }
            IReadOnlyList<PressurePlateState> plates = board.GetPressurePlates();
            data.Add(plates.Count);
            foreach (PressurePlateState plate in plates)
            {
                data.Add(plate.Definition.TerrainId); data.Add(plate.Definition.LinkId); data.Add(plate.Definition.Occupants);
                data.Add(plate.Position); data.Add(plate.IsPressed);
            }
            IReadOnlyList<GateState> gates = board.GetGates();
            data.Add(gates.Count);
            foreach (GateState gate in gates)
            {
                data.Add(gate.Definition.TerrainId); data.Add(gate.Definition.LinkId); data.Add(gate.Definition.Rule);
                data.Add(gate.Position); data.Add(gate.IsOpen);
            }
            ShamblerState[] roster = enemies.OrderBy(enemy => enemy.ActorId.Value).ToArray();
            data.Add(roster.Length);
            foreach (ShamblerState enemy in roster)
            {
                data.Add(enemy.ActorId); data.Add(enemy.Definition.Damage); data.Add(enemy.Phase);
                var intent = enemy.LockedIntent;
                data.Add(intent != null);
                if (intent == null) continue;
                data.Add(intent.ActorId); data.Add(intent.Kind); data.Add(intent.TargetPosition); data.Add(intent.TargetId);
                data.Add(intent.AttackOnPlayerEntry); data.Add(intent.InvestigationTarget); data.Add(intent.NoiseId); data.Add(intent.SourcePosition);
            }
            values = data.ToArray();
            unchecked
            {
                hash = 17;
                foreach (object value in values) hash = hash * 31 + (value?.GetHashCode() ?? 0);
            }
        }

        /// <summary>Compares the complete captured structure, even when two hashes collide.</summary>
        public bool Equals(LandmarkSolverStateKey other) => other != null && values.SequenceEqual(other.values);
        /// <inheritdoc />
        public override bool Equals(object obj) => obj is LandmarkSolverStateKey other && Equals(other);
        /// <inheritdoc />
        public override int GetHashCode() => hash;
    }
}