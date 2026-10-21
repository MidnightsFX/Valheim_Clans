using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Clans.Settings {
    // levels.yaml: what each clan level needs and what it allows. A clan does not level up by itself: once it
    // has the experience its members hand in the resources, and a rank allowed to upgrade the clan does so.
    public class LevelsSettings {
        // Index 0 is level 1. XpRequired is the clan's total experience needed to reach that level.
        public List<ClanLevelDef> Levels { get; set; } = new List<ClanLevelDef>();

        internal static LevelsSettings BuildDefaults() {
            int[] xp = { 0, 1000, 2500, 5000, 8500, 13000, 19000, 26000, 35000, 50000 };
            int[] members = { 10, 12, 15, 18, 20, 25, 30, 35, 40, 50 };
            int[] treasury = { 0, 250, 500, 1000, 1500, 2500, 3500, 5000, 7500, 10000 };
            float[] shareFraction = { 0.25f, 0.27f, 0.3f, 0.32f, 0.35f, 0.37f, 0.4f, 0.42f, 0.45f, 0.5f };
            float[] shareRange = { 50f, 55f, 60f, 65f, 70f, 75f, 80f, 90f, 100f, 120f };
            // Each level asks for what the biome a clan of that level is likely working through gives.
            Dictionary<string, int>[] resources = {
                new Dictionary<string, int>(),
                new Dictionary<string, int> { { "Wood", 200 }, { "Stone", 150 }, { "Resin", 50 }, { "DeerHide", 20 } },
                new Dictionary<string, int> { { "FineWood", 150 }, { "RoundLog", 100 }, { "Bronze", 30 }, { "TrollHide", 10 } },
                new Dictionary<string, int> { { "ElderBark", 100 }, { "Iron", 50 }, { "Guck", 20 }, { "WitheredBone", 20 } },
                new Dictionary<string, int> { { "Silver", 50 }, { "WolfPelt", 30 }, { "Obsidian", 50 }, { "Crystal", 20 } },
                new Dictionary<string, int> { { "BlackMetal", 50 }, { "LinenThread", 50 }, { "Tar", 30 }, { "LoxPelt", 15 } },
                new Dictionary<string, int> { { "YggdrasilWood", 100 }, { "Eitr", 30 }, { "Carapace", 40 }, { "BlackCore", 10 } },
                new Dictionary<string, int> { { "Blackwood", 100 }, { "FlametalNew", 30 }, { "AskHide", 30 }, { "CeramicPlate", 40 } },
                new Dictionary<string, int> { { "FlametalNew", 60 }, { "CharredBone", 50 }, { "MorgenSinew", 10 }, { "Eitr", 60 } },
                new Dictionary<string, int> { { "FlametalNew", 100 }, { "BlackCore", 20 }, { "MorgenHeart", 5 }, { "CeramicPlate", 80 } },
            };
            LevelsSettings settings = new LevelsSettings();
            for (int i = 0; i < xp.Length; i++) {
                settings.Levels.Add(new ClanLevelDef {
                    XpRequired = xp[i],
                    MaxMembers = members[i],
                    TreasuryCost = treasury[i],
                    Resources = resources[i],
                    XpShareFraction = shareFraction[i],
                    XpShareRange = shareRange[i],
                    // From level 5 clanmates in one party share however far apart they are.
                    XpShareAnyDistance = i >= 4,
                });
            }
            return settings;
        }

        internal static ValidationReport Validate(LevelsSettings next, LevelsSettings previous) {
            ValidationReport report = new ValidationReport();
            if (next.Levels == null || next.Levels.Count == 0) {
                return report.Error("Levels is empty; at least level 1 is needed");
            }
            next.Levels.RemoveAll(l => l == null);
            if (next.Levels.Count == 0) {
                return report.Error("Levels is empty; at least level 1 is needed");
            }
            if (next.Levels[0].XpRequired != 0) {
                report.Warn($"level 1 needs {next.Levels[0].XpRequired} experience; it should be 0, so 0 is used.");
                next.Levels[0].XpRequired = 0;
            }
            // Item names can only be checked once every mod has added its items, which is in a world.
            bool checkItems = ZNetScene.instance != null && ObjectDB.instance != null;
            for (int i = 0; i < next.Levels.Count; i++) {
                ClanLevelDef level = next.Levels[i];
                if (i > 0 && level.XpRequired <= next.Levels[i - 1].XpRequired) {
                    report.Warn($"level {i + 1} needs no more experience than level {i}; levels should climb.");
                }
                if (level.TreasuryCost < 0) { level.TreasuryCost = 0; }
                if (level.XpShareFraction < 0f || level.XpShareFraction > 1f) {
                    report.Warn($"level {i + 1} has XpShareFraction {level.XpShareFraction}; it must be 0 - 1.");
                    level.XpShareFraction = UnityEngine.Mathf.Clamp01(level.XpShareFraction);
                }
                if (level.XpShareRange < 0f) { level.XpShareRange = 0f; }
                level.Resources = CleanItems(report, $"level {i + 1} Resources", level.Resources, checkItems);
            }
            return report;
        }

        // Drops empty names and amounts below 1, and warns about names that are no item.
        internal static Dictionary<string, int> CleanItems(ValidationReport report, string where, Dictionary<string, int> items, bool checkItems) {
            Dictionary<string, int> clean = new Dictionary<string, int>();
            if (items == null) { return clean; }
            foreach (KeyValuePair<string, int> item in items) {
                string name = (item.Key ?? "").Trim();
                if (name.Length == 0 || item.Value <= 0) { continue; }
                if (checkItems && ObjectDB.instance.GetItemPrefab(name) == null) {
                    report.Warn($"{where} has '{name}', which is no item in this game (names are prefab names and case matters).");
                }
                clean[name] = item.Value;
            }
            return clean;
        }

        // Level for a total experience value, 1-based. Only used to give clans saved before levels were bought the
        // level their experience gave them then.
        internal int LevelFor(long xp) {
            int level = 1;
            for (int i = 1; i < Levels.Count; i++) {
                if (xp >= Levels[i].XpRequired) { level = i + 1; } else { break; }
            }
            return level;
        }

        internal ClanLevelDef Get(int level) {
            if (Levels.Count == 0) { return new ClanLevelDef(); }
            int index = level - 1;
            if (index < 0) { index = 0; }
            if (index >= Levels.Count) { index = Levels.Count - 1; }
            return Levels[index];
        }

        internal int MaxLevel => Levels.Count;

        // Every item any level asks for, by prefab name.
        internal IEnumerable<string> ResourceItems() {
            return Levels.Where(l => l.Resources != null).SelectMany(l => l.Resources.Keys).Distinct();
        }
    }

    public class ClanLevelDef {
        public int XpRequired { get; set; }

        [DefaultValue(10)]
        public int MaxMembers { get; set; } = 10;

        // Items the members hand in to reach this level: prefab name -> amount.
        public Dictionary<string, int> Resources { get; set; } = new Dictionary<string, int>();

        // Coins taken from the clan treasury when the clan is upgraded to this level.
        public int TreasuryCost { get; set; }

        // How clanmates in one party share skill experience while the clan is at this level: the share of each gain
        // they receive, within how many metres, and whether distance stops mattering.
        [DefaultValue(0.25f)]
        public float XpShareFraction { get; set; } = 0.25f;

        [DefaultValue(50f)]
        public float XpShareRange { get; set; } = 50f;

        public bool XpShareAnyDistance { get; set; }
    }
}
