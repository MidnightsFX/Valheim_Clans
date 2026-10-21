using Clans.Gameplay;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Clans.Settings {
    // storage.yaml: the clan storage upgrades. Each shared tab holds 20 slots and is bought in order from
    // the clan treasury; the personal tab upgrade gives every member a private tab of their own.
    //
    // It also says which items may be put into clan storage. A tab follows its clan everywhere, so without a
    // limit it carries anything from one end of the world to the other, past every portal rule. Taking items
    // out is never limited by this, so items stored before the list changed can always come back out.
    public class StorageSettings {
        public List<StorageTabDef> SharedTabs { get; set; } = new List<StorageTabDef>();
        public StorageTabDef PersonalTab { get; set; } = new StorageTabDef();
        public bool RestrictItems { get; set; } = true;
        public bool AllowUpgradeItems { get; set; } = true;
        [DefaultValue(true)]
        public bool AllowFeastItems { get; set; } = true;
        public List<string> AllowedItems { get; set; } = new List<string>(DefaultAllowedItems);

        // Common building materials, all of which go through portals anyway, until the upgrades cost items
        // (UpgradeItems). Also what a file written before AllowedItems existed gets.
        private static readonly string[] DefaultAllowedItems = {
            "Wood", "FineWood", "RoundLog", "Stone", "Flint", "Resin", "LeatherScraps", "DeerHide",
        };

        // Built on first use; a reload brings a new instance, and a change to levels.yaml or feasts.yaml clears it.
        private HashSet<int> allowedHashes;

        internal static StorageSettings BuildDefaults() {
            return new StorageSettings {
                SharedTabs = new List<StorageTabDef> {
                    new StorageTabDef { Cost = 500, MinClanLevel = 1 },
                    new StorageTabDef { Cost = 1500, MinClanLevel = 2 },
                    new StorageTabDef { Cost = 3000, MinClanLevel = 4 },
                    new StorageTabDef { Cost = 6000, MinClanLevel = 6 },
                    new StorageTabDef { Cost = 10000, MinClanLevel = 8 },
                },
                PersonalTab = new StorageTabDef { Cost = 2500, MinClanLevel = 3 },
            };
        }

        internal static ValidationReport Validate(StorageSettings next, StorageSettings previous) {
            ValidationReport report = new ValidationReport();
            if (next.SharedTabs == null) { next.SharedTabs = new List<StorageTabDef>(); }
            if (next.PersonalTab == null) {
                report.Warn("PersonalTab is missing; the personal tab cannot be bought.");
            }
            if (previous != null && previous.SharedTabs != null && next.SharedTabs.Count < previous.SharedTabs.Count) {
                report.Warn("SharedTabs got shorter. Clans that already bought the removed tabs keep them and their contents.");
            }
            if (next.AllowedItems == null) { next.AllowedItems = new List<string>(); }
            next.AllowedItems.RemoveAll(string.IsNullOrWhiteSpace);
            for (int i = 0; i < next.AllowedItems.Count; i++) { next.AllowedItems[i] = next.AllowedItems[i].Trim(); }
            // Item names can only be checked once every mod has added its items, which is in a world.
            if (ZNetScene.instance != null && ObjectDB.instance != null) {
                foreach (string name in next.AllowedItems) {
                    if (ObjectDB.instance.GetItemPrefab(name) == null) {
                        report.Warn($"AllowedItems has '{name}', which is no item in this game (names are prefab names and case matters).");
                    }
                }
            }
            return report;
        }

        // Prefab names of the items the clan upgrades cost: the resources members hand in for a clan level
        // (levels.yaml). Storage tabs themselves cost treasury coins only.
        internal IEnumerable<string> UpgradeItems() {
            return ClansSettings.Levels.ResourceItems();
        }

        // Prefab names of the items guild feasts cost (feasts.yaml), so members can gather them in storage and the
        // clan can pay for feasts from there. Coins are left out: a clan pays those from its treasury.
        internal IEnumerable<string> FeastItems() {
            return ClansSettings.Feasts.Feasts.Where(f => f.Cost != null).SelectMany(f => f.Cost.Keys).Where(name => name != "Coins").Distinct();
        }

        // The levels or feasts changed: the list of accepted items is rebuilt on next use.
        internal void ForgetAccepted() => allowedHashes = null;

        // Prefab names of the items that may be put in while RestrictItems is on.
        internal IEnumerable<string> AcceptedItems() {
            IEnumerable<string> names = AllowedItems ?? Enumerable.Empty<string>();
            if (AllowUpgradeItems) { names = names.Concat(UpgradeItems()); }
            if (AllowFeastItems) { names = names.Concat(FeastItems()); }
            return names.Distinct();
        }

        // Whether an item may be put into clan storage, by the stable hash of its prefab name (which is what the
        // server reads from a tab's contents). Race chests never may: they are only allowed to travel by hand,
        // cart or ship.
        internal bool Accepts(int prefabHash) {
            for (int tier = 0; tier < ClansPrefabs.RaceTiers; tier++) {
                if (prefabHash == ClansPrefabs.RaceChest(tier).GetStableHashCode()) { return false; }
            }
            if (RestrictItems == false) { return true; }
            allowedHashes ??= new HashSet<int>(AcceptedItems().Select(name => name.GetStableHashCode()));
            return allowedHashes.Contains(prefabHash);
        }

        internal bool Accepts(ItemDrop.ItemData item) {
            if (item == null) { return false; }
            return Accepts(item.m_dropPrefab != null ? item.m_dropPrefab.name.GetStableHashCode() : 0);
        }
    }

    public class StorageTabDef {
        // Coins taken from the clan treasury.
        public int Cost { get; set; }
        public int MinClanLevel { get; set; }
    }
}
