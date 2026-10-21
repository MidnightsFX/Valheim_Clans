using Clans.Settings;
using System;

#pragma warning disable IDE0130
namespace Clans {
#pragma warning restore IDE0130

    internal static partial class YamlConfigManager {
        internal static YamlConfigFile<RanksSettings> RanksFile;
        internal static YamlConfigFile<LevelsSettings> LevelsFile;
        internal static YamlConfigFile<EmblemSettings> EmblemsFile;
        internal static YamlConfigFile<StorageSettings> StorageFile;
        internal static YamlConfigFile<ActivitiesSettings> ActivitiesFile;
        internal static YamlConfigFile<FeastsSettings> FeastsFile;

        // Called from Init().
        private static void RegisterConfigFiles() {
            RanksFile = Register(new YamlConfigFile<RanksSettings>("ranks.yaml") {
                Header = RanksHeader,
                Defaults = RanksSettings.BuildDefaults,
                Apply = parsed => ClansSettings.Apply(parsed),
                Validate = RanksSettings.Validate,
                AllowAdminEdit = true,
            });
            LevelsFile = Register(new YamlConfigFile<LevelsSettings>("levels.yaml") {
                Header = LevelsHeader,
                Defaults = LevelsSettings.BuildDefaults,
                Apply = parsed => ClansSettings.Apply(parsed),
                Validate = LevelsSettings.Validate,
                AllowAdminEdit = true,
            });
            EmblemsFile = Register(new YamlConfigFile<EmblemSettings>("emblems.yaml") {
                Header = EmblemsHeader,
                Defaults = EmblemSettings.BuildDefaults,
                Apply = parsed => ClansSettings.Apply(parsed),
                Validate = EmblemSettings.Validate,
                AllowAdminEdit = true,
            });
            StorageFile = Register(new YamlConfigFile<StorageSettings>("storage.yaml") {
                Header = StorageHeader,
                Defaults = StorageSettings.BuildDefaults,
                Apply = parsed => ClansSettings.Apply(parsed),
                Validate = StorageSettings.Validate,
                AllowAdminEdit = true,
            });
            ActivitiesFile = Register(new YamlConfigFile<ActivitiesSettings>("activities.yaml") {
                Header = ActivitiesHeader,
                Defaults = ActivitiesSettings.BuildDefaults,
                Apply = parsed => ClansSettings.Apply(parsed),
                Validate = ActivitiesSettings.Validate,
                AllowAdminEdit = true,
            });
            FeastsFile = Register(new YamlConfigFile<FeastsSettings>("feasts.yaml") {
                Header = FeastsHeader,
                Defaults = FeastsSettings.BuildDefaults,
                Apply = parsed => ClansSettings.Apply(parsed),
                Validate = FeastsSettings.Validate,
                AllowAdminEdit = true,
            });
        }

        private const string RanksHeader = @"#################################################
# Clans - Ranks
#
# The ranks a newly founded clan starts with. Each clan keeps its own copy, which its leader can
# rename, reorder, extend or trim, so editing this file only changes clans founded afterwards.
#
#   MaxRanks       int    Most ranks a clan may have (leader and entry rank included). 2 - 16.
#   DefaultRanks   list   Top to bottom. The first rank is the leader rank and always has every
#                         permission. The last is the rank new members join at.
#     Name         string Shown to players. A $token is translated; anything else is shown as typed.
#     Permissions  list   Any of: ManageApplications, Invite, Kick, Promote, Demote, EditInfo,
#                         EditEmblem, ManageWar, StartActivities, UpgradeStorage, StorageDeposit,
#                         StorageWithdraw, TreasuryDonate, TreasuryWithdraw, EditRanks, UpgradeClan,
#                         ClanFeasts
#
# Nobody can kick, promote or demote a member of their own rank or higher.
#################################################";

