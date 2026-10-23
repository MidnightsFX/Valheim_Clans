using Clans.Core;
using Clans.Net;
using Clans.Server;
using Clans.Server.Model;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans {
    // Clans.API on the server (dedicated, listen host or single player): the saved records, which hold every clan in
    // full, the online sessions and the parties. The only receiver that changes clan coins and items.
    internal sealed class ServerReceiver : ApiReceiver {
        private const int MaxReason = 120;

        internal static bool Active => ZNet.instance != null && ZNet.instance.IsServer() && ClanStore.Loaded;

        public override bool IsReady() => Active;

        public override bool HasFullData() => true;

        // ---------------------------------------------------------------- accounts

        public override string GetAccountId(Player player) {
            if (player == null) { return ""; }
            if (player == Player.m_localPlayer) { return GetLocalAccountId(); }
            // A character's ZDOID carries the session id of the client that made it, which is its peer id.
            return GetAccountIdOfPeer(player.GetZDOID().UserID);
        }

        public override string GetAccountIdOfPeer(long peerId) {
            string account = Sessions.AccountOfPeer(peerId);
            if (account != null) { return account; }
            if (peerId == ZDOMan.GetSessionID()) { return GetLocalAccountId(); }
            // Connected, but their client has not said hello yet.
            ZNetPeer peer = ZNet.instance.GetPeer(peerId);
            return peer != null ? AccountId.ForPeer(peer) : "";
        }

        public override string GetAccountIdOfPlayerId(long playerId) {
            if (playerId == 0) { return ""; }
            foreach (Player player in Player.GetAllPlayers()) {
                if (player != null && player.GetPlayerID() == playerId) { return GetAccountId(player); }
            }
            foreach (ClanSender sender in Sessions.All) {
                if (sender.PlayerId == playerId) { return sender.Account; }
            }
            return "";
        }

        public override string GetPlayerName(string accountId) {
            if (string.IsNullOrEmpty(accountId)) { return ""; }
            ClanSender sender = Sessions.Get(accountId);
            if (sender != null) { return sender.CharacterName; }
            return ClanRules.Account(accountId)?.LastCharacterName ?? "";
        }

        public override bool IsOnline(string accountId) => Sessions.IsOnline(accountId);

        // ---------------------------------------------------------------- clans

        public override List<long> GetClanIds() => new List<long>(ClanStore.State.Clans.Keys);

        public override bool ClanExists(long clanId) => ClanRules.Clan(clanId) != null;

        public override string GetClanName(long clanId) => ClanRules.Clan(clanId)?.Name ?? "";

        public override long FindClanByName(string name) => ClanRules.ClanByName(name)?.Id ?? 0;

        public override int GetClanLevel(long clanId) {
            ClanRecord clan = ClanRules.Clan(clanId);
            return clan == null ? 0 : ClanRules.Level(clan);
        }

        public override bool AreAtWar(long clanA, long clanB) {
            if (clanA == 0 || clanB == 0 || clanA == clanB) { return false; }
            foreach (WarRecord war in ClanStore.State.Wars) {
                if (war.State == WarState.Active && war.Involves(clanA) && war.Involves(clanB)) { return true; }
            }
            return false;
        }

        protected override List<long> Enemies(long clanId) {
            List<long> enemies = new List<long>();
            foreach (WarRecord war in ClanStore.State.Wars) {
                if (war.State == WarState.Active && war.Involves(clanId)) { enemies.Add(war.Other(clanId)); }
            }
            return enemies;
        }

        protected override ClanView Clan(long clanId) {
            ClanRecord clan = ClanRules.Clan(clanId);
            if (clan == null) { return null; }
            MemberRecord leader = ClanRules.Leader(clan);
            return new ClanView {
                Id = clan.Id,
                Name = clan.Name ?? "",
                Description = clan.Description ?? "",
                Level = ClanRules.Level(clan),
                MemberCount = clan.Members.Count,
                Recruiting = clan.Recruiting,
                LeaderAccount = leader?.Account ?? "",
                LeaderName = leader?.LastCharacterName ?? "",
                Detail = true,
                Xp = clan.Xp,
                Treasury = clan.Treasury,
                SharedTabs = clan.Storage.SharedTabs.Count,
            };
        }

        // ---------------------------------------------------------------- membership and ranks

        public override long GetClanIdOf(string accountId) => ClanRules.Account(accountId)?.ClanId ?? 0;

        protected override bool TryRank(string accountId, out RankInfo rank) {
            ClanRecord clan = ClanRules.ClanOf(accountId);
            MemberRecord member = ClanRules.Member(clan, accountId);
            if (member == null) {
                rank = default;
                return false;
            }
            return Rank(clan.Id, member.RankId, clan.Ranks, out rank);
        }

        protected override List<MemberView> Members(long clanId) {
            List<MemberView> members = new List<MemberView>();
            ClanRecord clan = ClanRules.Clan(clanId);
            if (clan == null) { return members; }
            foreach (MemberRecord member in clan.Members.Values) {
                Rank(clan.Id, member.RankId, clan.Ranks, out RankInfo rank);
                members.Add(new MemberView {
                    Account = member.Account,
                    Name = member.LastCharacterName ?? "",
                    RankId = rank.Id,
                    RankIndex = rank.Index,
                    RankName = rank.StoredName,
                    Online = Sessions.IsOnline(member.Account),
                    JoinedUtc = member.JoinedUtc,
                    LastSeenUtc = member.LastSeenUtc,
                });
            }
            return members;
        }

        public override List<string> GetOnlineMemberIds(long clanId) {
            List<string> online = new List<string>();
            if (clanId == 0) { return online; }
            foreach (ClanSender sender in Sessions.All) {
                if (ClanRules.Account(sender.Account)?.ClanId == clanId) { online.Add(sender.Account); }
            }
            return online;
        }

        protected override List<RankDef> Ranks(long clanId) => ClanRules.Clan(clanId)?.Ranks;

        protected override ClanPermission Permissions(string accountId) {
            ClanRecord clan = ClanRules.ClanOf(accountId);
            return ClanRules.Permissions(clan, ClanRules.Member(clan, accountId));
        }

        // ---------------------------------------------------------------- parties and raids

        protected override bool TryParty(string accountId, out PartyInfo party) {
            PartyService.Party found = PartyService.PartyOf(accountId);
            party = found == null ? default : new PartyInfo { PartyId = found.Id, RaidId = found.RaidId, Squad = PartyService.SquadOf(found) };
            return found != null;
        }

        protected override PartyView Party(long partyId) {
            PartyService.Party party = PartyService.Find(partyId);
            return party == null ? null : View(party);
        }

        protected override RaidView Raid(long raidId) {
            PartyService.Raid raid = PartyService.FindRaid(raidId);
            if (raid == null) { return null; }
            RaidView view = new RaidView { Id = raid.Id, Leader = raid.Leader ?? "" };
            foreach (long partyId in raid.Parties) {
                PartyService.Party squad = PartyService.Find(partyId);
                if (squad != null) { view.Squads.Add(View(squad)); }
            }
            return view;
        }

        private PartyView View(PartyService.Party party) {
            PartyView view = new PartyView { Id = party.Id, Leader = party.Leader ?? "", RaidId = party.RaidId, Squad = PartyService.SquadOf(party) };
            foreach (string account in party.Members) {
                view.Members.Add(new PartyMemberView {
                    Account = account,
                    Name = GetPlayerName(account),
                    Online = Sessions.IsOnline(account),
                    ClanId = GetClanIdOf(account),
                });
            }
            return view;
        }

        // ---------------------------------------------------------------- treasury

        public override long GetTreasury(long clanId) => ClanRules.Clan(clanId)?.Treasury ?? -1;

        public override bool AddTreasury(long clanId, long coins, string reason) {
            ClanRecord clan = ClanRules.Clan(clanId);
            if (clan == null || coins <= 0 || coins > long.MaxValue - clan.Treasury) { return false; }
            ClanRules.AddTreasury(clan, coins);
            LogReason(clan, "$clans_log_api_treasury_in", reason, coins.ToString());
            return true;
        }

        public override bool TakeTreasury(long clanId, long coins, string reason) {
            ClanRecord clan = ClanRules.Clan(clanId);
            if (clan == null || coins <= 0 || clan.Treasury < coins) { return false; }
            ClanRules.AddTreasury(clan, -coins);
            LogReason(clan, "$clans_log_api_treasury_out", reason, coins.ToString());
            return true;
        }

        // ---------------------------------------------------------------- storage

        public override int GetSharedTabCount(long clanId) => ClanRules.Clan(clanId)?.Storage.SharedTabs.Count ?? -1;

        protected override StorageContents Storage(long clanId) {
            ClanRecord clan = ClanRules.Clan(clanId);
            return clan == null ? null : StorageService.Contents(clan);
        }

        public override bool TakeStorageItems(long clanId, string itemsJson, string reason, out string error) {
            ClanRecord clan = StorageClan(clanId, out error);
            if (clan == null) { return false; }
            Dictionary<string, int> items = JsonOut.ParseItems(itemsJson);
            if (items == null) { error = "badItems"; return false; }
            switch (StorageService.TryTakeShared(clan, items, out _)) {
                case StorageService.TakeResult.Short: error = "notEnough"; return false;
                case StorageService.TakeResult.TabOpen: error = "tabOpen"; return false;
            }
            foreach (KeyValuePair<string, int> item in items) {
                LogReason(clan, "$clans_log_api_storage_out", reason, item.Value.ToString(), ItemToken(item.Key));
            }
            return true;
        }

        public override bool AddStorageItems(long clanId, string itemsJson, string reason, out string error) {
            ClanRecord clan = StorageClan(clanId, out error);
            if (clan == null) { return false; }
            Dictionary<string, int> parsed = JsonOut.ParseItems(itemsJson);
            if (parsed == null) { error = "badItems"; return false; }
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>();
            foreach (KeyValuePair<string, int> entry in parsed) {
                ItemDrop.ItemData item = NewItem(entry.Key, entry.Value);
                if (item == null) { error = "unknownItem"; return false; }
                items.Add(item);
            }
            return AddItems(clan, items, reason, out error);
        }

        // A copy goes in; the caller's own item is left as it was.
        public override bool AddStorageItem(long clanId, ItemDrop.ItemData item, string reason, out string error) {
            ClanRecord clan = StorageClan(clanId, out error);
            if (clan == null) { return false; }
            if (item == null || item.m_stack < 1 || item.m_shared == null) { error = "badItems"; return false; }
            if (item.m_dropPrefab == null) { error = "unknownItem"; return false; }
            return AddItems(clan, new List<ItemDrop.ItemData> { item.Clone() }, reason, out error);
        }

        private static bool AddItems(ClanRecord clan, List<ItemDrop.ItemData> items, string reason, out string error) {
            error = "";
            foreach (ItemDrop.ItemData item in items) {
                if (ClansSettings.Storage.Accepts(item) == false) { error = "notAccepted"; return false; }
            }
            if (StorageService.TryAddShared(clan, items, out string openBy) == false) {
                error = openBy != null ? "tabOpen" : "noRoom";
                return false;
            }
            foreach (ItemDrop.ItemData item in items) {
                LogReason(clan, "$clans_log_api_storage_in", reason, item.m_stack.ToString(), item.m_shared.m_name);
            }
            return true;
        }

        // The clan, when its storage can be changed.
        private static ClanRecord StorageClan(long clanId, out string error) {
            error = "";
            ClanRecord clan = ClanRules.Clan(clanId);
            if (clan == null) { error = "noClan"; return null; }
            if (ClansConfig.EnableStorage.Value == false) { error = "storageOff"; return null; }
            return clan;
        }

        // A new item of the prefab, as the game would make one.
        private static ItemDrop.ItemData NewItem(string prefabName, int stack) {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null) { return null; }
            ItemDrop.ItemData item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = stack;
            item.m_quality = 1;
            item.m_durability = item.GetMaxDurability();
            item.m_worldLevel = (byte)Game.m_worldLevel;
            return item;
        }

        // An item's name token for the log (translated by each reader), or its prefab name.
        private static string ItemToken(string prefab) {
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared.m_name : prefab;
        }

        // The clan's log gets a line when the calling mod gave a reason; a $token in it is translated for each reader.
        private static void LogReason(ClanRecord clan, string token, string reason, params string[] args) {
            string text = ClanRules.CleanText(reason, MaxReason);
            if (text.Length == 0) { return; }
            ClanRules.Log(clan, token, new[] { text }.Concat(args).ToArray());
        }
    }
}
