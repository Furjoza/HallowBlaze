using System;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Owns one free, transient pocket selection; final keyboard/hotbar presentation belongs to M10.7.</summary>
    public sealed class QuickPocketPresenter : MonoBehaviour, IDisposable
    {
        private BoardRuntime runtime;
        private CommandPresentationCoordinator coordinator;
        private Func<bool> inputAllowed;
        private bool disposed;

        /// <summary>Gets the selected exact snapshot, or null for an ordinary unmodified next command.</summary>
        public QuickPocketOption Selection { get; private set; }

        /// <summary>Borrows one board lifetime and the existing sole command coordinator.</summary>
        public void Bind(BoardRuntime runtime, CommandPresentationCoordinator coordinator, Func<bool> inputAllowed = null)
        {
            if (this.runtime != null || disposed) throw new InvalidOperationException("A quick selection belongs to one board.");
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            this.inputAllowed = inputAllowed;
        }

        /// <summary>Chooses or replaces one legal pocket snapshot for free; pause, setup, replay and other modals cannot select.</summary>
        public bool TrySelect(string pocketId)
        {
            if (!CanSelect()) return false;
            QuickPocketOption option = runtime.QuickPockets.GetOption(runtime.BoardState, runtime.RunState, runtime.PlayerId, pocketId);
            if (option == null) return false;
            Selection = option;
            return true;
        }

        /// <summary>Clears only the uncommitted selection for free while idle; it never discards a carried item.</summary>
        public bool ClearSelection()
        {
            if (!CanSelect()) return false;
            Selection = null;
            return true;
        }

        /// <summary>Submits the chosen snapshot with one primary through the sole coordinator; blocked/canceled attempts preserve it.</summary>
        public async Task<CommandSubmission> SubmitAsync(PlayerCommand primary, CancellationToken token = default)
        {
            if (disposed) return new CommandSubmission(CommandSubmissionStatus.Blocked);
            PlayerCommand command = Selection == null ? primary : Selection.CreateCommand(primary);
            CommandSubmission submission = await coordinator.SubmitAsync(command, token);
            Complete(submission);
            return submission;
        }

        /// <summary>Forgets a resolved or rejected snapshot, including a pickup modal's frozen selection; no automatic retargeting occurs.</summary>
        public void Complete(CommandSubmission submission)
        {
            if (submission?.Result != null) Selection = null;
        }

        private bool CanSelect() => !disposed && runtime != null && !runtime.IsDisposed && !runtime.Controller.IsTerminal &&
            runtime.RunState.Status == Core.State.RunStatus.Active && coordinator.CanSubmit && (inputAllowed?.Invoke() ?? true);

        private void OnGUI()
        {
            if (!CanSelect()) return;
            float width = Mathf.Min(220f, Screen.width - 16f);
            float height = 112f;
            GUILayout.BeginArea(new Rect(Mathf.Max(8f, Screen.width - width - 8f), Mathf.Max(8f, Screen.height - height - 96f), width, height), GUI.skin.box);
            DrawPocket(InventoryLayoutEntry.LeftPocket, "Left pocket");
            DrawPocket(InventoryLayoutEntry.RightPocket, "Right pocket");
            GUILayout.EndArea();
        }

        private void DrawPocket(string pocketId, string label)
        {
            QuickPocketOption option = runtime.QuickPockets.GetOption(runtime.BoardState, runtime.RunState, runtime.PlayerId, pocketId);
            GUI.enabled = option != null;
            bool selected = Selection?.Item.PocketId == pocketId;
            string text = label + ": " + (option?.Item.DefinitionId ?? "Empty");
            string hint = option == null ? "Unavailable" : option.Effect.Resource + " +" + option.Applied + "; wasted " + option.Wasted;
            bool next = GUILayout.Toggle(selected, new GUIContent(text, hint), new GUIStyle(GUI.skin.button) { wordWrap = true }, GUILayout.Height(46f));
            if (next != selected)
            {
                if (next) TrySelect(pocketId);
                else ClearSelection();
            }
            GUI.enabled = true;
        }

        private void OnDisable() => Selection = null;
        private void OnDestroy() => Dispose();
        /// <summary>Forgets this lifetime's selection without touching borrowed runtime, resources, items or other input owners.</summary>
        public void Dispose() { disposed = true; Selection = null; }
    }
}