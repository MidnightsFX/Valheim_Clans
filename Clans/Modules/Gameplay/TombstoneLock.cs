using Clans.Client;
using Clans.Core;
using Clans.Net;
using Clans.Settings;
using Clans.UI;
using HarmonyLib;

namespace Clans.Gameplay {
    // Tombstones open for their owner and the owner's clan only, so nobody else can loot a player they killed in PvP
    // or take what someone left where they died. A player without a clan is the only one who can open theirs, and a
    // player can make theirs private: the Client config sets it for new tombstones, and alt-use on one of their own
    // switches that one.
    //
    // The dying player's client stamps the new tombstone with their account, their clan and that switch. Whether the
    // owner is in the looter's clan is decided from what the looter's client knows now: the owner's roster entry while
    // they are online, otherwise the looter's own clan's member list, so someone who left or was removed since loses
    // their old clan's access to it (and a clan they joined gains it). The clan stamped at death only names the clan
    // in the hover text of an offline owner in another clan.
    //
    // Each client checks its own player: in the tombstone's hover text and use, and in Container.CheckAccess, which
    // every way into a container's items asks (open, take all). The same question reaching the container's owner from
    // another client is left alone: the asker knows its own clan better than anyone else does, and a client that
    // skipped the check could take over the tombstone's world object anyway.
    //
    // Tombstones made without Clans, before it was installed, stay open to everyone.
    internal static class TombstoneLock {
        private const string PrivateRpc = "Clans_TombPrivate";
        private static readonly int OwnerHash = "clans_tomb_owner".GetStableHashCode();
        private static readonly int PrivateHash = "clans_tomb_private".GetStableHashCode();
        private static readonly int ClanHash = ClansPrefabs.ClanKey.GetStableHashCode();

        private static bool Enabled => ClansConfig.LockTombstones.Value;

        private static long MyPlayerId => Game.instance != null ? Game.instance.GetPlayerProfile().GetPlayerID() : 0L;

        // The server's word for this player's account once it has said hello, the same rule worked out here before.
        private static string MyAccount => ClanClient.MyAccount.Length > 0
            ? ClanClient.MyAccount
            : AccountId.ForLocalUser(Game.instance.GetPlayerProfile().GetName());

        private static ZDO ZdoOf(TombStone tomb) {
            ZNetView view = tomb != null ? tomb.m_nview : null;
            return view != null && view.IsValid() ? view.GetZDO() : null;
        }

        private static string OwnerOf(ZDO zdo) => zdo.GetString(OwnerHash, "");

        // Locked, with the switch on, when Clans stamped it.
        private static bool Locked(ZDO zdo) => zdo != null && Enabled && OwnerOf(zdo).Length > 0;

        // The character that died, or any other character of the same account.
        private static bool IsMine(ZDO zdo) => zdo.GetLong(ZDOVars.s_owner, 0L) == MyPlayerId || OwnerOf(zdo) == MyAccount;

        internal static bool MayOpen(ZDO zdo) {
            if (Locked(zdo) == false || IsMine(zdo)) { return true; }
            if (zdo.GetBool(PrivateHash)) { return false; }
            long myClan = ClanClient.Me.ClanId;
            return myClan != 0 && ClanOf(zdo) == myClan;
        }

        // The owner's clan, as far as this client knows (see the top).
        private static long ClanOf(ZDO zdo) {
            string owner = OwnerOf(zdo);
            RosterEntry online = ClanClient.RosterOf(owner);
            if (online != null) { return online.ClanId; }
            long stamped = zdo.GetLong(ClanHash, 0L);
            ClanDetailInfo myClan = ClanClient.MyClan;
            if (myClan != null && myClan.Id != 0 && myClan.Id == ClanClient.Me.ClanId) {
                if (myClan.Member(owner) != null) { return myClan.Id; }
                if (stamped == myClan.Id) { return 0L; }
            }
            return stamped;
        }

        // Who may open it, for a player who may not.
        private static string LockedText(TombStone tomb, ZDO zdo) {
            ClanSummary clan = zdo.GetBool(PrivateHash) ? null : ClanClient.Clan(ClanOf(zdo));
            return clan != null
                ? UiBind.Loc("$clans_tomb_locked_clan", clan.Name)
                : UiBind.Loc("$clans_tomb_locked_owner", tomb.GetOwnerName());
        }

