using Clans.Client;
using Clans.Core;
using Clans.Net;
using Clans.Server;
using Clans.Settings;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI.Pages {
    // The clan's storage tabs: open the ones bought, buy the next one from the treasury.
    internal sealed class StoragePage : ClanPage {
        internal override string Id => "storage";
        internal override string Prefab => "Clans_Page_Storage";
        internal override bool AvailableNow() => ClanClient.InClan;

        private const int NamesShown = 6;

        private TemplateList list;

        protected override void Bind() {
            list = new TemplateList(UiBind.Find(T, "List"));
        }

        internal override void Refresh() {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) { return; }
            StorageSettings settings = ClansSettings.Storage;
            bool canTake = ClanClient.HasPermission(ClanPermission.StorageWithdraw);
            bool canPut = ClanClient.HasPermission(ClanPermission.StorageDeposit);
            bool canBuy = ClanClient.HasPermission(ClanPermission.UpgradeStorage);
            UiBind.SetText(T, "Treasury", UiBind.Loc("$clans_overview_treasury", clan.Treasury));
            string access = canTake && canPut ? "$clans_storage_access_full"
                : canPut ? "$clans_storage_access_deposit"
                : canTake ? "$clans_storage_access_withdraw"
                : "$clans_storage_access_none";
            UiBind.SetText(T, "Access", UiBind.Loc(access));
            UiBind.SetText(T, "Note", AcceptsNote(settings));

            // One row per shared tab the server offers (or the clan already has), then the personal tab.
            int sharedRows = Mathf.Max(clan.SharedTabs, settings.SharedTabs.Count);
            int rows = sharedRows + (settings.PersonalTab != null || clan.PersonalUnlocked ? 1 : 0);
            list.Sync(rows, (item, i) => {
                Transform t = item.transform;
                if (i < sharedRows) {
                    FillShared(t, clan, settings, i, canBuy, canTake || canPut);
                } else {
                    FillPersonal(t, clan, settings, canBuy);
                }
            });
        }

        private void FillShared(Transform t, ClanDetailInfo clan, StorageSettings settings, int index, bool canBuy, bool canOpen) {
            bool bought = index < clan.SharedTabs;
            bool next = index == clan.SharedTabs;
            UiBind.SetText(t, "Texts/Title", UiBind.Loc("$clans_storage_tab_name", index + 1));
            StorageTabDef def = index < settings.SharedTabs.Count ? settings.SharedTabs[index] : null;
            string info;
            if (bought) {
                info = UiBind.Loc("$clans_storage_tab_open_info", StorageService.Width * StorageService.Height);
            } else if (def == null) {
                info = "";
            } else if (next) {
                info = UiBind.Loc("$clans_storage_tab_price", def.Cost, def.MinClanLevel);
            } else {
                info = UiBind.Loc("$clans_storage_tab_later", index);
            }
            UiBind.SetText(t, "Texts/Info", info);
            UiBind.SetActive(t, "Open", bought && canOpen);
            UiBind.SetActive(t, "Buy", next && def != null && canBuy);
            if (next && def != null) {
                UiBind.SetInteractable(UiBind.Get<Button>(t, "Buy"), clan.Level >= def.MinClanLevel && clan.Treasury >= def.Cost);
            }
            int tab = index;
            UiBind.OnClick(t, "Open", () => ClanStorageClient.Open(StorageService.TabKind.Shared, tab, ClansWindow.ReportFailure));
            UiBind.OnClick(t, "Buy", () => Buy(StorageService.TabKind.Shared, def));
        }

        private void FillPersonal(Transform t, ClanDetailInfo clan, StorageSettings settings, bool canBuy) {
            StorageTabDef def = settings.PersonalTab;
            UiBind.SetText(t, "Texts/Title", UiBind.Loc("$clans_storage_personal"));
            UiBind.SetText(t, "Texts/Info", clan.PersonalUnlocked
                ? UiBind.Loc("$clans_storage_personal_info")
                : (def != null ? UiBind.Loc("$clans_storage_personal_price", def.Cost, def.MinClanLevel) : ""));
            UiBind.SetActive(t, "Open", clan.PersonalUnlocked);
            UiBind.SetActive(t, "Buy", clan.PersonalUnlocked == false && def != null && canBuy);
            if (clan.PersonalUnlocked == false && def != null) {
                UiBind.SetInteractable(UiBind.Get<Button>(t, "Buy"), clan.Level >= def.MinClanLevel && clan.Treasury >= def.Cost);
            }
            UiBind.OnClick(t, "Open", () => ClanStorageClient.Open(StorageService.TabKind.Personal, 0, ClansWindow.ReportFailure));
            UiBind.OnClick(t, "Buy", () => Buy(StorageService.TabKind.Personal, def));
        }

        // What may be put in, when the server limits it. The note has room for one line.
        private static string AcceptsNote(StorageSettings settings) {
            if (settings.RestrictItems == false) { return UiBind.Loc("$clans_storage_note"); }
            List<string> names = settings.AcceptedItems().Select(ItemName).ToList();
            if (names.Count == 0) { return UiBind.Loc("$clans_storage_accepts_nothing"); }
            string shown = string.Join(", ", names.Take(NamesShown));
            return names.Count > NamesShown
                ? UiBind.Loc("$clans_storage_accepts_more", shown, names.Count - NamesShown)
                : UiBind.Loc("$clans_storage_accepts", shown);
        }

        private static string ItemName(string prefab) {
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : prefab;
        }

        private static void Buy(StorageService.TabKind kind, StorageTabDef def) {
            if (def == null) { return; }
            ConfirmDialog.Ask(ClansWindow.Root, "$clans_storage_buy", UiBind.Loc("$clans_storage_buy_body", def.Cost), () =>
                ClanRequests.Send(Op.BuyStorageUpgrade, pkg => pkg.Write((byte)kind), result => {
                    if (result.Ok) { ClansWindow.Status(UiBind.Loc("$clans_storage_bought")); } else { ClansWindow.ReportFailure(result); }
                }));
        }
    }
}
