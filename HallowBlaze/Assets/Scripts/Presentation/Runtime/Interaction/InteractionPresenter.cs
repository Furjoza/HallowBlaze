using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Core.State;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime.Interaction
{
    /// <summary>
    /// Board-owned PC target picker. E opens a free draft, arrows/Tab select, Enter confirms,
    /// and Escape cancels. Numbered persistent geometry and a scrollable action list do not depend on animation or color.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class InteractionPresenter : MonoBehaviour, IDisposable
    {
        private BoardRuntime runtime;
        private CommandPresentationCoordinator coordinator;
        private Func<bool> inputAllowed;
        private readonly List<GameObject> markers = new List<GameObject>();
        private Material material;
        private Vector2 scroll;
        private bool releasePending;
        private IReadOnlyDictionary<string, ToolDefinition> toolCatalog;
        private ToolPickupOption replacement;
        private PickupToolCommand replacementChoice;
        private string[] replacementLabels = Array.Empty<string>();

        /// <summary>Gets whether a free selection draft currently owns input.</summary>
        public bool IsOpen { get; private set; }
        /// <summary>Gets whether this board-owned display has permanently closed.</summary>
        public bool IsDisposed { get; private set; }
        /// <summary>Gets the retained query snapshot; preview and cancel never remove its ground entities.</summary>
        public IReadOnlyList<InteractionOption> Options { get; private set; } = Array.Empty<InteractionOption>();
        /// <summary>Gets detached tool consequences following ordinary interactions in the same explicit picker.</summary>
        public IReadOnlyList<ToolUseOption> ToolOptions { get; private set; } = Array.Empty<ToolUseOption>();
        /// <summary>Gets detached ground-tool choices following ordinary interactions and tool use.</summary>
        public IReadOnlyList<ToolPickupOption> PickupOptions { get; private set; } = Array.Empty<ToolPickupOption>();
        /// <summary>Gets whether an explicit replacement comparison owns this draft without submitting a turn.</summary>
        public bool IsReplacing => replacement != null;
        /// <summary>Gets detached definition/charge comparisons for both generic slots while replacing.</summary>
        public IReadOnlyList<string> ReplacementLabels => Array.AsReadOnly(replacementLabels);
        /// <summary>Gets the total number of numbered interactions, tool uses and pickups, including disabled choices.</summary>
        public int OptionCount => Options.Count + ToolOptions.Count + PickupOptions.Count;
        /// <summary>Gets the explicit selected index, or -1 before the player chooses.</summary>
        public int SelectedIndex { get; private set; } = -1;

        /// <summary>Binds this display once to the current board and its existing command coordinator.</summary>
        /// <param name="runtime">Borrowed board whose query is also used by its resolver.</param>
        /// <param name="coordinator">The one production command/presentation path.</param>
        /// <param name="inputAllowed">Optional lifecycle permission excluding this picker's own block.</param>
        public void Bind(BoardRuntime runtime, CommandPresentationCoordinator coordinator, Func<bool> inputAllowed = null)
        {
            if (IsDisposed || this.runtime != null)
                throw new InvalidOperationException("An interaction picker belongs to exactly one board.");
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            this.inputAllowed = inputAllowed ?? (() => true);
            toolCatalog = ToolContentCatalog.Load();
        }

        /// <summary>Opens all adjacent options without choosing a preferred action or submitting a command.</summary>
        /// <returns>False when blocked or empty; an empty query is free and never traps input.</returns>
        public bool Open()
        {
            if (!CanUse() || IsOpen || releasePending || !coordinator.CanSubmit)
                return false;
            Options = runtime.Interactions.GetOptions(runtime.BoardState, runtime.RunState, runtime.PlayerId);
            ToolOptions = runtime.Tools.GetOptions(runtime.BoardState, runtime.RunState, runtime.PlayerId);
            PickupOptions = runtime.Pickups.GetOptions(runtime.BoardState, runtime.RunState, runtime.PlayerId);
            if (OptionCount == 0)
                return false;
            IsOpen = true;
            SelectedIndex = -1;
            scroll = Vector2.zero;
            coordinator.Gate.SetBlocked(PresentationInputBlock.Interaction, true);
            try { DrawMarkers(); }
            catch { Close(false); throw; }
            return true;
        }

        /// <summary>Selects one available snapshot without applying an effect or spending resources.</summary>
        /// <returns>Whether this explicit selection was admitted.</returns>
        public bool Select(int index)
        {
            if (!CanChoose() || IsReplacing || index < 0 || index >= OptionCount || !IsAvailable(index))
                return false;
            SelectedIndex = index;
            DrawMarkers();
            return true;
        }

        /// <summary>Submits exactly the selected complete command once; stale targets are rejected by the resolver.</summary>
        /// <returns>The existing pipeline result, or a free NoCommand/Blocked result without resolution.</returns>
        public Task<CommandSubmission> ConfirmAsync()
        {
            if (!CanChoose() || SelectedIndex < 0)
                return Task.FromResult(new CommandSubmission(CommandSubmissionStatus.NoCommand));
            PlayerCommand command;
            int pickupIndex = SelectedIndex - Options.Count - ToolOptions.Count;
            if (pickupIndex >= 0)
            {
                ToolPickupOption pickup = PickupOptions[pickupIndex];
                if (pickup.RequiresReplacement)
                {
                    if (replacement == null)
                    {
                        replacement = pickup;
                        replacementChoice = null;
                        replacementLabels = new string[RunState.ToolSlotCount];
                        foreach (PickupToolCommand choice in pickup.Choices)
                            replacementLabels[choice.SlotIndex] = "Tool " + (choice.SlotIndex + 1) + ": " +
                                DescribeTool(choice.ExpectedSlotDefinitionId, choice.ExpectedSlotCharges) + " -> " +
                                DescribeTool(choice.Tool.DefinitionId, choice.Tool.Charges) + "\nOld tool remains here";
                        return Task.FromResult(new CommandSubmission(CommandSubmissionStatus.NoCommand));
                    }
                    if (replacementChoice == null) return Task.FromResult(new CommandSubmission(CommandSubmissionStatus.NoCommand));
                    command = replacementChoice;
                }
                else command = pickup.Choices[0];
            }
            else command = SelectedIndex < Options.Count ? (PlayerCommand)Options[SelectedIndex].CreateCommand() :
                ToolOptions[SelectedIndex - Options.Count].CreateCommand();
            Close(false);
            Task<CommandSubmission> submission = coordinator.SubmitAsync(command);
            HoldRemainderOfFrame();
            return submission;
        }

        /// <summary>Selects one exact occupied-slot snapshot; confirming is still required and no turn is resolved here.</summary>
        public bool SelectReplacement(int slotIndex)
        {
            if (!CanChoose() || replacement == null) return false;
            foreach (PickupToolCommand choice in replacement.Choices)
                if (choice.SlotIndex == slotIndex) { replacementChoice = choice; return true; }
            return false;
        }

        /// <summary>Leaves the exact same ground instance and equipment unchanged, closing this free draft.</summary>
        public void Leave() => Cancel();

        /// <summary>Discards the preview for free, preserving the exact ground entities and all other input owners.</summary>
        public void Cancel()
        {
            if (!IsOpen) return;
            Close(false);
            HoldRemainderOfFrame();
        }

        private bool CanUse() => !IsDisposed && isActiveAndEnabled && runtime != null && !runtime.IsDisposed &&
            !runtime.Controller.IsTerminal && inputAllowed();

        private bool CanChoose() => IsOpen && CanUse() && coordinator.Gate.State == PresentationGateState.Idle &&
            (coordinator.Gate.Blocks & ~PresentationInputBlock.Interaction) == PresentationInputBlock.None;

        private async void Update()
        {
            if (IsDisposed || runtime == null) return;
            if (IsOpen && !CanChoose()) { Cancel(); return; }
            try
            {
                if (!IsOpen)
                {
                    if (Input.GetKeyDown(KeyCode.E)) Open();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
                if (IsReplacing)
                {
                    if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.UpArrow))
                        SelectReplacement(replacementChoice == null ? 0 : 1 - replacementChoice.SlotIndex);
                    else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) await ConfirmAsync();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.DownArrow)) Cycle(1);
                else if (Input.GetKeyDown(KeyCode.UpArrow)) Cycle(-1);
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) await ConfirmAsync();
            }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        private void Cycle(int step)
        {
            int next = SelectedIndex < 0 ? (step > 0 ? -1 : 0) : SelectedIndex;
            for (int count = 0; count < OptionCount; count++)
            {
                next = (next + step + OptionCount) % OptionCount;
                if (Select(next)) return;
            }
        }

        private void LateUpdate()
        {
            if (!releasePending) return;
            releasePending = false;
            coordinator.Gate.SetBlocked(PresentationInputBlock.Interaction, false);
        }

        private void HoldRemainderOfFrame()
        {
            if (IsDisposed || runtime.IsDisposed || runtime.Controller.IsTerminal) return;
            releasePending = true;
            coordinator.Gate.SetBlocked(PresentationInputBlock.Interaction, true);
        }

        private void OnGUI()
        {
            if (!CanChoose()) return;
            float width = Mathf.Min(300f, Screen.width - 16f);
            float top = ToolHudPresenter.ReservedHeight + 8f;
            GUILayout.BeginArea(new Rect(Screen.width - width - 8f, top, width, Mathf.Max(100f, Screen.height - top - 16f)), GUI.skin.box);
            if (IsReplacing)
            {
                GUILayout.Label("Replace tool");
                GUILayout.Label(DescribeTool(replacement.Tool.DefinitionId, replacement.Tool.Charges));
                scroll = GUILayout.BeginScrollView(scroll);
                var comparisonStyle = new GUIStyle(GUI.skin.button) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
                foreach (PickupToolCommand choice in replacement.Choices)
                    if (GUILayout.Button((replacementChoice == choice ? "> " : "") + replacementLabels[choice.SlotIndex],
                        comparisonStyle, GUILayout.MinHeight(90f))) SelectReplacement(choice.SlotIndex);
                GUILayout.EndScrollView();
                GUILayout.Label("1 turn; Food -" + runtime.Tools.ActionCosts.FoodCost + "; Noise " +
                    ActionNoiseProfile.Prototype.InteractionStrength + " / " + ActionNoiseProfile.Prototype.LifetimeTurns + " turns");
                GUI.enabled = replacementChoice != null;
                if (GUILayout.Button("Replace", GUILayout.Height(32f))) ConfirmFromGui();
                GUI.enabled = true;
                if (GUILayout.Button("Leave", GUILayout.Height(32f))) Leave();
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label("Interaction");
            scroll = GUILayout.BeginScrollView(scroll);
            var optionStyle = new GUIStyle(GUI.skin.button) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
            for (int index = 0; index < OptionCount; index++)
            {
                GUI.enabled = IsAvailable(index);
                string label = (index == SelectedIndex ? "> " : "") + (index + 1) + ". " + GetOptionLabel(index);
                if (GUILayout.Button(label, optionStyle, GUILayout.MinHeight(index < Options.Count ? 32f : 72f))) Select(index);
                GUI.enabled = true;
                if (!IsAvailable(index)) GUILayout.Label(index < Options.Count ? Options[index].Availability.ToString() :
                    index < Options.Count + ToolOptions.Count ? ToolOptions[index - Options.Count].RejectionCode.ToString() :
                    CommandRejectionCode.InvalidState.ToString());
            }
            GUILayout.EndScrollView();
            GUI.enabled = SelectedIndex >= 0;
            if (GUILayout.Button("Confirm", GUILayout.Height(32f))) ConfirmFromGui();
            GUI.enabled = true;
            if (GUILayout.Button("Cancel", GUILayout.Height(32f))) Cancel();
            GUILayout.EndArea();
        }

        private async void ConfirmFromGui()
        {
            try { await ConfirmAsync(); }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        private static string ActionLabel(string actionId) => actionId == "collect" ? "Collect" :
            actionId == "push" ? "Push" : actionId == "interact" ? "Interact" : actionId;

        /// <summary>Reads the exact displayed consequence, including slot, charge, current food cost and noise before confirmation.</summary>
        public string GetOptionLabel(int index)
        {
            if (index < 0 || index >= OptionCount) throw new ArgumentOutOfRangeException(nameof(index));
            GridPosition position = TargetPosition(index);
            string target = " (" + position.X + ", " + position.Y + ")";
            if (index < Options.Count)
            {
                InteractionOption interaction = Options[index];
                if (interaction.EffectLabel == null) return ActionLabel(interaction.ActionId) + target;
                return interaction.EffectLabel + target + "\n1 turn; Food -" + runtime.Tools.ActionCosts.FoodCost +
                    "; Charge 0; Noise " + interaction.NoiseStrength + " / " + interaction.NoiseLifetimeTurns + " turns";
            }
            if (index >= Options.Count + ToolOptions.Count)
            {
                ToolPickupOption pickup = PickupOptions[index - Options.Count - ToolOptions.Count];
                return "Pick up " + DescribeTool(pickup.Tool.DefinitionId, pickup.Tool.Charges) + target +
                    (pickup.RequiresReplacement ? "\nReplace / Leave" : "\nTool " + (pickup.Choices.Count == 0 ? "?" : (pickup.Choices[0].SlotIndex + 1).ToString())) +
                    "\n1 turn; Food -" + runtime.Tools.ActionCosts.FoodCost + "; Charge 0; Noise " +
                    ActionNoiseProfile.Prototype.InteractionStrength + " / " + ActionNoiseProfile.Prototype.LifetimeTurns + " turns";
            }
            ToolUseOption option = ToolOptions[index - Options.Count];
            return "Tool " + (option.SlotIndex + 1) + ": " + option.EffectLabel + target +
                "\n1 turn; Food -" + option.FoodCost + "; Charge -" + option.ChargeCost +
                " (" + option.ExpectedCharges + " left); Noise " + option.NoiseStrength +
                (option.NoiseStrength == 0 ? "" : " / " + option.NoiseLifetimeTurns + " turns");
        }

        private string DescribeTool(string definitionId, int charges) =>
            (toolCatalog.TryGetValue(definitionId, out ToolDefinition definition) ? definition.DisplayName : definitionId) + " (" + charges + " charges)";

        private bool IsAvailable(int index) => index < Options.Count ? Options[index].IsAvailable :
            index < Options.Count + ToolOptions.Count ? ToolOptions[index - Options.Count].IsAvailable :
            PickupOptions[index - Options.Count - ToolOptions.Count].Choices.Count > 0;

        private GridPosition TargetPosition(int index) => index < Options.Count ? Options[index].TargetPosition :
            index < Options.Count + ToolOptions.Count ? ToolOptions[index - Options.Count].TargetPosition :
            PickupOptions[index - Options.Count - ToolOptions.Count].Position;

        private void DrawMarkers()
        {
            ClearMarkers();
            if (material == null)
                material = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            var slots = new Dictionary<GridPosition, int>();
            for (int index = 0; index < OptionCount; index++)
            {
                if (!IsAvailable(index)) continue;
                GridPosition targetPosition = TargetPosition(index);
                var marker = new GameObject("Interaction target " + (index + 1)) { hideFlags = HideFlags.DontSave };
                markers.Add(marker);
                marker.transform.SetParent(transform, false);
                marker.transform.position = new Vector3(targetPosition.X, targetPosition.Y, -0.2f);
                LineRenderer line = marker.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = 4;
                line.SetPositions(index == SelectedIndex
                    ? new[] { new Vector3(0, 0.46f), new Vector3(0.46f, 0), new Vector3(0, -0.46f), new Vector3(-0.46f, 0) }
                    : new[] { new Vector3(-0.4f, -0.4f), new Vector3(0.4f, -0.4f), new Vector3(0.4f, 0.4f), new Vector3(-0.4f, 0.4f) });
                line.widthMultiplier = 0.035f;
                line.startColor = line.endColor = Color.white;
                line.sortingLayerName = EnemyIntentView.GlyphSortingLayer;
                line.sortingOrder = EnemyIntentView.GlyphSortingOrder + 2;
                slots.TryGetValue(targetPosition, out int slot);
                slots[targetPosition] = slot + 1;
                var label = new GameObject("Target number");
                label.transform.SetParent(marker.transform, false);
                label.transform.localPosition = new Vector3(slot % 2 == 0 ? -0.16f : 0.16f, 0.22f - slot / 2 * 0.22f, -0.01f);
                TextMesh text = label.AddComponent<TextMesh>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 48;
                text.characterSize = 0.07f;
                text.anchor = TextAnchor.MiddleCenter;
                text.text = (index == SelectedIndex ? ">" : "") + (index + 1);
                text.color = Color.white;
                MeshRenderer renderer = label.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = text.font.material;
                renderer.sortingLayerName = EnemyIntentView.GlyphSortingLayer;
                renderer.sortingOrder = EnemyIntentView.GlyphSortingOrder + 3;
            }
        }

        private void ClearMarkers()
        {
            foreach (GameObject marker in markers)
                if (marker != null) { marker.SetActive(false); Release(marker); }
            markers.Clear();
        }

        private void Close(bool immediateRelease)
        {
            IsOpen = false;
            SelectedIndex = -1;
            Options = Array.Empty<InteractionOption>();
            ToolOptions = Array.Empty<ToolUseOption>();
            PickupOptions = Array.Empty<ToolPickupOption>();
            replacement = null;
            replacementChoice = null;
            replacementLabels = Array.Empty<string>();
            ClearMarkers();
            if (coordinator != null) coordinator.Gate.SetBlocked(PresentationInputBlock.Interaction, false);
            if (immediateRelease) releasePending = false;
        }

        private void OnDisable() => Close(true);
        private void OnDestroy() => Dispose();

        /// <summary>Releases only owned geometry and input ownership; never disposes the borrowed runtime.</summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Close(true);
            if (material != null) Release(material);
            material = null;
        }

        private static void Release(UnityEngine.Object instance)
        {
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
        }
    }
}