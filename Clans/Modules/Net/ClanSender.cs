using Clans.Core;
using UnityEngine;

namespace Clans.Net {
    // Who a request came from, as the server sees it. Built from the connection the request arrived on, so
    // a client cannot claim to be anyone else. The listen host and single player have no ZNetPeer for
    // themselves, so their sender is synthesized from the local user (Peer is null).
    internal sealed class ClanSender {
        internal readonly long PeerUid;
        internal readonly string Account;
        internal readonly string CharacterName;
        internal readonly ZNetPeer Peer;

        private ClanSender(long peerUid, string account, string characterName, ZNetPeer peer) {
            PeerUid = peerUid;
            Account = account;
            CharacterName = characterName ?? "";
            Peer = peer;
        }

        internal bool IsLocalHost => Peer == null;

        // Server-known position. For a peer this is the reference position the client reports every two
        // seconds, which the server has whether or not the player shares their position on the map.
        internal Vector3 Position {
            get {
                if (Peer != null) { return Peer.m_refPos; }
                return Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            }
        }

        // The character's player id (Player.GetPlayerID), read from its networked object, which the server
        // has for every connected player; 0 before the character has spawned.
        internal long PlayerId {
            get {
                if (Peer == null) { return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L; }
                if (Peer.m_characterID.IsNone() || ZDOMan.instance == null) { return 0L; }
                ZDO zdo = ZDOMan.instance.GetZDO(Peer.m_characterID);
                return zdo != null ? zdo.GetLong(ZDOVars.s_playerID, 0L) : 0L;
            }
        }

        internal static ClanSender FromPeer(ZNetPeer peer) {
            return new ClanSender(peer.m_uid, AccountId.ForPeer(peer), peer.m_playerName, peer);
        }

        internal static ClanSender LocalHost() {
            string name = Game.instance != null && Game.instance.GetPlayerProfile() != null
                ? Game.instance.GetPlayerProfile().GetName() : "";
            return new ClanSender(ZDOMan.GetSessionID(), AccountId.ForLocalUser(name), name, null);
        }

        public override string ToString() => $"{CharacterName} ({Account})";
    }
}
