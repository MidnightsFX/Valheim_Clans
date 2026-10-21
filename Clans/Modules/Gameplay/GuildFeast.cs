using Clans.Settings;
using Clans.UI;
using HarmonyLib;
using Splatform;
using System;
using UnityEngine;

namespace Clans.Gameplay {
    // On every guild feast (Prefabs.RegisterGuildFeast), a copy of a vanilla feast that a clan member sets out
    // and anyone may eat from. It never runs out: eating from it takes nothing away. Its ZDO says when it is
    // cleared away, and whoever owns it clears it then (it cannot be packed up).
    internal sealed class GuildFeast : MonoBehaviour {
        // ZNet time, in ticks.
        internal const string ExpiresKey = "clans_feast_expires";

        // In front of the player, a step away, so they do not stand in it.
        private const float SpawnDistance = 2f;
        private const float CheckSeconds = 2f;

        private static int groundMask;

        private ZNetView nview;

        private void Awake() {
            nview = GetComponent<ZNetView>();
            if (nview == null || nview.GetZDO() == null) { return; }
            InvokeRepeating(nameof(Check), CheckSeconds, CheckSeconds);
        }

        // Seconds until it is cleared away; null until its owner has set the time.
        internal double? SecondsLeft() {
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            long expires = zdo != null ? zdo.GetLong(ExpiresKey, 0L) : 0L;
            if (expires == 0L || ZNet.instance == null) { return null; }
            return (expires - ZNet.instance.GetTime().Ticks) / (double)TimeSpan.TicksPerSecond;
        }

        private void Check() {
            if (nview == null || nview.IsValid() == false || nview.IsOwner() == false) { return; }
            ZDO zdo = nview.GetZDO();
            // Spawned some other way (the spawn command): it gets the usual time from now.
            if (zdo.GetLong(ExpiresKey, 0L) == 0L) { zdo.Set(ExpiresKey, ExpiryFromNow(ClansSettings.Feasts.LifetimeMinutes)); }
            double? left = SecondsLeft();
            if (left.HasValue && left.Value <= 0) { nview.Destroy(); }
        }

        private static long ExpiryFromNow(int minutes) {
            return ZNet.instance.GetTime().Ticks + TimeSpan.FromMinutes(Math.Max(1, minutes)).Ticks;
        }

        // Sets a feast out in front of the local player, the way vanilla places one from the feast table.
        internal static bool Spawn(ClansPrefabs.GuildFeastVariant variant, int lifetimeMinutes) {
            Player player = Player.m_localPlayer;
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(variant.Prefab) : null;
            if (player == null || prefab == null || ZNet.instance == null) { return false; }

            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            if (groundMask == 0) { groundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"); }
            // Closer when a wall is in the way, so it is not set out inside it.
            float distance = SpawnDistance;
            if (Physics.Raycast(player.transform.position + Vector3.up, forward, out RaycastHit wall, SpawnDistance + 0.6f, groundMask)) {
                distance = Mathf.Clamp(wall.distance - 0.6f, 0.6f, SpawnDistance);
            }
            Vector3 spot = player.transform.position + forward * distance;
            if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, groundMask)) {
                spot = hit.point;
            } else {
                spot.y = player.transform.position.y;
            }
            // Facing the player.
            Quaternion rotation = Quaternion.LookRotation(-forward);

            GameObject go = Instantiate(prefab, spot, rotation);
            ZNetView view = go.GetComponent<ZNetView>();
            ZDO zdo = view != null ? view.GetZDO() : null;
            if (zdo != null) { zdo.Set(ExpiresKey, ExpiryFromNow(lifetimeMinutes)); }
            Piece piece = go.GetComponent<Piece>();
            if (piece != null) { piece.SetCreator(player.GetPlayerID(), PlatformManager.DistributionPlatform.LocalUser.PlatformUserID); }
            WearNTear wear = go.GetComponent<WearNTear>();
            if (wear != null) { wear.OnPlaced(); }
            ItemDrop drop = go.GetComponent<ItemDrop>();
            if (drop != null) { drop.MakePiece(sendRPC: true); }
            if (piece != null) { piece.m_placeEffect.Create(spot, rotation, go.transform); }
            return true;
        }

        // Vanilla's eat on the feast's owner, minus taking a serving away: the eater still gets the food and everyone
        // still sees the effect, and the feast stays full.
        [HarmonyPatch(typeof(Feast), "RPC_TryEat")]
        private static class EatPatch {
            private static bool Prefix(Feast __instance, long sender) {
                if (__instance.GetComponent<GuildFeast>() == null) { return true; }
                ZNetView view = __instance.m_nview;
                if (view == null || view.IsValid() == false || view.IsOwner() == false) { return false; }
                view.InvokeRPC(ZNetView.Everybody, "RPC_OnEat");
                view.InvokeRPC(sender, "RPC_EatConfirmation");
                return false;
            }
        }

        // Vanilla's "eat ( 7/10 )" without the count, which never goes down, and with how long it still stands.
        [HarmonyPatch(typeof(Feast), nameof(Feast.GetHoverText))]
        private static class HoverPatch {
            private static bool Prefix(Feast __instance, ref string __result) {
                GuildFeast guild = __instance.GetComponent<GuildFeast>();
                Player player = Player.m_localPlayer;
                if (guild == null || player == null) { return true; }
                if (Vector3.Distance(player.transform.position, __instance.transform.position) >= __instance.m_useDistance) {
                    __result = Localization.instance.Localize("<color=#888888>$piece_toofar</color>");
                    return false;
                }
                __result = Localization.instance.Localize(__instance.GetHoverName() + "\n[<color=yellow><b>$KEY_Use</b></color>] $item_eat");
                double? left = guild.SecondsLeft();
                if (left.HasValue) {
                    int minutes = Mathf.Max(1, Mathf.CeilToInt((float)(left.Value / 60.0)));
                    __result += "\n<color=#B4B4B4>" + UiBind.Loc("$clans_feast_leaves", minutes) + "</color>";
                }
                return false;
            }
        }
    }
}
