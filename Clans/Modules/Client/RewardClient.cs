using Clans.Compat;
using Clans.Net;
using Clans.Server;
using Clans.Server.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans.Client {
    // Applies deliveries from the server's mailbox: coins, items, Epic Loot items and returned storage.
    //
    // Which deliveries this character has applied is kept in the character's own save data, next to the
    // inventory it changed, and a delivery is only acknowledged (and so removed from the server) once that
    // save is on disk: the character is saved a moment after deliveries arrive. If the game crashes before,
    // both roll back together and the server's re-send applies it again; if it was saved, a re-send is only
    // acknowledged.
    internal static class RewardClient {
        private const string AppliedKey = "Clans_Deliveries";
        private const int AppliedKept = 200;
        private const float SaveDelaySeconds = 1f;
        // More stacks than this left over (the inventory is full) and a delivery waits for room instead of
        // being dropped at the player's feet; checked again every few seconds.
        private const int MaxDroppedStacks = 10;
        private const float RoomRetrySeconds = 5f;

        private static readonly Queue<Delivery> waiting = new Queue<Delivery>();
        // Applied (or found already applied) but not yet acknowledged: waiting for the character to be saved.
        private static readonly List<long> unsaved = new List<long>();
        private static float saveTimer = -1f;
        private static float roomWait;
        private static long roomNoticeFor;

        internal static void Init() {
            ClanNet.RegisterClient(Op.RewardGrant, OnGrant);
        }

        private static void OnGrant(uint reqId, ZPackage body) {
            waiting.Enqueue(DeliveryCodec.Read(body));
        }

        // Deliveries wait until there is a living local player to hand them to.
        internal static void Tick(float dt) {
            SaveWhenDue(dt);
            Player player = Player.m_localPlayer;
            if (waiting.Count == 0 || player == null || player.IsDead() || player.IsTeleporting()) { return; }
            if (roomWait > 0f) {
                roomWait -= dt;
                return;
            }
            Delivery delivery = waiting.Peek();
            if (AlreadyApplied(player, delivery.Id) == false) {
                Inventory inventory = player.GetInventory();
                int missing = StacksNeeded(inventory, delivery) - inventory.GetEmptySlots();
                if (missing > MaxDroppedStacks) {
                    roomWait = RoomRetrySeconds;
                    if (roomNoticeFor != delivery.Id) {
                        roomNoticeFor = delivery.Id;
                        ClanNotices.ShowInfo(UI.UiBind.Loc("$clans_delivery_need_room", missing.ToString()));
                    }
                    return;
                }
            }
            waiting.Dequeue();
            try {
                if (AlreadyApplied(player, delivery.Id) == false) {
                    Apply(player, delivery);
                    MarkApplied(player, delivery.Id);
                }
                if (unsaved.Contains(delivery.Id) == false) { unsaved.Add(delivery.Id); }
                saveTimer = SaveDelaySeconds;
            } catch (Exception e) {
                Logger.LogError($"Could not apply Clans delivery {delivery.Id}: {e}");
            }
        }

        // About how many inventory slots a delivery fills: whole stacks beyond what tops up stacks already held.
        private static int StacksNeeded(Inventory inventory, Delivery delivery) {
            int stacks = 0;
            if (delivery.Coins > 0) { stacks += StacksFor(inventory, "Coins", delivery.Coins); }
            foreach (DeliveryItem item in delivery.Items) { stacks += StacksFor(inventory, item.Prefab, item.Stack); }
            foreach (string encoded in delivery.Inventories) { stacks += ItemsIn(encoded); }
            return stacks;
        }

        private static int StacksFor(Inventory inventory, string prefabName, int amount) {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null || amount <= 0) { return 0; }
            int maxStack = Math.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            if (maxStack > 1) {
                foreach (ItemDrop.ItemData held in inventory.GetAllItems()) {
                    if (held.m_shared.m_name == drop.m_itemData.m_shared.m_name) { amount -= Math.Max(0, maxStack - held.m_stack); }
                }
            }
            return amount <= 0 ? 0 : (amount + maxStack - 1) / maxStack;
        }

        private static int ItemsIn(string encoded) {
            if (string.IsNullOrEmpty(encoded)) { return 0; }
            try {
                Inventory temp = new Inventory("clans_delivery", null, 8, 8);
                temp.Load(new ZPackage(encoded));
                return temp.GetAllItems().Count;
            } catch (Exception) {
                return 0;
            }
        }

        // One save for a burst of deliveries.
        private static void SaveWhenDue(float dt) {
            if (saveTimer < 0f) { return; }
            saveTimer -= dt;
            if (saveTimer > 0f) { return; }
            saveTimer = -1f;
            if (Game.instance != null && Player.m_localPlayer != null) { Game.instance.SavePlayerProfile(false); }
        }

        // The character is on disk: what it holds can be acknowledged now.
        [HarmonyLib.HarmonyPatch(typeof(PlayerProfile), "SavePlayerToDisk")]
        private static class SavedPatch {
            private static void Postfix(PlayerProfile __instance, bool __result) {
                if (__result == false || unsaved.Count == 0 || Game.instance == null || Game.instance.GetPlayerProfile() != __instance) { return; }
                foreach (long id in unsaved) {
                    long acked = id;
                    ClanRequests.Report(Op.AckDelivery, pkg => pkg.Write(acked));
                }
                unsaved.Clear();
            }
        }

        private static void Apply(Player player, Delivery delivery) {
            Inventory inventory = player.GetInventory();
            if (delivery.Coins > 0) { Give(player, inventory, "Coins", delivery.Coins, 1, false, 0); }
            foreach (DeliveryItem item in delivery.Items) {
                Give(player, inventory, item.Prefab, item.Stack, item.Quality, item.Magic, item.Rarity);
            }
            foreach (string encoded in delivery.Inventories) {
                GiveInventory(player, inventory, encoded);
            }
            if (string.IsNullOrEmpty(delivery.Reason) == false) {
                ClanNotices.ShowInfo(Localization.instance.Localize(delivery.Reason));
            }
        }

        // Puts items in the local player's inventory, dropping what does not fit at their feet.
        internal static void GiveItem(string prefabName, int amount) {
            Player player = Player.m_localPlayer;
            if (player == null || amount <= 0) { return; }
            Give(player, player.GetInventory(), prefabName, amount, 1, false, 0);
        }

        private static void Give(Player player, Inventory inventory, string prefabName, int amount, int quality, bool magic, int rarity) {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null) {
                Logger.LogWarning($"Clans reward item '{prefabName}' does not exist; skipped.");
                return;
            }
            int maxStack = Math.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            int remaining = Math.Max(1, amount);
            while (remaining > 0) {
                ItemDrop.ItemData item = drop.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_quality = Math.Max(1, quality);
                item.m_stack = Math.Min(remaining, maxStack);
                remaining -= item.m_stack;
                if (magic && EpicLootApi.CanBeMagicItem(item)) { EpicLootApi.TryMakeMagicItem(item, rarity); }
                Place(player, inventory, item);
            }
        }

        private static void GiveInventory(Player player, Inventory inventory, string encoded) {
            if (string.IsNullOrEmpty(encoded)) { return; }
            // Large enough that nothing saved from a 5x4 tab lands outside the grid.
            Inventory temp = new Inventory("clans_delivery", null, 8, 8);
            temp.Load(new ZPackage(encoded));
            foreach (ItemDrop.ItemData item in temp.GetAllItems().ToList()) {
                Place(player, inventory, item.Clone());
            }
        }

        // Into the inventory, or at the player's feet when it is full.
        private static void Place(Player player, Inventory inventory, ItemDrop.ItemData item) {
            if (inventory.CanAddItem(item) && inventory.AddItem(item)) { return; }
            Vector3 position = player.transform.position + player.transform.forward + Vector3.up;
            ItemDrop.DropItem(item, item.m_stack, position, Quaternion.identity);
        }

        // Delivery ids are only unique within one world, and a character visits many.
        private static string Key(long id) => $"{(ZNet.World != null ? ZNet.World.m_uid : 0):x}:{id}";

        private static bool AlreadyApplied(Player player, long id) {
            return Applied(player).Contains(Key(id));
        }

        private static void MarkApplied(Player player, long id) {
            List<string> applied = Applied(player);
            applied.Add(Key(id));
            if (applied.Count > AppliedKept) { applied.RemoveRange(0, applied.Count - AppliedKept); }
            player.m_customData[AppliedKey] = string.Join(",", applied);
        }

        private static List<string> Applied(Player player) {
            if (player.m_customData.TryGetValue(AppliedKey, out string value) == false || string.IsNullOrEmpty(value)) {
                return new List<string>();
            }
            return value.Split(',').ToList();
        }

        internal static void Reset() {
            waiting.Clear();
            unsaved.Clear();
            saveTimer = -1f;
            roomWait = 0f;
            roomNoticeFor = 0;
        }
    }
}
