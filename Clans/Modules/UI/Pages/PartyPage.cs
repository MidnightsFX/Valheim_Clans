using Clans.Client;
using Clans.Net;
using Clans.Settings;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI.Pages {
    // Your squad and raid, and the players you could invite: those nearby, your clanmates, or anyone online.
    internal sealed class PartyPage : ClanPage {
        internal override string Id => "party";
        internal override string Prefab => "Clans_Page_Party";

        private enum Filter { Nearby, Clanmates, Everyone }

        private static readonly Color OnlineColor = new Color(0.4f, 0.85f, 0.35f, 1f);
        private static readonly Color OfflineColor = new Color(0.35f, 0.33f, 0.3f, 1f);

        private TemplateList members;
        private TemplateList squads;
        private TemplateList players;
        private TMP_InputField search;
        private Filter filter = Filter.Nearby;

        private sealed class Candidate {
            internal string Account;
            internal string Name;
            internal float Distance = -1f;
        }

        protected override void Bind() {
            members = new TemplateList(UiBind.Find(T, "Left/Members"));
            squads = new TemplateList(UiBind.Find(T, "Left/Squads"));
            players = new TemplateList(UiBind.Find(T, "Right/Players"));
            search = UiBind.Get<TMP_InputField>(T, "Right/Search");
            if (search != null) { search.onValueChanged.AddListener(_ => RefreshPlayers()); }
            UiBind.OnClick(T, "Right/Filters/Nearby", () => SetFilter(Filter.Nearby));
            UiBind.OnClick(T, "Right/Filters/Clanmates", () => SetFilter(Filter.Clanmates));
            UiBind.OnClick(T, "Right/Filters/Everyone", () => SetFilter(Filter.Everyone));
            UiBind.OnClick(T, "Left/PartyButtons/Create", () => PartyActions.Create(ClansWindow.ReportFailure));
            UiBind.OnClick(T, "Left/PartyButtons/Leave", () => PartyActions.Leave(ClansWindow.ReportFailure));
            UiBind.OnClick(T, "Left/PartyButtons/PostInvite", () => {
                ChatChannel channel = ChatClient.Available(ChatChannel.Global) ? ChatChannel.Global : ChatChannel.Clan;
                ChatClient.PostInvite(ChatLinkKind.PartyInvite, channel, result => {
                    if (result.Ok) { ClansWindow.Status(UiBind.Loc("$clans_chat_invite_posted")); } else { ClansWindow.ReportFailure(result); }
                });
            });
            UiBind.OnClick(T, "Left/LeaveRaid", () => PartyActions.LeaveRaid(ClansWindow.ReportFailure));
        }

        private void SetFilter(Filter value) {
            filter = value;
            RefreshPlayers();
        }

        // Distances to nearby players change as everyone moves.
        internal override void Tick() {
            if (filter == Filter.Nearby) { RefreshPlayers(); }
        }

        internal override void Refresh() {
            RefreshParty();
            RefreshPlayers();
        }

        private void RefreshParty() {
            int max = ClansConfig.MaxPartySize.Value;
            PartyClient.Squad mine = PartyClient.MySquad;
            bool leader = PartyClient.IsLeader;
            UiBind.SetText(T, "Left/PartyHeader", mine == null ? UiBind.Loc("$clans_party_none") : UiBind.Loc("$clans_party_header", mine.Members.Count, max));
            UiBind.SetActive(T, "Left/PartyButtons/Create", PartyClient.InParty == false);
            UiBind.SetActive(T, "Left/PartyButtons/Leave", PartyClient.InParty);
            // Posting one makes a party first if there is none; only its leader can invite.
            UiBind.SetActive(T, "Left/PartyButtons/PostInvite", (PartyClient.InParty == false || PartyClient.IsLeader)
                && (ChatClient.Available(ChatChannel.Global) || ChatClient.Available(ChatChannel.Clan)));

            List<PartyClient.Member> list = mine?.Members ?? new List<PartyClient.Member>();
            members.Sync(list.Count, (item, i) => {
                PartyClient.Member member = list[i];
                Transform t = item.transform;
                bool self = member.Account == ClanClient.MyAccount;
                Image dot = UiBind.Get<Image>(t, "Online");
                if (dot != null) { dot.color = member.Online ? OnlineColor : OfflineColor; }
                ShowEmblem(UiBind.Find(t, "Emblem"), member.ClanId);
                UiBind.SetText(t, "Name", member.Name);
                string role = member.Account == mine.Leader ? "$clans_party_leader" : (member.Online ? "" : "$clans_party_offline");
                UiBind.SetText(t, "Role", UiBind.Loc(role));
                UiBind.SetActive(t, "Promote", leader && self == false && member.Online);
                UiBind.SetActive(t, "Kick", leader && self == false);
                string account = member.Account;
                UiBind.OnClick(t, "Promote", () => PartyActions.SetLeader(account, ClansWindow.ReportFailure));
                UiBind.OnClick(t, "Kick", () => PartyActions.Kick(account, ClansWindow.ReportFailure));
            });

            int maxSquads = ClansConfig.MaxSquadsPerRaid.Value;
            UiBind.SetText(T, "Left/RaidHeader", PartyClient.InRaid ? UiBind.Loc("$clans_raid_header", PartyClient.Squads.Count, maxSquads) : UiBind.Loc("$clans_raid_none"));
            UiBind.SetActive(T, "Left/LeaveRaid", PartyClient.InRaid && leader);
            List<PartyClient.Squad> raid = PartyClient.InRaid ? PartyClient.Squads : new List<PartyClient.Squad>();
            squads.Sync(raid.Count, (item, i) => {
                PartyClient.Squad squad = raid[i];
                Transform t = item.transform;
                string leaderName = squad.Members.Find(m => m.Account == squad.Leader)?.Name ?? "";
                string title = UiBind.Loc("$clans_raid_squad", i + 1, leaderName);
                if (squad.Leader == PartyClient.RaidLeader) { title += " - " + UiBind.Loc("$clans_raid_leader"); }
                UiBind.SetText(t, "Head/Title", title);
                UiBind.SetText(t, "Members", string.Join(", ", squad.Members.Select(m => m.Name)));
                bool canLead = PartyClient.IsRaidLeader && squad.Leader != ClanClient.MyAccount;
                UiBind.SetActive(t, "Head/MakeRaidLeader", canLead);
                string squadLeader = squad.Leader;
                UiBind.OnClick(t, "Head/MakeRaidLeader", () => PartyActions.SetRaidLeader(squadLeader, ClansWindow.ReportFailure));
            });
        }

        private void RefreshPlayers() {
            foreach (Filter f in new[] { Filter.Nearby, Filter.Clanmates, Filter.Everyone }) {
                UiBind.SetTabSelected(UiBind.Get<Button>(T, "Right/Filters/" + f), f == filter);
            }
            List<Candidate> candidates = Candidates();
            string text = search != null ? search.text.Trim() : "";
            if (text.Length > 0) {
                candidates = candidates.FindAll(c => c.Name.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }
            UiBind.SetActive(T, "Right/Players/Empty", candidates.Count == 0);

            bool canInvite = PartyClient.InParty == false || PartyClient.IsLeader;
            bool partyFull = PartyClient.MySquad != null && PartyClient.MySquad.Members.Count >= ClansConfig.MaxPartySize.Value;
            bool canInviteRaid = PartyClient.IsLeader && (PartyClient.InRaid == false || PartyClient.IsRaidLeader)
                && (PartyClient.InRaid == false || PartyClient.Squads.Count < ClansConfig.MaxSquadsPerRaid.Value);
            players.Sync(candidates.Count, (item, i) => {
                Candidate c = candidates[i];
                Transform t = item.transform;
                RosterEntry roster = ClanClient.RosterOf(c.Account);
                ShowEmblem(UiBind.Find(t, "Emblem"), roster?.ClanId ?? 0);
                UiBind.SetText(t, "Texts/Name", c.Name);
                UiBind.SetText(t, "Texts/Info", Describe(c, roster));
                bool free = roster == null || roster.PartyId == 0;
                bool theirLeader = roster != null && roster.PartyId != 0 && roster.PartyLeader && roster.PartyId != PartyClient.PartyId && roster.RaidId == 0;
                UiBind.SetActive(t, "Invite", canInvite && free && partyFull == false);
                UiBind.SetActive(t, "InviteRaid", canInviteRaid && theirLeader);
                string account = c.Account;
                string name = c.Name;
                UiBind.OnClick(t, "Invite", () => PartyActions.Invite(account, result => {
                    if (result.Ok) { ClansWindow.Status(UiBind.Loc("$clans_party_invited", name)); } else { ClansWindow.ReportFailure(result); }
                }));
                UiBind.OnClick(t, "InviteRaid", () => PartyActions.InviteToRaid(account, result => {
                    if (result.Ok) { ClansWindow.Status(UiBind.Loc("$clans_raid_invited", name)); } else { ClansWindow.ReportFailure(result); }
                }));
            });
        }

        private List<Candidate> Candidates() {
            List<Candidate> result = new List<Candidate>();
            string me = ClanClient.MyAccount;
            Player local = Player.m_localPlayer;
            switch (filter) {
                case Filter.Nearby:
                    if (local == null) { break; }
                    float range = ClansConfig.NearbyInviteRange.Value;
                    foreach (Player player in Player.GetAllPlayers()) {
                        if (player == null || player == local) { continue; }
                        float distance = Vector3.Distance(player.transform.position, local.transform.position);
                        if (distance > range) { continue; }
                        string account = PlayerMap.AccountOf(player);
                        if (account == null) { continue; }
                        result.Add(new Candidate { Account = account, Name = player.GetPlayerName(), Distance = distance });
                    }
                    result.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                    break;
                case Filter.Clanmates:
                    ClanDetailInfo clan = ClanClient.MyClan;
                    if (clan == null) { break; }
                    foreach (MemberInfo member in clan.Members) {
                        if (member.Online && member.Account != me) { result.Add(new Candidate { Account = member.Account, Name = member.Name }); }
                    }
                    result.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
                    break;
                default:
                    foreach (RosterEntry entry in ClanClient.Roster.Values) {
                        if (entry.Account != me) { result.Add(new Candidate { Account = entry.Account, Name = entry.Name }); }
                    }
                    result.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
                    break;
            }
            return result;
        }

        private static string Describe(Candidate c, RosterEntry roster) {
            List<string> parts = new List<string>();
            if (c.Distance >= 0f) { parts.Add(UiBind.Loc("$clans_party_distance", Mathf.RoundToInt(c.Distance))); }
            ClanSummary clan = ClanClient.Clan(roster?.ClanId ?? 0);
            if (clan != null) { parts.Add(clan.Name); }
            if (roster != null && roster.PartyId != 0) {
                parts.Add(UiBind.Loc(roster.PartyId == PartyClient.PartyId ? "$clans_party_in_yours" : "$clans_party_in_other"));
            }
            return string.Join(" - ", parts);
        }

        private static void ShowEmblem(Transform emblem, long clanId) {
            if (emblem == null) { return; }
            ClanSummary clan = ClanClient.Clan(clanId);
            emblem.gameObject.SetActive(clan != null);
            if (clan != null) { EmblemView.Apply(emblem, clan.Emblem); }
        }
    }
}
