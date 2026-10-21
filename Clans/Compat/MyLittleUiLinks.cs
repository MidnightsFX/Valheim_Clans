using System;
using System.Reflection;

namespace Clans.Compat {
    // My Little UI's chat item links (Alt + click an item). Each link arrives as a line holding only a marker,
    // "MLUI_ITEM_LINK:<number>", and My Little UI keeps the item under that number; it turns the
    // marker into a link when vanilla adds the formatted line to its own chat window. The Clans chat panel
    // takes the line before that, so it looks the item up itself. My Little UI has no API for this: its
    // private lookup is found by reflection, and without it the marker is shown as a plain name.
    internal static class MyLittleUiLinks {
        internal const string MarkerPrefix = "MLUI_ITEM_LINK:";
        internal const string MarkerSuffix = "";

        private static bool resolved;
        private static MethodInfo tryGetLinkedItem;

        private static void Resolve() {
            if (resolved) { return; }
            resolved = true;
            Type links = Type.GetType("MyLittleUI.ChatItemLinks, MyLittleUI");
            if (links == null) { return; }
            tryGetLinkedItem = links.GetMethod("TryGetLinkedItem", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(int), typeof(ItemDrop.ItemData).MakeByRefType() }, null);
            if (tryGetLinkedItem == null) {
                Logger.LogWarning("My Little UI is installed but its item link lookup was not found; its item links show as plain names in the Clans chat panel.");
            }
        }

        // The item My Little UI keeps for a link number; null when it is unknown.
        internal static ItemDrop.ItemData Item(int id) {
            Resolve();
            if (tryGetLinkedItem == null) { return null; }
            try {
                object[] args = { id, null };
                return (bool)tryGetLinkedItem.Invoke(null, args) ? args[1] as ItemDrop.ItemData : null;
            } catch (Exception e) {
                Logger.LogDebug($"My Little UI item link lookup failed: {e.Message}");
                return null;
            }
        }
    }
}
