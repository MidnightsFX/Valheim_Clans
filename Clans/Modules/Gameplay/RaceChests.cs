using Clans.Client;
using Clans.Net;
using Clans.Server.Model;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Clans.Gameplay {
    // The Race's chests must be carried, never teleported, and only go where the race allows.
    //
    // - Portals refuse them: the items are not teleportable, and their tool tier of 1000 makes vanilla refuse
    //   them even on a world with the "teleport everything" modifier.
    // - Every other teleport goes through Player.TeleportTo (dungeon doors, map and console teleports, other
    //   mods), which refuses while one is carried.
    // - A mod that moves the player, a cart or a ship directly is caught by a watchdog: a jump of more than
    //   a few dozen metres in one frame drops the chests back where they were.
    // - Containers refuse them except the player's own inventory, carts, ships, tombstones and the race's own
    //   destination; an inventory this code does not know (another mod's backpack, clan storage) refuses too.
    // - The destination takes race chests only: anything else put in it would vanish with it.
    // - Loading a saved inventory puts back what was there and is not checked; only moves are.
    // - Chests of a race that is over are removed from inventories, carts and the ground.
    internal static class RaceChests {
        private const float JumpDistance = 50f;
        private const float PurgeSeconds = 10f;
        private const string RaceChestName = "$clans_race_chest_";

        private static readonly ConditionalWeakTable<Inventory, Container> containers = new ConditionalWeakTable<Inventory, Container>();
        private static readonly ConditionalWeakTable<Inventory, Container> destinations = new ConditionalWeakTable<Inventory, Container>();
        private static int loading;
        private static readonly List<Container> carriers = new List<Container>();
        private static readonly Dictionary<Container, Vector3> carrierPositions = new Dictionary<Container, Vector3>();
        private static Vector3 lastPlayerPosition;
        private static bool havePlayerPosition;
        private static float purgeTimer = PurgeSeconds;

        internal static bool IsRaceChest(ItemDrop.ItemData item) {
            if (item == null) { return false; }
            // Runs for every item added to any inventory: the item's own name field only (a prefab's name would
            // be read from the engine, and allocate, every time). Every race chest's name starts this way.
            string name = item.m_shared?.m_name;
            return name != null && name.StartsWith(RaceChestName, System.StringComparison.Ordinal);
        }

        internal static long RaceOf(ItemDrop.ItemData item) {
            if (item?.m_customData == null || item.m_customData.TryGetValue(ClansPrefabs.RaceIdData, out string id) == false) { return 0; }
            return long.TryParse(id, out long race) ? race : 0;
        }

        internal static int SerialOf(ItemDrop.ItemData item) {
            if (item?.m_customData == null || item.m_customData.TryGetValue(ClansPrefabs.RaceSerialData, out string serial) == false) { return 0; }
            return int.TryParse(serial, out int value) ? value : 0;
        }

        internal static bool Carrying(Humanoid humanoid) {
            Inventory inventory = humanoid?.GetInventory();
            if (inventory == null) { return false; }
            foreach (ItemDrop.ItemData item in inventory.GetAllItems()) {
                if (IsRaceChest(item)) { return true; }
            }
            return false;
        }

        private static bool RaceActive(long raceId) {
            ActivityInfo info = ActivityClient.Get(raceId);
            return info != null && info.Type == ActivityType.Race && info.State == ActivityState.Running;
        }

        // ---------------------------------------------------------------- where chests may go

        internal static bool AllowedIn(Inventory inventory, ItemDrop.ItemData item) {
            if (inventory == null) { return false; }
            foreach (Player player in Player.GetAllPlayers()) {
                if (player != null && player.GetInventory() == inventory) { return true; }
            }
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetInventory() == inventory) { return true; }
            if (containers.TryGetValue(inventory, out Container container) == false || container == null) { return false; }
            if (container.m_wagon != null || container.GetComponentInParent<Ship>() != null || container.GetComponent<TombStone>() != null) { return true; }
            RaceDestination destination = container.GetComponentInParent<RaceDestination>();
            return destination != null && destination.ActivityId != 0 && destination.ActivityId == RaceOf(item);
        }

        private static bool Refuse(Inventory inventory, ItemDrop.ItemData item, ref bool result) {
            if (loading > 0 || item == null) { return true; }
            if (IsRaceChest(item)) {
                if (AllowedIn(inventory, item)) { return true; }
            } else if (inventory == null || destinations.TryGetValue(inventory, out _) == false) {
                return true;
            }
            result = false;
            return false;
        }

        // Both overloads: the game has Load(pkg) and Load(pkg, bool), and naming the method alone is ambiguous.
        [HarmonyPatch]
        private static class LoadPatch {
            private static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(Inventory))
                .Where(m => m.Name == nameof(Inventory.Load) && m.IsStatic == false);

            private static void Prefix() => loading++;
            private static void Finalizer() => loading = Mathf.Max(0, loading - 1);
        }

        [HarmonyPatch(typeof(Container), "Awake")]
        private static class ContainerAwakePatch {
            private static void Postfix(Container __instance) {
                Inventory inventory = __instance.GetInventory();
                if (inventory == null) { return; }
                containers.Remove(inventory);
                containers.Add(inventory, __instance);
                if (__instance.GetComponentInParent<RaceDestination>() != null) {
                    destinations.Remove(inventory);
                    destinations.Add(inventory, __instance);
                }
                if (__instance.m_wagon != null || __instance.GetComponentInParent<Ship>() != null) { carriers.Add(__instance); }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
        private static class AddItemPatch {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result) => Refuse(__instance, item, ref __result);
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(Vector2i))]
        private static class AddItemAtPatch {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result) => Refuse(__instance, item, ref __result);
        }

        // The funnel that moving and dragging items between inventories goes through.
        [HarmonyPatch(typeof(Inventory), "AddItem", typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool))]
        private static class AddItemMovePatch {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result) => Refuse(__instance, item, ref __result);
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
        private static class CanAddItemPatch {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result) => Refuse(__instance, item, ref __result);
        }

        [HarmonyPatch(typeof(ItemStand), "CanAttach")]
        private static class ItemStandPatch {
            private static bool Prefix(ItemDrop.ItemData item, ref bool __result) {
                if (IsRaceChest(item) == false) { return true; }
                __result = false;
                return false;
            }
        }

        // ---------------------------------------------------------------- teleports

        [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
        private static class TeleportPatch {
            private static bool Prefix(Player __instance, ref bool __result) {
                if (Carrying(__instance) == false) { return true; }
                __instance.Message(MessageHud.MessageType.Center, "$clans_race_no_teleport");
                __result = false;
                return false;
            }
        }

        // ---------------------------------------------------------------- watchdog and clean-up

        internal static void Tick(float dt) {
            Player player = Player.m_localPlayer;
            if (player == null) {
                havePlayerPosition = false;
                return;
            }
            WatchPlayer(player);
            WatchCarriers();
            purgeTimer -= dt;
            if (purgeTimer <= 0f) {
                purgeTimer = PurgeSeconds;
                if (ActivityClient.Received) { Purge(player); }
            }
        }

        private static void WatchPlayer(Player player) {
            Vector3 position = player.transform.position;
            if (havePlayerPosition && player.IsDead() == false && Vector3.Distance(position, lastPlayerPosition) > JumpDistance && Carrying(player)) {
                DropChests(player.GetInventory(), lastPlayerPosition);
                player.Message(MessageHud.MessageType.Center, "$clans_race_chests_left_behind");
            }
            lastPlayerPosition = position;
            havePlayerPosition = player.IsDead() == false;
        }

        private static void WatchCarriers() {
            for (int i = carriers.Count - 1; i >= 0; i--) {
                Container container = carriers[i];
                if (container == null) {
                    carriers.RemoveAt(i);
                    continue;
                }
                Vector3 position = container.transform.position;
                if (carrierPositions.TryGetValue(container, out Vector3 last)
                    && Vector3.Distance(position, last) > JumpDistance
                    && container.m_nview != null && container.m_nview.IsOwner()) {
                    DropChests(container.GetInventory(), last);
                }
                carrierPositions[container] = position;
            }
            if (carrierPositions.Count > carriers.Count * 2 + 16) {
                List<Container> gone = new List<Container>();
                foreach (Container c in carrierPositions.Keys) { if (c == null) { gone.Add(c); } }
                foreach (Container c in gone) { carrierPositions.Remove(c); }
            }
        }

        // Takes every race chest out of an inventory and leaves it on the ground at a position.
        private static void DropChests(Inventory inventory, Vector3 at) {
            if (inventory == null) { return; }
            List<ItemDrop.ItemData> chests = inventory.GetAllItems().FindAll(IsRaceChest);
            foreach (ItemDrop.ItemData chest in chests) {
                inventory.RemoveItem(chest);
                ItemDrop.DropItem(chest, chest.m_stack, at + Vector3.up + Random.insideUnitSphere, Quaternion.identity);
            }
        }

        // Removes chests of races that are over from what this client is responsible for.
        private static void Purge(Player player) {
            RemoveStale(player.GetInventory());
            foreach (Container carrier in carriers) {
                if (carrier != null && carrier.m_nview != null && carrier.m_nview.IsOwner()) { RemoveStale(carrier.GetInventory()); }
            }
            List<ItemDrop> stale = new List<ItemDrop>();
            foreach (ItemDrop drop in ItemDrop.s_instances) {
                if (drop == null || IsRaceChest(drop.m_itemData) == false) { continue; }
                if (drop.m_nview == null || drop.m_nview.IsValid() == false || drop.m_nview.IsOwner() == false) { continue; }
                if (RaceActive(RaceOf(drop.m_itemData)) == false) { stale.Add(drop); }
            }
            foreach (ItemDrop drop in stale) { ZNetScene.instance.Destroy(drop.gameObject); }
        }

        private static void RemoveStale(Inventory inventory) {
            if (inventory == null) { return; }
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().FindAll(IsRaceChest)) {
                if (RaceActive(RaceOf(item)) == false) { inventory.RemoveItem(item); }
            }
        }
    }
}
