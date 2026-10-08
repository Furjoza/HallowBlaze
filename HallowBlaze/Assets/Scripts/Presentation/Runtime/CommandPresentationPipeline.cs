using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Independent input owners that must not be cleared by completing presentation.</summary>
    [Flags]
    public enum PresentationInputBlock
    {
        /// <summary>No external input owner is blocking.</summary>
        None = 0,
        /// <summary>The board has not finished setup.</summary>
        Setup = 1,
        /// <summary>A modal owns input while building or canceling a draft.</summary>
        Modal = 2,
        /// <summary>The owning view or lifecycle has disabled input.</summary>
        Disabled = 4
    }

    /// <summary>Transient ownership of the one admitted submission.</summary>
    public enum PresentationGateState
    {
        /// <summary>No submission is resolving or replaying.</summary>
        Idle,
        /// <summary>The domain is producing the complete result.</summary>
        Resolving,
        /// <summary>The resolved events are being replayed sequentially.</summary>
        Presenting
    }

    /// <summary>Combines independent input blocks with submission ownership on the Unity main thread.</summary>
    public sealed class PresentationGate
    {
        /// <summary>Gets external blocks; setup is blocked until explicitly released by its owner.</summary>
        public PresentationInputBlock Blocks { get; private set; } = PresentationInputBlock.Setup;
        /// <summary>Gets the current submission phase, independent of external blocks.</summary>
        public PresentationGateState State { get; private set; }
        /// <summary>Gets whether an idle, initialized presentation can admit input.</summary>
        public bool CanSubmit => Blocks == PresentationInputBlock.None && State == PresentationGateState.Idle;

        /// <summary>Sets only the named external owner's block without releasing an active submission.</summary>
        /// <param name="reason">One or more defined external block flags, but not None.</param>
        /// <param name="blocked">Whether these owners currently block input.</param>
        public void SetBlocked(PresentationInputBlock reason, bool blocked)
        {
            const PresentationInputBlock all = PresentationInputBlock.Setup |
                PresentationInputBlock.Modal | PresentationInputBlock.Disabled;
            if (reason == PresentationInputBlock.None || (reason & ~all) != 0)
                throw new ArgumentOutOfRangeException(nameof(reason));
            Blocks = blocked ? Blocks | reason : Blocks & ~reason;
        }

        internal bool TryEnter()
        {
            if (!CanSubmit)
                return false;
            State = PresentationGateState.Resolving;
            return true;
        }

        internal void BeginPresentation() => State = PresentationGateState.Presenting;
        internal void Release() => State = PresentationGateState.Idle;
    }

    /// <summary>Supplies a complete intent without resolving a draft or selecting gameplay rules.</summary>
    public interface ICommandSource
    {
        /// <summary>Consumes at most one complete intent; returns false for an unfinished or absent draft.</summary>
        /// <param name="command">The complete command, or null when no intent is ready.</param>
        /// <returns>Whether a non-null intent was consumed.</returns>
        bool TryTakeCommand(out PlayerCommand command);
        /// <summary>Discards a draft for free, without submitting any command.</summary>
        void CancelDraft();
    }

    /// <summary>Stable presentation-only failure categories; these are never gameplay events.</summary>
    public enum PresentationDiagnosticCode
    {
        /// <summary>No handler was registered for the exact event type.</summary>
        UnknownEvent,
        /// <summary>A handler or presentation callback failed.</summary>
        HandlerFailed,
        /// <summary>Replay was canceled after domain resolution.</summary>
        Canceled,
        /// <summary>The resolver threw; any already-mutated domain state remains authoritative.</summary>
        ResolutionFailed
    }

    /// <summary>Identifies an aborted replay and exposes secondary diagnostic/recovery faults.</summary>
    public sealed class PresentationDiagnostic
    {
        private readonly List<Exception> recoveryErrors = new List<Exception>();

        internal PresentationDiagnostic(PresentationDiagnosticCode code, int index, GameEvent gameEvent, Exception error)
        {
            Code = code;
            EventIndex = index;
            EventType = gameEvent?.GetType().FullName;
            Error = error;
            RecoveryErrors = recoveryErrors.AsReadOnly();
        }

        /// <summary>Gets the reason remaining visual events were aborted.</summary>
        public PresentationDiagnosticCode Code { get; }
        /// <summary>Gets the zero-based event index, or -1 for a failure before replay.</summary>
        public int EventIndex { get; }
        /// <summary>Gets the exact CLR event identity, or null before an event was selected.</summary>
        public string EventType { get; }
        /// <summary>Gets the original exception, if any.</summary>
        public Exception Error { get; }
        /// <summary>Gets callback/recovery errors without losing the original diagnostic or gate release.</summary>
        public IReadOnlyList<Exception> RecoveryErrors { get; }

        internal void AddRecoveryError(Exception error) => recoveryErrors.Add(error);
    }

    /// <summary>Replays one event at a time using exact-type handlers, never resolving gameplay.</summary>
    public sealed class OrderedEventDispatcher
    {
        private readonly Dictionary<Type, Func<GameEvent, CancellationToken, Task>> handlers =
            new Dictionary<Type, Func<GameEvent, CancellationToken, Task>>();
        private readonly Action<PresentationDiagnostic> reportDiagnostic;
        private readonly Action resynchronize;
        private bool replaying;

        /// <summary>Creates a dispatcher with mandatory diagnostic and authoritative recovery sinks.</summary>
        /// <param name="reportDiagnostic">Receives the identity of an aborted event.</param>
        /// <param name="resynchronize">Snaps views and refreshes HUD from domain state; never resolves again.</param>
        public OrderedEventDispatcher(Action<PresentationDiagnostic> reportDiagnostic, Action resynchronize)
        {
            this.reportDiagnostic = reportDiagnostic ?? throw new ArgumentNullException(nameof(reportDiagnostic));
            this.resynchronize = resynchronize ?? throw new ArgumentNullException(nameof(resynchronize));
        }

        /// <summary>Registers exactly one handler for an event type before replay begins.</summary>
        /// <typeparam name="TEvent">The concrete existing domain event type.</typeparam>
        /// <param name="handler">Main-thread handler that completes all effects before returning and honors cancellation.</param>
        /// <exception cref="InvalidOperationException">Registration is attempted during replay.</exception>
        /// <exception cref="ArgumentException">This type already has a handler.</exception>
        public void Register<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : GameEvent
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (replaying)
                throw new InvalidOperationException("Handlers cannot change during replay.");
            handlers.Add(typeof(TEvent), (gameEvent, token) => handler((TEvent)gameEvent, token));
        }

        /// <summary>Awaits events in resolver order; unknown events, exceptions and cancellation abort and recover once.</summary>
        /// <param name="events">The immutable resolved event sequence.</param>
        /// <param name="token">Cooperative replay cancellation; already-resolved domain effects are not rolled back.</param>
        /// <returns>Null on completion, otherwise the diagnostic including recovery faults.</returns>
        public async Task<PresentationDiagnostic> ReplayAsync(IReadOnlyList<GameEvent> events, CancellationToken token = default)
        {
            if (events == null)
                throw new ArgumentNullException(nameof(events));
            if (replaying)
                throw new InvalidOperationException("Only one replay may own this dispatcher.");
            replaying = true;
            int index = -1;
            GameEvent current = null;
            try
            {
                for (index = 0; index < events.Count; index++)
                {
                    current = events[index];
                    token.ThrowIfCancellationRequested();
                    if (current == null || !handlers.TryGetValue(current.GetType(), out var handler))
                        return Recover(new PresentationDiagnostic(PresentationDiagnosticCode.UnknownEvent, index, current, null));
                    Task pending = handler(current, token);
                    if (pending == null)
                        throw new InvalidOperationException("A presentation handler must return a completion task.");
                    await pending;
                    token.ThrowIfCancellationRequested();
                }
                return null;
            }
            catch (Exception error)
            {
                var code = error is OperationCanceledException
                    ? PresentationDiagnosticCode.Canceled : PresentationDiagnosticCode.HandlerFailed;
                return Recover(new PresentationDiagnostic(code, index, current, error));
            }
            finally
            {
                replaying = false;
            }
        }

        internal PresentationDiagnostic Recover(PresentationDiagnostic diagnostic)
        {
            try { reportDiagnostic(diagnostic); }
            catch (Exception error) { diagnostic.AddRecoveryError(error); }
            try { resynchronize(); }
            catch (Exception error) { diagnostic.AddRecoveryError(error); }
            return diagnostic;
        }
    }

    /// <summary>Distinguishes admission, domain rejection and presentation failure without retrying a resolved turn.</summary>
    public enum CommandSubmissionStatus
    {
        /// <summary>Input ownership, runtime disposal, or a terminal controller prevented resolution.</summary>
        Blocked,
        /// <summary>The source had no complete command.</summary>
        NoCommand,
        /// <summary>Cancellation before resolution was free.</summary>
        Canceled,
        /// <summary>The domain rejected this one resolver invocation without effects.</summary>
        Rejected,
        /// <summary>The accepted result was completely presented.</summary>
        Presented,
        /// <summary>Replay or resolution failed; any returned accepted result remains applied.</summary>
        PresentationFailed
    }

    /// <summary>Retains domain acceptance independently of the success of visual replay.</summary>
    public sealed class CommandSubmission
    {
        internal CommandSubmission(CommandSubmissionStatus status, TurnResult result = null, PresentationDiagnostic diagnostic = null)
        {
            Status = status;
            Result = result;
            Diagnostic = diagnostic;
        }

        /// <summary>Gets admission/replay status, not a second domain verdict.</summary>
        public CommandSubmissionStatus Status { get; }
        /// <summary>Gets the single resolved result, including when its accepted replay failed.</summary>
        public TurnResult Result { get; }
        /// <summary>Gets a controlled replay/resolution failure, if present.</summary>
        public PresentationDiagnostic Diagnostic { get; }
    }

    /// <summary>
    /// Owns one main-thread input-to-replay path for a BoardRuntime. Acquire before Resolve, release in finally.
    /// The lifecycle owner must dispose this coordinator before replacing its runtime; it never owns domain persistence.
    /// </summary>
    public sealed class CommandPresentationCoordinator : IDisposable
    {
        private readonly BoardRuntime runtime;
        private readonly OrderedEventDispatcher dispatcher;
        private readonly Action<CommandRejectionCode> rejected;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool disposed;

        /// <summary>Composes the existing runtime/controller with its gate and one event dispatcher.</summary>
        public CommandPresentationCoordinator(BoardRuntime runtime, PresentationGate gate,
            OrderedEventDispatcher dispatcher, Action<CommandRejectionCode> rejected)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Gate = gate ?? throw new ArgumentNullException(nameof(gate));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.rejected = rejected ?? throw new ArgumentNullException(nameof(rejected));
        }

        /// <summary>Gets this path's presentation and external-input ownership.</summary>
        public PresentationGate Gate { get; }
        /// <summary>Gets the actual number of controller invocations for transient diagnostics.</summary>
        public int ResolutionCount { get; private set; }
        /// <summary>Gets whether a complete command could be admitted without touching the controller.</summary>
        public bool CanSubmit => !disposed && !runtime.IsDisposed && Gate.CanSubmit &&
            !runtime.Controller.IsResolving && !runtime.Controller.IsTerminal;

        /// <summary>Submits one complete command, never a modal draft; null is a free no-command result.</summary>
        /// <param name="command">Complete player intent.</param>
        /// <param name="token">Cancellation before resolution is free; afterward only replay is canceled.</param>
        /// <returns>Admission, the sole domain result, and any replay diagnostic.</returns>
        public Task<CommandSubmission> SubmitAsync(PlayerCommand command, CancellationToken token = default)
            => SubmitCoreAsync(() => command, token);

        /// <summary>Consumes at most one source intent only after admission; blocked sources are not sampled.</summary>
        /// <param name="source">Extensible source that can build/cancel drafts without domain effects.</param>
        /// <param name="token">Cooperative cancellation.</param>
        /// <returns>The single submission outcome.</returns>
        public Task<CommandSubmission> SubmitFromAsync(ICommandSource source, CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            return SubmitCoreAsync(() => source.TryTakeCommand(out PlayerCommand command) ? command : null, token);
        }

        private async Task<CommandSubmission> SubmitCoreAsync(Func<PlayerCommand> takeCommand, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return new CommandSubmission(CommandSubmissionStatus.Canceled);
            if (!CanSubmit || !Gate.TryEnter())
                return new CommandSubmission(CommandSubmissionStatus.Blocked);
            try
            {
                PlayerCommand command = takeCommand();
                if (token.IsCancellationRequested || disposed)
                    return new CommandSubmission(CommandSubmissionStatus.Canceled);
                if (command == null)
                    return new CommandSubmission(CommandSubmissionStatus.NoCommand);
                if (runtime.IsDisposed || Gate.Blocks != PresentationInputBlock.None ||
                    runtime.Controller.IsResolving || runtime.Controller.IsTerminal)
                    return new CommandSubmission(CommandSubmissionStatus.Blocked);

                TurnResult result;
                try
                {
                    ResolutionCount++;
                    result = runtime.Controller.Resolve(command);
                }
                catch (Exception error)
                {
                    var diagnostic = dispatcher.Recover(new PresentationDiagnostic(
                        PresentationDiagnosticCode.ResolutionFailed, -1, null, error));
                    return new CommandSubmission(CommandSubmissionStatus.PresentationFailed, null, diagnostic);
                }
                if (!result.Accepted)
                {
                    rejected(((RejectedTurnResult)result).RejectionCode);
                    return new CommandSubmission(CommandSubmissionStatus.Rejected, result);
                }

                Gate.BeginPresentation();
                using (var replayCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
                {
                    PresentationDiagnostic diagnostic = await dispatcher.ReplayAsync(result.Events, replayCancellation.Token);
                    return new CommandSubmission(diagnostic == null ? CommandSubmissionStatus.Presented :
                        CommandSubmissionStatus.PresentationFailed, result, diagnostic);
                }
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>Stops new input and requests cooperative replay cancellation; the active call releases its gate.</summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
        }
    }
}