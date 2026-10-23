using Clans.Net;
using Clans.Settings;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans.Server {
    // Parties (squads) and raids. A raid joins several parties under one raid leader; each party in a raid
    // is a squad, numbered by its place in the raid. In memory only: a server restart ends them all.
    //
    // A player who disconnects keeps their place for the disconnect grace period, shown as offline.
    internal static class PartyService {
        internal sealed class Party {
            internal long Id;
            internal string Leader;
            internal readonly List<string> Members = new List<string>();
            internal long RaidId;
        }

        internal sealed class Raid {
            internal long Id;
            internal string Leader;
            // Squad order: index 0 is squad 1.
            internal readonly List<long> Parties = new List<long>();
        }

        internal enum InviteKind : byte { Party = 0, Raid = 1 }

        private sealed class Invite {
            internal long Id;
            internal InviteKind Kind;
            internal string From;
            internal string FromName;
            internal string To;
            internal long PartyId;
            internal float Expires;
        }

        private static readonly Dictionary<long, Party> parties = new Dictionary<long, Party>();
        private static readonly Dictionary<long, Raid> raids = new Dictionary<long, Raid>();
        private static readonly Dictionary<string, long> partyOf = new Dictionary<string, long>();
        private static readonly Dictionary<long, Invite> invites = new Dictionary<long, Invite>();
        // Offline members and when they dropped, for the grace period.
        private static readonly Dictionary<string, float> offlineSince = new Dictionary<string, float>();
        private static readonly HashSet<long> dirtyParties = new HashSet<long>();
        private static long nextId = 1;

        internal static IEnumerable<Party> AllParties => parties.Values;

        internal static void Register() {
            ClanNet.RegisterServer(Op.CreateParty, OnCreate);
            ClanNet.RegisterServer(Op.InviteParty, OnInvite);
            ClanNet.RegisterServer(Op.RespondInvite, OnRespond);
            ClanNet.RegisterServer(Op.LeaveParty, OnLeave);
            ClanNet.RegisterServer(Op.KickParty, OnKick);
            ClanNet.RegisterServer(Op.SetPartyLeader, OnSetLeader);
            ClanNet.RegisterServer(Op.InviteSquadToRaid, OnInviteRaid);
            ClanNet.RegisterServer(Op.LeaveRaid, OnLeaveRaid);
            ClanNet.RegisterServer(Op.MoveToSquad, OnMoveToSquad);
            ClanNet.RegisterServer(Op.SetRaidLeader, OnSetRaidLeader);
        }

        internal static bool Enabled => ClansConfig.EnableParties.Value;

        internal static void Reset() {
            parties.Clear();
            raids.Clear();
            partyOf.Clear();
            invites.Clear();
            offlineSince.Clear();
            dirtyParties.Clear();
        }

        // ---------------------------------------------------------------- lookups

        internal static Party PartyOf(string account) {
            if (account == null || partyOf.TryGetValue(account, out long id) == false) { return null; }
            parties.TryGetValue(id, out Party party);
            return party;
        }

        internal static Raid RaidOf(Party party) {
            if (party == null || party.RaidId == 0) { return null; }
            raids.TryGetValue(party.RaidId, out Raid raid);
            return raid;
        }

        internal static Party Find(long partyId) {
            parties.TryGetValue(partyId, out Party party);
            return party;
        }

        internal static Raid FindRaid(long raidId) {
            raids.TryGetValue(raidId, out Raid raid);
            return raid;
        }

        internal static int SquadOf(Party party) {
            Raid raid = RaidOf(party);
            return raid == null ? 0 : raid.Parties.IndexOf(party.Id) + 1;
        }

        internal static bool IsOnline(string account) => Sessions.IsOnline(account);

        // Every member of every squad in a raid.
        internal static IEnumerable<string> RaidMembers(Raid raid) {
            return raid.Parties.Select(id => parties.TryGetValue(id, out Party p) ? p : null).Where(p => p != null).SelectMany(p => p.Members);
        }

        // The player's party, made for them (as its leader) if they have none.
        internal static Party EnsureParty(string account) => PartyOf(account) ?? Create(account);

        // Joining through an invite a party leader posted in chat.
        // A party invite posted in chat holds only while its poster still leads that party.
        internal static void JoinFromChat(ServerRequest request, long partyId, string poster) {
            if (CheckEnabled(request) == false) { return; }
            if (parties.TryGetValue(partyId, out Party party) == false || party.Leader != poster) { request.Fail("$clans_err_invite_gone"); return; }
            if (PartyOf(request.Sender.Account) != null) { request.Fail("$clans_err_in_party"); return; }
            if (party.Members.Count >= ClansConfig.MaxPartySize.Value) { request.Fail("$clans_err_party_full", ClansConfig.MaxPartySize.Value.ToString()); return; }
            Join(party, request.Sender);
            request.Ok();
        }

        // Everyone who sees this player on the map: their raid, or just their party.
        internal static IEnumerable<string> Companions(Party party) {
            Raid raid = RaidOf(party);
            if (raid == null) { return party.Members; }
            return raid.Parties.Select(id => parties.TryGetValue(id, out Party p) ? p : null).Where(p => p != null).SelectMany(p => p.Members);
        }

        internal static void FillRoster(RosterEntry entry) {
            Party party = PartyOf(entry.Account);
            if (party == null) { return; }
            entry.PartyId = party.Id;
            entry.RaidId = party.RaidId;
            entry.Squad = SquadOf(party);
            entry.PartyLeader = party.Leader == entry.Account;
            entry.RaidLeader = RaidOf(party)?.Leader == entry.Account;
        }

        // ---------------------------------------------------------------- lifecycle

        internal static void OnAccountOffline(string account) {
            if (PartyOf(account) != null) {
                offlineSince[account] = Time.unscaledTime;
                MarkParty(PartyOf(account));
            }
            foreach (Invite invite in invites.Values.Where(i => i.To == account || i.From == account).ToList()) {
                CloseInvite(invite, "$clans_invite_closed_offline");
            }
        }

        internal static void OnAccountOnline(ClanSender sender) {
            offlineSince.Remove(sender.Account);
            Party party = PartyOf(sender.Account);
            if (party != null) { MarkParty(party); }
            SendState(sender.Account);
        }

        // Clan changes alter what party members can share; the frames show clan emblems too.
        internal static void OnClanChanged(string account) {
            Party party = PartyOf(account);
            if (party != null) { MarkParty(party); }
        }

        private const float TickSeconds = 0.25f;
        private static float tickTimer;

        internal static void Tick(float dt) {
            tickTimer -= dt;
            if (tickTimer > 0f) { return; }
            tickTimer = TickSeconds;
            float now = Time.unscaledTime;
            foreach (Invite invite in invites.Values.Where(i => i.Expires <= now).ToList()) {
                CloseInvite(invite, "$clans_invite_closed_expired");
            }
            float grace = ClansConfig.DisconnectGraceSeconds.Value;
            foreach (KeyValuePair<string, float> entry in offlineSince.Where(e => now - e.Value > grace).ToList()) {
                offlineSince.Remove(entry.Key);
                RemoveFromParty(entry.Key, "$clans_party_log_timed_out", "timedOut");
            }
            if (dirtyParties.Count > 0) {
                foreach (long id in dirtyParties.ToList()) { PushParty(id); }
                dirtyParties.Clear();
            }
        }

        // ---------------------------------------------------------------- party requests

        private static bool CheckEnabled(ServerRequest request) {
            if (Enabled) { return true; }
            request.Fail("$clans_err_parties_off");
            return false;
        }

        private static void OnCreate(ServerRequest request) {
            if (CheckEnabled(request) == false) { return; }
            if (PartyOf(request.Sender.Account) != null) { request.Fail("$clans_err_in_party"); return; }
            Create(request.Sender.Account);
            request.Ok();
        }

        private static Party Create(string leader) {
            Party party = new Party { Id = nextId++, Leader = leader };
            party.Members.Add(leader);
            parties[party.Id] = party;
            partyOf[leader] = party.Id;
            // Any invitations to a party this player had are moot now.
            foreach (Invite invite in invites.Values.Where(i => i.To == leader && i.Kind == InviteKind.Party).ToList()) {
                CloseInvite(invite, "$clans_invite_closed_joined");
            }
            MarkParty(party);
            ApiEvents.PartyJoined(party.Id, leader);
            return party;
        }

        private static void OnInvite(ServerRequest request) {
            string target = request.Body.ReadString();
            if (CheckEnabled(request) == false) { return; }
            ClanSender sender = request.Sender;
            if (target == sender.Account) { request.Fail("$clans_err_bad_request"); return; }
            ClanSender targetSession = Sessions.Get(target);
            if (targetSession == null) { request.Fail("$clans_err_not_online"); return; }
            if (PartyOf(target) != null) { request.Fail("$clans_err_target_in_party", targetSession.CharacterName); return; }

            Party party = PartyOf(sender.Account) ?? Create(sender.Account);
            if (party.Leader != sender.Account) { request.Fail("$clans_err_party_leader_only"); return; }
            if (party.Members.Count >= ClansConfig.MaxPartySize.Value) { request.Fail("$clans_err_party_full", ClansConfig.MaxPartySize.Value.ToString()); return; }
            if (invites.Values.Any(i => i.To == target && i.From == sender.Account && i.Kind == InviteKind.Party)) {
                request.Fail("$clans_err_already_invited", targetSession.CharacterName);
                return;
            }

            Invite invite = new Invite {
                Id = nextId++,
                Kind = InviteKind.Party,
                From = sender.Account,
                FromName = sender.CharacterName,
                To = target,
                PartyId = party.Id,
                Expires = Time.unscaledTime + ClansConfig.InviteExpirySeconds.Value,
            };
            invites[invite.Id] = invite;
            SendInvite(invite, targetSession, party);
            request.Ok();
        }

        private static void SendInvite(Invite invite, ClanSender to, Party party) {
            ClanNet.Send(to, Op.Invite, 0, pkg => {
                pkg.Write(invite.Id);
                pkg.Write((byte)invite.Kind);
                pkg.Write(invite.From);
                pkg.Write(invite.FromName ?? "");
                pkg.Write(party?.Members.Count ?? 0);
                pkg.Write(Mathf.Max(0f, invite.Expires - Time.unscaledTime));
            });
        }

        private static void CloseInvite(Invite invite, string reasonToken) {
            invites.Remove(invite.Id);
            ClanSender to = Sessions.Get(invite.To);
            if (to != null) {
                ClanNet.Send(to, Op.InviteClosed, 0, pkg => { pkg.Write(invite.Id); pkg.Write(reasonToken ?? ""); });
            }
        }

        private static void OnRespond(ServerRequest request) {
            long id = request.Body.ReadLong();
            bool accept = request.Body.ReadBool();
            if (invites.TryGetValue(id, out Invite invite) == false || invite.To != request.Sender.Account) {
                request.Fail("$clans_err_invite_gone");
                return;
            }
            CloseInvite(invite, "");
            if (accept == false) {
                ClanService.Notify(invite.From, "$clans_notice_invite_declined", request.Sender.CharacterName);
                request.Ok();
                return;
            }
            if (invite.Kind == InviteKind.Raid) {
                AcceptRaidInvite(request, invite);
                return;
            }

            if (parties.TryGetValue(invite.PartyId, out Party party) == false) { request.Fail("$clans_err_invite_gone"); return; }
            if (PartyOf(request.Sender.Account) != null) { request.Fail("$clans_err_in_party"); return; }
            if (party.Members.Count >= ClansConfig.MaxPartySize.Value) { request.Fail("$clans_err_party_full", ClansConfig.MaxPartySize.Value.ToString()); return; }
            Join(party, request.Sender);
            request.Ok();
        }

        private static void Join(Party party, ClanSender joiner) {
            party.Members.Add(joiner.Account);
            partyOf[joiner.Account] = party.Id;
            foreach (Invite other in invites.Values.Where(i => i.To == joiner.Account && i.Kind == InviteKind.Party).ToList()) {
                CloseInvite(other, "$clans_invite_closed_joined");
            }
            NotifyParty(party, joiner.Account, "$clans_notice_party_joined", joiner.CharacterName);
            MarkParty(party);
            ApiEvents.PartyJoined(party.Id, joiner.Account);
        }

        private static void OnLeave(ServerRequest request) {
            if (PartyOf(request.Sender.Account) == null) { request.Fail("$clans_err_not_in_party"); return; }
            RemoveFromParty(request.Sender.Account, "$clans_notice_party_left", "left");
            request.Ok();
        }

        private static void OnKick(ServerRequest request) {
            string target = request.Body.ReadString();
            Party party = PartyOf(request.Sender.Account);
            if (party == null) { request.Fail("$clans_err_not_in_party"); return; }
            if (party.Leader != request.Sender.Account) { request.Fail("$clans_err_party_leader_only"); return; }
            if (target == request.Sender.Account || party.Members.Contains(target) == false) { request.Fail("$clans_err_not_in_your_party"); return; }
            RemoveFromParty(target, "$clans_notice_party_kicked", "kicked");
            ClanService.Notify(target, "$clans_notice_you_were_kicked_party");
            request.Ok();
        }

        private static void OnSetLeader(ServerRequest request) {
            string target = request.Body.ReadString();
            Party party = PartyOf(request.Sender.Account);
            if (party == null) { request.Fail("$clans_err_not_in_party"); return; }
            if (party.Leader != request.Sender.Account) { request.Fail("$clans_err_party_leader_only"); return; }
            if (party.Members.Contains(target) == false) { request.Fail("$clans_err_not_in_your_party"); return; }
            party.Leader = target;
            ApiEvents.PartyLeaderChanged(party.Id, target);
            Raid raid = RaidOf(party);
            if (raid != null && raid.Leader == request.Sender.Account) {
                raid.Leader = target;
                ApiEvents.RaidLeaderChanged(raid.Id, target);
                MarkRaid(raid);
            }
            NotifyParty(party, null, "$clans_notice_party_leader", NameOf(target));
            MarkParty(party);
            request.Ok();
        }

        // Takes a player out of their party, handing on leadership and dissolving what becomes empty. reason, for
        // other mods (Clans.API): "left", "kicked" or "timedOut".
        internal static void RemoveFromParty(string account, string noticeToken, string reason) {
            Party party = PartyOf(account);
            if (party == null) { return; }
            party.Members.Remove(account);
            partyOf.Remove(account);
            offlineSince.Remove(account);
            ClanSender session = Sessions.Get(account);
            if (session != null) { SendState(account); }
            Broadcaster.MarkRoster();
            ApiEvents.PartyLeft(party.Id, account, reason);

            if (party.Members.Count == 0) {
                Disband(party);
                return;
            }
            if (party.Leader == account) {
                party.Leader = party.Members.FirstOrDefault(IsOnline) ?? party.Members[0];
                ApiEvents.PartyLeaderChanged(party.Id, party.Leader);
            }
            // The raid leader need not lead their own squad: whoever leaves, the raid keeps a leader. It passes
            // to this squad's leader, or another squad leader who is online.
            Raid raid = RaidOf(party);
            if (raid != null && raid.Leader == account) {
                List<string> squadLeaders = raid.Parties.Select(id => parties.TryGetValue(id, out Party p) ? p.Leader : null).Where(l => l != null).ToList();
                raid.Leader = IsOnline(party.Leader) ? party.Leader : squadLeaders.FirstOrDefault(IsOnline) ?? party.Leader;
                ApiEvents.RaidLeaderChanged(raid.Id, raid.Leader);
                MarkRaid(raid);
            }
            // A party of one is not much of a party, but it stays: its leader may be about to invite.
            NotifyParty(party, null, noticeToken, NameOf(account));
            MarkParty(party);
        }

        private static void Disband(Party party) {
            parties.Remove(party.Id);
            foreach (Invite invite in invites.Values.Where(i => i.PartyId == party.Id).ToList()) {
                CloseInvite(invite, "$clans_invite_closed_gone");
            }
            Raid raid = RaidOf(party);
            if (raid != null) { RemoveFromRaid(raid, party); }
            Broadcaster.MarkRoster();
            ApiEvents.PartyDisbanded(party.Id);
        }

        // ---------------------------------------------------------------- raids

        // A party leader invites another party's leader; their whole party joins as a new squad.
        private static void OnInviteRaid(ServerRequest request) {
            string target = request.Body.ReadString();
            if (CheckEnabled(request) == false) { return; }
            Party mine = PartyOf(request.Sender.Account);
            if (mine == null || mine.Leader != request.Sender.Account) { request.Fail("$clans_err_party_leader_only"); return; }
            Party theirs = PartyOf(target);
            ClanSender targetSession = Sessions.Get(target);
            if (theirs == null || theirs.Leader != target || targetSession == null) { request.Fail("$clans_err_target_not_leader"); return; }
            if (theirs.Id == mine.Id) { request.Fail("$clans_err_bad_request"); return; }
            if (theirs.RaidId != 0) { request.Fail("$clans_err_target_in_raid", targetSession.CharacterName); return; }

            Raid raid = RaidOf(mine);
            if (raid != null && raid.Leader != request.Sender.Account) { request.Fail("$clans_err_raid_leader_only"); return; }
            int squads = raid?.Parties.Count ?? 1;
            if (squads >= ClansConfig.MaxSquadsPerRaid.Value) { request.Fail("$clans_err_raid_full", ClansConfig.MaxSquadsPerRaid.Value.ToString()); return; }

            Invite invite = new Invite {
                Id = nextId++,
                Kind = InviteKind.Raid,
                From = request.Sender.Account,
                FromName = request.Sender.CharacterName,
                To = target,
                PartyId = mine.Id,
                Expires = Time.unscaledTime + ClansConfig.InviteExpirySeconds.Value,
            };
            invites[invite.Id] = invite;
            SendInvite(invite, targetSession, mine);
            request.Ok();
        }

        private static void AcceptRaidInvite(ServerRequest request, Invite invite) {
            Party theirs = PartyOf(request.Sender.Account);
            if (theirs == null || theirs.Leader != request.Sender.Account) { request.Fail("$clans_err_party_leader_only"); return; }
            if (theirs.RaidId != 0) { request.Fail("$clans_err_in_raid"); return; }
            if (parties.TryGetValue(invite.PartyId, out Party inviting) == false) { request.Fail("$clans_err_invite_gone"); return; }
            // The invite holds only while its sender still leads that party, and its raid if it has one.
            Raid current = RaidOf(inviting);
            if (inviting.Leader != invite.From || (current != null && current.Leader != invite.From)) { request.Fail("$clans_err_invite_gone"); return; }
            if (inviting.Id == theirs.Id) { request.Fail("$clans_err_bad_request"); return; }

            Raid raid = current;
            if (raid == null) {
                raid = new Raid { Id = nextId++, Leader = inviting.Leader };
                raid.Parties.Add(inviting.Id);
                raids[raid.Id] = raid;
                inviting.RaidId = raid.Id;
                ApiEvents.RaidJoined(raid.Id, inviting.Id, 1);
            }
            if (raid.Parties.Count >= ClansConfig.MaxSquadsPerRaid.Value) { request.Fail("$clans_err_raid_full", ClansConfig.MaxSquadsPerRaid.Value.ToString()); return; }
            raid.Parties.Add(theirs.Id);
            theirs.RaidId = raid.Id;
            ApiEvents.RaidJoined(raid.Id, theirs.Id, raid.Parties.Count);
            NotifyRaid(raid, null, "$clans_notice_raid_joined", request.Sender.CharacterName);
            MarkRaid(raid);
            request.Ok();
        }

        private static void OnLeaveRaid(ServerRequest request) {
            Party party = PartyOf(request.Sender.Account);
            Raid raid = RaidOf(party);
            if (raid == null) { request.Fail("$clans_err_not_in_raid"); return; }
            if (party.Leader != request.Sender.Account) { request.Fail("$clans_err_party_leader_only"); return; }
            NotifyRaid(raid, null, "$clans_notice_raid_left", request.Sender.CharacterName);
            RemoveFromRaid(raid, party);
            request.Ok();
        }

        private static void RemoveFromRaid(Raid raid, Party party) {
            raid.Parties.Remove(party.Id);
            party.RaidId = 0;
            MarkParty(party);
            ApiEvents.RaidLeft(raid.Id, party.Id);
            if (raid.Parties.Count <= 1) {
                // One squad left is just a party again.
                foreach (long id in raid.Parties) {
                    if (parties.TryGetValue(id, out Party rest)) {
                        rest.RaidId = 0;
                        MarkParty(rest);
                        ApiEvents.RaidLeft(raid.Id, rest.Id);
                    }
                }
                raids.Remove(raid.Id);
                ApiEvents.RaidDisbanded(raid.Id);
            } else {
                if (party.Members.Contains(raid.Leader) || PartyOf(raid.Leader)?.RaidId != raid.Id) {
                    raid.Leader = parties[raid.Parties[0]].Leader;
                    ApiEvents.RaidLeaderChanged(raid.Id, raid.Leader);
                }
                MarkRaid(raid);
            }
            Broadcaster.MarkRoster();
        }

        // The raid leader moves a member into another squad that has room.
        private static void OnMoveToSquad(ServerRequest request) {
            string target = request.Body.ReadString();
            int squad = request.Body.ReadInt();
            Party mine = PartyOf(request.Sender.Account);
            Raid raid = RaidOf(mine);
            if (raid == null) { request.Fail("$clans_err_not_in_raid"); return; }
            if (raid.Leader != request.Sender.Account) { request.Fail("$clans_err_raid_leader_only"); return; }
            Party from = PartyOf(target);
            if (from == null || from.RaidId != raid.Id || squad < 1 || squad > raid.Parties.Count) { request.Fail("$clans_err_bad_request"); return; }
            Party to = parties[raid.Parties[squad - 1]];
            if (to.Id == from.Id) { request.Ok(); return; }
            if (to.Members.Count >= ClansConfig.MaxPartySize.Value) { request.Fail("$clans_err_party_full", ClansConfig.MaxPartySize.Value.ToString()); return; }

            from.Members.Remove(target);
            to.Members.Add(target);
            partyOf[target] = to.Id;
            ApiEvents.PartyLeft(from.Id, target, "moved");
            ApiEvents.PartyJoined(to.Id, target);
            if (from.Leader == target && from.Members.Count > 0) {
                from.Leader = from.Members[0];
                ApiEvents.PartyLeaderChanged(from.Id, from.Leader);
            }
            if (from.Members.Count == 0) { Disband(from); } else { MarkParty(from); }
            MarkRaid(raid);
            Broadcaster.MarkRoster();
            request.Ok();
        }

        private static void OnSetRaidLeader(ServerRequest request) {
            string target = request.Body.ReadString();
            Party mine = PartyOf(request.Sender.Account);
            Raid raid = RaidOf(mine);
            if (raid == null) { request.Fail("$clans_err_not_in_raid"); return; }
            if (raid.Leader != request.Sender.Account) { request.Fail("$clans_err_raid_leader_only"); return; }
            if (PartyOf(target)?.RaidId != raid.Id) { request.Fail("$clans_err_bad_request"); return; }
            raid.Leader = target;
            ApiEvents.RaidLeaderChanged(raid.Id, target);
            NotifyRaid(raid, null, "$clans_notice_raid_leader", NameOf(target));
            MarkRaid(raid);
            request.Ok();
        }

        // ---------------------------------------------------------------- state pushes

        private static void MarkParty(Party party) {
            if (party == null) { return; }
            Raid raid = RaidOf(party);
            if (raid != null) { MarkRaid(raid); return; }
            dirtyParties.Add(party.Id);
            Broadcaster.MarkRoster();
        }

        // Every squad in a raid sees the whole raid, so a change anywhere refreshes all of them.
        private static void MarkRaid(Raid raid) {
            foreach (long id in raid.Parties) { dirtyParties.Add(id); }
            Broadcaster.MarkRoster();
        }

        private static void PushParty(long id) {
            if (parties.TryGetValue(id, out Party party) == false) { return; }
            foreach (string account in party.Members) { SendState(account); }
        }

        // The player's party and raid as they see it; an empty state when they have none.
        internal static void SendState(string account) {
            ClanSender to = Sessions.Get(account);
            if (to == null) { return; }
            Party party = PartyOf(account);
            Raid raid = RaidOf(party);
            ClanNet.Send(to, Op.PartyState, 0, pkg => {
                pkg.Write(party?.Id ?? 0L);
                pkg.Write(raid?.Id ?? 0L);
                pkg.Write(raid?.Leader ?? "");
                List<Party> squads = raid == null
                    ? (party == null ? new List<Party>() : new List<Party> { party })
                    : raid.Parties.Select(pid => parties.TryGetValue(pid, out Party p) ? p : null).Where(p => p != null).ToList();
                pkg.Write(squads.Count);
                foreach (Party squad in squads) {
                    pkg.Write(squad.Id);
                    pkg.Write(squad.Leader ?? "");
                    pkg.Write(squad.Members.Count);
                    foreach (string member in squad.Members) {
                        pkg.Write(member);
                        pkg.Write(NameOf(member));
                        pkg.Write(IsOnline(member));
                        pkg.Write(ClanRules.Account(member)?.ClanId ?? 0L);
                    }
                }
            });
        }

        private static string NameOf(string account) {
            ClanSender session = Sessions.Get(account);
            if (session != null) { return session.CharacterName; }
            return ClanRules.Account(account)?.LastCharacterName ?? account;
        }

        private static void NotifyParty(Party party, string except, string token, params string[] args) {
            if (string.IsNullOrEmpty(token)) { return; }
            foreach (string member in party.Members) {
                if (member != except) { ClanService.Notify(member, token, args); }
            }
        }

        private static void NotifyRaid(Raid raid, string except, string token, params string[] args) {
            foreach (long id in raid.Parties) {
                if (parties.TryGetValue(id, out Party party)) { NotifyParty(party, except, token, args); }
            }
        }
    }
}
