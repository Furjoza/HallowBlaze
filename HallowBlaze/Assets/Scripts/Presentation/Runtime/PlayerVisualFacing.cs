using System;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Faces the animated sprite left or right by horizontal mirroring; never rotates the actor, grid or colliders.</summary>
    public sealed class PlayerVisualFacing : MonoBehaviour
    {
        private SpriteRenderer source;
        private SpriteRenderer visual;
        private bool originalRenderingOff;
        private int? sampledFrame;

        /// <summary>Gets the transient facing code: 0 authored pose, 1 right or 3 left; it is not a sprite rotation.</summary>
        public int QuarterTurns { get; private set; }

        /// <summary>Gets the visual-only pivot; no gameplay component is parented to it.</summary>
        public Transform Pivot => visual != null ? visual.transform : null;

        private void Awake()
        {
            source = GetComponent<SpriteRenderer>();
            if (source == null) return;
            originalRenderingOff = source.forceRenderingOff;
            var owner = new GameObject("Player visual facing") { hideFlags = HideFlags.DontSave };
            owner.transform.SetParent(transform, false);
            visual = owner.AddComponent<SpriteRenderer>();
            visual.sharedMaterial = source.sharedMaterial;
            Synchronize();
        }

        private void OnEnable()
        {
            if (source == null || visual == null) return;
            source.forceRenderingOff = true;
            visual.gameObject.SetActive(true);
        }

        /// <summary>Sets left with Q or right with E on an isolated idle walking edge; other gameplay keys win.</summary>
        /// <param name="keyDown">Current-frame key-down edges, never held state.</param>
        /// <param name="frame">Consumed even while blocked, preventing deferred or duplicate facing changes.</param>
        /// <param name="inputAllowed">The owning current board's complete gameplay-input permission.</param>
        /// <returns>Whether the cosmetic pivot changed; no command, RNG or domain object is accessed.</returns>
        public bool SampleInput(Func<KeyCode, bool> keyDown, int frame, bool inputAllowed)
        {
            if (keyDown == null) throw new ArgumentNullException(nameof(keyDown));
            if (sampledFrame == frame) return false;
            sampledFrame = frame;
            if (!inputAllowed || !isActiveAndEnabled || visual == null) return false;
            if (keyDown(PcInputBindings.InteractionToggle) || keyDown(PcInputBindings.Space) ||
                keyDown(PcInputBindings.Notes) || keyDown(PcInputBindings.Cancel)) return false;
            foreach (KeyCode key in PcInputBindings.Movement.Keys) if (keyDown(key)) return false;
            bool left = keyDown(PcInputBindings.TurnLeft);
            bool right = keyDown(PcInputBindings.TurnRight);
            if (left == right) return false;
            int facing = right ? 1 : 3;
            if (QuarterTurns == facing) return false;
            QuarterTurns = facing;
            Synchronize();
            return true;
        }

        private void LateUpdate() => Synchronize();

        private void Synchronize()
        {
            if (source == null || visual == null) return;
            visual.sprite = source.sprite;
            visual.color = source.color;
            visual.flipX = QuarterTurns == 0 ? source.flipX : QuarterTurns == 3;
            visual.flipY = source.flipY;
            visual.sortingLayerID = source.sortingLayerID;
            visual.sortingOrder = source.sortingOrder;
            visual.maskInteraction = source.maskInteraction;
            visual.enabled = source.enabled && !originalRenderingOff;
        }

        private void OnDisable()
        {
            if (source != null) source.forceRenderingOff = originalRenderingOff;
            if (visual != null) visual.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (visual != null) Destroy(visual.gameObject);
        }
    }
}