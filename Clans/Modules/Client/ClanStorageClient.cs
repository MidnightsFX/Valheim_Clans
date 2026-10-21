using Clans.Core;
using Clans.Net;
using Clans.Server;
using HarmonyLib;
using System;
using UnityEngine;

namespace Clans.Client {
    // Shows a clan storage tab in vanilla's container panel.
    //
    // The tab lives in a Container that belongs to no world object: a Clans-only GameObject that follows the
    // player (so the panel's walk-away check never closes it) with a marker component. Patches below skip
    // everything vanilla's Container does through its ZNetView for that one instance, and nothing of it is
    // saved in the world. Its contents come from the server when a tab opens and go back to the server right
    // after every change (the next frame, so a burst of changes goes as one) and when the panel closes. Nothing
    // can be moved while no tab is held, and the panel closes whenever the server takes the tab away.
    //
    // Other mods that look through containers (crafting from nearby chests and the like) expect a ZNetView.
    // This one gets a ZNetView that never initialised, so it reports no ZDO and is not valid, which such
    // mods already skip; and the object is inactive whenever no tab is open, so scans do not find it.
    internal static class ClanStorageClient {

        internal sealed class Marker : MonoBehaviour {
            private void LateUpdate() {
                if (Player.m_localPlayer != null) { transform.position = Player.m_localPlayer.transform.position; }
            }
        }

        private static GameObject host;
        private static Container container;
        private static long leaseId;
        private static StorageService.TabKind openKind;
        private static int openIndex;
        private static bool loading;
        private static float sendTimer = -1f;

        internal static event Action Changed;

        internal static bool IsOpen => leaseId != 0;
        internal static StorageService.TabKind OpenKind => openKind;
        internal static int OpenIndex => openIndex;

        internal static void Init() {
            ClanNet.RegisterClient(Op.StorageResync, OnResync);
            ClanNet.RegisterClient(Op.StorageClosed, OnClosed);
        }

        internal static bool IsOurs(Container c) => c != null && container != null && c == container;
        internal static bool IsOurs(Inventory inventory) => inventory != null && container != null && container.m_inventory == inventory;

        // Filling the tab with what the server sent, which may hold items storage no longer accepts.
        internal static bool Loading => loading;

        // What the local player may do with the open tab. Personal tabs are theirs entirely; nothing at all
        // while the tab is not held (being switched, or taken away).
        internal static bool CanTake => IsOpen && (openKind == StorageService.TabKind.Personal || ClanClient.HasPermission(ClanPermission.StorageWithdraw));
        internal static bool CanPut => IsOpen && (openKind == StorageService.TabKind.Personal || ClanClient.HasPermission(ClanPermission.StorageDeposit));

        internal static void Open(StorageService.TabKind kind, int index, Action<NetResult> done = null) {
            if (Player.m_localPlayer == null || InventoryGui.instance == null) { return; }
            // Hand back the tab already open, so the server has its latest contents first, and close the panel:
            // a tab whose lease is given up must not stay usable while the next one is on its way (or refused).
            ClosePanel();
            SendClose();
            ClanRequests.Send(Op.OpenStorage, pkg => { pkg.Write((byte)kind); pkg.Write(index); }, result => {
                if (result.Ok) { Show(result.Extra); }
                done?.Invoke(result);
            });
        }

        private static void Show(ZPackage extra) {
            leaseId = extra.ReadLong();
            openKind = (StorageService.TabKind)extra.ReadByte();
            openIndex = extra.ReadInt();
            byte[] bytes = extra.ReadByteArray();
            Ensure();
            container.m_name = openKind == StorageService.TabKind.Personal
                ? "$clans_storage_personal"
                : string.Format(Localization.instance.Localize("$clans_storage_tab_name"), openIndex + 1);
            Inventory inventory = container.GetInventory();
            inventory.m_name = container.m_name;
            Load(bytes);
            UI.ClansWindow.Close();
            if (Player.m_localPlayer != null) { host.transform.position = Player.m_localPlayer.transform.position; }
            host.SetActive(true);
            InventoryGui.instance.Show(container);
            Changed?.Invoke();
        }

        private static void Ensure() {
            if (container != null) { return; }
            host = new GameObject("Clans_Storage");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.SetActive(false);
            host.AddComponent<Marker>();
            GameObject view = new GameObject("Clans_StorageView");
            view.SetActive(false);
            view.transform.SetParent(host.transform, false);
            view.AddComponent<ZNetView>();
            container = host.AddComponent<Container>();
            container.m_name = "$clans_storage";
            container.m_width = StorageService.Width;
            container.m_height = StorageService.Height;
            host.SetActive(true);
            host.SetActive(false);
        }

