using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Clans.Settings {
    // activities.yaml: the weekly clan activities. Lists with one entry per difficulty are in the order
    // Easy, Medium, Hard.
    public class ActivitiesSettings {
        // How much harder each difficulty is than Easy. Scales wave sizes, kill counts and rewards.
        public List<float> DifficultyMultipliers { get; set; } = new List<float>();
        public TrophyHuntSettings TrophyHunt { get; set; } = new TrophyHuntSettings();
        public RaceSettings Race { get; set; } = new RaceSettings();
        public WorldBossSettings WorldBoss { get; set; } = new WorldBossSettings();
        // Items a reward's MagicItems are picked from, by biome name; the World Boss uses its boss's biome.
        public Dictionary<string, List<string>> MagicItemPools { get; set; } = new Dictionary<string, List<string>>();

        internal static ActivitiesSettings BuildDefaults() {
            return new ActivitiesSettings {
                MagicItemPools = new Dictionary<string, List<string>> {
                    { "Meadows", new List<string> { "AxeFlint", "SpearFlint", "KnifeFlint", "Club", "Bow", "ShieldWood", "ArmorLeatherChest", "ArmorLeatherLegs", "HelmetLeather" } },
                    { "BlackForest", new List<string> { "AxeBronze", "SwordBronze", "MaceBronze", "SpearBronze", "AtgeirBronze", "BowFineWood", "ShieldBronzeBuckler", "ArmorBronzeChest", "ArmorBronzeLegs", "HelmetBronze", "ArmorTrollLeatherChest" } },
                    { "Swamp", new List<string> { "SwordIron", "MaceIron", "AxeIron", "AtgeirIron", "SpearElderbark", "Battleaxe", "BowHuntsman", "ShieldIronSquare", "ArmorIronChest", "ArmorIronLegs", "HelmetIron" } },
                    { "Mountain", new List<string> { "SwordSilver", "SpearWolfFang", "MaceSilver", "BowDraugrFang", "ShieldSilver", "ArmorWolfChest", "ArmorWolfLegs", "HelmetDrake" } },
                    { "Plains", new List<string> { "SwordBlackmetal", "AxeBlackMetal", "AtgeirBlackmetal", "KnifeBlackMetal", "MaceNeedle", "ShieldBlackmetal", "ArmorPaddedCuirass", "ArmorPaddedGreaves", "HelmetPadded" } },
                    { "Mistlands", new List<string> { "SwordMistwalker", "AxeJotunBane", "SpearCarapace", "KnifeSkollAndHati", "BowSpineSnap", "CrossbowArbalest", "StaffFireball", "ShieldCarapace", "ArmorCarapaceChest", "ArmorCarapaceLegs", "HelmetCarapace", "ArmorMageChest" } },
                    { "AshLands", new List<string> { "SwordNiedhogg", "AxeBerzerkr", "MaceEldner", "SpearSplitner", "BowAshlands", "CrossbowRipper", "StaffClusterbomb", "ArmorAshlandsMediumChest", "ArmorAshlandsMediumlegs", "HelmetAshlandsMediumHood" } },
                },
                DifficultyMultipliers = new List<float> { 1f, 2f, 4f },
                TrophyHunt = TrophyHuntSettings.BuildDefaults(),
                Race = RaceSettings.BuildDefaults(),
                WorldBoss = WorldBossSettings.BuildDefaults(),
            };
        }

        internal static ValidationReport Validate(ActivitiesSettings next, ActivitiesSettings previous) {
            ValidationReport report = new ValidationReport();
            ActivitiesSettings defaults = BuildDefaults();
            if (next.DifficultyMultipliers == null || next.DifficultyMultipliers.Count != 3) {
                report.Warn("DifficultyMultipliers needs three values (Easy, Medium, Hard); using 1, 2, 4.");
                next.DifficultyMultipliers = defaults.DifficultyMultipliers;
            }
            if (next.TrophyHunt == null) { next.TrophyHunt = defaults.TrophyHunt; report.Warn("TrophyHunt is missing; using defaults."); }
            if (next.Race == null) { next.Race = defaults.Race; report.Warn("Race is missing; using defaults."); }
            if (next.WorldBoss == null) { next.WorldBoss = defaults.WorldBoss; report.Warn("WorldBoss is missing; using defaults."); }
            if (next.MagicItemPools == null || next.MagicItemPools.Count == 0) { next.MagicItemPools = defaults.MagicItemPools; }
            next.TrophyHunt.Validate(report, defaults.TrophyHunt);
            next.Race.Validate(report, defaults.Race);
            next.WorldBoss.Validate(report, defaults.WorldBoss);
            return report;
        }

        internal float Multiplier(int difficulty) {
            return DifficultyMultipliers[Clamp(difficulty)];
        }

        internal static int Clamp(int difficulty) => difficulty < 0 ? 0 : (difficulty > 2 ? 2 : difficulty);

        // Lists with one entry per difficulty that came back short get padded with the default's entries.
        internal static List<T> PerDifficulty<T>(ValidationReport report, string name, List<T> value, List<T> fallback) {
            if (value != null && value.Count >= 3) { return value; }
            report.Warn($"{name} needs three entries (Easy, Medium, Hard); using the defaults.");
            return fallback;
        }
    }

    // What an activity pays out when it is completed, for one difficulty.
    public class ActivityReward {
        public int ClanXp { get; set; }
        // Coins added to the clan treasury.
        public int TreasuryCoins { get; set; }
        // Coins given to every participant.
        public int ParticipantCoins { get; set; }
        // Extra items given to every participant: prefab name -> amount.
        public Dictionary<string, int> ParticipantItems { get; set; } = new Dictionary<string, int>();
        // Random Epic Loot magic items given to every participant (World Boss only; needs Epic Loot).
        public int MagicItems { get; set; }
        // Relative chance of each magic item rarity: Magic, Rare, Epic, Legendary, Mythic.
        public List<int> MagicRarityWeights { get; set; } = new List<int>();
    }

    public class TrophyHuntSettings {
        [DefaultValue(true)]
        public bool Enabled { get; set; } = true;
        // Creature types to hunt per difficulty.
        public List<int> TargetCounts { get; set; } = new List<int>();
        // Kills needed of each ordinary creature type, per difficulty. Grows with clan size, see KillsPerExtraMember.
        public List<int> KillsPerTarget { get; set; } = new List<int>();
        // Extra kills needed per clan member beyond the first five.
        public float KillsPerExtraMember { get; set; }
        // Kills needed when a target is a boss.
        [DefaultValue(1)]
        public int BossKills { get; set; } = 1;
        // Chance that a target slot picks a boss the server has already defeated.
        public float BossChance { get; set; }
        // Share of targets drawn from the highest biome the server has unlocked; the rest come from lower biomes.
        public float HighestBiomeShare { get; set; }
        public List<HuntBiomePool> Biomes { get; set; } = new List<HuntBiomePool>();
        public List<ActivityReward> Rewards { get; set; } = new List<ActivityReward>();

        internal static TrophyHuntSettings BuildDefaults() {
            return new TrophyHuntSettings {
                Enabled = true,
                TargetCounts = new List<int> { 3, 6, 9 },
                KillsPerTarget = new List<int> { 5, 8, 12 },
                KillsPerExtraMember = 0.5f,
                BossKills = 1,
                BossChance = 0.1f,
                HighestBiomeShare = 0.6f,
                Biomes = new List<HuntBiomePool> {
                    new HuntBiomePool { Biome = Heightmap.Biome.Meadows, RequiredKey = "",
                        Creatures = new List<string> { "Boar", "Deer", "Neck", "Greyling" }, Bosses = new List<string> { "Eikthyr" } },
                    new HuntBiomePool { Biome = Heightmap.Biome.BlackForest, RequiredKey = "defeated_eikthyr",
                        Creatures = new List<string> { "Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman", "Troll", "Skeleton" }, Bosses = new List<string> { "gd_king" } },
                    new HuntBiomePool { Biome = Heightmap.Biome.Swamp, RequiredKey = "defeated_gdking",
                        Creatures = new List<string> { "Draugr", "Draugr_Elite", "Blob", "BlobElite", "Leech", "Wraith", "Surtling", "Abomination" }, Bosses = new List<string> { "Bonemass" } },
                    new HuntBiomePool { Biome = Heightmap.Biome.Mountain, RequiredKey = "defeated_bonemass",
                        Creatures = new List<string> { "Wolf", "Hatchling", "StoneGolem", "Fenring", "Ulv", "Fenring_Cultist" }, Bosses = new List<string> { "Dragon" } },
                    new HuntBiomePool { Biome = Heightmap.Biome.Plains, RequiredKey = "defeated_dragon",
                        Creatures = new List<string> { "Goblin", "GoblinBrute", "GoblinShaman", "Deathsquito", "Lox", "BlobTar" }, Bosses = new List<string> { "GoblinKing" } },
                    new HuntBiomePool { Biome = Heightmap.Biome.Mistlands, RequiredKey = "defeated_goblinking",
                        Creatures = new List<string> { "Seeker", "SeekerBrute", "Tick", "Gjall", "Hare" }, Bosses = new List<string> { "SeekerQueen" } },
                    new HuntBiomePool { Biome = Heightmap.Biome.AshLands, RequiredKey = "defeated_queen",
                        Creatures = new List<string> { "Charred_Melee", "Charred_Archer", "Charred_Mage", "Morgen", "Asksvin", "Volture", "FallenValkyrie", "BlobLava" }, Bosses = new List<string> { "Fader" } },
                },
                Rewards = new List<ActivityReward> {
                    new ActivityReward { ClanXp = 300, TreasuryCoins = 150, ParticipantCoins = 25 },
                    new ActivityReward { ClanXp = 600, TreasuryCoins = 300, ParticipantCoins = 50 },
                    new ActivityReward { ClanXp = 1200, TreasuryCoins = 600, ParticipantCoins = 100 },
                },
            };
        }

        internal void Validate(ValidationReport report, TrophyHuntSettings defaults) {
            TargetCounts = ActivitiesSettings.PerDifficulty(report, "TrophyHunt.TargetCounts", TargetCounts, defaults.TargetCounts);
            KillsPerTarget = ActivitiesSettings.PerDifficulty(report, "TrophyHunt.KillsPerTarget", KillsPerTarget, defaults.KillsPerTarget);
            Rewards = ActivitiesSettings.PerDifficulty(report, "TrophyHunt.Rewards", Rewards, defaults.Rewards);
            if (Biomes == null || Biomes.Count == 0) {
                report.Warn("TrophyHunt.Biomes is empty; using the defaults.");
                Biomes = defaults.Biomes;
            }
        }
    }

    public class HuntBiomePool {
        public Heightmap.Biome Biome { get; set; }
        // Global key the server must have for this biome to count as reached. Empty = always reached.
        public string RequiredKey { get; set; } = "";
        public List<string> Creatures { get; set; } = new List<string>();
        public List<string> Bosses { get; set; } = new List<string>();
    }

    public class RaceSettings {
        [DefaultValue(true)]
        public bool Enabled { get; set; } = true;
        // Chest type per difficulty: Easy uses Tiers[0], Medium Tiers[1], Hard Tiers[2].
        public List<RaceTier> Tiers { get; set; } = new List<RaceTier>();
        // Chests per member present at the start, per difficulty.
        public List<float> ChestsPerMember { get; set; } = new List<float>();
        // How far the destination is from the start, as a share of the world's diameter.
        public float DistanceFraction { get; set; }
        // Never closer than this, in metres.
        public float MinDistance { get; set; }
        // Where the start and the destination may be placed.
        public List<Heightmap.Biome> AllowedBiomes { get; set; } = new List<Heightmap.Biome>();
        [DefaultValue(90)]
        public int TimeLimitMinutes { get; set; } = 90;
        public List<ActivityReward> Rewards { get; set; } = new List<ActivityReward>();

        internal static RaceSettings BuildDefaults() {
            return new RaceSettings {
                Enabled = true,
                Tiers = new List<RaceTier> {
                    new RaceTier { Name = "Light", BasePrefab = "chest_hildir1", Weight = 100f },
                    new RaceTier { Name = "Medium", BasePrefab = "chest_hildir2", Weight = 200f },
                    new RaceTier { Name = "Heavy", BasePrefab = "chest_hildir3", Weight = 300f },
                },
                ChestsPerMember = new List<float> { 1f, 2f, 3f },
                DistanceFraction = 0.25f,
                MinDistance = 1500f,
                AllowedBiomes = new List<Heightmap.Biome> { Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Plains },
                TimeLimitMinutes = 90,
                Rewards = new List<ActivityReward> {
                    new ActivityReward { ClanXp = 400, TreasuryCoins = 200, ParticipantCoins = 30 },
                    new ActivityReward { ClanXp = 800, TreasuryCoins = 400, ParticipantCoins = 60 },
                    new ActivityReward { ClanXp = 1600, TreasuryCoins = 800, ParticipantCoins = 120 },
                },
            };
        }

        internal void Validate(ValidationReport report, RaceSettings defaults) {
            Tiers = ActivitiesSettings.PerDifficulty(report, "Race.Tiers", Tiers, defaults.Tiers);
            ChestsPerMember = ActivitiesSettings.PerDifficulty(report, "Race.ChestsPerMember", ChestsPerMember, defaults.ChestsPerMember);
            Rewards = ActivitiesSettings.PerDifficulty(report, "Race.Rewards", Rewards, defaults.Rewards);
            if (AllowedBiomes == null || AllowedBiomes.Count == 0) {
                report.Warn("Race.AllowedBiomes is empty; using the defaults.");
                AllowedBiomes = defaults.AllowedBiomes;
            }
            if (DistanceFraction <= 0f || DistanceFraction > 1f) {
                report.Warn($"Race.DistanceFraction {DistanceFraction} is outside 0-1; using {defaults.DistanceFraction}.");
                DistanceFraction = defaults.DistanceFraction;
            }
        }
    }

    public class RaceTier {
        public string Name { get; set; } = "";
        // The vanilla item the race chest is copied from.
        public string BasePrefab { get; set; } = "";
        [DefaultValue(200f)]
        public float Weight { get; set; } = 200f;
    }

    public class WorldBossSettings {
        [DefaultValue(true)]
        public bool Enabled { get; set; } = true;
        public List<WorldBossDef> Bosses { get; set; } = new List<WorldBossDef>();
        // Total waves, the boss being the last one.
        [DefaultValue(12)]
        public int Waves { get; set; } = 12;
        // Portals creatures come out of, per difficulty.
        public List<int> SpawnPoints { get; set; } = new List<int>();
        // Most creatures in one wave, per difficulty.
        public List<int> MaxCreatures { get; set; } = new List<int>();
        // Boss health compared to normal, before the participant bonus.
        [DefaultValue(10f)]
        public float BossHealthMultiplier { get; set; } = 10f;
        // Star Level System boss abilities added to the boss, per difficulty.
        public List<int> BossModifiers { get; set; } = new List<int>();
        // Extra boss health and wave size per participant beyond the first.
        public float PerParticipantScale { get; set; }
        // Boss stars, per difficulty.
        public List<int> BossStars { get; set; } = new List<int>();
        [DefaultValue(60)]
        public int TimeLimitMinutes { get; set; } = 60;
        public List<ActivityReward> Rewards { get; set; } = new List<ActivityReward>();

        internal static WorldBossSettings BuildDefaults() {
            List<int> rarity = new List<int> { 50, 30, 15, 5, 0 };
            return new WorldBossSettings {
                Enabled = true,
                Bosses = new List<WorldBossDef> {
                    new WorldBossDef { Id = "eikthyr", Prefab = "Eikthyr", FortressCreature = "Eikthyr", Biome = Heightmap.Biome.Meadows, RequiredKey = "defeated_eikthyr" },
                    new WorldBossDef { Id = "elder", Prefab = "gd_king", FortressCreature = "TheElder", Biome = Heightmap.Biome.BlackForest, RequiredKey = "defeated_gdking" },
                    new WorldBossDef { Id = "bonemass", Prefab = "Bonemass", FortressCreature = "Bonemass", Biome = Heightmap.Biome.Swamp, RequiredKey = "defeated_bonemass" },
                    new WorldBossDef { Id = "moder", Prefab = "Dragon", FortressCreature = "Moder", Biome = Heightmap.Biome.Mountain, RequiredKey = "defeated_dragon" },
                    new WorldBossDef { Id = "yagluth", Prefab = "GoblinKing", FortressCreature = "Yagluth", Biome = Heightmap.Biome.Plains, RequiredKey = "defeated_goblinking" },
                    new WorldBossDef { Id = "queen", Prefab = "SeekerQueen", FortressCreature = "TheQueen", Biome = Heightmap.Biome.Mistlands, RequiredKey = "defeated_queen", Enabled = false },
                    new WorldBossDef { Id = "fader", Prefab = "Fader", FortressCreature = "Fader", Biome = Heightmap.Biome.AshLands, RequiredKey = "defeated_fader", Enabled = false },
                },
                Waves = 12,
                SpawnPoints = new List<int> { 10, 15, 20 },
                MaxCreatures = new List<int> { 20, 30, 40 },
                BossHealthMultiplier = 10f,
                BossModifiers = new List<int> { 1, 2, 3 },
                PerParticipantScale = 0.15f,
                BossStars = new List<int> { 2, 3, 4 },
                TimeLimitMinutes = 60,
                Rewards = new List<ActivityReward> {
                    new ActivityReward { ClanXp = 1000, TreasuryCoins = 500, ParticipantCoins = 100, MagicItems = 1, MagicRarityWeights = rarity,
                        ParticipantItems = new Dictionary<string, int> { { "ForestToken", 5 } } },
                    new ActivityReward { ClanXp = 2000, TreasuryCoins = 1000, ParticipantCoins = 200, MagicItems = 2, MagicRarityWeights = new List<int> { 30, 35, 25, 10, 0 },
                        ParticipantItems = new Dictionary<string, int> { { "ForestToken", 10 }, { "IronBountyToken", 1 } } },
                    new ActivityReward { ClanXp = 4000, TreasuryCoins = 2000, ParticipantCoins = 400, MagicItems = 3, MagicRarityWeights = new List<int> { 10, 30, 35, 20, 5 },
                        ParticipantItems = new Dictionary<string, int> { { "ForestToken", 20 }, { "IronBountyToken", 2 }, { "GoldBountyToken", 1 } } },
                },
            };
        }

        internal void Validate(ValidationReport report, WorldBossSettings defaults) {
            SpawnPoints = ActivitiesSettings.PerDifficulty(report, "WorldBoss.SpawnPoints", SpawnPoints, defaults.SpawnPoints);
            MaxCreatures = ActivitiesSettings.PerDifficulty(report, "WorldBoss.MaxCreatures", MaxCreatures, defaults.MaxCreatures);
            BossModifiers = ActivitiesSettings.PerDifficulty(report, "WorldBoss.BossModifiers", BossModifiers, defaults.BossModifiers);
            BossStars = ActivitiesSettings.PerDifficulty(report, "WorldBoss.BossStars", BossStars, defaults.BossStars);
            Rewards = ActivitiesSettings.PerDifficulty(report, "WorldBoss.Rewards", Rewards, defaults.Rewards);
            if (Bosses == null || Bosses.Count == 0) {
                report.Warn("WorldBoss.Bosses is empty; using the defaults.");
                Bosses = defaults.Bosses;
            }
            if (Waves < 2) {
                report.Warn($"WorldBoss.Waves is {Waves}; at least 2 are needed (the last is the boss). Using 12.");
                Waves = 12;
            }
        }

        internal WorldBossDef Find(string id) => Bosses.FirstOrDefault(b => b.Id == id);
    }

    public class WorldBossDef {
        public string Id { get; set; } = "";
        // The boss creature's prefab.
        public string Prefab { get; set; } = "";
        // The name Valheim Fortress knows this creature by.
        public string FortressCreature { get; set; } = "";
        // Where the totem is placed and which creatures make up the waves.
        public Heightmap.Biome Biome { get; set; }
        // Only offered once the server has this global key (the boss has been beaten normally).
        public string RequiredKey { get; set; } = "";
        [DefaultValue(true)]
        public bool Enabled { get; set; } = true;
    }
}
