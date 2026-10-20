using System;
using System.Linq;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;

namespace HallowBlaze.Core.Session
{
    /// <summary>
    /// Owns durable route boundaries; choosing a road starts travel without recording arrival.
    /// </summary>
    public sealed class RouteTravelService
    {
        private readonly GameSession session;
        private readonly WorldDefinition world;
        private readonly ISaveStore saveStore;

        /// <summary>Creates one travel owner using the session's run and authored segment catalogue.</summary>
        /// <param name="session">The owner of the active run and profile.</param>
        /// <param name="world">The immutable world used to resolve roads and stable segments.</param>
        /// <param name="saveStore">The store that must commit a boundary before presentation proceeds.</param>
        public RouteTravelService(GameSession session, WorldDefinition world, ISaveStore saveStore)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.saveStore = saveStore ?? throw new ArgumentNullException(nameof(saveStore));
        }

        /// <summary>
        /// Starts and checkpoints a Pending leg without changing discovery, history, or the reached node.
        /// A failed save restores the previous boundary so a retry cannot allocate another traversal.
        /// Imported legacy-RNG runs retain legacy v1 generation on roads upgraded to ordinary versions 1 or 2.
        /// </summary>
        /// <param name="edgeId">An authored outgoing road from the reached checkpoint.</param>
        /// <returns>The durable checkpoint result; failure never authorizes board input.</returns>
        /// <exception cref="InvalidOperationException">The road is not legal or a leg is already active.</exception>
        public SaveStoreResult StartRoute(string edgeId)
        {
            RunState run = session.GetCurrentRun();
            if (!world.TryGetEdge(edgeId, out WorldEdgeDefinition edge) ||
                edge.FromNodeId != run.ReachedCheckpointNodeId || run.ActiveLeg != null)
                throw new InvalidOperationException("The road is not an available checkpoint exit.");

            int completedDays = run.CompletedDayCount;
            string checkpoint = run.ReachedCheckpointNodeId;
            int sequence = run.TraversalSequence;
            TravelHistoryEntry[] history = run.TravelHistory.ToArray();
            run.StartLeg(edge.EdgeId, edge.FromNodeId, edge.ToNodeId,
                edge.Segments.Select(segment =>
                {
                    bool preserveLegacy = run.RandomAlgorithmId == RunState.LegacyRandomAlgorithmId &&
                        segment.GenerationConfigurationId == "ordinary.board.v1" &&
                        (segment.GenerationVersion == 1 || segment.GenerationVersion == 2);
                    return new RouteSegmentState(segment.SegmentId,
                        preserveLegacy ? "legacy.board.v1" : segment.GenerationConfigurationId,
                        preserveLegacy ? 1 : segment.GenerationVersion);
                }));
            SaveStoreResult result = saveStore.SaveRun(run);
            if (result.IsFailure)
                run.RestoreTravelState(completedDays, checkpoint, sequence, history, null);
            return result;
        }

        /// <summary>
        /// Durably consumes the active intro before any rendering. Recovery of Consumed skips the intro.
        /// </summary>
        /// <param name="showIntro">True only after this call checkpoints Pending to Consumed successfully.</param>
        /// <returns>A failed checkpoint leaves Pending and never authorizes rendering or input.</returns>
        /// <exception cref="InvalidOperationException">There is no active route leg.</exception>
        public SaveStoreResult ConsumeIntro(out bool showIntro)
        {
            showIntro = false;
            RunState run = session.GetCurrentRun();
            ActiveRouteLegState previous = run.ActiveLeg ??
                throw new InvalidOperationException("No active route intro is available.");
            if (!run.TryConsumeIntro(previous.LegId))
                return SaveStoreResult.Success();

            SaveStoreResult result = saveStore.SaveRun(run);
            if (result.IsFailure)
                run.RestoreTravelState(run.CompletedDayCount, run.ReachedCheckpointNodeId,
                    run.TraversalSequence, run.TravelHistory, previous);
            else
                showIntro = true;
            return result;
        }

        /// <summary>
        /// Checkpoints an exact authored selection before its first render; repeated identical selections perform no write.
        /// Failure restores the previous selection and never authorizes board input.
        /// </summary>
        /// <exception cref="InvalidOperationException">The selection is foreign, conflicts or is not the current final consumed segment.</exception>
        public SaveStoreResult SelectLandmark(BoardRequest request, LandmarkRecord selection)
        {
            RunState run = session.GetCurrentRun();
            if (request?.Address == null || selection == null || !selection.Address.Equals(request.Address) ||
                !BoardOutcomeIdentityGuard.ValidateRequest(request, run, out _) ||
                run.ActiveLeg.IntroState != DayIntroState.Consumed)
                throw new InvalidOperationException("Landmark selection does not match current travel.");
            BoardAddressFactory.Create(run, world);
            LandmarkRecord previous = run.ActiveLandmark;
            if (!run.TrySelectLandmark(selection)) throw new InvalidOperationException("Conflicting landmark selection.");
            if (previous != null) return SaveStoreResult.Success();
            SaveStoreResult saved = saveStore.SaveRun(run);
            if (saved.IsFailure) run.RestoreLandmarkState(previous, run.CompletedLandmarks);
            return saved;
        }

        /// <summary>
        /// Advances one matching consumed segment and checkpoints its boundary once.
        /// Final completion atomically records the selected landmark result and arrival in the run;
        /// profile reconciliation must follow before atlas/input. Failure rolls both records back, not resolved board resources.
        /// </summary>
        /// <param name="request">The complete current segment identity; foreign or repeated requests are free.</param>
        /// <param name="checkpoint">The save result when admitted, or null when rejected.</param>
        /// <param name="arrived">True only when final arrival has been durably committed.</param>
        /// <param name="landmark">Trusted result resolved from the actual exit; required exactly when an authored selection is active.</param>
        /// <returns>Whether the current segment admitted this completion, independently of save success.</returns>
        public bool TryCompleteSegment(BoardRequest request, out SaveStoreResult checkpoint, out bool arrived, LandmarkRecord landmark = null)
        {
            checkpoint = null;
            arrived = false;
            RunState run = session.ActiveRun;
            if (request?.Address == null || run?.ActiveLeg == null ||
                !BoardOutcomeIdentityGuard.ValidateRequest(request, run, out _) ||
                run.ActiveLeg.IntroState != DayIntroState.Consumed)
                return false;

            BoardAddressFactory.Create(run, world);
            ActiveRouteLegState previous = run.ActiveLeg;
            int completedDays = run.CompletedDayCount;
            string reached = run.ReachedCheckpointNodeId;
            int sequence = run.TraversalSequence;
            TravelHistoryEntry[] history = run.TravelHistory.ToArray();
            LandmarkRecord selected = run.ActiveLandmark;
            LandmarkRecord[] completed = run.CompletedLandmarks.ToArray();
            if (selected != null ? !run.TryCompleteLandmark(landmark) : landmark != null) return false;
            bool final = previous.IsFinalSegment;
            if (final)
                run.TryCompleteLeg(previous.LegId, previous.CurrentSegment.SegmentId);
            else
                run.TryAdvanceSegment(previous.LegId, previous.CurrentSegment.SegmentId);

            checkpoint = saveStore.SaveRun(run);
            if (checkpoint.IsFailure)
            {
                run.RestoreTravelState(completedDays, reached, sequence, history, previous);
                run.RestoreLandmarkState(selected, completed);
            }
            else
                arrived = final;
            return true;
        }
    }
}