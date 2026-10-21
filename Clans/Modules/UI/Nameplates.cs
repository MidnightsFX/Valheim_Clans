using Clans.Client;
using Clans.Net;
using Clans.Settings;
using HarmonyLib;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI {
    // Clan emblems next to other players' names, on vanilla's player HUD (EnemyHud.m_baseHudPlayer).
    //
    // The emblem is added to the player HUD template once, so every name plate vanilla clones from it has
    // one. Each plate is only redrawn when clan data changes, or when vanilla makes a new one (a player who
    // went out of sight and came back). Players of a clan at war with yours get a red name.
    //
    // Star Level System patches EnemyHud too (including a transpiler on UpdateHuds) but never touches the
    // player template, and these are plain postfixes, so the two do not meet.
    [HarmonyPatch(typeof(EnemyHud))]
    internal static class Nameplates {
        private const string EmblemName = "Clans_Emblem";
        private const float EmblemSize = 28f;
        private static readonly Color WarColor = new Color(1f, 0.35f, 0.3f, 1f);

        // By plate, not by player: vanilla destroys a plate and clones a fresh one as players come and go.
        private static readonly Dictionary<GameObject, int> drawn = new Dictionary<GameObject, int>();
        private static int version = 1;
        private static bool subscribed;
        private static Color nameColor = Color.white;

        [HarmonyPostfix]
        [HarmonyPatch("Awake")]
        private static void AddToTemplate(EnemyHud __instance) {
            Subscribe();
            drawn.Clear();
            GameObject template = __instance.m_baseHudPlayer;
            if (template == null || template.transform.Find(EmblemName) != null) { return; }
            Transform name = template.transform.Find("Name");
            if (name != null && name.GetComponent<TMP_Text>() is TMP_Text text) { nameColor = text.color; }

            GameObject emblem = new GameObject(EmblemName, typeof(RectTransform));
            emblem.layer = template.layer;
            emblem.transform.SetParent(template.transform, false);
            RectTransform rect = (RectTransform)emblem.transform;
            rect.sizeDelta = new Vector2(EmblemSize, EmblemSize);
            rect.anchoredPosition = name != null ? ((RectTransform)name).anchoredPosition : Vector2.zero;
            AddLayer(emblem.transform, "Back", 0f);
            AddLayer(emblem.transform, "Front", EmblemSize * 0.2f);
            emblem.SetActive(false);
        }

        private static void AddLayer(Transform parent, string name, float inset) {
            GameObject layer = new GameObject(name, typeof(RectTransform));
            layer.layer = parent.gameObject.layer;
            layer.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)layer.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            Image image = layer.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("UpdateHuds")]
        private static void Refresh(EnemyHud __instance) {
            foreach (KeyValuePair<Character, EnemyHud.HudData> entry in __instance.m_huds) {
                if (entry.Key is Player player && entry.Value.m_gui != null && entry.Value.m_gui.activeSelf) {
                    GameObject plate = entry.Value.m_gui;
                    if (drawn.TryGetValue(plate, out int stamp) && stamp == version) { continue; }
                    if (Draw(player, entry.Value)) { drawn[plate] = version; }
                }
            }
            if (drawn.Count > 64) { Prune(); }
        }

        // Returns false when the player's account is not known yet, so the plate is tried again.
        private static bool Draw(Player player, EnemyHud.HudData hud) {
            Transform emblem = hud.m_gui.transform.Find(EmblemName);
            string account = PlayerMap.AccountOf(player);
            if (account == null) {
                if (emblem != null) { emblem.gameObject.SetActive(false); }
                return false;
            }
            long clanId = ClanClient.ClanIdOf(account);
            ClanSummary clan = ClanClient.Clan(clanId);
            bool atWar = ClanClient.ActiveWar(ClanClient.Me.ClanId, clanId) != null;
            if (hud.m_name != null) { hud.m_name.color = atWar ? WarColor : nameColor; }

            bool show = clan != null && ClansConfig.ShowNameplateEmblems.Value;
            if (emblem == null) { return true; }
            emblem.gameObject.SetActive(show);
            if (show == false) { return true; }
            EmblemView.Apply(emblem, clan.Emblem);
            if (hud.m_name != null) {
                // Just left of the name, whatever its length.
                float width = hud.m_name.GetPreferredValues(hud.m_name.text).x;
                Vector2 namePos = hud.m_name.rectTransform.anchoredPosition;
                ((RectTransform)emblem).anchoredPosition = new Vector2(namePos.x - width / 2f - EmblemSize / 2f - 4f, namePos.y);
            }
            return true;
        }

        // Plates vanilla has destroyed.
        private static void Prune() {
            List<GameObject> gone = new List<GameObject>();
            foreach (GameObject plate in drawn.Keys) {
                if (plate == null) { gone.Add(plate); }
            }
            foreach (GameObject plate in gone) { drawn.Remove(plate); }
        }

        private static void Subscribe() {
            if (subscribed) { return; }
            subscribed = true;
            ClanClient.DirectoryChanged += Invalidate;
            ClanClient.RosterChanged += Invalidate;
            ClanClient.AccountChanged += Invalidate;
            ClansSettings.Changed += Invalidate;
            ClansConfig.ShowNameplateEmblems.SettingChanged += (_, _) => Invalidate();
        }

        private static void Invalidate() => version++;
    }
}
