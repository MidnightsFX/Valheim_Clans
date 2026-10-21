using Clans.Client;
using Clans.Net;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Gameplay {
    // An activity's totem before it stands in the world, in the manner of Epic Loot's adventure spawn controller.
    //
    // The server puts this invisible marker in the general area, chosen from the world generator alone, which knows
    // nothing of trees, rocks or buildings. Whoever owns the marker once the area around it is loaded, as far as that
    // owner can load it (Placement.AreaSettled: its objects instantiated, their colliders in place), looks for open
    // ground near it in the biome the server chose (Placement.Check, within ClansPrefabs.PlacerRadius, then twice and
    // four times as far), puts the real totem there with the same stamp, and removes the marker. The server notices
    // the totem by its stamp (ActivityService.FindPlacedTotems).
    //
    // The first spot that breaks no rule is taken. When the whole reach has none (a dense forest, a cliff, a sprawling
    // base), the totem goes on the least bad spot found (Placement.Cost) rather than leaving the activity without one.
    internal sealed class TotemPlacer : MonoBehaviour {
        private const float PollSeconds = 0.5f;
        // Once the area reads as ready, a moment more for the colliders of what just appeared.
        private const float SettleSeconds = 1f;
        private const int SamplesPerRound = 150;
        private const int SamplesPerFrame = 10;
        // Nothing in reach could be looked at (not loaded here): look again after this long.
        private const float RetrySeconds = 30f;
        // Not too steep, and fairly level across the footprint.
        private const float MinUpright = 0.9f;
        private const float MaxStep = 1f;

        private enum Phase { Waiting, Settling, Searching, Resting }

        private static readonly List<ZDO> stamped = new List<ZDO>();

        private ZNetView view;
        private long activityId;
        private long clanId;
        private ClansPrefabs.TotemKind kind;
        private Phase phase;
        private float pollTimer;
        private float phaseUntil;
        private int round;
        private int samples;
        private bool warned;
        private Heightmap.Biome biome;
        // The least bad spot so far this search, for when none is clear.
        private int bestCost;
        private Vector3 best;
        private Placement.Faults bestFaults;
        // Zones whose objects are all instantiated (or not), worked out once per search.
        private readonly Dictionary<Vector2s, bool> readyZones = new Dictionary<Vector2s, bool>();

        private void Awake() {
            view = GetComponent<ZNetView>();
            if (view == null || view.GetZDO() == null) { return; }
            ZDO zdo = view.GetZDO();
            activityId = zdo.GetLong(ClansPrefabs.ActivityKey, 0L);
            clanId = zdo.GetLong(ClansPrefabs.ClanKey, 0L);
            kind = Totems.KindOf(zdo);
        }

        private void Update() {
            if (view == null || view.IsValid() == false || view.IsOwner() == false) {
                phase = Phase.Waiting;
                return;
            }
            switch (phase) {
                case Phase.Waiting:
                    pollTimer -= Time.deltaTime;
                    if (pollTimer > 0f) { return; }
                    pollTimer = PollSeconds;
                    if (Blocked() || Placement.AreaSettled(transform.position) == false) { return; }
                    phase = Phase.Settling;
                    phaseUntil = Time.time + SettleSeconds;
                    return;
                case Phase.Settling:
                    if (Time.time < phaseUntil) { return; }
                    if (Blocked()) {
                        phase = Phase.Waiting;
                        return;
                    }
                    StartSearch();
                    return;
                case Phase.Searching:
                    Search();
                    return;
                default:
                    if (Time.time >= phaseUntil) { phase = Phase.Waiting; }
                    return;
            }
        }

        // Whether to hold off. Removes the marker when its work is done: the activity is over, or its totem already
        // stands (put there by an owner that left before it could remove the marker).
        private bool Blocked() {
            if (ZNetScene.instance == null || ActivityClient.Received == false) { return true; }
            ActivityInfo info = ActivityClient.Get(activityId);
            bool done = info == null || info.Active == false;
            if (done == false) {
                Totems.FindStamped(activityId, transform.position, ClansPrefabs.PlacerReach + 10f, stamped);
                done = stamped.Exists(zdo => Totems.IsPlacer(zdo) == false && Totems.KindOf(zdo) == kind);
            }
            if (done) { ZNetScene.instance.Destroy(gameObject); }
            return done;
        }

        private void StartSearch() {
            phase = Phase.Searching;
            round = 0;
            samples = 0;
            bestCost = int.MaxValue;
            bestFaults = Placement.Faults.None;
            readyZones.Clear();
            // The biome the server chose this area for; a placer from before it said so goes by the ground here.
            biome = (Heightmap.Biome)view.GetZDO().GetInt(ClansPrefabs.BiomeKey, 0);
            if (biome == Heightmap.Biome.None) {
                Vector3 center = transform.position;
                ZoneSystem.instance.GetGroundData(ref center, out _, out biome, out _, out _);
            }
        }

        private void Search() {
            ClansPrefabs.Footprint(kind, out float radius, out float height);
            for (int i = 0; i < SamplesPerFrame; i++) {
                if (samples >= SamplesPerRound) {
                    samples = 0;
                    if (++round >= ClansPrefabs.PlacerRounds.Length) {
                        Settle();
                        return;
                    }
                }
                samples++;
                float reach = ClansPrefabs.PlacerRadius * ClansPrefabs.PlacerRounds[round];
                // The marker's own spot first, then anywhere in the disc, evenly by area.
                Vector2 offset = round == 0 && samples == 1 ? Vector2.zero : Random.insideUnitCircle * reach;
                Vector3 spot = transform.position + new Vector3(offset.x, 0f, offset.y);
                if (ZoneReady(ZoneSystem.GetZone(spot)) == false) { continue; }
                Placement.Faults faults = Placement.Check(ref spot, radius, height, MinUpright, MaxStep, biome);
                if (faults == Placement.Faults.None) {
                    Place(spot);
                    return;
                }
                int cost = Placement.Cost(faults);
                if (cost < bestCost) {
                    bestCost = cost;
                    best = spot;
                    bestFaults = faults;
                }
            }
        }

        // The whole reach was looked at and no spot broke no rule: the least bad one will do. Only when nothing in
        // reach could be looked at is it tried again later.
        private void Settle() {
            if (bestCost == int.MaxValue) {
                if (warned == false) {
                    warned = true;
                    Logger.LogWarning($"No ground for the clan activity's {kind} totem near {transform.position} has loaded here; looking again shortly.");
                }
                phase = Phase.Resting;
                phaseUntil = Time.time + RetrySeconds;
                return;
            }
            Logger.LogWarning($"No open ground for the clan activity's {kind} totem within {ClansPrefabs.PlacerReach:0} m of {transform.position}; "
                + $"it stands on the best spot found ({bestFaults}).");
            Place(best);
        }

        // Loaded here, with every object in it instantiated, so whatever stands there has its collider.
        private bool ZoneReady(Vector2s zone) {
            if (readyZones.TryGetValue(zone, out bool ready)) { return ready; }
            ready = Placement.ZoneSettled(zone);
            readyZones[zone] = ready;
            return ready;
        }

        private void Place(Vector3 spot) {
            ZDO totem = Totems.CreateObject(ClansPrefabs.TotemPrefab(kind), spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            Totems.Stamp(totem, activityId, kind, clanId);
            // Already on the ground as it is now.
            totem.Set(ClansPrefabs.SnappedKey, true);
            Logger.LogDebug($"Placed the clan activity's {kind} totem at {spot}, {Utils.DistanceXZ(spot, transform.position):0} m from its marker.");
            ZNetScene.instance.Destroy(gameObject);
        }
    }
}
