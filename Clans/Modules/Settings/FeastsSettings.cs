using Clans.Gameplay;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Clans.Settings {
    // feasts.yaml: the guild feasts clan members can set out. Each is a copy of a vanilla biome feast giving the health,
    // stamina and eitr set here, unlocked by clan level and paid for by the member who sets it out (or the clan).
    public class FeastsSettings {
        // How long the food lasts once eaten.
        [DefaultValue(30)]
        public int FoodMinutes { get; set; } = 30;

        // How long a feast stands before it is cleared away, eaten up or not.
        [DefaultValue(30)]
        public int LifetimeMinutes { get; set; } = 30;

        public List<GuildFeastDef> Feasts { get; set; } = new List<GuildFeastDef>();

        internal static FeastsSettings BuildDefaults() {
            return new FeastsSettings {
                Feasts = new List<GuildFeastDef> {
                    // Health and stamina are the vanilla feast's plus 5. Eitr is the vanilla feast's plus 5 from the
                    // Mistlands on; the feasts before it give some too, climbing toward the Mistlands' amount.
                    Feast("Meadows", 1, 40, 40, 5, 500, ("CookedMeat", 10), ("CookedDeerMeat", 10), ("Carrot", 20)),
                    Feast("BlackForest", 2, 40, 40, 10, 750, ("CookedDeerMeat", 10), ("CookedBjornMeat", 10), ("Carrot", 30)),
                    Feast("Swamp", 3, 40, 40, 15, 1000, ("CookedMeat", 15), ("NeckTailGrilled", 15), ("Turnip", 30)),
                    Feast("Ocean", 3, 50, 50, 20, 1000, ("SerpentMeatCooked", 5), ("FishCooked", 15), ("Turnip", 30)),
                    Feast("Mountain", 4, 50, 50, 25, 1500, ("CookedWolfMeat", 15), ("Onion", 30), ("Turnip", 20)),
                    Feast("Plains", 5, 60, 60, 30, 2000, ("CookedLoxMeat", 10), ("CookedChickenMeat", 10), ("Barley", 40), ("Onion", 20)),
                    Feast("Mistlands", 6, 70, 70, 38, 3000, ("CookedHareMeat", 10), ("CookedBugMeat", 10), ("MushroomJotunPuffs", 30), ("MushroomMagecap", 20)),
                    Feast("Ashlands", 8, 80, 80, 43, 4000, ("CookedAsksvinMeat", 15), ("CookedVoltureMeat", 10), ("MushroomJotunPuffs", 30), ("MushroomMagecap", 30)),
                    Feast("DeepNorth", 10, 90, 90, 48, 5000, ("CookedMooseMeat", 15), ("CookedSealBlubber", 10), ("Kale", 30), ("Oat", 30)),
                },
            };
        }

        // Cooked meats and farmed crops of its biome (the Ashlands has no crops of its own, so it takes the Mistlands'
        // mushrooms), and coins on top.
        private static GuildFeastDef Feast(string biome, int level, float health, float stamina, float eitr, int coins, params (string Item, int Amount)[] food) {
            Dictionary<string, int> cost = new Dictionary<string, int>();
            foreach ((string item, int amount) in food) { cost[item] = amount; }
            cost["Coins"] = coins;
            return new GuildFeastDef {
                Biome = biome,
                MinClanLevel = level,
                Health = health,
                Stamina = stamina,
                Eitr = eitr,
                Cost = cost,
            };
        }

        internal static ValidationReport Validate(FeastsSettings next, FeastsSettings previous) {
            ValidationReport report = new ValidationReport();
            if (next.FoodMinutes < 1) { report.Warn("FoodMinutes must be at least 1; using 1."); next.FoodMinutes = 1; }
            if (next.LifetimeMinutes < 1) { report.Warn("LifetimeMinutes must be at least 1; using 1."); next.LifetimeMinutes = 1; }
            if (next.Feasts == null) { next.Feasts = new List<GuildFeastDef>(); }
            bool checkItems = ZNetScene.instance != null && ObjectDB.instance != null;
            HashSet<string> seen = new HashSet<string>();
            List<GuildFeastDef> kept = new List<GuildFeastDef>();
            foreach (GuildFeastDef feast in next.Feasts) {
                if (feast == null) { continue; }
                ClansPrefabs.GuildFeastVariant variant = ClansPrefabs.GuildFeastOf(feast.Biome);
                if (variant == null) {
                    report.Warn($"Feasts has Biome '{feast.Biome}', which is none of {string.Join(", ", ClansPrefabs.GuildFeasts.Select(f => f.Biome))}; it is left out.");
                    continue;
                }
                if (seen.Add(variant.Biome) == false) {
                    report.Warn($"Feasts lists {variant.Biome} twice; only the first is used.");
                    continue;
                }
                feast.Biome = variant.Biome;
                if (feast.MinClanLevel < 1) { feast.MinClanLevel = 1; }
                if (feast.Health < 0f) { feast.Health = 0f; }
                if (feast.Stamina < 0f) { feast.Stamina = 0f; }
                if (feast.Eitr < 0f) { feast.Eitr = 0f; }
                if (feast.GivesFood == false) {
                    report.Warn($"the {variant.Biome} feast gives no health, stamina or eitr; it gives what the vanilla feast does.");
                }
                feast.Cost = LevelsSettings.CleanItems(report, $"the {variant.Biome} feast's Cost", feast.Cost, checkItems);
                kept.Add(feast);
            }
            next.Feasts = kept;
            return report;
        }

        internal GuildFeastDef Find(string biome) {
            return Feasts.FirstOrDefault(f => string.Equals(f.Biome, biome, System.StringComparison.OrdinalIgnoreCase));
        }
    }

    public class GuildFeastDef {
        // Which vanilla feast it copies: Meadows, BlackForest, Swamp, Ocean, Mountain, Plains, Mistlands, Ashlands
        // or DeepNorth.
        public string Biome { get; set; } = "";

        [DefaultValue(1)]
        public int MinClanLevel { get; set; } = 1;

        // What its food gives while it lasts.
        public float Health { get; set; }
        public float Stamina { get; set; }
        public float Eitr { get; set; }

        // Items taken from the member who sets it out: prefab name -> amount.
        public Dictionary<string, int> Cost { get; set; } = new Dictionary<string, int>();

        // False for an entry with none of the three set (a file from before they existed): the vanilla feast's food.
        internal bool GivesFood => Health > 0f || Stamina > 0f || Eitr > 0f;
    }
}
