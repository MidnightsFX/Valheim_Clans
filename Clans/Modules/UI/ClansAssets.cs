using Clans.Settings;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans.UI {
    // The Clans asset bundle (UI prefabs, emblem images, icons) and sprite lookups for emblem parts.
    //
    // Clients only: a dedicated server never draws any of it, so it never loads the bundle. The bundle is
    // built by ClansUnity's "Clans/Build Asset Bundle" menu, which copies it to Clans/Assets/clans; a build
    // without it still works, minus everything drawn from it.
    internal static class ClansAssets {
        private const string BundleResource = "Clans.Assets.clans";

        private static bool loaded;
        private static AssetBundle bundle;
        private static readonly Dictionary<string, Sprite> bundleSprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> resolved = new Dictionary<string, Sprite>();
        // Sprite names not found, and when to look again (an item icon can be missing only because ObjectDB is
        // not ready yet); each is reported once.
        private const float MissingRetrySeconds = 10f;
        private static readonly Dictionary<string, float> missingUntil = new Dictionary<string, float>();
        private static readonly HashSet<string> reported = new HashSet<string>();

        internal static void Init() {
            // Loaded on first use rather than here: the game's own UI sprites, which the bundle's placeholder
            // art is swapped for, do not exist yet during plugin Awake.
            ClansSettings.Changed += () => resolved.Clear();
        }

        internal static AssetBundle Bundle {
            get {
                Load();
                return bundle;
            }
        }

        private static void Load() {
            if (loaded) { return; }
            loaded = true;
            if (GUIManager.IsHeadless()) { return; }
            if (typeof(Clans).Assembly.GetManifestResourceNames().Any(n => n.EndsWith(BundleResource)) == false) {
                Logger.LogWarning("This Clans build has no asset bundle; the Clans window and emblems cannot be shown.");
                return;
            }
            bundle = AssetUtils.LoadAssetBundleFromResources(BundleResource, typeof(Clans).Assembly);
            if (bundle == null) {
                Logger.LogError("The Clans asset bundle failed to load.");
                return;
            }
            Clans.EmbeddedResourceBundle = bundle;
            foreach (Sprite sprite in bundle.LoadAllAssets<Sprite>()) {
                bundleSprites[sprite.name] = sprite;
            }
            Logger.LogDebug($"Clans asset bundle loaded: {bundleSprites.Count} sprites.");
        }

        internal static GameObject Prefab(string name) {
            AssetBundle source = Bundle;
            return source == null ? null : source.LoadAsset<GameObject>(name);
        }

        internal static Sprite BundleSprite(string name) {
            Load();
            bundleSprites.TryGetValue(name ?? "", out Sprite sprite);
            return sprite;
        }

        // An emblem part's image from its catalog source ("bundle:x", "vanilla:x", "item:Prefab"). Null when
        // it cannot be found, which the caller draws as an empty layer.
        internal static Sprite EmblemSprite(EmblemPart part) {
            if (part == null || string.IsNullOrEmpty(part.Sprite)) { return null; }
            if (resolved.TryGetValue(part.Sprite, out Sprite cached)) { return cached; }
            if (missingUntil.TryGetValue(part.Sprite, out float retry) && Time.unscaledTime < retry) { return null; }

            Sprite sprite = null;
            int colon = part.Sprite.IndexOf(':');
            string kind = colon > 0 ? part.Sprite.Substring(0, colon).ToLowerInvariant() : "bundle";
            string name = colon > 0 ? part.Sprite.Substring(colon + 1) : part.Sprite;
            try {
                switch (kind) {
                    case "bundle":
                        sprite = BundleSprite(name);
                        break;
                    case "vanilla":
                        sprite = GUIManager.Instance != null ? GUIManager.Instance.GetSprite(name) : null;
                        break;
                    case "item":
                        GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null;
                        ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                        sprite = drop != null && drop.m_itemData.m_shared.m_icons.Length > 0 ? drop.m_itemData.m_shared.m_icons[0] : null;
                        break;
                }
            } catch (Exception e) {
                Logger.LogDebug($"Emblem sprite '{part.Sprite}' failed: {e.Message}");
            }
            if (sprite == null) {
                missingUntil[part.Sprite] = Time.unscaledTime + MissingRetrySeconds;
                if (reported.Add(part.Sprite)) { Logger.LogWarning($"Emblem sprite '{part.Sprite}' was not found."); }
                return null;
            }
            resolved[part.Sprite] = sprite;
            return sprite;
        }
    }
}
