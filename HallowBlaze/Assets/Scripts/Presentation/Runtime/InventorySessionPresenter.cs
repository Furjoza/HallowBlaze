using System;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Resolution;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Minimal bag test/debug adapter; owns a modal draft and routes Finish through the existing sole coordinator.</summary>
    public sealed class InventorySessionPresenter : IDisposable
    {
        private readonly BoardRuntime runtime;
        private readonly CommandPresentationCoordinator coordinator;
        private readonly Func<bool> inputAllowed;
        private bool disposed;
        private bool submitting;

        /// <summary>Borrows one board lifetime; this adapter grants no items, UI bindings or free gameplay-close action.</summary>
        /// <param name="runtime">The sole board/run/controller owner.</param>
        /// <param name="coordinator">The same command path used by walking and interactions.</param>
        /// <param name="inputAllowed">Optional lifecycle permission, including pause; it is rechecked at Finish.</param>
        /// <exception cref="ArgumentNullException">Runtime or coordinator is null.</exception>
        public InventorySessionPresenter(BoardRuntime runtime, CommandPresentationCoordinator coordinator, Func<bool> inputAllowed = null)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            this.inputAllowed = inputAllowed;
        }

        /// <summary>Gets whether this adapter owns an unfinished bag session; Finish is its only normal close.</summary>
        public bool IsOpen { get; private set; }
        /// <summary>Gets the transient draft while open, or null; it never writes run state directly.</summary>
        public InventoryDraft Draft { get; private set; }
        /// <summary>Gets the last Finish outcome for debug feedback; rejected drafts are rebased to the current run.</summary>
        public CommandSubmission LastSubmission { get; private set; }

        /// <summary>Opens a free draft only on an idle, live, unpaused and unobstructed gameplay board.</summary>
        /// <returns>False without mutation if another input owner, replay or terminal state blocks opening.</returns>
        public bool TryOpen()
        {
            if (!CanUse() || IsOpen || submitting || !coordinator.CanSubmit) return false;
            Draft = new InventoryDraft(runtime.RunState);
            LastSubmission = null;
            IsOpen = true;
            coordinator.Gate.SetBlocked(PresentationInputBlock.Inventory, true);
            return true;
        }

        /// <summary>Submits exactly one complete proposal, including no-op, without releasing unrelated input owners.</summary>
        /// <param name="token">Pre-resolution cancellation keeps the draft open; later cancellation affects replay only.</param>
        /// <returns>Blocked for rapid/pause input; rejected proposals remain open and rebase, accepted ones close after replay/recovery.</returns>
        public async Task<CommandSubmission> FinishAsync(CancellationToken token = default)
        {
            if (!CanUse() || !IsOpen || submitting || coordinator.Gate.State != PresentationGateState.Idle ||
                coordinator.Gate.Blocks != PresentationInputBlock.Inventory)
                return new CommandSubmission(CommandSubmissionStatus.Blocked);
            var command = Draft.CreateCommand();
            submitting = true;
            coordinator.Gate.SetBlocked(PresentationInputBlock.Inventory, false);
            try
            {
                LastSubmission = await coordinator.SubmitAsync(command, token);
                if (LastSubmission.Result?.Accepted == true) Close();
                else if (LastSubmission.Status == CommandSubmissionStatus.Rejected && !disposed && !runtime.IsDisposed)
                    Draft = new InventoryDraft(runtime.RunState);
                return LastSubmission;
            }
            finally
            {
                submitting = false;
                if (!disposed && !runtime.IsDisposed && IsOpen)
                    coordinator.Gate.SetBlocked(PresentationInputBlock.Inventory, true);
            }
        }

        private bool CanUse() => !disposed && !runtime.IsDisposed && !runtime.Controller.IsResolving &&
            !runtime.Controller.IsTerminal && runtime.RunState.Status == RunStatus.Active &&
            runtime.RunState.Health > 0 && runtime.RunState.Satiety > 0 && (inputAllowed?.Invoke() ?? true);

        private void Close()
        {
            IsOpen = false;
            Draft = null;
            coordinator.Gate.SetBlocked(PresentationInputBlock.Inventory, false);
        }

        /// <summary>Abandons only the view's draft on board/lifecycle disposal, never as a normal free-close action.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Close();
        }
    }
}