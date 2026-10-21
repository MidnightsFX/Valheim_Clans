using Clans.Net;
using Clans.Settings;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Client {
    // Map pings and map positions for the clan alone, each switched by the player on the large map (and in the
    // Client config) while the server allows it.
    //
    // A clan ping is vanilla's ping, sent to this player and to each online clanmate instead of to everyone,
    // so it looks and fades exactly like any other. Being visible to the clan is told to the server, which
    // passes this player's position on to their clanmates; MapPins shows the clanmates it receives.
    internal static class ClanMap {
        // A list older than this is ignored: the server stopped sending (or this player's clan changed).
        private const float PositionLifetime = 12f;

        internal struct Clanmate {
            internal string Account;
            internal Vector3 Position;
        }

        // Sorted by account, so map pins keep a stable order.
        internal static readonly List<Clanmate> Clanmates = new List<Clanmate>();
        private static float receivedAt = -PositionLifetime;

        internal static bool PingsAvailable => ClansConfig.ClanPings.Value && ClanClient.InClan;
        internal static bool PositionsAvailable => ClansConfig.ClanPositions.Value && ClanClient.InClan;

        internal static bool ClanmatesCurrent => Clanmates.Count > 0 && ClanClient.InClan && Time.unscaledTime - receivedAt <= PositionLifetime;

        internal static void Init() {
            ClanNet.RegisterClient(Op.ClanPositions, OnPositions);
            ClansConfig.VisibleToClan.SettingChanged += (_, _) => ReportSharing();
        }

        internal static void Reset() {
            Clanmates.Clear();
            receivedAt = -PositionLifetime;
        }

        // Sent on login and whenever the switch changes; the server forgets it when the player leaves.
        internal static void ReportSharing() {
            if (ClanClient.Welcomed == false) { return; }
            ClanRequests.Report(Op.MapSharing, pkg => pkg.Write(ClansConfig.VisibleToClan.Value));
        }

        private static void OnPositions(uint reqId, ZPackage body) {
            Clanmates.Clear();
            int count = body.ReadInt();
            for (int i = 0; i < count; i++) {
                Clanmates.Add(new Clanmate { Account = body.ReadString(), Position = body.ReadVector3() });
            }
            Clanmates.Sort((a, b) => string.CompareOrdinal(a.Account, b.Account));
            receivedAt = Time.unscaledTime;
        }

        // The routed-RPC ids of the online clanmates. A character's ZDOID carries the session id of the client
        // that made it, which is that client's peer id (vanilla's chat relies on the same).
        private static List<long> ClanmatePeers() {
            List<long> peers = new List<long>();
            long clanId = ClanClient.Me.ClanId;
            foreach (RosterEntry entry in ClanClient.Roster.Values) {
                if (entry.ClanId != clanId || entry.Account == ClanClient.MyAccount) { continue; }
                if (PlayerMap.TryInfo(entry.Account, out ZNet.PlayerInfo info) == false || info.m_characterID.IsNone()) { continue; }
                long peer = info.m_characterID.UserID;
                if (peers.Contains(peer) == false) { peers.Add(peer); }
            }
            return peers;
        }

        [HarmonyPatch(typeof(Chat), nameof(Chat.SendPing))]
        private static class PingPatch {
            private static bool Prefix(Vector3 position) {
                if (ClansConfig.PingClanOnly.Value == false || PingsAvailable == false) { return true; }
                Player player = Player.m_localPlayer;
                if (player == null || ZRoutedRpc.instance == null) { return true; }
                // As vanilla's SendPing, but addressed to each clanmate and this player instead of everybody.
                Vector3 at = position;
                at.y = player.transform.position.y;
                UserInfo user = UserInfo.GetLocalUser();
                ZRoutedRpc.instance.InvokeRoutedRPC(ZDOMan.GetSessionID(), "ChatMessage", at, (int)Talker.Type.Ping, user, "");
                foreach (long peer in ClanmatePeers()) {
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer, "ChatMessage", at, (int)Talker.Type.Ping, user, "");
                }
                return false;
            }
        }
    }
}
