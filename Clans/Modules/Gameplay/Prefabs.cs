using Clans.Settings;
using Jotunn.Entities;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Gameplay {
    // Registers the activity totems, their placer, the race chests, the clan banners and the guild feasts, on every
    // machine (a dedicated server included: it places placers as bare ZDOs, and every prefab must exist for anyone to
    // spawn them).
    //
    // Totems are copies of vanilla objects with everything interactive stripped (pieces, wear, item stands,
    // runestones, boss stones and any nested networked child), so they keep the look and nothing else.
    internal static class Prefabs {
        private static bool registered;

        private static readonly string[] RaceStartBases = { "BossStone_Eikthyr", "piece_chest_wood" };
        private static readonly string[] WorldBossBases = { "BossStone_Bonemass", "BossStone_Eikthyr", "piece_chest_wood" };
        private static readonly string[] RaceEndBases = { "piece_chest_blackmetal", "piece_chest", "piece_chest_wood" };
        private static readonly string[] ChestBases = { "chest_hildir1", "chest_hildir2", "chest_hildir3" };
        // The look the start totem has, stood beside the destination's chest so it can be seen from afar.
        private const string DestinationTotemBase = "BossStone_Eikthyr";
        private const float DestinationTotemGap = 0.3f;

        internal const int DestinationWidth = 8;
        internal const int DestinationHeight = 5;

        private static readonly List<CustomItem> chests = new List<CustomItem>();

        // A guild feast and the food its vanilla feast gives, kept for a feast feasts.yaml sets no food for.
        private sealed class GuildFeastPrefab {
            internal ClansPrefabs.GuildFeastVariant Variant;
            internal CustomItem Item;
            internal float Health;
            internal float Stamina;
            internal float Eitr;
        }

        private static readonly List<GuildFeastPrefab> guildFeasts = new List<GuildFeastPrefab>();

        // A clan banner as the hammer offers it: its colour, and the icon it keeps for players outside a clan.
        internal sealed class BannerPiece {
            internal Piece Piece;
            internal ClansPrefabs.BannerVariant Variant;
            internal Sprite VanillaIcon;
        }

        internal static readonly List<BannerPiece> BannerPieces = new List<BannerPiece>();

        internal static bool IsBanner(Piece piece) {
            if (piece == null) { return false; }
            foreach (BannerPiece banner in BannerPieces) {
                if (banner.Piece == piece) { return true; }
            }
            return false;
        }

        internal static void Init() {
            PrefabManager.OnVanillaPrefabsAvailable += Register;
            ClansSettings.Changed += ApplyChestSettings;
            ClansSettings.Changed += ApplyFeastSettings;
            ClanBanner.Init();
        }

        private static void Register() {
            if (registered) { return; }
            registered = true;
            try {
                GameObject start = MakeTotem(ClansPrefabs.RaceStart, RaceStartBases, keepContainer: false);
                if (start != null) { start.AddComponent<ClanTotem>(); }
                GameObject boss = MakeTotem(ClansPrefabs.WorldBoss, WorldBossBases, keepContainer: false);
                if (boss != null) { boss.AddComponent<ClanTotem>(); }
                GameObject end = MakeTotem(ClansPrefabs.RaceEnd, RaceEndBases, keepContainer: true);
                if (end != null) {
                    Container container = end.GetComponentInChildren<Container>(true);
                    if (container == null) { container = end.AddComponent<Container>(); }
                    container.m_name = "$clans_race_destination";
                    container.m_width = DestinationWidth;
                    container.m_height = DestinationHeight;
                    container.m_privacy = Container.PrivacySetting.Public;
                    container.m_checkGuardStone = false;
                    end.AddComponent<RaceDestination>();
                    AddDestinationTotem(end);
                }
                GameObject placer = MakePlacer();
                foreach (GameObject prefab in new[] { start, boss, end, placer }) {
                    if (prefab != null) { PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false)); }
                }
                RegisterChests();
                foreach (ClansPrefabs.BannerVariant variant in ClansPrefabs.Banners) { RegisterBanner(variant); }
                foreach (ClansPrefabs.GuildFeastVariant variant in ClansPrefabs.GuildFeasts) { RegisterGuildFeast(variant); }
                ApplyFeastSettings();
            } catch (Exception e) {
                Logger.LogError($"Could not register the Clans activity prefabs: {e}");
            } finally {
                PrefabManager.OnVanillaPrefabsAvailable -= Register;
            }
        }

        // The totem placer: a networked object with nothing to see or touch. Built under an inactive holder, so its
        // ZNetView does not wake (and make a world object) while it is only a prefab.
        private static GameObject MakePlacer() {
            GameObject holder = new GameObject("Clans_PrefabHolder");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);
            GameObject placer = new GameObject(ClansPrefabs.Placer);
            placer.transform.SetParent(holder.transform, false);
            ZNetView view = placer.AddComponent<ZNetView>();
            view.m_persistent = true;
            view.m_type = ZDO.ObjectType.Default;
            view.m_distant = false;
            placer.AddComponent<TotemPlacer>();
            return placer;
        }

        private static GameObject MakeTotem(string name, string[] bases, bool keepContainer) {
            GameObject prefab = null;
            foreach (string baseName in bases) {
                if (PrefabManager.Instance.GetPrefab(baseName) == null) { continue; }
                prefab = PrefabManager.Instance.CreateClonedPrefab(name, baseName);
                if (prefab != null) { break; }
            }
            if (prefab == null) {
                Logger.LogError($"None of {string.Join(", ", bases)} exist; the {name} totem is unavailable.");
                return null;
            }
            Strip(prefab, keepContainer);
            ZNetView view = prefab.GetComponent<ZNetView>();
            if (view == null) { view = prefab.AddComponent<ZNetView>(); }
            view.m_persistent = true;
            view.m_type = ZDO.ObjectType.Default;
            view.m_distant = false;
            return prefab;
        }

        // Everything that makes the original a building piece or something to use.
        private static void Strip(GameObject prefab, bool keepContainer) {
            // A nested networked child (an item stand on a boss stone) would get a world object of its own.
            foreach (ZNetView child in prefab.GetComponentsInChildren<ZNetView>(true)) {
                if (child.gameObject != prefab) { UnityEngine.Object.DestroyImmediate(child.gameObject); }
            }
            foreach (Component component in prefab.GetComponentsInChildren<Component>(true)) {
                if (component == null || component is Transform || component is ZNetView) { continue; }
                if (keepContainer && component is Container) { continue; }
                if (component is Piece || component is WearNTear || component is Destructible || component is ItemStand || component is BossStone
                    || component is Hoverable || component is Interactable || component is Container || component is ImpactEffect) {
                    UnityEngine.Object.DestroyImmediate(component);
                }
            }
        }

        // A chest is easy to miss: a totem like the start's stands beside it. Only its meshes and colliders are kept,
        // so it is part of the destination (using it opens the chest). The destination prefab is held inactive, so the
        // copy wakes only with each destination in the world.
        private static void AddDestinationTotem(GameObject destination) {
            GameObject source = PrefabManager.Instance.GetPrefab(DestinationTotemBase);
            if (source == null) {
                Logger.LogWarning($"{DestinationTotemBase} does not exist; the race destination has no totem beside it.");
                return;
            }
            Bounds chest = MeshBounds(destination, destination.transform);
            GameObject totem = UnityEngine.Object.Instantiate(source, destination.transform, false);
            totem.name = "Clans_DestinationTotem";
            totem.transform.localPosition = Vector3.zero;
            totem.transform.localRotation = Quaternion.identity;
            foreach (ZNetView child in totem.GetComponentsInChildren<ZNetView>(true)) {
                if (child.gameObject != totem) { UnityEngine.Object.DestroyImmediate(child.gameObject); }
            }
            // Last first, so a component another one needs goes after it.
            MonoBehaviour[] behaviours = totem.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = behaviours.Length - 1; i >= 0; i--) {
                if (behaviours[i] != null) { UnityEngine.Object.DestroyImmediate(behaviours[i]); }
            }
            foreach (Rigidbody body in totem.GetComponentsInChildren<Rigidbody>(true)) { UnityEngine.Object.DestroyImmediate(body); }

            // Edge to edge with the chest, a little apart, and within the ground the placer clears (ClansPrefabs.Footprint).
            Bounds marker = MeshBounds(totem, destination.transform);
            float shift = Mathf.Clamp(chest.min.x - DestinationTotemGap - marker.max.x, -2f, -0.8f);
            totem.transform.localPosition = new Vector3(shift, 0f, 0f);
        }

        // The box an object's meshes fill, in another's space. Read from the meshes: the renderers of an inactive
        // prefab report no bounds.
        private static Bounds MeshBounds(GameObject of, Transform space) {
            Matrix4x4 toSpace = space.worldToLocalMatrix;
            Bounds bounds = new Bounds();
            bool any = false;
            foreach (MeshFilter filter in of.GetComponentsInChildren<MeshFilter>(true)) {
                if (filter.sharedMesh == null) { continue; }
                Matrix4x4 toLocal = toSpace * filter.transform.localToWorldMatrix;
                Bounds mesh = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++) {
                    Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = toLocal.MultiplyPoint3x4(mesh.center + Vector3.Scale(mesh.extents, sign));
                    if (any) {
                        bounds.Encapsulate(point);
                    } else {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                }
            }
            return bounds;
        }

        // Each banner keeps everything its vanilla one has (recipe and dyes, workbench, comfort, wear); ClanBanner adds
        // the blank cloth and the emblem.
        private static void RegisterBanner(ClansPrefabs.BannerVariant variant) {
            if (PrefabManager.Instance.GetPrefab(variant.Base) == null) {
                Logger.LogError($"{variant.Base} does not exist; the {variant.Colour} clan banner is unavailable.");
                return;
            }
            CustomPiece banner = new CustomPiece(variant.Prefab, variant.Base, "Hammer");
            if (banner.PiecePrefab == null || banner.Piece == null) {
                Logger.LogError($"Could not copy {variant.Base} for the {variant.Colour} clan banner.");
                return;
            }
            banner.Piece.m_name = variant.Name;
            banner.Piece.m_description = "$clans_banner_description";
            banner.PiecePrefab.AddComponent<ClanBanner>();
            PieceManager.Instance.AddPiece(banner);
            BannerPieces.Add(new BannerPiece { Piece = banner.Piece, Variant = variant, VanillaIcon = banner.Piece.m_icon });
        }

        // A copy of the vanilla feast (the placed one, which is an item too, so the food it gives survives a relog) under
        // its own name, so it is a food of its own beside the vanilla feast. It cannot be packed up, so it never turns
        // back into the vanilla feast it was made from; GuildFeast clears it away instead.
        private static void RegisterGuildFeast(ClansPrefabs.GuildFeastVariant variant) {
            GameObject vanilla = PrefabManager.Instance.GetPrefab(variant.Base);
            if (vanilla == null || vanilla.GetComponent<Feast>() == null || vanilla.GetComponent<ItemDrop>() == null) {
                Logger.LogInfo($"This game has no {variant.Base}; the {variant.Biome} guild feast is not offered.");
                return;
            }
            CustomItem item = new CustomItem(variant.Prefab, variant.Base);
            if (item.ItemDrop == null) {
                Logger.LogError($"Could not copy {variant.Base} for the {variant.Biome} guild feast.");
                return;
            }
            ItemDrop.ItemData.SharedData shared = item.ItemDrop.m_itemData.m_shared;
            GuildFeastPrefab feast = new GuildFeastPrefab {
                Variant = variant,
                Item = item,
                Health = shared.m_food,
                Stamina = shared.m_foodStamina,
                Eitr = shared.m_foodEitr,
            };
            shared.m_name = variant.Name;
            shared.m_description = "$clans_feast_description";
            Piece piece = item.ItemPrefab.GetComponent<Piece>();
            if (piece != null) {
                piece.m_name = variant.Name;
                piece.m_description = "$clans_feast_description";
                piece.m_canBeRemoved = false;
                piece.m_resources = new Piece.Requirement[0];
            }
            item.ItemPrefab.AddComponent<GuildFeast>();
            ItemManager.Instance.AddItem(item);
            guildFeasts.Add(feast);
        }

        // Food strength and duration follow feasts.yaml, including the server's copy once it arrives.
        private static void ApplyFeastSettings() {
            FeastsSettings settings = ClansSettings.Feasts;
            foreach (GuildFeastPrefab feast in guildFeasts) {
                GuildFeastDef def = settings.Find(feast.Variant.Biome);
                ApplyFood(feast.Item.ItemDrop, feast, def, settings.FoodMinutes);
                GameObject live = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(feast.Variant.Prefab) : null;
                ItemDrop liveDrop = live != null ? live.GetComponent<ItemDrop>() : null;
                if (liveDrop != null && liveDrop != feast.Item.ItemDrop) { ApplyFood(liveDrop, feast, def, settings.FoodMinutes); }
            }
        }

        // The food feasts.yaml sets, or the vanilla feast's when it sets none.
        private static void ApplyFood(ItemDrop drop, GuildFeastPrefab feast, GuildFeastDef def, int minutes) {
            if (drop == null) { return; }
            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            bool configured = def != null && def.GivesFood;
            shared.m_food = configured ? def.Health : feast.Health;
            shared.m_foodStamina = configured ? def.Stamina : feast.Stamina;
            shared.m_foodEitr = configured ? def.Eitr : feast.Eitr;
            shared.m_foodBurnTime = Mathf.Max(1, minutes) * 60f;
        }

        private static void RegisterChests() {
            for (int tier = 0; tier < ClansPrefabs.RaceTiers; tier++) {
                string baseName = ChestBases[tier];
                List<RaceTier> tiers = ClansSettings.Activities.Race.Tiers;
                if (tier < tiers.Count && string.IsNullOrEmpty(tiers[tier].BasePrefab) == false && PrefabManager.Instance.GetPrefab(tiers[tier].BasePrefab) != null) {
                    baseName = tiers[tier].BasePrefab;
                }
                CustomItem item = new CustomItem(ClansPrefabs.RaceChest(tier), baseName);
                if (item.ItemDrop == null) {
                    Logger.LogError($"Could not copy {baseName} for race chest tier {tier}.");
                    continue;
                }
                ItemDrop.ItemData.SharedData shared = item.ItemDrop.m_itemData.m_shared;
                shared.m_name = "$clans_race_chest_" + tier;
                shared.m_description = "$clans_race_chest_description";
                shared.m_teleportable = false;
                // Vanilla refuses any item of tool tier 1000 or more at a portal, even with "teleport all".
                shared.m_toolTier = 1000;
                shared.m_maxStackSize = 1;
                shared.m_questItem = false;
                item.ItemDrop.m_autoDestroy = false;
                ItemManager.Instance.AddItem(item);
                chests.Add(item);
            }
            ApplyChestSettings();
        }

        // Weights follow activities.yaml, including the server's copy once it arrives.
        private static void ApplyChestSettings() {
            List<RaceTier> tiers = ClansSettings.Activities.Race.Tiers;
            for (int tier = 0; tier < chests.Count && tier < tiers.Count; tier++) {
                ItemDrop drop = chests[tier].ItemDrop;
                if (drop != null) { drop.m_itemData.m_shared.m_weight = Mathf.Max(1f, tiers[tier].Weight); }
                GameObject live = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(ClansPrefabs.RaceChest(tier)) : null;
                ItemDrop liveDrop = live != null ? live.GetComponent<ItemDrop>() : null;
                if (liveDrop != null) { liveDrop.m_itemData.m_shared.m_weight = Mathf.Max(1f, tiers[tier].Weight); }
            }
        }
    }
}
