using System;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Gameplay {
    // Whether something may stand at a spot in the loaded world. These are the tests other mods use to put things in
    // the world (Epic Loot's adventure spawns, Star Level System's raids and summons, Valheim Fortress's remote
    // spawns):
    // - the ground must be loaded, dry, not lava, not too steep and fairly level across the footprint;
    // - the first solid surface straight down must be the ground itself, not a rock, tree or building over it;
    // - nothing solid (trees, rocks, bushes, buildings) may stand inside a box the object's size;
    // - it may not be in a player's base or inside a ward.
    // Check names every rule a spot breaks, and Cost weighs them, so a search that finds no spot breaking none can
    // still take the least bad one (as Epic Loot's adventure spawns fall back by tier and penalty).
    // Only meaningful where the area is loaded and its objects are instantiated (AreaSettled): the owner of a totem
    // placer or of a totem, never a server that has nothing instantiated there.
    internal static class Placement {
        [Flags]
        internal enum Faults {
            None = 0,
            // Not loaded here, so nothing standing on it could be seen: never used.
            NotLoaded = 1 << 0,
            WrongBiome = 1 << 1,
            // A little steeper or less level than asked for.
            Rough = 1 << 2,
            // Much steeper or less level than asked for.
            Steep = 1 << 3,
            Water = 1 << 4,
            Lava = 1 << 5,
            // Something solid inside the box.
            Blocked = 1 << 6,
            // A rock, tree or building over the ground.
            Covered = 1 << 7,
            PlayerBase = 1 << 8,
            Ward = 1 << 9,
        }

        // A spot on a root or a pebble still passes; one on a rock, a tree or a building does not.
        private const float MaxAboveGround = 0.5f;
        private const float MinAboveWater = 0.5f;
        // The box starts this far above the ground, so the terrain's own bumps inside it do not count.
        private const float BoxLift = 0.3f;
        // How far past the asked-for slope and step a spot is only Rough rather than Steep.
        private const float RoughUpright = 0.15f;
        private const float RoughStepFactor = 2f;

        private static readonly Vector2[] Edge = { new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f) };
        private static readonly List<ZDO> zoneObjects = new List<ZDO>();

        // radius and height: the object's footprint. minUpright: the ground's normal's least upward part (0.9 is about
        // 25 degrees of slope at most). maxStep: how much higher or lower the ground at the footprint's edge may be.
        // biome: None for any. On success spot is moved onto the ground.
        internal static bool Clear(ref Vector3 spot, float radius, float height, float minUpright, float maxStep, Heightmap.Biome biome = Heightmap.Biome.None) =>
            Check(ref spot, radius, height, minUpright, maxStep, biome) == Faults.None;

        // Every rule the spot breaks, with spot moved onto the ground when it is loaded.
        internal static Faults Check(ref Vector3 spot, float radius, float height, float minUpright, float maxStep, Heightmap.Biome biome = Heightmap.Biome.None) {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null) { return Faults.NotLoaded; }
            zones.GetGroundData(ref spot, out Vector3 normal, out Heightmap.Biome found, out _, out Heightmap heightmap);
            if (heightmap == null) { return Faults.NotLoaded; }
            Faults faults = Faults.None;
            if (biome != Heightmap.Biome.None && found != biome) { faults |= Faults.WrongBiome; }
            float ground = spot.y;
            if (ground < zones.m_waterLevel + MinAboveWater) { faults |= Faults.Water; }
            if (found == Heightmap.Biome.AshLands && heightmap.IsLava(spot)) { faults |= Faults.Lava; }
            if (normal.y < minUpright) { faults |= normal.y >= minUpright - RoughUpright ? Faults.Rough : Faults.Steep; }
            float step = 0f;
            foreach (Vector2 edge in Edge) {
                Vector3 at = new Vector3(spot.x + edge.x * radius, ground, spot.z + edge.y * radius);
                if (zones.GetGroundHeight(at, out float edgeGround) == false) { return Faults.NotLoaded; }
                step = Mathf.Max(step, Mathf.Abs(edgeGround - ground));
            }
            if (step > maxStep) { faults |= step <= maxStep * RoughStepFactor ? Faults.Rough : Faults.Steep; }
            if (zones.FindFloor(new Vector3(spot.x, ground + 100f, spot.z), out float solid) && solid - ground > MaxAboveGround) { faults |= Faults.Covered; }
            Vector3 center = new Vector3(spot.x, ground + BoxLift + height * 0.5f, spot.z);
            if (Physics.CheckBox(center, new Vector3(radius, height * 0.5f, radius), Quaternion.identity, zones.m_blockRayMask)) { faults |= Faults.Blocked; }
            if (EffectArea.IsPointInsideArea(spot, EffectArea.Type.PlayerBase, radius) != null) { faults |= Faults.PlayerBase; }
            foreach (PrivateArea ward in PrivateArea.m_allAreas) {
                if (ward != null && ward.IsInside(spot, radius)) {
                    faults |= Faults.Ward;
                    break;
                }
            }
            return faults;
        }

        // How bad a spot is: 0 when it breaks nothing, int.MaxValue when it cannot be used at all. Rough ground is the
        // least of it; a ward (whose owner's guests alone may use what stands inside) the most.
        internal static int Cost(Faults faults) {
            if ((faults & Faults.NotLoaded) != 0) { return int.MaxValue; }
            int cost = 0;
            if ((faults & Faults.Rough) != 0) { cost += 1; }
            if ((faults & Faults.WrongBiome) != 0) { cost += 2; }
            if ((faults & Faults.Steep) != 0) { cost += 3; }
            if ((faults & Faults.Blocked) != 0) { cost += 4; }
            if ((faults & Faults.Covered) != 0) { cost += 4; }
            if ((faults & Faults.PlayerBase) != 0) { cost += 6; }
            if ((faults & Faults.Water) != 0) { cost += 8; }
            if ((faults & Faults.Lava) != 0) { cost += 9; }
            if ((faults & Faults.Ward) != 0) { cost += 10; }
            return cost;
        }

        // Whether the area around a point has finished loading, as far as this machine can load it: the point's zone
        // and the eight around it, each loaded with every object in it instantiated, so their colliders are in place.
        // Vanilla's ZNetScene.IsAreaReady waits for all nine, but a client only instantiates objects within its own
        // near simulation area. On a low Simulation Distance setting (or the circular one, from a diagonal zone) some
        // of the nine never load, and a placer's owner standing right by it could wait forever. Zones this machine
        // cannot load are not waited on, as in Epic Loot's adventure spawn controller.
        internal static bool AreaSettled(Vector3 point) {
            if (ZNet.instance == null || ZNetScene.instance == null || ZoneSystem.instance == null || ZDOMan.instance == null) { return false; }
            // A dedicated server has no near area of its own; keep vanilla's rule there.
            if (ZNet.instance.IsDedicated()) { return ZNetScene.instance.IsAreaReady(point); }
            Vector2s center = ZoneSystem.GetZone(point);
            // The owner has to come closer before the point itself can load.
            if (InNearArea(center) == false) { return false; }
            for (int y = center.y - 1; y <= center.y + 1; y++) {
                for (int x = center.x - 1; x <= center.x + 1; x++) {
                    Vector2s zone = new Vector2s(x, y);
                    if (InNearArea(zone) && Instantiated(zone) == false) { return false; }
                }
            }
            return true;
        }

        // Whether things standing in a zone can be seen from here: it is in this machine's near area (where objects
        // are instantiated), loaded, and every object in it has been created.
        internal static bool ZoneSettled(Vector2s zone) {
            if (ZNet.instance == null || ZNetScene.instance == null || ZoneSystem.instance == null || ZDOMan.instance == null) { return false; }
            return (ZNet.instance.IsDedicated() || InNearArea(zone)) && Instantiated(zone);
        }

        // The shape ZDOMan.FindSectorObjects gives the near area: a square of NearSimulationDistance zones around the
        // reference position, trimmed to a circle unless the setting is classic.
        private static bool InNearArea(Vector2s zone) {
            Vector2s center = ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
            SimulationDistance distance = ZNet.instance.GetSyncedSimulationDistance();
            int near = distance.NearSimulationDistance;
            int ring = Mathf.Max(Mathf.Abs(zone.x - center.x), Mathf.Abs(zone.y - center.y));
            if (ring == 0) { return true; }
            return ring <= near && (distance.IsClassic || ZoneSystem.instance.ZonesWithinRadius(center, zone, near));
        }

        // Vanilla's IsAreaReady for one zone: loaded, and every object in it with a known prefab created.
        private static bool Instantiated(Vector2s zone) {
            if (ZoneSystem.instance.IsZoneLoaded(zone) == false) { return false; }
            zoneObjects.Clear();
            ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0, classic: true), zoneObjects);
            bool ready = true;
            foreach (ZDO zdo in zoneObjects) {
                if (ZNetScene.instance.IsPrefabZDOValid(zdo) && ZNetScene.instance.HaveInstance(zdo) == false) {
                    ready = false;
                    break;
                }
            }
            zoneObjects.Clear();
            return ready;
        }
    }
}
