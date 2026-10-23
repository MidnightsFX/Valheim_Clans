using Clans.Core;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Clans.Server {
    // Clan storage. Each tab is a 5x4 inventory saved as a base64 Inventory.Save package in the clan record:
    // the shared tabs the clan has bought, and (once the upgrade is bought) one personal tab per member.
    //
    // One player edits a tab at a time: opening it takes a lease, released on close or disconnect. The client
    // sends the whole tab right after every change; the server compares it with what it had and refuses
    // changes the player's rank does not allow (taking without StorageWithdraw, putting in without
    // StorageDeposit), answering with the stored contents so the client puts them back.
    //
    // When the server takes a lease away (the player left or was removed, the clan disbanded, their rank
    // changed) it tells the client, which closes the tab at once: a tab the server no longer listens to must
    // never stay usable, or what is taken from it would still be stored here.
    //
    // Items are read with the game's own ItemData.Load, which needs no item database, and the client's bytes
    // are stored as they came: an item the server does not know is never dropped.
    internal static class StorageService {
        internal const int Width = 5;
        internal const int Height = 4;
        private const int MaxTabBytes = 65536;

        internal enum TabKind : byte { Shared = 0, Personal = 1 }

        private sealed class Lease {
            internal long Id;
            internal long ClanId;
            internal string Key;
            internal TabKind Kind;
            internal int Index;
            internal string Account;
            internal string Name;
            internal long LastUsed;
        }

        private static readonly Dictionary<string, Lease> leasesByKey = new Dictionary<string, Lease>();
        private static readonly Dictionary<long, Lease> leasesById = new Dictionary<long, Lease>();
        private static long nextLeaseId = 1;

        internal static void Register() {
            ClanNet.RegisterServer(Op.OpenStorage, OnOpen);
            ClanNet.RegisterServer(Op.StorageSnapshot, OnSnapshot);
            ClanNet.RegisterServer(Op.CloseStorage, OnClose);
            ClanNet.RegisterServer(Op.BuyStorageUpgrade, OnBuyUpgrade);
            ClanNet.RegisterServer(Op.ReadStorage, OnRead);
        }

        internal static void Reset() {
            leasesByKey.Clear();
            leasesById.Clear();
        }

        private static string Key(long clanId, TabKind kind, int index, string account) {
            return kind == TabKind.Shared ? $"{clanId}:s:{index}" : $"{clanId}:p:{account}";
        }

        // ---------------------------------------------------------------- open / edit / close

        private static void OnOpen(ServerRequest request) {
            TabKind kind = (TabKind)request.Body.ReadByte();
            int index = request.Body.ReadInt();
            if (ClansConfig.EnableStorage.Value == false) { request.Fail("$clans_err_storage_off"); return; }
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }

            string stored;
            if (kind == TabKind.Shared) {
                if (index < 0 || index >= clan.Storage.SharedTabs.Count) { request.Fail("$clans_err_tab_locked"); return; }
                ClanPermission perms = ClanRules.Permissions(clan, ClanRules.Member(clan, request.Sender.Account));
                if (perms.Has(ClanPermission.StorageDeposit) == false && perms.Has(ClanPermission.StorageWithdraw) == false) {
                    request.Fail("$clans_err_no_permission");
                    return;
                }
                stored = clan.Storage.SharedTabs[index] ?? "";
            } else {
                if (clan.Storage.PersonalUnlocked == false) { request.Fail("$clans_err_tab_locked"); return; }
                index = 0;
                clan.Storage.PersonalTabs.TryGetValue(request.Sender.Account, out stored);
                stored ??= "";
            }

            string key = Key(clan.Id, kind, index, request.Sender.Account);
            if (leasesByKey.TryGetValue(key, out Lease lease) && lease.Account != request.Sender.Account) {
                request.Fail("$clans_err_storage_in_use", lease.Name);
                return;
            }
            // One tab at a time per player: switching tabs gives up the previous one.
            foreach (Lease mine in leasesById.Values.Where(l => l.Account == request.Sender.Account && l.Key != key).ToList()) { Release(mine); }
            if (lease == null) {
                lease = new Lease {
                    Id = nextLeaseId++,
                    ClanId = clan.Id,
                    Key = key,
                    Kind = kind,
                    Index = index,
                    Account = request.Sender.Account,
                    Name = request.Sender.CharacterName,
                };
                leasesByKey[key] = lease;
                leasesById[lease.Id] = lease;
            }
            lease.LastUsed = ClanStore.Now;
            byte[] bytes = Decode(stored);
            Lease opened = lease;
            request.Ok(pkg => {
                pkg.Write(opened.Id);
                pkg.Write((byte)opened.Kind);
                pkg.Write(opened.Index);
                pkg.Write(bytes);
            });
        }

        private static void OnSnapshot(ServerRequest request) {
            long id = request.Body.ReadLong();
            byte[] bytes = request.Body.ReadByteArray();
            Apply(request.Sender, id, bytes, release: false);
        }

        private static void OnClose(ServerRequest request) {
            long id = request.Body.ReadLong();
            byte[] bytes = request.Body.ReadByteArray();
            Apply(request.Sender, id, bytes, release: true);
        }

        private static void Apply(ClanSender sender, long leaseId, byte[] bytes, bool release) {
            if (leasesById.TryGetValue(leaseId, out Lease lease) == false || lease.Account != sender.Account) {
                // A tab this server no longer holds for the player: make sure their panel is closed.
                if (release == false) { SendClosed(sender, leaseId, "$clans_storage_closed"); }
                return;
            }
            ClanRecord clan = ClanRules.Clan(lease.ClanId);
            if (clan == null || clan.Members.ContainsKey(sender.Account) == false) {
                Release(lease);
                if (release == false) { SendClosed(sender, leaseId, "$clans_storage_closed"); }
                return;
            }
            string stored = Stored(clan, lease);
            string error = Validate(clan, lease, Decode(stored), bytes);
            if (error != null) {
                Logger.LogWarning($"Refused a clan storage change from {sender}: {error}.");
                ClanNet.Send(sender, Op.StorageResync, 0, pkg => {
                    pkg.Write(lease.Id);
                    pkg.Write(Decode(stored));
                    pkg.Write("$clans_err_storage_refused");
                });
            } else {
                Store(clan, lease, Convert.ToBase64String(bytes ?? new byte[0]));
            }
            lease.LastUsed = ClanStore.Now;
            if (release) { Release(lease); }
        }

        private static string Stored(ClanRecord clan, Lease lease) {
            if (lease.Kind == TabKind.Shared) {
                return lease.Index < clan.Storage.SharedTabs.Count ? clan.Storage.SharedTabs[lease.Index] ?? "" : "";
            }
            return clan.Storage.PersonalTabs.TryGetValue(lease.Account, out string tab) ? tab ?? "" : "";
        }

        private static void Store(ClanRecord clan, Lease lease, string value) {
            if (lease.Kind == TabKind.Shared) {
                if (lease.Index < clan.Storage.SharedTabs.Count && clan.Storage.SharedTabs[lease.Index] != value) {
                    clan.Storage.SharedTabs[lease.Index] = value;
                    ApiEvents.StorageChanged(clan, lease.Index);
                }
            } else {
                clan.Storage.PersonalTabs[lease.Account] = value;
            }
            ClanStore.MarkDirtySoon();
        }

        private static void Release(Lease lease) {
            leasesById.Remove(lease.Id);
            if (leasesByKey.TryGetValue(lease.Key, out Lease held) && held.Id == lease.Id) { leasesByKey.Remove(lease.Key); }
        }

        private static void SendClosed(ClanSender to, long leaseId, string reasonToken) {
            if (to == null) { return; }
            ClanNet.Send(to, Op.StorageClosed, 0, pkg => { pkg.Write(leaseId); pkg.Write(reasonToken ?? ""); });
        }

        // Takes a player's tabs away and closes them on their screen. sharedOnly: their rank changed, which
        // only matters for shared tabs.
        internal static void CloseLeasesOf(string account, string reasonToken, bool sharedOnly = false) {
            foreach (Lease lease in leasesById.Values.Where(l => l.Account == account && (sharedOnly == false || l.Kind == TabKind.Shared)).ToList()) {
                Release(lease);
                SendClosed(Sessions.Get(account), lease.Id, reasonToken);
            }
        }

        internal static void OnAccountOffline(string account) {
            foreach (Lease lease in leasesById.Values.Where(l => l.Account == account).ToList()) { Release(lease); }
        }

        // A lease held by someone no longer online (a disconnect the server missed) is given up, and every tab
        // closes once clan storage is turned off.
        internal static void Housekeeping(long now) {
            if (leasesById.Count == 0) { return; }
            if (ClansConfig.EnableStorage.Value == false) {
                foreach (Lease lease in leasesById.Values.ToList()) {
                    Release(lease);
                    SendClosed(Sessions.Get(lease.Account), lease.Id, "$clans_err_storage_off");
                }
                return;
            }
            foreach (Lease lease in leasesById.Values.Where(l => Sessions.IsOnline(l.Account) == false).ToList()) { Release(lease); }
        }

        // ---------------------------------------------------------------- validation

        // Returns why a change is refused, or null when it is fine.
        private static string Validate(ClanRecord clan, Lease lease, byte[] before, byte[] after) {
            if (after == null) { return "no contents"; }
            if (after.Length > MaxTabBytes) { return $"{after.Length} bytes is too large"; }
            if (TryCount(after, out Dictionary<string, int> next, out HashSet<string> refused, out string parseError) == false) { return parseError; }
            if (TryCount(before, out Dictionary<string, int> previous, out _, out _) == false) {
                // What was stored no longer reads; let the player sort it out rather than locking the tab.
                return null;
            }
            // Items storage does not accept may stay where they are (stored before the list changed), but no
            // more of them may come in, personal tabs included.
            foreach (string item in refused) {
                previous.TryGetValue(item, out int was);
                if (next[item] > was) { return "an item clan storage does not accept"; }
            }
            // Personal tabs belong to their owner.
            if (lease.Kind == TabKind.Personal) { return null; }

            ClanPermission perms = ClanRules.Permissions(clan, ClanRules.Member(clan, lease.Account));
            bool canTake = perms.Has(ClanPermission.StorageWithdraw);
            bool canPut = perms.Has(ClanPermission.StorageDeposit);
            foreach (string item in previous.Keys.Union(next.Keys)) {
                previous.TryGetValue(item, out int was);
                next.TryGetValue(item, out int now);
                if (now < was && canTake == false) { return "items taken without the withdraw permission"; }
                if (now > was && canPut == false) { return "items added without the deposit permission"; }
            }
            return null;
        }

        // Stack totals per kind of item (prefab, quality, variant, world level and custom data, which is where
        // Epic Loot keeps its enchantments), plus a check that every item fits the tab's grid. refused: the kinds
        // storage does not accept (StorageSettings.Accepts).
        private static bool TryCount(byte[] bytes, out Dictionary<string, int> counts, out HashSet<string> refused, out string error) {
            counts = new Dictionary<string, int>();
            refused = new HashSet<string>();
            error = null;
            if (bytes == null || bytes.Length == 0) { return true; }
            try {
                ZPackage pkg = new ZPackage(bytes);
                Version.Item version = (Version.Item)pkg.ReadInt();
                if (version < Version.Item.Smaller) {
                    error = $"an old inventory format ({(int)version})";
                    return false;
                }
                int count = pkg.ReadUShort();
                if (count > Width * Height) {
                    error = $"{count} items in a tab of {Width * Height}";
                    return false;
                }
                for (int i = 0; i < count; i++) {
                    (int prefab, ItemDrop.ItemData item) = ItemDrop.ItemData.Load(pkg, version);
                    if (item.m_gridPos.x < 0 || item.m_gridPos.x >= Width || item.m_gridPos.y < 0 || item.m_gridPos.y >= Height) {
                        error = "an item outside the tab";
                        return false;
                    }
                    string key = ItemKey(prefab, item);
                    counts.TryGetValue(key, out int total);
                    counts[key] = total + Math.Max(0, item.m_stack);
                    if (ClansSettings.Storage.Accepts(prefab) == false) { refused.Add(key); }
                }
                return true;
            } catch (Exception e) {
                error = "contents that do not read: " + e.Message;
                return false;
            }
        }

        private static string ItemKey(int prefab, ItemDrop.ItemData item) {
            StringBuilder key = new StringBuilder();
            key.Append(prefab).Append('|').Append(item.m_quality).Append('|').Append(item.m_variant).Append('|').Append(item.m_worldLevel);
            if (item.m_customData != null && item.m_customData.Count > 0) {
                foreach (KeyValuePair<string, string> entry in item.m_customData.OrderBy(e => e.Key, StringComparer.Ordinal)) {
                    key.Append('|').Append(entry.Key).Append('=').Append(entry.Value);
                }
            }
            return key.ToString();
        }

        private static byte[] Decode(string base64) {
            if (string.IsNullOrEmpty(base64)) { return new byte[0]; }
            try {
                return Convert.FromBase64String(base64);
            } catch (FormatException) {
                return new byte[0];
            }
        }

        // ---------------------------------------------------------------- changing stored tabs (feasts, other mods)

        // One item in a stored tab: where its bytes are, how many it holds, and the item as read.
        private sealed class StoredItem {
            internal int Start;
            internal int End;
            internal int Prefab;
            internal int Stack;
            // Where its stack is written, or -1 when it holds exactly one (vanilla then writes no stack).
            internal int StackAt = -1;
            internal ItemDrop.ItemData Data;
        }

        // A shared tab being changed: what it holds, new stacks for the items it keeps (0 drops one), and items
        // added to it as vanilla writes them.
        private sealed class TabEdit {
            internal int Index;
            internal byte[] Bytes;
            internal List<StoredItem> Items;
            internal readonly Dictionary<StoredItem, int> Stacks = new Dictionary<StoredItem, int>();
            internal readonly List<byte[]> Added = new List<byte[]>();
            // For adding: which cells hold something, and whether new items may be written in this tab's format.
            internal readonly bool[] Used = new bool[Width * Height];
            internal bool Appendable;

            internal bool Changed => Stacks.Count > 0 || Added.Count > 0;
            internal int StackOf(StoredItem item) => Stacks.TryGetValue(item, out int stack) ? stack : item.Stack;
        }

        // Every shared tab nobody has open, read; a tab someone has open is left alone, because the copy on their
        // screen would undo the change. openBy: who has the first open one, else null.
        private static List<TabEdit> EditableTabs(ClanRecord clan, out string openBy) {
            openBy = null;
            List<TabEdit> tabs = new List<TabEdit>();
            for (int index = 0; index < clan.Storage.SharedTabs.Count; index++) {
                if (leasesByKey.TryGetValue(Key(clan.Id, TabKind.Shared, index, null), out Lease lease)) {
                    openBy ??= lease.Name;
                    continue;
                }
                byte[] bytes = Decode(clan.Storage.SharedTabs[index]);
                if (bytes.Length == 0) { bytes = EmptyTab(); }
                if (TryReadItems(bytes, out List<StoredItem> items) == false) { continue; }
                tabs.Add(new TabEdit { Index = index, Bytes = bytes, Items = items });
            }
            return tabs;
        }

        // Stores the tabs that changed.
        private static void Save(ClanRecord clan, List<TabEdit> tabs) {
            foreach (TabEdit tab in tabs) {
                if (tab.Changed == false) { continue; }
                clan.Storage.SharedTabs[tab.Index] = Convert.ToBase64String(Rewrite(tab));
                ApiEvents.StorageChanged(clan, tab.Index);
            }
            ClanStore.MarkDirtySoon();
        }

        internal enum TakeResult { Taken, Short, TabOpen }

        // Takes items (prefab name -> amount) from the clan's shared tabs, all of them or none. TabOpen: too few
        // without the tabs someone has open. Items are matched by prefab alone, whatever their quality, and taken
        // from the first tab and slot on; every other item keeps its bytes exactly as they were.
        internal static TakeResult TryTakeShared(ClanRecord clan, Dictionary<string, int> items, out string openBy) {
            openBy = null;
            Dictionary<int, int> wanted = new Dictionary<int, int>();
            foreach (KeyValuePair<string, int> item in items) {
                if (item.Value > 0) { wanted[item.Key.GetStableHashCode()] = item.Value; }
            }
            if (wanted.Count == 0) { return TakeResult.Taken; }

            List<TabEdit> tabs = EditableTabs(clan, out openBy);
            Dictionary<int, int> available = new Dictionary<int, int>();
            foreach (TabEdit tab in tabs) {
                foreach (StoredItem item in tab.Items) {
                    if (wanted.ContainsKey(item.Prefab)) {
                        available.TryGetValue(item.Prefab, out int have);
                        available[item.Prefab] = have + item.Stack;
                    }
                }
            }
            foreach (KeyValuePair<int, int> need in wanted) {
                available.TryGetValue(need.Key, out int have);
                if (have < need.Value) { return openBy != null ? TakeResult.TabOpen : TakeResult.Short; }
            }

            foreach (TabEdit tab in tabs) {
                foreach (StoredItem item in tab.Items) {
                    if (wanted.TryGetValue(item.Prefab, out int left) == false || left <= 0) { continue; }
                    // Only a stack of more than one can be part taken, and those always have their stack written.
                    int take = Math.Min(left, item.Stack);
                    wanted[item.Prefab] = left - take;
                    tab.Stacks[item] = item.Stack - take;
                }
            }
            Save(clan, tabs);
            return TakeResult.Taken;
        }

        // For feasts: null, or the error token to show.
        internal static string TakeFromShared(ClanRecord clan, Dictionary<string, int> items, out string openBy) {
            switch (TryTakeShared(clan, items, out openBy)) {
                case TakeResult.Short: return "$clans_err_feast_storage_short";
                case TakeResult.TabOpen: return "$clans_err_feast_storage_open";
                default: return null;
            }
        }

        // Puts items into the clan's shared tabs, all of them or none; false when they do not fit. Every item needs
        // its prefab (m_dropPrefab). A plain stackable (no custom data, variant or crafter) first tops up stored stacks
        // of the same prefab, quality and world level; the rest goes into empty cells, first tab and row first, in
        // stacks no larger than the item's own. Tabs someone has open are left alone, and new items are only written
        // into tabs saved in the game's current inventory format (they would not read back in an older one).
        internal static bool TryAddShared(ClanRecord clan, List<ItemDrop.ItemData> items, out string openBy) {
            int version = new ZPackage(EmptyTab()).ReadInt();
            List<TabEdit> tabs = EditableTabs(clan, out openBy);
            foreach (TabEdit tab in tabs) {
                tab.Appendable = new ZPackage(tab.Bytes).ReadInt() == version;
                foreach (StoredItem stored in tab.Items) {
                    Vector2i at = stored.Data.m_gridPos;
                    if (at.x >= 0 && at.x < Width && at.y >= 0 && at.y < Height) { tab.Used[at.y * Width + at.x] = true; }
                }
            }

            foreach (ItemDrop.ItemData item in items) {
                int prefab = item.m_dropPrefab.name.GetStableHashCode();
                int max = Math.Max(1, item.m_shared.m_maxStackSize);
                int left = item.m_stack;
                if (max > 1 && Plain(item)) {
                    foreach (TabEdit tab in tabs) {
                        foreach (StoredItem stored in tab.Items) {
                            if (left <= 0) { break; }
                            if (stored.Prefab != prefab || stored.StackAt < 0 || Plain(stored.Data) == false) { continue; }
                            if (stored.Data.m_quality != item.m_quality || stored.Data.m_worldLevel != item.m_worldLevel) { continue; }
                            int stack = tab.StackOf(stored);
                            int put = Math.Min(left, max - stack);
                            if (put <= 0) { continue; }
                            tab.Stacks[stored] = stack + put;
                            left -= put;
                        }
                    }
                }
                while (left > 0) {
                    if (TryFreeCell(tabs, out TabEdit tab, out int x, out int y) == false) { return false; }
                    int put = Math.Min(left, max);
                    tab.Added.Add(Record(item, put, x, y));
                    tab.Used[y * Width + x] = true;
                    left -= put;
                }
            }
            Save(clan, tabs);
            return true;
        }

        private static bool Plain(ItemDrop.ItemData item) {
            return (item.m_customData == null || item.m_customData.Count == 0) && item.m_variant == 0 && item.m_crafterID == 0;
        }

        // The first empty cell, row by row, of the first tab new items can be written into.
        private static bool TryFreeCell(List<TabEdit> tabs, out TabEdit found, out int x, out int y) {
            foreach (TabEdit tab in tabs) {
                if (tab.Appendable == false) { continue; }
                for (int cell = 0; cell < Width * Height; cell++) {
                    if (tab.Used[cell]) { continue; }
                    found = tab;
                    x = cell % Width;
                    y = cell / Width;
                    return true;
                }
            }
            found = null;
            x = -1;
            y = -1;
            return false;
        }

        // An item as vanilla saves it into an inventory, in the given cell.
        private static byte[] Record(ItemDrop.ItemData item, int stack, int x, int y) {
            ItemDrop.ItemData copy = item.Clone();
            copy.m_stack = stack;
            copy.m_gridPos = new Vector2i(x, y);
            copy.m_equipped = false;
            ZPackage pkg = new ZPackage();
            copy.Save(pkg);
            return pkg.GetArray();
        }

        // A tab with nothing in it, as vanilla writes one: the current inventory version and an item count of 0.
        private static byte[] EmptyTab() {
            ZPackage pkg = new ZPackage();
            new Inventory("", null, Width, Height).Save(pkg);
            return pkg.GetArray();
        }

        // A tab's items with their byte ranges, read the way TryCount reads them.
        private static bool TryReadItems(byte[] bytes, out List<StoredItem> items) {
            items = new List<StoredItem>();
            if (bytes == null || bytes.Length == 0) { return true; }
            try {
                ZPackage pkg = new ZPackage(bytes);
                Version.Item version = (Version.Item)pkg.ReadInt();
                if (version < Version.Item.Smaller) { return false; }
                int count = pkg.ReadUShort();
                for (int i = 0; i < count; i++) {
                    int start = pkg.GetPos();
                    (int prefab, ItemDrop.ItemData data) = ItemDrop.ItemData.Load(pkg, version);
                    // Durability (4 bytes), grid x, y and world level, then the flags: 4 says a quality follows, 8 a stack.
                    byte flags = bytes[start + 7];
                    items.Add(new StoredItem {
                        Start = start,
                        End = pkg.GetPos(),
                        Prefab = prefab,
                        Stack = Math.Max(0, data.m_stack),
                        StackAt = (flags & 8) != 0 ? start + 8 + ((flags & 4) != 0 ? 2 : 0) : -1,
                        Data = data,
                    });
                }
                return true;
            } catch (Exception e) {
                Logger.LogWarning($"Could not read a clan storage tab: {e.Message}");
                return false;
            }
        }

        // The tab as it will be stored: its inventory version as it was, each kept item's bytes (with its new stack
        // where that changed), then the added items.
        private static byte[] Rewrite(TabEdit tab) {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream)) {
                // The item count is written once the kept items are known.
                writer.Write(tab.Bytes, 0, 4);
                writer.Write((ushort)0);
                int count = 0;
                foreach (StoredItem item in tab.Items) {
                    int stack = tab.StackOf(item);
                    if (stack <= 0) { continue; }
                    byte[] record = new byte[item.End - item.Start];
                    Array.Copy(tab.Bytes, item.Start, record, 0, record.Length);
                    if (stack != item.Stack && item.StackAt >= 0) {
                        int at = item.StackAt - item.Start;
                        record[at] = (byte)(stack & 0xFF);
                        record[at + 1] = (byte)(stack >> 8);
                    }
                    writer.Write(record);
                    count++;
                }
                foreach (byte[] record in tab.Added) {
                    writer.Write(record);
                    count++;
                }
                writer.Flush();
                byte[] result = stream.ToArray();
                result[4] = (byte)(count & 0xFF);
                result[5] = (byte)(count >> 8);
                return result;
            }
        }

        // ---------------------------------------------------------------- reading tabs (other mods)

        // What the shared tabs hold. A tab someone has open is as they last sent it.
        internal static StorageContents Contents(ClanRecord clan) {
            StorageContents contents = new StorageContents { ClanId = clan.Id };
            for (int index = 0; index < clan.Storage.SharedTabs.Count; index++) {
                contents.OpenBy.Add(leasesByKey.TryGetValue(Key(clan.Id, TabKind.Shared, index, null), out Lease lease) ? lease.Name ?? "" : "");
                if (TryReadItems(Decode(clan.Storage.SharedTabs[index]), out List<StoredItem> items) == false) { continue; }
                foreach (StoredItem item in items) {
                    contents.Items.Add(new StorageContents.Item {
                        Tab = index,
                        Prefab = PrefabName(item.Prefab),
                        Stack = item.Stack,
                        Quality = item.Data.m_quality,
                        X = item.Data.m_gridPos.x,
                        Y = item.Data.m_gridPos.y,
                        Custom = item.Data.m_customData != null && item.Data.m_customData.Count > 0,
                    });
                }
            }
            return contents;
        }

        private static string PrefabName(int hash) {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(hash) : null;
            return prefab != null ? prefab.name : "";
        }

        // A client's mod asking what its clan's shared tabs hold (Clans.API.RequestStorageJson): the same members
        // who may open a shared tab may read them.
        private static void OnRead(ServerRequest request) {
            if (ClansConfig.EnableStorage.Value == false) { request.Fail("$clans_err_storage_off"); return; }
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            ClanPermission perms = ClanRules.Permissions(clan, ClanRules.Member(clan, request.Sender.Account));
            if (perms.Has(ClanPermission.StorageDeposit) == false && perms.Has(ClanPermission.StorageWithdraw) == false) {
                request.Fail("$clans_err_no_permission");
                return;
            }
            StorageContents contents = Contents(clan);
            request.Ok(contents.Write);
        }

        // ---------------------------------------------------------------- upgrades

        private static void OnBuyUpgrade(ServerRequest request) {
            TabKind kind = (TabKind)request.Body.ReadByte();
            if (ClansConfig.EnableStorage.Value == false) { request.Fail("$clans_err_storage_off"); return; }
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.UpgradeStorage) == false) { request.Fail("$clans_err_no_permission"); return; }

            StorageSettings settings = ClansSettings.Storage;
            StorageTabDef def;
            if (kind == TabKind.Shared) {
                int index = clan.Storage.SharedTabs.Count;
                if (index >= settings.SharedTabs.Count) { request.Fail("$clans_err_no_more_tabs"); return; }
                def = settings.SharedTabs[index];
            } else {
                if (clan.Storage.PersonalUnlocked) { request.Fail("$clans_err_already_bought"); return; }
                def = settings.PersonalTab;
                if (def == null) { request.Fail("$clans_err_no_more_tabs"); return; }
            }
            int level = ClanRules.Level(clan);
            if (level < def.MinClanLevel) { request.Fail("$clans_err_level_too_low", def.MinClanLevel.ToString()); return; }
            if (clan.Treasury < def.Cost) { request.Fail("$clans_err_treasury_low", clan.Treasury.ToString()); return; }

            ClanRules.AddTreasury(clan, -def.Cost);
            if (kind == TabKind.Shared) {
                clan.Storage.SharedTabs.Add("");
                ApiEvents.StorageChanged(clan, clan.Storage.SharedTabs.Count - 1);
                ClanRules.Log(clan, "$clans_log_tab_bought", request.Sender.CharacterName, clan.Storage.SharedTabs.Count.ToString(), def.Cost.ToString());
            } else {
                clan.Storage.PersonalUnlocked = true;
                ClanRules.Log(clan, "$clans_log_personal_bought", request.Sender.CharacterName, def.Cost.ToString());
            }
            Broadcaster.MarkDetail(clan.Id);
            ClanStore.MarkDirtySoon();
            request.Ok();
        }

        // ---------------------------------------------------------------- membership changes

        // A member leaving or kicked gets their personal tab back through the mailbox.
        internal static void OnMemberRemoved(ClanRecord clan, string account) {
            CloseLeasesOf(account, "$clans_storage_closed_left");
            if (clan.Storage.PersonalTabs.TryGetValue(account, out string tab) == false) { return; }
            clan.Storage.PersonalTabs.Remove(account);
            if (string.IsNullOrEmpty(tab) == false) {
                MailboxService.Add(account, new Delivery { Reason = "$clans_mail_personal_tab", Inventories = { tab } });
            }
            ClanStore.MarkDirtySoon();
        }

        // Shared tabs go to the leader (one delivery per tab, so none is too large to send), personal tabs to
        // their owners.
        internal static void OnClanDisbanded(ClanRecord clan, string leaderAccount) {
            foreach (Lease lease in leasesById.Values.Where(l => l.ClanId == clan.Id).ToList()) {
                Release(lease);
                SendClosed(Sessions.Get(lease.Account), lease.Id, "$clans_storage_closed_left");
            }
            foreach (KeyValuePair<string, string> personal in clan.Storage.PersonalTabs) {
                if (string.IsNullOrEmpty(personal.Value) == false) {
                    MailboxService.Add(personal.Key, new Delivery { Reason = "$clans_mail_personal_tab", Inventories = { personal.Value } });
                }
            }
            if (leaderAccount != null) {
                foreach (string tab in clan.Storage.SharedTabs) {
                    if (string.IsNullOrEmpty(tab) == false) {
                        MailboxService.Add(leaderAccount, new Delivery { Reason = "$clans_mail_shared_storage", Inventories = { tab } });
                    }
                }
            }
            clan.Storage.PersonalTabs.Clear();
            clan.Storage.SharedTabs.Clear();
        }
    }
}
