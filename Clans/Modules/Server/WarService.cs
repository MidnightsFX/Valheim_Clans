using Clans.Core;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans.Server {
    // Wars between clans. One clan declares, the other accepts or declines; a war then lasts until either side
    // accepts the other's offer of peace, its time runs out, or (when the declaration set one) one side reaches
    // the kill target. While a war is active, members of the two clans can hurt each other whatever their PvP
    // setting (see Client/Hostility).
    internal static class WarService {
        private const int HistoryKept = 100;
        // A death only counts when the killer was this close, by the positions the server knows.
        private const float KillReportRange = 150f;
        // The same killer and victim count at most once in this window, against repeated reports.
        private const float KillDedupeSeconds = 10f;

        private static readonly Dictionary<string, float> recentKills = new Dictionary<string, float>();
        // The character each victim last reported dying as: a character dies once, and a new life is a new
        // character, so one life cannot be reported as many deaths.
        private static readonly Dictionary<string, ZDOID> lastDeath = new Dictionary<string, ZDOID>();

        internal static void Register() {
            ClanNet.RegisterServer(Op.ProposeWar, OnPropose);
            ClanNet.RegisterServer(Op.RespondWar, OnRespond);
            ClanNet.RegisterServer(Op.CancelWarProposal, OnCancel);
            ClanNet.RegisterServer(Op.OfferPeace, OnOfferPeace);
            ClanNet.RegisterServer(Op.RespondPeace, OnRespondPeace);
            ClanNet.RegisterServer(Op.ReportPvpDeath, OnPvpDeath);
        }

        internal static void Reset() {
            recentKills.Clear();
            lastDeath.Clear();
        }

        private static ClansWorldState State => ClanStore.State;

        internal static WarRecord Between(long a, long b) {
            return State.Wars.FirstOrDefault(w => w.State != WarState.Ended && w.Involves(a) && w.Involves(b));
        }

        private static WarRecord Find(long id) => State.Wars.FirstOrDefault(w => w.Id == id);

        // The sender's clan, when their rank may make war and peace.
        private static ClanRecord WarClan(ServerRequest request) {
            if (ClansConfig.EnableWars.Value == false) { request.Fail("$clans_err_wars_off"); return null; }
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return null; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.ManageWar) == false) { request.Fail("$clans_err_no_permission"); return null; }
            return clan;
        }

        // ---------------------------------------------------------------- requests

        private static void OnPropose(ServerRequest request) {
            long targetId = request.Body.ReadLong();
            int killTarget = request.Body.ReadInt();
            ClanRecord clan = WarClan(request);
            if (clan == null) { return; }
            ClanRecord target = ClanRules.Clan(targetId);
            if (target == null || target.Id == clan.Id) { request.Fail("$clans_err_no_such_clan"); return; }
            if (Between(clan.Id, target.Id) != null) { request.Fail("$clans_err_war_exists", target.Name); return; }

            long now = ClanStore.Now;
            long cooldown = ClansConfig.WarProposalCooldownHours.Value * 3600L;
            WarRecord last = State.WarHistory.LastOrDefault(w => w.Involves(clan.Id) && w.Involves(target.Id));
            if (last != null && now - last.EndedUtc < cooldown) {
                request.Fail("$clans_err_war_cooldown", Hours(cooldown - (now - last.EndedUtc)));
                return;
            }

            int max = ClansConfig.WarKillTargetMax.Value;
            if (max <= 0) {
                killTarget = 0;
            } else if (killTarget != 0) {
                killTarget = Mathf.Clamp(killTarget, ClansConfig.WarKillTargetMin.Value, max);
            }

            WarRecord war = new WarRecord {
                Id = State.NextWarId++,
                ClanA = clan.Id,
                ClanB = target.Id,
                State = WarState.Proposed,
                ProposedBy = request.Sender.Account,
                ProposedUtc = now,
                KillTarget = killTarget,
            };
            State.Wars.Add(war);
            ClanRules.Log(clan, "$clans_log_war_declared", request.Sender.CharacterName, target.Name);
            ClanRules.Log(target, "$clans_log_war_received", clan.Name);
            NotifyManagers(target, "$clans_notice_war_declared", clan.Name);
            Changed();
            request.Ok();
        }

        private static void OnRespond(ServerRequest request) {
            long id = request.Body.ReadLong();
            bool accept = request.Body.ReadBool();
            ClanRecord clan = WarClan(request);
            if (clan == null) { return; }
            WarRecord war = Find(id);
            if (war == null || war.State != WarState.Proposed || war.ClanB != clan.Id) { request.Fail("$clans_err_no_such_war"); return; }
            ClanRecord other = ClanRules.Clan(war.ClanA);
            if (accept == false) {
                End(war, WarEndReason.Declined, 0);
                ClanRules.Log(clan, "$clans_log_war_declined_us", request.Sender.CharacterName, other?.Name ?? "");
                if (other != null) { ClanRules.Log(other, "$clans_log_war_declined_them", clan.Name); }
                request.Ok();
                return;
            }
            long now = ClanStore.Now;
            war.State = WarState.Active;
            war.StartedUtc = now;
            war.EndsUtc = now + ClansConfig.WarDurationHours.Value * 3600L;
            ClanRules.Log(clan, "$clans_log_war_started", other?.Name ?? "");
            if (other != null) {
                ClanRules.Log(other, "$clans_log_war_started", clan.Name);
                ClanService.NotifyClan(other, null, "$clans_notice_war_started", clan.Name);
            }
            ClanService.NotifyClan(clan, null, "$clans_notice_war_started", other?.Name ?? "");
            Changed();
            request.Ok();
        }

        private static void OnCancel(ServerRequest request) {
            long id = request.Body.ReadLong();
            ClanRecord clan = WarClan(request);
            if (clan == null) { return; }
            WarRecord war = Find(id);
            if (war == null || war.State != WarState.Proposed || war.ClanA != clan.Id) { request.Fail("$clans_err_no_such_war"); return; }
            End(war, WarEndReason.Cancelled, 0);
            ClanRecord other = ClanRules.Clan(war.ClanB);
            ClanRules.Log(clan, "$clans_log_war_cancelled", request.Sender.CharacterName, other?.Name ?? "");
            request.Ok();
        }

        private static void OnOfferPeace(ServerRequest request) {
            long id = request.Body.ReadLong();
            ClanRecord clan = WarClan(request);
            if (clan == null) { return; }
            WarRecord war = Find(id);
            if (war == null || war.State != WarState.Active || war.Involves(clan.Id) == false) { request.Fail("$clans_err_no_such_war"); return; }
            war.PeaceOfferedBy = clan.Id;
            ClanRecord other = ClanRules.Clan(war.Other(clan.Id));
            ClanRules.Log(clan, "$clans_log_peace_offered", request.Sender.CharacterName, other?.Name ?? "");
            if (other != null) {
                ClanRules.Log(other, "$clans_log_peace_received", clan.Name);
                NotifyManagers(other, "$clans_notice_peace_offered", clan.Name);
            }
            Changed();
            request.Ok();
        }

        private static void OnRespondPeace(ServerRequest request) {
            long id = request.Body.ReadLong();
            bool accept = request.Body.ReadBool();
            ClanRecord clan = WarClan(request);
            if (clan == null) { return; }
            WarRecord war = Find(id);
            if (war == null || war.State != WarState.Active || war.Involves(clan.Id) == false
                || war.PeaceOfferedBy == 0 || war.PeaceOfferedBy == clan.Id) {
                request.Fail("$clans_err_no_peace_offer");
                return;
            }
            ClanRecord other = ClanRules.Clan(war.Other(clan.Id));
            if (accept == false) {
                war.PeaceOfferedBy = 0;
                if (other != null) { ClanRules.Log(other, "$clans_log_peace_rejected", clan.Name); }
                Changed();
                request.Ok();
                return;
            }
            End(war, WarEndReason.Peace, 0);
            ClanRules.Log(clan, "$clans_log_peace", other?.Name ?? "");
            if (other != null) {
                ClanRules.Log(other, "$clans_log_peace", clan.Name);
                ClanService.NotifyClan(other, null, "$clans_notice_peace", clan.Name);
            }
            ClanService.NotifyClan(clan, null, "$clans_notice_peace", other?.Name ?? "");
            request.Ok();
        }

        // The victim's client reports who killed them; the server checks the war and the distance.
        private static void OnPvpDeath(ServerRequest request) {
            string attackerAccount = request.Body.ReadString();
            ClanSender victim = request.Sender;
            ClanSender attacker = Sessions.Get(attackerAccount);
            if (attacker == null || attacker.Account == victim.Account) { return; }
            ClanRecord victimClan = ClanRules.ClanOf(victim.Account);
            ClanRecord attackerClan = ClanRules.ClanOf(attacker.Account);
            if (victimClan == null || attackerClan == null) { return; }
            WarRecord war = Between(victimClan.Id, attackerClan.Id);
            if (war == null || war.State != WarState.Active) { return; }
            if (Utils.DistanceXZ(victim.Position, attacker.Position) > KillReportRange) { return; }
            if (victim.Peer != null) {
                ZDOID character = victim.Peer.m_characterID;
                if (character.IsNone() || (lastDeath.TryGetValue(victim.Account, out ZDOID previous) && previous == character)) { return; }
                lastDeath[victim.Account] = character;
            }

            string key = attacker.Account + ">" + victim.Account;
            float now = Time.unscaledTime;
            if (recentKills.TryGetValue(key, out float last) && now - last < KillDedupeSeconds) { return; }
            recentKills[key] = now;
            if (recentKills.Count > 500) {
                foreach (string stale in recentKills.Where(e => now - e.Value > KillDedupeSeconds).Select(e => e.Key).ToList()) { recentKills.Remove(stale); }
            }

            int kills;
            if (war.ClanA == attackerClan.Id) { kills = ++war.KillsA; } else { kills = ++war.KillsB; }
            ClanRules.Log(attackerClan, "$clans_log_war_kill", attacker.CharacterName, victim.CharacterName);
            ClanRules.Log(victimClan, "$clans_log_war_death", victim.CharacterName, attacker.CharacterName);
            if (war.KillTarget > 0 && kills >= war.KillTarget) {
                Win(war, attackerClan, victimClan);
                return;
            }
            Changed();
        }

        private static void Win(WarRecord war, ClanRecord winner, ClanRecord loser) {
            End(war, WarEndReason.KillTarget, winner.Id);
            winner.WarsWon++;
            ClanRules.Log(winner, "$clans_log_war_won", loser.Name);
            ClanRules.Log(loser, "$clans_log_war_lost", winner.Name);
            ClanService.NotifyClan(winner, null, "$clans_notice_war_won", loser.Name);
            ClanService.NotifyClan(loser, null, "$clans_notice_war_lost", winner.Name);
            ClanRules.AddXp(winner, ClansConfig.WarWinnerClanXp.Value, "$clans_reason_war");
        }

        // ---------------------------------------------------------------- housekeeping

        internal static void Housekeeping(long now) {
            long expiry = ClansConfig.WarProposalExpiryHours.Value * 3600L;
            foreach (WarRecord war in State.Wars.ToList()) {
                if (war.State == WarState.Proposed && now - war.ProposedUtc > expiry) {
                    End(war, WarEndReason.Expired, 0);
                    ClanRecord a = ClanRules.Clan(war.ClanA);
                    ClanRecord b = ClanRules.Clan(war.ClanB);
                    if (a != null) { ClanRules.Log(a, "$clans_log_war_unanswered", b?.Name ?? ""); }
                } else if (war.State == WarState.Active && war.EndsUtc > 0 && now >= war.EndsUtc) {
                    long winner = war.KillsA > war.KillsB ? war.ClanA : (war.KillsB > war.KillsA ? war.ClanB : 0);
                    End(war, WarEndReason.Expired, winner);
                    ClanRecord a = ClanRules.Clan(war.ClanA);
                    ClanRecord b = ClanRules.Clan(war.ClanB);
                    if (a != null) { ClanRules.Log(a, "$clans_log_war_over", b?.Name ?? "", war.KillsA.ToString(), war.KillsB.ToString()); }
                    if (b != null) { ClanRules.Log(b, "$clans_log_war_over", a?.Name ?? "", war.KillsB.ToString(), war.KillsA.ToString()); }
                }
            }
        }

        internal static void OnClanDisbanded(ClanRecord clan) {
            foreach (WarRecord war in State.Wars.Where(w => w.Involves(clan.Id) && w.State != WarState.Ended).ToList()) {
                End(war, WarEndReason.Disbanded, war.State == WarState.Active ? war.Other(clan.Id) : 0);
                ClanRecord other = ClanRules.Clan(war.Other(clan.Id));
                if (other != null) { ClanRules.Log(other, "$clans_log_war_disbanded", clan.Name); }
            }
        }

        internal static void End(WarRecord war, WarEndReason reason, long winner) {
            war.State = WarState.Ended;
            war.EndReason = reason;
            war.Winner = winner;
            war.EndedUtc = ClanStore.Now;
            war.PeaceOfferedBy = 0;
            State.Wars.Remove(war);
            State.WarHistory.Add(war);
            if (State.WarHistory.Count > HistoryKept) { State.WarHistory.RemoveRange(0, State.WarHistory.Count - HistoryKept); }
            Changed();
        }

        private static void Changed() {
            Broadcaster.MarkDirectory();
            ClanStore.MarkDirty();
        }

        private static void NotifyManagers(ClanRecord clan, string token, params string[] args) {
            foreach (MemberRecord member in clan.Members.Values) {
                if (ClanRules.Can(clan, member.Account, ClanPermission.ManageWar)) { ClanService.Notify(member.Account, token, args); }
            }
        }

        private static string Hours(long seconds) => Math.Max(1, (int)Math.Ceiling(seconds / 3600.0)).ToString();
    }
}
