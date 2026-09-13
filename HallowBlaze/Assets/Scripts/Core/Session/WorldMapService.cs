using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;

namespace HallowBlaze.Core.Session
{
    /// <summary>
    /// Identifies the discovery operation that produced a durable profile change.
    /// </summary>
    public enum WorldDiscoveryOperation
    {
        /// <summary>The active run reached its current world node.</summary>
        EnteredNode,

        /// <summary>The active run observed every outgoing choice at its current node.</summary>
        ObservedExits,

        /// <summary>The active run completed travel over one directed edge.</summary>
        TraversedEdge
    }

    /// <summary>
    /// Describes the outcome of a discovery command without using exceptions for expected rejection.
    /// </summary>
    public enum WorldMapCommandStatus
    {
        /// <summary>Durable knowledge changed and was saved.</summary>
        Applied,

        /// <summary>The requested knowledge was already present.</summary>
        NoChange,

        /// <summary>The command was not legal in the current session state.</summary>
        Rejected,

        /// <summary>Knowledge changed in memory, but its profile save must be retried.</summary>
        PersistenceFailed
    }

    /// <summary>
    /// Represents one node or edge knowledge transition in a discovery event.
    /// </summary>
    public sealed class WorldDiscoveryChange
    {
        private WorldDiscoveryChange(
            string nodeId,
            NodeDiscoveryState? nodeState,
            string edgeId,
            EdgeDiscoveryState? edgeState)
        {
            NodeId = nodeId;
            NodeState = nodeState;
            EdgeId = edgeId;
            EdgeState = edgeState;
        }

        /// <summary>Gets the changed node ID, or <c>null</c> for an edge change.</summary>
        public string NodeId { get; }

        /// <summary>Gets the new node state, or <c>null</c> for an edge change.</summary>
        public NodeDiscoveryState? NodeState { get; }

        /// <summary>Gets the changed edge ID, or <c>null</c> for a node change.</summary>
        public string EdgeId { get; }

        /// <summary>Gets the new edge state, or <c>null</c> for a node change.</summary>
        public EdgeDiscoveryState? EdgeState { get; }

        internal static WorldDiscoveryChange ForNode(
            string nodeId,
            NodeDiscoveryState state)
        {
            return new WorldDiscoveryChange(nodeId, state, null, null);
        }

        internal static WorldDiscoveryChange ForEdge(
            string edgeId,
            EdgeDiscoveryState state)
        {
            return new WorldDiscoveryChange(null, null, edgeId, state);
        }
    }

    /// <summary>
    /// Aggregates every durable discovery transition produced by one world-map operation.
    /// </summary>
    public sealed class WorldDiscoveryEvent
    {
        internal WorldDiscoveryEvent(
            WorldDiscoveryOperation operation,
            string operationId,
            IReadOnlyList<WorldDiscoveryChange> changes)
        {
            Operation = operation;
            OperationId = operationId;
            Changes = changes;
        }

        /// <summary>Gets the operation that produced the changes.</summary>
        public WorldDiscoveryOperation Operation { get; }

        /// <summary>Gets the relevant node or edge ID.</summary>
        public string OperationId { get; }

        /// <summary>Gets the read-only changes in deterministic order.</summary>
        public IReadOnlyList<WorldDiscoveryChange> Changes { get; }
    }

    /// <summary>
    /// Reports whether a discovery command was saved, unchanged, rejected, or left pending.
    /// </summary>
    public sealed class WorldMapCommandResult
    {
        private WorldMapCommandResult(
            WorldMapCommandStatus status,
            WorldDiscoveryEvent discoveryEvent,
            SaveStoreResultType? persistenceResultType,
            string errorMessage)
        {
            Status = status;
            DiscoveryEvent = discoveryEvent;
            PersistenceResultType = persistenceResultType;
            ErrorMessage = errorMessage;
        }

        /// <summary>Gets the command outcome.</summary>
        public WorldMapCommandStatus Status { get; }

        /// <summary>Gets the published event when the command was applied.</summary>
        public WorldDiscoveryEvent DiscoveryEvent { get; }

        /// <summary>Gets the failed persistence result type, when applicable.</summary>
        public SaveStoreResultType? PersistenceResultType { get; }

        /// <summary>Gets a stable diagnostic message for a rejected or failed command.</summary>
        public string ErrorMessage { get; }

        /// <summary>Gets whether the command completed without rejection or persistence failure.</summary>
        public bool IsSuccess =>
            Status == WorldMapCommandStatus.Applied ||
            Status == WorldMapCommandStatus.NoChange;

