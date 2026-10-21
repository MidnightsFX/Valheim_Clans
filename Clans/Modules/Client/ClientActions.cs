using Clans.Core;
using Clans.Gameplay;
using Clans.Net;
using Clans.Settings;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Client {
    // Everything the player can ask the server to do, for the UI and the console commands. Each call reports
    // its outcome through the callback (or, without one, shows a failure to the player).
    internal static class ClientActions {
        // ---------------------------------------------------------------- coins

        internal static string CoinName {
            get {
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Coins") : null;
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                return drop != null ? drop.m_itemData.m_shared.m_name : "$item_coins";
            }
        }

        internal static int CoinsCarried() {
            Player player = Player.m_localPlayer;
            if (player == null) { return 0; }
            return player.GetInventory().CountItems(CoinName, -1, false);
        }

        private static bool TakeCoins(int amount) {
            Player player = Player.m_localPlayer;
            if (amount <= 0) { return true; }
            if (player == null || CoinsCarried() < amount) { return false; }
            player.GetInventory().RemoveItem(CoinName, amount, -1, false);
            return true;
        }

        private static void RefundCoins(int amount) {
            RewardClient.GiveItem("Coins", amount);
        }

        // ---------------------------------------------------------------- founding and joining

        // Founds a clan, paying the fee from the player's inventory. The server holds the name while the fee
        // is taken; if the founding still fails after that, the fee is handed back.
        internal static void CreateClan(string name, string description, EmblemData emblem, bool recruiting, Action<NetResult> done) {
            ClanRequests.Send(Op.CreateClan, pkg => {
                pkg.Write(name ?? "");
                pkg.Write(description ?? "");
                (emblem ?? new EmblemData()).Write(pkg);
                pkg.Write(recruiting);
            }, result => {
                if (result.Ok == false) { done?.Invoke(result); return; }
                long reservation = result.Extra.ReadLong();
                int fee = result.Extra.ReadInt();
                if (reservation == 0) { done?.Invoke(result); return; }
                if (TakeCoins(fee) == false) {
                    ClanRequests.Send(Op.CancelCreate, pkg => pkg.Write(reservation), _ => { });
                    done?.Invoke(new NetResult(false, "$clans_err_cannot_afford", new[] { fee.ToString() }, null));
                    return;
                }
                ClanRequests.Send(Op.ConfirmCreateFee, pkg => pkg.Write(reservation), confirm => {
                    // Without an answer the clan may have been founded: the fee is not handed back twice.
                    if (confirm.TimedOut) { Logger.LogWarning($"No answer to the clan founding; the {fee} coin fee was not given back."); }
                    else if (confirm.Ok == false) { RefundCoins(fee); }
                    done?.Invoke(confirm);
                });
            });
        }

        internal static void Apply(long clanId, string message, Action<NetResult> done = null) {
            ClanRequests.Send(Op.Apply, pkg => { pkg.Write(clanId); pkg.Write(message ?? ""); }, done);
        }

        internal static void WithdrawApplication(long clanId, Action<NetResult> done = null) {
            ClanRequests.Send(Op.WithdrawApplication, pkg => pkg.Write(clanId), done);
        }

        internal static void ResolveApplication(string account, bool accept, Action<NetResult> done = null) {
            ClanRequests.Send(Op.ResolveApplication, pkg => { pkg.Write(account ?? ""); pkg.Write(accept); }, done);
        }

        // ---------------------------------------------------------------- membership

        internal static void Leave(Action<NetResult> done = null) {
            ClanRequests.Send(Op.Leave, null, done);
        }

        internal static void Kick(string account, Action<NetResult> done = null) {
            ClanRequests.Send(Op.Kick, pkg => pkg.Write(account ?? ""), done);
        }

        internal static void SetRank(string account, int rankId, Action<NetResult> done = null) {
            ClanRequests.Send(Op.SetRank, pkg => { pkg.Write(account ?? ""); pkg.Write(rankId); }, done);
        }

        internal static void TransferLeader(string account, Action<NetResult> done = null) {
            ClanRequests.Send(Op.TransferLeader, pkg => pkg.Write(account ?? ""), done);
        }

        internal static void Disband(string confirmName, Action<NetResult> done = null) {
            ClanRequests.Send(Op.Disband, pkg => pkg.Write(confirmName ?? ""), done);
        }

        // ---------------------------------------------------------------- clan settings

        internal static void EditInfo(string status, string description, bool recruiting, Action<NetResult> done = null) {
            ClanRequests.Send(Op.EditInfo, pkg => { pkg.Write(status ?? ""); pkg.Write(description ?? ""); pkg.Write(recruiting); }, done);
        }

        internal static void EditEmblem(EmblemData emblem, Action<NetResult> done = null) {
            ClanRequests.Send(Op.EditEmblem, pkg => (emblem ?? new EmblemData()).Write(pkg), done);
        }

        // Top to bottom. Give new ranks Id 0.
        internal static void EditRanks(List<RankDef> ranks, Action<NetResult> done = null) {
            ClanRequests.Send(Op.EditRanks, pkg => {
                pkg.Write(ranks.Count);
                foreach (RankDef rank in ranks) { rank.Write(pkg); }
            }, done);
        }

        // The server answers with this player's standing and clan detail first, so both are current by the time
        // done runs.
        internal static void RequestDetail(Action<NetResult> done = null) {
            ClanRequests.Send(Op.RequestDetail, null, result => done?.Invoke(result));
        }

        // ---------------------------------------------------------------- treasury

        internal static void Donate(int coins, Action<NetResult> done = null) {
            if (coins <= 0) { return; }
            if (TakeCoins(coins) == false) {
                done?.Invoke(new NetResult(false, "$clans_err_cannot_afford", new[] { coins.ToString() }, null));
                return;
            }
            ClanRequests.Send(Op.DonateTreasury, pkg => pkg.Write(coins), result => {
                // Without an answer the donation may have arrived: the coins are not handed back twice.
                if (result.TimedOut) { Logger.LogWarning($"No answer to a donation of {coins} coins; they were not given back."); }
                else if (result.Ok == false) { RefundCoins(coins); }
                if (done != null) { done(result); } else if (result.Ok == false) { ClanNotices.ShowError(result.Message()); }
            });
        }

        // The coins arrive through the mailbox.
        internal static void WithdrawTreasury(int coins, Action<NetResult> done = null) {
            ClanRequests.Send(Op.WithdrawTreasury, pkg => pkg.Write(coins), done);
        }

        // ---------------------------------------------------------------- items

        internal static ItemDrop ItemPrefab(string prefab) {
            GameObject go = ObjectDB.instance != null && string.IsNullOrEmpty(prefab) == false ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            return go != null ? go.GetComponent<ItemDrop>() : null;
        }

        // An item's translated name, or its prefab name when the game has no such item.
        internal static string ItemName(string prefab) {
            ItemDrop drop = ItemPrefab(prefab);
            return drop != null && Localization.instance != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : prefab;
        }

        // How many of an item the player carries (the inventory counts items by their name token).
        internal static int Carried(string prefab) {
            Player player = Player.m_localPlayer;
            ItemDrop drop = ItemPrefab(prefab);
            if (player == null || drop == null) { return 0; }
            return player.GetInventory().CountItems(drop.m_itemData.m_shared.m_name, -1, false);
        }

        internal static bool CarriesAll(Dictionary<string, int> items) {
            foreach (KeyValuePair<string, int> item in items) {
                if (Carried(item.Key) < item.Value) { return false; }
            }
            return true;
        }

        // Every listed item, or none.
        private static bool TakeItems(Dictionary<string, int> items) {
            Player player = Player.m_localPlayer;
            if (player == null || CarriesAll(items) == false) { return false; }
            foreach (KeyValuePair<string, int> item in items) {
                player.GetInventory().RemoveItem(ItemPrefab(item.Key).m_itemData.m_shared.m_name, item.Value, -1, false);
            }
            return true;
        }

        private static void RefundItems(Dictionary<string, int> items) {
            foreach (KeyValuePair<string, int> item in items) { RewardClient.GiveItem(item.Key, item.Value); }
        }

        // ---------------------------------------------------------------- levels and feasts

        // Hands items in toward the clan's next level. The server keeps no more than the level still needs; the rest
        // comes straight back.
        internal static void ContributeLevel(string item, int amount, Action<NetResult> done = null) {
            if (amount <= 0) { return; }
            Dictionary<string, int> batch = new Dictionary<string, int> { { item, amount } };
            if (TakeItems(batch) == false) {
                Report(done, new NetResult(false, "$clans_err_missing_items", null, null));
                return;
            }
            ClanRequests.Send(Op.ContributeLevel, pkg => { pkg.Write(item); pkg.Write(amount); }, result => {
                // Without an answer they may have arrived: they are not handed back twice.
                if (result.TimedOut) {
                    Logger.LogWarning($"No answer to handing in {amount} {item}; they were not given back.");
                } else if (result.Ok == false) {
                    RefundItems(batch);
                } else {
                    int accepted = result.Extra.ReadInt();
                    if (accepted < amount) { RewardClient.GiveItem(item, amount - accepted); }
                }
                Report(done, result);
            });
        }

        internal static void UpgradeLevel(Action<NetResult> done = null) {
            ClanRequests.Send(Op.UpgradeLevel, null, done);
        }

        // Sets a guild feast out in front of the player. Paid from their inventory, or with clanPays (a rank with
        // ClanFeasts) by the clan: the server takes the coins from the treasury and the rest from clan storage. If
        // the server refuses, or the feast cannot be set out, what the player paid comes back.
        internal static void BuyFeast(GuildFeastDef def, bool clanPays, Action<NetResult> done = null) {
            ClansPrefabs.GuildFeastVariant variant = def != null ? ClansPrefabs.GuildFeastOf(def.Biome) : null;
            Player player = Player.m_localPlayer;
            if (variant == null || player == null) { return; }
            if (player.IsSwimming() || player.IsOnGround() == false) {
                Report(done, new NetResult(false, "$clans_err_feast_place", null, null));
                return;
            }
            Dictionary<string, int> cost = clanPays ? new Dictionary<string, int>() : new Dictionary<string, int>(def.Cost);
            if (TakeItems(cost) == false) {
                Report(done, new NetResult(false, "$clans_err_missing_items", null, null));
                return;
            }
            ClanRequests.Send(Op.BuyFeast, pkg => { pkg.Write(variant.Biome); pkg.Write(clanPays); }, result => {
                if (result.TimedOut) {
                    if (cost.Count > 0) { Logger.LogWarning($"No answer to setting out the {variant.Biome} guild feast; its cost was not given back."); }
                } else if (result.Ok == false) {
                    RefundItems(cost);
                } else {
                    int minutes = result.Extra.ReadInt();
                    if (GuildFeast.Spawn(variant, minutes) == false) {
                        // What the clan paid is spent: the server cannot know the feast never appeared.
                        if (clanPays) { Logger.LogWarning($"The {variant.Biome} guild feast the clan paid for could not be set out."); }
                        RefundItems(cost);
                        result = new NetResult(false, "$clans_err_feast_place", null, null);
                    }
                }
                Report(done, result);
            });
        }

        private static void Report(Action<NetResult> done, NetResult result) {
            if (done != null) { done(result); } else if (result.Ok == false) { ClanNotices.ShowError(result.Message()); }
        }

        // ---------------------------------------------------------------- diagnostics

        internal static void Ping() {
            ClanRequests.Report(Op.Ping, pkg => pkg.Write(DateTime.UtcNow.Ticks));
        }
    }
}
