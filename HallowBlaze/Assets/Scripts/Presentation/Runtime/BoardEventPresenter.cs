using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Presentation.Runtime.Interaction;
using HallowBlaze.Presentation.Runtime.Noise;
using UnityEngine;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Receives authoritative current resources, never event deltas or a mutable gameplay owner.</summary>
    public interface IRunHud
    {
        /// <summary>Replaces displayed resource values with the latest RunState values.</summary>
        /// <param name="health">Current run health.</param>
        /// <param name="food">Current run food after complete domain resolution.</param>
        void Refresh(int health, int food);
    }

    /// <summary>Presents stable rejection and informational feedback without applying gameplay effects.</summary>
    public interface ITurnFeedback
    {
        /// <summary>Shows the domain's stable, cost-free rejection reason.</summary>
        void ShowRejection(CommandRejectionCode reason);
        /// <summary>Shows Wait or interaction feedback without moving views or changing resources.</summary>
        void ShowEvent(GameEvent gameEvent);
    }

    /// <summary>Optionally bounds player-step audio to its movement presentation; instant steps are silent.</summary>
    public interface IPlayerStepAudio
    {
        /// <summary>Starts at most one short non-looping cue within the supplied unscaled movement duration.</summary>
        /// <param name="duration">Movement presentation duration in seconds; zero requests silence.</param>
        void BeginStep(float duration);
        /// <summary>Stops only this player's current step cue on completion, recovery or disposal.</summary>
        void StopStep();
    }

    /// <summary>The injectable guarded lifecycle boundary for ordered terminal events.</summary>
    public interface IBoardOutcomeSink
    {
        /// <summary>
        /// Receives ExitReached, PlayerStarved or PlayerDied only at its ordered replay position.
        /// Implementations must guard the active board identity and reject stale/duplicate outcomes without effects.
        /// </summary>
        /// <param name="request">The exact runtime request whose already-resolved outcome is being presented.</param>
        /// <param name="outcome">The existing terminal domain event; never recompute rules from views.</param>
        /// <returns>Whether the lifecycle owner accepted this outcome.</returns>
        bool TryNotify(BoardRequest request, GameEvent outcome);
    }

    /// <summary>
    /// Owns the reusable main-thread handlers and coordinator for registered board views.
    /// Animation affects only time: both modes use the same handlers and never mutate domain resources or occupancy.
    /// </summary>
    public sealed class BoardEventPresenter : IDisposable
    {
        private readonly BoardRuntime runtime;
        private readonly IRunHud hud;
        private readonly ITurnFeedback feedback;
        private readonly IBoardOutcomeSink outcomes;
        private readonly EnemyAttackPresenter attacks;
        private readonly EnemyIntentPresenter intents;
        private readonly Action<string> observeFact;
        private readonly Action<FactObservation> observeObservation;
        private readonly Dictionary<EntityId, (EnemyIntent Plan, GridPosition Source)> published =
            new Dictionary<EntityId, (EnemyIntent, GridPosition)>();
        private bool disposed;

        /// <summary>Composes isolated presentation; the caller synchronizes and releases Setup when ready.</summary>
        /// <param name="runtime">The active runtime; this presenter borrows rather than disposes it.</param>
        /// <param name="hud">Authoritative-value HUD sink.</param>
        /// <param name="feedback">Rejection and informational feedback sink.</param>
        /// <param name="outcomes">Guarded lifecycle sink for the active board.</param>
        /// <param name="diagnostic">Mandatory failure sink for unknown events and controlled faults.</param>
        /// <param name="moveDuration">Finite nonnegative movement duration in unscaled seconds.</param>
        /// <param name="intents">Optional unbound board-owned display. This presenter binds and closes its
        /// display lifetime, publishing at setup, recovery and completion without planning. Its GameObject is borrowed.</param>
        /// <param name="interactionInputAllowed">Optional production lifecycle permission for free target selection.</param>
        /// <param name="observeFact">Optional durable knowledge sink invoked only after presenting the corresponding actual noise or Shambler trap.</param>
        /// <param name="observeObservation">Optional typed mechanical observation sink for idempotent Field Notes discovery after actual replay.</param>
        public BoardEventPresenter(BoardRuntime runtime, IRunHud hud, ITurnFeedback feedback,
            IBoardOutcomeSink outcomes, Action<PresentationDiagnostic> diagnostic, float moveDuration = 0.1f,
            EnemyIntentPresenter intents = null, Func<bool> interactionInputAllowed = null, Action<string> observeFact = null,
            Action<FactObservation> observeObservation = null)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.hud = hud ?? throw new ArgumentNullException(nameof(hud));
            this.feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
            this.outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
            this.intents = intents;
            this.observeFact = observeFact;
            this.observeObservation = observeObservation;
            if (float.IsNaN(moveDuration) || float.IsInfinity(moveDuration) || moveDuration < 0f)
                throw new ArgumentOutOfRangeException(nameof(moveDuration));
            MoveDuration = moveDuration;
            attacks = new EnemyAttackPresenter(runtime, hud, feedback, moveDuration);
            ObstacleDisplay = new ObstacleWorkPresenter(runtime);
            DiggingDisplay = new ShovelWorkPresenter(runtime);
            GroundToolsDisplay = new GroundToolPresenter(runtime);
            var noiseOwner = new GameObject("Board noise debug") { hideFlags = HideFlags.DontSave };
            NoiseDisplay = noiseOwner.AddComponent<BoardNoisePresenter>();
            NoiseDisplay.Bind(runtime.BoardState, () => disposed || runtime.IsDisposed || runtime.Controller.IsTerminal
                ? null : runtime.BoardState);
            Dispatcher = new OrderedEventDispatcher(diagnostic, SynchronizeFromState);
            Coordinator = new CommandPresentationCoordinator(runtime, new PresentationGate(), Dispatcher,
                feedback.ShowRejection, result =>
                {
                    RefreshIntents();
                    NoiseDisplay.Refresh();
                    ObstacleDisplay.Refresh();
                    DiggingDisplay.Refresh();
                    GroundToolsDisplay.Refresh();
                });
            var interactionOwner = new GameObject("Board interaction") { hideFlags = HideFlags.DontSave };
            InteractionDisplay = interactionOwner.AddComponent<InteractionPresenter>();
            InteractionDisplay.Bind(runtime, Coordinator, interactionInputAllowed);
            if (intents != null)
                intents.Bind(runtime.BoardState, () => disposed || runtime.IsDisposed || runtime.Controller.IsTerminal
                    ? null : runtime.BoardState);
            Dispatcher.Register<EntityMovedEvent>(MoveAsync);
            Dispatcher.Register<BoulderPushedEvent>(PushAsync);
            Dispatcher.Register<PressurePlateChangedEvent>((gameEvent, token) =>
            {
                RequirePlateGateView(gameEvent.Plate.Definition.TerrainId).Present(gameEvent.Plate);
                return ShowFeedback(gameEvent);
            });
            Dispatcher.Register<GateChangedEvent>((gameEvent, token) =>
            {
                RequirePlateGateView(gameEvent.Gate.Definition.TerrainId).Present(gameEvent.Gate);
                return ShowFeedback(gameEvent);
            });
            Dispatcher.Register<EntityWaitedEvent>((gameEvent, token) => ShowFeedback(gameEvent));
            Dispatcher.Register<InteractionPerformedEvent>((gameEvent, token) => ShowFeedback(gameEvent));
            Dispatcher.Register<ToolUsedEvent>((gameEvent, token) => ShowFeedback(gameEvent));
            Dispatcher.Register<ToolChargeSpentEvent>((gameEvent, token) => ShowFeedback(gameEvent));
            Dispatcher.Register<ToolPickedUpEvent>((gameEvent, token) =>
            {
                GroundToolsDisplay.Refresh();
                RefreshHud();
                return ShowFeedback(gameEvent);
            });
            Dispatcher.Register<ObstacleWorkedEvent>((gameEvent, token) =>
            {
                ObstacleDisplay.Present(gameEvent);
                return ShowFeedback(gameEvent);
            });
            Dispatcher.Register<ItemCollectedEvent>((gameEvent, token) => HideItem(gameEvent));
            Dispatcher.Register<CacheWorkedEvent>((gameEvent, token) =>
            {
                DiggingDisplay.Refresh();
                return ShowFeedback(gameEvent);
            });
            Dispatcher.Register<PitCreatedEvent>((gameEvent, token) =>
            {
                DiggingDisplay.Refresh();
                return ShowFeedback(gameEvent);
            });
            Dispatcher.Register<ShamblerTrappedEvent>((gameEvent, token) =>
            {
                DiggingDisplay.Refresh();
                ShowFeedback(gameEvent);
                observeFact?.Invoke(ShamblerTrappedEvent.FactId);
                observeObservation?.Invoke(FactObservation.ShamblerTrappedPresented);
                return Task.CompletedTask;
            });
            Dispatcher.Register<ListenerInvestigatedEvent>((gameEvent, token) =>
            {
                ShowFeedback(gameEvent);
                observeObservation?.Invoke(FactObservation.ListenerInvestigationPresented);
                return Task.CompletedTask;
            });
            Dispatcher.Register<FoodRestoredEvent>((gameEvent, token) => RefreshFood(gameEvent));
            Dispatcher.Register<ActionCostAppliedEvent>((gameEvent, token) => RefreshHud());
            Dispatcher.Register<NoiseEvent>((gameEvent, token) =>
            {
                NoiseDisplay.Refresh();
                ShowFeedback(gameEvent);
                if (gameEvent.ObservedFactId != null) observeFact?.Invoke(gameEvent.ObservedFactId);
                if (gameEvent.ObservedFactId == ObstacleClearingRules.AxeNoiseFactId)
                    observeObservation?.Invoke(FactObservation.AxeNoisePresented);
                return Task.CompletedTask;
            });
            Dispatcher.Register<EnemyAttackResolvedEvent>((gameEvent, token) =>
            {
                attacks.AnimationsEnabled = AnimationsEnabled;
                return attacks.ReplayAsync(gameEvent, token);
            });
            Dispatcher.Register<ExitReachedEvent>((gameEvent, token) => NotifyOutcome(gameEvent));
            Dispatcher.Register<PlayerStarvedEvent>((gameEvent, token) => NotifyOutcome(gameEvent));
            Dispatcher.Register<PlayerDiedEvent>((gameEvent, token) => NotifyOutcome(gameEvent));
        }

        /// <summary>Gets the one command path borrowing the runtime's existing controller.</summary>
        public CommandPresentationCoordinator Coordinator { get; }
        /// <summary>Gets the extensible registry; future concrete events can register handlers before replay.</summary>
        public OrderedEventDispatcher Dispatcher { get; }
        /// <summary>Gets this presenter's owned debug hearing display; snapshots are never recomputed by presentation.</summary>
        public BoardNoisePresenter NoiseDisplay { get; }
        /// <summary>Gets the owned free target picker, sharing this runtime's query and command gate.</summary>
        public InteractionPresenter InteractionDisplay { get; }
        /// <summary>Gets the owned domain-progress display and view-only clearing feedback adapter.</summary>
        public ObstacleWorkPresenter ObstacleDisplay { get; }
        /// <summary>Gets the owned marked-cache, diggable-ground and temporary-pit overlay with non-color lifetime/hold labels.</summary>
        public ShovelWorkPresenter DiggingDisplay { get; }
        /// <summary>Gets the owned exact-definition/charge display for ground tools and displaced replacements.</summary>
        public GroundToolPresenter GroundToolsDisplay { get; }
        /// <summary>Gets or sets whether movement and attacks animate; false uses the same handlers with zero visual delay.</summary>
        public bool AnimationsEnabled { get; set; } = true;
        /// <summary>Gets movement duration in unscaled seconds; zero completes synchronously in either mode.</summary>
        public float MoveDuration { get; }

        /// <summary>
        /// Resynchronizes all surviving registered views and HUD from authoritative state without replaying outcomes.
        /// Removed entities are hidden, existing entities are snapped and activated, and Z is presentation-owned.
        /// Publishes retained plans without planning; recovery and completion deduplicate the same batch.
        /// Terminal/disposed lifetimes clear intents, and a disposed runtime is not read.
        /// Missing views are reported after other views, HUD and intents are recovered.
        /// </summary>
        public void SynchronizeFromState()
        {
            (feedback as IPlayerStepAudio)?.StopStep();
            if (disposed || runtime.IsDisposed)
            {
                CloseIntents();
                NoiseDisplay.Dispose();
                ObstacleDisplay.Dispose();
                DiggingDisplay.Dispose();
                GroundToolsDisplay.Dispose();
                return;
            }
            var errors = new List<Exception>();
            foreach (var binding in runtime.Views)
            {
                try
                {
                    GameObject view = RequireView(binding.Key);
                    bool exists = runtime.BoardState.TryGetEntity(binding.Key, out BoardEntityState entity);
                    if (exists)
                        view.transform.position = Position(entity.Position, view.transform.position.z);
                    view.SetActive(exists);
                }
                catch (Exception error) { errors.Add(error); }
            }
            try { RefreshHud(); }
            catch (Exception error) { errors.Add(error); }
            try { RefreshIntents(); }
            catch (Exception error) { errors.Add(error); }
            try { NoiseDisplay.Refresh(); }
            catch (Exception error) { errors.Add(error); }
            try { ObstacleDisplay.Refresh(); }
            catch (Exception error) { errors.Add(error); }
            try { DiggingDisplay.Refresh(); }
            catch (Exception error) { errors.Add(error); }
            try { GroundToolsDisplay.Refresh(); }
            catch (Exception error) { errors.Add(error); }
            try
            {
                foreach (PressurePlateState plate in runtime.BoardState.GetPressurePlates()) RequirePlateGateView(plate.Definition.TerrainId).Present(plate);
                foreach (GateState gate in runtime.BoardState.GetGates()) RequirePlateGateView(gate.Definition.TerrainId).Present(gate);
            }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0)
                throw new AggregateException("Registered views or HUD could not be fully synchronized.", errors);
        }

        private void RefreshIntents()
        {
            if (ReferenceEquals(intents, null))
                return;
            if (disposed || runtime.IsDisposed || runtime.Controller.IsTerminal)
            {
                CloseIntents();
                return;
            }
            if (intents == null || intents.IsDisposed)
                throw new InvalidOperationException("The active board's required intent display is unavailable.");
            var live = new List<ShamblerState>();
            var liveListeners = new List<ListenerState>();
            var next = new Dictionary<EntityId, (EnemyIntent Plan, GridPosition Source)>();
            bool unchanged = true;
            foreach (ShamblerState enemy in runtime.Enemies)
            {
                if (!runtime.BoardState.TryGetEntity(enemy.ActorId, out BoardEntityState entity))
                    continue;
                live.Add(enemy);
                next.Add(enemy.ActorId, (enemy.LockedIntent, entity.Position));
                unchanged &= published.TryGetValue(enemy.ActorId, out var previous) &&
                    ReferenceEquals(previous.Plan, enemy.LockedIntent) && previous.Source.Equals(entity.Position) &&
                    intents.Views.TryGetValue(enemy.ActorId, out EnemyIntentView view) &&
                    view != null && view.gameObject.activeInHierarchy;
            }
            foreach (ListenerState listener in runtime.Listeners)
            {
                if (!runtime.BoardState.TryGetEntity(listener.ActorId, out BoardEntityState entity))
                    continue;
                liveListeners.Add(listener);
                next.Add(listener.ActorId, (listener.LockedIntent, entity.Position));
                unchanged &= published.TryGetValue(listener.ActorId, out var previous) &&
                    ReferenceEquals(previous.Plan, listener.LockedIntent) && previous.Source.Equals(entity.Position) &&
                    intents.Views.TryGetValue(listener.ActorId, out EnemyIntentView view) &&
                    view != null && view.gameObject.activeInHierarchy;
            }
            if (unchanged && published.Count == next.Count && intents.Views.Count == next.Count)
                return;
            intents.Publish(runtime.BoardState, live, liveListeners);
            published.Clear();
            foreach (var entry in next)
                published.Add(entry.Key, entry.Value);
        }

        private void CloseIntents()
        {
            if (intents != null)
                intents.Dispose();
            published.Clear();
        }

        private async Task MoveAsync(EntityMovedEvent gameEvent, CancellationToken token)
        {
            GameObject view = RequireView(gameEvent.EntityId);
            if (!runtime.BoardState.TryGetEntity(gameEvent.EntityId, out BoardEntityState entity) ||
                !entity.Position.Equals(gameEvent.To))
                throw new InvalidOperationException("Move event disagrees with authoritative position: " + gameEvent.EntityId);
            Vector3 origin = view.transform.position;
            Vector3 target = Position(entity.Position, origin.z);
            IPlayerStepAudio stepAudio = gameEvent.EntityId.Equals(runtime.PlayerId) ? feedback as IPlayerStepAudio : null;
            stepAudio?.BeginStep(AnimationsEnabled ? MoveDuration : 0f);
            try
            {
                if (AnimationsEnabled && MoveDuration > 0f)
                {
                    float elapsed = 0f;
                    while (elapsed < MoveDuration)
                    {
                        await Task.Yield();
                        token.ThrowIfCancellationRequested();
                        if (runtime.IsDisposed || view == null)
                            throw new InvalidOperationException("The board or moving view was disposed during presentation.");
                        elapsed += Time.unscaledDeltaTime;
                        view.transform.position = Vector3.Lerp(origin, target, Mathf.Clamp01(elapsed / MoveDuration));
                    }
                }
                token.ThrowIfCancellationRequested();
                view.transform.position = target;
                if (!runtime.BoardState.TryGetEntity(gameEvent.EntityId, out entity) || !entity.Position.Equals(gameEvent.To))
                    throw new InvalidOperationException("Authoritative position changed during presentation: " + gameEvent.EntityId);
                feedback.ShowEvent(gameEvent);
            }
            finally { stepAudio?.StopStep(); }
        }

        private async Task PushAsync(BoulderPushedEvent gameEvent, CancellationToken token)
        {
            GameObject playerView = RequireView(gameEvent.PlayerId);
            GameObject boulderView = RequireView(gameEvent.BoulderId);
            ValidatePushPositions(gameEvent);
            Vector3 playerOrigin = playerView.transform.position;
            Vector3 boulderOrigin = boulderView.transform.position;
            Vector3 playerTarget = Position(gameEvent.PlayerTo, playerOrigin.z);
            Vector3 boulderTarget = Position(gameEvent.BoulderTo, boulderOrigin.z);
            IPlayerStepAudio stepAudio = feedback as IPlayerStepAudio;
            stepAudio?.BeginStep(AnimationsEnabled ? MoveDuration : 0f);
            try
            {
                if (AnimationsEnabled && MoveDuration > 0f)
                {
                    float elapsed = 0f;
                    while (elapsed < MoveDuration)
                    {
                        await Task.Yield();
                        token.ThrowIfCancellationRequested();
                        if (runtime.IsDisposed || playerView == null || boulderView == null)
                            throw new InvalidOperationException("A push participant was disposed during presentation.");
                        elapsed += Time.unscaledDeltaTime;
                        float progress = Mathf.Clamp01(elapsed / MoveDuration);
                        playerView.transform.position = Vector3.Lerp(playerOrigin, playerTarget, progress);
                        boulderView.transform.position = Vector3.Lerp(boulderOrigin, boulderTarget, progress);
                    }
                }
                token.ThrowIfCancellationRequested();
                playerView.transform.position = playerTarget;
                boulderView.transform.position = boulderTarget;
                ValidatePushPositions(gameEvent);
                feedback.ShowEvent(gameEvent);
            }
            finally { stepAudio?.StopStep(); }
        }

        private void ValidatePushPositions(BoulderPushedEvent gameEvent)
        {
            if (!runtime.BoardState.TryGetEntity(gameEvent.PlayerId, out BoardEntityState player) ||
                !player.Position.Equals(gameEvent.PlayerTo) ||
                !runtime.BoardState.TryGetEntity(gameEvent.BoulderId, out BoardEntityState boulder) ||
                !boulder.Position.Equals(gameEvent.BoulderTo))
                throw new InvalidOperationException("Push event disagrees with authoritative participant positions.");
        }

        private Task RefreshFood(FoodRestoredEvent gameEvent)
        {
            RefreshHud();
            feedback.ShowEvent(gameEvent);
            return Task.CompletedTask;
        }

        private Task HideItem(ItemCollectedEvent gameEvent)
        {
            if (runtime.BoardState.TryGetEntity(gameEvent.ItemId, out _))
                throw new InvalidOperationException("Collected item still exists in authoritative state: " + gameEvent.ItemId);
            RequireView(gameEvent.ItemId).SetActive(false);
            return Task.CompletedTask;
        }

        private Task ShowFeedback(GameEvent gameEvent)
        {
            feedback.ShowEvent(gameEvent);
            return Task.CompletedTask;
        }

        private Task RefreshHud()
        {
            hud.Refresh(runtime.RunState.Health, runtime.RunState.Food);
            return Task.CompletedTask;
        }

        private Task NotifyOutcome(GameEvent gameEvent)
        {
            CloseIntents();
            NoiseDisplay.Dispose();
            InteractionDisplay.Dispose();
            ObstacleDisplay.Dispose();
            DiggingDisplay.Dispose();
            GroundToolsDisplay.Dispose();
            outcomes.TryNotify(runtime.Request, gameEvent);
            return Task.CompletedTask;
        }

        private PlateGateView RequirePlateGateView(EntityId id)
        {
            PlateGateView view = RequireView(id).GetComponent<PlateGateView>();
            return view != null ? view : throw new InvalidOperationException("Missing plate/gate presentation adapter: " + id);
        }

        private GameObject RequireView(EntityId id)
        {
            if (!runtime.Views.TryGetValue(id, out GameObject view) || view == null)
                throw new InvalidOperationException("Missing registered view: " + id);
            return view;
        }

        private static Vector3 Position(GridPosition position, float depth) => new Vector3(position.X, position.Y, depth);

        /// <summary>Cancels replay and closes owned attack/intent display lifetimes without destroying the borrowed runtime or views.</summary>
        public void Dispose()
        {
            (feedback as IPlayerStepAudio)?.StopStep();
            if (disposed)
                return;
            disposed = true;
            Coordinator.Dispose();
            attacks.Dispose();
            CloseIntents();
            NoiseDisplay.Dispose();
            InteractionDisplay.Dispose();
            ObstacleDisplay.Dispose();
            DiggingDisplay.Dispose();
            GroundToolsDisplay.Dispose();
            if (InteractionDisplay != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(InteractionDisplay.gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(InteractionDisplay.gameObject);
            }
            if (NoiseDisplay != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(NoiseDisplay.gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(NoiseDisplay.gameObject);
            }
        }
    }
}