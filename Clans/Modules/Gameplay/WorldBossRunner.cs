using Clans.Client;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using ValheimFortress;
using SlsApi = StarLevelSystem.API;
using VfApi = ValheimFortress.API;

namespace Clans.Gameplay {
    // The world side of a World Boss fight, run by whoever owns the totem's object. Once the fight begins it
    // starts the Valheim Fortress waves around the totem (generated waves of the boss's biome, then the boss
    // itself with extra health and Star Level System boss abilities), follows them, and tells the server how
    // far they got. When the server ends the fight first (time limit, cancel) it stops the waves.
    //
    // Valheim Fortress 0.40.0 and later (API schema 4) hand back a handle to follow and stop the run. Older
    // versions only start one, so LegacyRun watches the run's object instead and boosts the boss itself.
    internal sealed class WorldBossRunner {
        private const float RingMin = 22f;
        private const float RingMax = 38f;
        // Portal spots (SpawnPoints): never closer to each other than this, and on open ground a portal's size
        // (Placement.Clear), not too steep or uneven.
        private const float MinPortalSpacing = 10f;
        private const float PortalClearRadius = 1.5f;
        private const float PortalClearHeight = 3.5f;
        private const float PortalMinUpright = 0.75f;
        private const float PortalMaxStep = 1.5f;
        private const int AttemptsPerPortal = 24;
        private const int FillAttempts = 80;
        // Seconds to wait for the ground and everything on it to load around the totem before choosing portal
        // spots, so the checks can see the trees. The fight starts anyway after this.
        private const int AreaWaitSeconds = 15;
        // Seconds a started run may be unreadable (not loaded here yet, or gone) before it counts as lost.
        private const int MissingSecondsToFail = 20;
        private const int ReportEverySeconds = 10;
        // The result is said again this often while the server still lists the fight as going.
        private const int ResendEverySeconds = 5;
        internal const string LegacyHandle = "legacy";

        private const byte StatusRunning = 0;
        private const byte StatusWon = 1;
        private const byte StatusLost = 2;

        private readonly ClanTotem totem;
        private readonly ZNetView view;
        private readonly long activityId;
        private string handle;
        private int lastPhase = -1;
        private int missing;
        private int sinceReport;
        private bool ended;
        private int finalPhase;
        private byte finalStatus;
        private int resendIn;
        private bool stopped;
        private int areaWait;
        private LegacyRun legacy;

        private static List<string> fortressCreatures;

        internal WorldBossRunner(ClanTotem totem, ZNetView view, long activityId) {
            this.totem = totem;
            this.view = view;
            this.activityId = activityId;
        }

        // Once a second, on the totem's owner. info is null once the server no longer lists the fight.
        internal void Tick(ActivityInfo info) {
            ZDO zdo = view.GetZDO();
            // The server's copy wins: it is the one kept up to date if the run's object is found again.
            if (info != null && string.IsNullOrEmpty(info.FortressHandle) == false) { handle = info.FortressHandle; }
            if (string.IsNullOrEmpty(handle)) { handle = zdo.GetString(ClansPrefabs.FortressHandleKey, ""); }

            if (info == null) {
                // Won, lost, timed out or cancelled on the server: close a run still going here.
                if (ActivityClient.Received && string.IsNullOrEmpty(handle) == false) { Stop(); }
                return;
            }
            if (info.State != ActivityState.Running && info.State != ActivityState.BossPhase) { return; }
            if (ended) {
                // The result did not reach the server (or was lost on the way): say it again.
                if (--resendIn <= 0) {
                    resendIn = ResendEverySeconds;
                    Send(finalPhase, finalStatus);
                }
                return;
            }

            if (string.IsNullOrEmpty(handle)) {
                if (zdo.GetBool(ClansPrefabs.FortressStartedKey)) {
                    // Started here before, but the handle never got back (the start threw, or an old owner left).
                    Report(info.Phase, StatusLost);
                    return;
                }
                if (Placement.AreaSettled(totem.transform.position) == false && ++areaWait < AreaWaitSeconds) { return; }
                Start(info);
                return;
            }
            if (handle.StartsWith(LegacyHandle)) {
                if (legacy == null) { legacy = new LegacyRun(totem.transform, Boss(info), info); }
                Follow(info, legacy.Read(info));
            } else {
                Follow(info, VfApi.GetChallengeState(handle));
            }
        }

