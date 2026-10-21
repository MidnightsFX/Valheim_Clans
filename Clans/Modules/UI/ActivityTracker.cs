using BepInEx.Configuration;
using Clans.Client;
using Clans.Net;
using Clans.Server;
using Clans.Server.Model;
using Clans.Settings;
using Clans.UI.Pages;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI {
    // On the HUD, left of the minimap and below the status effects: each of your clan's activities under way
    // whose switch is on (the tracker menu in the inventory, or the Client settings), with what it still needs
    // and the time left. A Trophy Hunt lists every target's kills done and required; a Race its chests
    // delivered; a World Boss Battle its wave. Lives under Hud.m_rootObject, so it hides with the rest of the
    // HUD, and it hides while the large map is open. The player can move it elsewhere (see HudLayout).
    internal sealed class ActivityTracker : MonoBehaviour {
        // Often enough for the time left; changes from the server redraw it at once.
        private const float RefreshSeconds = 1f;
        // Vanilla's status effects run leftward from 31 px left of the minimap, in a row whose icons, names and
        // timers reach about 106 px below the minimap's top. The tracker's top-right corner goes just under
        // that row, so the effects keep their place. Measured against the minimap rather than read from the
        // status effect list, which other mods move.
        private const float LeftOfMinimap = 31f;
        private const float BelowMinimapTop = 114f;
        // Where vanilla puts the minimap's top-left corner, for worlds without one.
        private static readonly Vector2 MinimapTopLeft = new Vector2(-240f, -40f);
        private static readonly Color DoneCount = new Color(0.56f, 0.86f, 0.42f, 1f);
        private static readonly Color DoneText = new Color(0.66f, 0.66f, 0.66f, 1f);
        // Its outline in the layout editor while nothing is tracked.
        private static readonly Vector2 EmptySize = new Vector2(220f, 120f);

        private static ActivityTracker instance;

        private RectTransform frame;
        private HudLayout.Movable movable;
        private TemplateList blocks;
        private readonly Dictionary<GameObject, TemplateList> lineLists = new Dictionary<GameObject, TemplateList>();
        private readonly List<ActivityInfo> shown = new List<ActivityInfo>();
        private readonly List<Row> rows = new List<Row>();
        private readonly Vector3[] corners = new Vector3[4];
        private float refreshTimer;
        private bool dirty = true;
        private bool wasMapOpen;

        private struct Row {
            internal string Text;
            internal string Count;
            internal bool Done;
        }

        internal static void MarkDirty() {
            if (instance != null) { instance.dirty = true; }
        }

        // Called every frame from the client tick; builds the tracker once the HUD exists.
        internal static void Ensure() {
            if (instance != null || Hud.instance == null || Hud.instance.m_rootObject == null) { return; }
            GameObject prefab = ClansAssets.Prefab("Clans_HUD_Tracker");
            if (prefab == null) { return; }

            GameObject host = new GameObject("Clans_ActivityTracker", typeof(RectTransform));
            host.transform.SetParent(Hud.instance.m_rootObject.transform, false);
            RectTransform rect = (RectTransform)host.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            instance = host.AddComponent<ActivityTracker>();
            instance.Build(prefab);
        }

        // Your clan's activities that are under way (waiting at their totem, running, or at the boss), all of
        // them whatever their switch says.
        internal static void MineUnderWay(List<ActivityInfo> into) {
            into.Clear();
            long clan = ClanClient.Me.ClanId;
            if (clan == 0) { return; }
            foreach (ActivityInfo info in ActivityClient.Active) {
                if (info.ClanId == clan && info.Active) { into.Add(info); }
            }
        }

        private void Build(GameObject prefab) {
            GameObject go = Instantiate(prefab, transform, false);
            ClansStyle.Apply(go);
            frame = (RectTransform)go.transform;
            blocks = new TemplateList(frame, "Activity");
            go.SetActive(false);

            ActivityClient.Changed += MarkDirty;
            ClanClient.AccountChanged += MarkDirty;
            foreach (ActivityType type in Types) { ClansConfig.Tracked(type).SettingChanged += OnSettingChanged; }
            ClansConfig.TrackerPosition.SettingChanged += OnSettingChanged;

            movable = new HudLayout.Movable {
                Label = "$clans_layout_tracker",
                Rect = frame,
                EmptySize = EmptySize,
                HasContent = () => frame.gameObject.activeSelf,
                Place = Place,
                Save = () => ClansConfig.TrackerPosition.Value = HudLayout.Format(HudLayout.FractionOf(frame)),
                Reset = () => {
                    ClansConfig.TrackerPosition.Value = "";
                    Place();
                },
            };
            HudLayout.Register(movable);
        }

        private static readonly ActivityType[] Types = { ActivityType.TrophyHunt, ActivityType.Race, ActivityType.WorldBoss };

        private void OnDestroy() {
            ActivityClient.Changed -= MarkDirty;
            ClanClient.AccountChanged -= MarkDirty;
            foreach (ActivityType type in Types) { ClansConfig.Tracked(type).SettingChanged -= OnSettingChanged; }
            ClansConfig.TrackerPosition.SettingChanged -= OnSettingChanged;
            HudLayout.Unregister(movable);
            if (instance == this) { instance = null; }
        }

        private void OnSettingChanged(object sender, EventArgs e) => dirty = true;

        private void Update() {
            refreshTimer -= Time.unscaledDeltaTime;
            bool mapOpen = Minimap.IsOpen();
            if (dirty == false && refreshTimer > 0f && mapOpen == wasMapOpen) { return; }
            wasMapOpen = mapOpen;
            dirty = false;
            refreshTimer = RefreshSeconds;
            try {
                Refresh(mapOpen);
            } catch (Exception e) {
                Logger.LogError($"The Clans activity tracker failed to refresh: {e}");
            }
        }

        private void Refresh(bool mapOpen) {
            MineUnderWay(shown);
            shown.RemoveAll(info => ClansConfig.Tracked(info.Type).Value == false);
            bool show = shown.Count > 0 && mapOpen == false && ClansConfig.EnableActivities.Value;
            if (frame.gameObject.activeSelf != show) { frame.gameObject.SetActive(show); }
            if (show == false) { return; }
            // While the layout is being edited, the editor has it.
            if (HudLayout.Editing == false) { Place(); }
            blocks.Sync(shown.Count, (block, i) => Fill(block, shown[i]));
        }

        // Where the player put it; otherwise left of the minimap, under the status effects' row (where the
        // minimap would be when it is hidden). Kept on screen as its list grows.
        private void Place() {
            if (HudLayout.TryParse(ClansConfig.TrackerPosition.Value, out Vector2 saved)) {
                HudLayout.ApplyFraction(frame, saved);
            } else {
                PlaceByMinimap();
            }
            HudLayout.Clamp(frame, movable.Size);
        }

        private void PlaceByMinimap() {
            RectTransform parent = (RectTransform)frame.parent;
            Minimap map = Minimap.instance;
            RectTransform small = map != null && map.m_smallRoot != null && map.m_smallRoot.activeInHierarchy
                ? map.m_smallRoot.transform as RectTransform : null;
            Vector3 point;
            if (small != null) {
                small.GetWorldCorners(corners);
                point = parent.InverseTransformPoint(corners[1]);
            } else {
                point = new Vector3(parent.rect.xMax + MinimapTopLeft.x, parent.rect.yMax + MinimapTopLeft.y, 0f);
            }
            // The frame's pivot is its top-right corner.
            point.x -= LeftOfMinimap;
            point.y -= BelowMinimapTop;
            point.z = 0f;
            if (frame.localPosition != point) { frame.localPosition = point; }
        }

        private void Fill(GameObject block, ActivityInfo info) {
            Transform t = block.transform;
            Image icon = UiBind.Get<Image>(t, "Head/Icon");
            Sprite sprite = icon != null ? ClansAssets.BundleSprite(ActivitiesPage.IconName(info.Type)) : null;
            if (sprite != null && icon.sprite != sprite) { icon.sprite = sprite; }
            UiBind.SetText(t, "Head/Title", UiBind.Loc(ActivityService.Name(info.Type)));
            UiBind.SetText(t, "Head/Time", info.DeadlineUtc > 0 ? UiFormat.Span(info.DeadlineUtc - ClanClient.ServerNow) : "");

            if (lineLists.TryGetValue(block, out TemplateList lines) == false) {
                lines = new TemplateList(UiBind.Find(t, "Lines"), "Line");
                lineLists[block] = lines;
            }
            Rows(info, rows);
            lines.Sync(rows.Count, (line, j) => {
                Row row = rows[j];
                TMP_Text text = UiBind.Get<TMP_Text>(line.transform, "Text");
                if (text != null) {
                    text.text = row.Text;
                    text.color = row.Done ? DoneText : Color.white;
                }
                TMP_Text count = UiBind.Get<TMP_Text>(line.transform, "Count");
                if (count != null) {
                    count.text = row.Count;
                    count.color = row.Done ? DoneCount : Color.white;
                }
            });
        }

        private static void Rows(ActivityInfo info, List<Row> into) {
            into.Clear();
            if (info.State == ActivityState.Placed) {
                into.Add(new Row { Text = UiBind.Loc("$clans_tracker_waiting"), Count = "" });
                return;
            }
            switch (info.Type) {
                case ActivityType.TrophyHunt:
                    foreach (HuntTarget target in info.Targets) {
                        int count = Mathf.Min(target.Count, target.Required);
                        into.Add(new Row {
                            Text = ActivitiesPage.CreatureName(target.Prefab, target.Prefab),
                            Count = $"{count}/{target.Required}",
                            Done = count >= target.Required,
                        });
                    }
                    break;
                case ActivityType.Race:
                    into.Add(new Row { Text = UiBind.Loc("$clans_tracker_chests"), Count = $"{info.Delivered}/{info.ChestsTotal}" });
                    break;
                default:
                    if (info.State == ActivityState.BossPhase) {
                        into.Add(new Row { Text = UiBind.Loc("$clans_wb_boss_phase"), Count = "" });
                    } else {
                        into.Add(new Row { Text = UiBind.Loc("$clans_tracker_wave"), Count = $"{Mathf.Max(1, info.Phase)}/{ClansSettings.Activities.WorldBoss.Waves}" });
                    }
                    break;
            }
        }
    }

    // The tracker's switches, in the inventory: a button one step left of the compendium, shown while your clan
    // has an activity under way, opens a short menu with a switch for each such activity.
    //
    // The button is a copy of the crafting panel's repair button (the square wood-framed button other mods,
    // Dverger Automation among them, also copy), which brings vanilla's frame, hover and press tints and tooltip.
    internal sealed class TrackerMenu : MonoBehaviour {
        private const string ButtonName = "Clans_TrackerButton";
        private const float GapLeftOfButton = 6f;

        private GameObject button;
        private RectTransform buttonRect;
        private GameObject menu;
        private TemplateList items;
        private readonly List<ActivityInfo> underWay = new List<ActivityInfo>();
        private readonly Vector3[] corners = new Vector3[4];

        internal static void Create(InventoryGui gui) {
            GameObject prefab = ClansAssets.Prefab("Clans_INV_TrackerMenu");
            Transform info = gui != null ? gui.m_info : null;
            Transform texts = info != null ? info.Find("Texts") : null;
            if (prefab == null) { return; }
            if (texts == null || gui.m_repairButton == null || info.parent == null) {
                Logger.LogWarning("The inventory screen has no compendium or repair button; the activity tracker button was not added.");
                return;
            }

            GameObject button = MakeButton(gui.m_repairButton.gameObject, info);
            RectTransform rect = (RectTransform)button.transform;
            RectTransform textsRect = (RectTransform)texts;
            // As far left of the compendium as the Skills button is right of it.
            Transform skills = info.Find("Skills");
            float step = skills != null
                ? ((RectTransform)skills).anchoredPosition.x - textsRect.anchoredPosition.x
                : textsRect.sizeDelta.x * 1.7f;
            rect.anchorMin = textsRect.anchorMin;
            rect.anchorMax = textsRect.anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = textsRect.anchoredPosition - new Vector2(step, 0f);

            // In the inventory screen's root rather than the top-right panel, so the panels drawn after that
            // one (crafting, containers) cannot cover it.
            GameObject menu = Instantiate(prefab, info.parent, false);
            menu.name = "Clans_TrackerMenu";
            ClansStyle.Apply(menu);
            menu.SetActive(false);

            // On an empty object of its own, which stays active while the button and the menu come and go.
            GameObject holder = new GameObject("Clans_TrackerMenuOwner", typeof(RectTransform));
            holder.transform.SetParent(info, false);
            TrackerMenu owner = holder.AddComponent<TrackerMenu>();
            owner.button = button;
            owner.buttonRect = rect;
            owner.menu = menu;
            owner.items = new TemplateList(UiBind.Find(menu.transform, "List"));
            button.GetComponent<Button>().onClick.AddListener(owner.ToggleMenu);
            ActivityClient.Changed += owner.Refresh;
            ClanClient.AccountChanged += owner.Refresh;
            owner.Refresh();
        }

        private static GameObject MakeButton(GameObject repair, Transform parent) {
            GameObject go = Instantiate(repair, parent, false);
            go.name = ButtonName;
            // The template hides while no station can repair, and a copy of a hidden object starts hidden.
            go.SetActive(true);
            // A mouse-only control: the repair button's gamepad binding and its hint stay with the original.
            UIGamePad pad = go.GetComponent<UIGamePad>();
            if (pad != null) { Destroy(pad); }
            Transform hint = go.transform.Find("gamepad_hint");
            if (hint != null) { Destroy(hint.gameObject); }
            // The scripts that grey the icon and label while there is nothing to repair; this button is never
            // unavailable. By name, as they live in an assembly Clans does not reference.
            foreach (MonoBehaviour behaviour in go.GetComponents<MonoBehaviour>()) {
                string type = behaviour.GetType().Name;
                if (type == "ButtonImageColor" || type == "ButtonTextColor") { Destroy(behaviour); }
            }
            Transform glow = go.transform.Find("Glow");
            if (glow != null) { glow.gameObject.SetActive(false); }
            Transform label = go.transform.Find("Text");
            TMP_Text text = label != null ? label.GetComponent<TMP_Text>() : null;
            if (text != null) { text.text = ""; }

            Button button = go.GetComponent<Button>();
            UiBind.OnClick(button, null);
            button.interactable = true;
            Image icon = UiBind.Get<Image>(go.transform, "Image");
            Sprite sprite = ClansAssets.BundleSprite("clans_tracker");
            if (icon != null && sprite != null) {
                icon.sprite = sprite;
                icon.color = Color.white;
            }
            UITooltip tooltip = go.GetComponent<UITooltip>();
            if (tooltip != null) { tooltip.m_text = "$clans_tracker_tooltip"; }
            return go;
        }

        private void OnDestroy() {
            ActivityClient.Changed -= Refresh;
            ClanClient.AccountChanged -= Refresh;
            if (menu != null) { Destroy(menu); }
        }

        private void ToggleMenu() {
            if (menu.activeSelf) {
                menu.SetActive(false);
                return;
            }
            Refresh();
            Place();
            menu.SetActive(true);
            menu.transform.SetAsLastSibling();
        }

        // To the left of the button, top edges level, so it opens over the world rather than the inventory.
        private void Place() {
            RectTransform menuRect = (RectTransform)menu.transform;
            RectTransform parent = (RectTransform)menuRect.parent;
            buttonRect.GetWorldCorners(corners);
            Vector3 topLeft = parent.InverseTransformPoint(corners[1]);
            menuRect.pivot = new Vector2(1f, 1f);
            menuRect.anchorMin = menuRect.anchorMax = parent.pivot;
            menuRect.localPosition = new Vector3(topLeft.x - GapLeftOfButton, topLeft.y, 0f);
        }

        // The button only while there is something to switch; the menu closes with the inventory.
        private void LateUpdate() {
            if (menu.activeSelf && (InventoryGui.IsVisible() == false || button.activeSelf == false)) { menu.SetActive(false); }
        }

        private void Refresh() {
            if (button == null || menu == null) { return; }
            ActivityTracker.MineUnderWay(underWay);
            bool any = underWay.Count > 0 && ClansConfig.EnableActivities.Value;
            if (button.activeSelf != any) { button.SetActive(any); }
            if (any == false) {
                if (menu.activeSelf) { menu.SetActive(false); }
                return;
            }
            items.Sync(underWay.Count, (item, i) => {
                ConfigEntry<bool> entry = ClansConfig.Tracked(underWay[i].Type);
                UiBind.SetText(item.transform, "Label", UiBind.Loc(ActivityService.Name(underWay[i].Type)));
                Toggle toggle = UiBind.Get<Toggle>(item.transform, "Toggle");
                if (toggle == null) { return; }
                toggle.onValueChanged.RemoveAllListeners();
                toggle.SetIsOnWithoutNotify(entry.Value);
                toggle.onValueChanged.AddListener(on => entry.Value = on);
            });
        }
    }
}
