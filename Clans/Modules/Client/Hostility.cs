using Clans.Net;
using Clans.Settings;
using HarmonyLib;

namespace Clans.Client {
    // Who may hurt whom between players.
    //
    // Party members and clanmates cannot hurt each other, even with PvP on. Players whose clans are at war
    // can, even with PvP off: BaseAI.IsEnemy opens the attacker's side (melee, projectiles and area attacks
    // all ask it), and the victim's side ignores the PvP flag for that hit.
    internal static class Hostility {
        internal static bool AtWar(Player a, Player b) {
            if (a == null || b == null || a == b) { return false; }
            long clanA = ClanClient.ClanIdOf(PlayerMap.AccountOf(a));
            long clanB = ClanClient.ClanIdOf(PlayerMap.AccountOf(b));
            return ClanClient.ActiveWar(clanA, clanB) != null;
        }

        internal static bool Allies(string a, string b) {
            if (a == null || b == null || a == b) { return false; }
            if (ClansConfig.BlockPartyDamage.Value) {
                RosterEntry ra = ClanClient.RosterOf(a);
                RosterEntry rb = ClanClient.RosterOf(b);
                long partyA = a == ClanClient.MyAccount ? PartyClient.PartyId : ra?.PartyId ?? 0;
                long partyB = b == ClanClient.MyAccount ? PartyClient.PartyId : rb?.PartyId ?? 0;
                if (partyA != 0 && partyA == partyB) { return true; }
            }
            if (ClansConfig.BlockClanDamage.Value) {
                long clanA = ClanClient.ClanIdOf(a);
                if (clanA != 0 && clanA == ClanClient.ClanIdOf(b)) { return true; }
            }
            return false;
        }

        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), typeof(Character), typeof(Character))]
        private static class IsEnemyPatch {
            private static void Postfix(Character a, Character b, ref bool __result) {
                if (__result || ClanClient.Wars.Count == 0) { return; }
                if (a is Player pa && b is Player pb && AtWar(pa, pb)) { __result = true; }
            }
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class DamagePatch {
            // Runs on the victim's owner.
            private static bool Prefix(Character __instance, HitData hit) {
                if (hit == null || !(__instance is Player victim)) { return true; }
                if (!(hit.GetAttacker() is Player attacker) || attacker == victim) { return true; }
                if (AtWar(attacker, victim)) {
                    hit.m_ignorePVP = true;
                    return true;
                }
                return Allies(PlayerMap.AccountOf(attacker), PlayerMap.AccountOf(victim)) == false;
            }
        }
    }
}
