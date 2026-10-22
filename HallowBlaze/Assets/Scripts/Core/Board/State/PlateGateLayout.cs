using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.Board.State
{
    /// <summary>Declares which authoritative occupancy can press a terrain plate; items never count.</summary>
    public enum PlateOccupants
    {
        /// <summary>Any actor, including the player and enemies, presses the plate.</summary>
        Actors,
        /// <summary>Only an explicitly authored blocking interactive boulder presses the plate.</summary>
        Boulders,
        /// <summary>Either a supported actor or an authored boulder presses the plate.</summary>
        ActorsOrBoulders
    }

    /// <summary>Combines plates sharing a stable link ID without timers, latching or hidden connections.</summary>
    public enum GateRule
    {
        /// <summary>Every linked plate must be pressed.</summary>
        All,
        /// <summary>At least one linked plate must be pressed.</summary>
        Any
    }

    /// <summary>Binds one fixed walkable terrain plate to a visible stable link and explicit pressure rule.</summary>
    public sealed class PressurePlateDefinition
    {
        /// <summary>Creates immutable authoring data; entity existence and link completeness are validated against the board.</summary>
        public PressurePlateDefinition(Primitives.EntityId terrainId, string linkId, PlateOccupants occupants = PlateOccupants.ActorsOrBoulders)
        {
            PlateGateValidator.RequireLink(linkId);
            if (!Enum.IsDefined(typeof(PlateOccupants), occupants)) throw new ArgumentOutOfRangeException(nameof(occupants));
            TerrainId = terrainId;
            LinkId = linkId;
            Occupants = occupants;
        }

        /// <summary>Gets the exact fixed terrain entity, not a Unity object or prefab identity.</summary>
        public Primitives.EntityId TerrainId { get; }
        /// <summary>Gets the stable textual connection exposed by presentation.</summary>
        public string LinkId { get; }
        /// <summary>Gets the only accepted pressure categories.</summary>
        public PlateOccupants Occupants { get; }
    }

    /// <summary>Binds one terrain gate to linked plates; its walkability is derived, never authored as a mutable flag.</summary>
    public sealed class GateDefinition
    {
        /// <summary>Creates immutable link/combination data; a gate requires at least one matching plate.</summary>
        public GateDefinition(Primitives.EntityId terrainId, string linkId, GateRule rule = GateRule.All)
        {
            PlateGateValidator.RequireLink(linkId);
            if (!Enum.IsDefined(typeof(GateRule), rule)) throw new ArgumentOutOfRangeException(nameof(rule));
            TerrainId = terrainId;
            LinkId = linkId;
            Rule = rule;
        }

        /// <summary>Gets the fixed terrain entity whose runtime walkability follows the linked plates.</summary>
        public Primitives.EntityId TerrainId { get; }
        /// <summary>Gets the stable visible connection.</summary>
        public string LinkId { get; }
        /// <summary>Gets whether every or any linked plate must be pressed.</summary>
        public GateRule Rule { get; }
    }

    /// <summary>Validates explicit plate/gate bindings without publishing or mutating any board state.</summary>
    public static class PlateGateValidator
    {
        /// <summary>Gets the exact walkable, non-exit plate terrain accepted by this mechanic.</summary>
        public static BoardEntityDefinition PlateContent { get; } = new BoardEntityDefinition(BoardLayer.Terrain,
            new Primitives.EntityKind("terrain"), "landmark.plate", new BoardEntityTraits(true, false, false));
        /// <summary>Gets the closed, non-exit gate terrain; an open gate retains identity and changes only walkability.</summary>
        public static BoardEntityDefinition GateContent { get; } = new BoardEntityDefinition(BoardLayer.Terrain,
            new Primitives.EntityKind("terrain"), "landmark.gate", new BoardEntityTraits(false, false, false));

        /// <summary>Collects duplicate, missing, unsupported and unpaired-link errors in supplied order without mutation.</summary>
        /// <returns>Detached diagnostics; an empty list permits atomic layout publication.</returns>
        public static IReadOnlyList<string> Validate(BoardState board, IEnumerable<PressurePlateDefinition> plates, IEnumerable<GateDefinition> gates)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (plates == null) throw new ArgumentNullException(nameof(plates));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            var errors = new List<string>();
            var identities = new HashSet<Primitives.EntityId>();
            var plateLinks = new SortedSet<string>(StringComparer.Ordinal);
            var gateLinks = new SortedSet<string>(StringComparer.Ordinal);
            foreach (PressurePlateDefinition plate in plates)
            {
                if (plate == null) { errors.Add("Null pressure plate binding."); continue; }
                if (!identities.Add(plate.TerrainId)) errors.Add("Duplicate terrain binding: " + plate.TerrainId);
                if (!board.TryGetEntity(plate.TerrainId, out BoardEntityState entity) || !entity.Definition.Equals(PlateContent))
                    errors.Add("Unsupported pressure plate terrain: " + plate.TerrainId);
                plateLinks.Add(plate.LinkId);
            }
            foreach (GateDefinition gate in gates)
            {
                if (gate == null) { errors.Add("Null gate binding."); continue; }
                if (!identities.Add(gate.TerrainId)) errors.Add("Duplicate terrain binding: " + gate.TerrainId);
                if (!board.TryGetEntity(gate.TerrainId, out BoardEntityState entity) || !IsGate(entity))
                    errors.Add("Unsupported gate terrain: " + gate.TerrainId);
                gateLinks.Add(gate.LinkId);
            }
            foreach (string link in plateLinks) if (!gateLinks.Contains(link)) errors.Add("Plate link has no gate: " + link);
            foreach (string link in gateLinks) if (!plateLinks.Contains(link)) errors.Add("Gate link has no plate: " + link);
            foreach (BoardEntityState entity in board.GetEntities())
                if ((entity.Definition.ContentId == PlateContent.ContentId || entity.Definition.ContentId == GateContent.ContentId) && !identities.Contains(entity.Id))
                    errors.Add("Unbound plate/gate terrain: " + entity.Id);
            return errors.AsReadOnly();
        }

        internal static bool IsGate(BoardEntityState entity) => entity.Definition.Layer == BoardLayer.Terrain &&
            entity.Definition.Kind.Equals(GateContent.Kind) && entity.Definition.ContentId == GateContent.ContentId &&
            !entity.Definition.Traits.IsExit && !entity.Definition.Traits.IsInteractable && entity.Definition.AutomaticFoodReward == 0;

        internal static void RequireLink(string linkId)
        {
            if (string.IsNullOrWhiteSpace(linkId) || linkId != linkId.Trim())
                throw new ArgumentException("A plate/gate link requires a stable nonempty textual ID.", nameof(linkId));
        }
    }
}