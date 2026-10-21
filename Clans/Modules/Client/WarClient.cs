using Clans.Net;
using HarmonyLib;
using System;

namespace Clans.Client {
    // War requests, and the report that counts a war kill.
    internal static class WarActions {
        internal static void Declare(long clanId, int killTarget, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.ProposeWar, pkg => { pkg.Write(clanId); pkg.Write(killTarget); }, done);

        internal static void Respond(long warId, bool accept, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.RespondWar, pkg => { pkg.Write(warId); pkg.Write(accept); }, done);

        internal static void Cancel(long warId, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.CancelWarProposal, pkg => pkg.Write(warId), done);

        internal static void OfferPeace(long warId, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.OfferPeace, pkg => pkg.Write(warId), done);

        internal static void RespondPeace(long warId, bool accept, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.RespondPeace, pkg => { pkg.Write(warId); pkg.Write(accept); }, done);

        // When the local player dies to another player whose clan is at war with theirs, tells the server
        // who did it. The server checks the war and how far apart the two were before counting it.
        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class DeathPatch {
            private static void Prefix(Player __instance) {
                if (__instance != Player.m_localPlayer || __instance.m_lastHit == null) { return; }
                if (!(__instance.m_lastHit.GetAttacker() is Player killer) || killer == __instance) { return; }
                if (Hostility.AtWar(killer, __instance) == false) { return; }
                string account = PlayerMap.AccountOf(killer);
                if (account != null) { ClanRequests.Report(Op.ReportPvpDeath, pkg => pkg.Write(account)); }
            }
        }
    }
}
