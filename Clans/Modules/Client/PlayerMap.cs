using Clans.Core;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Client {
    // Which account a Player in the world belongs to. The server's player list (ZNet.GetPlayerList) pairs each
    // player's character ZDOID with their platform id, which is all an account id is made of, so the server
    // never has to send peer ids for this.
    internal static class PlayerMap {
        private const float RefreshSeconds = 1f;

        private static readonly Dictionary<ZDOID, string> accountByCharacter = new Dictionary<ZDOID, string>();
        private static readonly Dictionary<string, ZNet.PlayerInfo> infoByAccount = new Dictionary<string, ZNet.PlayerInfo>();
        private static float nextRefresh;

        internal static string AccountOf(Player player) {
            if (player == null) { return null; }
            if (player == Player.m_localPlayer) { return ClanClient.MyAccount; }
            Refresh();
            accountByCharacter.TryGetValue(player.GetZDOID(), out string account);
            return account;
        }

        // The account of an online client, from its peer id (a routed RPC's sender). A character's ZDOID carries the
        // session id of the client that made it, which is that client's peer id.
        internal static string AccountOfPeer(long peer) {
            if (peer == ZDOMan.GetSessionID()) { return ClanClient.MyAccount; }
            Refresh();
            foreach (KeyValuePair<ZDOID, string> pair in accountByCharacter) {
                if (pair.Key.UserID == peer) { return pair.Value; }
            }
            return null;
        }

        // The Player object for an account, if that player is loaded near this client.
        internal static Player PlayerOf(string account) {
            if (string.IsNullOrEmpty(account)) { return null; }
            if (account == ClanClient.MyAccount) { return Player.m_localPlayer; }
            foreach (Player player in Player.GetAllPlayers()) {
                if (player != null && AccountOf(player) == account) { return player; }
            }
            return null;
        }

        // The server's player-list entry for an online account.
        internal static bool TryInfo(string account, out ZNet.PlayerInfo info) {
            Refresh();
            return infoByAccount.TryGetValue(account ?? "", out info);
        }

        internal static void Reset() {
            accountByCharacter.Clear();
            infoByAccount.Clear();
            nextRefresh = 0f;
        }

        private static void Refresh() {
            if (Time.unscaledTime < nextRefresh || ZNet.instance == null) { return; }
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            accountByCharacter.Clear();
            infoByAccount.Clear();
            foreach (ZNet.PlayerInfo info in ZNet.instance.GetPlayerList()) {
                string account = AccountId.ForPlayerInfo(info);
                infoByAccount[account] = info;
                if (info.m_characterID.IsNone()) { continue; }
                accountByCharacter[info.m_characterID] = account;
            }
        }
    }
}
