using Clans.Client;
using Clans.Compat;
using Clans.Net;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Clans.UI {
    // The Clans chat panel, in place of vanilla's chat window (Chatter-style): tabs for All, Global, Clan,
    // Party and Raid, lines grouped under their sender with the time, a panel that can be dragged and resized,
    // and clickable links: hover a linked item for its tooltip, click an invite to join.
    //
    // Vanilla's chat keeps doing the work: its input field is moved into this panel (so Enter, history and
    // commands behave as always), and its own window stays in place but invisible, since vanilla decides chat
    // focus from it. Not built when Chatter is installed or the panel is turned off; Clans' lines then go into
    // the usual window (see ChatLog).
    internal sealed class ChatPanel : MonoBehaviour {
        private const int MaxRows = 120;
        // Lines grouped under one header before the next line starts a new one, so no row grows without end.
        private const int MaxLinesPerRow = 30;
        private const double GroupMinutes = 3;
        private const float TooltipRefreshSeconds = 0.25f;
        private static readonly Vector2 DefaultPosition = new Vector2(10f, 110f);
        private static readonly Vector2 DefaultSize = new Vector2(540f, 300f);
        private static readonly Vector2 MinSize = new Vector2(320f, 150f);
        private static readonly Vector2 MaxSize = new Vector2(1600f, 1000f);
        // Vanilla's chat window canvas order, for when it cannot be read.
        private const int DefaultSortingOrder = 900;

        private static ChatPanel instance;
        // The bundle's panel lacks a part it needs: vanilla's chat stays as it is for the session.
        private static bool broken;

        private enum TabKey { All, Global, Clan, Party, Raid }

        private sealed class Tab {
            internal TabKey Key;
            internal ChatChannel Channel;
            internal string Title;
            internal GameObject Button;
            internal bool Unread;
        }

        private sealed class Row {
            internal ChatChannel Channel;
            internal string Sender;
            internal string Key;
            internal DateTime First;
            internal DateTime Last;
            internal readonly StringBuilder Body = new StringBuilder();
            internal int Lines = 1;
            // Bumped when the row's text changes; the view is drawn again only then.
            internal int Version;
            internal GameObject View;
            internal int BoundVersion = -1;
        }

        private static readonly Tab[] allTabs = {
            new Tab { Key = TabKey.All, Channel = ChatChannel.Say, Title = "$clans_chat_tab_all" },
            new Tab { Key = TabKey.Global, Channel = ChatChannel.Global, Title = "$clans_chat_tag_global" },
            new Tab { Key = TabKey.Clan, Channel = ChatChannel.Clan, Title = "$clans_chat_tag_clan" },
            new Tab { Key = TabKey.Party, Channel = ChatChannel.Party, Title = "$clans_chat_tag_party" },
            new Tab { Key = TabKey.Raid, Channel = ChatChannel.Raid, Title = "$clans_chat_tag_raid" },
        };

        private readonly List<Tab> tabs = new List<Tab>();
        private readonly List<Row> rows = new List<Row>();
        private TemplateList tabButtons;
        // Row views are kept with their row and only drawn when it changes: one scrolling off the top is
        // reused at the bottom.
        private GameObject rowTemplate;
        private readonly Stack<GameObject> spareViews = new Stack<GameObject>();
        private readonly Dictionary<GameObject, TMP_Text> bodies = new Dictionary<GameObject, TMP_Text>();
        private bool shownTimestamps;
        private ScrollRect scroll;
        private CanvasGroup group;
        private TMP_Text channelLabel;
        private RectTransform rect;
        private HudLayout.Movable movable;
        private UITooltip tooltip;
        private TabKey current = TabKey.All;
        private bool stickToBottom;
        private bool rowsDirty;
        private int tabMask = -1;
        private ChatChannel? shownChannel;
        // The link under the pointer, looked up again only when something there can have changed.
        private bool linkUsable;
        private Vector3 linkPointer;
        private float linkScroll;
        private bool linksStale = true;
        private string linkId;
        private TMP_Text linkText;
        private string hoveredLink;
        private float tooltipTimer;
        private readonly List<RaycastResult> raycastHits = new List<RaycastResult>();
        private PointerEventData pointerData;
        private EventSystem pointerEvents;

        // ---------------------------------------------------------------- lifetime

        // Called every frame by the client: builds the panel once the game's chat and HUD exist.
        internal static void Ensure() {
            if (instance != null) { return; }
            ChatLog.PanelActive = false;
            if (broken || ClansConfig.ChatPanel.Value == false || ModPresence.Is(ModPresence.Capability.Chatter)) { return; }
            Chat chat = Chat.instance;
            if (chat == null || chat.m_chatWindow == null || chat.m_input == null || Hud.instance == null || Hud.instance.m_rootObject == null
                || Hud.instance.m_rootObject.transform.parent == null || Player.m_localPlayer == null) { return; }
            GameObject prefab = ClansAssets.Prefab("Clans_HUD_Chat");
            if (prefab == null) { return; }
            // Vanilla's input field moves into the panel, so a panel without its parts would take chat away.
            if (UiBind.Find(prefab.transform, "InputRow/InputSlot") == null || UiBind.Find(prefab.transform, "Header/Tabs") == null
                || UiBind.Find(prefab.transform, "List") == null || prefab.GetComponentInChildren<ScrollRect>(true) == null) {
                broken = true;
                Logger.LogWarning("The Clans chat panel in the asset bundle is missing parts; the usual chat window is used instead.");
                return;
            }

            // Vanilla's chat window is a canvas of its own under IngameGui, which has none, so a panel beside it
            // would never be drawn. It goes on the HUD's canvas instead, next to the HUD's root (which is moved
            // off screen when the HUD is hidden; the panel fades itself then), with its own canvas drawn where
            // vanilla's chat window is: above the HUD and the inventory.
            GameObject go = Instantiate(prefab, Hud.instance.m_rootObject.transform.parent, false);
            go.name = "Clans_Chat";
            Canvas vanillaCanvas = chat.m_chatWindow.GetComponent<Canvas>();
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = vanillaCanvas != null ? vanillaCanvas.sortingOrder : DefaultSortingOrder;
            go.AddComponent<GraphicRaycaster>();
            ClansStyle.Apply(go);
            instance = go.AddComponent<ChatPanel>();
            instance.Build(chat);
        }

        private void Build(Chat chat) {
            rect = (RectTransform)transform;
            group = GetComponent<CanvasGroup>();
            if (group == null) { group = gameObject.AddComponent<CanvasGroup>(); }
            tabButtons = new TemplateList(UiBind.Find(transform, "Header/Tabs"), "Tab");
            Transform list = UiBind.Find(transform, "List");
            Transform template = list == null ? null : list.Find("Row") ?? UiBind.Find(list, "Row");
            rowTemplate = template != null ? template.gameObject : null;
            if (rowTemplate != null) { rowTemplate.SetActive(false); }
            shownTimestamps = ClansConfig.ChatTimestamps.Value;
            scroll = GetComponentInChildren<ScrollRect>(true);
            channelLabel = UiBind.Get<TMP_Text>(transform, "InputRow/Channel");

            // Vanilla's input field, moved in; vanilla's window stays (chat focus depends on it) but unseen.
            Transform slot = UiBind.Find(transform, "InputRow/InputSlot");
            RectTransform input = (RectTransform)chat.m_input.transform;
            input.SetParent(slot, false);
            input.anchorMin = Vector2.zero;
            input.anchorMax = Vector2.one;
            input.offsetMin = Vector2.zero;
            input.offsetMax = Vector2.zero;
            Image standIn = slot.GetComponent<Image>();
            if (standIn != null) { standIn.enabled = false; }
            CanvasGroup vanilla = chat.m_chatWindow.GetComponent<CanvasGroup>();
            if (vanilla == null) { vanilla = chat.m_chatWindow.gameObject.AddComponent<CanvasGroup>(); }
            vanilla.alpha = 0f;
            vanilla.blocksRaycasts = false;
            vanilla.interactable = false;

            AddDrag("Header", resize: false);
            AddDrag("Resize", resize: true);
            ApplySavedRect();
            ClansConfig.ChatPanelRect.SettingChanged += OnRectChanged;
            movable = new HudLayout.Movable {
                Label = "$clans_layout_chat",
                Rect = rect,
                EmptySize = DefaultSize,
                HasContent = () => true,
                Place = ApplySavedRect,
                Save = SaveRect,
                Reset = () => {
                    ClansConfig.ChatPanelRect.Value = "";
                    ApplySavedRect();
                },
            };
            HudLayout.Register(movable);

            tooltip = gameObject.AddComponent<UITooltip>();
            if (InventoryGui.instance != null && InventoryGui.instance.m_playerGrid != null) {
                UITooltip element = InventoryGui.instance.m_playerGrid.m_elementPrefab.GetComponent<UITooltip>();
                if (element != null) { tooltip.m_tooltipPrefab = element.m_tooltipPrefab; }
            }

            ChatLog.PanelActive = true;
            // What was said before the panel appeared (connection messages and the like).
            List<string> buffer = chat.m_chatBuffer;
            for (int i = Math.Max(0, buffer.Count - 40); i < buffer.Count; i++) { ChatLog.AddSystem(buffer[i]); }

            ChatLog.Added += OnAdded;
            ChatClient.ChannelChanged += OnChannelChanged;
            RefreshTabs();
            Rebuild();
        }

        private void OnDestroy() {
            ChatLog.Added -= OnAdded;
            ChatClient.ChannelChanged -= OnChannelChanged;
            ClansConfig.ChatPanelRect.SettingChanged -= OnRectChanged;
            HudLayout.Unregister(movable);
            if (instance == this) {
                instance = null;
                ChatLog.PanelActive = false;
            }
        }

        // ---------------------------------------------------------------- tabs

        private void RefreshTabs() {
            int mask = 0;
            for (int i = 0; i < allTabs.Length; i++) {
                if (allTabs[i].Key == TabKey.All || ChatClient.Available(allTabs[i].Channel)) { mask |= 1 << i; }
            }
            if (mask == tabMask) { return; }
            tabMask = mask;
            tabs.Clear();
            for (int i = 0; i < allTabs.Length; i++) {
                if ((mask & (1 << i)) != 0) { tabs.Add(allTabs[i]); }
            }
            if (tabs.Exists(t => t.Key == current) == false) { Select(TabKey.All, setChannel: true); }
            tabButtons.Sync(tabs.Count, (go, i) => {
                Tab tab = tabs[i];
                tab.Button = go;
                UiBind.SetButtonText(go.GetComponent<Button>(), UiBind.Loc(tab.Title));
                UiBind.OnClick(go.GetComponent<Button>(), () => Select(tab.Key, setChannel: true));
            });
            ShowTabState();
        }

        private void Select(TabKey key, bool setChannel) {
            Tab tab = Array.Find(allTabs, t => t.Key == key);
            if (setChannel && ChatClient.SetChannel(tab.Channel) == false) { return; }
            if (current != key) {
                current = key;
                Rebuild();
            }
            tab.Unread = false;
            ShowTabState();
        }

        private void ShowTabState() {
            foreach (Tab tab in tabs) {
                if (tab.Button == null) { continue; }
                UiBind.SetTabSelected(tab.Button.GetComponent<Button>(), tab.Key == current);
                Transform dot = tab.Button.transform.Find("Unread");
                if (dot != null) { dot.gameObject.SetActive(tab.Unread && tab.Key != current); }
            }
        }

        // Typing /c (or another channel command) moves the panel to that channel's tab.
        private void OnChannelChanged() {
            Select(TabOf(ChatClient.Current), setChannel: false);
        }

        private static TabKey TabOf(ChatChannel channel) {
            switch (channel) {
                case ChatChannel.Global: return TabKey.Global;
                case ChatChannel.Clan: return TabKey.Clan;
                case ChatChannel.Party: return TabKey.Party;
                case ChatChannel.Raid: return TabKey.Raid;
                default: return TabKey.All;
            }
        }

        private static bool Shows(TabKey key, ChatEntry entry) {
            return key == TabKey.All || TabOf(entry.Channel) == key && entry.Channel != ChatChannel.Say && entry.Channel != ChatChannel.System;
        }

        // ---------------------------------------------------------------- rows

        private void Rebuild() {
            foreach (Row row in rows) { Release(row); }
            rows.Clear();
            foreach (ChatEntry entry in ChatLog.Entries) {
                if (Shows(current, entry)) { Append(entry); }
            }
            rowsDirty = true;
            stickToBottom = true;
        }

        private void OnAdded(ChatEntry entry) {
            if (Shows(current, entry)) {
                bool atBottom = scroll == null || scroll.verticalNormalizedPosition <= 0.02f || scroll.content.rect.height <= scroll.viewport.rect.height;
                Append(entry);
                rowsDirty = true;
                if (atBottom) { stickToBottom = true; }
            }
            bool tabsChanged = false;
            foreach (Tab tab in tabs) {
                if (tab.Key != current && tab.Key != TabKey.All && Shows(tab.Key, entry) && tab.Unread == false) {
                    tab.Unread = true;
                    tabsChanged = true;
                }
            }
            if (tabsChanged) { ShowTabState(); }
        }

        private void Append(ChatEntry entry) {
            Row last = rows.Count > 0 ? rows[rows.Count - 1] : null;
            bool joins = last != null && entry.Channel != ChatChannel.System && last.Channel == entry.Channel && last.Key == entry.SenderKey
                && (entry.Time - last.Last).TotalMinutes < GroupMinutes && last.Lines < MaxLinesPerRow;
            if (joins) {
                last.Body.Append('\n').Append(entry.Body);
                last.Last = entry.Time;
                last.Lines++;
                last.Version++;
                return;
            }
            Row row = new Row { Channel = entry.Channel, Sender = entry.Sender, Key = entry.SenderKey, First = entry.Time, Last = entry.Time };
            row.Body.Append(entry.Body);
            rows.Add(row);
            while (rows.Count > MaxRows) {
                Release(rows[0]);
                rows.RemoveAt(0);
            }
        }

        private void Release(Row row) {
            if (row.View == null) { return; }
            row.View.SetActive(false);
            spareViews.Push(row.View);
            row.View = null;
        }

        // Rows are only ever added at the bottom, so a row given a view goes last and the order holds.
        private void SyncRows() {
            if (rowTemplate == null) { return; }
            foreach (Row row in rows) {
                if (row.View == null) {
                    row.View = spareViews.Count > 0 ? spareViews.Pop() : Instantiate(rowTemplate, rowTemplate.transform.parent);
                    row.View.name = "Row Item";
                    row.View.transform.SetAsLastSibling();
                    row.View.SetActive(true);
                    row.BoundVersion = -1;
                }
                if (row.BoundVersion == row.Version) { continue; }
                row.BoundVersion = row.Version;
                try {
                    Bind(row.View.transform, row);
                } catch (Exception e) {
                    Logger.LogError($"Clans chat: drawing a row failed: {e}");
                }
            }
            linksStale = true;
        }

        private TMP_Text BodyOf(GameObject view) {
            if (bodies.TryGetValue(view, out TMP_Text body) == false) {
                body = UiBind.Get<TMP_Text>(view.transform, "Body");
                bodies[view] = body;
            }
            return body;
        }

        private void Bind(Transform view, Row row) {
            bool system = row.Channel == ChatChannel.System;
            UiBind.SetActive(view, "Head", system == false);
            if (system == false) {
                // The channel tag is only needed where channels mix.
                bool tagged = current == TabKey.All && row.Channel != ChatChannel.Say;
                UiBind.SetActive(view, "Head/Channel", tagged);
                if (tagged) {
                    UiBind.SetText(view, "Head/Channel", "<color=" + ChatLog.ColorHex(row.Channel) + ">[" + UiBind.Loc(ChatLog.Tag(row.Channel)) + "]</color>");
                }
                UiBind.SetText(view, "Head/Name", row.Sender);
                UiBind.SetActive(view, "Head/Time", ClansConfig.ChatTimestamps.Value);
                UiBind.SetText(view, "Head/Time", row.First.ToString("HH:mm", CultureInfo.CurrentCulture));
            }
            TMP_Text body = BodyOf(view.gameObject);
            if (body != null) {
                // Typed "\n" and the like stay as typed.
                body.parseCtrlCharacters = false;
                body.text = row.Body.ToString();
                body.color = system ? new Color(0.82f, 0.82f, 0.82f, 1f) : row.Channel == ChatChannel.Say ? Color.white : ParseColor(ChatLog.ColorHex(row.Channel));
            }
        }

        private static Color ParseColor(string hex) => ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.white;

        // ---------------------------------------------------------------- per frame

        private void Update() {
            Chat chat = Chat.instance;
            if (chat == null) { return; }
            RefreshTabs();
            if (ClansConfig.ChatTimestamps.Value != shownTimestamps) {
                shownTimestamps = ClansConfig.ChatTimestamps.Value;
                foreach (Row row in rows) { row.BoundVersion = -1; }
                rowsDirty = true;
            }
            if (rowsDirty) {
                rowsDirty = false;
                SyncRows();
            }
            UpdateVisibility(chat);
            if (channelLabel != null && shownChannel != ChatClient.Current) {
                ChatChannel channel = ChatClient.Current;
                shownChannel = channel;
                channelLabel.text = "<color=" + ChatLog.ColorHex(channel) + ">" + UiBind.Loc(ChatLog.Tag(channel)) + "</color>";
            }
            UpdateLinks();
        }

        private void LateUpdate() {
            if (stickToBottom == false || scroll == null) { return; }
            stickToBottom = false;
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 0f;
        }

        // Shown while chatting, for a while after a message, whenever the cursor is free to use it and while
        // the HUD layout is being edited; otherwise faded to the configured opacity. Hidden with the rest of
        // the HUD.
        private void UpdateVisibility(Chat chat) {
            bool hudHidden = Hud.instance != null && Hud.IsUserHidden();
            bool pointerOver = ZCursor.IsVisible && RectTransformUtility.RectangleContainsScreenPoint(rect, ZInput.pointerPosition, null);
            bool active = chat.m_input.isFocused || chat.m_hideTimer < chat.m_hideDelay || InventoryGui.IsVisible() || pointerOver || HudLayout.Editing;
            float target = hudHidden ? 0f : active ? 1f : Mathf.Clamp01(ClansConfig.ChatIdleOpacity.Value);
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime * 4f);
            group.blocksRaycasts = group.alpha > 0.05f && ZCursor.IsVisible;
        }

        // ---------------------------------------------------------------- links

        private void UpdateLinks() {
            Vector3 pointer = ZInput.pointerPosition;
            bool usable = ZCursor.IsVisible && group.alpha > 0.5f;
            float scrolled = scroll != null ? scroll.verticalNormalizedPosition : 0f;
            bool click = usable && ZInput.GetKeyDown(KeyCode.Mouse0, logWarning: false);
            if (linksStale || click || usable != linkUsable || pointer != linkPointer || scrolled != linkScroll) {
                linksStale = false;
                linkUsable = usable;
                linkPointer = pointer;
                linkScroll = scrolled;
                FindLink(usable, pointer);
            }
            ChatLinkTarget target = ChatLog.Link(linkId);
            if (click && target != null) {
                if (target.Kind == ChatLinkKind.Bounty) {
                    BountyShare.ShowOnMap(target.Position);
                } else if (target.Kind != ChatLinkKind.Item) {
                    ChatClient.JoinInvite(target.InviteId);
                }
            }
            ShowItemTooltip(target != null && target.Kind == ChatLinkKind.Item ? target : null, linkId, linkText);
        }

        private void FindLink(bool usable, Vector3 pointer) {
            linkId = null;
            linkText = null;
            if (usable == false || PointerOnList(pointer) == false) { return; }
            foreach (Row row in rows) {
                if (row.View == null) { continue; }
                TMP_Text body = BodyOf(row.View);
                if (body == null || RectTransformUtility.RectangleContainsScreenPoint(body.rectTransform, pointer, null) == false) { continue; }
                int index = TMP_TextUtilities.FindIntersectingLink(body, pointer, null);
                if (index >= 0) {
                    linkId = body.textInfo.linkInfo[index].GetLinkID();
                    linkText = body;
                }
                return;
            }
        }

        // Only rows that can be seen count: the pointer must be inside the list's visible area, with nothing
        // else (an open inventory, a tooltip) drawn over the panel there.
        private bool PointerOnList(Vector3 pointer) {
            if (scroll == null || scroll.viewport == null || RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, pointer, null) == false) { return false; }
            EventSystem events = EventSystem.current;
            if (events == null) { return true; }
            if (pointerData == null || pointerEvents != events) {
                pointerData = new PointerEventData(events);
                pointerEvents = events;
            }
            pointerData.position = pointer;
            raycastHits.Clear();
            events.RaycastAll(pointerData, raycastHits);
            return raycastHits.Count == 0 || raycastHits[0].gameObject.transform.IsChildOf(transform);
        }

        // Vanilla's item tooltip, which Epic Loot fills in for magic items. Refreshed while hovered, so
        // holding Shift shows Epic Loot's details.
        private void ShowItemTooltip(ChatLinkTarget target, string id, TMP_Text over) {
            if (target == null || target.Item == null || tooltip == null || tooltip.m_tooltipPrefab == null || Player.m_localPlayer == null) {
                if (hoveredLink != null) {
                    hoveredLink = null;
                    UITooltip.HideTooltip();
                }
                return;
            }
            tooltipTimer -= Time.unscaledDeltaTime;
            if (id == hoveredLink && tooltipTimer > 0f) { return; }
            tooltipTimer = TooltipRefreshSeconds;
            tooltip.Set(ChatItems.DecoratedName(target.Item, target.Label), target.Item.GetTooltip());
            if (id != hoveredLink) {
                hoveredLink = id;
                tooltip.OnHoverStart(over.gameObject);
            }
        }

        // ---------------------------------------------------------------- moving and sizing

        private void AddDrag(string path, bool resize) {
            Transform target = UiBind.Find(transform, path);
            if (target == null) { return; }
            ChatDrag drag = target.gameObject.AddComponent<ChatDrag>();
            drag.Panel = this;
            drag.Resize = resize;
        }

        internal void DragBy(Vector2 delta, bool resize) {
            // The panel's own canvas is nested; the GUI scale is on the root one.
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null) { canvas = canvas.rootCanvas; }
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            delta /= scale;
            if (resize) {
                // The bottom-right corner: dragging down grows the panel downwards.
                Vector2 size = rect.sizeDelta + new Vector2(delta.x, -delta.y);
                Vector2 clamped = new Vector2(Mathf.Clamp(size.x, MinSize.x, MaxSize.x), Mathf.Clamp(size.y, MinSize.y, MaxSize.y));
                rect.anchoredPosition += new Vector2(0f, rect.sizeDelta.y - clamped.y);
                rect.sizeDelta = clamped;
            } else {
                rect.anchoredPosition += delta;
            }
        }

        internal void SaveRect() {
            Vector2 p = rect.anchoredPosition;
            Vector2 s = rect.sizeDelta;
            ClansConfig.ChatPanelRect.Value = string.Join(",", new[] { p.x, p.y, s.x, s.y }.Select(v => Mathf.Round(v).ToString(CultureInfo.InvariantCulture)));
        }

        // Also after a drag (SaveRect changes the setting) and on edits from a config manager. Kept on screen,
        // as a smaller resolution or larger GUI scale can leave a saved spot past the edge.
        private void ApplySavedRect() {
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = DefaultPosition;
            rect.sizeDelta = DefaultSize;
            if (TryParseRect(ClansConfig.ChatPanelRect.Value, out float[] values)) {
                rect.anchoredPosition = new Vector2(values[0], values[1]);
                rect.sizeDelta = new Vector2(Mathf.Clamp(values[2], MinSize.x, MaxSize.x), Mathf.Clamp(values[3], MinSize.y, MaxSize.y));
            }
            HudLayout.Clamp(rect, rect.sizeDelta);
        }

        private static bool TryParseRect(string value, out float[] values) {
            values = new float[4];
            string[] parts = (value ?? "").Split(',');
            if (parts.Length != 4) { return false; }
            for (int i = 0; i < 4; i++) {
                if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) == false) { return false; }
            }
            return true;
        }

        private void OnRectChanged(object sender, EventArgs e) => ApplySavedRect();
    }

    // Dragging the panel's header moves it; dragging its corner resizes it.
    internal sealed class ChatDrag : MonoBehaviour, IDragHandler, IEndDragHandler {
        internal ChatPanel Panel;
        internal bool Resize;

        public void OnDrag(PointerEventData eventData) => Panel?.DragBy(eventData.delta, Resize);

        public void OnEndDrag(PointerEventData eventData) => Panel?.SaveRect();
    }
}