        private const string LevelsHeader = @"#################################################
# Clans - Levels
#
# Clans earn experience from activities and wars. Each entry is one level, starting at level 1.
# A clan does not level up by itself: once it has the experience, its members hand in the
# resources (from the Level tab of the Clans window), and a rank with the UpgradeClan permission
# upgrades it, paying TreasuryCost from the clan treasury.
#
#   XpRequired          int    Total clan experience needed to reach this level. Level 1 must be 0.
#   MaxMembers          int    Most members a clan of this level can have.
#   Resources           list   prefab: amount. Items the members hand in to reach this level
#                              (prefab names as the spawn command takes them; case matters).
#   TreasuryCost        int    Coins taken from the clan treasury when upgrading to this level.
#   XpShareFraction     0-1    Share of each skill gain clanmates in the same party receive.
#   XpShareRange        metres Within which clanmates in the same party share experience.
#   XpShareAnyDistance  bool   Clanmates in the same party share at any distance.
#
# Party members outside the clan share by the [Experience Sharing] settings in the .cfg; its
# Clanmates Any Distance setting turns XpShareAnyDistance off for every level.
# Storage tabs have a minimum clan level (storage.yaml), and so do guild feasts (feasts.yaml).
#################################################";

        private const string EmblemsHeader = @"#################################################
# Clans - Emblems
#
# A clan emblem is a background with a symbol on top, each in a colour from the palette.
#
#   Backgrounds / Symbols   list of parts
#     Id       string  The part's identity, stored in each clan's emblem. Renaming one resets the
#                      emblems that use it to the default.
#     Sprite   string  bundle:<name>   a white image shipped with this mod
#                      vanilla:<name>  an image already in the game, by sprite name
#                      item:<prefab>   an item's inventory icon
#     Tint     bool    Colour the image with the chosen colour. Turn off for coloured images.
#   Palette   list     #RRGGBB colours offered in the emblem editor.
#   Default            The emblem a new clan starts with: BackId, BackColor, FrontId, FrontColor.
#################################################";

        private const string StorageHeader = @"#################################################
# Clans - Storage
#
# Clan storage tabs hold 20 slots each and are bought in order from the clan treasury by ranks
# with the UpgradeStorage permission.
#
#   SharedTabs     list   One entry per tab, in purchase order.
#     Cost         int    Coins taken from the clan treasury.
#     MinClanLevel int    Clan level needed before the tab can be bought.
#   PersonalTab           The upgrade that gives every member a private tab. Cost, MinClanLevel.
#
# Removing a tab from this list does not take it from clans that already bought it.
#
# Which items may be put into clan storage (shared and personal tabs). Storage goes wherever its
# clan goes, so anything allowed in can be carried across the world, past every portal rule.
# Taking items out is never limited, and race chests are never allowed in.
#
#   RestrictItems      bool   Only the items below may be put in. Off: any item may.
#   AllowUpgradeItems  bool   Every item a clan level asks for (Resources in levels.yaml) may be
#                             put in, so members can gather them in storage before handing them in.
#   AllowFeastItems    bool   Every item a guild feast costs (Cost in feasts.yaml, coins aside) may
#                             be put in, so the clan can pay for feasts from its storage.
#   AllowedItems       list   More items that may be put in, by prefab name (as the spawn
#                             command takes them; case matters), e.g. Wood, Stone, Resin.
#
# Turning clan storage off entirely, so clans only keep coins in their treasury, is the
# [Features] Clan Storage setting in the .cfg.
#################################################";

