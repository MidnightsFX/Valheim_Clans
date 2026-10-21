using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Client {
    // Party and raid members on the map, whether or not they share their position publicly, and clanmates who
    // chose to be visible to their clan.
    //
    // The map draws named player pins from ZNet.GetOtherPublicPlayers, and nothing else calls it, so the
    // companions and clanmates the server relays are appended there as if they were public. Players already
    // listed are left as they are, so nobody shows twice. Appended in a stable order: the map matches pins
    // to the list by index.
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.GetOtherPublicPlayers))]
    internal static class MapPins {
        private static void Postfix(ZNet __instance, List<ZNet.PlayerInfo> playerList) {
            foreach (PartyClient.Companion companion in PartyClient.Companions) {
                Append(__instance, playerList, companion.Account, companion.Position);
            }
            if (ClanMap.ClanmatesCurrent == false) { return; }
            foreach (ClanMap.Clanmate clanmate in ClanMap.Clanmates) {
                Append(__instance, playerList, clanmate.Account, clanmate.Position);
            }
        }

        private static void Append(ZNet net, List<ZNet.PlayerInfo> playerList, string account, Vector3 position) {
            if (PlayerMap.TryInfo(account, out ZNet.PlayerInfo info) == false) { return; }
            if (info.m_characterID.IsNone() || info.m_characterID == net.m_characterID) { return; }
            if (AlreadyListed(playerList, info.m_characterID)) { return; }
            info.m_publicPosition = true;
            info.m_position = position;
            playerList.Add(info);
        }

        private static bool AlreadyListed(List<ZNet.PlayerInfo> list, ZDOID id) {
            for (int i = 0; i < list.Count; i++) {
                if (list[i].m_characterID == id) { return true; }
            }
            return false;
        }
    }
}