        internal static WorldMapCommandResult Applied(WorldDiscoveryEvent discoveryEvent)
        {
            return new WorldMapCommandResult(
                WorldMapCommandStatus.Applied,
                discoveryEvent,
                null,
                null);
        }

        internal static WorldMapCommandResult NoChange()
        {
            return new WorldMapCommandResult(
                WorldMapCommandStatus.NoChange,
                null,
                null,
                null);
        }

        internal static WorldMapCommandResult Rejected(string errorMessage)
        {
            return new WorldMapCommandResult(
                WorldMapCommandStatus.Rejected,
                null,
                null,
                errorMessage);
        }

        internal static WorldMapCommandResult PersistenceFailed(SaveStoreResult result)
        {
            return new WorldMapCommandResult(
                WorldMapCommandStatus.PersistenceFailed,
                null,
                result.Type,
                result.ErrorMessage);
        }
    }

    /// <summary>
    /// Exposes only the node fields justified by the profile's current knowledge state.
    /// </summary>
    public sealed class WorldMapNodeView
    {
        internal WorldMapNodeView(
            string nodeId,
            NodeDiscoveryState discoveryState,
            int? atlasX,
            int? atlasY,
            int? distanceLayer,
            string placeKind,
            string biomeFamily)
        {
            NodeId = nodeId;
            DiscoveryState = discoveryState;
            AtlasX = atlasX;
            AtlasY = atlasY;
            DistanceLayer = distanceLayer;
            PlaceKind = placeKind;
            BiomeFamily = biomeFamily;
        }

        /// <summary>Gets the stable node ID used to correlate durable knowledge.</summary>
        public string NodeId { get; }

        /// <summary>Gets the durable node knowledge state.</summary>
        public NodeDiscoveryState DiscoveryState { get; }

        /// <summary>Gets the atlas X coordinate when the node is at least sighted.</summary>
        public int? AtlasX { get; }

        /// <summary>Gets the atlas Y coordinate when the node is at least sighted.</summary>
        public int? AtlasY { get; }

        /// <summary>Gets the distance layer when the node is at least sighted.</summary>
        public int? DistanceLayer { get; }

        /// <summary>Gets the stable place kind only after the node was visited.</summary>
        public string PlaceKind { get; }

        /// <summary>Gets the stable biome family only after the node was visited.</summary>
        public string BiomeFamily { get; }
    }

    /// <summary>
    /// Exposes a known directed edge without returning its mutable profile owner or raw catalogue object.
    /// </summary>
    public sealed class WorldMapEdgeView
    {
        internal WorldMapEdgeView(
            WorldEdgeDefinition edge,
            EdgeDiscoveryState discoveryState)
        {
            EdgeId = edge.EdgeId;
            FromNodeId = edge.FromNodeId;
            ToNodeId = edge.ToNodeId;
            WorldDirection = edge.WorldDirection;
            ClueKey = edge.ClueKey;
            DiscoveryState = discoveryState;
        }

        /// <summary>Gets the stable directed edge ID.</summary>
        public string EdgeId { get; }

        /// <summary>Gets the known source node ID.</summary>
        public string FromNodeId { get; }

        /// <summary>Gets the known destination node ID.</summary>
        public string ToNodeId { get; }

        /// <summary>Gets the stable world direction.</summary>
        public string WorldDirection { get; }

        /// <summary>Gets the diegetic clue key associated with the edge.</summary>
        public string ClueKey { get; }

        /// <summary>Gets the durable edge knowledge state.</summary>
        public EdgeDiscoveryState DiscoveryState { get; }
    }

    /// <summary>
    /// Provides an immutable atlas projection containing only profile-known topology.
    /// </summary>
    public sealed class WorldMapSnapshot
    {
        internal WorldMapSnapshot(
            IReadOnlyList<WorldMapNodeView> nodes,
            IReadOnlyList<WorldMapEdgeView> edges)
        {
            Nodes = nodes;
            Edges = edges;
        }

        /// <summary>Gets known nodes in stable catalogue order.</summary>
        public IReadOnlyList<WorldMapNodeView> Nodes { get; }

        /// <summary>Gets known edges whose two endpoints are also known.</summary>
        public IReadOnlyList<WorldMapEdgeView> Edges { get; }
    }

    /// <summary>
    /// Describes one currently legal directed choice without revealing its destination identity.
    /// </summary>
    public sealed class WorldMapExitOption
    {
        internal WorldMapExitOption(WorldEdgeDefinition edge)
        {
            EdgeId = edge.EdgeId;
            WorldDirection = edge.WorldDirection;
            ClueKey = edge.ClueKey;
        }

