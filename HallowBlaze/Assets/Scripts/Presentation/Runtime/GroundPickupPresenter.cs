using System;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Board-owned pre-command consumable modal; cancel is free and the authoritative move/reward has not happened yet.</summary>
    [DefaultExecutionOrder(-60)]
    public sealed class GroundPickupPresenter : MonoBehaviour, IDisposable
    {
        private BoardRuntime runtime;
        private CommandPresentationCoordinator coordinator;
        private Func<bool> inputAllowed;
        private Action<CommandSubmission> completed;
        private QuickPocketOption quick;
        private InventoryState availableInventory;
        private bool disposed, submitting, releasePending;
        private InventoryLayoutEntry placement;
        private Vector2 scroll;

        /// <summary>Gets whether an uncommitted pickup intent owns input.</summary>
        public bool IsOpen => Option != null;
        /// <summary>Gets the detached observed option; it never owns a ground entity or applies an effect.</summary>
        public GroundPickupOption Option { get; private set; }
        /// <summary>Gets the explicitly selected valid Store anchor, or null before selection.</summary>
        public InventoryLayoutEntry Placement => placement;

        /// <summary>Borrows one runtime and its sole coordinator; the optional callback forgets a quick snapshot only after a domain result.</summary>
        public void Bind(BoardRuntime runtime, CommandPresentationCoordinator coordinator, Func<bool> inputAllowed = null,
            Action<CommandSubmission> completed = null)
        {
            if (this.runtime != null || disposed) throw new InvalidOperationException("A pickup modal belongs to one board.");
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            this.inputAllowed = inputAllowed;
            this.completed = completed;
        }

        /// <summary>Opens only for a decision-producing intent; an optional frozen quick selection projects capacity without changing inventory.</summary>
        public bool TryOpen(PlayerCommand primary, QuickPocketOption quick = null)
        {
            if (!CanUse() || IsOpen || releasePending || submitting || !coordinator.CanSubmit) return false;
            availableInventory = null;
            if (quick != null && !quick.TryProjectInventory(runtime.RunState, out availableInventory)) return false;
            Option = runtime.GroundPickups.GetOption(runtime.BoardState, runtime.RunState, runtime.PlayerId, primary, availableInventory);
            if (Option == null) return false;
            this.quick = quick;
            placement = null;
            scroll = Vector2.zero;
            coordinator.Gate.SetBlocked(PresentationInputBlock.Pickup, true);
            return true;
        }

        /// <summary>Selects one currently legal anchor without rearranging items, applying effects or spending resources.</summary>
        public bool SelectPlacement(string containerId, int column = 0, int row = 0)
        {
            if (!CanChoose() || !Option.CanStore) return false;
            var proposed = new InventoryLayoutEntry(Option.Item.InstanceId, containerId, column, row);
            if (!Fits(proposed)) return false;
            placement = proposed;
            return true;
        }

        /// <summary>Submits one complete composite exactly once; stale rejection closes rather than silently retargeting the snapshot.</summary>
        public async Task<CommandSubmission> ConfirmAsync(GroundPickupChoice choice, CancellationToken token = default)
        {
            if (!CanChoose() || choice == GroundPickupChoice.Store && placement == null ||
                choice == GroundPickupChoice.UseNow && !Option.UseAvailable || choice == GroundPickupChoice.Store && !Option.CanStore)
                return new CommandSubmission(CommandSubmissionStatus.Blocked);
            PlayerCommand command = Option.CreateCommand(choice, choice == GroundPickupChoice.Store ? placement : null);
            if (quick != null) command = quick.CreateCommand(command);
            submitting = true;
            coordinator.Gate.SetBlocked(PresentationInputBlock.Pickup, false);
            try
            {
                CommandSubmission submission = await coordinator.SubmitAsync(command, token);
                completed?.Invoke(submission);
                if (submission.Result != null) Close(false);
                return submission;
            }
            finally
            {
                submitting = false;
                if (!disposed && IsOpen) coordinator.Gate.SetBlocked(PresentationInputBlock.Pickup, true);
            }
        }

        /// <summary>Cancels before domain submission without moving, revealing, using, storing or spending a turn.</summary>
        public bool Cancel()
        {
            if (!IsOpen || submitting) return false;
            Close(Application.isPlaying);
            return true;
        }

        private bool CanUse() => !disposed && runtime != null && !runtime.IsDisposed && !runtime.Controller.IsTerminal &&
            !runtime.Controller.IsResolving && runtime.RunState.Status == RunStatus.Active && (inputAllowed?.Invoke() ?? true);
        private bool CanChoose() => CanUse() && IsOpen && !submitting && coordinator.Gate.State == PresentationGateState.Idle &&
            coordinator.Gate.Blocks == PresentationInputBlock.Pickup;

        private bool Fits(InventoryLayoutEntry entry)
        {
            InventoryContainer container;
            switch (entry.ContainerId)
            {
                case InventoryLayoutEntry.MainGrid: container = InventoryContainer.MainGrid; break;
                case InventoryLayoutEntry.LeftPocket: container = InventoryContainer.LeftPocket; break;
                case InventoryLayoutEntry.RightPocket: container = InventoryContainer.RightPocket; break;
                default: return false;
            }
            return (availableInventory ?? runtime.RunState.Inventory).TryPlace(new InventoryPlacement(new ItemInstanceState(Option.Item.InstanceId,
                Option.Item.DefinitionId), container, entry.Column, entry.Row), out _, out _);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) HandleKey(KeyCode.Escape);
        }

        /// <summary>Consumes one cancel edge only while this modal owns input, retaining its block through the closing frame.</summary>
        public bool HandleKey(KeyCode key) => key == KeyCode.Escape && CanChoose() && Cancel();
        private void LateUpdate()
        {
            if (!releasePending) return;
            releasePending = false;
            coordinator.Gate.SetBlocked(PresentationInputBlock.Pickup, false);
        }
        private void OnGUI()
        {
            if (!IsOpen || !CanUse()) return;
            float width = Mathf.Min(340f, Screen.width - 16f);
            float height = Mathf.Min(520f, Screen.height - 32f);
            GUILayout.BeginArea(new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUILayout.Label(Option.Item.DefinitionId, wrap);
            if (runtime.RunState.InventoryContent.Items.TryGetDefinition(Option.Item.DefinitionId, out ItemDefinition definition) &&
                runtime.RunState.InventoryContent.Effects.TryGetDefinition(definition.EffectId, out ConsumableEffectDefinition effect))
                GUILayout.Label(definition.Width + " x " + definition.Height + "; " + effect.Resource + " +" + effect.Amount, wrap);
            GUI.enabled = CanChoose() && Option.UseAvailable;
            if (GUILayout.Button("Use now", GUILayout.Height(36f))) { ConfirmFromGui(GroundPickupChoice.UseNow); GUIUtility.ExitGUI(); }
            GUI.enabled = true;
            if (!Option.UseAvailable) GUILayout.Label(Option.UseUnavailableReason, wrap);
            GUILayout.Space(8f);
            GUILayout.Label("Backpack");
            for (int row = 0; row < InventoryState.MainGridHeight; row++)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < InventoryState.MainGridWidth; column++)
                    DrawAnchor(InventoryLayoutEntry.MainGrid, column, row, column + "." + row);
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            DrawAnchor(InventoryLayoutEntry.LeftPocket, 0, 0, "Left pocket");
            DrawAnchor(InventoryLayoutEntry.RightPocket, 0, 0, "Right pocket");
            GUILayout.EndHorizontal();
            GUI.enabled = CanChoose() && Option.CanStore && placement != null;
            if (GUILayout.Button("Store", GUILayout.Height(36f))) { ConfirmFromGui(GroundPickupChoice.Store); GUIUtility.ExitGUI(); }
            GUI.enabled = true;
            if (!Option.CanStore) GUILayout.Label(Option.StoreUnavailableReason, wrap);
            GUILayout.Space(8f);
            GUI.enabled = CanChoose();
            if (GUILayout.Button("Leave", GUILayout.Height(36f))) { ConfirmFromGui(GroundPickupChoice.Leave); GUIUtility.ExitGUI(); }
            if (GUILayout.Button("Cancel", GUILayout.Height(32f))) Cancel();
            GUI.enabled = true;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        private void DrawAnchor(string containerId, int column, int row, string label)
        {
            var entry = new InventoryLayoutEntry(Option.Item.InstanceId, containerId, column, row);
            GUI.enabled = CanChoose() && Option.CanStore && Fits(entry);
            Color previous = GUI.backgroundColor;
            bool selected = placement != null && placement.ContainerId == containerId && placement.Column == column && placement.Row == row;
            if (selected) GUI.backgroundColor = Interaction.InteractionPresenter.SelectionColor;
            InventoryState inventory = availableInventory ?? runtime.RunState.Inventory;
            ItemInstanceState occupied = containerId == InventoryLayoutEntry.MainGrid ? inventory.GetMainGridItem(column, row) :
                inventory.GetPocketItem(containerId == InventoryLayoutEntry.LeftPocket ? InventoryContainer.LeftPocket : InventoryContainer.RightPocket);
            if (GUILayout.Button(new GUIContent(occupied == null ? label : "Occupied", occupied?.DefinitionId ?? label), GUILayout.Height(40f)))
                SelectPlacement(containerId, column, row);
            GUI.backgroundColor = previous;
            GUI.enabled = true;
        }
        private async void ConfirmFromGui(GroundPickupChoice choice)
        {
            try { await ConfirmAsync(choice); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
        private void Close(bool holdFrame)
        {
            Option = null;
            quick = null;
            availableInventory = null;
            placement = null;
            releasePending = holdFrame;
            coordinator?.Gate.SetBlocked(PresentationInputBlock.Pickup, holdFrame);
        }
        private void OnDisable() { if (!submitting) Close(false); }
        private void OnDestroy() => Dispose();
        /// <summary>Closes this board's modal and only its input ownership; no gameplay mutation is performed.</summary>
        public void Dispose() { if (disposed) return; disposed = true; Close(false); }
    }
}