        private static void Load(byte[] bytes) {
            Inventory inventory = container.GetInventory();
            loading = true;
            try {
                if (bytes == null || bytes.Length == 0) {
                    inventory.RemoveAll();
                } else {
                    inventory.Load(new ZPackage(bytes));
                }
            } finally {
                loading = false;
            }
        }

        private static byte[] Save() {
            ZPackage pkg = new ZPackage();
            container.GetInventory().Save(pkg);
            return pkg.GetArray();
        }

        private static void OnInventoryChanged() {
            if (loading || leaseId == 0) { return; }
            sendTimer = 0f;
        }

        internal static void Tick(float dt) {
            if (sendTimer < 0f) { return; }
            sendTimer -= dt;
            if (sendTimer > 0f) { return; }
            sendTimer = -1f;
            if (leaseId == 0 || container == null) { return; }
            long id = leaseId;
            byte[] bytes = Save();
            ClanRequests.Report(Op.StorageSnapshot, pkg => { pkg.Write(id); pkg.Write(bytes); });
        }

        // The panel closed (or another tab is opening): the final contents go back and the lease is given up.
        private static void SendClose() {
            if (leaseId == 0 || container == null) { return; }
            long id = leaseId;
            byte[] bytes = Save();
            leaseId = 0;
            sendTimer = -1f;
            ClanRequests.Report(Op.CloseStorage, pkg => { pkg.Write(id); pkg.Write(bytes); });
            if (host != null) { host.SetActive(false); }
            Changed?.Invoke();
        }

        // The server took the tab away: close it without sending anything back.
        private static void OnClosed(uint reqId, ZPackage body) {
            long id = body.ReadLong();
            string reason = body.ReadString();
            if (id != leaseId || container == null) { return; }
            leaseId = 0;
            sendTimer = -1f;
            ClosePanel();
            loading = true;
            try { container.GetInventory().RemoveAll(); } finally { loading = false; }
            if (host != null) { host.SetActive(false); }
            if (string.IsNullOrEmpty(reason) == false) { ClanNotices.ShowError(Localization.instance.Localize(reason)); }
            Changed?.Invoke();
        }

        private static void ClosePanel() {
            InventoryGui gui = InventoryGui.instance;
            if (gui != null && container != null && gui.m_currentContainer == container) { gui.CloseContainer(); }
        }

        private static void OnResync(uint reqId, ZPackage body) {
            long id = body.ReadLong();
            byte[] bytes = body.ReadByteArray();
            string reason = body.ReadString();
            if (id != leaseId || container == null) { return; }
            Load(bytes);
            ClanNotices.ShowError(Localization.instance.Localize(reason));
        }

        internal static void Reset() {
            // Whatever was last changed goes back while the connection may still be up.
            try { SendClose(); } catch (Exception) { /* the connection is already gone */ }
            leaseId = 0;
            sendTimer = -1f;
            if (container != null) { container.GetInventory().RemoveAll(); }
        }

        // ---------------------------------------------------------------- vanilla Container, for our instance

        [HarmonyPatch(typeof(Container), "Awake")]
        private static class AwakePatch {
            private static bool Prefix(Container __instance) {
                if (__instance.GetComponent<Marker>() == null) { return true; }
                __instance.m_nview = __instance.GetComponentInChildren<ZNetView>(true);
                __instance.m_inventory = new Inventory(__instance.m_name, null, __instance.m_width, __instance.m_height);
                __instance.m_inventory.m_onChanged += OnInventoryChanged;
                return false;
            }
        }

        [HarmonyPatch(typeof(Container), nameof(Container.IsOwner))]
        private static class IsOwnerPatch {
            private static bool Prefix(Container __instance, ref bool __result) {
                if (IsOurs(__instance) == false) { return true; }
                __result = true;
                return false;
            }
        }

        // The panel calls this every frame with true and once with false when it closes.
        [HarmonyPatch(typeof(Container), nameof(Container.SetInUse))]
        private static class SetInUsePatch {
            private static bool Prefix(Container __instance, bool inUse) {
                if (IsOurs(__instance) == false) { return true; }
                if (inUse == false) { SendClose(); }
                return false;
            }
        }

        // Holding the use key stacks into the container; without the deposit right it does nothing.
        [HarmonyPatch(typeof(Container), nameof(Container.StackAll))]
        private static class StackAllPatch {
            private static bool Prefix(Container __instance) {
                if (IsOurs(__instance) == false) { return true; }
                if (CanPut && Player.m_localPlayer != null) {
                    __instance.GetInventory().StackAll(Player.m_localPlayer.GetInventory());
                }
                return false;
            }
        }
    }
}