        /// <summary>Gets the stable edge ID that a later route command can submit.</summary>
        public string EdgeId { get; }

        /// <summary>Gets the observable world direction.</summary>
        public string WorldDirection { get; }

        /// <summary>Gets the observable diegetic clue key.</summary>
        public string ClueKey { get; }
    }

    /// <summary>
    /// Reports a legal-exit query without throwing for an absent or invalid active run position.
    /// </summary>
    public sealed class WorldMapExitQueryResult
    {
        private static readonly IReadOnlyList<WorldMapExitOption> NoExits =
            Array.AsReadOnly(new WorldMapExitOption[0]);

        private WorldMapExitQueryResult(
            bool isSuccess,
            IReadOnlyList<WorldMapExitOption> exits,
            string errorMessage)
        {
            IsSuccess = isSuccess;
            Exits = exits;
            ErrorMessage = errorMessage;
        }

        /// <summary>Gets whether the active run position resolved to a catalogue node.</summary>
        public bool IsSuccess { get; }

        /// <summary>Gets the legal outgoing choices in stable edge-ID order.</summary>
        public IReadOnlyList<WorldMapExitOption> Exits { get; }

        /// <summary>Gets the rejection reason when the query could not be resolved.</summary>
        public string ErrorMessage { get; }

        internal static WorldMapExitQueryResult Success(IReadOnlyList<WorldMapExitOption> exits)
        {
            return new WorldMapExitQueryResult(true, exits, null);
        }

        internal static WorldMapExitQueryResult Rejected(string errorMessage)
        {
            return new WorldMapExitQueryResult(false, NoExits, errorMessage);
        }
    }

    /// <summary>
    /// Identifies the current lifecycle phase of the route choice owned by the map service.
    /// </summary>
    public enum RouteChoiceState
    {
        /// <summary>No board exit is currently awaiting a route choice.</summary>
        Inactive,

        /// <summary>Legal exits were observed and one may be selected.</summary>
        AwaitingChoice,

        /// <summary>The run moved in memory and its checkpoint save must be retried.</summary>
        RunSavePending,

        /// <summary>The selected route was saved and published.</summary>
        Committed
    }

    /// <summary>Describes the outcome of a route choice command.</summary>
    public enum RouteChoiceCommandStatus
    {
        /// <summary>The route was committed and its run checkpoint was saved.</summary>
        Applied,

        /// <summary>The same committed route was submitted again.</summary>
        NoChange,

        /// <summary>The route was not legal in the current choice state.</summary>
        Rejected,

        /// <summary>A profile or run save must be retried.</summary>
        PersistenceFailed
    }

    /// <summary>Identifies which durable owner failed during a route choice.</summary>
    public enum RouteChoicePersistenceTarget
    {
        /// <summary>The persistent profile atlas.</summary>
        Profile,

        /// <summary>The active run checkpoint.</summary>
        Run
    }

    /// <summary>Describes one route after its run checkpoint has been saved.</summary>
    public sealed class RouteChosenEvent
    {
        internal RouteChosenEvent(
            string runId,
            string edgeId,
            string fromNodeId,
            string toNodeId,
            int currentDay)
        {
            RunId = runId;
            EdgeId = edgeId;
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
            CurrentDay = currentDay;
        }

        /// <summary>Gets the run whose checkpoint contains the route.</summary>
        public string RunId { get; }

        /// <summary>Gets the traversed directed edge ID.</summary>
        public string EdgeId { get; }

        /// <summary>Gets the node where the choice began.</summary>
        public string FromNodeId { get; }

        /// <summary>Gets the destination stored in the run checkpoint.</summary>
        public string ToNodeId { get; }

        /// <summary>Gets the day stored in the run checkpoint.</summary>
        public int CurrentDay { get; }
    }

    /// <summary>Reports whether a route was committed, unchanged, rejected, or left pending.</summary>
    public sealed class RouteChoiceCommandResult
    {
        private RouteChoiceCommandResult(
            RouteChoiceCommandStatus status,
            RouteChosenEvent routeEvent,
            RouteChoicePersistenceTarget? persistenceTarget,
            SaveStoreResultType? persistenceResultType,
            string errorMessage)
        {
            Status = status;
            RouteEvent = routeEvent;
            PersistenceTarget = persistenceTarget;
            PersistenceResultType = persistenceResultType;
            ErrorMessage = errorMessage;
        }

        /// <summary>Gets the command outcome.</summary>
        public RouteChoiceCommandStatus Status { get; }

