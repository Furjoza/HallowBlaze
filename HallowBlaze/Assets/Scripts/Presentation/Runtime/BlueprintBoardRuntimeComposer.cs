using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Resolution;
using UnityEngine;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Builds one unpublished runtime from authoritative blueprint data, never from a scene scrape.</summary>
    public static class BlueprintBoardRuntimeComposer
    {
        /// <summary>
        /// Validates request identity, content and exact view bindings before constructing independent state and initial intents.
        /// Blueprint entity IDs and enemy rules are retained; views cannot change definitions or placements.
        /// </summary>
        /// <param name="request">The actual persisted board boundary.</param>
        /// <param name="run">Borrowed active run; composition does not modify its resources or persistence.</param>
        /// <param name="blueprint">Initial model matching the request's full address.</param>
        /// <param name="catalog">Authoritative definitions and supported enemy rules.</param>
        /// <param name="views">Exactly one unique, positioned view per entity ID; decorative objects are not bindings.</param>
        /// <param name="tools">Optional explicit tool dispatch and current action-cost policy; null leaves placeholders without effects.</param>
        /// <param name="groundTools">Optional existing run-allocated tool instances matching authored item entities; no generation or equipment mutation.</param>
        /// <returns>A complete runtime whose initial state reproduces the blueprint.</returns>
        /// <exception cref="BoardRuntimeCompositionException">Identity, blueprint validation or view parity fails.</exception>
        public static BoardRuntime Compose(BoardRequest request, RunState run, BoardBlueprint blueprint,
            BoardValidationCatalog catalog, IReadOnlyDictionary<EntityId, GameObject> views, ToolUseQuery tools = null,
            IEnumerable<GroundToolState> groundTools = null)
        {
            if (request == null || run == null || blueprint == null || catalog == null || views == null ||
                !BoardOutcomeIdentityGuard.ValidateRequest(request, run, out _) || !blueprint.Address.Equals(request.Address))
                throw new BoardRuntimeCompositionException(BoardRuntimeDiagnosticCode.InvalidInput, "blueprint:identity-or-input");
            BoardValidationReport report = BoardValidator.Validate(blueprint, catalog);
            if (!report.IsValid) throw new BoardGenerationException(report);
            if (views.Count != blueprint.Entities.Count)
                throw new BoardRuntimeCompositionException(BoardRuntimeDiagnosticCode.InvalidInput, "blueprint:view-count");
            var bindings = new Dictionary<EntityId, GameObject>();
            var uniqueViews = new HashSet<GameObject>();
            var enemies = new List<ShamblerState>();
            EntityId playerId = default(EntityId);
            foreach (BoardEntityState entity in blueprint.Entities)
            {
                if (!views.TryGetValue(entity.Id, out GameObject view) || view == null || !uniqueViews.Add(view))
                    throw new BoardRuntimeCompositionException(BoardRuntimeDiagnosticCode.DuplicateEntity,
                        "blueprint:view:" + entity.Id);
                Vector3 position = view.transform.position;
                if (!Mathf.Approximately(position.x, entity.Position.X) || !Mathf.Approximately(position.y, entity.Position.Y))
                    throw new BoardRuntimeCompositionException(BoardRuntimeDiagnosticCode.ViewPositionMismatch,
                        "blueprint:" + entity.Id + "@" + entity.Position);
                bindings.Add(entity.Id, view);
                if (entity.Definition.Kind.Equals(EntityKind.Player)) playerId = entity.Id;
                else if (entity.Definition.Kind.Equals(EntityKind.Enemy))
                {
                    if (!catalog.TryGetShambler(entity.Definition.ContentId, out ShamblerDefinition rule))
                        throw new BoardRuntimeCompositionException(BoardRuntimeDiagnosticCode.UnknownContent, entity.Definition.ContentId);
                    enemies.Add(new ShamblerState(entity.Id, rule));
                }
            }
            BoardState board = blueprint.CreateState();
            foreach (GroundToolState ground in groundTools ?? Array.Empty<GroundToolState>())
            {
                BoardItemSpawn spawn = null;
                if (ground != null)
                    foreach (BoardItemSpawn candidate in blueprint.Items)
                        if (candidate.EntityId.Equals(ground.EntityId)) { spawn = candidate; break; }
                if (ground == null || !run.CanReceiveTool(0, new ToolInstanceState(ground.InstanceId, ground.DefinitionId, ground.Charges)) ||
                    spawn == null || spawn.SourceId != ground.SourceId || !board.TryBindGroundTool(ground))
                    throw new BoardRuntimeCompositionException(BoardRuntimeDiagnosticCode.InvalidInput, "blueprint:invalid-ground-tool");
            }
            return new BoardRuntime(request, run, board, playerId, bindings, enemies, tools: tools);
        }
    }
}