using System;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Mirrors the animated player sprite on a cosmetic pivot; never rotates the actor, grid or colliders.</summary>
    public sealed class PlayerVisualFacing : MonoBehaviour
    {
        private SpriteRenderer source;
        private SpriteRenderer visual;
        private bool originalRenderingOff;
        private int? sampledFrame;

        /// <summary>Clockwise cardinal quarter-turns from the authored North pose; movement preserves this transient value.</summary>
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

        /// <summary>Admits one isolated Q/E down edge per idle walking frame. Movement/Wait/F/N/Escape wins; opposite turns cancel.</summary>
        /// <param name="keyDown">Current-frame key-down edges, never held state.</param>
        /// <param name="frame">Consumed even while blocked, preventing deferred or duplicate rotation.</param>
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
            QuarterTurns = (QuarterTurns + (right ? 1 : 3)) % 4;
            visual.transform.localRotation = Quaternion.Euler(0, 0, -90 * QuarterTurns);
            return true;
        }

        private void LateUpdate() => Synchronize();

        private void Synchronize()
        {
            if (source == null || visual == null) return;
            visual.sprite = source.sprite;
            visual.color = source.color;
            visual.flipX = source.flipX;
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