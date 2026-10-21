using Clans.Compat;
using Clans.Settings;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI {
    // Puts the Clan button where the PvP toggle was, in the row of round buttons at the top right of the
    // inventory, and moves the vanilla PvP toggle itself into the Clans window's header.
    //
    // The activity tracker's button (TrackerMenu) goes in the same row, left of the compendium.
    //
    // The toggle is moved rather than copied: InventoryGui.UpdateCharacterStats keeps driving it (SetPVP from
    // isOn, interactable from the combat timer) through its m_pvp field wherever it sits, and the inventory
    // is always open when the window is.
    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class InventoryHooks {
        private const string ButtonName = "Clans_Button";
        private static bool warnedAuga;

        private static void Postfix(InventoryGui __instance) {
            if (ModPresence.Is(ModPresence.Capability.Auga)) {
                if (warnedAuga == false) {
                    warnedAuga = true;
                    Logger.LogInfo("Auga replaces the inventory screen; open the Clans window with the 'Clans Window Hotkey' setting.");
                }
                return;
            }
            // Without the window there is nothing to open, so the PvP toggle stays where it is.
            if (ClansAssets.Prefab("Clans_UI_Window") == null) { return; }
            try {
                AddButton(__instance);
                InvitePanel.Create(__instance);
                StorageTabStrip.Create(__instance);
                TrackerMenu.Create(__instance);
            } catch (System.Exception e) {
                Logger.LogError($"Could not add the Clan button to the inventory: {e}");
            }
        }

        private static void AddButton(InventoryGui gui) {
            Toggle pvp = gui.m_pvp;
            Transform trophies = gui.m_info != null ? UiBind.Find(gui.m_info, "Trophies") : null;
            if (pvp == null || trophies == null) {
                Logger.LogWarning("The inventory screen has no PvP toggle or Trophies button; the Clan button was not added.");
                return;
            }
            RectTransform pvpRect = (RectTransform)pvp.transform;
            GameObject button = Object.Instantiate(trophies.gameObject, trophies.parent, false);
            button.name = ButtonName;
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = pvpRect.anchorMin;
            rect.anchorMax = pvpRect.anchorMax;
            rect.pivot = pvpRect.pivot;
            rect.anchoredPosition = pvpRect.anchoredPosition;
            rect.sizeDelta = pvpRect.sizeDelta;

            Transform icon = button.transform.Find("Image");
            Sprite sprite = ClansAssets.BundleSprite("clans_button");
            if (icon != null && sprite != null) { icon.GetComponent<Image>().sprite = sprite; }
            UITooltip tooltip = button.GetComponent<UITooltip>();
            if (tooltip != null) { tooltip.m_text = "$clans_button_tooltip"; }
            UiBind.OnClick(button.GetComponent<Button>(), ClansWindow.Toggle);

            // Hidden until the window is first built and takes it into its header.
            pvp.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- hotkey

        internal static void Tick() {
            PartyHud.Ensure();
            ActivityTracker.Ensure();
            if (ClansConfig.ClansHotkey == null || Player.m_localPlayer == null) { return; }
            if (ClansConfig.ClansHotkey.Value.IsDown() == false || ClansWindow.IsTyping) { return; }
            if (Console.IsVisible() || Menu.IsVisible() || Minimap.IsOpen() || (Chat.instance != null && Chat.instance.HasFocus())) { return; }
            if (ClansWindow.IsOpen == false && TextInput.IsVisible()) { return; }
            ClansWindow.Toggle();
        }
    }

    internal static class PvpToggleMover {
        internal static void MoveInto(Transform slot) {
            Toggle pvp = InventoryGui.instance != null ? InventoryGui.instance.m_pvp : null;
            if (pvp == null || slot == null) { return; }
            RectTransform rect = (RectTransform)pvp.transform;
            rect.SetParent(slot, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            pvp.gameObject.SetActive(true);
        }
    }
}
