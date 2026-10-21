using Clans.Core;
using Clans.Gameplay;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans.Server {
    // Weekly clan activities: Trophy Hunt, Race and World Boss Battles.
    //
    // Each clan may start each activity once per week (WeeklyClock). Starting places what the activity needs
    // in the world (a totem, for the Race and the World Boss) or begins it at once (Trophy Hunt). A totem
    // activity begins when a member allowed to start activities uses its totem; from then on whichever client
    // owns the totem's object runs the world side (the race destination's chests, the World Boss's waves) and
    // reports progress here, where it is checked against what the server knows. A race's chests are handed out
    // one per use of its start totem, numbered here. Deadlines are kept on the server's clock.
    internal static class ActivityService {
        private const int HistoryKept = 10;
        private const float KillRange = 120f;
        private const float ReportRange = 150f;
        private const float ParticipantCheckSeconds = 5f;
        private const long CleanupDelaySeconds = 20;

        // ZDOIDs already counted per hunt, against the same death being reported twice.
        private static readonly Dictionary<long, HashSet<ZDOID>> huntSeen = new Dictionary<long, HashSet<ZDOID>>();
        // Where each hunt's latest counted kill was: its rolled rewards drop there.
        private static readonly Dictionary<long, Vector3> huntLastKill = new Dictionary<long, Vector3>();
        // The destination totem's data revision when its chests were last counted, by race.
        private static readonly Dictionary<long, uint> destRevision = new Dictionary<long, uint>();
        // A World Boss fight won sooner than this after it began is not believed (a modified client).
        private const long MinWorldBossSeconds = 120;
        // Activities whose world objects are removed a little after they end, so players see how it ended.
        private static readonly Dictionary<long, long> cleanupAt = new Dictionary<long, long>();
        private static float participantTimer;
        private static bool dirty;
        private static long lastPeriod;

        internal static void Register() {
            ClanNet.RegisterServer(Op.StartActivity, OnStart);
            ClanNet.RegisterServer(Op.CancelActivity, OnCancel);
            ClanNet.RegisterServer(Op.BeginActivity, OnBegin);
            ClanNet.RegisterServer(Op.ReportKill, OnKill);
            ClanNet.RegisterServer(Op.ReportRaceChests, OnRaceChests);
            ClanNet.RegisterServer(Op.TakeRaceChest, OnTakeRaceChest);
            ClanNet.RegisterServer(Op.ReportWorldBossState, OnWorldBossState);
        }

        internal static void Reset() {
            huntSeen.Clear();
            huntLastKill.Clear();
            destRevision.Clear();
            cleanupAt.Clear();
            dirty = false;
            lastPeriod = 0;
        }

        internal static void MarkChanged() => dirty = true;

        private static IEnumerable<(ClanRecord Clan, ActivityInstance Activity)> All() {
            foreach (ClanRecord clan in ClanStore.State.Clans.Values) {
                foreach (ActivityInstance activity in clan.Activities) { yield return (clan, activity); }
            }
        }

        private static bool IsActive(ActivityInstance a) => a.State == ActivityState.Placed || a.State == ActivityState.Running || a.State == ActivityState.BossPhase;

        private static (ClanRecord Clan, ActivityInstance Activity) Find(long id) {
            foreach ((ClanRecord clan, ActivityInstance activity) in All()) {
                if (activity.Id == id) { return (clan, activity); }
            }
            return (null, null);
        }

        private static Vector3 TotemPos(ActivityInstance a) => new Vector3(a.TotemX, a.TotemY, a.TotemZ);
        private static Vector3 DestPos(ActivityInstance a) => new Vector3(a.DestX, a.DestY, a.DestZ);

        // ---------------------------------------------------------------- start / cancel

        private static void OnStart(ServerRequest request) {
            ActivityType type = (ActivityType)request.Body.ReadByte();
            int difficulty = ActivitiesSettings.Clamp(request.Body.ReadInt());
            string bossId = request.Body.ReadString();

            if (ClansConfig.EnableActivities.Value == false) { request.Fail("$clans_err_activities_off"); return; }
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.StartActivities) == false) { request.Fail("$clans_err_no_permission"); return; }
            if (TypeEnabled(type) == false) { request.Fail("$clans_err_activity_off"); return; }
            long now = ClanStore.Now;
            long period = WeeklyClock.PeriodStart(now);
            if (clan.Lockouts.TryGetValue(type.ToString(), out long last) && last == period) { request.Fail("$clans_err_activity_done_this_week"); return; }
            if (clan.Activities.Any(a => a.Type == type && IsActive(a))) { request.Fail("$clans_err_activity_running"); return; }

            ActivityInstance activity = new ActivityInstance {
                Id = ClanStore.State.NextActivityId++,
                Type = type,
                Difficulty = difficulty,
                StartedBy = request.Sender.Account,
                StartedUtc = now,
                DeadlineUtc = WeeklyClock.PeriodEnd(now),
            };
            string error = type switch {
                ActivityType.TrophyHunt => StartHunt(clan, activity),
                ActivityType.Race => StartRace(clan, activity),
                ActivityType.WorldBoss => StartWorldBoss(clan, activity, bossId),
                _ => "$clans_err_bad_request",
            };
            if (error != null) { request.Fail(error); return; }

            clan.Activities.Add(activity);
            while (clan.Activities.Count > HistoryKept) {
                ActivityInstance oldest = clan.Activities.FirstOrDefault(a => IsActive(a) == false);
                if (oldest == null) { break; }
                clan.Activities.Remove(oldest);
            }
            clan.Lockouts[type.ToString()] = period;
            ClanRules.Log(clan, "$clans_log_activity_started", request.Sender.CharacterName, Name(type), Difficulty(difficulty));
            ClanService.NotifyClan(clan, request.Sender.Account, "$clans_notice_activity_started", request.Sender.CharacterName, Name(type));
            Changed(clan);
            request.Ok();
        }

        private static bool TypeEnabled(ActivityType type) {
            ActivitiesSettings settings = ClansSettings.Activities;
            switch (type) {
                case ActivityType.TrophyHunt: return settings.TrophyHunt.Enabled;
                case ActivityType.Race: return settings.Race.Enabled;
                case ActivityType.WorldBoss:
                    return settings.WorldBoss.Enabled && Compat.ModPresence.Is(Compat.ModPresence.WorldBossNeeds);
                default: return false;
            }
        }

        // Cancelling before a totem is used gives the week's attempt back; after that it is spent.
        private static void OnCancel(ServerRequest request) {
            long id = request.Body.ReadLong();
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.StartActivities) == false) { request.Fail("$clans_err_no_permission"); return; }
            ActivityInstance activity = clan.Activities.FirstOrDefault(a => a.Id == id && IsActive(a));
            if (activity == null) { request.Fail("$clans_err_no_such_activity"); return; }
            if (activity.State == ActivityState.Placed) { clan.Lockouts.Remove(activity.Type.ToString()); }
            Finish(clan, activity, ActivityState.Cancelled);
            ClanRules.Log(clan, "$clans_log_activity_cancelled", request.Sender.CharacterName, Name(activity.Type));
            request.Ok();
        }

        internal static void OnClanDisbanded(ClanRecord clan) {
            foreach (ActivityInstance activity in clan.Activities.Where(IsActive).ToList()) {
                activity.State = ActivityState.Cancelled;
                activity.EndedUtc = ClanStore.Now;
                RemoveWorldObjects(activity);
            }
            dirty = true;
        }

        // ---------------------------------------------------------------- Trophy Hunt

        private static string StartHunt(ClanRecord clan, ActivityInstance activity) {
            TrophyHuntSettings hunt = ClansSettings.Activities.TrophyHunt;
            List<HuntBiomePool> reached = hunt.Biomes.Where(b => string.IsNullOrEmpty(b.RequiredKey) || GlobalKey(b.RequiredKey)).ToList();
            if (reached.Count == 0) { return "$clans_err_no_biomes"; }
            int count = Mathf.Max(1, hunt.TargetCounts[activity.Difficulty]);
            int extra = Mathf.FloorToInt(Mathf.Max(0, clan.Members.Count - 5) * hunt.KillsPerExtraMember);
            int kills = Mathf.Max(1, hunt.KillsPerTarget[activity.Difficulty] + extra);
            HuntBiomePool highest = reached[reached.Count - 1];
            // A biome's boss has been beaten when the next biome is reached.
            List<string> bosses = reached.Take(reached.Count - 1).SelectMany(b => b.Bosses).ToList();

            HashSet<string> chosen = new HashSet<string>();
            for (int i = 0; i < count; i++) {
                for (int attempt = 0; attempt < 30; attempt++) {
                    bool boss = bosses.Count > 0 && UnityEngine.Random.value < hunt.BossChance;
                    string prefab;
                    if (boss) {
                        prefab = bosses[UnityEngine.Random.Range(0, bosses.Count)];
                    } else {
                        HuntBiomePool pool = UnityEngine.Random.value < hunt.HighestBiomeShare || reached.Count == 1
                            ? highest
                            : reached[UnityEngine.Random.Range(0, reached.Count - 1)];
                        if (pool.Creatures.Count == 0) { continue; }
                        prefab = pool.Creatures[UnityEngine.Random.Range(0, pool.Creatures.Count)];
                    }
                    if (chosen.Add(prefab) == false) { continue; }
                    activity.Targets.Add(new HuntTarget { Prefab = prefab, Required = boss ? Mathf.Max(1, hunt.BossKills) : kills, IsBoss = boss });
                    break;
                }
            }
            if (activity.Targets.Count == 0) { return "$clans_err_no_biomes"; }
            activity.State = ActivityState.Running;
            activity.BeganUtc = activity.StartedUtc;
            return null;
        }

        private static bool GlobalKey(string key) => ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);

        // The most players one kill report may name.
        private const int MaxKillersPerReport = 32;

        // The creature's owner reports a death with every player who hurt it; the server credits each of their
        // clans once (two clanmates on one creature count it once for their clan).
        private static void OnKill(ServerRequest request) {
            string prefab = request.Body.ReadString();
            ZDOID zdoid = request.Body.ReadZDOID();
            Vector3 position = request.Body.ReadVector3();
            int count = request.Body.ReadInt();
            if (count <= 0 || count > MaxKillersPerReport) { return; }
            List<string> accounts = new List<string>(count);
            for (int i = 0; i < count; i++) { accounts.Add(request.Body.ReadString()); }
            // The creature must exist on the server as the reported kind, owned by whoever reports its death,
            // and its position is the server's. (The host's own reports are trusted.) Who hurt it is the owner's
            // word: the creature's own record of it may not have reached the server yet for a one-hit kill.
            if (request.Sender.IsLocalHost == false) {
                ZDO creature = zdoid.IsNone() || ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(zdoid);
                if (creature == null || creature.GetPrefab() != prefab.GetStableHashCode() || creature.GetOwner() != request.Sender.PeerUid) { return; }
                position = creature.GetPosition();
            }
            foreach (string account in accounts.Distinct()) { CreditKill(account, prefab, zdoid, position); }
        }

        private static void CreditKill(string account, string prefab, ZDOID zdoid, Vector3 position) {
            ClanSender attacker = Sessions.Get(account);
            if (attacker == null || Utils.DistanceXZ(attacker.Position, position) > KillRange) { return; }
            ClanRecord clan = ClanRules.ClanOf(account);
            ActivityInstance hunt = clan?.Activities.FirstOrDefault(a => a.Type == ActivityType.TrophyHunt && a.State == ActivityState.Running);
            if (hunt == null) { return; }
            HuntTarget target = hunt.Targets.FirstOrDefault(t => t.Prefab == prefab && t.Count < t.Required);
            if (target == null) { return; }
            if (huntSeen.TryGetValue(hunt.Id, out HashSet<ZDOID> seen) == false) {
                seen = new HashSet<ZDOID>();
                huntSeen[hunt.Id] = seen;
            }
            if (zdoid.IsNone() == false && seen.Add(zdoid) == false) {
                // Already counted for this clan; a clanmate who helped still took part.
                if (hunt.Participants.Contains(account) == false) { hunt.Participants.Add(account); }
                return;
            }

            target.Count++;
            huntLastKill[hunt.Id] = position;
            if (hunt.Participants.Contains(account) == false) { hunt.Participants.Add(account); }
            if (target.Count >= target.Required) {
                ClanRules.Log(clan, "$clans_log_hunt_target_done", attacker.CharacterName, CreatureToken(prefab));
            }
            if (hunt.Targets.All(t => t.Count >= t.Required)) {
                Complete(clan, hunt);
                return;
            }
            Progressed();
        }

        // ---------------------------------------------------------------- Race

        private static string StartRace(ClanRecord clan, ActivityInstance activity) {
            RaceSettings race = ClansSettings.Activities.Race;
            if (WorldPlacement.TryFind(race.AllowedBiomes, out Vector3 start, out Heightmap.Biome biome) == false) { return "$clans_err_no_place"; }
            PlaceTotem(clan, activity, ClansPrefabs.TotemKind.RaceStart, start, biome);
            activity.State = ActivityState.Placed;
            return null;
        }

        // False when no destination can be placed anywhere; the race then stays waiting at its totem.
        private static bool BeginRace(ClanRecord clan, ActivityInstance activity, List<string> present) {
            RaceSettings race = ClansSettings.Activities.Race;
            float distance = Mathf.Max(race.MinDistance, race.DistanceFraction * WorldExtent.PlayableRadius * 2f);
            Vector3 start = TotemPos(activity);
            if (WorldPlacement.TryFind(race.AllowedBiomes, out Vector3 dest, out Heightmap.Biome biome, start, distance * 0.85f, distance * 1.15f) == false
                && WorldPlacement.TryFind(race.AllowedBiomes, out dest, out biome, start, race.MinDistance, distance * 1.5f) == false
                // Nowhere suitable at that distance: anywhere in the allowed biomes will do.
                && WorldPlacement.TryFind(race.AllowedBiomes, out dest, out biome) == false) {
                return false;
            }
            ZDO placer = MakePlacer(clan, activity, ClansPrefabs.TotemKind.RaceEnd, dest, biome);
            activity.DestUser = placer.m_uid.UserID;
            activity.DestZdoId = placer.m_uid.ID;
            activity.DestX = dest.x;
            activity.DestY = dest.y;
            activity.DestZ = dest.z;
            activity.DestPending = true;
            // No more than the destination totem can hold.
            activity.ChestsTotal = Mathf.Clamp(Mathf.CeilToInt(present.Count * race.ChestsPerMember[activity.Difficulty]), 1,
                global::Clans.Gameplay.Prefabs.DestinationWidth * global::Clans.Gameplay.Prefabs.DestinationHeight);
            // Numbered as members take them from the start totem (OnTakeRaceChest).
            activity.ChestSerials = new List<int>();
            activity.DeadlineUtc = ClanStore.Now + race.TimeLimitMinutes * 60L;
            return true;
        }

        // Members take as many chests as they like (each client refuses while its player is overloaded); this
        // only stops a runaway client from numbering chests without end.
        private const int MaxChestsPerRace = 200;

        // A member at the start totem takes a chest: the server numbers it, so the destination can tell this race's
        // chests apart, and the client puts it in the player's inventory.
        private static void OnTakeRaceChest(ServerRequest request) {
            long id = request.Body.ReadLong();
            (ClanRecord clan, ActivityInstance race) = Find(id);
            if (race == null || race.Type != ActivityType.Race || race.State != ActivityState.Running) { request.Fail("$clans_err_no_such_activity"); return; }
            if (ClanRules.ClanOf(request.Sender.Account)?.Id != clan.Id) { request.Fail("$clans_err_not_your_activity"); return; }
            if (Utils.DistanceXZ(request.Sender.Position, TotemPos(race)) > ReportRange) { request.Fail("$clans_err_too_far"); return; }
            if (race.ChestSerials.Count >= MaxChestsPerRace) { request.Fail("$clans_err_race_no_more_chests"); return; }

            int serial = race.ChestSerials.Count == 0 ? 1 : race.ChestSerials.Max() + 1;
            race.ChestSerials.Add(serial);
            race.ChestsSpawned = true;
            if (race.Participants.Contains(request.Sender.Account) == false) {
                race.Participants.Add(request.Sender.Account);
                Progressed();
            }
            ClanStore.MarkDirty();
            request.Ok(pkg => pkg.Write(serial));
        }

        // The destination's owner reports what is inside it.
        private static void OnRaceChests(ServerRequest request) {
            long id = request.Body.ReadLong();
            int count = request.Body.ReadInt();
            if (count < 0 || count > 500) { return; }
            // The chests the client saw; the server counts them itself (CheckDestination).
            for (int i = 0; i < count; i++) { request.Body.ReadInt(); }
            (ClanRecord clan, ActivityInstance race) = Find(id);
            if (race == null || race.Type != ActivityType.Race || race.State != ActivityState.Running) { return; }
            if (Utils.DistanceXZ(request.Sender.Position, DestPos(race)) > ReportRange) { return; }
            CheckDestination(clan, race, force: true);
        }

        // Counts this race's chests in the destination totem from its object as the server has it, so only
        // chests really delivered count. Done when its owner reports a change and whenever the object changes.
        private static void CheckDestination(ClanRecord clan, ActivityInstance race, bool force) {
            // Still only its placer: nothing can be in it yet.
            if (race.DestPending || ZDOMan.instance == null || ObjectDB.instance == null) { return; }
            ZDO zdo = ZDOMan.instance.GetZDO(new ZDOID(race.DestUser, race.DestZdoId));
            if (zdo == null) { return; }
            if (force == false && destRevision.TryGetValue(race.Id, out uint seen) && seen == zdo.DataRevision) { return; }
            destRevision[race.Id] = zdo.DataRevision;

            List<int> serials = new List<int>();
            // Containers keep their items as a byte array (Container.Save), not a string.
            byte[] data = zdo.GetByteArray(ZDOVars.s_items);
            if (data != null && data.Length > 0) {
                Inventory inventory = new Inventory("clans_race_check", null, global::Clans.Gameplay.Prefabs.DestinationWidth, global::Clans.Gameplay.Prefabs.DestinationHeight);
                inventory.Load(new ZPackage(data));
                foreach (ItemDrop.ItemData item in inventory.GetAllItems()) {
                    if (RaceChests.IsRaceChest(item) && RaceChests.RaceOf(item) == race.Id) { serials.Add(RaceChests.SerialOf(item)); }
                }
            }
            int before = race.Delivered.Count;
            race.Delivered = serials.Where(s => race.ChestSerials.Contains(s)).Distinct().ToList();
            if (race.Delivered.Count != before) { Progressed(); }
            if (race.Delivered.Count >= race.ChestsTotal) {
                AddPresent(clan, race, DestPos(race));
                Complete(clan, race);
            }
        }

        // ---------------------------------------------------------------- World Boss

        private static string StartWorldBoss(ClanRecord clan, ActivityInstance activity, string bossId) {
            WorldBossDef boss = ClansSettings.Activities.WorldBoss.Find(bossId);
            if (boss == null || boss.Enabled == false) { return "$clans_err_no_such_boss"; }
            if (string.IsNullOrEmpty(boss.RequiredKey) == false && GlobalKey(boss.RequiredKey) == false) { return "$clans_err_boss_not_beaten"; }
            if (WorldPlacement.TryFind(new[] { boss.Biome }, out Vector3 point, out Heightmap.Biome biome) == false) { return "$clans_err_no_place"; }
            activity.BossId = boss.Id;
            PlaceTotem(clan, activity, ClansPrefabs.TotemKind.WorldBoss, point, biome);
            activity.State = ActivityState.Placed;
            return null;
        }

        private static void OnWorldBossState(ServerRequest request) {
            long id = request.Body.ReadLong();
            int phase = request.Body.ReadInt();
            byte status = request.Body.ReadByte();
            string handle = request.Body.ReadString();
            (ClanRecord clan, ActivityInstance boss) = Find(id);
            if (boss == null || boss.Type != ActivityType.WorldBoss || (boss.State != ActivityState.Running && boss.State != ActivityState.BossPhase)) { return; }
            if (Utils.DistanceXZ(request.Sender.Position, TotemPos(boss)) > ReportRange && OwnsTotem(request.Sender, boss) == false) { return; }
            if (string.IsNullOrEmpty(handle) == false) { boss.FortressHandle = handle; }
            if (phase != boss.Phase) {
                boss.Phase = phase;
                int waves = ClansSettings.Activities.WorldBoss.Waves;
                if (phase >= waves && boss.State == ActivityState.Running) {
                    boss.State = ActivityState.BossPhase;
                    ClanService.NotifyClan(clan, null, "$clans_notice_boss_wave");
                }
                Progressed();
            }
            // 1: every wave beaten, boss included. 2: the run ended without that.
            if (status == 1 && ClanStore.Now - boss.BeganUtc < MinWorldBossSeconds) {
                Logger.LogWarning($"A World Boss win for {clan.Name} came {ClanStore.Now - boss.BeganUtc} s after the fight began; not believed.");
                return;
            }
            if (status == 1) {
                AddPresent(clan, boss, TotemPos(boss));
                Complete(clan, boss);
            } else if (status == 2) {
                Fail(clan, boss, "$clans_log_boss_failed");
            }
        }

        // The fight runs on the totem's owner, who may stand a little beyond the usual report range.
        private static bool OwnsTotem(ClanSender sender, ActivityInstance activity) {
            if (activity.TotemPending || ZDOMan.instance == null || (activity.TotemUser == 0 && activity.TotemZdoId == 0)) { return false; }
            ZDO zdo = ZDOMan.instance.GetZDO(new ZDOID(activity.TotemUser, activity.TotemZdoId));
            return zdo != null && zdo.GetOwner() == sender.PeerUid;
        }

        // ---------------------------------------------------------------- begin (totem used)

        private static void OnBegin(ServerRequest request) {
            long id = request.Body.ReadLong();
            (ClanRecord clan, ActivityInstance activity) = Find(id);
            if (activity == null || activity.State != ActivityState.Placed) { request.Fail("$clans_err_no_such_activity"); return; }
            if (ClanRules.ClanOf(request.Sender.Account)?.Id != clan.Id) { request.Fail("$clans_err_not_your_activity"); return; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.StartActivities) == false) { request.Fail("$clans_err_no_permission"); return; }
            // The totem was used, so it stands; the server may not have noticed yet.
            FindPlacedTotems(activity);
            if (activity.TotemPending) { request.Fail("$clans_err_totem_settling"); return; }
            Vector3 totem = TotemPos(activity);
            if (Utils.DistanceXZ(request.Sender.Position, totem) > ClansConfig.ActivityParticipantRadius.Value) { request.Fail("$clans_err_too_far"); return; }

            List<string> present = MembersNear(clan, totem);
            if (present.Contains(request.Sender.Account) == false) { present.Add(request.Sender.Account); }
            if (activity.Type == ActivityType.Race && BeginRace(clan, activity, present) == false) { request.Fail("$clans_err_no_place"); return; }
            activity.Participants = present;
            activity.ParticipantCountAtBegin = present.Count;
            activity.BeganUtc = ClanStore.Now;
            if (activity.Type == ActivityType.WorldBoss) {
                activity.DeadlineUtc = ClanStore.Now + ClansSettings.Activities.WorldBoss.TimeLimitMinutes * 60L;
            }
            activity.State = ActivityState.Running;
            ClanRules.Log(clan, "$clans_log_activity_began", request.Sender.CharacterName, Name(activity.Type), present.Count.ToString());
            ClanService.NotifyClan(clan, request.Sender.Account, "$clans_notice_activity_began", Name(activity.Type));
            Changed(clan);
            request.Ok();
        }

        private static List<string> MembersNear(ClanRecord clan, Vector3 point) {
            float radius = ClansConfig.ActivityParticipantRadius.Value;
            return clan.Members.Keys
                .Where(account => Sessions.Get(account) is ClanSender s && Utils.DistanceXZ(s.Position, point) <= radius)
                .ToList();
        }

        private static void AddPresent(ClanRecord clan, ActivityInstance activity, Vector3 point) {
            foreach (string account in MembersNear(clan, point)) {
                if (activity.Participants.Contains(account) == false) { activity.Participants.Add(account); }
            }
        }

        // ---------------------------------------------------------------- endings

        private static void Complete(ClanRecord clan, ActivityInstance activity) {
            ActivitiesSettings settings = ClansSettings.Activities;
            // Only those still in the clan share its reward.
            activity.Participants = activity.Participants.Where(clan.Members.ContainsKey).ToList();
            ActivityReward reward;
            Heightmap.Biome biome = Heightmap.Biome.Meadows;
            switch (activity.Type) {
                case ActivityType.TrophyHunt: reward = settings.TrophyHunt.Rewards[activity.Difficulty]; biome = HighestReachedBiome(); break;
                case ActivityType.Race: reward = settings.Race.Rewards[activity.Difficulty]; break;
                default:
                    reward = settings.WorldBoss.Rewards[activity.Difficulty];
                    biome = settings.WorldBoss.Find(activity.BossId)?.Biome ?? Heightmap.Biome.Meadows;
                    break;
            }
            Finish(clan, activity, ActivityState.Completed);
            clan.ActivitiesCompleted++;
            ClanRules.Log(clan, "$clans_log_activity_done", Name(activity.Type), activity.Participants.Count.ToString());
            ClanService.NotifyClan(clan, null, "$clans_notice_activity_done", Name(activity.Type));
            RewardService.Grant(clan, activity, reward, biome, LootPoint(activity));
            huntLastKill.Remove(activity.Id);
        }

        // Where rolled rewards drop: at the World Boss totem, the Race destination, or the Trophy Hunt's last
        // kill (else next to the first participant who is online).
        private static Vector3 LootPoint(ActivityInstance activity) {
            switch (activity.Type) {
                case ActivityType.WorldBoss: return TotemPos(activity);
                case ActivityType.Race: return DestPos(activity);
            }
            if (huntLastKill.TryGetValue(activity.Id, out Vector3 kill)) { return kill; }
            ClanSender someone = activity.Participants.Select(Sessions.Get).FirstOrDefault(s => s != null);
            return someone != null ? someone.Position : Vector3.zero;
        }

        private static void Fail(ClanRecord clan, ActivityInstance activity, string logToken) {
            Finish(clan, activity, ActivityState.Failed);
            ClanRules.Log(clan, logToken, Name(activity.Type));
            ClanService.NotifyClan(clan, null, "$clans_notice_activity_failed", Name(activity.Type));
        }

        private static void Finish(ClanRecord clan, ActivityInstance activity, ActivityState state) {
            activity.State = state;
            activity.EndedUtc = ClanStore.Now;
            huntSeen.Remove(activity.Id);
            cleanupAt[activity.Id] = ClanStore.Now + CleanupDelaySeconds;
            Changed(clan);
            ClanStore.MarkDirtySoon();
        }

        private static Heightmap.Biome HighestReachedBiome() {
            HuntBiomePool highest = ClansSettings.Activities.TrophyHunt.Biomes
                .LastOrDefault(b => string.IsNullOrEmpty(b.RequiredKey) || GlobalKey(b.RequiredKey));
            return highest?.Biome ?? Heightmap.Biome.Meadows;
        }

        // ---------------------------------------------------------------- housekeeping

        internal static void Housekeeping(long now, float dt) {
            // A new week: everyone's tiles show the lockouts lifted.
            long period = WeeklyClock.PeriodStart(now);
            if (period != lastPeriod) {
                if (lastPeriod != 0) { MarkChanged(); }
                lastPeriod = period;
            }
            foreach ((ClanRecord clan, ActivityInstance activity) in All().ToList()) {
                if (IsActive(activity) == false) { continue; }
                if (activity.DeadlineUtc > 0 && now >= activity.DeadlineUtc) {
                    Fail(clan, activity, activity.State == ActivityState.Placed ? "$clans_log_activity_unused" : "$clans_log_activity_timeout");
                }
                if (activity.Type == ActivityType.Race && activity.State == ActivityState.Running) { CheckDestination(clan, activity, force: false); }
            }
            placementTimer -= dt;
            if (placementTimer <= 0f) {
                placementTimer = PlacementCheckSeconds;
                foreach ((ClanRecord _, ActivityInstance activity) in All()) {
                    if (IsActive(activity) && (activity.TotemPending || activity.DestPending)) { FindPlacedTotems(activity); }
                }
            }
            foreach (KeyValuePair<long, long> entry in cleanupAt.Where(e => now >= e.Value).ToList()) {
                cleanupAt.Remove(entry.Key);
                ActivityInstance activity = Find(entry.Key).Activity;
                if (activity != null) { RemoveWorldObjects(activity); }
            }
            participantTimer -= dt;
            if (participantTimer <= 0f) {
                participantTimer = ParticipantCheckSeconds;
                // Members who join a World Boss fight after it began take part too.
                foreach ((ClanRecord clan, ActivityInstance activity) in All()) {
                    if (activity.Type == ActivityType.WorldBoss && (activity.State == ActivityState.Running || activity.State == ActivityState.BossPhase)) {
                        int before = activity.Participants.Count;
                        AddPresent(clan, activity, TotemPos(activity));
                        if (activity.Participants.Count != before) { Progressed(); }
                    }
                }
            }
        }

        private static void RemoveWorldObjects(ActivityInstance activity) {
            WorldPlacement.DestroyObject(activity.TotemUser, activity.TotemZdoId);
            WorldPlacement.DestroyObject(activity.DestUser, activity.DestZdoId);
            // Also a totem put up just before the end that the server had not been pointed at yet, and any placer.
            foreach (Vector3 near in new[] { TotemPos(activity), DestPos(activity) }) {
                if (near == Vector3.zero) { continue; }
                Totems.FindStamped(activity.Id, near, ClansPrefabs.PlacerReach + 10f, stampedNear);
                foreach (ZDO zdo in stampedNear) { Destroy(zdo); }
            }
        }

        // ---------------------------------------------------------------- totem placement

        private const float PlacementCheckSeconds = 2f;
        private static float placementTimer;
        private static readonly List<ZDO> stampedNear = new List<ZDO>();

        private static ClansPrefabs.TotemKind StartKind(ActivityInstance activity) =>
            activity.Type == ActivityType.Race ? ClansPrefabs.TotemKind.RaceStart : ClansPrefabs.TotemKind.WorldBoss;

        // Points an activity at the totems its placers have put up (Gameplay.TotemPlacer), found by their stamp in
        // the server's own copy of the world. Only a totem of the right kind, within a placer's reach of where the
        // placer was, is believed.
        private static void FindPlacedTotems(ActivityInstance activity) {
            if (activity.TotemPending && TryFindTotem(activity, StartKind(activity), TotemPos(activity), out ZDO totem)) {
                // The placer removes itself; this is for one whose owner left first.
                WorldPlacement.DestroyObject(activity.TotemUser, activity.TotemZdoId);
                Vector3 at = totem.GetPosition();
                activity.TotemUser = totem.m_uid.UserID;
                activity.TotemZdoId = totem.m_uid.ID;
                activity.TotemX = at.x;
                activity.TotemY = at.y;
                activity.TotemZ = at.z;
                activity.TotemPending = false;
                Progressed();
            }
            if (activity.DestPending && TryFindTotem(activity, ClansPrefabs.TotemKind.RaceEnd, DestPos(activity), out ZDO dest)) {
                WorldPlacement.DestroyObject(activity.DestUser, activity.DestZdoId);
                Vector3 at = dest.GetPosition();
                activity.DestUser = dest.m_uid.UserID;
                activity.DestZdoId = dest.m_uid.ID;
                activity.DestX = at.x;
                activity.DestY = at.y;
                activity.DestZ = at.z;
                activity.DestPending = false;
                Progressed();
            }
        }

        private static bool TryFindTotem(ActivityInstance activity, ClansPrefabs.TotemKind kind, Vector3 near, out ZDO totem) {
            int prefab = ClansPrefabs.TotemPrefab(kind).GetStableHashCode();
            Totems.FindStamped(activity.Id, near, ClansPrefabs.PlacerReach + 10f, stampedNear);
            totem = stampedNear.FirstOrDefault(zdo => zdo.GetPrefab() == prefab && Totems.KindOf(zdo) == kind);
            return totem != null;
        }

        // Puts every activity back in place after a restart.
        //
        // Valheim gives every object a new id when the world loads (ZDO.Load), so the ids saved with an activity
        // are stale. Its objects are found again by the stamp they carry (clans_activity, clans_kind) and the
        // activity is pointed at them: a totem if it stands, else its placer, still to put it up; objects of
        // activities that are over (or were cleaned up just before the restart) are removed. An activity whose totem
        // and placer are both gone fails and gives the week's attempt back. A World Boss fight cannot carry on:
        // Valheim Fortress does not keep a run's waves over a restart, so the fight ends as interrupted, its waves
        // are told to stop, and the attempt is given back.
        internal static void OnStateLoaded() {
            if (ZDOMan.instance == null) { return; }
            Dictionary<long, (ClanRecord Clan, ActivityInstance Activity)> active = All().Where(e => IsActive(e.Activity)).ToDictionary(e => e.Activity.Id, e => e);
            HashSet<int> totemPrefabs = new HashSet<int> {
                ClansPrefabs.RaceStart.GetStableHashCode(), ClansPrefabs.RaceEnd.GetStableHashCode(), ClansPrefabs.WorldBoss.GetStableHashCode(),
                ClansPrefabs.Placer.GetStableHashCode(),
            };
            int runnerPrefab = FortressRunnerPrefab.GetStableHashCode();
            List<ZDO> runners = new List<ZDO>();
            // Per activity and kind: the totems and the placers found.
            Dictionary<(long, ClansPrefabs.TotemKind), List<ZDO>> totemsFound = new Dictionary<(long, ClansPrefabs.TotemKind), List<ZDO>>();
            Dictionary<(long, ClansPrefabs.TotemKind), List<ZDO>> placersFound = new Dictionary<(long, ClansPrefabs.TotemKind), List<ZDO>>();

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.ToList()) {
                int prefab = zdo.GetPrefab();
                if (prefab == runnerPrefab) { runners.Add(zdo); continue; }
                if (totemPrefabs.Contains(prefab) == false) { continue; }
                long id = zdo.GetLong(ClansPrefabs.ActivityKey, 0L);
                if (active.ContainsKey(id) == false) {
                    Destroy(zdo);
                    continue;
                }
                Dictionary<(long, ClansPrefabs.TotemKind), List<ZDO>> found = Totems.IsPlacer(zdo) ? placersFound : totemsFound;
                (long, ClansPrefabs.TotemKind) key = (id, Totems.KindOf(zdo));
                if (found.TryGetValue(key, out List<ZDO> list) == false) {
                    list = new List<ZDO>();
                    found[key] = list;
                }
                list.Add(zdo);
            }

            foreach ((ClanRecord clan, ActivityInstance activity) in active.Values) {
                if (activity.Type == ActivityType.TrophyHunt) { continue; }
                bool running = activity.State == ActivityState.Running || activity.State == ActivityState.BossPhase;
                bool haveTotem = Reattach(activity, StartKind(activity), totemsFound, placersFound, out ZDO totem, out bool totemPending);
                if (haveTotem) {
                    Vector3 at = totem.GetPosition();
                    activity.TotemUser = totem.m_uid.UserID;
                    activity.TotemZdoId = totem.m_uid.ID;
                    activity.TotemX = at.x;
                    activity.TotemY = at.y;
                    activity.TotemZ = at.z;
                    activity.TotemPending = totemPending;
                }
                bool haveDest = Reattach(activity, ClansPrefabs.TotemKind.RaceEnd, totemsFound, placersFound, out ZDO dest, out bool destPending);
                if (haveDest) {
                    Vector3 at = dest.GetPosition();
                    activity.DestUser = dest.m_uid.UserID;
                    activity.DestZdoId = dest.m_uid.ID;
                    activity.DestX = at.x;
                    activity.DestY = at.y;
                    activity.DestZ = at.z;
                    activity.DestPending = destPending;
                }
                bool lost = haveTotem == false || (activity.Type == ActivityType.Race && running && haveDest == false);
                if (lost) {
                    EndAfterRestart(clan, activity, "$clans_log_activity_lost");
                } else if (activity.Type == ActivityType.WorldBoss && running) {
                    StopFortressRun(runners, TotemPos(activity));
                    EndAfterRestart(clan, activity, "$clans_log_activity_interrupted");
                }
            }
            dirty = true;
            ClanStore.MarkDirty();
        }

        private const string FortressRunnerPrefab = "VF_api_challenge_runner";

        private static void EndAfterRestart(ClanRecord clan, ActivityInstance activity, string logToken) {
            activity.State = ActivityState.Failed;
            activity.EndedUtc = ClanStore.Now;
            clan.Lockouts.Remove(activity.Type.ToString());
            ClanRules.Log(clan, logToken, Name(activity.Type));
            RemoveWorldObjects(activity);
            Broadcaster.MarkDetail(clan.Id);
        }

        // Asks the Valheim Fortress run next to a totem to stop (its next owner carries the request out).
        private static void StopFortressRun(List<ZDO> runners, Vector3 totem) {
            foreach (ZDO runner in runners) {
                if (Utils.DistanceXZ(runner.GetPosition(), totem) > 15f) { continue; }
                runner.SetOwner(ZDOMan.GetSessionID());
                runner.Set("api_stop_requested", true);
            }
        }

        private static void Destroy(ZDO zdo) {
            zdo.SetOwner(ZDOMan.GetSessionID());
            ZDOMan.instance.DestroyZDO(zdo);
        }

        // ---------------------------------------------------------------- world objects

        // A placer roughly where the totem should go; it puts the totem on open ground near it once someone is there
        // (Gameplay.TotemPlacer), and FindPlacedTotems then points the activity at the totem.
        private static void PlaceTotem(ClanRecord clan, ActivityInstance activity, ClansPrefabs.TotemKind kind, Vector3 point, Heightmap.Biome biome) {
            ZDO placer = MakePlacer(clan, activity, kind, point, biome);
            activity.TotemUser = placer.m_uid.UserID;
            activity.TotemZdoId = placer.m_uid.ID;
            activity.TotemX = point.x;
            activity.TotemY = point.y;
            activity.TotemZ = point.z;
            activity.TotemPending = true;
        }

        private static ZDO MakePlacer(ClanRecord clan, ActivityInstance activity, ClansPrefabs.TotemKind kind, Vector3 point, Heightmap.Biome biome) {
            ZDO zdo = Totems.CreateObject(ClansPrefabs.Placer, point, Quaternion.identity);
            Totems.Stamp(zdo, activity.Id, kind, clan.Id);
            zdo.Set(ClansPrefabs.BiomeKey, (int)biome);
            return zdo;
        }

        // The object an activity's totem of a kind is after a restart: the totem if it stands (any extra copy, and its
        // placer, are removed), else its placer. False when neither is left.
        private static bool Reattach(ActivityInstance activity, ClansPrefabs.TotemKind kind,
                Dictionary<(long, ClansPrefabs.TotemKind), List<ZDO>> totems, Dictionary<(long, ClansPrefabs.TotemKind), List<ZDO>> placers,
                out ZDO zdo, out bool pending) {
            zdo = null;
            pending = false;
            totems.TryGetValue((activity.Id, kind), out List<ZDO> standing);
            placers.TryGetValue((activity.Id, kind), out List<ZDO> waiting);
            if (standing != null && standing.Count > 0) {
                zdo = standing[0];
                foreach (ZDO extra in standing.Skip(1)) { Destroy(extra); }
                if (waiting != null) {
                    foreach (ZDO placer in waiting) { Destroy(placer); }
                }
                return true;
            }
            if (waiting == null || waiting.Count == 0) { return false; }
            zdo = waiting[0];
            pending = true;
            foreach (ZDO extra in waiting.Skip(1)) { Destroy(extra); }
            return true;
        }

        // ---------------------------------------------------------------- pushes

        private static void Changed(ClanRecord clan) {
            dirty = true;
            Broadcaster.MarkDetail(clan.Id);
            ClanStore.MarkDirty();
        }

        // Progress (a kill, a chest, a wave, a newcomer) only shows on the activity tiles, which the activity
        // push carries: the clan's detail (log, lockouts) is not resent for it.
        private static void Progressed() {
            dirty = true;
            ClanStore.MarkDirty();
        }

        // Called from the broadcaster's flush: every active activity, to everyone.
        internal static void Flush() {
            if (dirty == false || ClanStore.State == null) { return; }
            dirty = false;
            ClanNet.Broadcast(Op.ActivityState, Write);
        }

        internal static void SendTo(ClanSender sender) => ClanNet.Send(sender, Op.ActivityState, 0, Write);

        private static void Write(ZPackage pkg) {
            long now = ClanStore.Now;
            pkg.Write(WeeklyClock.PeriodStart(now));
            pkg.Write(WeeklyClock.PeriodEnd(now));
            pkg.Write(WeeklyClock.DebugOffsetSeconds);
            List<(ClanRecord Clan, ActivityInstance Activity)> active = All().Where(e => IsActive(e.Activity)).ToList();
            pkg.Write(active.Count);
            foreach ((ClanRecord clan, ActivityInstance activity) in active) {
                ActivityInfo.Write(pkg, clan.Id, activity, ClanRules.Account(activity.StartedBy)?.LastCharacterName ?? "");
            }
        }

        // ---------------------------------------------------------------- admin

        internal static List<string> AdminDescribe() {
            return All().Where(e => IsActive(e.Activity))
                .Select(e => $"{e.Activity.Id}: {e.Clan.Name} - {e.Activity.Type} {e.Activity.State}, difficulty {e.Activity.Difficulty}, " +
                    $"{e.Activity.Participants.Count} participants, phase {e.Activity.Phase}, {e.Activity.Delivered.Count}/{e.Activity.ChestsTotal} chests")
                .ToList();
        }

        // Ends a running activity as won (rewards paid) or lost, for testing.
        internal static bool AdminEnd(long id, bool won) {
            (ClanRecord clan, ActivityInstance activity) = Find(id);
            if (activity == null || IsActive(activity) == false) { return false; }
            if (won) { Complete(clan, activity); } else { Fail(clan, activity, "$clans_log_activity_timeout"); }
            return true;
        }

        // Lets the clan start an activity type again this week (one type, or every type). Returns the types it had
        // already done this week; a lockout from an earlier week blocks nothing and is just dropped.
        internal static List<ActivityType> AdminResetLockouts(ClanRecord clan, ActivityType? only) {
            long period = WeeklyClock.PeriodStart(ClanStore.Now);
            List<ActivityType> cleared = new List<ActivityType>();
            foreach (ActivityType type in Enum.GetValues(typeof(ActivityType))) {
                if (only != null && type != only) { continue; }
                if (clan.Lockouts.TryGetValue(type.ToString(), out long start) && start == period) { cleared.Add(type); }
                clan.Lockouts.Remove(type.ToString());
            }
            if (cleared.Count == 1) {
                ClanRules.Log(clan, "$clans_log_activity_reset", Name(cleared[0]));
            } else if (cleared.Count > 1) {
                ClanRules.Log(clan, "$clans_log_activities_reset");
            }
            Changed(clan);
            return cleared;
        }

        // A running activity still keeps its type from being started again until it ends.
        internal static List<ActivityInstance> AdminRunning(ClanRecord clan, ActivityType? only) {
            return clan.Activities.Where(a => IsActive(a) && (only == null || a.Type == only)).ToList();
        }

        // ---------------------------------------------------------------- names

        // A creature's name token (for example $enemy_boar), so each reader sees it in their own language.
        private static string CreatureToken(string prefab) {
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            Character character = go != null ? go.GetComponent<Character>() : null;
            return character != null && string.IsNullOrEmpty(character.m_name) == false ? character.m_name : prefab;
        }

        internal static string Name(ActivityType type) => "$clans_activity_" + type.ToString().ToLowerInvariant();
        internal static string Difficulty(int difficulty) => "$clans_difficulty_" + ActivitiesSettings.Clamp(difficulty);
    }
}
