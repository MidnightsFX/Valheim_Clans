using Clans.Client;
using Clans.Net;
using Clans.Settings;
using Clans.UI.Pages;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Clans.UI {
    // The Clans window: header, tabs and the page area, living inside the inventory screen like vanilla's
    // Trophies panel. Built once per inventory screen from Clans_UI_Window and kept, hidden, between opens.
    //
    // While it is open it holds Jotunn's input block, so typing in its fields neither moves the player nor
    // fires inventory hotkeys; that also stops the inventory reacting to Escape and Tab, so the window
    // handles those itself.
    internal sealed class ClansWindow : MonoBehaviour {
        private static ClansWindow instance;

        // A tab and the pages it can show. The first page is the tab's own; the others are reached from it
        // (founding and joining sit under the Clan tab) and keep the tab highlighted.
        private sealed class TabDef {
            internal string Id;
            internal string Title;
            internal GameObject Button;
        }

        private Transform panel;
        private Transform pagesRoot;
        private TemplateList tabButtons;
        private readonly List<TabDef> tabs = new List<TabDef>();
        private readonly Dictionary<string, ClanPage> pages = new Dictionary<string, ClanPage>();
        private ClanPage current;
        private string currentTab;
        private bool lastInClan;
        private bool holdingInput;
        private TMP_Text status;
        private float statusUntil;

        internal static bool IsOpen => instance != null && instance.gameObject.activeSelf;

        // A field in the window has the keyboard.
        internal static bool IsTyping => IsOpen && EditingText();

        // The window's transform, for dialogs that should sit on top of it.
        internal static Transform Root => instance != null ? instance.transform : null;

        internal static void Toggle() {
            if (IsOpen) { Close(); } else { Open(); }
        }

        internal static void Open(string page = null) {
            ClansWindow window = Ensure();
            if (window == null) {
                ClanNotices.ShowError(UiBind.Loc("$clans_ui_missing"));
                return;
            }
            if (InventoryGui.instance != null && InventoryGui.IsVisible() == false) {
                InventoryGui.instance.Show(null);
            }
            window.gameObject.SetActive(true);
            window.transform.SetAsLastSibling();
            ClientActions.RequestDetail();
            window.ShowDefault(page);
        }

        internal static void Close() {
            if (instance == null) { return; }
            ConfirmDialog.Close();
            instance.gameObject.SetActive(false);
        }

        // The tabs are rebuilt first: they follow clan membership, which founding or joining has just changed.
        internal static void ShowPage(string id) {
            if (instance != null) { instance.ShowDefault(id); }
        }

        // A short message in the window's status line (and on the HUD when the window is closed).
        internal static void Status(string text, bool error = false) {
            if (instance == null || instance.status == null || IsOpen == false) {
                if (error) { ClanNotices.ShowError(text); } else { ClanNotices.ShowInfo(text); }
                return;
            }
            instance.status.text = text ?? "";
            instance.status.color = error ? new Color(1f, 0.45f, 0.35f) : GUIManager.Instance.ValheimOrange;
            instance.statusUntil = Time.unscaledTime + 6f;
        }

        // Reports a request's failure in the status line; success is left to the caller.
        internal static void ReportFailure(NetResult result) {
            if (result != null && result.Ok == false) { Status(result.Message(), true); }
        }

        private static ClansWindow Ensure() {
            if (instance != null) { return instance; }
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || gui.m_trophiesPanel == null) { return null; }
            GameObject prefab = ClansAssets.Prefab("Clans_UI_Window");
            if (prefab == null) { return null; }

            GameObject go = Instantiate(prefab, gui.m_trophiesPanel.transform.parent, false);
            go.name = "Clans_Window";
            go.SetActive(false);
            ClansStyle.Apply(go);
            instance = go.AddComponent<ClansWindow>();
            instance.Build();
            return instance;
        }

        private void Build() {
            panel = UiBind.Find(transform, "Panel");
            pagesRoot = UiBind.Find(transform, "Panel/Pages");
            status = UiBind.Get<TMP_Text>(transform, "Panel/Status");
            if (status != null) { status.text = ""; }
            tabButtons = new TemplateList(UiBind.Find(transform, "Panel/Tabs"), "Tab");
            UiBind.OnClick(transform, "Panel/Close", Close);
            // Like vanilla's Trophies and Skills panels: a click outside the panel closes it.
            UiBind.OnClick(transform, "Backdrop", Close);
            // The inventory hides the PvP toggle for the window to show: without its slot it still goes somewhere.
            Transform pvpSlot = UiBind.Find(transform, "Panel/Header/PvpSlot");
            if (pvpSlot == null) { Logger.LogWarning("The Clans window has no PvP slot; the PvP toggle is put in its header."); }
            PvpToggleMover.MoveInto(pvpSlot ?? UiBind.Find(transform, "Panel/Header") ?? transform);
            // Beside it: moving the Clans HUD (tracker, party frame, chat).
            Button layout = UiBind.OnClick(transform, "Panel/Header/LayoutButton", HudLayout.BeginEditing);
            if (layout != null) { ClansStyle.Tooltip(layout.gameObject, "$clans_layout_tooltip"); }

            Register(new NoClanPage());
            Register(new CreateClanPage());
            Register(new JoinClanPage());
            Register(new OverviewPage());
            Register(new LevelPage());
            Register(new MembersPage());
            Register(new PartyPage());
            Register(new WarPage());
            Register(new StoragePage());
            Register(new ActivitiesPage());

            ClanClient.DirectoryChanged += OnDataChanged;
            ClanClient.RosterChanged += OnDataChanged;
            ClanClient.AccountChanged += OnAccountChanged;
            ClanClient.DetailChanged += OnDataChanged;
            ClansSettings.Changed += OnDataChanged;
            PartyClient.Changed += OnDataChanged;
            lastInClan = ClanClient.InClan;
        }

        private void OnDestroy() {
            ClanClient.DirectoryChanged -= OnDataChanged;
            ClanClient.RosterChanged -= OnDataChanged;
            ClanClient.AccountChanged -= OnAccountChanged;
            ClanClient.DetailChanged -= OnDataChanged;
            ClansSettings.Changed -= OnDataChanged;
            PartyClient.Changed -= OnDataChanged;
            foreach (ClanPage page in pages.Values) { page.Destroy(); }
            ReleaseInput();
            if (instance == this) { instance = null; }
        }

        private void Register(ClanPage page) {
            GameObject prefab = ClansAssets.Prefab(page.Prefab);
            if (prefab == null) {
                Logger.LogWarning($"Clans UI: the bundle has no {page.Prefab}; the {page.Id} page is not available.");
                return;
            }
            GameObject root = Instantiate(prefab, pagesRoot, false);
            root.SetActive(false);
            ClansStyle.Apply(root);
            page.Attach(root);
            pages[page.Id] = page;
        }

        // ---------------------------------------------------------------- tabs and pages

        private void RebuildTabs() {
            tabs.Clear();
            if (ClanClient.InClan) {
                AddTab("overview", "$clans_tab_overview");
                AddTab("level", "$clans_tab_level");
                AddTab("members", "$clans_tab_members");
                if (ClansConfig.EnableWars.Value) { AddTab("war", "$clans_tab_war"); }
                if (ClansConfig.EnableActivities.Value) { AddTab("activities", "$clans_tab_activities"); }
                if (ClansConfig.EnableStorage.Value) { AddTab("storage", "$clans_tab_storage"); }
            } else {
                AddTab("noclan", "$clans_tab_clan");
            }
            AddTab("party", "$clans_tab_party");
            tabButtons.Sync(tabs.Count, (go, i) => {
                TabDef tab = tabs[i];
                tab.Button = go;
                UiBind.SetButtonText(go.GetComponent<Button>(), UiBind.Loc(tab.Title));
                UiBind.OnClick(go.GetComponent<Button>(), () => Show(tab.Id));
            });
        }

        private void AddTab(string id, string title) {
            if (pages.ContainsKey(id)) { tabs.Add(new TabDef { Id = id, Title = title }); }
        }

        private void ShowDefault(string requested) {
            RebuildTabs();
            if (requested != null && pages.ContainsKey(requested)) { Show(requested); return; }
            if (current != null && current.Root.activeSelf && current.AvailableNow()) { Show(current.Id); return; }
            Show(ClanClient.InClan ? "overview" : "noclan");
        }

        private void Show(string id) {
            if (pages.TryGetValue(id, out ClanPage page) == false) { return; }
            ConfirmDialog.Close();
            if (current != null && current != page) {
                current.OnHide();
                current.Root.SetActive(false);
            }
            current = page;
            currentTab = page.TabId;
            page.Root.SetActive(true);
            page.OnShow();
            foreach (TabDef tab in tabs) {
                UiBind.SetTabSelected(tab.Button != null ? tab.Button.GetComponent<Button>() : null, tab.Id == currentTab);
            }
            RefreshHeader();
        }

        private void OnAccountChanged() {
            // Joining or leaving a clan swaps the whole set of tabs.
            if (ClanClient.InClan != lastInClan) {
                lastInClan = ClanClient.InClan;
                if (IsOpen) { ShowDefault(null); }
                return;
            }
            OnDataChanged();
        }

        private void OnDataChanged() {
            if (IsOpen == false) { return; }
            RefreshHeader();
            if (current != null && current.Root.activeSelf) { current.Refresh(); }
        }

        private void RefreshHeader() {
            Transform header = UiBind.Find(transform, "Panel/Header");
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) {
                EmblemView.Apply(UiBind.Find(header, "Emblem"), ClansSettings.Emblems.Default);
                UiBind.SetText(header, "ClanName", Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "");
                UiBind.SetText(header, "ClanSub", UiBind.Loc("$clans_header_no_clan"));
                UiBind.SetActive(header, "XpBar", false);
                return;
            }
            EmblemView.Apply(UiBind.Find(header, "Emblem"), clan.Emblem);
            UiBind.SetText(header, "ClanName", clan.Name);
            int max = ClansSettings.Levels.Get(clan.Level).MaxMembers;
            UiBind.SetText(header, "ClanSub", UiBind.Loc("$clans_header_sub", clan.Level, clan.Members.Count, max));
            UiBind.SetActive(header, "XpBar", true);

            // Toward the next level's experience; a clan with enough is told to upgrade (the Level tab).
            LevelsSettings levels = ClansSettings.Levels;
            long floor = levels.Get(clan.Level).XpRequired;
            bool maxed = clan.Level >= levels.MaxLevel;
            long ceiling = maxed ? floor : levels.Get(clan.Level + 1).XpRequired;
            UiBind.SetBar(UiBind.Get<Image>(header, "XpBar/Fill"), maxed || ceiling <= floor ? 1f : (clan.Xp - floor) / (float)(ceiling - floor));
            string xpText = maxed ? UiBind.Loc("$clans_header_max_level")
                : clan.Xp >= ceiling ? UiBind.Loc("$clans_header_level_ready")
                : $"{Math.Max(0, clan.Xp - floor)} / {ceiling - floor}";
            UiBind.SetText(header, "XpBar/Text", xpText);
        }

        // ---------------------------------------------------------------- input

        private void OnEnable() {
            if (holdingInput == false) {
                holdingInput = true;
                GUIManager.BlockInput(true);
            }
        }

        private void OnDisable() {
            ReleaseInput();
            if (current != null) { current.OnHide(); }
        }

        private void ReleaseInput() {
            if (holdingInput == false) { return; }
            holdingInput = false;
            GUIManager.BlockInput(false);
        }

        private float pageTick;

        private void Update() {
            pageTick -= Time.unscaledDeltaTime;
            if (pageTick <= 0f) {
                pageTick = 1f;
                if (current != null && current.Root.activeSelf) { current.Tick(); }
            }
            if (status != null && statusUntil > 0f && Time.unscaledTime > statusUntil) {
                statusUntil = 0f;
                status.text = "";
            }
            if (EditingText()) { return; }

            if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB")) {
                if (ConfirmDialog.IsOpen) { ConfirmDialog.Close(); } else { Close(); }
                return;
            }
            // The inventory key closes everything, as it closes the inventory with Trophies open.
            if (ZInput.GetButtonDown("Inventory") || ZInput.GetButtonDown("JoyButtonY")) {
                Close();
                if (InventoryGui.instance != null) { InventoryGui.instance.Hide(); }
            }
        }

        // Escape while typing only leaves the field.
        private static bool EditingText() {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            TMP_InputField field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
            return field != null && field.isFocused;
        }

        // ---------------------------------------------------------------- inventory hooks

        [HarmonyLib.HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        private static class HidePatch {
            private static void Postfix() => Close();
        }
    }
}