        /// <summary>Gets the committed route for an applied or repeated command.</summary>
        public RouteChosenEvent RouteEvent { get; }

        /// <summary>Gets the durable owner whose save failed, when applicable.</summary>
        public RouteChoicePersistenceTarget? PersistenceTarget { get; }

        /// <summary>Gets the failed persistence result type, when applicable.</summary>
        public SaveStoreResultType? PersistenceResultType { get; }

        /// <summary>Gets a stable diagnostic message for a rejected or failed command.</summary>
        public string ErrorMessage { get; }

        /// <summary>Gets whether the command completed without rejection or persistence failure.</summary>
        public bool IsSuccess =>
            Status == RouteChoiceCommandStatus.Applied ||
            Status == RouteChoiceCommandStatus.NoChange;

        internal static RouteChoiceCommandResult Applied(RouteChosenEvent routeEvent)
        {
            return new RouteChoiceCommandResult(
                RouteChoiceCommandStatus.Applied,
                routeEvent,
                null,
                null,
                null);
        }

        internal static RouteChoiceCommandResult NoChange(RouteChosenEvent routeEvent)
        {
            return new RouteChoiceCommandResult(
                RouteChoiceCommandStatus.NoChange,
                routeEvent,
                null,
                null,
                null);
        }

        internal static RouteChoiceCommandResult Rejected(string errorMessage)
        {
            return new RouteChoiceCommandResult(
                RouteChoiceCommandStatus.Rejected,
                null,
                null,
                null,
                errorMessage);
        }

        internal static RouteChoiceCommandResult PersistenceFailed(
            RouteChoicePersistenceTarget target,
            SaveStoreResultType resultType,
            string errorMessage)
        {
            return new RouteChoiceCommandResult(
                RouteChoiceCommandStatus.PersistenceFailed,
                null,
                target,
                resultType,
                errorMessage);
        }
    }

    /// <summary>
    /// Joins one immutable world catalogue to session position and durable profile knowledge.
    /// </summary>
    public sealed class WorldMapService
    {
        private readonly WorldDefinition world;
        private readonly GameSession session;
        private readonly ISaveStore saveStore;
        private PendingDiscovery pendingDiscovery;
        private string selectionRunId;
        private string selectionNodeId;
        private string pendingRouteEdgeId;
        private string pendingRouteDestinationNodeId;
        private int pendingRouteDay;
        private RouteChosenEvent committedRoute;

        /// <summary>
        /// Creates an authoritative query and discovery boundary for one profile world.
        /// </summary>
        /// <param name="world">The immutable catalogue used by this profile.</param>
        /// <param name="session">The session that owns the profile and active run.</param>
        /// <param name="saveStore">The persistence boundary used for discovery autosaves.</param>
        /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// The profile targets another world or already contains discovery IDs absent from the catalogue.
        /// </exception>
        public WorldMapService(
            WorldDefinition world,
            GameSession session,
            ISaveStore saveStore)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.saveStore = saveStore ?? throw new ArgumentNullException(nameof(saveStore));

            ValidateWorldBinding();
        }

        /// <summary>
        /// Raised exactly once after one operation's durable profile changes are saved successfully.
        /// </summary>
        public event Action<WorldDiscoveryEvent> OnDiscovery;

        /// <summary>
        /// Raised exactly once after a chosen route's run checkpoint is saved successfully.
        /// </summary>
        public event Action<RouteChosenEvent> OnRouteChosen;

        /// <summary>Gets the current route choice lifecycle phase.</summary>
        public RouteChoiceState ChoiceState { get; private set; }

