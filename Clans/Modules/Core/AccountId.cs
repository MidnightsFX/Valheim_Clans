using Clans.Settings;
using Splatform;

namespace Clans.Core {
    // Clan membership belongs to an account (the player's platform id, e.g. "Steam_7656..."), not to a
    // character, so all of one person's characters share a clan and rank.
    //
    // The server derives the account from the connection, never from anything the client says, and a
    // client derives other players' accounts from ZNet.GetPlayerList(), which the server fills from the
    // same sockets. Both go through Make so they always agree.
    internal static class AccountId {
        // A backend with no platform ids (a custom socket) leaves only the character name to go on.
        private const string NamePrefix = "name:";

        internal static string Make(PlatformUserID platformId, string characterName) {
            string id = platformId.IsValid ? platformId.ToString() : NamePrefix + (characterName ?? "");
            if (ClansConfig.DebugSplitAccountsByCharacter != null && ClansConfig.DebugSplitAccountsByCharacter.Value && platformId.IsValid) {
                // Testing aid: one person, two characters, two accounts.
                id += ":" + (characterName ?? "");
            }
            return id;
        }

        // Same rule ZNet.UpdatePlayerList uses to fill PlayerInfo.m_userInfo.m_id for a peer.
        internal static PlatformUserID PlatformIdOf(ZNetPeer peer) {
            if (peer == null || peer.m_socket == null) { return PlatformUserID.None; }
            string hostName = peer.m_socket.GetHostName();
            if (string.IsNullOrEmpty(hostName)) { return PlatformUserID.None; }
            if (ZNet.m_onlineBackend == OnlineBackendType.Steamworks) {
                return new PlatformUserID(ZNet.instance.m_steamPlatform, hostName);
            }
            if (ZNet.m_onlineBackend == OnlineBackendType.PlayFab && PlatformUserID.TryParse(hostName, out PlatformUserID parsed)) {
                return parsed;
            }
            return PlatformUserID.None;
        }

        internal static string ForPeer(ZNetPeer peer) {
            return Make(PlatformIdOf(peer), peer?.m_playerName);
        }

        internal static string ForLocalUser(string characterName) {
            return Make(PlatformManager.DistributionPlatform.LocalUser.PlatformUserID, characterName);
        }

        internal static string ForPlayerInfo(ZNet.PlayerInfo info) {
            return Make(info.m_userInfo.m_id, info.m_name);
        }
    }
}
