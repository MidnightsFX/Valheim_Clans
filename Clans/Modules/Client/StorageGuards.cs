using Clans.Settings;
using HarmonyLib;
using UnityEngine;

namespace Clans.Client {
    // Stops moves the player's rank does not allow before they happen, while a clan storage tab is open:
    // taking items out without StorageWithdraw, putting them in without StorageDeposit, and putting in items
    // the server's storage does not accept (StorageSettings.Accepts). The server checks too and puts the tab
    // back if anything slips through, but by then the item would already be in the player's inventory, so
    // the honest path never reaches that.
    internal static class StorageGuards {
        private static bool StorageOpen(InventoryGui gui) => ClanStorageClient.IsOurs(gui.m_currentContainer);

        private static bool IsStorageGrid(InventoryGui gui, InventoryGrid grid) {
            return grid != null && gui.m_currentContainer != null && grid.GetInventory() == gui.m_currentContainer.GetInventory();
        }

        private static bool Refuse(string token) {
            ClanNotices.ShowError(Localization.instance.Localize(token));
            return false;
        }

        private static bool Accepted(ItemDrop.ItemData item) {
            if (ClansSettings.Storage.Accepts(item)) { return true; }
            ClanNotices.ShowError(UI.UiBind.Loc("$clans_storage_not_accepted", Localization.instance.Localize(item.m_shared.m_name)));
            return false;
        }

        // InventoryGrid.DropItem: dropping a whole stack on a different item swaps the two.
        private static bool Swaps(ItemDrop.ItemData dragged, int amount, ItemDrop.ItemData target) {
            if (target == null || target == dragged || dragged.m_stack != amount) { return false; }
            return target.m_shared.m_name != dragged.m_shared.m_name
                || (dragged.m_shared.m_maxQuality > 1 && target.m_quality != dragged.m_quality)
                || target.m_shared.m_maxStackSize == 1;
        }

        [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
        private static class SelectedItemPatch {
            private static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod) {
                if (StorageOpen(__instance) == false) { return true; }
                bool storageGrid = IsStorageGrid(__instance, grid);
                Inventory storage = __instance.m_currentContainer.GetInventory();

                if (__instance.m_dragGo != null) {
                    // Dropping into the tab from the player's inventory puts in; landing on an item there swaps
                    // that item out.
                    if (storageGrid && __instance.m_dragInventory != storage) {
                        if (ClanStorageClient.CanPut == false) { return Refuse("$clans_storage_no_deposit"); }
                        if (Accepted(__instance.m_dragItem) == false) { return false; }
                        if (item != null && ClanStorageClient.CanTake == false) { return Refuse("$clans_storage_no_withdraw"); }
                    }
                    // Dropping an item from the tab on a different one in the player's inventory swaps that one in.
                    if (storageGrid == false && __instance.m_dragInventory == storage && Swaps(__instance.m_dragItem, __instance.m_dragAmount, item)) {
                        if (ClanStorageClient.CanPut == false) { return Refuse("$clans_storage_no_deposit"); }
                        if (Accepted(item) == false) { return false; }
                    }
                    return true;
                }
                if (item == null) { return true; }
                // Picking an item up in the tab, or shift-clicking it out, or dropping it on the ground.
                if (storageGrid && ClanStorageClient.CanTake == false) { return Refuse("$clans_storage_no_withdraw"); }
                // Shift-clicking an item from the player's inventory moves it into the open container.
                if (storageGrid == false && mod == InventoryGrid.Modifier.Move) {
                    if (ClanStorageClient.CanPut == false) { return Refuse("$clans_storage_no_deposit"); }
                    if (Accepted(item) == false) { return false; }
                }
                return true;
            }
        }

        // Right-click uses an item from where it is: eating or equipping from the tab takes it out.
        [HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
        private static class RightClickPatch {
            private static bool Prefix(InventoryGui __instance, InventoryGrid grid) {
                if (StorageOpen(__instance) == false || IsStorageGrid(__instance, grid) == false) { return true; }
                return ClanStorageClient.CanTake || Refuse("$clans_storage_no_withdraw");
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "OnTakeAll")]
        private static class TakeAllPatch {
            private static bool Prefix(InventoryGui __instance) {
                if (StorageOpen(__instance) == false) { return true; }
                return ClanStorageClient.CanTake || Refuse("$clans_storage_no_withdraw");
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "OnStackAll")]
        private static class StackAllPatch {
            private static bool Prefix(InventoryGui __instance) {
                if (StorageOpen(__instance) == false) { return true; }
                return ClanStorageClient.CanPut || Refuse("$clans_storage_no_deposit");
            }
        }

        // ---------------------------------------------------------------- the tab's inventory itself

        // Every other way an item can come in (stacking all, other mods) meets this: an item storage does not
        // accept is quietly turned away. It cannot stand in for the checks above, which have to come first: a
        // swap takes the dragged item out of its inventory before adding it here, so refusing it here loses it.
        private static bool Admit(Inventory inventory, ItemDrop.ItemData item, ref bool result) {
            if (item == null || ClanStorageClient.IsOurs(inventory) == false || ClanStorageClient.Loading || ClansSettings.Storage.Accepts(item)) { return true; }
            result = false;
            return false;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
        private static class AddItemPatch {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result) => Admit(__instance, item, ref __result);
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(Vector2i))]
        private static class AddItemAtPatch {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result) => Admit(__instance, item, ref __result);
        }

        // The funnel that moving and dragging items between inventories goes through.
        [HarmonyPatch(typeof(Inventory), "AddItem", typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool))]
        private static class AddItemMovePatch {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result) => Admit(__instance, item, ref __result);
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
        private static class CanAddItemPatch {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result) => Admit(__instance, item, ref __result);
        }
    }
}
