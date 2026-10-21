using Clans.Client;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Clans.UI {
    // The Clans HUD pieces a player can move: the activity tracker, the party frame and the chat panel. Each
    // registers itself when it is built. The Layout button in the Clans window starts the edit mode: the
    // inventory closes but input stays held, a grey outline is drawn over every piece, and dragging an outline
    // moves its piece. Done Editing (or Escape) saves where they are and lets go of input.
    //
    // Positions are kept in each piece's Client setting. The tracker and the party frame store their pivot
    // as fractions of the screen ("x,y"), so they keep their place when the resolution or GUI scale changes;
    // the chat panel keeps its own "x,y,width,height". Every placement is clamped so the piece stays on screen.
    internal static class HudLayout {
        // A piece of the HUD that can be moved.
        internal sealed class Movable {
            // Its name on the outline, a localization token.
            internal string Label;
            internal RectTransform Rect;
            // The outline's size while the piece shows nothing (no activity under way, no party).
            internal Vector2 EmptySize;
            internal Func<bool> HasContent;
            // Puts it at its saved spot, or its default one.
            internal Action Place;
            // Writes where it is now to its setting.
            internal Action Save;
            // Forgets the saved spot and puts it back at its default one.
            internal Action Reset;

            // Dragged since the editor opened: only these are saved, so an untouched tracker keeps
            // following the minimap.
            internal bool Moved;

            internal bool Alive => Rect != null;

            // The room it takes on screen, in its own units.
            internal Vector2 Size => HasContent != null && HasContent() ? Rect.rect.size : EmptySize;
        }

        private static readonly List<Movable> movables = new List<Movable>();
        private static HudLayoutEditor editor;

        internal static bool Editing => editor != null;

        internal static IReadOnlyList<Movable> Movables => movables;

        internal static void Register(Movable movable) {
            if (movable != null && movables.Contains(movable) == false) { movables.Add(movable); }
        }

        internal static void Unregister(Movable movable) => movables.Remove(movable);

        // From the Layout button in the Clans window header.
        internal static void BeginEditing() {
            if (Editing || Hud.instance == null || Hud.instance.m_rootObject == null || Player.m_localPlayer == null) { return; }
            // Next to the HUD's root rather than under it: the root is moved off screen when the HUD is hidden,
            // and Done must always be there to click.
            Transform parent = Hud.instance.m_rootObject.transform.parent;
            GameObject prefab = ClansAssets.Prefab("Clans_HUD_LayoutEditor");
            if (parent == null || prefab == null) {
                ClanNotices.ShowError(UiBind.Loc("$clans_ui_missing"));
                return;
            }
            // The inventory closes first and takes the Clans window (and the window's hold on input) with it;
            // the editor takes its own hold in the same frame, so the cursor is never captured in between.
            if (InventoryGui.instance != null) { InventoryGui.instance.Hide(); }
            ClansWindow.Close();

            GameObject go = UnityEngine.Object.Instantiate(prefab, parent, false);
            go.name = "Clans_LayoutEditor";
            ClansStyle.Apply(go);
            editor = go.AddComponent<HudLayoutEditor>();
            try {
                editor.Build();
            } catch (Exception e) {
                Logger.LogError($"The Clans layout editor failed to open: {e}");
                EndEditing();
            }
        }

        // Done Editing, Escape, or the player going away: saves every piece that was moved where it is.
        internal static void EndEditing() {
            if (editor == null) { return; }
            HudLayoutEditor closing = editor;
            editor = null;
            SaveAll();
            closing.Finish();
            // Its input guard lets go when it is destroyed.
            UnityEngine.Object.Destroy(closing.gameObject);
        }

        internal static void ResetAll() {
            foreach (Movable movable in movables.ToArray()) {
                movable.Moved = false;
                if (movable.Alive) { Run(movable.Reset, "reset"); }
            }
        }

        internal static void PlaceAll() {
            foreach (Movable movable in movables.ToArray()) {
                movable.Moved = false;
                if (movable.Alive) { Run(movable.Place, "place"); }
            }
        }

        // The editor was destroyed without Done (the HUD went away on logout): whatever is still there is saved.
        internal static void EditorDestroyed(HudLayoutEditor destroyed) {
            if (editor != destroyed) { return; }
            editor = null;
            SaveAll();
        }

        private static void SaveAll() {
            foreach (Movable movable in movables.ToArray()) {
                if (movable.Moved && movable.Alive) { Run(movable.Save, "save"); }
                movable.Moved = false;
            }
        }

        private static void Run(Action action, string what) {
            if (action == null) { return; }
            try {
                action();
            } catch (Exception e) {
                Logger.LogError($"Clans HUD layout: could not {what} a HUD element: {e}");
            }
        }

        // ---------------------------------------------------------------- positions

        // "x,y" as fractions of the parent; empty or unreadable is no saved spot.
        internal static bool TryParse(string value, out Vector2 fraction) {
            fraction = Vector2.zero;
            string[] parts = (value ?? "").Split(',');
            if (parts.Length != 2) { return false; }
            if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) == false
                || float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) == false) { return false; }
            fraction = new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
            return true;
        }

        internal static string Format(Vector2 fraction) =>
            fraction.x.ToString("0.####", CultureInfo.InvariantCulture) + "," + fraction.y.ToString("0.####", CultureInfo.InvariantCulture);

        // Where the element's pivot sits in its parent, as fractions from the bottom-left.
        internal static Vector2 FractionOf(RectTransform rect) {
            Rect area = ((RectTransform)rect.parent).rect;
            Vector3 p = rect.localPosition;
            return new Vector2(
                area.width > 0f ? Mathf.Clamp01((p.x - area.xMin) / area.width) : 0.5f,
                area.height > 0f ? Mathf.Clamp01((p.y - area.yMin) / area.height) : 0.5f);
        }

        internal static void ApplyFraction(RectTransform rect, Vector2 fraction) {
            Rect area = ((RectTransform)rect.parent).rect;
            Vector3 point = new Vector3(area.xMin + fraction.x * area.width, area.yMin + fraction.y * area.height, 0f);
            if (rect.localPosition != point) { rect.localPosition = point; }
        }

        // Moves the element (taking size, around its pivot) back inside its parent. One taller or wider than the
        // screen keeps its top-left corner on it.
        internal static void Clamp(RectTransform rect, Vector2 size) {
            if (rect == null || !(rect.parent is RectTransform parent)) { return; }
            Rect area = parent.rect;
            size = Vector2.Scale(size, rect.localScale);
            Vector2 pivot = rect.pivot;
            float minX = area.xMin + pivot.x * size.x;
            float maxX = area.xMax - (1f - pivot.x) * size.x;
            float minY = area.yMin + pivot.y * size.y;
            float maxY = area.yMax - (1f - pivot.y) * size.y;
            Vector3 p = rect.localPosition;
            Vector3 clamped = new Vector3(
                maxX < minX ? minX : Mathf.Clamp(p.x, minX, maxX),
                maxY < minY ? maxY : Mathf.Clamp(p.y, minY, maxY),
                0f);
            if (p != clamped) { rect.localPosition = clamped; }
        }

        // The movable's box in world space, from its pivot and size, so a piece showing nothing still has one.
        internal static void WorldBox(Movable movable, out Vector3 min, out Vector3 max) {
            RectTransform rect = movable.Rect;
            Vector2 size = Vector2.Scale(movable.Size, rect.localScale);
            Vector3 p = rect.localPosition;
            Vector3 bottomLeft = new Vector3(p.x - rect.pivot.x * size.x, p.y - rect.pivot.y * size.y, 0f);
            Transform parent = rect.parent;
            min = parent.TransformPoint(bottomLeft);
            max = parent.TransformPoint(bottomLeft + new Vector3(size.x, size.y, 0f));
        }
    }

    // The edit mode's overlay: an outline per movable piece, and Done Editing and Reset in the middle of the
    // screen. Built from Clans_HUD_LayoutEditor with its own canvas, so it draws over the HUD and the chat panel
    // and gets the clicks. Holds input (cursor free, no movement, attack or looking) for as long as it exists.
    internal sealed class HudLayoutEditor : MonoBehaviour, IPointerDownHandler {
        private const int SortingOrder = 1000;

        private sealed class Outline {
            internal HudLayout.Movable Movable;
            internal RectTransform Rect;
            internal Image[] Lines;
            internal Color[] IdleColors;
            internal GameObject Label;
            internal bool Lit;
        }

        private RectTransform area;
        private TemplateList outlineList;
        private readonly List<Outline> outlines = new List<Outline>();
        private readonly List<HudLayout.Movable> bound = new List<HudLayout.Movable>();
        private int selected = -1;
        private int hovered = -1;
        private bool restoreHudHidden;

        internal void Build() {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;
            gameObject.AddComponent<GraphicRaycaster>();
            gameObject.AddComponent<ConfigUI.ConfigUIInputGuard>().Hold();
            transform.SetAsLastSibling();

            Transform outlinesRoot = UiBind.Find(transform, "Outlines") ?? transform;
            area = (RectTransform)outlinesRoot;
            outlineList = new TemplateList(outlinesRoot, "Outline");
            UiBind.OnClick(transform, "Center/Done", HudLayout.EndEditing);
            UiBind.OnClick(transform, "Center/Reset", HudLayout.ResetAll);

            // The pieces sit under the HUD's root, which is off screen while the HUD is hidden (Ctrl+F3).
            if (Hud.instance != null && Hud.instance.m_userHidden) {
                restoreHudHidden = true;
                Hud.instance.m_userHidden = false;
            }
            // A piece that has not shown yet (no activity so far) has never been placed.
            HudLayout.PlaceAll();
            Bind();
        }

        // Called once when Done is pressed, before the overlay is destroyed.
        internal void Finish() {
            if (restoreHudHidden && Hud.instance != null) { Hud.instance.m_userHidden = true; }
            restoreHudHidden = false;
        }

        private void OnDestroy() {
            Finish();
            HudLayout.EditorDestroyed(this);
        }

        private void Update() {
            if (Player.m_localPlayer == null) {
                HudLayout.EndEditing();
                return;
            }
            if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB")) { HudLayout.EndEditing(); }
        }

        private void LateUpdate() {
            if (Changed()) { Bind(); }
            for (int i = 0; i < outlines.Count; i++) {
                Outline outline = outlines[i];
                HudLayout.Movable movable = outline.Movable;
                if (movable.Alive == false) {
                    if (outline.Rect.gameObject.activeSelf) { outline.Rect.gameObject.SetActive(false); }
                    continue;
                }
                // A piece whose content grew past the screen's edge comes back on it.
                HudLayout.Clamp(movable.Rect, movable.Size);
                HudLayout.WorldBox(movable, out Vector3 worldMin, out Vector3 worldMax);
                Vector3 min = area.InverseTransformPoint(worldMin);
                Vector3 max = area.InverseTransformPoint(worldMax);
                outline.Rect.localPosition = new Vector3(min.x, min.y, 0f);
                outline.Rect.sizeDelta = new Vector2(Mathf.Max(8f, max.x - min.x), Mathf.Max(8f, max.y - min.y));

                bool empty = movable.HasContent == null || movable.HasContent() == false;
                if (outline.Label != null && outline.Label.activeSelf != empty) { outline.Label.SetActive(empty); }
                SetLit(outline, i == selected || i == hovered);
            }
        }

        // Pieces built or destroyed while editing (the chat panel coming up late, a logout) rebuild the outlines.
        private bool Changed() {
            IReadOnlyList<HudLayout.Movable> now = HudLayout.Movables;
            if (now.Count != bound.Count) { return true; }
            for (int i = 0; i < now.Count; i++) {
                if (now[i] != bound[i]) { return true; }
            }
            return false;
        }

        private void Bind() {
            bound.Clear();
            bound.AddRange(HudLayout.Movables);
            // The views are reused: back to grey before their colours are read again.
            foreach (Outline old in outlines) { SetLit(old, false); }
            outlines.Clear();
            selected = hovered = -1;
            outlineList.Sync(bound.Count, (go, i) => {
                RectTransform rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = Vector2.zero;
                Transform border = UiBind.Find(go.transform, "Border");
                Image[] lines = border != null ? border.GetComponentsInChildren<Image>(true) : new Image[0];
                Color[] idle = new Color[lines.Length];
                for (int l = 0; l < lines.Length; l++) { idle[l] = lines[l].color; }
                Transform label = UiBind.Find(go.transform, "Label");
                UiBind.SetText(go.transform, "Label", UiBind.Loc(bound[i].Label));

                Transform fill = UiBind.Find(go.transform, "Fill") ?? go.transform;
                HudLayoutDrag drag = fill.GetComponent<HudLayoutDrag>();
                if (drag == null) { drag = fill.gameObject.AddComponent<HudLayoutDrag>(); }
                drag.Editor = this;
                drag.Index = i;

                outlines.Add(new Outline { Movable = bound[i], Rect = rect, Lines = lines, IdleColors = idle, Label = label != null ? label.gameObject : null });
            });
        }

        private static void SetLit(Outline outline, bool lit) {
            if (outline.Lit == lit) { return; }
            outline.Lit = lit;
            Color orange = GUIManager.Instance.ValheimOrange;
            for (int l = 0; l < outline.Lines.Length; l++) {
                if (outline.Lines[l] != null) { outline.Lines[l].color = lit ? orange : outline.IdleColors[l]; }
            }
        }

        // ---------------------------------------------------------------- pointer

        // A click on empty space drops the selection.
        public void OnPointerDown(PointerEventData eventData) => selected = -1;

        internal void Select(int index) {
            if (index < 0 || index >= outlines.Count) { return; }
            selected = index;
            // In front of the others, where it can be grabbed again.
            outlines[index].Rect.SetAsLastSibling();
        }

        internal void Hover(int index, bool over) {
            if (over) { hovered = index; } else if (hovered == index) { hovered = -1; }
        }

        internal void DragBy(int index, Vector2 delta) {
            if (index < 0 || index >= outlines.Count) { return; }
            HudLayout.Movable movable = outlines[index].Movable;
            if (movable.Alive == false) { return; }
            // The pointer moves in screen pixels; the piece moves in its parent's units.
            Vector3 scale = movable.Rect.parent.lossyScale;
            Vector3 move = new Vector3(scale.x > 0f ? delta.x / scale.x : delta.x, scale.y > 0f ? delta.y / scale.y : delta.y, 0f);
            movable.Rect.localPosition += move;
            HudLayout.Clamp(movable.Rect, movable.Size);
            movable.Moved = true;
        }
    }

    // On an outline's fill: pressing selects it, dragging with the left button moves its piece.
    internal sealed class HudLayoutDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerEnterHandler, IPointerExitHandler {
        internal HudLayoutEditor Editor;
        internal int Index;

        public void OnPointerDown(PointerEventData eventData) => Editor?.Select(Index);

        public void OnDrag(PointerEventData eventData) {
            if (eventData.button == PointerEventData.InputButton.Left) { Editor?.DragBy(Index, eventData.delta); }
        }

        public void OnPointerEnter(PointerEventData eventData) => Editor?.Hover(Index, true);

        public void OnPointerExit(PointerEventData eventData) => Editor?.Hover(Index, false);
    }
}
