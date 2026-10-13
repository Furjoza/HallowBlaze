using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Resolution;
using UnityEngine;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>
    /// Owns one live board's authoritative domain state, resolver, and Unity view bindings.
    /// The runtime is reconstructed for every board and is never persisted.
    /// </summary>
    public sealed class BoardRuntime : IDisposable
    {
        private readonly Dictionary<EntityId, GameObject> mutableViews;
        private readonly ReadOnlyDictionary<EntityId, GameObject> readOnlyViews;
        private readonly List<ShamblerState> mutableEnemies;
        private readonly ReadOnlyCollection<ShamblerState> readOnlyEnemies;
        private BoardRequest request;
        private RunState runState;
        private BoardState boardState;
        private TurnController controller;
        private bool isDisposed;

        internal BoardRuntime(
            BoardRequest request,
            RunState runState,
            BoardState boardState,
            EntityId playerId,
            Dictionary<EntityId, GameObject> views,
            IEnumerable<ShamblerState> enemies)
        {
            this.request = request ?? throw new ArgumentNullException(nameof(request));
            this.runState = runState ?? throw new ArgumentNullException(nameof(runState));
            this.boardState = boardState ?? throw new ArgumentNullException(nameof(boardState));
            mutableViews = views ?? throw new ArgumentNullException(nameof(views));
            readOnlyViews = new ReadOnlyDictionary<EntityId, GameObject>(mutableViews);
            mutableEnemies = new List<ShamblerState>(EnemyInitiativeOrder.Create(enemies));
            readOnlyEnemies = mutableEnemies.AsReadOnly();
            PlayerId = playerId;
            var enemyPhases = new EnemyBatchTurnPhases(readOnlyEnemies);
            enemyPhases.PlanInitialIntents(boardState, playerId);
            controller = new TurnController(boardState, runState, playerId, phases: enemyPhases.Handlers);
        }

        /// <summary>Gets whether this runtime has released its controller and view bindings.</summary>
        public bool IsDisposed => isDisposed;

        /// <summary>Gets the immutable request that identifies this board instance.</summary>
        /// <exception cref="ObjectDisposedException">The board runtime has been disposed.</exception>
        public BoardRequest Request
        {
            get
            {
                EnsureActive();
                return request;
            }
        }

        /// <summary>Gets the active run referenced by this board without taking ownership of its persistence.</summary>
        /// <exception cref="ObjectDisposedException">The board runtime has been disposed.</exception>
        public RunState RunState
        {
            get
            {
                EnsureActive();
                return runState;
            }
        }

        /// <summary>Gets the sole authoritative gameplay board for this runtime.</summary>
        /// <exception cref="ObjectDisposedException">The board runtime has been disposed.</exception>
        public BoardState BoardState
        {
            get
            {
                EnsureActive();
                return boardState;
            }
        }

        /// <summary>Gets the stable board-local identifier assigned to the player.</summary>
        public EntityId PlayerId { get; }

        /// <summary>Gets the single command resolver owned for this board lifetime.</summary>
        /// <exception cref="ObjectDisposedException">The board runtime has been disposed.</exception>
        public TurnController Controller
        {
            get
            {
                EnsureActive();
                return controller;
            }
        }

        /// <summary>Gets a read-only mapping from authoritative entity IDs to their Unity views.</summary>
        /// <exception cref="ObjectDisposedException">The board runtime has been disposed.</exception>
        public IReadOnlyDictionary<EntityId, GameObject> Views
        {
            get
            {
                EnsureActive();
                return readOnlyViews;
            }
        }

        /// <summary>
        /// Gets the structurally read-only roster in ascending initiative order. States remain owned
        /// by the controller; presentation may read retained plans but must not consume, lock, or reset them.
        /// Initial plans already exist before this runtime is published. Disposal empties captured rosters.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The board runtime has been disposed.</exception>
        public IReadOnlyList<ShamblerState> Enemies
        {
            get
            {
                EnsureActive();
                return readOnlyEnemies;
            }
        }

        /// <summary>Releases resolver and enemy roster ownership and clears borrowed view references without destroying views.</summary>
        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            mutableViews.Clear();
            mutableEnemies.Clear();
            controller = null;
            boardState = null;
            runState = null;
            request = null;
        }

        private void EnsureActive()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(BoardRuntime));
        }
    }
}
