using System;
using System.Reflection;

namespace Clans.Compat {
    // The Epic Loot calls Clans needs, through Epic Loot's public API class by reflection so Clans runs with
    // or without Epic Loot installed.
    internal static class EpicLootApi {
        private static bool resolved;
        private static MethodInfo canBeMagicItem;
        private static MethodInfo tryMakeMagicItem;
        private static MethodInfo isMagicItem;
        private static MethodInfo getItemDecoratedName;
        private static MethodInfo getItemRarityColor;
        // Bounties (Epic Loot API 2).
        private static MethodInfo getActiveBounties;
        private static MethodInfo getBountyPinType;
        private static MethodInfo addAdventureDataChangedListener;
        private static MethodInfo addBountyTargetSlainListener;

        internal static bool Available {
            get {
                Resolve();
                return tryMakeMagicItem != null;
            }
        }

        private static void Resolve() {
            if (resolved) { return; }
            resolved = true;
            Type api = Type.GetType("EpicLoot.API, EpicLoot");
            if (api == null) { return; }
            canBeMagicItem = api.GetMethod("CanBeMagicItem", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ItemDrop.ItemData) }, null);
            tryMakeMagicItem = api.GetMethod("TryMakeMagicItem", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(float), typeof(string) }, null);
            // Query endpoints (Epic Loot 0.13+); absent on older versions, which then show plain names.
            isMagicItem = api.GetMethod("IsMagicItem", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ItemDrop.ItemData) }, null);
            getItemDecoratedName = api.GetMethod("GetItemDecoratedName", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(ItemDrop.ItemData), typeof(string) }, null);
            getItemRarityColor = api.GetMethod("GetItemRarityColor", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ItemDrop.ItemData) }, null);
            // Bounty endpoints (Epic Loot API 2); absent on older versions, which then share no bounties.
            getActiveBounties = api.GetMethod("GetActiveBounties", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            getBountyPinType = api.GetMethod("GetBountyPinType", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            addAdventureDataChangedListener = api.GetMethod("AddAdventureDataChangedListener", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Action) }, null);
            addBountyTargetSlainListener = api.GetMethod("AddBountyTargetSlainListener", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Action<string>) }, null);
            if (tryMakeMagicItem == null) {
                Logger.LogWarning("Epic Loot is installed but its TryMakeMagicItem API was not found; Clans rewards will be ordinary items.");
            }
        }

        // Whether this Epic Loot can tell Clans about bounties.
        internal static bool BountiesAvailable {
            get {
                Resolve();
                return getActiveBounties != null && addAdventureDataChangedListener != null && addBountyTargetSlainListener != null;
            }
        }

        // The local player's open and unclaimed bounties, as Epic Loot's JSON array; "[]" when unavailable.
        internal static string ActiveBountiesJson() {
            Resolve();
            if (getActiveBounties == null) { return "[]"; }
            try {
                return getActiveBounties.Invoke(null, null) as string ?? "[]";
            } catch (Exception e) {
                Logger.LogWarning($"Epic Loot GetActiveBounties failed: {e.Message}");
                return "[]";
            }
        }

        // The pin type Epic Loot draws bounty icons with; -1 before its minimap has started, or without one.
        internal static int BountyPinType() {
            Resolve();
            if (getBountyPinType == null) { return -1; }
            try {
                return (int)getBountyPinType.Invoke(null, null);
            } catch (Exception) {
                return -1;
            }
        }

        // Runs on the local client after the local player's bounties (or any adventure data) were saved.
        internal static bool AddAdventureDataChangedListener(Action listener) {
            Resolve();
            if (addAdventureDataChangedListener == null || listener == null) { return false; }
            try {
                return (bool)addAdventureDataChangedListener.Invoke(null, new object[] { listener });
            } catch (Exception e) {
                Logger.LogWarning($"Epic Loot AddAdventureDataChangedListener failed: {e.Message}");
                return false;
            }
        }

        // Runs on the server for every bounty creature that dies, with { BountyId, PlayerId, MonsterId, IsAdd }.
        internal static bool AddBountyTargetSlainListener(Action<string> listener) {
            Resolve();
            if (addBountyTargetSlainListener == null || listener == null) { return false; }
            try {
                return (bool)addBountyTargetSlainListener.Invoke(null, new object[] { listener });
            } catch (Exception e) {
                Logger.LogWarning($"Epic Loot AddBountyTargetSlainListener failed: {e.Message}");
                return false;
            }
        }

        internal static bool CanBeMagicItem(ItemDrop.ItemData item) {
            Resolve();
            if (canBeMagicItem == null || item == null) { return false; }
            try {
                return (bool)canBeMagicItem.Invoke(null, new object[] { item });
            } catch (Exception e) {
                Logger.LogWarning($"Epic Loot CanBeMagicItem failed: {e.Message}");
                return false;
            }
        }

        internal static bool IsMagicItem(ItemDrop.ItemData item) {
            Resolve();
            if (isMagicItem == null || item == null) { return false; }
            try {
                return (bool)isMagicItem.Invoke(null, new object[] { item });
            } catch (Exception) {
                return false;
            }
        }

        // The item's name as Epic Loot shows it: a magic item's own name, coloured by rarity. Null without
        // Epic Loot, or for an item it has nothing to say about.
        internal static string DecoratedName(ItemDrop.ItemData item) {
            Resolve();
            if (getItemDecoratedName == null || item == null || IsMagicItem(item) == false) { return null; }
            try {
                return getItemDecoratedName.Invoke(null, new object[] { item, null }) as string;
            } catch (Exception) {
                return null;
            }
        }

        // The colour Epic Loot draws a magic item's name in, as "#RRGGBB"; null when there is none (or it is
        // not a plain colour, since it goes into rich text).
        internal static string RarityColor(ItemDrop.ItemData item) {
            Resolve();
            if (getItemRarityColor == null || item == null || IsMagicItem(item) == false) { return null; }
            try {
                string color = getItemRarityColor.Invoke(null, new object[] { item }) as string;
                return color != null && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$") ? color : null;
            } catch (Exception) {
                return null;
            }
        }

        // Turns item into a magic item of the given rarity (0 Magic ... 5 Ancient) in place.
        internal static bool TryMakeMagicItem(ItemDrop.ItemData item, int rarity) {
            Resolve();
            if (tryMakeMagicItem == null || item == null) { return false; }
            try {
                return (bool)tryMakeMagicItem.Invoke(null, new object[] { item, rarity, 0f, "" });
            } catch (Exception e) {
                Logger.LogWarning($"Epic Loot TryMakeMagicItem failed: {e.Message}");
                return false;
            }
        }
    }
}
