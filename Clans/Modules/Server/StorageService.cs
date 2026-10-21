using Clans.Core;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
                if (lease.Index < clan.Storage.SharedTabs.Count) { clan.Storage.SharedTabs[lease.Index] = value; }
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

        // ---------------------------------------------------------------- the clan paying with stored items

        // One item in a stored tab: where its bytes are and how many it holds.
        private sealed class StoredItem {
            internal int Start;
            internal int End;
            internal int Prefab;
            internal int Stack;
            // Where its stack is written, or -1 when it holds exactly one (vanilla then writes no stack).
            internal int StackAt = -1;
        }

        // Takes items (prefab name -> amount) from the clan's shared tabs, all of them or none, and answers null or
        // an error token. A tab someone has open is left alone: the copy on their screen would put the items back.
        // Items are matched by prefab alone, whatever their quality, and taken from the first tab and slot on; every
        // other item keeps its bytes exactly as they were.
        internal static string TakeFromShared(ClanRecord clan, Dictionary<string, int> items, out string openBy) {
            openBy = null;
            Dictionary<int, int> wanted = new Dictionary<int, int>();
            foreach (KeyValuePair<string, int> item in items) {
                if (item.Value > 0) { wanted[item.Key.GetStableHashCode()] = item.Value; }
            }
            if (wanted.Count == 0) { return null; }

            Dictionary<int, byte[]> tabs = new Dictionary<int, byte[]>();
            Dictionary<int, List<StoredItem>> contents = new Dictionary<int, List<StoredItem>>();
            Dictionary<int, int> available = new Dictionary<int, int>();
            for (int index = 0; index < clan.Storage.SharedTabs.Count; index++) {
                if (leasesByKey.TryGetValue(Key(clan.Id, TabKind.Shared, index, null), out Lease lease)) {
                    openBy ??= lease.Name;
                    continue;
                }
                byte[] bytes = Decode(clan.Storage.SharedTabs[index]);
                if (TryReadItems(bytes, out List<StoredItem> stored) == false) { continue; }
                tabs[index] = bytes;
                contents[index] = stored;
                foreach (StoredItem item in stored) {
                    if (wanted.ContainsKey(item.Prefab)) {
                        available.TryGetValue(item.Prefab, out int have);
                        available[item.Prefab] = have + item.Stack;
                    }
                }
            }
            foreach (KeyValuePair<int, int> need in wanted) {
                available.TryGetValue(need.Key, out int have);
                if (have < need.Value) { return openBy != null ? "$clans_err_feast_storage_open" : "$clans_err_feast_storage_short"; }
            }

            foreach (int index in tabs.Keys.OrderBy(i => i)) {
                List<StoredItem> stored = contents[index];
                if (stored.Any(item => wanted.TryGetValue(item.Prefab, out int left) && left > 0) == false) { continue; }
                clan.Storage.SharedTabs[index] = Convert.ToBase64String(Without(tabs[index], stored, wanted));
            }
            ClanStore.MarkDirtySoon();
            return null;
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
                    });
                }
                return true;
            } catch (Exception e) {
                Logger.LogWarning($"Could not read a clan storage tab to pay for a feast: {e.Message}");
                return false;
            }
        }

        // The tab with what is wanted taken out (wanted is counted down): emptied stacks dropped, the rest copied.
        private static byte[] Without(byte[] bytes, List<StoredItem> items, Dictionary<int, int> wanted) {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream)) {
                // The inventory version, as it was, and the item count, written once the kept items are known.
                writer.Write(bytes, 0, 4);
                writer.Write((ushort)0);
                int kept = 0;
                foreach (StoredItem item in items) {
                    wanted.TryGetValue(item.Prefab, out int left);
                    int take = Math.Min(left, item.Stack);
                    if (take > 0) { wanted[item.Prefab] = left - take; }
                    if (take >= item.Stack) { continue; }
                    byte[] record = new byte[item.End - item.Start];
                    Array.Copy(bytes, item.Start, record, 0, record.Length);
                    if (take > 0) {
                        // Only a stack of more than one can be part taken, and those always have their stack written.
                        int at = item.StackAt - item.Start;
                        ushort stack = (ushort)(item.Stack - take);
                        record[at] = (byte)(stack & 0xFF);
                        record[at + 1] = (byte)(stack >> 8);
                    }
                    writer.Write(record);
                    kept++;
                }
                writer.Flush();
                byte[] result = stream.ToArray();
                result[4] = (byte)(kept & 0xFF);
                result[5] = (byte)(kept >> 8);
                return result;
            }
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
