using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans.Server {
    // Finds the general area for an activity totem: on land, in one of the wanted biomes, preferably fairly flat, away
    // from player buildings and from the game's own locations. Works from the world generator alone, so the spot does
    // not need to be loaded, and it knows nothing of trees, rocks or buildings standing there: a placer goes here
    // (Gameplay.TotemPlacer), which puts the totem on open ground nearby once the area is loaded.
    //
    // Spots are drawn from the world biome index, so every one tried is in a wanted biome. As in Epic Loot's adventure
    // spots, a cell's centre is tried first (the one point sure to be in its biome) and a few points around it only
    // once it is good, for variety. A spot that meets every preference wins at once; when none of the cells tried does,
    // the one missing the least is used, so a biome that is in reach always gives a spot.
    internal static class WorldPlacement {
        private const float MaxSlope = 3f;
        private const float SlopeProbe = 4f;
        private const float MinAboveWater = 2f;
        // Cells looked at before taking the best found.
        private const int MaxCellsTried = 256;
        private const int VarietyAttempts = 4;
        private const float BuildingRange = 120f;
        // Lava in the Ashlands height mask, as Epic Loot reads it.
        private const float LavaMask = 0.6f;

        // What a preference costs when a spot misses it.
        private const int MixedZoneCost = 1;
        private const int SteepCost = 1;
        private const int LocationCost = 1;
        // More than the others together: a rough spot is better than one beside someone's home.
        private const int BuildingCost = 4;

        private enum Unusable { None, OutsideWorld, OtherBiome, Water, Lava }

        private static readonly List<ZDO> nearby = new List<ZDO>();
        private static readonly List<WorldBiomeIndex.Cell> cells = new List<WorldBiomeIndex.Cell>();

        // A spot anywhere in the world, or between minDistance and maxDistance from center when one is given, and the
        // biome it was chosen for.
        internal static bool TryFind(ICollection<Heightmap.Biome> biomes, out Vector3 point, out Heightmap.Biome biome,
                Vector3? center = null, float minDistance = 0f, float maxDistance = 0f) {
            point = Vector3.zero;
            biome = Heightmap.Biome.None;
            WorldGenerator world = WorldGenerator.instance;
            if (world == null || ZoneSystem.instance == null) { return false; }
            if (WorldBiomeIndex.EnsureReady() == false) {
                Logger.LogWarning($"No spot for a clan activity: the world biome index is not available ({WorldBiomeIndex.Describe()}).");
                return false;
            }
            WorldBiomeIndex.Collect(biomes, center, minDistance, maxDistance, cells);
            string where = center.HasValue ? $" {minDistance:0}-{Mathf.Max(minDistance, maxDistance):0} m from {center.Value:F0}" : "";
            if (cells.Count == 0) {
                Logger.LogWarning($"No {Names(biomes)} in the world{where} ({WorldBiomeIndex.Describe()}, {WorldExtent.Describe()}).");
                return false;
            }

            int bestCost = int.MaxValue;
            int water = 0;
            int lava = 0;
            int tries = Mathf.Min(cells.Count, MaxCellsTried);
            for (int i = 0; i < tries; i++) {
                // A random cell not tried yet; those tried are kept at the front.
                int pick = Random.Range(i, cells.Count);
                WorldBiomeIndex.Cell cell = cells[pick];
                cells[pick] = cells[i];
                cells[i] = cell;
                int cost = Cost(world, cell.Biome, cell.At, bestCost, out Vector3 at, out Unusable why);
                if (why == Unusable.Water) { water++; }
                if (why == Unusable.Lava) { lava++; }
                if (why != Unusable.None || cost >= bestCost) { continue; }
                bestCost = cost;
                point = at;
                biome = cell.Biome;
                if (cost == 0) { break; }
            }
            if (bestCost == int.MaxValue) {
                Logger.LogWarning($"No dry land among {tries} of the {cells.Count} {Names(biomes)} cells{where} ({water} under water, {lava} lava).");
                return false;
            }
            if (bestCost == 0) {
                point = Varied(world, biome, point, center, minDistance, maxDistance);
            } else {
                Logger.LogDebug($"No {Names(biomes)} spot{where} is flat, all one biome and clear of buildings and locations; using {point:F0}, which comes closest.");
            }
            return true;
        }

        // Somewhere else in the cell that is just as good, so activities do not all sit on the sample grid.
        private static Vector3 Varied(WorldGenerator world, Heightmap.Biome biome, Vector3 point, Vector3? center, float minDistance, float maxDistance) {
            Vector2 cellCenter = new Vector2(point.x, point.z);
            for (int i = 0; i < VarietyAttempts; i++) {
                Vector2 at = cellCenter + Random.insideUnitCircle * WorldBiomeIndex.CellSize * 0.5f;
                if (at.magnitude > WorldExtent.PlayableRadius - WorldBiomeIndex.CellSize) { continue; }
                if (center.HasValue) {
                    float distance = Vector2.Distance(at, new Vector2(center.Value.x, center.Value.z));
                    if (distance < minDistance || distance > maxDistance) { continue; }
                }
                if (Cost(world, biome, at, 1, out Vector3 varied, out _) == 0) { return varied; }
            }
            return point;
        }

        // What a spot misses of the preferences (0 for none), or why nothing may stand there at all. Cheapest tests
        // first; the search for player buildings is skipped once the cost reaches limit, as the spot is no better then.
        private static int Cost(WorldGenerator world, Heightmap.Biome biome, Vector2 at, int limit, out Vector3 point, out Unusable why) {
            point = Vector3.zero;
            why = Unusable.None;
            if (at.magnitude > WorldExtent.PlayableRadius - WorldBiomeIndex.CellSize) {
                why = Unusable.OutsideWorld;
                return int.MaxValue;
            }
            if (world.GetBiome(at.x, at.y) != biome) {
                why = Unusable.OtherBiome;
                return int.MaxValue;
            }
            float height = world.GetBiomeHeight(biome, at.x, at.y, out Color mask);
            if (height < ZoneSystem.instance.m_waterLevel + MinAboveWater) {
                why = Unusable.Water;
                return int.MaxValue;
            }
            // The mask's alpha means lava only in the Ashlands; vanilla checks the biome first too.
            if (biome == Heightmap.Biome.AshLands && mask.a > LavaMask) {
                why = Unusable.Lava;
                return int.MaxValue;
            }
            point = new Vector3(at.x, height, at.y);
            int cost = 0;
            if (ZoneAllOneBiome(world, point, biome) == false) { cost += MixedZoneCost; }
            if (Steep(world, at, height)) { cost += SteepCost; }
            if (NearLocation(point)) { cost += LocationCost; }
            if (cost < limit && NearPlayerBuilding(point)) { cost += BuildingCost; }
            return cost;
        }

        // A loaded zone's ground takes its biome from the zone's four corners, not from each point, so a spot well
        // inside a biome can still load as another. With all four corners agreeing, the placer sees the biome chosen
        // here (as Epic Loot found for its bounties).
        private static bool ZoneAllOneBiome(WorldGenerator world, Vector3 point, Heightmap.Biome biome) {
            Vector3 zone = ZoneSystem.GetZonePos(ZoneSystem.GetZone(point));
            const float half = ZoneSystem.c_ZoneSizeHalf;
            return world.GetBiome(zone.x - half, zone.z - half) == biome && world.GetBiome(zone.x + half, zone.z - half) == biome
                && world.GetBiome(zone.x - half, zone.z + half) == biome && world.GetBiome(zone.x + half, zone.z + half) == biome;
        }

        private static bool Steep(WorldGenerator world, Vector2 at, float height) {
            float[] dx = { SlopeProbe, -SlopeProbe, 0f, 0f };
            float[] dz = { 0f, 0f, SlopeProbe, -SlopeProbe };
            for (int i = 0; i < 4; i++) {
                if (Mathf.Abs(world.GetHeight(at.x + dx[i], at.y + dz[i]) - height) > MaxSlope) { return true; }
            }
            return false;
        }

        private static bool NearLocation(Vector3 point) {
            return ZoneSystem.instance != null && ZoneSystem.instance.m_locationInstances.ContainsKey(ZoneSystem.GetZone(point));
        }

        // Anything a player built (it records a creator) in the zones around the spot.
        private static bool NearPlayerBuilding(Vector3 point) {
            if (ZDOMan.instance == null) { return false; }
            nearby.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(point), SimulationDistance.OriginalDistance, nearby);
            foreach (ZDO zdo in nearby) {
                if (zdo.GetLong(ZDOVars.s_creator, 0L) != 0L && Vector3.Distance(zdo.GetPosition(), point) < BuildingRange) { return true; }
            }
            return false;
        }

        private static string Names(ICollection<Heightmap.Biome> biomes) =>
            biomes == null || biomes.Count == 0 ? "land" : string.Join("/", biomes.Select(b => b.ToString()));

        // Removes an object (a placer or a totem), wherever it is and whoever owns it now.
        internal static void DestroyObject(long user, uint id) {
            if (ZDOMan.instance == null || (user == 0 && id == 0)) { return; }
            ZDO zdo = ZDOMan.instance.GetZDO(new ZDOID(user, id));
            if (zdo == null) { return; }
            zdo.SetOwner(ZDOMan.GetSessionID());
            ZDOMan.instance.DestroyZDO(zdo);
        }
    }
}