        // For the owner: who can open it, and the alt-use that switches it while they are in a clan.
        private static string OwnerLines(ZDO zdo) {
            bool inClan = ClanClient.InClan;
            bool shared = inClan && zdo.GetBool(PrivateHash) == false;
            string lines = "\n<color=#B4B4B4>" + UiBind.Loc(shared ? "$clans_tomb_shared" : "$clans_tomb_private") + "</color>";
            if (inClan == false) { return lines; }
            // The same key vanilla's hover texts name for alt-use.
            string alt = ZInput.IsNonClassicFunctionality() && ZInput.IsGamepadActive() ? "$KEY_AltKeys" : "$KEY_AltPlace";
            return lines + Localization.instance.Localize($"\n[<color=yellow><b>{alt} + $KEY_Use</b></color>] " + (shared ? "$clans_tomb_make_private" : "$clans_tomb_make_shared"));
        }

        // Asked of whoever owns the tombstone's world object, which may be another client.
        private static void SetPrivate(TombStone tomb, Humanoid character, bool value) {
            tomb.m_nview.InvokeRPC(PrivateRpc, value);
            character.Message(MessageHud.MessageType.Center, value ? "$clans_tomb_now_private" : "$clans_tomb_now_shared");
        }

        // Only the player whose tombstone it is may switch it.
        private static void OnSetPrivate(ZNetView view, long sender, bool value) {
            if (view == null || view.IsValid() == false || view.IsOwner() == false) { return; }
            ZDO zdo = view.GetZDO();
            string owner = OwnerOf(zdo);
            string account = sender == ZDOMan.GetSessionID() ? MyAccount : PlayerMap.AccountOfPeer(sender);
            if (owner.Length == 0 || account != owner) { return; }
            zdo.Set(PrivateHash, value);
        }

        // ---------------------------------------------------------------- patches

        [HarmonyPatch(typeof(TombStone), "Awake")]
        private static class AwakePatch {
            private static void Postfix(TombStone __instance) {
                ZNetView view = __instance.m_nview;
                if (view == null || view.IsValid() == false) { return; }
                view.Register<bool>(PrivateRpc, (sender, value) => OnSetPrivate(view, sender, value));
            }
        }

        // On the dying player's client, which owns the new tombstone. Always stamped, so switching the lock on later
        // covers it too.
        [HarmonyPatch(typeof(TombStone), nameof(TombStone.Setup))]
        private static class SetupPatch {
            private static void Postfix(TombStone __instance, long ownerUID) {
                ZDO zdo = ZdoOf(__instance);
                if (zdo == null || Game.instance == null || ownerUID != MyPlayerId) { return; }
                zdo.Set(OwnerHash, MyAccount);
                zdo.Set(ClanHash, ClanClient.Me.ClanId);
                zdo.Set(PrivateHash, ClansConfig.PrivateTombstones.Value);
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.GetHoverText))]
        private static class HoverPatch {
            private static void Postfix(TombStone __instance, ref string __result) {
                if (string.IsNullOrEmpty(__result)) { return; }
                ZDO zdo = ZdoOf(__instance);
                if (Locked(zdo) == false) { return; }
                if (IsMine(zdo)) {
                    __result += OwnerLines(zdo);
                } else if (MayOpen(zdo) == false) {
                    // The name line stays; the open prompt becomes who may open it.
                    __result = Localization.instance.Localize(__instance.m_text + " " + __instance.GetOwnerName())
                        + "\n<color=#B4B4B4>" + LockedText(__instance, zdo) + "</color>";
                }
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.Interact))]
        private static class InteractPatch {
            private static bool Prefix(TombStone __instance, Humanoid character, bool hold, bool alt, ref bool __result) {
                if (hold) { return true; }
                ZDO zdo = ZdoOf(__instance);
                if (Locked(zdo) == false || __instance.m_container.GetInventory().NrOfItems() == 0) { return true; }
                if (IsMine(zdo)) {
                    if (alt == false || ClanClient.InClan == false) { return true; }
                    SetPrivate(__instance, character, zdo.GetBool(PrivateHash) == false);
                    __result = true;
                    return false;
                }
                if (MayOpen(zdo)) { return true; }
                character.Message(MessageHud.MessageType.Center, LockedText(__instance, zdo));
                __result = true;
                return false;
            }
        }

        // Asked on the asking player's client and again on the container's owner; only the first is this client's to
        // answer (see the top).
        [HarmonyPatch(typeof(Container), "CheckAccess")]
        private static class AccessPatch {
            private static void Postfix(Container __instance, long playerID, ref bool __result) {
                if (__result == false || Enabled == false || playerID != MyPlayerId) { return; }
                TombStone tomb = __instance.GetComponent<TombStone>();
                if (tomb != null && MayOpen(ZdoOf(tomb)) == false) { __result = false; }
            }
        }
    }
}