        // ---------------------------------------------------------------- start

        private void Start(ActivityInfo info) {
            WorldBossDef boss = Boss(info);
            if (VfApi.IsAvailable == false || boss == null || KnownCreature(boss.FortressCreature) == false) {
                Logger.LogError(boss == null
                    ? $"World Boss {info.BossId} is not in activities.yaml; the fight cannot start."
                    : $"Valheim Fortress is missing or does not know the creature {boss.FortressCreature}; the World Boss fight cannot start.");
                Report(0, StatusLost);
                return;
            }
            // Set before starting, so a start that throws is never repeated by this or a later owner.
            view.GetZDO().Set(ClansPrefabs.FortressStartedKey, true);

            WorldBossSettings settings = ClansSettings.Activities.WorldBoss;
            int difficulty = Mathf.Clamp(info.Difficulty, 0, 2);
            Vector3[] points = SpawnPoints(At(settings.SpawnPoints, difficulty, 10));
            Vector3 center = totem.transform.position;
            try {
                if (VfApi.SupportsHandles) {
                    string started = VfApi.StartChallenge(Definition(info, boss, legacyMode: false), points, center);
                    if (string.IsNullOrEmpty(started)) {
                        Report(0, StatusLost);
                        return;
                    }
                    handle = started;
                } else {
                    if (VfApi.RunChallenge(Definition(info, boss, legacyMode: true), points, center) == false) {
                        Report(0, StatusLost);
                        return;
                    }
                    handle = LegacyHandle;
                    Logger.LogWarning("This Valheim Fortress is older than 0.40.0: World Boss fights cannot be stopped early, and it draws no portals of its own.");
                }
            } catch (Exception e) {
                Logger.LogError($"Valheim Fortress could not start the World Boss fight: {e}");
                Report(0, StatusLost);
                return;
            }
            view.GetZDO().Set(ClansPrefabs.FortressHandleKey, handle);
            // For the mist every client near draws over the spawn points (ClanTotem).
            view.GetZDO().Set(ClansPrefabs.PortalsKey, FormatPoints(points));
            lastPhase = 0;
            Report(0, StatusRunning);
        }

        private static VFChallengeDefinition Definition(ActivityInfo info, WorldBossDef boss, bool legacyMode) {
            WorldBossSettings settings = ClansSettings.Activities.WorldBoss;
            int difficulty = Mathf.Clamp(info.Difficulty, 0, 2);
            float scale = 1f + Mathf.Max(0f, settings.PerParticipantScale) * Mathf.Max(0, info.Participants - 1);
            short maxCreatures = (short)Mathf.Clamp(Mathf.RoundToInt(At(settings.MaxCreatures, difficulty, 20) * scale), 1, short.MaxValue);
            short stars = (short)Mathf.Max(0, At(settings.BossStars, difficulty, 2));
            VFModifierSet modifiers = BossModifiers(At(settings.BossModifiers, difficulty, 1));

            VFChallengeDefinition definition = new VFChallengeDefinition {
                Biome = boss.Biome,
                // Valheim Fortress's own levels give each biome five, starting at 1 for the Meadows.
                Difficulty = (short)(BiomeTier(boss.Biome) * 5 + 3 + difficulty),
                WaveStyle = "Normal",
                MaxCreaturesPerPhase = maxCreatures,
                EnableCreatureDrops = false,
                WaveStartMessage = "$clans_wb_start",
                WaveEndMessage = "$clans_wb_victory",
                DrawMapOverlay = true,
            };
            if (legacyMode) {
                // No final phase or health multiplier before schema 4: boss mode puts the biome's boss in the last
                // wave, a rule sets its stars and abilities, and LegacyRun raises its health once it appears.
                definition.NumPhases = (short)settings.Waves;
                definition.BossMode = true;
                definition.CreatureRules = new List<VFCreatureRule> {
                    new VFCreatureRule { Waves = new List<int> { -1 }, SpawnTypes = new List<string> { "unique" }, MinStars = stars, Modifiers = modifiers },
                };
            } else {
                definition.NumPhases = (short)(settings.Waves - 1);
                definition.SpawnPortalVisuals = true;
                definition.FinalPhase = new List<VFHoardEntry> {
                    new VFHoardEntry {
                        Creature = boss.FortressCreature,
                        Amount = 1,
                        Stars = stars,
                        Modifiers = modifiers,
                        HealthMultiplier = BossHealth(info),
                    },
                };
            }
            return definition;
        }

