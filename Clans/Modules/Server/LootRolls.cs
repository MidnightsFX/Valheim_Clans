using Clans.Compat;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using ItemFavoriteFrameworkAPI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Clans.Server {
    // Need/greed/pass for activity rewards, through Item Favorite Framework's server-run loot rolls.
    //
    // Members who took part in an activity together in a party (or a raid) roll for the magic items they
    // earned between them, instead of each getting their own: the server makes the magic items with Epic Loot,
    // drops them at the activity (the World Boss totem, the Race destination, the last Trophy Hunt kill) and
    // starts one roll per item, grouped so the roll window offers Greed all and Pass all. The winner picks it up.
    //
    // Needs the framework on the server with its loot rolls on, and Epic Loot there to make the items. Without
    // them, or for anyone who took part alone, rewards arrive by mailbox as before.
    internal static class LootRolls {
        private const string Source = "clans";
        private const string GroupPrefix = "clans-";
        private const float DropRadius = 1.2f;
        // Item Favorite Framework lets at most this many players roll (its MaxRollMembers default) and leaves
        // the rest out; a raid larger than that rolls squad by squad instead, so everyone can roll.
        private const int RollMemberCap = 10;

        private static bool listening;

        internal static bool Available => ClansConfig.LootRolls.Value && ItemFavorites.HasEndpoint("StartRollForItem") && EpicLootApi.Available;

        internal static void Init() {
            if (listening) { return; }
            listening = true;
            ItemFavorites.AddRollListener(OnRollEvent);
        }

        // Starts the rolls for this activity's magic rewards. Returns the participants whose magic items went
        // into a roll (their own deliveries leave those out). Items that cannot be made or rolled go to the
        // member whose share they were, by mailbox.
        internal static HashSet<string> Start(ClanRecord clan, ActivityInstance activity, ActivityReward reward, List<string> pool, Vector3 point) {
            HashSet<string> rolled = new HashSet<string>();
            if (reward.MagicItems <= 0 || pool.Count == 0 || Available == false) { return rolled; }

            int groupIndex = 0;
            foreach (List<(string Account, long PlayerId)> group in Groups(activity)) {
                if (group.Count < 2) { continue; }
                string ids = ItemFavorites.PlayerIdsToJson(group.Select(m => m.PlayerId));
                string groupId = GroupPrefix + clan.Id + "-" + activity.Id + "-" + groupIndex++;
                string options = Options(Localization.instance != null ? Localization.instance.Localize(ActivityService.Name(activity.Type)) : activity.Type.ToString(), groupId);

                int total = group.Count * reward.MagicItems;
                int index = 0;
                foreach ((string account, long _) in group) {
                    rolled.Add(account);
                    for (int i = 0; i < reward.MagicItems; i++, index++) {
                        string prefab = pool[UnityEngine.Random.Range(0, pool.Count)];
                        int rarity = RewardService.PickRarity(reward.MagicRarityWeights);
                        ItemDrop.ItemData item = MakeMagic(prefab, rarity);
                        string error = "invalidItem";
                        if (item != null && ItemFavorites.StartRollForItem(item, DropPoint(point, index, total), ids, options, out _, out error)) { continue; }
                        Logger.LogWarning($"Clans could not start a loot roll for {prefab} ({error}); it goes to {account} by mailbox.");
                        Mail(account, prefab, rarity);
                    }
                }
            }
            return rolled;
        }

        // Each participant's raid, or their party when they are not in one (a raid too large to roll together
        // splits into its squads); only members whose character the server can see, and so whose player id it
        // knows.
        private static IEnumerable<List<(string Account, long PlayerId)>> Groups(ActivityInstance activity) {
            Dictionary<long, List<(string Account, long PlayerId, long PartyId)>> groups = new Dictionary<long, List<(string, long, long)>>();
            foreach (string account in activity.Participants) {
                PartyService.Party party = PartyService.PartyOf(account);
                ClanSender session = Sessions.Get(account);
                long playerId = session != null ? session.PlayerId : 0L;
                if (party == null || playerId == 0L) { continue; }
                // Raids and parties number separately; keep their keys apart.
                long key = party.RaidId != 0 ? -party.RaidId : party.Id;
                if (groups.TryGetValue(key, out List<(string, long, long)> members) == false) {
                    members = new List<(string, long, long)>();
                    groups[key] = members;
                }
                members.Add((account, playerId, party.Id));
            }
            foreach (List<(string Account, long PlayerId, long PartyId)> group in groups.Values) {
                if (group.Count <= RollMemberCap) {
                    yield return group.Select(m => (m.Account, m.PlayerId)).ToList();
                    continue;
                }
                foreach (IGrouping<long, (string Account, long PlayerId, long PartyId)> squad in group.GroupBy(m => m.PartyId)) {
                    yield return squad.Select(m => (m.Account, m.PlayerId)).ToList();
                }
            }
        }

        private static ItemDrop.ItemData MakeMagic(string prefabName, int rarity) {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null) { return null; }
            ItemDrop.ItemData item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = 1;
            item.m_quality = 1;
            item.m_durability = item.GetMaxDurability();
            try {
                if (EpicLootApi.CanBeMagicItem(item) == false || EpicLootApi.TryMakeMagicItem(item, rarity) == false) { return null; }
            } catch (Exception e) {
                Logger.LogWarning($"Epic Loot could not make a magic {prefabName} on the server: {e.Message}");
                return null;
            }
            return item;
        }

        private static void Mail(string account, string prefab, int rarity) {
            Delivery delivery = new Delivery { Reason = "$clans_mail_activity_reward" };
            delivery.Items.Add(new DeliveryItem { Prefab = prefab, Stack = 1, Quality = 1, Magic = true, Rarity = rarity });
            MailboxService.Add(account, delivery);
        }

        // Spread around the point, a little above the ground so nothing spawns inside it.
        private static Vector3 DropPoint(Vector3 point, int index, int total) {
            float angle = index * Mathf.PI * 2f / Mathf.Max(1, total);
            return point + new Vector3(Mathf.Cos(angle) * DropRadius, 1f, Mathf.Sin(angle) * DropRadius);
        }

        private static string Options(string label, string groupId) {
            return "{\"label\":\"" + Escape(label) + "\",\"source\":\"" + Source + "\",\"groupId\":\"" + Escape(groupId) + "\"}";
        }

        private static string Escape(string text) {
            StringBuilder builder = new StringBuilder();
            foreach (char c in text ?? "") {
                if (c == '"' || c == '\\') { builder.Append('\\').Append(c); } else if (c >= ' ') { builder.Append(c); }
            }
            return builder.ToString();
        }

        // ---------------------------------------------------------------- results

        // Who won what goes into the clan's log, and loot nobody wanted or that was taken mid-roll.
        private static void OnRollEvent(string json) {
            try {
                if (ClanNet.IsServer == false || ClanStore.State == null) { return; }
                if (Field(json, "event") != "result" || Field(json, "side") != "server") { return; }
                string groupId = Field(json, "groupId");
                if (groupId == null || groupId.StartsWith(GroupPrefix, StringComparison.Ordinal) == false) { return; }
                string[] parts = groupId.Substring(GroupPrefix.Length).Split('-');
                if (parts.Length < 1 || long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long clanId) == false) { return; }
                ClanRecord clan = ClanRules.Clan(clanId);
                if (clan == null) { return; }
                string item = ItemToken(Field(json, "prefab"));
                switch (Field(json, "outcome")) {
                    case "won":
                        ClanRules.Log(clan, "$clans_log_loot_won", Field(json, "winnerName") ?? "", item);
                        break;
                    case "allPassed":
                        ClanRules.Log(clan, "$clans_log_loot_unclaimed", item);
                        break;
                    // The drop vanished mid-roll: someone (without the framework) took it.
                    case "cancelled":
                        ClanRules.Log(clan, "$clans_log_loot_cancelled", item);
                        break;
                }
            } catch (Exception e) {
                Logger.LogDebug($"Clans could not read a loot roll event: {e.Message}");
            }
        }

        // The item's name token, so each reader sees it in their own language.
        private static string ItemToken(string prefab) {
            GameObject go = prefab != null && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared.m_name : prefab ?? "";
        }

        // A string field of a flat JSON object; null when absent.
        private static string Field(string json, string key) {
            Match match = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return match.Success ? Regex.Unescape(match.Groups[1].Value) : null;
        }
    }
}
