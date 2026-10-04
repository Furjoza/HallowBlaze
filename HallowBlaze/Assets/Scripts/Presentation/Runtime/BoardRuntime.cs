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
            Dictionary<EntityId, GameObject> views)
        {
            this.request = request ?? throw new ArgumentNullException(nameof(request));
            this.runState = runState ?? throw new ArgumentNullException(nameof(runState));
            this.boardState = boardState ?? throw new ArgumentNullException(nameof(boardState));
            mutableViews = views ?? throw new ArgumentNullException(nameof(views));
            readOnlyViews = new ReadOnlyDictionary<EntityId, GameObject>(mutableViews);
            PlayerId = playerId;
            controller = new TurnController(boardState, runState, playerId);
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

        /// <summary>Releases resolver ownership and removes every Unity view reference from the registry.</summary>
        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            mutableViews.Clear();
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
