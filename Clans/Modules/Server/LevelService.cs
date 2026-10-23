using Clans.Core;
using Clans.Gameplay;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans.Server {
    // Clan levels and guild feasts. A level is bought: the clan needs the experience, its members hand in the
    // level's resources a batch at a time, and a rank allowed to upgrade the clan pays the treasury cost. Guild
    // feasts are paid for by the member who sets one out, from a clan level on.
    //
    // Handed-in items and feast costs are taken from the player's inventory by their client before it asks (as
    // treasury donations are); a refused request makes the client give them back.
    internal static class LevelService {
        // A member's hand-ins of one item within this long of each other make one log line.
        private const long ContributionLogMergeSeconds = 600;
        private const int MaxBatch = 100000;
        // A feast's coins come from the treasury when the clan pays.
        internal const string CoinsItem = "Coins";

        internal static void Register() {
            ClanNet.RegisterServer(Op.ContributeLevel, OnContribute);
            ClanNet.RegisterServer(Op.UpgradeLevel, OnUpgrade);
            ClanNet.RegisterServer(Op.BuyFeast, OnBuyFeast);
        }

        // ---------------------------------------------------------------- levels

        private static int Contributed(ClanRecord clan, string item) {
            return clan.LevelContributions.TryGetValue(item, out int amount) ? amount : 0;
        }

        // Answers with how many it took: never more than the level still needs, so the client gives the rest back.
        private static void OnContribute(ServerRequest request) {
            string item = request.Body.ReadString();
            int amount = request.Body.ReadInt();
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (amount <= 0 || amount > MaxBatch || string.IsNullOrEmpty(item)) { request.Fail("$clans_err_bad_request"); return; }
            ClanLevelDef next = ClanRules.NextLevel(clan);
            if (next == null) { request.Fail("$clans_err_max_level"); return; }
            if (next.Resources.TryGetValue(item, out int needed) == false) { request.Fail("$clans_err_level_item_unneeded"); return; }
            int have = Contributed(clan, item);
            int accepted = Math.Min(amount, needed - have);
            if (accepted <= 0) { request.Fail("$clans_err_level_item_done"); return; }

            clan.LevelContributions[item] = have + accepted;
            LogContribution(clan, request.Sender.CharacterName, ItemToken(item), accepted);
            Broadcaster.MarkDetail(clan.Id);
            ClanStore.MarkDirtySoon();
            request.Ok(pkg => pkg.Write(accepted));
        }

        private static void LogContribution(ClanRecord clan, string name, string itemToken, int amount) {
            const string token = "$clans_log_contributed";
            LogEntry last = clan.Log.Count > 0 ? clan.Log[clan.Log.Count - 1] : null;
            if (last != null && last.Token == token && last.Args.Count == 3 && last.Args[0] == name && last.Args[2] == itemToken
                && ClanStore.Now - last.Utc < ContributionLogMergeSeconds && int.TryParse(last.Args[1], out int before)) {
                last.Args[1] = (before + amount).ToString();
                last.Utc = ClanStore.Now;
                return;
            }
            ClanRules.Log(clan, token, name, amount.ToString(), itemToken);
        }

        private static void OnUpgrade(ServerRequest request) {
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.UpgradeClan) == false) { request.Fail("$clans_err_no_permission"); return; }
            ClanLevelDef next = ClanRules.NextLevel(clan);
            if (next == null) { request.Fail("$clans_err_max_level"); return; }
            if (clan.Xp < next.XpRequired) { request.Fail("$clans_err_level_xp", (next.XpRequired - clan.Xp).ToString()); return; }
            if (next.Resources.Any(r => Contributed(clan, r.Key) < r.Value)) { request.Fail("$clans_err_level_resources"); return; }
            if (clan.Treasury < next.TreasuryCost) { request.Fail("$clans_err_treasury_low", clan.Treasury.ToString()); return; }

            if (next.TreasuryCost > 0) { ClanRules.AddTreasury(clan, -next.TreasuryCost); }
            int level = ClanRules.Level(clan) + 1;
            clan.Level = level;
            clan.LevelContributions.Clear();
            ApiEvents.LevelChanged(clan, level - 1);
            ClanRules.Log(clan, "$clans_log_upgraded", request.Sender.CharacterName, level.ToString());
            ClanService.NotifyClan(clan, null, "$clans_notice_levelup", level.ToString());
            Logger.LogInfo($"{request.Sender.CharacterName} upgraded the clan '{clan.Name}' to level {level}.");
            Broadcaster.MarkDirectory();
            Broadcaster.MarkDetail(clan.Id);
            ClanStore.MarkDirtySoon();
            request.Ok();
        }

        // ---------------------------------------------------------------- feasts

        // Answers with how many minutes the feast stands; the client sets it out in front of the player.
        // clanPays: the clan pays instead of the member (a rank with ClanFeasts): coins from the treasury, everything
        // else from the shared storage tabs. Otherwise the client has already taken the cost from the member.
        private static void OnBuyFeast(ServerRequest request) {
            string biome = request.Body.ReadString();
            bool clanPays = request.Body.ReadBool();
            if (ClansConfig.EnableFeasts.Value == false) { request.Fail("$clans_err_feasts_off"); return; }
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            FeastsSettings settings = ClansSettings.Feasts;
            GuildFeastDef def = settings.Find(biome);
            ClansPrefabs.GuildFeastVariant variant = ClansPrefabs.GuildFeastOf(biome);
            if (def == null || variant == null || ZNetScene.instance == null || ZNetScene.instance.GetPrefab(variant.Prefab) == null) {
                request.Fail("$clans_err_no_such_feast");
                return;
            }
            if (ClanRules.Level(clan) < def.MinClanLevel) { request.Fail("$clans_err_level_too_low", def.MinClanLevel.ToString()); return; }

            if (clanPays) {
                if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.ClanFeasts) == false) { request.Fail("$clans_err_no_permission"); return; }
                def.Cost.TryGetValue(CoinsItem, out int coins);
                if (clan.Treasury < coins) { request.Fail("$clans_err_treasury_low", clan.Treasury.ToString()); return; }
                Dictionary<string, int> items = def.Cost.Where(c => c.Key != CoinsItem).ToDictionary(c => c.Key, c => c.Value);
                if (items.Count > 0) {
                    if (ClansConfig.EnableStorage.Value == false) { request.Fail("$clans_err_storage_off"); return; }
                    string error = StorageService.TakeFromShared(clan, items, out string openBy);
                    if (error != null) { request.Fail(error, openBy ?? ""); return; }
                }
                if (coins > 0) { ClanRules.AddTreasury(clan, -coins); }
                ClanRules.Log(clan, "$clans_log_feast_clan", request.Sender.CharacterName, variant.Name);
            } else {
                ClanRules.Log(clan, "$clans_log_feast", request.Sender.CharacterName, variant.Name);
            }
            ClanStore.MarkDirty();
            request.Ok(pkg => pkg.Write(Mathf.Max(1, settings.LifetimeMinutes)));
        }

        // ---------------------------------------------------------------- helpers

        // An item's name token for the log (translated by each reader), or its prefab name.
        private static string ItemToken(string prefab) {
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared.m_name : prefab;
        }
    }
}
