using System;
using HallowBlaze.Core.Board.State;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Replays immutable plate/gate snapshots using authored art and visible link/rule labels; never owns gameplay state.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class PlateGateView : MonoBehaviour
    {
        [SerializeField] private Sprite releasedOrClosed;
        [SerializeField] private Sprite pressedOrOpen;
        private TextMesh label;

        /// <summary>Gets the last replayed pressure/open state, which may be intermediate during ordered turn presentation.</summary>
        public bool DisplayedActive { get; private set; }
        /// <summary>Gets the visible stable link, rule and state label for diagnostics.</summary>
        public string LinkLabel => label == null ? string.Empty : label.text;

        /// <summary>Assigns two visual-only states during explicit prefab authoring; null sprites are rejected.</summary>
        public void ConfigureSprites(Sprite inactiveSprite, Sprite activeSprite)
        {
            releasedOrClosed = inactiveSprite != null ? inactiveSprite : throw new ArgumentNullException(nameof(inactiveSprite));
            pressedOrOpen = activeSprite != null ? activeSprite : throw new ArgumentNullException(nameof(activeSprite));
            GetComponent<SpriteRenderer>().sprite = releasedOrClosed;
        }

        /// <summary>Shows the recorded pressure and its declared category; no board query or recomputation occurs.</summary>
        public void Present(PressurePlateState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            string category = state.Definition.Occupants == PlateOccupants.Actors ? "ACTOR" :
                state.Definition.Occupants == PlateOccupants.Boulders ? "BOULDER" : "ACTOR / BOULDER";
            Show(state.IsPressed, state.Definition.LinkId + "\n" + category + (state.IsPressed ? " DOWN" : " UP"));
        }

        /// <summary>Shows the recorded gate state and All/Any rule even if a later mutation has already changed the model.</summary>
        public void Present(GateState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            Show(state.IsOpen, state.Definition.LinkId + "\n" + state.Definition.Rule.ToString().ToUpperInvariant() +
                (state.IsOpen ? " OPEN" : " CLOSED"));
        }

        private void Show(bool active, string text)
        {
            if (releasedOrClosed == null || pressedOrOpen == null) throw new InvalidOperationException("Plate/gate art references are missing.");
            DisplayedActive = active;
            GetComponent<SpriteRenderer>().sprite = active ? pressedOrOpen : releasedOrClosed;
            if (label == null)
            {
                var owner = new GameObject("Link and rule");
                owner.transform.SetParent(transform, false);
                owner.transform.localPosition = new Vector3(0, -0.3f, -0.1f);
                label = owner.AddComponent<TextMesh>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                owner.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.fontSize = 32;
                label.characterSize = 0.065f;
                label.color = Color.white;
                owner.GetComponent<MeshRenderer>().sortingLayerName = "Units";
                owner.GetComponent<MeshRenderer>().sortingOrder = 30;
            }
            label.text = text;
            label.transform.localScale = Vector3.one;
            Vector3 size = label.GetComponent<MeshRenderer>().bounds.size;
            float scale = Mathf.Min(1f, 0.92f / Mathf.Max(0.001f, size.x), 0.3f / Mathf.Max(0.001f, size.y));
            label.transform.localScale = Vector3.one * scale;
        }
    }
}