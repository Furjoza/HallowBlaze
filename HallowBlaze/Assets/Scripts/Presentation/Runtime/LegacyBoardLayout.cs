using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Identifies every content category emitted by the current legacy board generator.</summary>
    public enum LegacyBoardContentKind
    {
        /// <summary>Represents content for which no supported classification exists.</summary>
        Unknown = 0,

        /// <summary>The scene-owned player view.</summary>
        Player,

        /// <summary>A walkable in-bounds floor tile.</summary>
        Floor,

        /// <summary>A decorative boundary tile outside gameplay bounds.</summary>
        OuterWall,

        /// <summary>A blocking inner wall.</summary>
        Wall,

        /// <summary>A board exit trigger.</summary>
        Exit,

        /// <summary>A food pickup restoring ten Food.</summary>
        Food,

        /// <summary>A drink pickup restoring twenty Food.</summary>
        Soda,

        /// <summary>A bush food pickup restoring ten Food.</summary>
        BushFood,

        /// <summary>A buried carrot requiring an explicit legacy gathering action.</summary>
        BuriedFood,

        /// <summary>A legacy health pickup.</summary>
        Aid,

        /// <summary>A legacy enemy view.</summary>
        Enemy
    }

    /// <summary>Describes how one legacy content category participates in the authoritative runtime.</summary>
    public enum LegacyBoardContentClassification
    {
        /// <summary>The view has a matching entity in <see cref="BoardState"/> and the runtime registry.</summary>
        MappedGameplayEntity,

        /// <summary>The view is visual-only and is intentionally absent from gameplay state.</summary>
        PresentationOnly,

        /// <summary>The known content is deliberately disabled until its owning gameplay ticket.</summary>
        IntentionallyDisabled
    }

    /// <summary>Provides a stable failure category for board-runtime startup diagnostics.</summary>
    public enum BoardRuntimeDiagnosticCode
    {
        /// <summary>An argument or board identity does not match the active run.</summary>
        InvalidInput,

        /// <summary>The layout contains a category with no accepted classification.</summary>
        UnknownContent,

        /// <summary>Two descriptors address the same view or the same gameplay layer and cell.</summary>
        DuplicateEntity,

        /// <summary>Mapped gameplay content lies outside the declared board bounds.</summary>
        OutOfBounds,

        /// <summary>The layout does not contain exactly one mapped player.</summary>
        MissingPlayer,

        /// <summary>At least one in-bounds cell has no mapped terrain.</summary>
        MissingTerrain,

        /// <summary>The layout has no mapped exit.</summary>
        MissingExit,

        /// <summary>A descriptor's grid position disagrees with its Unity view.</summary>
        ViewPositionMismatch
    }

    /// <summary>Reports a reproducible board-runtime composition failure without publishing partial state.</summary>
    public sealed class BoardRuntimeCompositionException : InvalidOperationException
    {
        /// <summary>Creates a diagnostic with a stable category and deterministic context.</summary>
        /// <param name="code">The stable failure category.</param>
        /// <param name="context">Deterministic context sufficient to reproduce the invalid layout.</param>
        public BoardRuntimeCompositionException(BoardRuntimeDiagnosticCode code, string context)
            : base(code + ": " + (context ?? string.Empty))
        {
            Code = code;
            Context = context ?? string.Empty;
        }

        /// <summary>Gets the stable failure category.</summary>
        public BoardRuntimeDiagnosticCode Code { get; }

        /// <summary>Gets deterministic details about the offending content or missing requirement.</summary>
        public string Context { get; }
    }

    /// <summary>Captures one generated Unity view and its intended board-local grid position.</summary>
    public sealed class LegacyBoardView
    {
        /// <summary>Creates an immutable descriptor during legacy board generation.</summary>
        /// <param name="view">The generated or scene-owned Unity view.</param>
        /// <param name="kind">The accepted legacy content category.</param>
        /// <param name="position">The board-local grid position represented by the view.</param>
        /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
        public LegacyBoardView(GameObject view, LegacyBoardContentKind kind, GridPosition position)
        {
            View = view != null ? view : throw new ArgumentNullException(nameof(view));
            Kind = kind;
            Position = position;
        }

        /// <summary>Gets the generated or scene-owned Unity view.</summary>
        public GameObject View { get; }

        /// <summary>Gets the category used by the explicit content classification table.</summary>
        public LegacyBoardContentKind Kind { get; }

        /// <summary>Gets the board-local grid position captured during generation.</summary>
        public GridPosition Position { get; }
    }

    /// <summary>Defines how one legacy category maps to domain content or presentation-only state.</summary>
    public sealed class LegacyBoardContentDefinition
    {
        internal LegacyBoardContentDefinition(
            LegacyBoardContentKind kind,
            LegacyBoardContentClassification classification,
            BoardLayer? layer,
            EntityKind entityKind,
            string contentId,
            BoardEntityTraits traits,
            int automaticFoodReward)
        {
            Kind = kind;
            Classification = classification;
            Layer = layer;
            EntityKind = entityKind;
            ContentId = contentId;
            Traits = traits;
            AutomaticFoodReward = automaticFoodReward;
        }

        /// <summary>Gets the legacy generator category.</summary>
        public LegacyBoardContentKind Kind { get; }

        /// <summary>Gets whether the category is mapped, visual-only, or deliberately disabled.</summary>
        public LegacyBoardContentClassification Classification { get; }

        /// <summary>Gets the gameplay layer, or null when the category has no domain entity.</summary>
        public BoardLayer? Layer { get; }

        /// <summary>Gets the stable domain kind, or the invalid default for non-mapped content.</summary>
        public EntityKind EntityKind { get; }

        /// <summary>Gets the stable textual content ID, or an empty string for non-mapped content.</summary>
        public string ContentId { get; }

        /// <summary>Gets immutable gameplay traits for mapped content.</summary>
        public BoardEntityTraits Traits { get; }

        /// <summary>Gets the automatic Food reward resolved when the player enters this item cell.</summary>
        public int AutomaticFoodReward { get; }

        internal BoardEntityDefinition CreateBoardDefinition()
        {
            if (Classification != LegacyBoardContentClassification.MappedGameplayEntity || !Layer.HasValue)
                throw new InvalidOperationException("Only mapped gameplay content has a board definition.");

            return new BoardEntityDefinition(
                Layer.Value,
                EntityKind,
                ContentId,
                Traits,
                AutomaticFoodReward);
        }
    }

    /// <summary>Owns the complete, explicit classification of content produced by the current legacy generator.</summary>
    public static class LegacyBoardContentCatalog
    {
        private static readonly ReadOnlyCollection<LegacyBoardContentDefinition> definitions =
            Array.AsReadOnly(new[]
            {
                Mapped(LegacyBoardContentKind.Player, BoardLayer.Actor, EntityKind.Player, "legacy.player"),
                Mapped(
                    LegacyBoardContentKind.Floor,
                    BoardLayer.Terrain,
                    new EntityKind("terrain"),
                    "legacy.floor",
                    new BoardEntityTraits(true, false, false)),
                PresentationOnly(LegacyBoardContentKind.OuterWall),
                Mapped(LegacyBoardContentKind.Wall, BoardLayer.Obstacle, EntityKind.Obstacle, "legacy.wall"),
                Mapped(
                    LegacyBoardContentKind.Exit,
                    BoardLayer.Item,
                    new EntityKind("exit"),
                    "legacy.exit",
                    new BoardEntityTraits(false, true, false)),
                Mapped(LegacyBoardContentKind.Food, BoardLayer.Item, EntityKind.Item, "legacy.food", automaticFoodReward: 10),
                Mapped(LegacyBoardContentKind.Soda, BoardLayer.Item, EntityKind.Item, "legacy.soda", automaticFoodReward: 20),
                Mapped(LegacyBoardContentKind.BushFood, BoardLayer.Item, EntityKind.Item, "legacy.bush-food", automaticFoodReward: 10),
                Mapped(
                    LegacyBoardContentKind.BuriedFood,
                    BoardLayer.Item,
                    EntityKind.Item,
                    "legacy.buried-food",
                    new BoardEntityTraits(false, false, true)),
                Mapped(
                    LegacyBoardContentKind.Aid,
                    BoardLayer.Item,
                    EntityKind.Item,
                    "legacy.aid",
                    new BoardEntityTraits(false, false, true)),
                Mapped(LegacyBoardContentKind.Enemy, BoardLayer.Actor, EntityKind.Enemy, "legacy.enemy")
            });

        private static readonly IReadOnlyDictionary<LegacyBoardContentKind, LegacyBoardContentDefinition> byKind =
            BuildLookup(definitions);

        private static readonly ReadOnlyCollection<string> deferredLegacyMutationPaths =
            Array.AsReadOnly(new[]
            {
                "PlayerScript.AttemptMove: movement and Food cost",
                "PlayerScript.OnTriggerEnter2D: Exit/Food/Soda/Aid pickup and outcome mutations",
                "PlayerScript.AttemptGathering: Carrot pickup and Food cost",
                "PlayerScript.OnCantMove: wall damage",
                "GameManager.Update/MoveEnemies: enemy scheduling",
                "Enemy.MoveEnemy/OnCantMove: enemy movement, attack, and player health loss"
            });

        /// <summary>Gets the complete classification table for every current generated content category.</summary>
        public static IReadOnlyList<LegacyBoardContentDefinition> Definitions => definitions;

        /// <summary>
        /// Gets every legacy mutation path intentionally left connected until the M3.6.3 production cutover.
        /// </summary>
        public static IReadOnlyList<string> DeferredLegacyMutationPaths => deferredLegacyMutationPaths;

        /// <summary>Finds the accepted classification for one generated content category.</summary>
        /// <param name="kind">The legacy generator category.</param>
        /// <param name="definition">The immutable classification when known.</param>
        /// <returns>True when the category is explicitly classified.</returns>
        public static bool TryGetDefinition(
            LegacyBoardContentKind kind,
            out LegacyBoardContentDefinition definition)
        {
            return byKind.TryGetValue(kind, out definition);
        }

        private static LegacyBoardContentDefinition Mapped(
            LegacyBoardContentKind kind,
            BoardLayer layer,
            EntityKind entityKind,
            string contentId,
            BoardEntityTraits? traits = null,
            int automaticFoodReward = 0)
        {
            return new LegacyBoardContentDefinition(
                kind,
                LegacyBoardContentClassification.MappedGameplayEntity,
                layer,
                entityKind,
                contentId,
                traits ?? BoardEntityTraits.Default,
                automaticFoodReward);
        }

        private static LegacyBoardContentDefinition PresentationOnly(LegacyBoardContentKind kind)
        {
            return new LegacyBoardContentDefinition(
                kind,
                LegacyBoardContentClassification.PresentationOnly,
                null,
                EntityKind.Default,
                string.Empty,
                BoardEntityTraits.Default,
                0);
        }

        private static IReadOnlyDictionary<LegacyBoardContentKind, LegacyBoardContentDefinition> BuildLookup(
            IEnumerable<LegacyBoardContentDefinition> source)
        {
            var result = new Dictionary<LegacyBoardContentKind, LegacyBoardContentDefinition>();
            foreach (LegacyBoardContentDefinition definition in source)
                result.Add(definition.Kind, definition);
            return new ReadOnlyDictionary<LegacyBoardContentKind, LegacyBoardContentDefinition>(result);
        }
    }
}
