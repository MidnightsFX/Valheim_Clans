using Clans.Client;
using Clans.Core;
using Clans.Gameplay;
using Clans.Net;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI.Pages {
    // The clan's level: what the next one needs (experience, treasury coins and the resources members hand in) and
    // the upgrade itself; what every level gives and costs; and the guild feasts members can set out.
    internal sealed class LevelPage : ClanPage {
        internal override string Id => "level";
        internal override string Prefab => "Clans_Page_Level";
        internal override bool AvailableNow() => ClanClient.InClan;

        private const string Met = "#9BE07A";
        private const string Unmet = "#FF8A6E";

        private TemplateList resources;
        private TemplateList levels;
        private TemplateList feasts;

        protected override void Bind() {
            resources = new TemplateList(UiBind.Find(T, "Resources"));
            levels = new TemplateList(UiBind.Find(T, "Levels"));
            feasts = new TemplateList(UiBind.Find(T, "Feasts"));
            UiBind.OnClick(T, "Upgrade", Upgrade);
        }

        // What the player carries changes as they pick things up or put them away.
        internal override void Tick() => Refresh();

        internal override void Refresh() {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) { return; }
            LevelsSettings settings = ClansSettings.Levels;
            int level = Mathf.Clamp(clan.Level, 1, Mathf.Max(1, settings.MaxLevel));
            ClanLevelDef current = settings.Get(level);
            ClanLevelDef next = level < settings.MaxLevel ? settings.Get(level + 1) : null;

            UiBind.SetText(T, "LevelHeader", UiBind.Loc("$clans_level_title", level));
            UiBind.SetText(T, "Summary", PerksLine(current));
            long floor = current.XpRequired;
            long ceiling = next != null ? next.XpRequired : floor;
            UiBind.SetBar(UiBind.Get<Image>(T, "XpBar/Fill"), next == null || ceiling <= floor ? 1f : (clan.Xp - floor) / (float)(ceiling - floor));
            UiBind.SetText(T, "XpBar/Text", next == null ? UiBind.Loc("$clans_header_max_level") : UiBind.Loc("$clans_level_xp", clan.Xp, ceiling));

            FillNext(clan, level, next);
            FillLevels(level);
            FillFeasts(level);
        }

        // ---------------------------------------------------------------- the next level

        private void FillNext(ClanDetailInfo clan, int level, ClanLevelDef next) {
            if (next == null) {
                UiBind.SetText(T, "ResourcesHeader", UiBind.Loc("$clans_level_top"));
                resources.Sync(0, null);
                UiBind.SetText(T, "Requirements", "");
                UiBind.SetActive(T, "Upgrade", false);
                UiBind.SetText(T, "UpgradeNote", UiBind.Loc("$clans_level_top_note"));
                return;
            }
            UiBind.SetText(T, "ResourcesHeader", UiBind.Loc("$clans_level_next", level + 1));
            List<KeyValuePair<string, int>> needs = next.Resources.ToList();
            resources.Sync(needs.Count, (item, i) => {
                string prefab = needs[i].Key;
                int needed = needs[i].Value;
                int given = Math.Min(needed, clan.Contributed(prefab));
                int carried = ClientActions.Carried(prefab);
                int most = Math.Min(carried, needed - given);
                SetIcon(item.transform, ClientActions.ItemPrefab(prefab));
                UiBind.SetText(item.transform, "Texts/Name", ClientActions.ItemName(prefab));
                UiBind.SetText(item.transform, "Texts/Count", given >= needed
                    ? Colour(Met, UiBind.Loc("$clans_level_item_done", given, needed))
                    : UiBind.Loc("$clans_level_item_count", given, needed, carried));
                Button give = UiBind.Get<Button>(item.transform, "Give");
                UiBind.SetInteractable(give, most > 0);
                UiBind.OnClick(give, () => Give(prefab, most));
            });

            bool xpOk = clan.Xp >= next.XpRequired;
            bool itemsOk = next.Resources.All(r => clan.Contributed(r.Key) >= r.Value);
            bool coinsOk = clan.Treasury >= next.TreasuryCost;
            string xpLine = Colour(xpOk ? Met : Unmet, UiBind.Loc("$clans_level_req_xp", clan.Xp, next.XpRequired));
            string coinLine = next.TreasuryCost > 0 ? Colour(coinsOk ? Met : Unmet, UiBind.Loc("$clans_level_req_coins", clan.Treasury, next.TreasuryCost)) : "";
            UiBind.SetText(T, "Requirements", coinLine.Length > 0 ? xpLine + "\n" + coinLine : xpLine);

            bool allowed = ClanClient.HasPermission(ClanPermission.UpgradeClan);
            bool ready = xpOk && itemsOk && coinsOk;
            UiBind.SetActive(T, "Upgrade", true);
            UiBind.SetInteractable(UiBind.Get<Button>(T, "Upgrade"), ready && allowed);
            string note = xpOk == false ? UiBind.Loc("$clans_level_need_xp", next.XpRequired - clan.Xp)
                : itemsOk == false ? UiBind.Loc("$clans_level_need_items")
                : coinsOk == false ? UiBind.Loc("$clans_level_need_coins", next.TreasuryCost - clan.Treasury)
                : allowed == false ? UiBind.Loc("$clans_level_need_permission")
                : UiBind.Loc("$clans_level_ready", level + 1);
            UiBind.SetText(T, "UpgradeNote", note);
        }

        private static void Give(string prefab, int most) {
            if (most <= 0) { return; }
            string name = ClientActions.ItemName(prefab);
            ConfirmDialog.Prompt(ClansWindow.Root, "$clans_level_give", UiBind.Loc("$clans_level_give_body", name), most.ToString(), 6, true, text => {
                if (UiFormat.TryParseCoins(text, out int amount) == false) { return; }
                amount = Math.Min(amount, most);
                ClientActions.ContributeLevel(prefab, amount, result => {
                    if (result.Ok) { ClansWindow.Status(UiBind.Loc("$clans_level_given", amount, name)); } else { ClansWindow.ReportFailure(result); }
                });
            });
        }

        private static void Upgrade() {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null || clan.Level >= ClansSettings.Levels.MaxLevel) { return; }
            int level = clan.Level + 1;
            ClanLevelDef next = ClansSettings.Levels.Get(level);
            ConfirmDialog.Ask(ClansWindow.Root, "$clans_level_upgrade", UiBind.Loc("$clans_level_upgrade_body", level, next.TreasuryCost), () =>
                ClientActions.UpgradeLevel(result => {
                    if (result.Ok) { ClansWindow.Status(UiBind.Loc("$clans_level_upgraded", level)); } else { ClansWindow.ReportFailure(result); }
                }));
        }

        // ---------------------------------------------------------------- every level

        private void FillLevels(int level) {
            LevelsSettings settings = ClansSettings.Levels;
            levels.Sync(settings.MaxLevel, (item, i) => {
                int n = i + 1;
                ClanLevelDef def = settings.Get(n);
                string name = UiBind.Loc("$clans_level_title", n);
                if (n == level) { name += " " + UiBind.Loc("$clans_level_current"); }
                if (n == level + 1) { name += " " + UiBind.Loc("$clans_level_next_mark"); }
                UiBind.SetText(item.transform, "Name", name);
                List<string> lines = new List<string> { PerksLine(def) };
                string unlocks = Unlocks(n);
                if (unlocks.Length > 0) { lines.Add(UiBind.Loc("$clans_level_unlocks", unlocks)); }
                if (n > level) { lines.Add(Colour("#B4B4B4", Needs(def))); }
                UiBind.SetText(item.transform, "Perks", string.Join("\n", lines));
            });
        }

        private static string PerksLine(ClanLevelDef def) {
            int percent = Mathf.RoundToInt(def.XpShareFraction * 100f);
            bool anyDistance = def.XpShareAnyDistance && ClansConfig.XpShareClanmatesAnyDistance.Value;
            if (ClansConfig.XpShareEnabled.Value == false || ClansConfig.EnableParties.Value == false) {
                return UiBind.Loc("$clans_level_members", def.MaxMembers);
            }
            return anyDistance
                ? UiBind.Loc("$clans_level_perks_any", def.MaxMembers, percent)
                : UiBind.Loc("$clans_level_perks", def.MaxMembers, percent, Mathf.RoundToInt(def.XpShareRange));
        }

        // The storage tabs and feasts that open up at exactly this level.
        private static string Unlocks(int level) {
            List<string> parts = new List<string>();
            if (ClansConfig.EnableStorage.Value) {
                StorageSettings storage = ClansSettings.Storage;
                for (int tab = 0; tab < storage.SharedTabs.Count; tab++) {
                    if (Math.Max(1, storage.SharedTabs[tab].MinClanLevel) == level) { parts.Add(UiBind.Loc("$clans_level_unlock_tab", tab + 1)); }
                }
                if (storage.PersonalTab != null && Math.Max(1, storage.PersonalTab.MinClanLevel) == level) { parts.Add(UiBind.Loc("$clans_level_unlock_personal")); }
            }
            foreach (GuildFeastDef feast in OfferedFeasts()) {
                if (feast.MinClanLevel == level) { parts.Add(UiBind.Loc(ClansPrefabs.GuildFeastOf(feast.Biome).Name)); }
            }
            return string.Join(", ", parts);
        }

        private static string Needs(ClanLevelDef def) {
            string items = ItemList(def.Resources, false);
            return items.Length > 0
                ? UiBind.Loc("$clans_level_needs", def.XpRequired, def.TreasuryCost, items)
                : UiBind.Loc("$clans_level_needs_noitems", def.XpRequired, def.TreasuryCost);
        }

        // ---------------------------------------------------------------- feasts

        // The feasts this server offers and this game has.
        private static IEnumerable<GuildFeastDef> OfferedFeasts() {
            if (ClansConfig.EnableFeasts.Value == false) { return Enumerable.Empty<GuildFeastDef>(); }
            return ClansSettings.Feasts.Feasts
                .Where(f => ClansPrefabs.GuildFeastOf(f.Biome) != null && ClientActions.ItemPrefab(ClansPrefabs.GuildFeastOf(f.Biome).Prefab) != null)
                .OrderBy(f => f.MinClanLevel);
        }

        private void FillFeasts(int level) {
            FeastsSettings settings = ClansSettings.Feasts;
            List<GuildFeastDef> offered = OfferedFeasts().ToList();
            UiBind.SetText(T, "FeastNote", ClansConfig.EnableFeasts.Value
                ? UiBind.Loc("$clans_feast_note", settings.LifetimeMinutes, settings.FoodMinutes)
                : UiBind.Loc("$clans_err_feasts_off"));
            feasts.Sync(offered.Count, (item, i) => {
                GuildFeastDef def = offered[i];
                ClansPrefabs.GuildFeastVariant variant = ClansPrefabs.GuildFeastOf(def.Biome);
                ItemDrop drop = ClientActions.ItemPrefab(variant.Prefab);
                ItemDrop.ItemData.SharedData food = drop.m_itemData.m_shared;
                bool unlocked = level >= def.MinClanLevel;
                SetIcon(item.transform, drop);
                UiBind.SetText(item.transform, "Texts/Name", UiBind.Loc(variant.Name));
                UiBind.SetText(item.transform, "Texts/Info", food.m_foodEitr > 0f
                    ? UiBind.Loc("$clans_feast_info_eitr", Mathf.RoundToInt(food.m_food), Mathf.RoundToInt(food.m_foodStamina), Mathf.RoundToInt(food.m_foodEitr))
                    : UiBind.Loc("$clans_feast_info", Mathf.RoundToInt(food.m_food), Mathf.RoundToInt(food.m_foodStamina)));
                string cost = UiBind.Loc("$clans_feast_cost", ItemList(def.Cost, true));
                UiBind.SetText(item.transform, "Texts/Cost", unlocked ? cost : Colour(Unmet, UiBind.Loc("$clans_feast_locked", def.MinClanLevel)) + " " + cost);
                Button buy = UiBind.Get<Button>(item.transform, "Buttons/Buy");
                UiBind.SetInteractable(buy, unlocked && ClientActions.CarriesAll(def.Cost));
                UiBind.OnClick(buy, () => Buy(def, false));
                // The clan's own stock: whether storage holds the food only the server knows, so only the coins are
                // checked here.
                Button clanBuy = UiBind.Get<Button>(item.transform, "Buttons/ClanBuy");
                bool mayClanPay = ClanClient.HasPermission(ClanPermission.ClanFeasts);
                UiBind.SetActive(item.transform, "Buttons/ClanBuy", mayClanPay);
                if (mayClanPay) {
                    def.Cost.TryGetValue(Server.LevelService.CoinsItem, out int coins);
                    bool storageUsable = ClansConfig.EnableStorage.Value || def.Cost.Keys.All(k => k == Server.LevelService.CoinsItem);
                    UiBind.SetInteractable(clanBuy, unlocked && storageUsable && ClanClient.MyClan.Treasury >= coins);
                    UiBind.OnClick(clanBuy, () => Buy(def, true));
                }
            });
        }

        private static void Buy(GuildFeastDef def, bool clanPays) {
            ClansPrefabs.GuildFeastVariant variant = ClansPrefabs.GuildFeastOf(def.Biome);
            if (variant == null) { return; }
            string name = UiBind.Loc(variant.Name);
            int minutes = ClansSettings.Feasts.LifetimeMinutes;
            string body = clanPays
                ? UiBind.Loc("$clans_feast_clan_buy_body", name, ClanPayment(def.Cost), minutes)
                : UiBind.Loc("$clans_feast_buy_body", name, ItemList(def.Cost, false), minutes);
            ConfirmDialog.Ask(ClansWindow.Root, clanPays ? "$clans_feast_clan_buy" : "$clans_feast_buy", body, () =>
                ClientActions.BuyFeast(def, clanPays, result => {
                    if (result.Ok == false) { ClansWindow.ReportFailure(result); return; }
                    // Out of the way, so the player sees it standing in front of them.
                    ClansWindow.Close();
                    if (InventoryGui.instance != null) { InventoryGui.instance.Hide(); }
                    ClansWindow.Status(UiBind.Loc("$clans_feast_set_out", name));
                }));
        }

        // ---------------------------------------------------------------- helpers

        // "10 Cooked meat, 20 Carrots from clan storage and 500 coins from the treasury".
        private static string ClanPayment(Dictionary<string, int> cost) {
            string coin = Server.LevelService.CoinsItem;
            Dictionary<string, int> stored = cost.Where(c => c.Key != coin).ToDictionary(c => c.Key, c => c.Value);
            cost.TryGetValue(coin, out int coins);
            if (stored.Count > 0 && coins > 0) { return UiBind.Loc("$clans_feast_from_both", ItemList(stored, false), coins); }
            return stored.Count > 0 ? UiBind.Loc("$clans_feast_from_storage", ItemList(stored, false)) : UiBind.Loc("$clans_feast_from_treasury", coins);
        }

        // "200 Wood, 150 Stone"; with markShort, the items the player does not carry enough of are in red.
        private static string ItemList(Dictionary<string, int> items, bool markShort) {
            return string.Join(", ", items.Select(item => {
                string text = $"{item.Value} {ClientActions.ItemName(item.Key)}";
                return markShort && ClientActions.Carried(item.Key) < item.Value ? Colour(Unmet, text) : text;
            }));
        }

        private static void SetIcon(Transform item, ItemDrop drop) {
            Image icon = UiBind.Get<Image>(item, "Icon");
            if (icon == null) { return; }
            Sprite sprite = drop != null ? drop.m_itemData.GetIcon() : null;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        private static string Colour(string colour, string text) => $"<color={colour}>{text}</color>";
    }
}
