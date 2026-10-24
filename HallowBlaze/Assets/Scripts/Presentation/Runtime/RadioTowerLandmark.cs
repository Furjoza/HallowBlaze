using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Core.World;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Explicit build-included references for the first authored landmark; contains no level-only gameplay rules.</summary>
    public sealed class RadioTowerLandmark : ScriptableObject
    {
        /// <summary>Identifies the published final-segment generator, not a day/modulo trigger.</summary>
        public const string ConfigurationId = "landmark.radio-tower.board";
        /// <summary>Identifies the fixed optional axe source independently of its run-allocated instance.</summary>
        public const string AxeSourceId = "source.radio-tower.axe";
        /// <summary>Identifies the concrete ration definition collected by the shared automatic food effect.</summary>
        public const string RationsId = "landmark.radio-tower.rations";

        [SerializeField] private TextAsset template;
        [SerializeField] private TextAsset placement;
        [SerializeField] private GameObject boulder;
        [SerializeField] private GameObject plate;
        [SerializeField] private GameObject gate;
        [SerializeField] private GameObject axe;

        /// <summary>Loads required authored references from Resources in Editor and player builds.</summary>
        public static RadioTowerLandmark Load() => Resources.Load<RadioTowerLandmark>("RadioTowerLandmark") ??
            throw new InvalidOperationException("Radio tower landmark references are missing.");

        /// <summary>Reads and validates authored data against shared, explicitly named content definitions.</summary>
        public LandmarkTemplate ReadTemplate() => LandmarkTemplateJson.Deserialize(template != null ? template.text :
            throw new InvalidOperationException("Landmark template reference is missing."), CreateCatalog());

        /// <summary>Resolves the fixed inbound assignment against the published world; no day or RNG selects a template.</summary>
        public LandmarkPlacement CreatePlacement(WorldDefinition world) => LandmarkTemplateJson.DeserializePlacement(
            placement != null ? placement.text : throw new InvalidOperationException("Landmark placement reference is missing."), world, ReadTemplate());

        /// <summary>Finds explicit landmark views; returns null only for ordinary floor/enemy/exit/wood/ration mappings owned by the board adapter.</summary>
        /// <exception cref="InvalidOperationException">A required special prefab reference is missing; ordinary art must never substitute for it.</exception>
        public GameObject FindPrefab(string contentId)
        {
            GameObject prefab;
            if (contentId == BoulderPushAction.ContentId) prefab = boulder;
            else if (contentId == PlateGateValidator.PlateContent.ContentId) prefab = plate;
            else if (contentId == PlateGateValidator.GateContent.ContentId) prefab = gate;
            else if (contentId == "axe.basic") prefab = axe;
            else return null;
            return prefab != null ? prefab : throw new InvalidOperationException("Missing landmark prefab: " + contentId);
        }

        /// <summary>Creates detached content with the same push, clearing, plate/gate and 10 HP zombie rules as ordinary runtime.</summary>
        public static BoardValidationCatalog CreateCatalog()
        {
            var content = new List<BoardEntityDefinition>();
            foreach (LegacyBoardContentKind kind in new[] { LegacyBoardContentKind.Player, LegacyBoardContentKind.Floor, LegacyBoardContentKind.Wall })
            {
                LegacyBoardContentCatalog.TryGetDefinition(kind, out LegacyBoardContentDefinition definition);
                content.Add(definition.CreateBoardDefinition());
            }
            content.Add(BoulderPushAction.Definition);
            content.Add(PlateGateValidator.PlateContent);
            content.Add(PlateGateValidator.GateContent);
            content.Add(new BoardEntityDefinition(BoardLayer.Terrain, new EntityKind("terrain"), OrdinaryBoardContent.ExitTerrainId,
                new BoardEntityTraits(true, true, false)));
            content.Add(new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, OrdinaryBoardContent.Enemy10Id));
            content.Add(new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, "axe.basic"));
            content.Add(new BoardEntityDefinition(BoardLayer.Item, EntityKind.Item, RationsId, BoardEntityTraits.Default, 20));
            return new BoardValidationCatalog(content,
                new Dictionary<string, string> { { "axe.basic", "tools" }, { RationsId, "food" } },
                new Dictionary<string, string> { { AxeSourceId, "axe.basic" }, { "source.radio-tower.rations", RationsId } },
                new Dictionary<string, ShamblerDefinition> { { OrdinaryBoardContent.Enemy10Id, new ShamblerDefinition(10) } });
        }
    }
}