        /// <summary>
        /// Gets a read-only snapshot that omits every unknown node and edge.
        /// </summary>
        /// <returns>A detached, knowledge-filtered atlas projection.</returns>
        public WorldMapSnapshot GetAtlasSnapshot()
        {
            ProfileState profile = session.Profile;
            List<WorldMapNodeView> nodes = new List<WorldMapNodeView>();
            HashSet<string> knownNodeIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (WorldNodeDefinition node in world.Nodes)
            {
                NodeDiscoveryState state = profile.GetNodeDiscoveryState(node.NodeId);
                if (state == NodeDiscoveryState.Unknown)
                    continue;

                knownNodeIds.Add(node.NodeId);
                bool isSighted = state >= NodeDiscoveryState.Sighted;
                bool isVisited = state == NodeDiscoveryState.Visited;
                nodes.Add(new WorldMapNodeView(
                    node.NodeId,
                    state,
                    isSighted ? node.AtlasX : (int?)null,
                    isSighted ? node.AtlasY : (int?)null,
                    isSighted ? node.DistanceLayer : (int?)null,
                    isVisited ? node.PlaceKind : null,
                    isVisited ? node.BiomeFamily : null));
            }

            List<WorldMapEdgeView> edges = new List<WorldMapEdgeView>();
            foreach (WorldEdgeDefinition edge in world.Edges)
            {
                EdgeDiscoveryState state = profile.GetEdgeDiscoveryState(edge.EdgeId);
                if (state == EdgeDiscoveryState.Unknown ||
                    !knownNodeIds.Contains(edge.FromNodeId) ||
                    !knownNodeIds.Contains(edge.ToNodeId))
                {
                    continue;
                }

                edges.Add(new WorldMapEdgeView(edge, state));
            }

            return new WorldMapSnapshot(
                new ReadOnlyCollection<WorldMapNodeView>(nodes),
                new ReadOnlyCollection<WorldMapEdgeView>(edges));
        }

        /// <summary>
        /// Gets every directed edge currently selectable from the active run position.
        /// </summary>
        /// <returns>A controlled result with detached, read-only exit options.</returns>
        public WorldMapExitQueryResult GetLegalExits()
        {
            if (!TryGetCurrentNode(out WorldNodeDefinition currentNode, out string errorMessage))
                return WorldMapExitQueryResult.Rejected(errorMessage);

            IReadOnlyList<WorldEdgeDefinition> outgoingEdges =
                world.GetOutgoingEdges(currentNode.NodeId);
            List<WorldMapExitOption> exits = new List<WorldMapExitOption>(outgoingEdges.Count);
            foreach (WorldEdgeDefinition edge in outgoingEdges)
                exits.Add(new WorldMapExitOption(edge));

            return WorldMapExitQueryResult.Success(
                new ReadOnlyCollection<WorldMapExitOption>(exits));
        }

        /// <summary>
        /// Observes the current board exits and opens an explicit route choice after the profile save succeeds.
        /// </summary>
        /// <returns>The underlying discovery result for the observed legal exits.</returns>
        public WorldMapCommandResult BeginRouteChoice()
        {
            ResetChoiceIfRunChanged();
            if (ChoiceState == RouteChoiceState.RunSavePending)
            {
                return WorldMapCommandResult.Rejected(
                    "The selected route is waiting for its run save retry.");
            }

            if (!TryGetCurrentNode(out WorldNodeDefinition currentNode, out string errorMessage))
                return WorldMapCommandResult.Rejected(errorMessage);

            RunState run = session.GetCurrentRun();
            if (ChoiceState == RouteChoiceState.AwaitingChoice &&
                string.Equals(selectionRunId, run.RunId, StringComparison.Ordinal) &&
                string.Equals(selectionNodeId, currentNode.NodeId, StringComparison.Ordinal))
            {
                return WorldMapCommandResult.NoChange();
            }

            WorldMapCommandResult discoveryResult = ObserveCurrentExits();
            if (!discoveryResult.IsSuccess)
                return discoveryResult;

            selectionRunId = run.RunId;
            selectionNodeId = currentNode.NodeId;
            pendingRouteEdgeId = null;
            pendingRouteDestinationNodeId = null;
            committedRoute = null;
            ChoiceState = RouteChoiceState.AwaitingChoice;
            return discoveryResult;
        }

        /// <summary>Gets the legal options for the currently open route choice.</summary>
        /// <returns>A controlled result that rejects queries outside an active choice.</returns>
        public WorldMapExitQueryResult GetRouteChoices()
        {
            ResetChoiceIfRunChanged();
            if (ChoiceState != RouteChoiceState.AwaitingChoice)
            {
                return WorldMapExitQueryResult.Rejected(
                    "No route choice is currently awaiting a selection.");
            }

            if (!TryGetCurrentNode(out WorldNodeDefinition currentNode, out string errorMessage))
                return WorldMapExitQueryResult.Rejected(errorMessage);
            if (!string.Equals(currentNode.NodeId, selectionNodeId, StringComparison.Ordinal))
                return WorldMapExitQueryResult.Rejected("The route choice is stale.");

            return GetLegalExits();
        }

