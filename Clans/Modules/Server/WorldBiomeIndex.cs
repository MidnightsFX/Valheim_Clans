using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace Clans.Server {
    // A coarse map of where each biome is, sampled from the world seed alone, after Epic Loot's world biome index
    // (Adventure/WorldBiomeIndex).
    //
    // Activity spots used to be found by throwing thousands of random darts at the world and keeping the first that
    // landed in a wanted biome on good ground. A biome that covers little of the world (the Ashlands cap, the
    // Mistlands ring), or a ring around a race's start that holds little of it, could use up every dart and leave the
    // activity unplaced. WorldGenerator.GetBiome depends only on the seed and the position, so the whole world is
    // sampled once on a grid instead, a few milliseconds a frame after the world loads, and a search then only ever
    // looks at cells known to be in a wanted biome (WorldPlacement).
    internal static class WorldBiomeIndex {
        internal struct Cell {
            internal Heightmap.Biome Biome;
            internal Vector2 At;
        }

        private enum State { NotBuilt, Building, Ready, Failed }

        // Two zones wide on a vanilla world: biome noise runs at 1/1000 scale, so real patches are hundreds of metres
        // across.
        private const float DefaultCellSize = 128f;
        // So an enormous world cannot make the build endless.
        private const int MaxCells = 400000;
        private const double FrameBudgetMs = 2.0;
        // How often a built index checks the world is still the one it was built for.
        private const float StaleCheckSeconds = 10f;

        private static readonly Dictionary<Heightmap.Biome, Vector2[]> cells = new Dictionary<Heightmap.Biome, Vector2[]>();
        private static readonly Dictionary<Heightmap.Biome, List<Vector2>> building = new Dictionary<Heightmap.Biome, List<Vector2>>();

        private static State state = State.NotBuilt;
        private static float staleTimer;

        // What the index was built for: a world-size mod can resize the world without the seed changing.
        private static int seed;
        private static float stampTotal;
        private static float stampStretch;

        private static float cellSize = DefaultCellSize;
        private static float buildRadius;
        private static int steps;
        private static int row;
        private static int sampled;
        private static double buildMs;
        private static int buildFrames;

        internal static float CellSize => cellSize;

        // Every frame on the server: builds a missing or stale index a little at a time.
        internal static void Tick(float dt) {
            if (state == State.Building) {
                Step(FrameBudgetMs);
                return;
            }
            staleTimer -= dt;
            if (staleTimer > 0f) { return; }
            staleTimer = StaleCheckSeconds;
            if (Stale()) { Begin(); }
        }

        // For a search: finishes the build at once if it is still under way.
        internal static bool EnsureReady() {
            if (state != State.Building && Stale()) { Begin(); }
            if (state == State.Building) { Step(double.MaxValue); }
            return state == State.Ready;
        }

        // On world unload: every cell is a position in that world.
        internal static void Reset() {
            WorldExtent.Reset();
            cells.Clear();
            building.Clear();
            state = State.NotBuilt;
            staleTimer = 0f;
            seed = 0;
        }

        // The cells of the biomes (all of them when none are given) inside the world's playable part and, with a
        // centre, between minDistance and maxDistance of it.
        internal static void Collect(ICollection<Heightmap.Biome> biomes, Vector3? center, float minDistance, float maxDistance, List<Cell> into) {
            into.Clear();
            if (state != State.Ready) { return; }
            float edge = WorldExtent.PlayableRadius - cellSize;
            float edgeSq = edge * edge;
            float minSq = minDistance * minDistance;
            float maxSq = maxDistance * maxDistance;
            foreach (KeyValuePair<Heightmap.Biome, Vector2[]> bucket in cells) {
                if (biomes != null && biomes.Count > 0 && biomes.Contains(bucket.Key) == false) { continue; }
                foreach (Vector2 at in bucket.Value) {
                    if (at.sqrMagnitude > edgeSq) { continue; }
                    if (center.HasValue) {
                        float dx = at.x - center.Value.x;
                        float dz = at.y - center.Value.z;
                        float distanceSq = dx * dx + dz * dz;
                        if (distanceSq < minSq || distanceSq > maxSq) { continue; }
                    }
                    into.Add(new Cell { Biome = bucket.Key, At = at });
                }
            }
        }

        internal static int CountCells(Heightmap.Biome biome) => cells.TryGetValue(biome, out Vector2[] list) ? list.Length : 0;

        internal static string Describe() => state == State.Ready
            ? $"{sampled} cells on a {cellSize:0} m grid ({string.Join(", ", cells.Select(c => $"{c.Key} {c.Value.Length}"))})"
            : $"index {state}";

        private static bool Stale() {
            WorldGenerator world = WorldGenerator.instance;
            if (world == null || ZoneSystem.instance == null) { return false; }
            WorldExtent.Refresh();
            if (state == State.NotBuilt) { return true; }
            return seed != world.GetSeed() || Mathf.Approximately(stampTotal, WorldExtent.TotalRadius) == false
                || Mathf.Approximately(stampStretch, WorldExtent.Stretch) == false;
        }

        // Patch size follows stretch, not radius; the budget term keeps a huge world's grid within MaxCells.
        private static void Begin() {
            WorldGenerator world = WorldGenerator.instance;
            if (world == null) {
                state = State.NotBuilt;
                return;
            }
            seed = world.GetSeed();
            stampTotal = WorldExtent.TotalRadius;
            stampStretch = WorldExtent.Stretch;
            buildRadius = WorldExtent.TotalRadius;
            float byFeature = DefaultCellSize * Mathf.Clamp(WorldExtent.Stretch, 0.5f, 8f);
            float byBudget = Mathf.Sqrt(Mathf.PI * buildRadius * buildRadius / MaxCells);
            cellSize = Mathf.Max(byFeature, byBudget);
            steps = Mathf.CeilToInt(buildRadius * 2f / cellSize);
            row = 0;
            sampled = 0;
            buildMs = 0;
            buildFrames = 0;
            building.Clear();
            state = State.Building;
        }

        // Samples rows until the budget is spent.
        private static void Step(double budgetMs) {
            WorldGenerator world = WorldGenerator.instance;
            // The world changed under the build: start over on the next check.
            if (world == null || world.GetSeed() != seed) {
                building.Clear();
                state = State.NotBuilt;
                return;
            }
            Stopwatch watch = Stopwatch.StartNew();
            float radiusSq = buildRadius * buildRadius;
            try {
                while (row < steps) {
                    float z = -buildRadius + (row + 0.5f) * cellSize;
                    for (int column = 0; column < steps; column++) {
                        float x = -buildRadius + (column + 0.5f) * cellSize;
                        if (x * x + z * z > radiusSq) { continue; }
                        Heightmap.Biome biome = world.GetBiome(x, z);
                        sampled++;
                        if (biome == Heightmap.Biome.None) { continue; }
                        if (building.TryGetValue(biome, out List<Vector2> list) == false) {
                            list = new List<Vector2>();
                            building[biome] = list;
                        }
                        list.Add(new Vector2(x, z));
                    }
                    row++;
                    if (row < steps && watch.Elapsed.TotalMilliseconds >= budgetMs) {
                        buildMs += watch.Elapsed.TotalMilliseconds;
                        buildFrames++;
                        return;
                    }
                }
            } catch (Exception e) {
                Logger.LogError($"Could not sample the world's biomes for clan activities: {e}");
                building.Clear();
                state = State.Failed;
                return;
            }
            buildMs += watch.Elapsed.TotalMilliseconds;
            buildFrames++;
            cells.Clear();
            foreach (KeyValuePair<Heightmap.Biome, List<Vector2>> bucket in building) { cells[bucket.Key] = bucket.Value.ToArray(); }
            building.Clear();
            state = State.Ready;
            Logger.LogInfo($"World biome index for clan activities built in {buildMs:0} ms over {buildFrames} frames: {Describe()}.");
        }
    }
}
