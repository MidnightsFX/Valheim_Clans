using Clans.Client;
using Clans.Core;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Gameplay {
    // The Race's start totem and the World Boss totem. A member of the owning clan whose rank may start
    // activities uses it to begin; whoever owns its world object then runs the world side of the activity.
    // Once a race is running, any member of its clan takes a chest from the start totem with each use.
    internal sealed class ClanTotem : MonoBehaviour, Hoverable, Interactable {
        private const float TickSeconds = 1f;

        // A chest asked for and not yet handed over: one request at a time.
        private static bool takingChest;

        private ZNetView view;
        private ClansPrefabs.TotemKind kind;
        private long activityId;
        private WorldBossRunner boss;

        private void Awake() {
            view = GetComponent<ZNetView>();
            if (view == null || view.GetZDO() == null) { return; }
            kind = (ClansPrefabs.TotemKind)view.GetZDO().GetInt(ClansPrefabs.KindKey, 0);
            activityId = view.GetZDO().GetLong(ClansPrefabs.ActivityKey, 0L);
            if (kind == ClansPrefabs.TotemKind.WorldBoss) { boss = new WorldBossRunner(this, view, activityId); }
            InvokeRepeating(nameof(Tick), Random.Range(0.5f, 1.5f), TickSeconds);
        }

        internal ActivityInfo Info => ActivityClient.Get(activityId);

        private void Tick() {
            if (view == null || view.IsValid() == false) { return; }
            ActivityInfo info = Info;
            // Every client near draws the mist; only the owner runs the fight.
            if (kind == ClansPrefabs.TotemKind.WorldBoss) { UpdateMist(info); }
            if (view.IsOwner() == false) { return; }
            Totems.SnapToGround(view, transform);
            // The boss runner also needs to hear that the fight is gone, to stop waves still running here.
            if (kind == ClansPrefabs.TotemKind.WorldBoss) { boss.Tick(info); }
        }

        private void OnDestroy() => ClearMist();

        // ---------------------------------------------------------------- World Boss spawn mist

        private readonly List<GameObject> mist = new List<GameObject>();
        private string mistPoints = "";

        // Dark mist over each spawn point while the fight is on, from the points the owner stored when it began.
        private void UpdateMist(ActivityInfo info) {
            bool fighting = info != null && (info.State == ActivityState.Running || info.State == ActivityState.BossPhase);
            string points = fighting ? view.GetZDO().GetString(ClansPrefabs.PortalsKey, "") : "";
            if (points == mistPoints) { return; }
            ClearMist();
            mistPoints = points;
            foreach (Vector3 point in WorldBossRunner.ParsePoints(points)) {
                GameObject column = PortalMist.Create(point);
                if (column != null) { mist.Add(column); }
            }
        }

        private void ClearMist() {
            foreach (GameObject column in mist) {
                if (column != null) { Destroy(column); }
            }
            mist.Clear();
            mistPoints = "";
        }

        // ---------------------------------------------------------------- race chests

        private bool OffersChests(ActivityInfo info) {
            return kind == ClansPrefabs.TotemKind.RaceStart && info.Type == ActivityType.Race && info.State == ActivityState.Running
                && info.ClanId == ClanClient.Me.ClanId;
        }

        // One chest per use, as many as the player likes, but none while they carry more than they can bear.
        // The server numbers each chest, which is what the destination counts.
        private static bool TakeChest(ActivityInfo info, Player player) {
            if (takingChest) { return true; }
            if (player.IsEncumbered()) {
                player.Message(MessageHud.MessageType.Center, "$clans_race_too_heavy");
                return true;
            }
            GameObject prefab = ChestPrefab(info);
            if (prefab == null) { return true; }
            if (player.GetInventory().CanAddItem(prefab, 1) == false) {
                player.Message(MessageHud.MessageType.Center, "$msg_noroom");
                return true;
            }
            takingChest = true;
            long raceId = info.Id;
            ActivityActions.TakeRaceChest(raceId, result => {
                takingChest = false;
                if (result.Ok == false) {
                    ClanNotices.ShowError(result.Message());
                    return;
                }
                GiveChest(prefab, raceId, result.Extra.ReadInt());
            });
            return true;
        }

        private static GameObject ChestPrefab(ActivityInfo info) {
            string name = ClansPrefabs.RaceChest(Mathf.Clamp(info.Difficulty, 0, ClansPrefabs.RaceTiers - 1));
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null;
            if (prefab == null || prefab.GetComponent<ItemDrop>() == null) {
                Logger.LogError($"Race chest prefab {name} is missing.");
                return null;
            }
            return prefab;
        }

        private static void GiveChest(GameObject prefab, long raceId, int serial) {
            Player player = Player.m_localPlayer;
            if (player == null || serial <= 0) { return; }
            ItemDrop.ItemData chest = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            chest.m_dropPrefab = prefab;
            chest.m_stack = 1;
            chest.m_customData[ClansPrefabs.RaceIdData] = raceId.ToString();
            chest.m_customData[ClansPrefabs.RaceSerialData] = serial.ToString();
            if (player.GetInventory().AddItem(chest)) {
                player.ShowPickupMessage(chest, 1);
                return;
            }
            // The inventory filled while the server answered: at the player's feet instead.
            ItemDrop.DropItem(chest, 1, player.transform.position + player.transform.forward + Vector3.up, Quaternion.identity);
        }

        // ---------------------------------------------------------------- hover and use

        public string GetHoverName() {
            ActivityInfo info = Info;
            return info == null ? "" : Localization.instance.Localize(Server.ActivityService.Name(info.Type));
        }

        public string GetHoverText() {
            ActivityInfo info = Info;
            if (info == null) { return Localization.instance.Localize("$clans_totem_inactive"); }
            string clan = ClanClient.Clan(info.ClanId)?.Name ?? "";
            string text = $"{GetHoverName()}\n{clan}";
            if (info.State == ActivityState.Placed) {
                if (CanBegin(info)) {
                    text += "\n[<color=yellow><b>$KEY_Use</b></color>] $clans_totem_begin";
                } else {
                    text += "\n$clans_totem_waiting";
                }
            } else if (info.Type == ActivityType.WorldBoss) {
                text += "\n" + Localization.instance.Localize("$clans_totem_wave") + $" {Mathf.Max(1, info.Phase)} / {ClansSettings.Activities.WorldBoss.Waves}";
            } else if (info.Type == ActivityType.Race) {
                text += "\n" + string.Format(Localization.instance.Localize("$clans_totem_race_progress"), info.Delivered, info.ChestsTotal);
                if (OffersChests(info)) { text += "\n[<color=yellow><b>$KEY_Use</b></color>] $clans_totem_take_chest"; }
            }
            return Localization.instance.Localize(text);
        }

        public float GetHoverOffset() => 0f;

        private static bool CanBegin(ActivityInfo info) {
            return info.ClanId == ClanClient.Me.ClanId && ClanClient.HasPermission(ClanPermission.StartActivities);
        }

        public bool Interact(Humanoid user, bool hold, bool alt) {
            if (hold || user != Player.m_localPlayer) { return false; }
            ActivityInfo info = Info;
            if (info == null) { return false; }
            if (OffersChests(info)) { return TakeChest(info, Player.m_localPlayer); }
            if (info.State != ActivityState.Placed || CanBegin(info) == false) { return false; }
            ActivityActions.Begin(info.Id, result => {
                if (result.Ok == false) { ClanNotices.ShowError(result.Message()); }
            });
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }

    // The Race's destination: a container that only takes this race's chests. Its owner tells the server
    // which chests are inside.
    internal sealed class RaceDestination : MonoBehaviour {
        private const float ReportSeconds = 3f;

        private ZNetView view;
        private Container container;
        internal long ActivityId { get; private set; }
        private string lastReport = "";
        private float reportTimer;

        private void Awake() {
            view = GetComponent<ZNetView>();
            container = GetComponentInChildren<Container>();
            if (view == null || view.GetZDO() == null) { return; }
            ActivityId = view.GetZDO().GetLong(ClansPrefabs.ActivityKey, 0L);
        }

        private void Update() {
            if (view == null || view.IsValid() == false || view.IsOwner() == false || container == null) { return; }
            Totems.SnapToGround(view, transform);
            reportTimer -= Time.deltaTime;
            if (reportTimer > 0f) { return; }
            reportTimer = ReportSeconds;
            ActivityInfo info = ActivityClient.Get(ActivityId);
            if (info == null || info.State != ActivityState.Running || container.GetInventory() == null) { return; }

            List<int> serials = new List<int>();
            foreach (ItemDrop.ItemData item in container.GetInventory().GetAllItems()) {
                if (RaceChests.IsRaceChest(item) && RaceChests.RaceOf(item) == ActivityId) { serials.Add(RaceChests.SerialOf(item)); }
            }
            serials.Sort();
            string report = string.Join(",", serials);
            if (report == lastReport) { return; }
            lastReport = report;
            long id = ActivityId;
            ClanRequests.Report(Op.ReportRaceChests, pkg => {
                pkg.Write(id);
                pkg.Write(serials.Count);
                foreach (int serial in serials) { pkg.Write(serial); }
            });
        }
    }

    internal static class Totems {
        private const string SnappedKey = ClansPrefabs.SnappedKey;

        private static readonly List<ZDO> sectorObjects = new List<ZDO>();
        private static HashSet<int> stampedPrefabs;
        private static int placerHash;

        // A persistent object for whoever is near to spawn. Only its ZDO is made here; ZNetScene instantiates it where
        // a player is near, with the stamp already in its data, so its Awake sees it. Used by the server (placers) and
        // by a placer's owner (the totem itself).
        internal static ZDO CreateObject(string prefabName, Vector3 position, Quaternion rotation) {
            int hash = prefabName.GetStableHashCode();
            ZDO zdo = ZDOMan.instance.CreateNewZDO(position, hash);
            zdo.Persistent = true;
            zdo.Type = ZDO.ObjectType.Default;
            zdo.SetPrefab(hash);
            zdo.SetRotation(rotation);
            return zdo;
        }

        internal static void Stamp(ZDO zdo, long activityId, ClansPrefabs.TotemKind kind, long clanId) {
            zdo.Set(ClansPrefabs.ActivityKey, activityId);
            zdo.Set(ClansPrefabs.KindKey, (int)kind);
            zdo.Set(ClansPrefabs.ClanKey, clanId);
        }

        internal static ClansPrefabs.TotemKind KindOf(ZDO zdo) => (ClansPrefabs.TotemKind)zdo.GetInt(ClansPrefabs.KindKey, 0);

        internal static bool IsPlacer(ZDO zdo) {
            Prepare();
            return zdo.GetPrefab() == placerHash;
        }

        // The totems and placers stamped for an activity within reach of a point, from this machine's copy of the
        // world (the server has every ZDO; a client those around it).
        internal static void FindStamped(long activityId, Vector3 near, float reach, List<ZDO> into) {
            into.Clear();
            if (ZDOMan.instance == null || activityId == 0) { return; }
            Prepare();
            int zones = Mathf.CeilToInt(reach / ZoneSystem.c_ZoneSize);
            sectorObjects.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(near), new SimulationDistance(zones, 0, classic: true), sectorObjects);
            foreach (ZDO zdo in sectorObjects) {
                if (stampedPrefabs.Contains(zdo.GetPrefab()) == false || zdo.GetLong(ClansPrefabs.ActivityKey, 0L) != activityId) { continue; }
                if (Utils.DistanceXZ(zdo.GetPosition(), near) <= reach) { into.Add(zdo); }
            }
        }

        private static void Prepare() {
            if (stampedPrefabs != null) { return; }
            placerHash = ClansPrefabs.Placer.GetStableHashCode();
            stampedPrefabs = new HashSet<int> {
                ClansPrefabs.RaceStart.GetStableHashCode(), ClansPrefabs.RaceEnd.GetStableHashCode(),
                ClansPrefabs.WorldBoss.GetStableHashCode(), placerHash,
            };
        }

        // The server placed the totem by the world generator's height; terraforming may have moved the real
        // ground. Settle it once, as soon as its owner has the ground loaded.
        internal static void SnapToGround(ZNetView view, Transform transform) {
            ZDO zdo = view.GetZDO();
            if (zdo.GetBool(SnappedKey)) { return; }
            if (ZoneSystem.instance == null || ZoneSystem.instance.IsZoneLoaded(transform.position) == false) { return; }
            if (ZoneSystem.instance.GetGroundHeight(transform.position, out float ground) == false) { return; }
            Vector3 position = transform.position;
            position.y = ground;
            transform.position = position;
            zdo.SetPosition(position);
            zdo.Set(SnappedKey, true);
        }
    }
}
