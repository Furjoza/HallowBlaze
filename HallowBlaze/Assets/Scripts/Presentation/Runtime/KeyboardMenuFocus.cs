using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Owns keyboard focus inside one visible menu, including recovery, scrolling and return focus.</summary>
    [DefaultExecutionOrder(-80)]
    public sealed class KeyboardMenuFocus : MonoBehaviour
    {
        private static readonly List<KeyboardMenuFocus> owners = new List<KeyboardMenuFocus>();
        private static bool restoreNavigation;
        private static int handledFrame = -1;
        private readonly List<Selectable> controls = new List<Selectable>();
        private GameObject preferred;
        private GameObject retained;
        private GameObject returnFocus;
        private EventSystem events;
        private int openedFrame;
        private Action cancel;

        /// <summary>Installs transient navigation on an existing menu without changing its authored actions.</summary>
        public static KeyboardMenuFocus Attach(GameObject root, GameObject initial = null, Action cancel = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            KeyboardMenuFocus owner = root.GetComponent<KeyboardMenuFocus>();
            if (owner == null) owner = root.AddComponent<KeyboardMenuFocus>();
            owner.preferred = initial;
            owner.cancel = cancel;
            owner.enabled = true;
            if (owner.isActiveAndEnabled) owner.RecoverFocus();
            return owner;
        }

        private void OnEnable()
        {
            events = EventSystem.current;
            if (events == null)
                events = new GameObject("Menu EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)).GetComponent<EventSystem>();
            returnFocus = events.currentSelectedGameObject;
            if (owners.Count == 0) restoreNavigation = events.sendNavigationEvents;
            owners.Add(this);
            events.sendNavigationEvents = false;
            openedFrame = Time.frameCount;
            RecoverFocus();
        }

        /// <summary>Restores a reachable selection after a control disappears or a menu is rebuilt; never activates it.</summary>
        public void RecoverFocus()
        {
            if (events == null || owners.Count == 0 || owners[owners.Count - 1] != this) return;
            Collect();
            GameObject current = events.currentSelectedGameObject;
            if (Contains(current)) { retained = current; return; }
            GameObject next = Contains(retained) ? retained : Contains(preferred) ? preferred :
                controls.Count > 0 ? controls[0].gameObject : null;
            events.SetSelectedGameObject(next);
            retained = next;
        }

        /// <summary>Applies one keyboard edge to this menu only; navigation and scrolling do not activate controls.</summary>
        public void HandleKey(KeyCode key, bool shift = false)
        {
            if (!isActiveAndEnabled || events == null || owners.Count == 0 || owners[owners.Count - 1] != this) return;
            RecoverFocus();
            if (key == KeyCode.Escape) { cancel?.Invoke(); return; }
            if (controls.Count == 0) return;
            GameObject selected = events.currentSelectedGameObject;
            InputField input = selected.GetComponent<InputField>();
            if (input != null && input.isFocused)
            {
                if (key != KeyCode.Tab) return;
                input.DeactivateInputField();
            }
            int index = controls.FindIndex(control => control.gameObject == selected);
            Slider slider = selected != null ? selected.GetComponent<Slider>() : null;
            if (slider != null && (key == KeyCode.LeftArrow || key == KeyCode.RightArrow || key == KeyCode.A || key == KeyCode.D))
            {
                ExecuteEvents.Execute(selected, new AxisEventData(events)
                {
                    moveDir = key == KeyCode.LeftArrow || key == KeyCode.A ? MoveDirection.Left : MoveDirection.Right
                }, ExecuteEvents.moveHandler);
                return;
            }
            int step = key == KeyCode.Tab ? (shift ? -1 : 1) :
                key == KeyCode.DownArrow || key == KeyCode.RightArrow || key == KeyCode.D || key == KeyCode.S ? 1 :
                key == KeyCode.UpArrow || key == KeyCode.LeftArrow || key == KeyCode.A || key == KeyCode.W ? -1 : 0;
            if (step != 0)
            {
                retained = controls[(index + step + controls.Count) % controls.Count].gameObject;
                events.SetSelectedGameObject(retained);
                Reveal(retained);
            }
            else if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space)
                ExecuteEvents.Execute(selected, new BaseEventData(events), ExecuteEvents.submitHandler);
            else if (key == KeyCode.PageDown || key == KeyCode.PageUp || key == KeyCode.Home || key == KeyCode.End)
            {
                ScrollRect area = selected.GetComponentInParent<ScrollRect>();
                if (area == null) area = GetComponentInChildren<ScrollRect>();
                if (area != null) area.verticalNormalizedPosition = key == KeyCode.Home ? 1 : key == KeyCode.End ? 0 :
                    Mathf.Clamp01(area.verticalNormalizedPosition + (key == KeyCode.PageUp ? 0.25f : -0.25f));
            }
        }

        private void Update()
        {
            if (owners.Count == 0 || owners[owners.Count - 1] != this) return;
            RecoverFocus();
            if (Time.frameCount <= openedFrame || handledFrame == Time.frameCount) return;
            foreach (KeyCode key in PcInputBindings.MenuKeys)
                if (Input.GetKeyDown(key))
                {
                    handledFrame = Time.frameCount;
                    HandleKey(key, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                    return;
                }
        }

        private void Collect()
        {
            controls.Clear();
            foreach (Selectable control in GetComponentsInChildren<Selectable>())
            {
                bool visible = control.isActiveAndEnabled && control.IsInteractable();
                foreach (CanvasGroup group in control.GetComponentsInParent<CanvasGroup>())
                    visible &= group.alpha > 0 && group.interactable;
                if (visible) controls.Add(control);
            }
        }

        private bool Contains(GameObject target) => target != null && controls.Exists(control => control.gameObject == target);

        private static void Reveal(GameObject target)
        {
            ScrollRect scroll = target.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null || scroll.viewport == null) return;
            Canvas.ForceUpdateCanvases();
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, (RectTransform)target.transform);
            float delta = bounds.max.y > scroll.viewport.rect.yMax ? scroll.viewport.rect.yMax - bounds.max.y :
                bounds.min.y < scroll.viewport.rect.yMin ? scroll.viewport.rect.yMin - bounds.min.y : 0;
            scroll.content.anchoredPosition += new Vector2(0, delta);
        }

        private void OnDisable()
        {
            owners.Remove(this);
            if (events == null) return;
            events.sendNavigationEvents = owners.Count == 0 && restoreNavigation;
            if (events.currentSelectedGameObject != null && events.currentSelectedGameObject.transform.IsChildOf(transform))
                events.SetSelectedGameObject(returnFocus != null && returnFocus.activeInHierarchy ? returnFocus : null);
        }
    }
}