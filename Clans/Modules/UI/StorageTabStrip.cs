using Clans.Client;
using Clans.Net;
using Clans.Server;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI {
    // Tab buttons along the top of vanilla's container panel while a clan storage tab is open in it, to
    // switch between the clan's tabs (and the personal one) without going back to the Clans window.
    internal sealed class StorageTabStrip : MonoBehaviour {
        private InventoryGui gui;
        private TemplateList tabs;
        private bool shown;
        private int builtFor = -1;

        internal static void Create(InventoryGui gui) {
            GameObject prefab = ClansAssets.Prefab("Clans_INV_StorageTabs");
            if (prefab == null || gui == null || gui.m_container == null) { return; }
            GameObject go = Instantiate(prefab, gui.m_container, false);
            go.name = "Clans_StorageTabs";
            ClansStyle.Apply(go);
            StorageTabStrip strip = go.AddComponent<StorageTabStrip>();
            strip.gui = gui;
            strip.tabs = new TemplateList(go.transform, "Tab");
            ClanClient.DetailChanged += strip.Invalidate;
            ClanStorageClient.Changed += strip.Invalidate;
            strip.SetShown(false);
        }

        private void OnDestroy() {
            ClanClient.DetailChanged -= Invalidate;
            ClanStorageClient.Changed -= Invalidate;
        }

        private void Invalidate() => builtFor = -1;

        private void LateUpdate() {
            bool show = gui != null && ClanStorageClient.IsOurs(gui.m_currentContainer) && ClanClient.MyClan != null;
            if (show != shown) { SetShown(show); }
            if (show == false) { return; }
            ClanDetailInfo clan = ClanClient.MyClan;
            int stamp = clan.SharedTabs * 2 + (clan.PersonalUnlocked ? 1 : 0) + ClanStorageClient.OpenIndex * 100 + (int)ClanStorageClient.OpenKind * 10000;
            if (stamp != builtFor) {
                builtFor = stamp;
                Rebuild(clan);
            }
        }

        private void SetShown(bool show) {
            shown = show;
            for (int i = 0; i < transform.childCount; i++) {
                Transform child = transform.GetChild(i);
                if (child.name == "Tab") { continue; }
                child.gameObject.SetActive(show);
            }
            if (show) { builtFor = -1; }
        }

        private void Rebuild(ClanDetailInfo clan) {
            int count = clan.SharedTabs + (clan.PersonalUnlocked ? 1 : 0);
            tabs.Sync(count, (item, i) => {
                bool personal = i >= clan.SharedTabs;
                StorageService.TabKind kind = personal ? StorageService.TabKind.Personal : StorageService.TabKind.Shared;
                int index = personal ? 0 : i;
                UiBind.SetButtonText(item.GetComponent<Button>(), personal ? UiBind.Loc("$clans_storage_personal_short") : (i + 1).ToString());
                bool current = ClanStorageClient.OpenKind == kind && ClanStorageClient.OpenIndex == index;
                UiBind.SetTabSelected(item.GetComponent<Button>(), current);
                UiBind.OnClick(item.GetComponent<Button>(), () => {
                    if (current) { return; }
                    ClanStorageClient.Open(kind, index, Report);
                });
            });
        }

        private static void Report(NetResult result) {
            if (result.Ok == false) { ClanNotices.ShowError(result.Message()); }
        }
    }
}
