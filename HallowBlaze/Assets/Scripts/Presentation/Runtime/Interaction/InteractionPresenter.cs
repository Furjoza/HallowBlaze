using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Turns.Resolution;
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

        /// <summary>Gets whether a free selection draft currently owns input.</summary>
        public bool IsOpen { get; private set; }
        /// <summary>Gets whether this board-owned display has permanently closed.</summary>
        public bool IsDisposed { get; private set; }
        /// <summary>Gets the retained query snapshot; preview and cancel never remove its ground entities.</summary>
        public IReadOnlyList<InteractionOption> Options { get; private set; } = Array.Empty<InteractionOption>();
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
        }

        /// <summary>Opens all adjacent options without choosing a preferred action or submitting a command.</summary>
        /// <returns>False when blocked or empty; an empty query is free and never traps input.</returns>
        public bool Open()
        {
            if (!CanUse() || IsOpen || releasePending || !coordinator.CanSubmit)
                return false;
            Options = runtime.Interactions.GetOptions(runtime.BoardState, runtime.RunState, runtime.PlayerId);
            if (Options.Count == 0)
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
            if (!CanChoose() || index < 0 || index >= Options.Count || !Options[index].IsAvailable)
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
            var command = Options[SelectedIndex].CreateCommand();
            Close(false);
            Task<CommandSubmission> submission = coordinator.SubmitAsync(command);
            HoldRemainderOfFrame();
            return submission;
        }

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
                if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.DownArrow)) Cycle(1);
                else if (Input.GetKeyDown(KeyCode.UpArrow)) Cycle(-1);
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) await ConfirmAsync();
            }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        private void Cycle(int step)
        {
            int next = SelectedIndex < 0 ? (step > 0 ? -1 : 0) : SelectedIndex;
            for (int count = 0; count < Options.Count; count++)
            {
                next = (next + step + Options.Count) % Options.Count;
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
            GUILayout.BeginArea(new Rect(Screen.width - width - 8f, 60f, width, Mathf.Max(100f, Screen.height - 100f)), GUI.skin.box);
            GUILayout.Label("Interaction");
            scroll = GUILayout.BeginScrollView(scroll);
            for (int index = 0; index < Options.Count; index++)
            {
                InteractionOption option = Options[index];
                GUI.enabled = option.IsAvailable;
                string label = (index == SelectedIndex ? "> " : "") + (index + 1) + ". " +
                    ActionLabel(option.ActionId) + " (" + option.TargetPosition.X + ", " + option.TargetPosition.Y + ")";
                if (GUILayout.Button(label, GUILayout.MinHeight(32f))) Select(index);
                GUI.enabled = true;
                if (!option.IsAvailable) GUILayout.Label(option.Availability.ToString());
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

        private void DrawMarkers()
        {
            ClearMarkers();
            if (material == null)
                material = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            var slots = new Dictionary<GridPosition, int>();
            for (int index = 0; index < Options.Count; index++)
            {
                InteractionOption option = Options[index];
                if (!option.IsAvailable) continue;
                var marker = new GameObject("Interaction target " + (index + 1)) { hideFlags = HideFlags.DontSave };
                markers.Add(marker);
                marker.transform.SetParent(transform, false);
                marker.transform.position = new Vector3(option.TargetPosition.X, option.TargetPosition.Y, -0.2f);
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
                slots.TryGetValue(option.TargetPosition, out int slot);
                slots[option.TargetPosition] = slot + 1;
                var label = new GameObject("Target number");
                label.transform.SetParent(marker.transform, false);
                label.transform.localPosition = new Vector3(0f, 0.22f - slot * 0.22f, -0.01f);
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