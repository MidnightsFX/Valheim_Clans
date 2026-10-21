using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Clans.UI {
    // Finds and wires the named children of a prefab (see UI/README.md). A missing child is logged once and
    // skipped, so a hand-edited prefab that drops something degrades instead of throwing.
    internal static class UiBind {
        private static readonly HashSet<string> warned = new HashSet<string>();

        // A child by path ("Header/Emblem"), or failing that by name anywhere below root.
        internal static Transform Find(Transform root, string path) {
            if (root == null || string.IsNullOrEmpty(path)) { return null; }
            Transform found = root.Find(path);
            if (found != null) { return found; }
            string name = path.Substring(path.LastIndexOf('/') + 1);
            found = FindDeep(root, name);
            if (found == null && warned.Add(root.name + "/" + path)) {
                Logger.LogWarning($"Clans UI: '{root.name}' has no '{path}'; that part of the window is skipped.");
            }
            return found;
        }

        private static Transform FindDeep(Transform root, string name) {
            for (int i = 0; i < root.childCount; i++) {
                Transform child = root.GetChild(i);
                if (child.name == name) { return child; }
                Transform deeper = FindDeep(child, name);
                if (deeper != null) { return deeper; }
            }
            return null;
        }

        internal static T Get<T>(Transform root, string path) where T : Component {
            Transform found = Find(root, path);
            return found != null ? found.GetComponent<T>() : null;
        }

        internal static void SetText(Transform root, string path, string text) {
            TMP_Text label = Get<TMP_Text>(root, path);
            if (label != null) { label.text = text ?? ""; }
        }

        // The caption of a button (its child "Text").
        internal static void SetButtonText(Button button, string text) {
            if (button == null) { return; }
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) { label.text = text ?? ""; }
        }

        internal static Button OnClick(Transform root, string path, UnityAction action) {
            Button button = Get<Button>(root, path);
            OnClick(button, action);
            return button;
        }

        internal static void OnClick(Button button, UnityAction action) {
            if (button == null) { return; }
            // Replaces the whole event, persistent listeners included: a cloned vanilla button would otherwise
            // keep calling what the original did.
            button.onClick = new Button.ButtonClickedEvent();
            if (action != null) { button.onClick.AddListener(action); }
        }

        // Vanilla marks the current tab by disabling its button: it then shows the selected tab sprite and its
        // label turns the title colour (see ClansStyle). A "Selected" child, if the prefab has one, is shown too.
        internal static void SetTabSelected(Button button, bool selected) {
            if (button == null) { return; }
            button.interactable = selected == false;
            Transform marker = button.transform.Find("Selected");
            if (marker != null) { marker.gameObject.SetActive(selected); }
        }

        internal static void SetActive(Transform root, string path, bool active) {
            Transform found = Find(root, path);
            if (found != null && found.gameObject.activeSelf != active) { found.gameObject.SetActive(active); }
        }

        internal static void SetInteractable(Button button, bool interactable) {
            if (button != null) { button.interactable = interactable; }
        }

        // A bar's fill (0-1). Sized by its anchors, which works whether or not the Image has a sprite: a
        // Filled image without one ignores fillAmount and draws in full.
        internal static void SetBar(Image fill, float value) {
            if (fill == null) { return; }
            value = Mathf.Clamp01(value);
            fill.fillAmount = 1f;
            RectTransform rect = fill.rectTransform;
            if (rect.anchorMax.x != value) { rect.anchorMax = new Vector2(value, rect.anchorMax.y); }
        }

        internal static string Loc(string text) => ClansStyle.Localize(text);

        internal static string Loc(string token, params object[] args) {
            string text = ClansStyle.Localize(token);
            if (args == null || args.Length == 0) { return text; }
            try {
                return string.Format(text, args);
            } catch (FormatException) {
                return text;
            }
        }
    }

    // The clones of a list's inactive Item template (see UI/README.md). Sync makes exactly count clones,
    // reusing the ones it already has, and hands each to bind with its index.
    internal sealed class TemplateList {
        private readonly GameObject template;
        private readonly List<GameObject> items = new List<GameObject>();

        internal TemplateList(Transform parent, string templateName = "Item") {
            Transform found = parent == null ? null : parent.Find(templateName);
            if (found == null && parent != null) {
                // Lists keep their template in the scroll view's content.
                found = UiBind.Find(parent, templateName);
            }
            template = found != null ? found.gameObject : null;
            if (template != null) { template.SetActive(false); }
        }

        internal bool Valid => template != null;
        internal int Count => items.Count;
        internal IReadOnlyList<GameObject> Items => items;

        internal void Sync(int count, Action<GameObject, int> bind) {
            if (template == null) { return; }
            while (items.Count < count) {
                GameObject clone = UnityEngine.Object.Instantiate(template, template.transform.parent);
                clone.name = template.name + " " + items.Count;
                items.Add(clone);
            }
            for (int i = 0; i < items.Count; i++) {
                bool used = i < count;
                if (items[i].activeSelf != used) { items[i].SetActive(used); }
                if (used) {
                    try {
                        bind(items[i], i);
                    } catch (Exception e) {
                        Logger.LogError($"Clans UI: filling list entry {i} failed: {e}");
                    }
                }
            }
        }
    }
}