        private const string ActivitiesHeader = @"#################################################
# Clans - Activities
#
# Weekly clan activities. Each clan can run each activity once per week (see the Weekly Reset
# section of the .cfg). Lists marked 'per difficulty' have three entries: Easy, Medium, Hard.
#
#   DifficultyMultipliers   per difficulty   How much harder Medium and Hard are than Easy.
#
#   Rewards (per difficulty, for every activity):
#     ClanXp              Clan experience.
#     TreasuryCoins       Coins added to the clan treasury.
#     ParticipantCoins    Coins for every participant.
#     ParticipantItems    prefab: amount, for every participant.
#     MagicItems          Random Epic Loot items for every participant (World Boss only).
#     MagicRarityWeights  Relative chance of Magic, Rare, Epic, Legendary, Mythic.
#
#   TrophyHunt
#     TargetCounts        per difficulty   Creature types to hunt.
#     KillsPerTarget      per difficulty   Kills of each type needed.
#     KillsPerExtraMember float            Extra kills per clan member beyond five.
#     BossKills           int              Kills needed when a target is a boss.
#     BossChance          0-1              Chance a target is a boss the server has beaten.
#     HighestBiomeShare   0-1              Share of targets from the highest biome reached.
#     Biomes              list             Biome, RequiredKey (global key), Creatures, Bosses.
#
#   Race
#     Tiers               per difficulty   Name, BasePrefab (copied item), Weight.
#     ChestsPerMember     per difficulty   Chests per member present at the start.
#     DistanceFraction    0-1              Start to destination, as a share of the world's width.
#     MinDistance         metres           Never closer than this.
#     AllowedBiomes       list             Where the start and destination may be placed.
#     TimeLimitMinutes    int
#
#   WorldBoss (needs Star Level System, Epic Loot and Valheim Fortress)
#     Bosses              list    Id, Prefab, FortressCreature (Valheim Fortress' name for it),
#                                 Biome, RequiredKey (only offered once beaten), Enabled.
#     Waves               int     Total waves; the last is the boss.
#     SpawnPoints         per difficulty   Portals around the totem.
#     MaxCreatures        per difficulty   Most creatures in one wave.
#     BossHealthMultiplier float  Boss health compared to normal.
#     BossModifiers       per difficulty   Star Level System boss abilities.
#     BossStars           per difficulty
#     PerParticipantScale float   Extra boss health and wave size per participant beyond one.
#     TimeLimitMinutes    int
#################################################";

        private const string FeastsHeader = @"#################################################
# Clans - Guild Feasts
#
# Clan members can set out a guild feast from the Level tab of the Clans window. It appears right
# in front of them, anyone may eat from it (clan or not), and it is cleared away after a while.
# Each is a copy of a vanilla biome feast, named Guild Feast of the <biome>, whose food is stronger
# and lasts a set time. The member who sets it out pays its cost from their own inventory.
#
#   FoodMinutes        int    How long the food lasts once eaten.
#   LifetimeMinutes    int    How long a feast stands before it is cleared away. It never runs
#                             out before then: anyone can eat from it as often as the game lets them.
#   Feasts             list   One entry per feast offered:
#     Biome            string Meadows, BlackForest, Swamp, Ocean, Mountain, Plains, Mistlands,
#                             Ashlands or DeepNorth (the vanilla feast it copies).
#     MinClanLevel     int    Clan level needed before members can set it out.
#     Health           number Health its food gives.
#     Stamina          number Stamina its food gives.
#     Eitr             number Eitr its food gives. With none of the three set, the food is the
#                             vanilla feast's.
#     Cost             list   prefab: amount, taken from the member who sets it out. A rank with
#                             the ClanFeasts permission can have the clan pay instead: Coins from
#                             the clan treasury, everything else from the clan's shared storage
#                             tabs (storage.yaml's AllowFeastItems lets those items be stored).
#
# The .cfg setting [Features] Guild Feasts turns them off entirely.
#################################################";
    }
}

namespace Clans.Settings {
    // The live values of every yaml file. Always non-null: the defaults are in place until a file loads.
    internal static class ClansSettings {
        internal static RanksSettings Ranks = RanksSettings.BuildDefaults();
        internal static LevelsSettings Levels = LevelsSettings.BuildDefaults();
        internal static EmblemSettings Emblems = EmblemSettings.BuildDefaults();
        internal static StorageSettings Storage = StorageSettings.BuildDefaults();
        internal static ActivitiesSettings Activities = ActivitiesSettings.BuildDefaults();
        internal static FeastsSettings Feasts = FeastsSettings.BuildDefaults();

        // Raised on the main thread after any of them changes, for UI and caches built from them.
        internal static event Action Changed;

        internal static void Apply(RanksSettings value) { Ranks = value; Changed?.Invoke(); }
        // Storage accepts what the levels and feasts ask for, so its list is rebuilt with them.
        internal static void Apply(LevelsSettings value) { Levels = value; Storage.ForgetAccepted(); Changed?.Invoke(); }
        internal static void Apply(EmblemSettings value) { Emblems = value; Changed?.Invoke(); }
        internal static void Apply(StorageSettings value) { Storage = value; Changed?.Invoke(); }
        internal static void Apply(ActivitiesSettings value) { Activities = value; Changed?.Invoke(); }
        internal static void Apply(FeastsSettings value) { Feasts = value; Storage.ForgetAccepted(); Changed?.Invoke(); }
    }
}
