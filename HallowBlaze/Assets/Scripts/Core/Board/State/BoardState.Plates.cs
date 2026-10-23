using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;

namespace HallowBlaze.Core.Board.State
{
    /// <summary>Immutable pressure observation captured at one successful board mutation.</summary>
    public sealed class PressurePlateState
    {
        internal PressurePlateState(PressurePlateDefinition definition, GridPosition position, bool pressed)
        { Definition = definition; Position = position; IsPressed = pressed; }
        /// <summary>Gets the exact authored link and pressure rule.</summary>
        public PressurePlateDefinition Definition { get; }
        /// <summary>Gets the fixed plate cell.</summary>
        public GridPosition Position { get; }
        /// <summary>Gets occupancy-derived pressure, independent of a view or physics.</summary>
        public bool IsPressed { get; }
    }

    /// <summary>Immutable gate observation; IsOpen equals the gate terrain's current walkability.</summary>
    public sealed class GateState
    {
        internal GateState(GateDefinition definition, GridPosition position, bool open)
        { Definition = definition; Position = position; IsOpen = open; }
        /// <summary>Gets the authored link and All/Any rule.</summary>
        public GateDefinition Definition { get; }
        /// <summary>Gets the fixed gate cell.</summary>
        public GridPosition Position { get; }
        /// <summary>Gets whether entry is currently legal; closing never displaces or damages existing occupants.</summary>
        public bool IsOpen { get; }
    }

    /// <summary>One ordered pressure or gate transition retained until the turn layer records its event.</summary>
    public sealed class PlateGateChange
    {
        internal PlateGateChange(PressurePlateState plate) { Plate = plate; }
        internal PlateGateChange(GateState gate) { Gate = gate; }
        /// <summary>Gets the changed plate, or null for a gate transition.</summary>
        public PressurePlateState Plate { get; }
        /// <summary>Gets the changed gate, or null for a pressure transition.</summary>
        public GateState Gate { get; }
    }

    public sealed partial class BoardState
    {
        private readonly List<PressurePlateDefinition> plateDefinitions = new List<PressurePlateDefinition>();
        private readonly List<GateDefinition> gateDefinitions = new List<GateDefinition>();
        private readonly Dictionary<EntityId, PressurePlateState> pressurePlates = new Dictionary<EntityId, PressurePlateState>();
        private readonly Dictionary<EntityId, GateState> plateGates = new Dictionary<EntityId, GateState>();
        private readonly List<PlateGateChange> plateGateChanges = new List<PlateGateChange>();
        private bool plateGatesConfigured;

        /// <summary>Validates and publishes fixed terrain bindings once, deriving initial pressure without gameplay events or resources.</summary>
        /// <remarks>Subsequent successful add/remove/move/push operations recompute immediately. Failed publication changes nothing.
        /// Multiple gates may share a link and independently select All/Any. Items and arbitrary obstacles never press plates.</remarks>
        /// <exception cref="ArgumentException">A binding, content definition or link is invalid.</exception>
        /// <exception cref="InvalidOperationException">Bindings were already published.</exception>
        public void ConfigurePlateGates(IEnumerable<PressurePlateDefinition> plates, IEnumerable<GateDefinition> gates)
        {
            if (plateGatesConfigured) throw new InvalidOperationException("Plate/gate bindings are fixed for this board.");
            var suppliedPlates = new List<PressurePlateDefinition>(plates ?? throw new ArgumentNullException(nameof(plates)));
            var suppliedGates = new List<GateDefinition>(gates ?? throw new ArgumentNullException(nameof(gates)));
            IReadOnlyList<string> errors = PlateGateValidator.Validate(this, suppliedPlates, suppliedGates);
            if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));
            suppliedPlates.Sort((left, right) => left.TerrainId.CompareTo(right.TerrainId));
            suppliedGates.Sort((left, right) => left.TerrainId.CompareTo(right.TerrainId));
            plateDefinitions.AddRange(suppliedPlates);
            gateDefinitions.AddRange(suppliedGates);
            plateGatesConfigured = true;
            RecomputePlateGates();
            plateGateChanges.Clear();
        }

        /// <summary>Reads detached current pressure snapshots in terrain-ID order without changing state.</summary>
        public IReadOnlyList<PressurePlateState> GetPressurePlates()
        {
            var result = new List<PressurePlateState>();
            foreach (PressurePlateDefinition definition in plateDefinitions) result.Add(pressurePlates[definition.TerrainId]);
            return result.AsReadOnly();
        }

        /// <summary>Reads detached current gate snapshots in terrain-ID order, not a cached path result.</summary>
        public IReadOnlyList<GateState> GetGates()
        {
            var result = new List<GateState>();
            foreach (GateDefinition definition in gateDefinitions) result.Add(plateGates[definition.TerrainId]);
            return result.AsReadOnly();
        }

        /// <summary>Takes committed transitions once for ordered event recording; it never recomputes or changes pressure/walkability.</summary>
        /// <returns>Mutation order, with changed plates before changed gates and terrain-ID order within each mutation.</returns>
        public IReadOnlyList<PlateGateChange> TakePlateGateChanges()
        {
            var result = new List<PlateGateChange>(plateGateChanges);
            plateGateChanges.Clear();
            return result.AsReadOnly();
        }

        private bool IsBoundPlateGate(EntityId id) => pressurePlates.ContainsKey(id) || plateGates.ContainsKey(id);

        private void RecomputePlateGates()
        {
            foreach (PressurePlateDefinition definition in plateDefinitions)
            {
                BoardEntityState terrain = entitiesById[definition.TerrainId];
                bool actor = TryGetEntity(BoardLayer.Actor, terrain.Position, out _);
                bool boulder = TryGetEntity(BoardLayer.Obstacle, terrain.Position, out BoardEntityState obstacle) &&
                    obstacle.Definition.ContentId == "landmark.boulder" && obstacle.Definition.Kind.Equals(EntityKind.Obstacle) &&
                    !obstacle.Definition.Traits.IsWalkable && obstacle.Definition.Traits.IsInteractable && obstacle.Definition.AutomaticFoodReward == 0;
                bool pressed = definition.Occupants == PlateOccupants.Actors ? actor :
                    definition.Occupants == PlateOccupants.Boulders ? boulder : actor || boulder;
                if (pressurePlates.TryGetValue(definition.TerrainId, out PressurePlateState previous) && previous.IsPressed == pressed) continue;
                var state = new PressurePlateState(definition, terrain.Position, pressed);
                pressurePlates[definition.TerrainId] = state;
                plateGateChanges.Add(new PlateGateChange(state));
            }
            foreach (GateDefinition definition in gateDefinitions)
            {
                bool open = definition.Rule == GateRule.All;
                foreach (PressurePlateDefinition plate in plateDefinitions)
                {
                    if (plate.LinkId != definition.LinkId) continue;
                    bool pressed = pressurePlates[plate.TerrainId].IsPressed;
                    open = definition.Rule == GateRule.All ? open && pressed : open || pressed;
                }
                if (plateGates.TryGetValue(definition.TerrainId, out GateState previous) && previous.IsOpen == open) continue;
                BoardEntityState terrain = entitiesById[definition.TerrainId];
                entitiesById[terrain.Id] = new BoardEntityState(terrain.Id, new BoardEntityDefinition(BoardLayer.Terrain,
                    terrain.Definition.Kind, terrain.Definition.ContentId, new BoardEntityTraits(open, false, false)), terrain.Position);
                var state = new GateState(definition, terrain.Position, open);
                plateGates[definition.TerrainId] = state;
                plateGateChanges.Add(new PlateGateChange(state));
            }
        }
    }
}