        /// <summary>
        /// Commits one legal directed edge, then checkpoints the destination, day, and route in the active run.
        /// </summary>
        /// <param name="edgeId">The stable ID returned by <see cref="GetRouteChoices"/>.</param>
        /// <returns>A controlled result supporting identical retries after persistence failures.</returns>
        public RouteChoiceCommandResult ChooseRoute(string edgeId)
        {
            if (string.IsNullOrWhiteSpace(edgeId))
                return RouteChoiceCommandResult.Rejected("A route edge ID is required.");

            ResetChoiceIfRunChanged();
            if (ChoiceState == RouteChoiceState.Committed)
            {
                return committedRoute != null &&
                    string.Equals(committedRoute.EdgeId, edgeId, StringComparison.Ordinal)
                    ? RouteChoiceCommandResult.NoChange(committedRoute)
                    : RouteChoiceCommandResult.Rejected("A route was already committed for this board exit.");
            }

            if (ChoiceState == RouteChoiceState.Inactive)
                return RouteChoiceCommandResult.Rejected("No route choice is currently active.");

            if (ChoiceState == RouteChoiceState.RunSavePending)
            {
                if (!string.Equals(pendingRouteEdgeId, edgeId, StringComparison.Ordinal))
                {
                    return RouteChoiceCommandResult.Rejected(
                        "Another route is waiting for its run save retry.");
                }

                return PersistPendingRoute();
            }

            if (!TryGetCurrentNode(out WorldNodeDefinition currentNode, out string errorMessage))
                return RouteChoiceCommandResult.Rejected(errorMessage);
            if (!string.Equals(currentNode.NodeId, selectionNodeId, StringComparison.Ordinal))
                return RouteChoiceCommandResult.Rejected("The route choice is stale.");
            if (!world.TryGetEdge(edgeId, out WorldEdgeDefinition edge))
                return RouteChoiceCommandResult.Rejected($"World edge '{edgeId}' does not exist.");
            if (!string.Equals(edge.FromNodeId, selectionNodeId, StringComparison.Ordinal))
            {
                return RouteChoiceCommandResult.Rejected(
                    $"World edge '{edgeId}' is not an exit from '{selectionNodeId}'.");
            }

            RunState run = session.GetCurrentRun();
            if (run.CurrentDay == int.MaxValue)
                return RouteChoiceCommandResult.Rejected("The current day cannot be advanced.");

            WorldMapCommandResult traversalResult = CompleteTraversal(edge.EdgeId);
            if (!traversalResult.IsSuccess)
                return FromTraversalFailure(traversalResult);

            session.AdvanceToWorldNode(edge.ToNodeId);
            pendingRouteEdgeId = edge.EdgeId;
            pendingRouteDestinationNodeId = edge.ToNodeId;
            pendingRouteDay = run.CurrentDay;
            ChoiceState = RouteChoiceState.RunSavePending;
            return PersistPendingRoute();
        }

        /// <summary>
        /// Records arrival at the active run's current node without moving the run.
        /// </summary>
        /// <returns>The save-backed discovery command result.</returns>
        public WorldMapCommandResult EnterCurrentNode()
        {
            if (!TryGetCurrentNode(out WorldNodeDefinition currentNode, out string errorMessage))
                return WorldMapCommandResult.Rejected(errorMessage);

            const WorldDiscoveryOperation operation = WorldDiscoveryOperation.EnteredNode;
            if (!TryResumePending(operation, currentNode.NodeId, out WorldMapCommandResult pendingResult))
                return pendingResult;
            if (pendingResult != null)
                return pendingResult;

            if (session.Profile.GetNodeDiscoveryState(currentNode.NodeId) == NodeDiscoveryState.Visited)
                return WorldMapCommandResult.NoChange();

            WorldDiscoveryChange[] changes =
            {
                WorldDiscoveryChange.ForNode(currentNode.NodeId, NodeDiscoveryState.Visited)
            };
            session.Profile.AdvanceNodeDiscovery(currentNode.NodeId, NodeDiscoveryState.Visited);
            return Persist(
                new PendingDiscovery(operation, currentNode.NodeId, CreateEvent(operation, currentNode.NodeId, changes)));
        }

