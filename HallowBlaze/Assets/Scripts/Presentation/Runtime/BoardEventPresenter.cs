using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.Turns.Contracts;
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

    /// <summary>The injectable guarded lifecycle boundary; production BoardOutcome binding belongs to M3.6.3.</summary>
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

        /// <summary>Composes isolated presentation; the caller synchronizes and releases Setup when ready.</summary>
        /// <param name="runtime">The active runtime; this presenter borrows rather than disposes it.</param>
        /// <param name="hud">Authoritative-value HUD sink.</param>
        /// <param name="feedback">Rejection and informational feedback sink.</param>
        /// <param name="outcomes">Guarded lifecycle sink, unbound to production in this child.</param>
        /// <param name="diagnostic">Mandatory failure sink for unknown events and controlled faults.</param>
        /// <param name="moveDuration">Finite nonnegative movement duration in unscaled seconds.</param>
        public BoardEventPresenter(BoardRuntime runtime, IRunHud hud, ITurnFeedback feedback,
            IBoardOutcomeSink outcomes, Action<PresentationDiagnostic> diagnostic, float moveDuration = 0.1f)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.hud = hud ?? throw new ArgumentNullException(nameof(hud));
            this.feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
            this.outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
            if (float.IsNaN(moveDuration) || float.IsInfinity(moveDuration) || moveDuration < 0f)
                throw new ArgumentOutOfRangeException(nameof(moveDuration));
            MoveDuration = moveDuration;
            Dispatcher = new OrderedEventDispatcher(diagnostic, SynchronizeFromState);
            Coordinator = new CommandPresentationCoordinator(runtime, new PresentationGate(), Dispatcher, feedback.ShowRejection);
            Dispatcher.Register<EntityMovedEvent>(MoveAsync);
            Dispatcher.Register<EntityWaitedEvent>((gameEvent, token) => ShowFeedback(gameEvent));
            Dispatcher.Register<InteractionPerformedEvent>((gameEvent, token) => ShowFeedback(gameEvent));
            Dispatcher.Register<ItemCollectedEvent>((gameEvent, token) => HideItem(gameEvent));
            Dispatcher.Register<FoodRestoredEvent>((gameEvent, token) => RefreshHud());
            Dispatcher.Register<ActionCostAppliedEvent>((gameEvent, token) => RefreshHud());
            Dispatcher.Register<ExitReachedEvent>((gameEvent, token) => NotifyOutcome(gameEvent));
            Dispatcher.Register<PlayerStarvedEvent>((gameEvent, token) => NotifyOutcome(gameEvent));
            Dispatcher.Register<PlayerDiedEvent>((gameEvent, token) => NotifyOutcome(gameEvent));
        }

        /// <summary>Gets the one command path borrowing the runtime's existing controller.</summary>
        public CommandPresentationCoordinator Coordinator { get; }
        /// <summary>Gets the extensible registry; future concrete events can register handlers before replay.</summary>
        public OrderedEventDispatcher Dispatcher { get; }
        /// <summary>Gets or sets whether movement interpolates; false uses the same handler with zero visual delay.</summary>
        public bool AnimationsEnabled { get; set; } = true;
        /// <summary>Gets movement duration in unscaled seconds; zero completes synchronously in either mode.</summary>
        public float MoveDuration { get; }

        /// <summary>
        /// Resynchronizes all surviving registered views and HUD from authoritative state without replaying outcomes.
        /// Removed entities are hidden, existing entities are snapped and activated, and Z is presentation-owned.
        /// A disposed runtime is not read. Missing views are reported after other views and the HUD are recovered.
        /// </summary>
        public void SynchronizeFromState()
        {
            if (runtime.IsDisposed)
                return;
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
            if (errors.Count != 0)
                throw new AggregateException("Registered views or HUD could not be fully synchronized.", errors);
        }

        private async Task MoveAsync(EntityMovedEvent gameEvent, CancellationToken token)
        {
            GameObject view = RequireView(gameEvent.EntityId);
            if (!runtime.BoardState.TryGetEntity(gameEvent.EntityId, out BoardEntityState entity) ||
                !entity.Position.Equals(gameEvent.To))
                throw new InvalidOperationException("Move event disagrees with authoritative position: " + gameEvent.EntityId);
            Vector3 origin = view.transform.position;
            Vector3 target = Position(entity.Position, origin.z);
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
            outcomes.TryNotify(runtime.Request, gameEvent);
            return Task.CompletedTask;
        }

        private GameObject RequireView(EntityId id)
        {
            if (!runtime.Views.TryGetValue(id, out GameObject view) || view == null)
                throw new InvalidOperationException("Missing registered view: " + id);
            return view;
        }

        private static Vector3 Position(GridPosition position, float depth) => new Vector3(position.X, position.Y, depth);

        /// <summary>Cancels this presenter's coordinator without destroying the borrowed runtime or its views.</summary>
        public void Dispose() => Coordinator.Dispose();
    }
}