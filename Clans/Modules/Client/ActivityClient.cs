using Clans.Core;
using Clans.Net;
using Clans.Server.Model;
using HarmonyLib;
using System;
using System.Collections.Generic;

namespace Clans.Client {
    // Every clan's running activities as the server last described them, for the Activities page, the map,
    // the totems and the hunt kill reports.
    internal static class ActivityClient {
        internal static readonly List<ActivityInfo> Active = new List<ActivityInfo>();
        // Creature prefabs any running hunt is after, so creature owners only report deaths that matter.
        internal static readonly HashSet<string> HuntTargets = new HashSet<string>();
        internal static long PeriodStartUtc { get; private set; }
        internal static long PeriodEndUtc { get; private set; }
        // True once the server has sent the list this session, so an activity missing from it is really over.
        internal static bool Received { get; private set; }

        internal static event Action Changed;

        internal static void Init() {
            ClanNet.RegisterClient(Op.ActivityState, OnState);
        }

        internal static void Reset() {
            Active.Clear();
            HuntTargets.Clear();
            PeriodStartUtc = 0;
            PeriodEndUtc = 0;
            Received = false;
        }

        internal static ActivityInfo Get(long id) => Active.Find(a => a.Id == id);

        internal static ActivityInfo MineOf(ActivityType type) {
            long clan = ClanClient.Me.ClanId;
            return clan == 0 ? null : Active.Find(a => a.ClanId == clan && a.Type == type);
        }

        // Whether this player's clan already started this activity this week.
        internal static bool LockedOut(ActivityType type) {
            ClanDetailInfo clan = ClanClient.MyClan;
            return clan != null && clan.Lockouts.TryGetValue(type, out long start) && start == PeriodStartUtc;
        }

        private static void OnState(uint reqId, ZPackage body) {
            PeriodStartUtc = body.ReadLong();
            PeriodEndUtc = body.ReadLong();
            WeeklyClock.DebugOffsetSeconds = body.ReadLong();
            Active.Clear();
            HuntTargets.Clear();
            int count = body.ReadInt();
            for (int i = 0; i < count; i++) {
                ActivityInfo info = ActivityInfo.Read(body);
                Active.Add(info);
                if (info.Type == ActivityType.TrophyHunt && info.State == ActivityState.Running) {
                    foreach (HuntTarget target in info.Targets) {
                        if (target.Count < target.Required) { HuntTargets.Add(target.Prefab); }
                    }
                }
            }
            Received = true;
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- hunt kills

        // Runs where the creature dies (on its owner). Reports deaths of hunted creatures with every player who
        // hurt them; the server works out which clans, if any, each counts for.
        //
        // Every player who hurt it, not only the last hit's: poison, burning and other lingering damage tick
        // with no attacker, so a creature that bleeds out after a player's blow has no player on its last hit.
        // This is the record vanilla credits kills from (Character.OnDeath, ZDOVars.s_attackers + the player's
        // name, set in RPC_Damage).
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class DeathPatch {
            private static readonly List<string> killers = new List<string>();

            private static void Prefix(Character __instance) {
                if (HuntTargets.Count == 0 || __instance is Player) { return; }
                if (__instance.m_nview == null || __instance.m_nview.IsValid() == false || __instance.m_nview.IsOwner() == false) { return; }
                string prefab = Utils.GetPrefabName(__instance.gameObject);
                if (HuntTargets.Contains(prefab) == false) { return; }

                killers.Clear();
                if (__instance.m_lastHit != null && __instance.m_lastHit.GetAttacker() is Player lastHit) { Add(PlayerMap.AccountOf(lastHit)); }
                ZDO zdo = __instance.m_nview.GetZDO();
                if (ZNet.instance != null && zdo.GetInt(ZDOVars.s_attackers) > 0) {
                    foreach (ZNet.PlayerInfo info in ZNet.instance.GetPlayerList()) {
                        if (zdo.GetBool(ZDOVars.s_attackers + info.m_name)) { Add(AccountId.ForPlayerInfo(info)); }
                    }
                }
                if (killers.Count == 0) { return; }

                ZDOID id = __instance.GetZDOID();
                UnityEngine.Vector3 position = __instance.transform.position;
                List<string> accounts = new List<string>(killers);
                ClanRequests.Report(Op.ReportKill, pkg => {
                    pkg.Write(prefab);
                    pkg.Write(id);
                    pkg.Write(position);
                    pkg.Write(accounts.Count);
                    foreach (string account in accounts) { pkg.Write(account); }
                });
            }

            private static void Add(string account) {
                if (string.IsNullOrEmpty(account) == false && killers.Contains(account) == false) { killers.Add(account); }
            }
        }
    }

    internal static class ActivityActions {
        internal static void Start(ActivityType type, int difficulty, string bossId, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.StartActivity, pkg => { pkg.Write((byte)type); pkg.Write(difficulty); pkg.Write(bossId ?? ""); }, done);

        internal static void Cancel(long id, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.CancelActivity, pkg => pkg.Write(id), done);

        internal static void Begin(long id, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.BeginActivity, pkg => pkg.Write(id), done);

        // Answered with the chest's serial number (an int in Extra).
        internal static void TakeRaceChest(long id, Action<NetResult> done) =>
            ClanRequests.Send(Op.TakeRaceChest, pkg => pkg.Write(id), done);
    }
}
