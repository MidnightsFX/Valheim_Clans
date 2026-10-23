using Clans.Client;
using Clans.Core;
using Clans.Net;
using Clans.Server;
using System.Collections.Generic;

namespace Clans {
    // Clans.API on a plain client: what the server has pushed. Every clan's summary and the wars; every online
    // player with their clan, rank, party and squad; and the player's own clan, party and raid in full, offline
    // members too. Anything else answers as unknown, and nothing is changed from here: coins and items move only
    // on the server.
    internal sealed class ClientReceiver : ApiReceiver {
        private const string NotServer = "notServer";

        public override bool IsReady() => ClanClient.Welcomed;

        public override bool HasFullData() => false;

        // ---------------------------------------------------------------- accounts

        public override string GetAccountId(Player player) {
            if (player == null) { return ""; }
            if (player == Player.m_localPlayer) { return GetLocalAccountId(); }
            return PlayerMap.AccountOf(player) ?? "";
        }

        public override string GetAccountIdOfPeer(long peerId) => PlayerMap.AccountOfPeer(peerId) ?? "";

        // A loaded character, else any player whose character's object this client has.
        public override string GetAccountIdOfPlayerId(long playerId) {
            if (playerId == 0) { return ""; }
            foreach (Player player in Player.GetAllPlayers()) {
                if (player != null && player.GetPlayerID() == playerId) { return GetAccountId(player); }
            }
            if (ZNet.instance == null || ZDOMan.instance == null) { return ""; }
            foreach (ZNet.PlayerInfo info in ZNet.instance.GetPlayerList()) {
                if (info.m_characterID.IsNone()) { continue; }
                ZDO zdo = ZDOMan.instance.GetZDO(info.m_characterID);
                if (zdo != null && zdo.GetLong(ZDOVars.s_playerID, 0L) == playerId) { return AccountId.ForPlayerInfo(info); }
            }
            return "";
        }

        public override string GetPlayerName(string accountId) {
            if (string.IsNullOrEmpty(accountId)) { return ""; }
            RosterEntry entry = ClanClient.RosterOf(accountId);
            if (entry != null) { return entry.Name; }
            MemberInfo member = OwnClanMember(accountId);
            if (member != null) { return member.Name; }
            return OwnPartyMember(accountId, out _)?.Name ?? "";
        }

        public override bool IsOnline(string accountId) => string.IsNullOrEmpty(accountId) == false && ClanClient.RosterOf(accountId) != null;

        // ---------------------------------------------------------------- clans

        public override List<long> GetClanIds() => new List<long>(ClanClient.Clans.Keys);

        public override bool ClanExists(long clanId) => ClanClient.Clan(clanId) != null;

        public override string GetClanName(long clanId) => ClanClient.Clan(clanId)?.Name ?? "";

        public override long FindClanByName(string name) {
            if (string.IsNullOrEmpty(name)) { return 0; }
            string key = ClanRules.NameKey(name);
            foreach (ClanSummary clan in ClanClient.Clans.Values) {
                if (ClanRules.NameKey(clan.Name) == key) { return clan.Id; }
            }
            return 0;
        }

        public override int GetClanLevel(long clanId) => ClanClient.Clan(clanId)?.Level ?? 0;

        public override bool AreAtWar(long clanA, long clanB) => ClanClient.ActiveWar(clanA, clanB) != null;

        protected override List<long> Enemies(long clanId) {
            List<long> enemies = new List<long>();
            foreach (WarSummary war in ClanClient.Wars) {
                if (war.State == Server.Model.WarState.Active && war.Involves(clanId)) { enemies.Add(war.Other(clanId)); }
            }
            return enemies;
        }

        protected override ClanView Clan(long clanId) {
            ClanSummary summary = ClanClient.Clan(clanId);
            if (summary == null) { return null; }
            ClanView view = new ClanView {
                Id = summary.Id,
                Name = summary.Name ?? "",
                Description = summary.Description ?? "",
                Level = summary.Level,
                MemberCount = summary.MemberCount,
                Recruiting = summary.Recruiting,
                LeaderAccount = summary.LeaderAccount ?? "",
                LeaderName = summary.LeaderName ?? "",
            };
            ClanDetailInfo own = OwnClan(clanId);
            if (own != null) {
                view.Detail = true;
                view.Xp = own.Xp;
                view.Treasury = own.Treasury;
                view.SharedTabs = own.SharedTabs;
            }
            return view;
        }

