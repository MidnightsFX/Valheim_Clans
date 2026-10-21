using Clans.Client;
using Clans.Net;
using Clans.Settings;
using Clans.UI;
using HarmonyLib;
using Jotunn.Managers;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Gameplay {
    // The clan banners (registered in Prefabs, one per colour in ClansPrefabs.Banners): copies of vanilla banners whose
    // cloth is blank fabric of the banner's colour showing a clan's emblem.
    //
    // Only clan members find them in the hammer's menu, where each icon shows their own clan's emblem on that colour,
    // and only they can place one. Placing stores the builder's clan on the banner's world object (ClanKey). Every
    // client then draws that clan's emblem on it from the clan directory the server sends everyone, and draws it again
    // whenever the directory changes, so an emblem edit reaches every banner the clan has up. A banner whose clan is
    // gone shows the plain cloth. The placement ghost shows the builder's own clan.
    internal sealed class ClanBanner : MonoBehaviour, Hoverable, IPlaced {
        private static readonly HashSet<ClanBanner> shown = new HashSet<ClanBanner>();

        // What the build menu was last set up for, and the icons made for it.
        private static bool menuAllowed;
        private static string menuKey = "";
        private static readonly List<Sprite> menuIcons = new List<Sprite>();

        private ZNetView view;
        private Piece piece;
        private BannerArt.Cloth cloth;
        private BannerArt.Fabric fabric;
        private BannerArt.Art art;

        internal static void Init() {
            ClanClient.DirectoryChanged += OnClansChanged;
            ClanClient.AccountChanged += OnClansChanged;
            ClansSettings.Changed += OnSettingsChanged;
            ClansConfig.EnableBanners.SettingChanged += (_, _) => UpdateMenu();
        }

        internal static bool CanBuild => ClansConfig.EnableBanners.Value && ClanClient.InClan;

        private void Awake() {
            view = GetComponent<ZNetView>();
            piece = GetComponent<Piece>();
            if (GUIManager.IsHeadless()) { return; }
            cloth = BannerArt.FindCloth(gameObject);
            fabric = BannerArt.FabricOf(cloth, ClansPrefabs.BannerOf(Utils.GetPrefabName(gameObject)));
            if (fabric == null) {
                cloth = null;
                return;
            }
            shown.Add(this);
            Redraw();
        }

        private void OnDestroy() {
            shown.Remove(this);
            BannerArt.Release(art);
            art = null;
        }

        // The clan on the banner's world object. The placement ghost has none and shows the builder's clan.
        private long ClanId {
            get {
                ZDO zdo = view != null ? view.GetZDO() : null;
                return zdo != null ? zdo.GetLong(ClansPrefabs.ClanKey, 0L) : ClanClient.Me.ClanId;
            }
        }

        // On the builder's machine, right after placing, while it owns the new world object.
        public void OnPlaced() {
            ZDO zdo = view != null ? view.GetZDO() : null;
            if (zdo == null) { return; }
            zdo.Set(ClansPrefabs.ClanKey, ClanClient.Me.ClanId);
            Redraw();
        }

        private void Redraw() {
            if (cloth == null) { return; }
            // The new art first: a banner keeping its emblem must not drop the only hold on it in between.
            BannerArt.Art next = BannerArt.Acquire(fabric, ClanClient.Clan(ClanId)?.Emblem);
            BannerArt.Release(art);
            art = next;
            BannerArt.Show(cloth, fabric, art);
        }

        private static void RedrawAll() {
            foreach (ClanBanner banner in shown) { banner.Redraw(); }
        }

        private static void OnClansChanged() {
            if (GUIManager.IsHeadless()) { return; }
            RedrawAll();
            UpdateMenu();
        }

        // Only a new emblem catalog changes the art; the other yaml files raise this too.
        private static void OnSettingsChanged() {
            if (GUIManager.IsHeadless() || BannerArt.CatalogChanged() == false) { return; }
            RedrawAll();
            menuKey = null;
            UpdateMenu();
        }

        // ---------------------------------------------------------------- hover

        public string GetHoverName() => Localization.instance.Localize(piece != null ? piece.m_name : "$clans_banner_white");

        public string GetHoverText() {
            ClanSummary clan = ClanClient.Clan(ClanId);
            string owner = clan != null ? clan.Name : Localization.instance.Localize("$clans_banner_no_clan");
            return $"{GetHoverName()}\n{owner}";
        }

        public float GetHoverOffset() => 0f;

        // ---------------------------------------------------------------- build menu

        // The banners' place in the hammer's menu and their icons follow this player's clan.
        private static void UpdateMenu() {
            if (Prefabs.BannerPieces.Count == 0 || GUIManager.IsHeadless()) { return; }
            bool allowed = CanBuild;
            ClanSummary clan = allowed ? ClanClient.Clan(ClanClient.Me.ClanId) : null;
            string key = clan != null ? BannerArt.Key(clan.Emblem) : "";
            if (allowed == menuAllowed && key == menuKey) { return; }
            menuAllowed = allowed;
            menuKey = key;

            foreach (Sprite old in menuIcons) { BannerArt.DestroyIcon(old); }
            menuIcons.Clear();
            foreach (Prefabs.BannerPiece banner in Prefabs.BannerPieces) {
                // The registered prefab still has its vanilla cloth, so its colour's fabric can be made from it.
                BannerArt.Fabric colour = clan != null ? BannerArt.FabricOf(BannerArt.FindCloth(banner.Piece.gameObject), banner.Variant) : null;
                Sprite icon = colour != null ? BannerArt.Icon(colour, clan.Emblem) : null;
                banner.Piece.m_icon = icon != null ? icon : banner.VanillaIcon;
                if (icon != null) { menuIcons.Add(icon); }
            }

            // Rebuilds the menu's piece list and the placement ghost, which shows the clan too.
            Player player = Player.m_localPlayer;
            if (player != null) { player.UpdateAvailablePiecesList(); }
        }

        // Out of the hammer's menu for anyone not in a clan, or with banners switched off.
        [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
        private static class MenuPatch {
            private static void Postfix(PieceTable __instance) {
                if (CanBuild) { return; }
                foreach (Prefabs.BannerPiece banner in Prefabs.BannerPieces) {
                    if (__instance.m_availablePieces.Remove(banner.Piece) == false) { continue; }
                    foreach (List<Piece> category in __instance.m_availablePiecesByCategory) { category.Remove(banner.Piece); }
                }
            }
        }

        // And never placed by them, should a ghost outlast leaving the clan.
        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class PlacePatch {
            private static bool Prefix(Player __instance, Piece piece, ref bool __result) {
                if (CanBuild || Prefabs.IsBanner(piece) == false) { return true; }
                __instance.Message(MessageHud.MessageType.Center, "$clans_banner_need_clan");
                __result = false;
                return false;
            }
        }
    }
}