        /// <summary>
        /// Records sight of every outgoing edge and destination at the active run position.
        /// </summary>
        /// <returns>The save-backed discovery command result.</returns>
        public WorldMapCommandResult ObserveCurrentExits()
        {
            if (!TryGetCurrentNode(out WorldNodeDefinition currentNode, out string errorMessage))
                return WorldMapCommandResult.Rejected(errorMessage);

            const WorldDiscoveryOperation operation = WorldDiscoveryOperation.ObservedExits;
            if (!TryResumePending(operation, currentNode.NodeId, out WorldMapCommandResult pendingResult))
                return pendingResult;
            if (pendingResult != null)
                return pendingResult;

            List<WorldDiscoveryChange> changes = new List<WorldDiscoveryChange>();
            HashSet<string> changedTargetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorldEdgeDefinition edge in world.GetOutgoingEdges(currentNode.NodeId))
            {
                if (session.Profile.GetEdgeDiscoveryState(edge.EdgeId) < EdgeDiscoveryState.Sighted)
                    changes.Add(WorldDiscoveryChange.ForEdge(edge.EdgeId, EdgeDiscoveryState.Sighted));

                if (session.Profile.GetNodeDiscoveryState(edge.ToNodeId) < NodeDiscoveryState.Sighted &&
                    changedTargetIds.Add(edge.ToNodeId))
                {
                    changes.Add(WorldDiscoveryChange.ForNode(edge.ToNodeId, NodeDiscoveryState.Sighted));
                }
            }

            if (changes.Count == 0)
                return WorldMapCommandResult.NoChange();

            foreach (WorldDiscoveryChange change in changes)
                Apply(change);

