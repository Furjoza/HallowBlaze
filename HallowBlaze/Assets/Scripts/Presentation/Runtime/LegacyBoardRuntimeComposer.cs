using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using UnityEngine;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Validates a complete generated legacy layout and creates one unpublished board runtime.</summary>
    public static class LegacyBoardRuntimeComposer
    {
        /// <summary>
        /// Builds a complete runtime without mutating the run or publishing partial state on failure.
        /// Entity IDs are assigned after deterministic sorting and never depend on Unity instance IDs
        /// or descriptor enumeration order.
        /// </summary>
        /// <param name="request">The stable request used to generate this board.</param>
        /// <param name="runState">The active run referenced by the request.</param>
        /// <param name="bounds">Inclusive bounds for all mapped gameplay content.</param>
        /// <param name="views">The complete generated layout, including presentation-only views.</param>
        /// <returns>A complete, unpublished runtime ready for atomic ownership transfer.</returns>
        /// <exception cref="BoardRuntimeCompositionException">The request or generated layout is invalid.</exception>
        public static BoardRuntime Compose(
            BoardRequest request,
            RunState runState,
            GridBounds bounds,
            IEnumerable<LegacyBoardView> views)
        {
            ValidateIdentity(request, runState);
            if (views == null)
                throw Diagnostic(BoardRuntimeDiagnosticCode.InvalidInput, "views:null");

            var mapped = new List<MappedView>();
            var seenViews = new HashSet<GameObject>();
            var occupied = new HashSet<LayerPosition>();
            int playerCount = 0;
            int exitCount = 0;

            foreach (LegacyBoardView descriptor in views)
            {
                if (descriptor == null || descriptor.View == null)
                    throw Diagnostic(BoardRuntimeDiagnosticCode.InvalidInput, "view:null");
                if (!LegacyBoardContentCatalog.TryGetDefinition(
                    descriptor.Kind,
                    out LegacyBoardContentDefinition content))
                {
                    throw Diagnostic(
                        BoardRuntimeDiagnosticCode.UnknownContent,
                        "kind:" + descriptor.Kind);
                }
                if (!seenViews.Add(descriptor.View))
                {
                    throw Diagnostic(
                        BoardRuntimeDiagnosticCode.DuplicateEntity,
                        "view:" + descriptor.View.name);
                }

                ValidateViewPosition(descriptor);
                if (content.Classification == LegacyBoardContentClassification.PresentationOnly)
                    continue;
                if (content.Classification == LegacyBoardContentClassification.IntentionallyDisabled)
                {
                    descriptor.View.SetActive(false);
                    continue;
                }
                if (!content.Layer.HasValue)
                {
                    throw Diagnostic(
                        BoardRuntimeDiagnosticCode.UnknownContent,
                        "mapped-without-layer:" + descriptor.Kind);
                }
                if (!bounds.Contains(descriptor.Position))
                {
                    throw Diagnostic(
                        BoardRuntimeDiagnosticCode.OutOfBounds,
                        content.ContentId + "@" + descriptor.Position);
                }

                var layerPosition = new LayerPosition(content.Layer.Value, descriptor.Position);
                if (!occupied.Add(layerPosition))
                {
                    throw Diagnostic(
                        BoardRuntimeDiagnosticCode.DuplicateEntity,
                        layerPosition.ToString());
                }

                if (descriptor.Kind == LegacyBoardContentKind.Player)
                    playerCount++;
                if (descriptor.Kind == LegacyBoardContentKind.Exit)
                    exitCount++;
                mapped.Add(new MappedView(descriptor, content));
            }

            if (playerCount != 1)
                throw Diagnostic(BoardRuntimeDiagnosticCode.MissingPlayer, "count:" + playerCount);
            if (exitCount == 0)
                throw Diagnostic(BoardRuntimeDiagnosticCode.MissingExit, "count:0");
            ValidateTerrainCoverage(bounds, occupied);

            var exitPositions = new HashSet<GridPosition>();
            foreach (MappedView mappedView in mapped)
                if (mappedView.Descriptor.Kind == LegacyBoardContentKind.Exit)
                    exitPositions.Add(mappedView.Descriptor.Position);

            mapped.Sort(MappedView.Compare);
            var boardState = new BoardState(bounds);
            var registry = new Dictionary<EntityId, GameObject>(mapped.Count);
            EntityId playerId = default(EntityId);
            for (int index = 0; index < mapped.Count; index++)
            {
                MappedView mappedView = mapped[index];
                var id = new EntityId(index + 1L);
                BoardEntityDefinition definition = mappedView.Content.CreateBoardDefinition();
                if (mappedView.Descriptor.Kind == LegacyBoardContentKind.Floor &&
                    exitPositions.Contains(mappedView.Descriptor.Position))
                {
                    definition = new BoardEntityDefinition(BoardLayer.Terrain, mappedView.Content.EntityKind,
                        mappedView.Content.ContentId, new BoardEntityTraits(true, true, false));
                }
                var entity = new BoardEntityState(
                    id,
                    definition,
                    mappedView.Descriptor.Position);
                if (!boardState.TryAdd(entity))
                {
                    throw Diagnostic(
                        BoardRuntimeDiagnosticCode.DuplicateEntity,
                        mappedView.Content.ContentId + "@" + mappedView.Descriptor.Position);
                }

                registry.Add(id, mappedView.Descriptor.View);
                if (mappedView.Descriptor.Kind == LegacyBoardContentKind.Player)
                    playerId = id;
            }

            return new BoardRuntime(request, runState, boardState, playerId, registry);
        }

        private static void ValidateIdentity(BoardRequest request, RunState runState)
        {
            if (request == null)
                throw Diagnostic(BoardRuntimeDiagnosticCode.InvalidInput, "request:null");
            if (runState == null)
                throw Diagnostic(BoardRuntimeDiagnosticCode.InvalidInput, "run:null");
            if (runState.Status != RunStatus.Active ||
                !string.Equals(request.RunId, runState.RunId, StringComparison.Ordinal) ||
                request.RunSeed != runState.RunSeed ||
                !string.Equals(request.WorldNodeId, runState.WorldNodeId, StringComparison.Ordinal) ||
                request.CurrentDay != runState.CurrentDay ||
                request.BoardSeed != runState.GetBoardSeed())
            {
                throw Diagnostic(
                    BoardRuntimeDiagnosticCode.InvalidInput,
                    "request-run-identity-mismatch");
            }
        }

        private static void ValidateViewPosition(LegacyBoardView descriptor)
        {
            Vector3 viewPosition = descriptor.View.transform.position;
            if (!Mathf.Approximately(viewPosition.x, descriptor.Position.X) ||
                !Mathf.Approximately(viewPosition.y, descriptor.Position.Y))
            {
                throw Diagnostic(
                    BoardRuntimeDiagnosticCode.ViewPositionMismatch,
                    descriptor.Kind + ":model" + descriptor.Position + ":view(" +
                    viewPosition.x + ", " + viewPosition.y + ")");
            }
        }

        private static void ValidateTerrainCoverage(
            GridBounds bounds,
            HashSet<LayerPosition> occupied)
        {
            for (int x = bounds.MinX; x <= bounds.MaxX; x++)
            {
                for (int y = bounds.MinY; y <= bounds.MaxY; y++)
                {
                    var position = new GridPosition(x, y);
                    if (!occupied.Contains(new LayerPosition(BoardLayer.Terrain, position)))
                    {
                        throw Diagnostic(
                            BoardRuntimeDiagnosticCode.MissingTerrain,
                            position.ToString());
                    }
                }
            }
        }

        private static BoardRuntimeCompositionException Diagnostic(
            BoardRuntimeDiagnosticCode code,
            string context)
        {
            return new BoardRuntimeCompositionException(code, context);
        }

        private sealed class MappedView
        {
            public MappedView(LegacyBoardView descriptor, LegacyBoardContentDefinition content)
            {
                Descriptor = descriptor;
                Content = content;
            }

            public LegacyBoardView Descriptor { get; }

            public LegacyBoardContentDefinition Content { get; }

            public static int Compare(MappedView left, MappedView right)
            {
                int comparison = left.Content.Layer.Value.CompareTo(right.Content.Layer.Value);
                if (comparison != 0)
                    return comparison;
                comparison = left.Descriptor.Position.CompareTo(right.Descriptor.Position);
                if (comparison != 0)
                    return comparison;
                comparison = string.CompareOrdinal(left.Content.ContentId, right.Content.ContentId);
                if (comparison != 0)
                    return comparison;
                return left.Descriptor.Kind.CompareTo(right.Descriptor.Kind);
            }
        }

        private struct LayerPosition : IEquatable<LayerPosition>
        {
            public LayerPosition(BoardLayer layer, GridPosition position)
            {
                Layer = layer;
                Position = position;
            }

            public BoardLayer Layer { get; }

            public GridPosition Position { get; }

            public bool Equals(LayerPosition other)
            {
                return Layer == other.Layer && Position.Equals(other.Position);
            }

            public override bool Equals(object obj)
            {
                return obj is LayerPosition other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)Layer * 397) ^ Position.GetHashCode();
                }
            }

            public override string ToString()
            {
                return Layer + "@" + Position;
            }
        }
    }
}
