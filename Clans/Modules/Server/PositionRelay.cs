using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Server {
    // Tells party and raid members where their companions are and how hurt they are, a few times a
    // second, for the map pins and the party frame. Also tells clan members where the clanmates are who
    // chose to be visible to their clan, for the map pins.
    //
    // Positions are the reference positions every client reports to the server whether or not they share
    // their position on the map, so companions are visible to each other without making anyone public.
    // Health comes from each player's own ZDO, which the server always holds.
    internal static class PositionRelay {
        private static float timer;

        // Accounts that chose to be visible to their clan. Not saved: each client says so when it logs in.
        private static readonly HashSet<string> visibleToClan = new HashSet<string>();
        // Who was sent clanmates last time, so anyone who stops getting them is told once that none are left.
        private static HashSet<string> clanRecipients = new HashSet<string>();

        private struct Status {
            internal Vector3 Position;
            internal float Health;
        }

        internal static void Register() {
            ClanNet.RegisterServer(Op.MapSharing, OnMapSharing);
        }

        internal static void Reset() {
            timer = 0f;
            visibleToClan.Clear();
            clanRecipients.Clear();
        }

        internal static void OnAccountOffline(string account) {
            visibleToClan.Remove(account);
            clanRecipients.Remove(account);
        }

        internal static void Tick(float dt) {
            timer -= dt;
            if (timer > 0f) { return; }
            timer = Mathf.Max(0.5f, ClansConfig.PositionRelaySeconds.Value);
            if (PartyService.Enabled) { RelayParties(); }
            RelayClans();
        }

        private static void RelayParties() {
            Dictionary<string, Status> status = new Dictionary<string, Status>();
            HashSet<long> sentRaids = new HashSet<long>();
            foreach (PartyService.Party party in PartyService.AllParties) {
                // A raid is sent once, to every squad in it.
                if (party.RaidId != 0 && sentRaids.Add(party.RaidId) == false) { continue; }
                List<string> group = new List<string>(PartyService.Companions(party));
                if (group.Count < 2) { continue; }
                foreach (string account in group) {
                    if (status.ContainsKey(account) == false && TryStatus(account, out Status s)) { status[account] = s; }
                }
                foreach (string account in group) {
                    ClanSender to = Sessions.Get(account);
                    if (to == null) { continue; }
                    List<string> others = group.FindAll(a => a != account && status.ContainsKey(a));
                    if (others.Count == 0) { continue; }
                    ClanNet.Send(to, Op.PartyPositions, 0, pkg => {
                        pkg.Write(others.Count);
                        foreach (string other in others) {
                            Status s = status[other];
                            pkg.Write(other);
                            pkg.Write(s.Position);
                            pkg.Write(s.Health);
                        }
                    });
                }
            }
        }

        // Each online member of a clan gets every other member visible to the clan, as a whole list that
        // replaces the last one.
        private static void RelayClans() {
            HashSet<string> sent = new HashSet<string>();
            if (ClansConfig.ClanPositions.Value && visibleToClan.Count > 0) {
                Dictionary<long, List<string>> shownByClan = new Dictionary<long, List<string>>();
                foreach (string account in visibleToClan) {
                    ClanRecord clan = Sessions.IsOnline(account) ? ClanRules.ClanOf(account) : null;
                    if (clan == null) { continue; }
                    if (shownByClan.TryGetValue(clan.Id, out List<string> shown) == false) {
                        shown = new List<string>();
                        shownByClan[clan.Id] = shown;
                    }
                    shown.Add(account);
                }
                foreach (KeyValuePair<long, List<string>> pair in shownByClan) {
                    ClanRecord clan = ClanRules.Clan(pair.Key);
                    if (clan == null) { continue; }
                    List<(string Account, Vector3 Position)> shown = new List<(string, Vector3)>();
                    foreach (string account in pair.Value) {
                        ClanSender session = Sessions.Get(account);
                        if (session != null) { shown.Add((account, session.Position)); }
                    }
                    foreach (string member in clan.Members.Keys) {
                        ClanSender to = Sessions.Get(member);
                        if (to == null) { continue; }
                        List<(string Account, Vector3 Position)> others = shown.FindAll(s => s.Account != member);
                        if (others.Count == 0) { continue; }
                        SendClanmates(to, others);
                        sent.Add(member);
                    }
                }
            }
            foreach (string account in clanRecipients) {
                if (sent.Contains(account)) { continue; }
                SendClanmates(Sessions.Get(account), new List<(string Account, Vector3 Position)>());
            }
            clanRecipients = sent;
        }

        private static void SendClanmates(ClanSender to, List<(string Account, Vector3 Position)> clanmates) {
            if (to == null) { return; }
            ClanNet.Send(to, Op.ClanPositions, 0, pkg => {
                pkg.Write(clanmates.Count);
                foreach ((string account, Vector3 position) in clanmates) {
                    pkg.Write(account);
                    pkg.Write(position);
                }
            });
        }

        private static void OnMapSharing(ServerRequest request) {
            bool visible = request.Body.ReadBool();
            string account = request.Sender.Account;
            // A report from a player the server does not count as online would never be cleared.
            if (visible && Sessions.IsOnline(account)) { visibleToClan.Add(account); } else { visibleToClan.Remove(account); }
        }

        private static bool TryStatus(string account, out Status status) {
            status = default;
            ClanSender session = Sessions.Get(account);
            if (session == null) { return false; }
            status.Position = session.Position;
            status.Health = 1f;
            if (session.IsLocalHost) {
                if (Player.m_localPlayer != null) { status.Health = Player.m_localPlayer.GetHealthPercentage(); }
                return true;
            }
            ZDO zdo = session.Peer.m_characterID.IsNone() ? null : ZDOMan.instance.GetZDO(session.Peer.m_characterID);
            if (zdo != null) {
                float health = zdo.GetFloat(ZDOVars.s_health, 0f);
                float max = zdo.GetFloat(ZDOVars.s_maxHealth, 0f);
                if (max > 0f) { status.Health = Mathf.Clamp01(health / max); }
                if (health <= 0f && max > 0f) { status.Health = 0f; }
            }
            return true;
        }
    }
}