        // ---------------------------------------------------------------- membership and ranks

        public override long GetClanIdOf(string accountId) {
            if (string.IsNullOrEmpty(accountId)) { return 0; }
            if (accountId == ClanClient.MyAccount) { return ClanClient.Me.ClanId; }
            RosterEntry entry = ClanClient.RosterOf(accountId);
            if (entry != null) { return entry.ClanId; }
            return OwnClanMember(accountId) != null ? ClanClient.MyClan.Id : 0;
        }

        protected override bool TryRank(string accountId, out RankInfo rank) {
            MemberInfo mine = OwnClanMember(accountId);
            if (mine != null) { return Rank(ClanClient.MyClan.Id, mine.RankId, ClanClient.MyClan.Ranks, out rank); }
            RosterEntry entry = ClanClient.RosterOf(accountId);
            if (entry == null || entry.ClanId == 0 || entry.RankIndex < 0) {
                rank = default;
                return false;
            }
            rank = new RankInfo { ClanId = entry.ClanId, Id = entry.RankId, Index = entry.RankIndex, StoredName = entry.RankName ?? "" };
            return true;
        }

        // The player's own clan in full; of other clans, those online.
        protected override List<MemberView> Members(long clanId) {
            List<MemberView> members = new List<MemberView>();
            if (clanId == 0) { return members; }
            ClanDetailInfo own = OwnClan(clanId);
            if (own != null) {
                foreach (MemberInfo member in own.Members) {
                    Rank(own.Id, member.RankId, own.Ranks, out RankInfo rank);
                    members.Add(new MemberView {
                        Account = member.Account,
                        Name = member.Name ?? "",
                        RankId = rank.Id,
                        RankIndex = rank.Index,
                        RankName = rank.StoredName,
                        Online = ClanClient.RosterOf(member.Account) != null,
                        JoinedUtc = member.JoinedUtc,
                        LastSeenUtc = member.LastSeenUtc,
                    });
                }
                return members;
            }
            foreach (RosterEntry entry in ClanClient.Roster.Values) {
                if (entry.ClanId != clanId) { continue; }
                members.Add(new MemberView {
                    Account = entry.Account,
                    Name = entry.Name ?? "",
                    RankId = entry.RankId,
                    RankIndex = entry.RankIndex,
                    RankName = entry.RankName ?? "",
                    Online = true,
                });
            }
            return members;
        }

        public override List<string> GetOnlineMemberIds(long clanId) {
            List<string> online = new List<string>();
            if (clanId == 0) { return online; }
            foreach (RosterEntry entry in ClanClient.Roster.Values) {
                if (entry.ClanId == clanId) { online.Add(entry.Account); }
            }
            return online;
        }

        protected override List<RankDef> Ranks(long clanId) => OwnClan(clanId)?.Ranks;

        // Other clans' rights are never sent to clients.
        protected override ClanPermission Permissions(string accountId) {
            if (string.IsNullOrEmpty(accountId)) { return ClanPermission.None; }
            if (accountId == ClanClient.MyAccount) { return ClanClient.Me.Permissions; }
            MemberInfo member = OwnClanMember(accountId);
            if (member == null) { return ClanPermission.None; }
            List<RankDef> ranks = ClanClient.MyClan.Ranks;
            int index = IndexOf(ranks, member.RankId);
            return ClanPermissions.Effective(index, index >= 0 ? ranks[index].Permissions : ClanPermission.None);
        }

        // ---------------------------------------------------------------- parties and raids

        // The player's own party and raid first, which hold members who have just dropped out; then the roster.
        protected override bool TryParty(string accountId, out PartyInfo party) {
            party = default;
            if (string.IsNullOrEmpty(accountId)) { return false; }
            if (OwnPartyMember(accountId, out int squadIndex) != null) {
                PartyClient.Squad squad = PartyClient.Squads[squadIndex];
                party = new PartyInfo { PartyId = squad.PartyId, RaidId = PartyClient.RaidId, Squad = PartyClient.InRaid ? squadIndex + 1 : 0 };
                return true;
            }
            RosterEntry entry = ClanClient.RosterOf(accountId);
            if (entry == null || entry.PartyId == 0) { return false; }
            party = new PartyInfo { PartyId = entry.PartyId, RaidId = entry.RaidId, Squad = entry.Squad };
            return true;
        }

