using Clans.Client;
using Clans.Core;
using Clans.Net;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Clans {
    // The answers both receivers give the same way: anything built from other lookups, every JSON shape, the
    // listeners and a client's storage request. ServerReceiver and ClientReceiver fill in the abstract members from
    // wherever their machine keeps the data.
    internal abstract class ApiReceiver : IApiReceiver {
        private static readonly ServerReceiver server = new ServerReceiver();
        private static readonly ClientReceiver client = new ClientReceiver();

        // The server's records once they are loaded on this machine, else what the server has pushed to it.
        internal static IApiReceiver Current => ServerReceiver.Active ? (IApiReceiver)server : client;

        // ---------------------------------------------------------------- where the data comes from

        public abstract bool IsReady();
        public abstract bool HasFullData();
        public abstract string GetAccountId(Player player);
        public abstract string GetAccountIdOfPeer(long peerId);
        public abstract string GetAccountIdOfPlayerId(long playerId);
        public abstract string GetPlayerName(string accountId);
        public abstract bool IsOnline(string accountId);
        public abstract List<long> GetClanIds();
        public abstract bool ClanExists(long clanId);
        public abstract string GetClanName(long clanId);
        public abstract long FindClanByName(string name);
        public abstract int GetClanLevel(long clanId);
        public abstract bool AreAtWar(long clanA, long clanB);
        public abstract long GetClanIdOf(string accountId);
        public abstract List<string> GetOnlineMemberIds(long clanId);
        public abstract long GetTreasury(long clanId);
        public abstract int GetSharedTabCount(long clanId);
        public abstract bool AddTreasury(long clanId, long coins, string reason);
        public abstract bool TakeTreasury(long clanId, long coins, string reason);
        public abstract bool TakeStorageItems(long clanId, string itemsJson, string reason, out string error);
        public abstract bool AddStorageItems(long clanId, string itemsJson, string reason, out string error);
        public abstract bool AddStorageItem(long clanId, ItemDrop.ItemData item, string reason, out string error);

        // Null when there is no such clan.
        protected abstract ClanView Clan(long clanId);
        // The clans this one is fighting an active war with.
        protected abstract List<long> Enemies(long clanId);
        // False when in no clan, or not known here.
        protected abstract bool TryRank(string accountId, out RankInfo rank);
        protected abstract List<MemberView> Members(long clanId);
        // Top to bottom. Null when not known here.
        protected abstract List<RankDef> Ranks(long clanId);
        // None when in no clan, or not known here.
        protected abstract ClanPermission Permissions(string accountId);
        // False when in no party, or not known here.
        protected abstract bool TryParty(string accountId, out PartyInfo party);
        // Null when there is no such party or raid, or nothing is known of it here.
        protected abstract PartyView Party(long partyId);
        protected abstract RaidView Raid(long raidId);
        // What the clan's shared tabs hold. Null when not known here.
        protected abstract StorageContents Storage(long clanId);

        // ---------------------------------------------------------------- clans and members

        public string GetLocalAccountId() => ClanClient.MyAccount ?? "";

        public int GetMaxClanLevel() => ClansSettings.Levels.MaxLevel;

        public int GetMemberCount(long clanId) => Clan(clanId)?.MemberCount ?? 0;

        public string GetLeaderId(long clanId) => Clan(clanId)?.LeaderAccount ?? "";

        public string GetClanJson(long clanId) {
            ClanView clan = Clan(clanId);
            return clan == null ? "" : ClanJson(new JsonOut(), clan).ToString();
        }

        public string GetClansJson() {
            JsonOut json = new JsonOut().Array();
            foreach (long id in GetClanIds()) {
                ClanView clan = Clan(id);
                if (clan != null) { ClanJson(json, clan); }
            }
            return json.End().ToString();
        }

        public long GetClanIdOfPlayer(Player player) => GetClanIdOf(GetAccountId(player));

        public bool IsInClan(string accountId) => GetClanIdOf(accountId) != 0;

        public bool AreClanmates(string accountA, string accountB) {
            long clan = GetClanIdOf(accountA);
            return clan != 0 && clan == GetClanIdOf(accountB);
        }

        public List<string> GetMemberIds(long clanId) => Members(clanId).Select(m => m.Account).ToList();

        public string GetMembersJson(long clanId) {
            JsonOut json = new JsonOut().Array();
            foreach (MemberView member in Members(clanId)) {
                json.Object()
                    .Field("id", member.Account)
                    .Field("name", member.Name)
                    .Field("rankId", member.RankId)
                    .Field("rankIndex", member.RankIndex)
                    .Field("rank", Localize(member.RankName))
                    .Field("leader", member.RankIndex == 0)
                    .Field("online", member.Online)
                    .Field("joinedUtc", member.JoinedUtc)
                    .Field("lastSeenUtc", member.LastSeenUtc)
                    .End();
            }
            return json.End().ToString();
        }

        private JsonOut ClanJson(JsonOut json, ClanView clan) {
            LevelsSettings levels = ClansSettings.Levels;
            json.Object()
                .Field("id", clan.Id)
                .Field("name", clan.Name)
                .Field("description", clan.Description)
                .Field("level", clan.Level)
                .Field("maxLevel", levels.MaxLevel)
                .Field("maxMembers", levels.Get(clan.Level).MaxMembers)
                .Field("memberCount", clan.MemberCount)
                .Field("recruiting", clan.Recruiting)
                .Field("leaderId", clan.LeaderAccount)
                .Field("leaderName", clan.LeaderName)
                .Array("enemies");
            foreach (long enemy in Enemies(clan.Id)) { json.Value(enemy); }
            return json.End()
                .Field("detail", clan.Detail)
                .Field("xp", clan.Xp)
                .Field("treasury", clan.Treasury)
                .Field("sharedTabs", clan.SharedTabs)
                .End();
        }

        // ---------------------------------------------------------------- ranks and rights

        public int GetRankIndex(string accountId) => TryRank(accountId, out RankInfo rank) ? rank.Index : -1;

        public int GetRankId(string accountId) => TryRank(accountId, out RankInfo rank) ? rank.Id : 0;

        public string GetRankName(string accountId) => TryRank(accountId, out RankInfo rank) ? Localize(rank.StoredName) : "";

        public bool IsLeader(string accountId) => GetRankIndex(accountId) == 0;

        public bool HasPermission(string accountId, string permission) {
            return TryPermission(permission, out ClanPermission flag) && Permissions(accountId).Has(flag);
        }

        public List<string> GetPermissionNames() {
            return ClanPermissions.Split(ClanPermissions.All).Select(p => p.ToString()).ToList();
        }

        public string GetRanksJson(long clanId) {
            JsonOut json = new JsonOut().Array();
            List<RankDef> ranks = Ranks(clanId);
            if (ranks != null) {
                for (int i = 0; i < ranks.Count; i++) {
                    RankDef rank = ranks[i];
                    json.Object()
                        .Field("id", rank.Id)
                        .Field("index", i)
                        .Field("name", Localize(rank.Name))
                        .Field("storedName", rank.Name ?? "")
                        .Field("leader", i == 0)
                        .Field("entry", i == ranks.Count - 1)
                        .Array("permissions");
                    foreach (ClanPermission flag in ClanPermissions.Split(ClanPermissions.Effective(i, rank.Permissions))) {
                        json.Value(flag.ToString());
                    }
                    json.End().End();
                }
            }
            return json.End().ToString();
        }

        private static Dictionary<string, ClanPermission> permissionsByName;

        private static bool TryPermission(string name, out ClanPermission flag) {
            if (permissionsByName == null) {
                permissionsByName = new Dictionary<string, ClanPermission>(StringComparer.OrdinalIgnoreCase);
                foreach (ClanPermission each in ClanPermissions.Split(ClanPermissions.All)) { permissionsByName[each.ToString()] = each; }
            }
            return permissionsByName.TryGetValue(name ?? "", out flag);
        }

        // ---------------------------------------------------------------- parties and raids

        public long GetPartyId(string accountId) => TryParty(accountId, out PartyInfo party) ? party.PartyId : 0;

        public bool ArePartymates(string accountA, string accountB) {
            long party = GetPartyId(accountA);
            return party != 0 && party == GetPartyId(accountB);
        }

        public string GetPartyLeaderId(long partyId) => Party(partyId)?.Leader ?? "";

        public List<string> GetPartyMemberIds(long partyId) {
            return Party(partyId)?.Members.Select(m => m.Account).ToList() ?? new List<string>();
        }

        public string GetPartyJson(long partyId) {
            PartyView party = Party(partyId);
            return party == null ? "" : PartyJson(new JsonOut(), party).ToString();
        }

        public long GetRaidId(string accountId) => TryParty(accountId, out PartyInfo party) ? party.RaidId : 0;

        public int GetSquad(string accountId) => TryParty(accountId, out PartyInfo party) ? party.Squad : 0;

        public bool AreRaidmates(string accountA, string accountB) {
            long raid = GetRaidId(accountA);
            return raid != 0 && raid == GetRaidId(accountB);
        }

        public string GetRaidLeaderId(long raidId) => Raid(raidId)?.Leader ?? "";

        public List<string> GetRaidMemberIds(long raidId) {
            return Raid(raidId)?.Squads.SelectMany(s => s.Members).Select(m => m.Account).ToList() ?? new List<string>();
        }

        public string GetRaidJson(long raidId) {
            RaidView raid = Raid(raidId);
            if (raid == null) { return ""; }
            JsonOut json = new JsonOut().Object().Field("id", raid.Id).Field("leaderId", raid.Leader).Array("squads");
            foreach (PartyView squad in raid.Squads) { PartyJson(json, squad); }
            return json.End().End().ToString();
        }

        private static JsonOut PartyJson(JsonOut json, PartyView party) {
            json.Object()
                .Field("id", party.Id)
                .Field("leaderId", party.Leader)
                .Field("raidId", party.RaidId)
                .Field("squad", party.Squad)
                .Array("members");
            foreach (PartyMemberView member in party.Members) {
                json.Object()
                    .Field("id", member.Account)
                    .Field("name", member.Name)
                    .Field("leader", member.Account == party.Leader)
                    .Field("online", member.Online)
                    .Field("clanId", member.ClanId)
                    .End();
            }
            return json.End().End();
        }

        // ---------------------------------------------------------------- storage

        public string GetStorageJson(long clanId) {
            StorageContents contents = Storage(clanId);
            return contents == null ? "" : StorageJson(contents);
        }

        public int CountStorageItem(long clanId, string prefab) {
            StorageContents contents = Storage(clanId);
            if (contents == null) { return -1; }
            long total = 0;
            foreach (StorageContents.Item item in contents.Items) {
                if (item.Prefab == prefab) { total += item.Stack; }
            }
            return (int)Math.Min(int.MaxValue, total);
        }

        // Asked of the server the way the Clans window asks for anything, so it works the same on a listen host.
        public void RequestStorageJson(Action<bool, string> callback) {
            if (callback == null) { return; }
            if (ClanClient.Welcomed == false) {
                callback(false, "notReady");
                return;
            }
            ClanRequests.Send(Op.ReadStorage, _ => { }, result => {
                if (result.Ok) {
                    callback(true, StorageJson(StorageContents.Read(result.Extra)));
                    return;
                }
                callback(false, RequestError(result));
            });
        }

        private static string RequestError(NetResult result) {
            if (result.TimedOut) { return "timeout"; }
            switch (result.Token) {
                case "$clans_err_not_in_clan": return "notInClan";
                case "$clans_err_no_permission": return "noPermission";
                case "$clans_err_storage_off": return "storageOff";
                case "$clans_err_no_server": return "noServer";
                default: return result.Token.TrimStart('$');
            }
        }

        private static string StorageJson(StorageContents contents) {
            JsonOut json = new JsonOut().Object().Field("clan", contents.ClanId).Array("tabs");
            for (int tab = 0; tab < contents.OpenBy.Count; tab++) {
                json.Object().Field("index", tab).Field("openBy", contents.OpenBy[tab]).Array("items");
                foreach (StorageContents.Item item in contents.Items) {
                    if (item.Tab != tab) { continue; }
                    json.Object()
                        .Field("prefab", item.Prefab)
                        .Field("stack", item.Stack)
                        .Field("quality", item.Quality)
                        .Field("x", item.X)
                        .Field("y", item.Y)
                        .Field("custom", item.Custom)
                        .End();
                }
                json.End().End();
            }
            return json.End().End().ToString();
        }

        // ---------------------------------------------------------------- listeners

        public void AddChangedListener(Action listener) => ApiEvents.AddChanged(listener);

        public void RemoveChangedListener(Action listener) => ApiEvents.RemoveChanged(listener);

        public void AddServerEventListener(Action<string> listener) => ApiEvents.AddServer(listener);

        public void RemoveServerEventListener(Action<string> listener) => ApiEvents.RemoveServer(listener);

        // ---------------------------------------------------------------- helpers

        internal static string Localize(string text) {
            if (string.IsNullOrEmpty(text)) { return ""; }
            return Localization.instance != null ? Localization.instance.Localize(text) : text;
        }

        // Like ClanRules.RankIndex: a rank that no longer exists counts as the entry rank.
        protected static int IndexOf(List<RankDef> ranks, int rankId) {
            for (int i = 0; i < ranks.Count; i++) {
                if (ranks[i].Id == rankId) { return i; }
            }
            return ranks.Count - 1;
        }

        protected static bool Rank(long clanId, int rankId, List<RankDef> ranks, out RankInfo rank) {
            int index = IndexOf(ranks, rankId);
            rank = new RankInfo { ClanId = clanId, Id = rankId, Index = index, StoredName = index >= 0 ? ranks[index].Name ?? "" : "" };
            return index >= 0;
        }
    }
}
