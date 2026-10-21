using Clans.Client;
using Clans.Settings;
using HarmonyLib;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Clans.UI {
    // "Visible to clan", "Ping clan only" and "Share bounties with clan" on the large map, stacked above vanilla's
    // "Shared map" and "Visible to other players" in the bottom right corner. Each is a copy of vanilla's own
    // switch, shown while the server allows the feature and the player is in a clan, and backed by its Client
    // config entry.
    internal static class MapToggles {
        private sealed class Switch {
            internal GameObject Panel;
            internal Toggle Toggle;
        }

        private static Switch visibleToClan;
        private static Switch pingClanOnly;
        private static Switch shareBounties;

        [HarmonyPatch(typeof(Minimap), "Start")]
        private static class StartPatch {
            private static void Postfix(Minimap __instance) {
                try {
                    Build(__instance);
                } catch (Exception e) {
                    Logger.LogWarning($"The clan switches could not be added to the map: {e.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Minimap), "Update")]
        private static class UpdatePatch {
            private static void Postfix(Minimap __instance) {
                if (__instance.m_mode != Minimap.MapMode.Large) { return; }
                Sync(visibleToClan, ClanMap.PositionsAvailable, ClansConfig.VisibleToClan.Value);
                Sync(pingClanOnly, ClanMap.PingsAvailable, ClansConfig.PingClanOnly.Value);
                Sync(shareBounties, BountyShare.Available, ClansConfig.ShareBounties.Value);
            }
        }

        private static void Build(Minimap map) {
            visibleToClan = null;
            pingClanOnly = null;
            shareBounties = null;
            RectTransform publicPanel = map.m_publicPosition != null ? map.m_publicPosition.transform.parent as RectTransform : null;
            if (publicPanel == null || publicPanel.parent == null) { return; }
            // Vanilla's two switches sit one above the other; the clan's go on top, the same distance apart.
            RectTransform sharedPanel = publicPanel.parent.Find("SharedPanel") as RectTransform;
            float top = Bottom(publicPanel);
            float step = publicPanel.rect.height + 9f;
            if (sharedPanel != null && Bottom(sharedPanel) > top) {
                step = Bottom(sharedPanel) - top;
                top = Bottom(sharedPanel);
            }
            visibleToClan = Copy(publicPanel, "Clans_VisibleToClan", top + step, "$clans_map_visible_to_clan", on => ClansConfig.VisibleToClan.Value = on);
            pingClanOnly = Copy(publicPanel, "Clans_PingClanOnly", top + 2f * step, "$clans_map_ping_clan_only", on => ClansConfig.PingClanOnly.Value = on);
            shareBounties = Copy(publicPanel, "Clans_ShareBounties", top + 3f * step, "$clans_map_share_bounties", on => ClansConfig.ShareBounties.Value = on);
        }

        private static float Bottom(RectTransform rect) => rect.anchoredPosition.y - rect.pivot.y * rect.rect.height;

        private static Switch Copy(RectTransform source, string name, float bottom, string label, Action<bool> changed) {
            GameObject go = Object.Instantiate(source.gameObject, source.parent, false);
            go.name = name;
            RectTransform rect = (RectTransform)go.transform;
            rect.anchoredPosition = new Vector2(source.anchoredPosition.x, bottom + rect.pivot.y * rect.rect.height);
            // Vanilla's gamepad button flips vanilla's switch only.
            foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true)) {
                if (pad.m_hint != null) { Object.Destroy(pad.m_hint); }
                Object.Destroy(pad);
            }
            Toggle toggle = go.GetComponentInChildren<Toggle>(true);
            if (toggle == null) {
                Object.Destroy(go);
                return null;
            }
            // A fresh event drops the copied call to vanilla's public position handler.
            toggle.onValueChanged = new Toggle.ToggleEvent();
            toggle.onValueChanged.AddListener(on => changed(on));
            Transform caption = toggle.transform.Find("Label");
            string text = UiBind.Loc(label);
            TMP_Text tmp = caption != null ? caption.GetComponent<TMP_Text>() : null;
            Text legacy = caption != null ? caption.GetComponent<Text>() : null;
            if (tmp != null) { tmp.text = text; } else if (legacy != null) { legacy.text = text; }
            go.SetActive(false);
            return new Switch { Panel = go, Toggle = toggle };
        }

        private static void Sync(Switch control, bool shown, bool on) {
            if (control == null || control.Panel == null) { return; }
            if (control.Panel.activeSelf != shown) { control.Panel.SetActive(shown); }
            if (shown && control.Toggle.isOn != on) { control.Toggle.SetIsOnWithoutNotify(on); }
        }
    }
}
