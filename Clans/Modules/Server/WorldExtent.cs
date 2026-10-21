using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace Clans.Server {
    // How big this world is, as Epic Loot's adventure code works it out (Adventure/WorldExtent).
    //
    // WorldGenerator.worldSize and waterEdge are constants the compiler copies into Clans.dll, so a world-size mod has
    // nothing to patch and reading them reports a 10 km world on any map. Expand World Size is asked through its own
    // settings (no reference to it); any other mod's world is measured by probing for the edge, past which every
    // height is the same deep-ocean value.
    internal static class WorldExtent {
        private const float VanillaPlayableRadius = 10000f;
        private const float VanillaTotalRadius = 10500f;
        // Expand World Size's default edge band; the probe can find the edge but not where the band starts.
        private const float AssumedEdgeSize = 500f;
        // A probe this close to vanilla is vanilla, so an unmodded world gets the exact numbers.
        private const float VanillaSnapTolerance = 50f;
        private const float ProbeIntervalSeconds = 30f;

        // Terrain is made out to here; past it the ground falls away into the edge ocean.
        internal static float PlayableRadius { get; private set; } = VanillaPlayableRadius;
        // Past here every height is the edge ocean's.
        internal static float TotalRadius { get; private set; } = VanillaTotalRadius;
        // Biome size: a larger radius alone gives more patches of the same size, only stretch makes them bigger.
        internal static float Stretch { get; private set; } = 1f;
        internal static string Source { get; private set; } = "vanilla";

        private static bool probed;
        private static float probedAt;
        private static bool probeFound;
        private static float probedTotal;

        private static bool resolvedEws;
        private static MethodInfo ewsWorldRadius;
        private static MethodInfo ewsTotalRadius;
        private static MethodInfo ewsStretch;

        // Cheap enough to call before every search, which is what catches a world resized while it runs (Expand World
        // Size can reload its settings at any time, and the seed stays the same).
        internal static void Refresh() {
            float playable = VanillaPlayableRadius;
            float total = VanillaTotalRadius;
            float stretch = 1f;
            string source = "vanilla";
            if (TryReadExpandWorldSize(ref playable, ref total, ref stretch)) {
                source = "Expand World Size";
            } else if (TryProbeThrottled(out float found) && Mathf.Abs(found - VanillaTotalRadius) > VanillaSnapTolerance) {
                total = found;
                playable = Mathf.Max(1f, found - AssumedEdgeSize);
                source = "probe";
            }
            // A settings file caught mid-reload can say anything; keep the last good numbers.
            if (!(playable > 0f) || !(total >= playable) || !(stretch > 0f)) {
                Logger.LogWarning($"Ignoring an implausible world size from {source} (playable {playable}, total {total}, stretch {stretch}).");
                return;
            }
            if (Mathf.Approximately(playable, PlayableRadius) && Mathf.Approximately(total, TotalRadius)
                && Mathf.Approximately(stretch, Stretch) && source == Source) {
                return;
            }
            PlayableRadius = playable;
            TotalRadius = total;
            Stretch = stretch;
            Source = source;
            Logger.LogInfo($"World size for clan activities: {Describe()}.");
        }

        // On world change: the throttle would otherwise report the last world's size for a while.
        internal static void Reset() {
            probed = false;
            probeFound = false;
        }

        internal static string Describe() => $"playable {PlayableRadius:0} m, total {TotalRadius:0} m, stretch {Stretch:0.###} ({Source})";

        private static bool TryReadExpandWorldSize(ref float playable, ref float total, ref float stretch) {
            try {
                if (resolvedEws == false) {
                    resolvedEws = true;
                    if (Chainloader.PluginInfos.TryGetValue("expand_world_size", out BepInEx.PluginInfo info) && info?.Instance != null) {
                        Type configuration = info.Instance.GetType().Assembly.GetType("ExpandWorldSize.Configuration");
                        if (configuration != null) {
                            ewsWorldRadius = AccessTools.PropertyGetter(configuration, "WorldRadius");
                            ewsTotalRadius = AccessTools.PropertyGetter(configuration, "WorldTotalRadius");
                            ewsStretch = AccessTools.PropertyGetter(configuration, "WorldStretch");
                        }
                    }
                }
                if (ewsWorldRadius == null || ewsTotalRadius == null || ewsStretch == null) { return false; }
                playable = (float)ewsWorldRadius.Invoke(null, null);
                total = (float)ewsTotalRadius.Invoke(null, null);
                stretch = (float)ewsStretch.Invoke(null, null);
                return true;
            } catch (Exception e) {
                // Its internals moved: measure the world instead.
                Logger.LogWarning($"Could not read Expand World Size's settings ({e.Message}); measuring the world instead.");
                ewsWorldRadius = null;
                ewsTotalRadius = null;
                ewsStretch = null;
                return false;
            }
        }

        private static bool TryProbeThrottled(out float total) {
            if (probed == false || Time.unscaledTime - probedAt >= ProbeIntervalSeconds) {
                probed = true;
                probedAt = Time.unscaledTime;
                probeFound = TryProbe(out probedTotal);
            }
            total = probedTotal;
            return probeFound;
        }

        // Past the edge GetHeight is exactly -2 x the height multiplier (-400), far below the deepest ocean (about
        // -26), so the edge is found by halving the gap: about 40 height samples.
        private static bool TryProbe(out float total) {
            total = VanillaTotalRadius;
            WorldGenerator world = WorldGenerator.instance;
            if (world == null) { return false; }
            float outsideHeight = -2f * WorldGenerator.GetHeightMultiplier();
            float inside = 0f;
            float outside = VanillaTotalRadius;
            while (IsOutside(world, outside, outsideHeight) == false) {
                inside = outside;
                outside *= 2f;
                // No edge anywhere sensible: something else is changing heights.
                if (outside > 1000000f) { return false; }
            }
            // Smaller than vanilla is allowed too.
            if (IsOutside(world, inside, outsideHeight)) { inside = 0f; }
            for (int i = 0; i < 40 && outside - inside > 1f; i++) {
                float middle = (inside + outside) * 0.5f;
                if (IsOutside(world, middle, outsideHeight)) {
                    outside = middle;
                } else {
                    inside = middle;
                }
            }
            total = inside;
            return total > 0f;
        }

        private static bool IsOutside(WorldGenerator world, float radius, float outsideHeight) =>
            Mathf.Approximately(world.GetHeight(radius, 0f), outsideHeight) && Mathf.Approximately(world.GetHeight(0f, radius), outsideHeight);
    }
}
