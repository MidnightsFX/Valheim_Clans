using Clans.Compat;
using Clans.Net;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Client {
    // Items linked in chat. Shift + right-click an item in an inventory to put "[Item name]" into the chat
    // input; the item travels with the message, and everyone who reads it can hover the name for the item's
    // full tooltip, Epic Loot's magic effects included. On Clans' channels the item rides in the message; on
    // vanilla's say, shout and whisper it is sent just ahead of the line (see ChatClient.SendTextPatch).
    internal static class ChatItems {
        // What was linked into the input and not sent yet: the text shown, and a copy of the item.
        private static readonly List<KeyValuePair<string, ItemDrop.ItemData>> pending = new List<KeyValuePair<string, ItemDrop.ItemData>>();
        private static bool moveCaret;
        // The input is focused once the right mouse button is up, so the release does not land in the game.
        private static bool activateOnRelease;

        internal static void Reset() {
            pending.Clear();
            moveCaret = false;
            activateOnRelease = false;
        }

        internal static bool HasPending(string text) {
            foreach (KeyValuePair<string, ItemDrop.ItemData> link in pending) {
                if (text.Contains(link.Key)) { return true; }
            }
            return false;
        }

        // Called every frame: focuses the input once the click is over, then (the input field selects all text
        // when it is activated) puts the caret at the end.
        internal static void Tick() {
            Chat chat = Chat.instance;
            if (chat == null || chat.m_input == null) { return; }
            if (activateOnRelease) {
                if (ZInput.GetKey(KeyCode.Mouse1, logWarning: false)) { return; }
                activateOnRelease = false;
                chat.m_hideTimer = 0f;
                chat.m_input.ActivateInputField();
                moveCaret = true;
                return;
            }
            if (moveCaret == false) { return; }
            moveCaret = false;
            chat.m_input.MoveTextEnd(false);
        }

        // ---------------------------------------------------------------- linking from an inventory

        [HarmonyPatch(typeof(InventoryGrid), "OnRightDown")]
        private static class RightClickPatch {
            private static bool Prefix(InventoryGrid __instance, UIInputHandler element) {
                if (ZInput.GetKey(KeyCode.LeftShift) == false && ZInput.GetKey(KeyCode.RightShift) == false) { return true; }
                if (Chat.instance == null || Chat.instance.m_input == null || __instance.GetInventory() == null) { return true; }
                Vector2i pos = __instance.GetButtonPos(element.gameObject);
                ItemDrop.ItemData item = __instance.GetInventory().GetItemAt(pos.x, pos.y);
                if (item == null || item.m_shared == null) { return true; }
                Insert(item);
                return false;
            }
        }

        internal static void Insert(ItemDrop.ItemData item) {
            Chat chat = Chat.instance;
            string token = "[" + PlainName(item) + "]";
            pending.RemoveAll(p => p.Key == token);
            pending.Add(new KeyValuePair<string, ItemDrop.ItemData>(token, item.Clone()));
            chat.m_hideTimer = 0f;
            chat.m_chatWindow.gameObject.SetActive(true);
            chat.m_input.gameObject.SetActive(true);
            string text = chat.m_input.text ?? "";
            chat.m_input.text = text.Length == 0 || text.EndsWith(" ") ? text + token + " " : text + " " + token + " ";
            activateOnRelease = true;
        }

        // Replaces the linked names in a message being sent with "[[n]]" and returns the links. Names of
        // items no longer pending stay as they are.
        internal static List<ChatLink> TakeLinks(ref string text) {
            List<ChatLink> links = new List<ChatLink>();
            foreach (KeyValuePair<string, ItemDrop.ItemData> link in pending) {
                if (links.Count >= ChatLimits.MaxLinks) { break; }
                int at = text.IndexOf(link.Key, StringComparison.Ordinal);
                if (at < 0) { continue; }
                byte[] payload = Serialize(link.Value);
                if (payload == null || payload.Length > ChatLimits.MaxItemPayload) { continue; }
                text = text.Substring(0, at) + "[[" + links.Count + "]]" + text.Substring(at + link.Key.Length);
                links.Add(new ChatLink { Kind = ChatLinkKind.Item, Label = PlainName(link.Value), Payload = payload });
            }
            pending.Clear();
            return links;
        }

        // The links for a message whose text keeps the names as they are (a vanilla say, shout or whisper).
        internal static List<ChatLink> CollectLinks(string text) {
            List<ChatLink> links = new List<ChatLink>();
            foreach (KeyValuePair<string, ItemDrop.ItemData> link in pending) {
                if (links.Count >= ChatLimits.MaxLinks) { break; }
                if (text.IndexOf(link.Key, StringComparison.Ordinal) < 0) { continue; }
                byte[] payload = Serialize(link.Value);
                if (payload == null || payload.Length > ChatLimits.MaxItemPayload) { continue; }
                links.Add(new ChatLink { Kind = ChatLinkKind.Item, Label = PlainName(link.Value), Payload = payload });
            }
            pending.Clear();
            return links;
        }

        // ---------------------------------------------------------------- names

        // The name shown in the input and sent as the link's label: Epic Loot's magic name when there is one.
        internal static string PlainName(ItemDrop.ItemData item) {
            string name = BareName(item);
            if (item.m_quality > 1) { name += " (" + item.m_quality + ")"; }
            return name;
        }

        // The link as drawn in chat: Epic Loot's rarity colour for magic items, vanilla's gold otherwise. The
        // item came from another player, so its name is shown as plain text in a colour chosen here: nothing
        // in it can change how the chat is drawn.
        internal static string DecoratedName(ItemDrop.ItemData item, string fallbackLabel) {
            if (item == null) { return "<color=#FFD800>[" + Plain(fallbackLabel) + "]</color>"; }
            string color = EpicLootApi.RarityColor(item) ?? "#FFD800";
            return "<color=" + color + ">[" + PlainName(item) + "]</color>";
        }

        // The item's name, magic name included, without any markup.
        internal static string BareName(ItemDrop.ItemData item) {
            string decorated = EpicLootApi.DecoratedName(item);
            return Plain(Localization.instance.Localize(decorated != null ? StripTags(decorated) : item.m_shared.m_name));
        }

        // Text from another player, made safe to put into rich text.
        internal static string Plain(string text) => (text ?? "").Replace('<', ' ').Replace('>', ' ');

        private static string StripTags(string text) {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(text.Length);
            bool inTag = false;
            foreach (char c in text) {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (inTag == false) { builder.Append(c); }
            }
            return builder.ToString();
        }

        // ---------------------------------------------------------------- the item on the wire

        // The prefab's name, then vanilla's own item save format (what chests and tombstones use).
        internal static byte[] Serialize(ItemDrop.ItemData item) {
            string prefab = PrefabName(item);
            if (string.IsNullOrEmpty(prefab)) { return null; }
            try {
                ZPackage pkg = new ZPackage();
                pkg.Write(prefab);
                pkg.Write((int)Version.Item.NewCheats);
                item.Save(pkg);
                return pkg.GetArray();
            } catch (Exception e) {
                Logger.LogWarning($"Could not link {item.m_shared.m_name} in chat: {e.Message}");
                return null;
            }
        }

        // Rebuilds a linked item to show its tooltip; null when this client does not know the item.
        internal static ItemDrop.ItemData Deserialize(byte[] payload) {
            if (payload == null || payload.Length == 0 || ObjectDB.instance == null) { return null; }
            try {
                ZPackage pkg = new ZPackage(payload);
                string prefabName = pkg.ReadString();
                Version.Item version = (Version.Item)pkg.ReadInt();
                (int hash, ItemDrop.ItemData item) = ItemDrop.ItemData.Load(pkg, version);
                GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName) ?? ObjectDB.instance.GetItemPrefab(hash);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null) { return null; }
                item.m_shared = drop.m_itemData.m_shared;
                item.m_dropPrefab = prefab;
                item.m_equipped = false;
                return item;
            } catch (Exception e) {
                Logger.LogDebug($"Could not read a linked item: {e.Message}");
                return null;
            }
        }

        private static string PrefabName(ItemDrop.ItemData item) {
            if (item.m_dropPrefab != null) { return item.m_dropPrefab.name; }
            if (ObjectDB.instance == null) { return null; }
            foreach (GameObject prefab in ObjectDB.instance.m_items) {
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop != null && drop.m_itemData.m_shared.m_name == item.m_shared.m_name) { return prefab.name; }
            }
            return null;
        }
    }
}