        protected override PartyView Party(long partyId) {
            if (partyId == 0) { return null; }
            int index = PartyClient.Squads.FindIndex(s => s.PartyId == partyId);
            if (index >= 0) { return OwnSquadView(index); }
            PartyView view = null;
            foreach (RosterEntry entry in ClanClient.Roster.Values) {
                if (entry.PartyId != partyId) { continue; }
                view ??= new PartyView { Id = partyId, RaidId = entry.RaidId, Squad = entry.Squad };
                if (entry.PartyLeader) { view.Leader = entry.Account; }
                view.Members.Add(RosterMember(entry));
            }
            return view;
        }

        protected override RaidView Raid(long raidId) {
            if (raidId == 0) { return null; }
            if (PartyClient.RaidId == raidId) {
                RaidView own = new RaidView { Id = raidId, Leader = PartyClient.RaidLeader ?? "" };
                for (int i = 0; i < PartyClient.Squads.Count; i++) { own.Squads.Add(OwnSquadView(i)); }
                return own;
            }
            RaidView view = null;
            SortedDictionary<int, PartyView> squads = new SortedDictionary<int, PartyView>();
            foreach (RosterEntry entry in ClanClient.Roster.Values) {
                if (entry.RaidId != raidId) { continue; }
                view ??= new RaidView { Id = raidId };
                if (entry.RaidLeader) { view.Leader = entry.Account; }
                if (squads.TryGetValue(entry.Squad, out PartyView squad) == false) {
                    squad = new PartyView { Id = entry.PartyId, RaidId = raidId, Squad = entry.Squad };
                    squads[entry.Squad] = squad;
                }
                if (entry.PartyLeader) { squad.Leader = entry.Account; }
                squad.Members.Add(RosterMember(entry));
            }
            if (view != null) { view.Squads.AddRange(squads.Values); }
            return view;
        }

        private static PartyView OwnSquadView(int index) {
            PartyClient.Squad squad = PartyClient.Squads[index];
            PartyView view = new PartyView {
                Id = squad.PartyId,
                Leader = squad.Leader ?? "",
                RaidId = PartyClient.RaidId,
                Squad = PartyClient.InRaid ? index + 1 : 0,
            };
            foreach (PartyClient.Member member in squad.Members) {
                view.Members.Add(new PartyMemberView { Account = member.Account, Name = member.Name ?? "", Online = member.Online, ClanId = member.ClanId });
            }
            return view;
        }

        private static PartyMemberView RosterMember(RosterEntry entry) {
            return new PartyMemberView { Account = entry.Account, Name = entry.Name ?? "", Online = true, ClanId = entry.ClanId };
        }

        // ---------------------------------------------------------------- treasury and storage: the player's own clan, read only

        public override long GetTreasury(long clanId) => OwnClan(clanId)?.Treasury ?? -1;

        public override int GetSharedTabCount(long clanId) => OwnClan(clanId)?.SharedTabs ?? -1;

        // A client asks for it instead (RequestStorageJson).
        protected override StorageContents Storage(long clanId) => null;

        public override bool AddTreasury(long clanId, long coins, string reason) => false;

        public override bool TakeTreasury(long clanId, long coins, string reason) => false;

        public override bool TakeStorageItems(long clanId, string itemsJson, string reason, out string error) {
            error = NotServer;
            return false;
        }

        public override bool AddStorageItems(long clanId, string itemsJson, string reason, out string error) {
            error = NotServer;
            return false;
        }

        public override bool AddStorageItem(long clanId, ItemDrop.ItemData item, string reason, out string error) {
            error = NotServer;
            return false;
        }

        // ---------------------------------------------------------------- the player's own clan and party

        private static ClanDetailInfo OwnClan(long clanId) {
            ClanDetailInfo own = ClanClient.MyClan;
            return own != null && own.Id == clanId && clanId != 0 ? own : null;
        }

        private static MemberInfo OwnClanMember(string accountId) {
            ClanDetailInfo own = ClanClient.MyClan;
            if (own == null) { return null; }
            foreach (MemberInfo member in own.Members) {
                if (member.Account == accountId) { return member; }
            }
            return null;
        }

        // A member of the player's party, or of any squad of their raid.
        private static PartyClient.Member OwnPartyMember(string accountId, out int squadIndex) {
            for (squadIndex = 0; squadIndex < PartyClient.Squads.Count; squadIndex++) {
                foreach (PartyClient.Member member in PartyClient.Squads[squadIndex].Members) {
                    if (member.Account == accountId) { return member; }
                }
            }
            squadIndex = -1;
            return null;
        }
    }
}
