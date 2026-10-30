using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Pipeline;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Resolution;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Adapts existing ordinary content to stable generation definitions without inventory or profile ownership.</summary>
    public static class OrdinaryBoardContent
    {
        /// <summary>Identifies walkable exit terrain; its visual marker never counts as resource supply.</summary>
        public const string ExitTerrainId = "ordinary.exit-terrain";
        /// <summary>Identifies the current 10 HP Shambler variant independently of prefab identity.</summary>
        public const string Enemy10Id = "ordinary.shambler.10";
        /// <summary>Identifies the current 20 HP Shambler variant independently of prefab identity.</summary>
        public const string Enemy20Id = "ordinary.shambler.20";

        /// <summary>Creates detached authoritative definitions; automatic food rewards remain compatibility effects only.</summary>
        public static BoardValidationCatalog CreateCatalog()
        {
            var definitions = new List<BoardEntityDefinition>();
            foreach (LegacyBoardContentKind kind in new[] { LegacyBoardContentKind.Player, LegacyBoardContentKind.Floor,
                LegacyBoardContentKind.Wall, LegacyBoardContentKind.Food, LegacyBoardContentKind.Soda })
            {
                LegacyBoardContentCatalog.TryGetDefinition(kind, out LegacyBoardContentDefinition content);
                definitions.Add(content.CreateBoardDefinition());
            }
            definitions.Add(new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), ExitTerrainId,
                new BoardEntityTraits(true, true, false)));
            definitions.Add(new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, Enemy10Id));
            definitions.Add(new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, Enemy20Id));
            return new BoardValidationCatalog(definitions,
                new Dictionary<string, string> { { "legacy.food", "food" }, { "legacy.soda", "food" } },
                new Dictionary<string, string>(), new Dictionary<string, ShamblerDefinition>
                {
                    { Enemy10Id, new ShamblerDefinition(10) }, { Enemy20Id, new ShamblerDefinition(20) }
                });
        }

        /// <summary>Resolves retained v1/v2 budgets or version-3 entry-safe generation; arbitrary IDs/versions are rejected.</summary>
        /// <param name="address">The actual segment identity; day affects obstacle/enemy pressure, not board size.</param>
        /// <returns>A detached inclusive budget; this is a first-family preset, not final balance.</returns>
        /// <exception cref="ArgumentException">The generation family or version is unsupported.</exception>
        /// <exception cref="ArgumentNullException">The address is null.</exception>
        public static BoardGenerationConfig CreateConfiguration(BoardAddress address)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (address.GenerationConfigurationId != OrdinaryBoardGenerator.ConfigurationId ||
                (address.GenerationVersion != OrdinaryBoardGenerator.ConfigurationVersion &&
                address.GenerationVersion != 2 &&
                address.GenerationVersion != BoardGenerationPipeline.OrdinaryConfigurationVersion))
                throw new ArgumentException("Unsupported ordinary generation identity: " + address.GenerationConfigurationId +
                    "/" + address.GenerationVersion, nameof(address));
            int obstacleMinimum = 5 + Math.Min(2, (address.DayNumber - 1) / 3);
            int enemyCount = Math.Min(3, (int)Math.Log(address.DayNumber, 2));
            return new BoardGenerationConfig(address.GenerationConfigurationId, address.GenerationVersion,
                new GenerationRange(8, 14), new GenerationRange(20, 58),
                new GenerationRange(obstacleMinimum, obstacleMinimum + 2), new GenerationRange(enemyCount, enemyCount),
                Math.Min(1, enemyCount), 2,
                new[] { "legacy.player", "legacy.floor", "legacy.wall", "legacy.food", "legacy.soda", ExitTerrainId, Enemy10Id, Enemy20Id },
                new[] { new ResourceGenerationBudget("food", new GenerationRange(2, 5)) });
        }
    }
}