using Clans.Server.Model;
using Clans.Settings;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Server {
    // Pays out a completed activity: clan experience and treasury coins for the clan, and for every
    // participant a mailbox delivery of coins, items and (World Boss) random Epic Loot items. Members who took
    // part together in a party or raid roll for their magic items instead (LootRolls), dropped at lootPoint.
    internal static class RewardService {
        internal static void Grant(ClanRecord clan, ActivityInstance activity, ActivityReward reward, Heightmap.Biome biome, Vector3 lootPoint) {
            if (reward == null) { return; }
            string reason = ActivityService.Name(activity.Type);
            ClanRules.AddXp(clan, reward.ClanXp, reason);
            if (reward.TreasuryCoins > 0) {
                ClanRules.AddTreasury(clan, reward.TreasuryCoins);
                ClanRules.Log(clan, "$clans_log_treasury_reward", reward.TreasuryCoins.ToString(), reason);
            }

            List<string> pool = Pool(biome);
            HashSet<string> rolled = LootRolls.Start(clan, activity, reward, pool, lootPoint);
            foreach (string account in activity.Participants) {
                Delivery delivery = new Delivery {
                    Reason = "$clans_mail_activity_reward",
                    Coins = Mathf.Max(0, reward.ParticipantCoins),
                };
                if (reward.ParticipantItems != null) {
                    foreach (KeyValuePair<string, int> item in reward.ParticipantItems) {
                        if (string.IsNullOrEmpty(item.Key) || item.Value <= 0) { continue; }
                        delivery.Items.Add(new DeliveryItem { Prefab = item.Key, Stack = item.Value, Quality = 1 });
                    }
                }
                for (int i = 0; i < reward.MagicItems && pool.Count > 0 && rolled.Contains(account) == false; i++) {
                    delivery.Items.Add(new DeliveryItem {
                        Prefab = pool[Random.Range(0, pool.Count)],
                        Stack = 1,
                        Quality = 1,
                        Magic = true,
                        Rarity = PickRarity(reward.MagicRarityWeights),
                    });
                }
                if (delivery.Coins > 0 || delivery.Items.Count > 0) { MailboxService.Add(account, delivery); }
            }
        }

        private static List<string> Pool(Heightmap.Biome biome) {
            Dictionary<string, List<string>> pools = ClansSettings.Activities.MagicItemPools;
            if (pools != null && pools.TryGetValue(biome.ToString(), out List<string> pool) && pool != null && pool.Count > 0) { return pool; }
            if (pools != null && pools.TryGetValue("Meadows", out pool) && pool != null) { return pool; }
            return new List<string>();
        }

        // Index into Epic Loot's rarities (0 Magic ... 5 Ancient), weighted.
        internal static int PickRarity(List<int> weights) {
            if (weights == null || weights.Count == 0) { return 0; }
            int total = 0;
            foreach (int w in weights) { total += Mathf.Max(0, w); }
            if (total <= 0) { return 0; }
            int roll = Random.Range(0, total);
            for (int i = 0; i < weights.Count; i++) {
                roll -= Mathf.Max(0, weights[i]);
                if (roll < 0) { return i; }
            }
            return 0;
        }
    }
}