            return Persist(
                new PendingDiscovery(
                    operation,
                    currentNode.NodeId,
                    CreateEvent(operation, currentNode.NodeId, changes)));
        }

        /// <summary>
        /// Records traversal of one edge that leaves the active run's current node without moving the run.
        /// </summary>
        /// <param name="edgeId">The stable ID of the completed directed edge.</param>
        /// <returns>The save-backed discovery command result.</returns>
        public WorldMapCommandResult CompleteTraversal(string edgeId)
        {
            if (!TryGetCurrentNode(out WorldNodeDefinition currentNode, out string errorMessage))
                return WorldMapCommandResult.Rejected(errorMessage);
            if (!world.TryGetEdge(edgeId, out WorldEdgeDefinition edge))
                return WorldMapCommandResult.Rejected($"World edge '{edgeId}' does not exist.");
            if (!string.Equals(edge.FromNodeId, currentNode.NodeId, StringComparison.Ordinal))
                return WorldMapCommandResult.Rejected(
                    $"World edge '{edgeId}' is not an exit from '{currentNode.NodeId}'.");

            const WorldDiscoveryOperation operation = WorldDiscoveryOperation.TraversedEdge;
            if (!TryResumePending(operation, edge.EdgeId, out WorldMapCommandResult pendingResult))
                return pendingResult;
            if (pendingResult != null)
                return pendingResult;

            if (session.Profile.GetEdgeDiscoveryState(edge.EdgeId) == EdgeDiscoveryState.Traversed)
                return WorldMapCommandResult.NoChange();

            WorldDiscoveryChange[] changes =
            {
                WorldDiscoveryChange.ForEdge(edge.EdgeId, EdgeDiscoveryState.Traversed)
            };
            session.Profile.AdvanceEdgeDiscovery(edge.EdgeId, EdgeDiscoveryState.Traversed);
            return Persist(
                new PendingDiscovery(operation, edge.EdgeId, CreateEvent(operation, edge.EdgeId, changes)));
        }

        private void ValidateWorldBinding()
        {
            ProfileState profile = session.Profile;
            if (!string.Equals(
                    profile.WorldDefinitionId,
                    world.WorldDefinitionId,
                    StringComparison.Ordinal) ||
                profile.WorldDefinitionVersion != world.Version)
            {
                throw new InvalidOperationException(
                    "The profile and world definition identities do not match.");
            }

            foreach (NodeDiscovery discovery in profile.NodeDiscoveries)
            {
                if (!world.TryGetNode(discovery.NodeId, out _))
                    throw new InvalidOperationException(
                        $"Profile node discovery '{discovery.NodeId}' does not exist in the world definition.");
            }

            foreach (EdgeDiscovery discovery in profile.EdgeDiscoveries)
            {
                if (!world.TryGetEdge(discovery.EdgeId, out _))
                    throw new InvalidOperationException(
                        $"Profile edge discovery '{discovery.EdgeId}' does not exist in the world definition.");
            }

            foreach (WorldEdgeDefinition edge in world.Edges)
            {
                if (!world.TryGetNode(edge.FromNodeId, out _) ||
                    !world.TryGetNode(edge.ToNodeId, out _))
                {
                    throw new InvalidOperationException(
                        $"World edge '{edge.EdgeId}' has an unresolved endpoint.");
                }
            }
        }

        private bool TryGetCurrentNode(
            out WorldNodeDefinition currentNode,
            out string errorMessage)
        {
            currentNode = null;
            if (!session.IsRunActive())
            {
                errorMessage = "No active run is available.";
                return false;
            }

            string nodeId = session.GetCurrentRun().WorldNodeId;
            if (!world.TryGetNode(nodeId, out currentNode))
            {
                errorMessage = $"Current world node '{nodeId}' does not exist.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private bool TryResumePending(
            WorldDiscoveryOperation operation,
            string operationId,
            out WorldMapCommandResult result)
        {
            if (pendingDiscovery == null)
            {
                result = null;
                return true;
            }

            if (pendingDiscovery.Operation == operation &&
                string.Equals(pendingDiscovery.OperationId, operationId, StringComparison.Ordinal))
            {
                result = Persist(pendingDiscovery);
                return true;
            }

            result = WorldMapCommandResult.Rejected(
                "Another discovery operation is waiting for its profile save retry.");
            return false;
        }

        private WorldMapCommandResult Persist(PendingDiscovery discovery)
        {
            pendingDiscovery = discovery;
            SaveStoreResult saveResult = saveStore.SaveProfile(session.Profile);
            if (saveResult.IsFailure)
                return WorldMapCommandResult.PersistenceFailed(saveResult);

            WorldDiscoveryEvent discoveryEvent = pendingDiscovery.DiscoveryEvent;
            pendingDiscovery = null;
            OnDiscovery?.Invoke(discoveryEvent);
            return WorldMapCommandResult.Applied(discoveryEvent);
        }

        private RouteChoiceCommandResult PersistPendingRoute()
        {
            RunState run = session.GetCurrentRun();
            SaveStoreResult saveResult = saveStore.SaveRun(run);
            if (saveResult.IsFailure)
            {
                return RouteChoiceCommandResult.PersistenceFailed(
                    RouteChoicePersistenceTarget.Run,
                    saveResult.Type,
                    saveResult.ErrorMessage);
            }

            RouteChosenEvent routeEvent = new RouteChosenEvent(
                run.RunId,
                pendingRouteEdgeId,
                selectionNodeId,
                pendingRouteDestinationNodeId,
                pendingRouteDay);
            committedRoute = routeEvent;
            ChoiceState = RouteChoiceState.Committed;
            OnRouteChosen?.Invoke(routeEvent);
            return RouteChoiceCommandResult.Applied(routeEvent);
        }

        private static RouteChoiceCommandResult FromTraversalFailure(
            WorldMapCommandResult traversalResult)
        {
            if (traversalResult.Status == WorldMapCommandStatus.PersistenceFailed)
            {
                return RouteChoiceCommandResult.PersistenceFailed(
                    RouteChoicePersistenceTarget.Profile,
                    traversalResult.PersistenceResultType.Value,
                    traversalResult.ErrorMessage);
            }

            return RouteChoiceCommandResult.Rejected(traversalResult.ErrorMessage);
        }

        private void ResetChoiceIfRunChanged()
        {
            if (ChoiceState == RouteChoiceState.Inactive)
                return;
            if (session.IsRunActive() &&
                string.Equals(
                    session.GetCurrentRun().RunId,
                    selectionRunId,
                    StringComparison.Ordinal))
            {
                return;
            }

            selectionRunId = null;
            selectionNodeId = null;
            pendingRouteEdgeId = null;
            pendingRouteDestinationNodeId = null;
            committedRoute = null;
            ChoiceState = RouteChoiceState.Inactive;
        }

        private void Apply(WorldDiscoveryChange change)
        {
            if (change.NodeId != null)
            {
                session.Profile.AdvanceNodeDiscovery(
                    change.NodeId,
                    change.NodeState.Value);
                return;
            }

            session.Profile.AdvanceEdgeDiscovery(
                change.EdgeId,
                change.EdgeState.Value);
        }

        private static WorldDiscoveryEvent CreateEvent(
            WorldDiscoveryOperation operation,
            string operationId,
            IEnumerable<WorldDiscoveryChange> changes)
        {
            List<WorldDiscoveryChange> snapshot = new List<WorldDiscoveryChange>(changes);
            return new WorldDiscoveryEvent(
                operation,
                operationId,
                new ReadOnlyCollection<WorldDiscoveryChange>(snapshot));
        }

        private sealed class PendingDiscovery
        {
            public PendingDiscovery(
                WorldDiscoveryOperation operation,
                string operationId,
                WorldDiscoveryEvent discoveryEvent)
            {
                Operation = operation;
                OperationId = operationId;
                DiscoveryEvent = discoveryEvent;
            }

            public WorldDiscoveryOperation Operation { get; }
            public string OperationId { get; }
            public WorldDiscoveryEvent DiscoveryEvent { get; }
        }
    }
}