        internal static float BossHealth(ActivityInfo info) {
            WorldBossSettings settings = ClansSettings.Activities.WorldBoss;
            float scale = 1f + Mathf.Max(0f, settings.PerParticipantScale) * Mathf.Max(0, info.Participants - 1);
            return Mathf.Max(1f, settings.BossHealthMultiplier * scale);
        }

        // Random Star Level System boss abilities; none without SLS.
        private static VFModifierSet BossModifiers(int count) {
            if (count <= 0 || SlsApi.IsAvailable == false) { return null; }
            List<string> possible;
            try {
                possible = SlsApi.GetPossibleModifiers(2);
            } catch (Exception e) {
                Logger.LogWarning($"Could not read Star Level System boss abilities: {e.Message}");
                return null;
            }
            if (possible == null || possible.Count == 0) { return null; }
            List<string> pool = new List<string>(possible);
            List<string> chosen = new List<string>();
            while (chosen.Count < count && pool.Count > 0) {
                int pick = UnityEngine.Random.Range(0, pool.Count);
                chosen.Add(pool[pick]);
                pool.RemoveAt(pick);
            }
            return new VFModifierSet { Boss = chosen };
        }

        // A ring of portal spots around the totem, evenly spread, each on open ground and MinPortalSpacing from the
        // others. A spot that will not come clear in its own part of the ring is looked for anywhere on it; one
        // that still will not is left out, so a crowded forest gets fewer portals rather than portals in trees.
        private Vector3[] SpawnPoints(int count) {
            Vector3 center = totem.transform.position;
            List<Vector3> points = new List<Vector3>();
            count = Mathf.Max(1, count);
            for (int i = 0; i < count; i++) {
                for (int attempt = 0; attempt < AttemptsPerPortal; attempt++) {
                    // Wider in angle and distance with each attempt.
                    float spread = Mathf.Lerp(0.3f, 0.5f, attempt / (float)AttemptsPerPortal);
                    float angle = (i + UnityEngine.Random.Range(-spread, spread)) * Mathf.PI * 2f / count;
                    if (TryPortalSpot(center, angle, Widened(attempt, AttemptsPerPortal), points, out Vector3 spot)) {
                        points.Add(spot);
                        break;
                    }
                }
            }
            for (int attempt = 0; attempt < FillAttempts && points.Count < count; attempt++) {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                if (TryPortalSpot(center, angle, Widened(attempt, FillAttempts), points, out Vector3 spot)) { points.Add(spot); }
            }
            if (points.Count < count) {
                Logger.LogDebug($"Only {points.Count} of {count} World Boss portal spots around {center} are clear.");
            }
            if (points.Count == 0) { points.Add(center + new Vector3(RingMin, 0f, 0f)); }
            return points.ToArray();
        }

        // A distance from the totem, the ring widening inward and outward as attempts run out.
        private static float Widened(int attempt, int attempts) {
            float t = attempt / (float)Mathf.Max(1, attempts - 1);
            return UnityEngine.Random.Range(RingMin * (1f - 0.4f * t), RingMax * (1f + 0.3f * t));
        }

        private static bool TryPortalSpot(Vector3 center, float angle, float radius, List<Vector3> taken, out Vector3 spot) {
            spot = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            foreach (Vector3 other in taken) {
                if (Utils.DistanceXZ(other, spot) < MinPortalSpacing) { return false; }
            }
            return PortalSpotClear(ref spot);
        }

        // Whether a portal can stand here, with spot moved onto the ground.
        private static bool PortalSpotClear(ref Vector3 spot) =>
            Placement.Clear(ref spot, PortalClearRadius, PortalClearHeight, PortalMinUpright, PortalMaxStep);

        // Spawn points as stored on the totem: "x,y,z;x,y,z;...".
        internal static string FormatPoints(Vector3[] points) {
            List<string> parts = new List<string>(points.Length);
            foreach (Vector3 p in points) {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.##}", p.x, p.y, p.z));
            }
            return string.Join(";", parts);
        }

        internal static List<Vector3> ParsePoints(string text) {
            List<Vector3> points = new List<Vector3>();
            if (string.IsNullOrEmpty(text)) { return points; }
            foreach (string part in text.Split(';')) {
                string[] xyz = part.Split(',');
                if (xyz.Length == 3
                    && float.TryParse(xyz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(xyz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    && float.TryParse(xyz[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) {
                    points.Add(new Vector3(x, y, z));
                }
            }
            return points;
        }

        // ---------------------------------------------------------------- follow

        private void Follow(ActivityInfo info, VFChallengeState state) {
            if (state == null) {
                Report(Mathf.Max(0, lastPhase), StatusLost);
                return;
            }
            if (state.Exists == false) {
                if (++missing >= MissingSecondsToFail) { Report(Mathf.Max(0, lastPhase), StatusLost); }
                return;
            }
            missing = 0;
            if (state.Completed) {
                Report(Mathf.Max(state.Phases, lastPhase), StatusWon);
                return;
            }
            if (state.Stopped) {
                Report(Mathf.Max(0, lastPhase), StatusLost);
                return;
            }
            sinceReport++;
            if (state.Phase != lastPhase || sinceReport >= ReportEverySeconds) {
                lastPhase = state.Phase;
                sinceReport = 0;
                Report(state.Phase, StatusRunning);
            }
        }

        private void Stop() {
            if (stopped) { return; }
            stopped = true;
            try {
                if (handle.StartsWith(LegacyHandle)) {
                    legacy?.Stop();
                } else {
                    VfApi.StopChallenge(handle);
                }
            } catch (Exception e) {
                Logger.LogWarning($"Could not stop the World Boss waves: {e.Message}");
            }
        }

        private void Report(int phase, byte status) {
            if (status != StatusRunning) {
                ended = true;
                finalPhase = phase;
                finalStatus = status;
                resendIn = ResendEverySeconds;
            }
            Send(phase, status);
        }

        private void Send(int phase, byte status) {
            long id = activityId;
            string reported = handle ?? "";
            ClanRequests.Report(Op.ReportWorldBossState, pkg => {
                pkg.Write(id);
                pkg.Write(phase);
                pkg.Write(status);
                pkg.Write(reported);
            });
        }

        // ---------------------------------------------------------------- helpers

        private static WorldBossDef Boss(ActivityInfo info) => ClansSettings.Activities.WorldBoss.Find(info.BossId);

        private static int At(List<int> values, int index, int fallback) {
            if (values == null || values.Count == 0) { return fallback; }
            return values[Mathf.Clamp(index, 0, values.Count - 1)];
        }

        private static bool KnownCreature(string name) {
            if (string.IsNullOrEmpty(name)) { return false; }
            if (fortressCreatures == null || fortressCreatures.Count == 0) { fortressCreatures = VfApi.GetSpawnableCreatures() ?? new List<string>(); }
            return fortressCreatures.Contains(name);
        }

        private static int BiomeTier(Heightmap.Biome biome) {
            switch (biome) {
                case Heightmap.Biome.BlackForest: return 1;
                case Heightmap.Biome.Swamp: return 2;
                case Heightmap.Biome.Mountain: return 3;
                case Heightmap.Biome.Plains: return 4;
                case Heightmap.Biome.Mistlands: return 5;
                case Heightmap.Biome.AshLands: return 6;
                default: return 0;
            }
        }

        // ---------------------------------------------------------------- Valheim Fortress before 0.40.0

        // Follows a run started with RunChallenge, which gives nothing back: finds the run's object next to the
        // totem and reads its networked state. Those runs end only when every wave is cleared.
        private sealed class LegacyRun {
            private const string RunnerPrefab = "VF_api_challenge_runner";
            private const float FindRange = 10f;
            private const float BossRange = 120f;
            private const string BoostedKey = "clans_wb_boosted";

            private static readonly int RunnerHash = RunnerPrefab.GetStableHashCode();

            private readonly Transform totem;
            private readonly WorldBossDef boss;
            private readonly float bossHealth;
            private ZDOID runner = ZDOID.None;
            private bool seenActive;
            private int lastPhase;

            internal LegacyRun(Transform totem, WorldBossDef boss, ActivityInfo info) {
                this.totem = totem;
                this.boss = boss;
                bossHealth = BossHealth(info);
            }

            internal VFChallengeState Read(ActivityInfo info) {
                int waves = ClansSettings.Activities.WorldBoss.Waves;
                if (runner == ZDOID.None) { runner = FindRunner(); }
                ZDO zdo = runner == ZDOID.None ? null : ZDOMan.instance.GetZDO(runner);
                if (zdo == null) {
                    // The run removes itself a little after its last wave is cleared.
                    if (seenActive && lastPhase >= waves) { return new VFChallengeState { Exists = true, Completed = true, Phase = waves, Phases = waves }; }
                    return new VFChallengeState { Exists = false };
                }
                bool active = zdo.GetBool("shrine_challenge_active");
                lastPhase = Mathf.Max(lastPhase, zdo.GetInt("shrine_current_phase"));
                seenActive |= active;
                if (active && lastPhase >= waves) { BoostBoss(); }
                return new VFChallengeState {
                    Exists = true,
                    Starting = !seenActive,
                    Active = active,
                    Phase = lastPhase,
                    Phases = waves,
                    Alive = zdo.GetInt("spawned_creatures"),
                    Completed = seenActive && active == false && lastPhase >= waves,
                };
            }

            private ZDOID FindRunner() {
                if (ZNetScene.instance == null) { return ZDOID.None; }
                foreach (KeyValuePair<ZDO, ZNetView> entry in ZNetScene.instance.m_instances) {
                    if (entry.Key == null || entry.Key.GetPrefab() != RunnerHash) { continue; }
                    if (Utils.DistanceXZ(entry.Key.GetPosition(), totem.position) <= FindRange) { return entry.Key.m_uid; }
                }
                return ZDOID.None;
            }

            // The boss of a legacy run gets its extra health here, once, by whoever owns it.
            private void BoostBoss() {
                if (boss == null || string.IsNullOrEmpty(boss.Prefab)) { return; }
                foreach (Character character in Character.GetAllCharacters()) {
                    if (character == null || character.m_nview == null || character.m_nview.IsValid() == false || character.m_nview.IsOwner() == false) { continue; }
                    if (Utils.GetPrefabName(character.gameObject) != boss.Prefab) { continue; }
                    if (Utils.DistanceXZ(character.transform.position, totem.position) > BossRange) { continue; }
                    ZDO zdo = character.m_nview.GetZDO();
                    if (zdo.GetBool(BoostedKey)) { continue; }
                    zdo.Set(BoostedKey, true);
                    bool done = false;
                    if (SlsApi.IsAvailable) {
                        try {
                            float current = SlsApi.GetCreatureBaseAttribute(character, 0);
                            if (current <= 0f) { current = 1f; }
                            done = SlsApi.SetCreatureBaseAttribute(character, 0, current * bossHealth) && SlsApi.ApplyCreatureUpdates(character);
                        } catch (Exception e) {
                            Logger.LogWarning($"Star Level System could not raise the World Boss's health: {e.Message}");
                        }
                    }
                    if (done == false) {
                        float health = character.GetMaxHealth() * bossHealth;
                        character.SetMaxHealth(health);
                        character.SetHealth(health);
                    }
                }
            }

            // Older Valheim Fortress has no stop; the best that can be done is to take the run's object over and
            // end it, which stops further waves. Creatures already out stay.
            internal void Stop() {
                ZDO zdo = runner == ZDOID.None ? null : ZDOMan.instance.GetZDO(runner);
                if (zdo == null) { return; }
                GameObject instance = ZNetScene.instance.FindInstance(runner);
                ZNetView runnerView = instance != null ? instance.GetComponent<ZNetView>() : null;
                if (runnerView == null || runnerView.IsValid() == false) { return; }
                runnerView.ClaimOwnership();
                zdo.Set("shrine_challenge_active", false);
                zdo.Set("wave_definition_ready", false);
                ZNetScene.instance.Destroy(instance);
            }
        }
    }
}
