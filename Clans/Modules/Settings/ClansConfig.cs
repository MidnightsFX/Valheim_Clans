using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans.Settings {
    // The BepInEx .cfg entries: single values an admin is likely to tune. Structured data (ranks, levels,
    // emblems, storage upgrades, activities) lives in the yaml files in ClansYamlConfigs.cs.
    //
    // Everything bound through ValConfig.BindServerConfig is IsAdminOnly, so Jotunn pushes the server's
    // value to every client. The "Client" section is bound directly and stays per machine.
    internal static class ClansConfig {
        // Features
        internal static ConfigEntry<bool> EnableParties;
        internal static ConfigEntry<bool> EnableWars;
        internal static ConfigEntry<bool> EnableStorage;
        internal static ConfigEntry<bool> EnableActivities;
        internal static ConfigEntry<bool> EnableBanners;
        internal static ConfigEntry<bool> EnableFeasts;

        // Clans
        internal static ConfigEntry<int> CreationFee;
        internal static ConfigEntry<int> NameMinLength;
        internal static ConfigEntry<int> NameMaxLength;
        internal static ConfigEntry<int> MaxPendingApplications;
        internal static ConfigEntry<int> ApplicationExpiryDays;
        internal static ConfigEntry<int> LogEntriesKept;
        internal static ConfigEntry<bool> TreasuryWithdrawEnabled;
        internal static ConfigEntry<bool> DisbandRefund;

        // Parties
        internal static ConfigEntry<int> MaxPartySize;
        internal static ConfigEntry<int> MaxSquadsPerRaid;
        internal static ConfigEntry<int> InviteExpirySeconds;
        internal static ConfigEntry<int> DisconnectGraceSeconds;
        internal static ConfigEntry<float> PositionRelaySeconds;
        internal static ConfigEntry<float> NearbyInviteRange;

        // Experience sharing
        internal static ConfigEntry<bool> XpShareEnabled;
        internal static ConfigEntry<float> XpShareFraction;
        internal static ConfigEntry<float> XpShareRange;
        internal static ConfigEntry<bool> XpShareClanmatesAnyDistance;
        internal static ConfigEntry<string> XpShareExcludedSkills;
        internal static ConfigEntry<float> XpShareBatchSeconds;
        internal static ConfigEntry<float> XpShareMaxFactorPerSecond;

        // Map
        internal static ConfigEntry<bool> ClanPings;
        internal static ConfigEntry<bool> ClanPositions;
        internal static ConfigEntry<bool> ClanBounties;

        // PvP
        internal static ConfigEntry<bool> BlockPartyDamage;
        internal static ConfigEntry<bool> BlockClanDamage;

        // Tombstones
        internal static ConfigEntry<bool> LockTombstones;

        // Wars
        internal static ConfigEntry<int> WarDurationHours;
        internal static ConfigEntry<int> WarProposalExpiryHours;
        internal static ConfigEntry<int> WarKillTargetMin;
        internal static ConfigEntry<int> WarKillTargetMax;
        internal static ConfigEntry<int> WarProposalCooldownHours;
        internal static ConfigEntry<int> WarWinnerClanXp;

        // Weekly reset
        internal static ConfigEntry<DayOfWeek> WeeklyResetDay;
        internal static ConfigEntry<int> WeeklyResetHourUtc;

        // Activities
        internal static ConfigEntry<float> ActivityParticipantRadius;
        internal static ConfigEntry<bool> ActivityPinsForEveryone;
        internal static ConfigEntry<bool> LootRolls;
        internal static ConfigEntry<bool> GlobalChat;
        internal static ConfigEntry<int> ChatMaxLength;
        internal static ConfigEntry<int> ChatRateLimit;
        internal static ConfigEntry<bool> ChatPanel;
        internal static ConfigEntry<float> ChatIdleOpacity;
        internal static ConfigEntry<bool> ChatTimestamps;
        internal static ConfigEntry<string> ChatPanelRect;
        internal static ConfigEntry<string> TrackerPosition;
        internal static ConfigEntry<string> PartyFramePosition;

        // Debug
        internal static ConfigEntry<bool> DebugSplitAccountsByCharacter;

        // Client
        internal static ConfigEntry<bool> ShowNameplateEmblems;
        internal static ConfigEntry<bool> ShowPartyFrame;
        internal static ConfigEntry<float> InviteToastSeconds;
        internal static ConfigEntry<KeyboardShortcut> ClansHotkey;
        internal static ConfigEntry<bool> TrackTrophyHunt;
        internal static ConfigEntry<bool> TrackRace;
        internal static ConfigEntry<bool> TrackWorldBoss;
        internal static ConfigEntry<bool> PingClanOnly;
        internal static ConfigEntry<bool> VisibleToClan;
        internal static ConfigEntry<bool> ShareBounties;
        internal static ConfigEntry<bool> AnnounceBounties;
        internal static ConfigEntry<bool> PrivateTombstones;

        private static HashSet<string> excludedSkillsCache;

        internal static void Bind() {
            EnableParties = ValConfig.BindServerConfig("Features", "Parties", true, "Lets players form parties and raids.");
            EnableWars = ValConfig.BindServerConfig("Features", "Wars", true, "Lets clans declare war on each other.");
            EnableStorage = ValConfig.BindServerConfig("Features", "Clan Storage", true, "Lets clans buy and use storage tabs for items. Off: clans only keep coins, in their treasury; items already stored stay there until this is turned back on. Which items may be stored is set in storage.yaml.");
            EnableActivities = ValConfig.BindServerConfig("Features", "Clan Activities", true, "Lets clans run weekly activities (Trophy Hunt, Race, World Boss Battles).");
            EnableBanners = ValConfig.BindServerConfig("Features", "Clan Banners", true, "Lets clan members build clan banners, in seven colours, which show their clan's emblem to everyone. Banners already up keep showing it.");
            EnableFeasts = ValConfig.BindServerConfig("Features", "Guild Feasts", true, "Lets clan members set out guild feasts, stronger copies of the vanilla feasts that anyone may eat from, unlocked by clan level. Which feasts, their cost and strength are set in feasts.yaml.");

            CreationFee = ValConfig.BindServerConfig("Clans", "Creation Fee", 1000, "Coins the founder pays to create a clan.", false, 0, 100000);
            NameMinLength = ValConfig.BindServerConfig("Clans", "Name Min Length", 3, "Shortest clan name allowed.", false, 1, 32);
            NameMaxLength = ValConfig.BindServerConfig("Clans", "Name Max Length", 24, "Longest clan name allowed.", false, 4, 32);
            MaxPendingApplications = ValConfig.BindServerConfig("Clans", "Max Pending Applications", 10, "How many clans one player can have applied to at once.", false, 1, 50);
            ApplicationExpiryDays = ValConfig.BindServerConfig("Clans", "Application Expiry Days", 7, "Days before an unanswered application is withdrawn.", false, 1, 60);
            LogEntriesKept = ValConfig.BindServerConfig("Clans", "Log Entries Kept", 100, "How many recent actions each clan's log keeps.", true, 10, 500);
            TreasuryWithdrawEnabled = ValConfig.BindServerConfig("Clans", "Treasury Withdraw Enabled", true, "Lets ranks with the treasury withdraw permission take coins out of the clan treasury.");
            DisbandRefund = ValConfig.BindServerConfig("Clans", "Disband Refund", false, "When a clan disbands, sends its treasury to the leader instead of losing it.");

            MaxPartySize = ValConfig.BindServerConfig("Parties", "Max Party Size", 5, "Players in one party (squad).", false, 2, 10);
            MaxSquadsPerRaid = ValConfig.BindServerConfig("Parties", "Max Squads Per Raid", 4, "Parties that can join one raid.", false, 1, 8);
            InviteExpirySeconds = ValConfig.BindServerConfig("Parties", "Invite Expiry Seconds", 60, "Seconds before an unanswered party invite lapses.", false, 10, 600);
            DisconnectGraceSeconds = ValConfig.BindServerConfig("Parties", "Disconnect Grace Seconds", 120, "Seconds a disconnected player keeps their party slot.", false, 0, 1800);
            PositionRelaySeconds = ValConfig.BindServerConfig("Parties", "Position Relay Seconds", 2f, "Seconds between party position and health updates (and clan map positions).", true, 0.5f, 10f);
            NearbyInviteRange = ValConfig.BindServerConfig("Parties", "Nearby Invite Range", 40f, "Metres within which players show in the Nearby invite list.", false, 5f, 200f);

            XpShareEnabled = ValConfig.BindServerConfig("Experience Sharing", "Enabled", true, "Party members share part of the skill experience they gain.");
            XpShareFraction = ValConfig.BindServerConfig("Experience Sharing", "Fraction", 0.25f, "Share of each skill gain that party members outside the gainer's clan receive. Clanmates receive the share their clan's level gives (levels.yaml).", false, 0f, 1f);
            XpShareRange = ValConfig.BindServerConfig("Experience Sharing", "Range", 50f, "Metres within which party members outside the gainer's clan share experience. Clanmates use their clan level's range (levels.yaml).", false, 5f, 500f);
            XpShareClanmatesAnyDistance = ValConfig.BindServerConfig("Experience Sharing", "Clanmates Any Distance", true, "Lets clanmates in the same party share experience at any distance once their clan's level allows it (XpShareAnyDistance in levels.yaml). Off: never.");
            XpShareExcludedSkills = ValConfig.BindServerConfig("Experience Sharing", "Excluded Skills", "Run,Jump,Swim,Sneak", "Comma-separated skill names that are never shared.");
            XpShareBatchSeconds = ValConfig.BindServerConfig("Experience Sharing", "Batch Seconds", 2f, "Seconds of skill gains bundled into one network message.", true, 0.5f, 10f);
            XpShareMaxFactorPerSecond = ValConfig.BindServerConfig("Experience Sharing", "Max Factor Per Second", 10f, "Cap on how much skill gain one player can share per second, against exploits.", true, 0.5f, 100f);
            XpShareExcludedSkills.SettingChanged += (_, _) => excludedSkillsCache = null;

            ClanPings = ValConfig.BindServerConfig("Map", "Clan Pings", true, "Lets players send their map pings to their clan only, with a switch on the large map.");
            ClanPositions = ValConfig.BindServerConfig("Map", "Clan Positions", true, "Lets players show their map position to their clan only, with a switch on the large map.");
            ClanBounties = ValConfig.BindServerConfig("Map", "Clan Bounties", true, "Lets players show their Epic Loot bounties on their clanmates' maps, with a switch on the large map. Needs Epic Loot with API 2.");

            BlockPartyDamage = ValConfig.BindServerConfig("PvP", "Block Party Damage", true, "Party members cannot hurt each other, even with PvP on.");
            BlockClanDamage = ValConfig.BindServerConfig("PvP", "Block Clan Damage", true, "Clanmates cannot hurt each other, even with PvP on.");

            LockTombstones = ValConfig.BindServerConfig("Tombstones", "Lock To Clan", true, "Only a tombstone's owner and their clanmates can open it, so nobody else can loot a player killed in PvP or take what they left behind. A player without a clan is the only one who can open theirs, and players can make theirs private. Off: anyone can open any tombstone, as in vanilla.");

            WarDurationHours = ValConfig.BindServerConfig("Wars", "Duration Hours", 168, "Hours a war lasts before it ends on its own.", false, 1, 2160);
            WarProposalExpiryHours = ValConfig.BindServerConfig("Wars", "Proposal Expiry Hours", 24, "Hours a declaration of war waits for an answer.", false, 1, 336);
            WarKillTargetMin = ValConfig.BindServerConfig("Wars", "Kill Target Min", 5, "Smallest kill target a clan can set when declaring war.", false, 1, 1000);
            WarKillTargetMax = ValConfig.BindServerConfig("Wars", "Kill Target Max", 100, "Largest kill target a clan can set when declaring war. 0 turns kill targets off.", false, 0, 10000);
            WarProposalCooldownHours = ValConfig.BindServerConfig("Wars", "Redeclare Cooldown Hours", 24, "Hours before the same two clans can go to war again.", false, 0, 720);
            WarWinnerClanXp = ValConfig.BindServerConfig("Wars", "Winner Clan XP", 500, "Clan experience for winning a war by kill target.", false, 0, 100000);

            WeeklyResetDay = ValConfig.cfg.Bind("Weekly Reset", "Day", DayOfWeek.Monday,
                new ConfigDescription("Day of the week clan activities reset.", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            WeeklyResetHourUtc = ValConfig.BindServerConfig("Weekly Reset", "Hour UTC", 0, "Hour of the day (UTC) clan activities reset.", false, 0, 23);

            ActivityParticipantRadius = ValConfig.BindServerConfig("Activities", "Participant Radius", 60f, "Metres from an activity totem within which clan members count as taking part.", false, 10f, 500f);
            LootRolls = ValConfig.BindServerConfig("Activities", "Loot Rolls", true, "Members who take part in an activity together in a party or raid roll Need, Greed or Pass for its magic item rewards, dropped at the activity. Needs Item Favorite Framework (with its loot rolls on) and Epic Loot on the server; otherwise rewards arrive by mailbox.");
            ActivityPinsForEveryone = ValConfig.BindServerConfig("Activities", "Map Markers For Everyone", true, "Every player sees every clan's activity totems and race destinations on the map. Off: only the clan's own members see them.");

            GlobalChat = ValConfig.BindServerConfig("Chat", "Global Chat", true, "A server-wide chat channel (/g), where players can also post party and clan invites.");
            ChatMaxLength = ValConfig.BindServerConfig("Chat", "Max Message Length", 300, "Longest Global, Clan, Party or Raid chat message, in characters.", false, 20, 1000);
            ChatRateLimit = ValConfig.BindServerConfig("Chat", "Messages Per 10 Seconds", 8, "How many Clans chat messages one player may send in 10 seconds. 0: no limit.", false, 0, 100);

            DebugSplitAccountsByCharacter = ValConfig.BindServerConfig("Debug", "Split Accounts By Character", false, "Treats each character as its own account, so one person can test with two characters. Never leave this on for real play.", null, true);

            ShowNameplateEmblems = ValConfig.cfg.Bind("Client", "Show Nameplate Emblems", true, "Shows clan emblems next to player names.");
            ShowPartyFrame = ValConfig.cfg.Bind("Client", "Show Party Frame", true, "Shows your party on the left side of the screen.");
            InviteToastSeconds = ValConfig.cfg.Bind("Client", "Invite Toast Seconds", 8f, "Seconds a party invite notice stays on screen.");
            ChatPanel = ValConfig.cfg.Bind("Client", "Chat Panel", true, "Replaces the chat window with the Clans chat panel (tabs for Global, Clan, Party and Raid, clickable links). Off, or with Chatter installed, Clans chat lines go into the usual chat window. Takes effect on the next login.");
            ChatIdleOpacity = ValConfig.cfg.Bind("Client", "Chat Idle Opacity", 0f, new BepInEx.Configuration.ConfigDescription("How visible the chat panel stays while nobody is chatting: 0 hides it, 1 keeps it fully visible.", new BepInEx.Configuration.AcceptableValueRange<float>(0f, 1f)));
            ChatTimestamps = ValConfig.cfg.Bind("Client", "Chat Timestamps", true, "Shows the time of each chat message in the chat panel.");
            ChatPanelRect = ValConfig.cfg.Bind("Client", "Chat Panel Position", "", "Where the chat panel sits and how big it is (x, y, width, height from the bottom-left); set by dragging it. Empty: the default spot.");
            TrackerPosition = ValConfig.cfg.Bind("Client", "Tracker Position", "", "Where the activity tracker sits, as fractions of the screen (x, y from the bottom-left); set from the Layout button in the Clans window. Empty: next to the minimap.");
            PartyFramePosition = ValConfig.cfg.Bind("Client", "Party Frame Position", "", "Where the party frame sits, as fractions of the screen (x, y from the bottom-left); set from the Layout button in the Clans window. Empty: the default spot at the left.");
            ClansHotkey = ValConfig.cfg.Bind("Client", "Clans Window Hotkey", new KeyboardShortcut(KeyCode.None), "Opens the Clans window. Useful when another mod replaces the inventory screen.");
            TrackTrophyHunt = ValConfig.cfg.Bind("Client", "Track Trophy Hunt", true, "Lists your clan's Trophy Hunt kills next to the minimap while it runs. Also switched from the tracker button in the inventory.");
            TrackRace = ValConfig.cfg.Bind("Client", "Track Race", true, "Shows your clan's Race next to the minimap while it runs. Also switched from the tracker button in the inventory.");
            TrackWorldBoss = ValConfig.cfg.Bind("Client", "Track World Boss", true, "Shows your clan's World Boss Battle next to the minimap while it runs. Also switched from the tracker button in the inventory.");
            PingClanOnly = ValConfig.cfg.Bind("Client", "Ping Clan Only", false, "Your map pings are seen by your clan only. Also switched on the large map.");
            VisibleToClan = ValConfig.cfg.Bind("Client", "Visible To Clan", false, "Your clan sees where you are on the map, even while you are not visible to other players. Also switched on the large map.");
            ShareBounties = ValConfig.cfg.Bind("Client", "Share Bounties", false, "Your clan sees the areas of your Epic Loot bounties on their maps, so they can help. Also switched on the large map.");
            AnnounceBounties = ValConfig.cfg.Bind("Client", "Announce Bounties", false, "When you take a bounty while sharing them, a line in clan chat shows your clanmates where it is.");
            PrivateTombstones = ValConfig.cfg.Bind("Client", "Private Tombstones", false, "Only you can open your tombstones, not your clanmates. Applies to tombstones made from then on; alt-use (Shift + E) on one of your own tombstones switches that one.");
        }

        // Whether the activity tracker shows this kind of activity.
        internal static ConfigEntry<bool> Tracked(Server.Model.ActivityType type) {
            switch (type) {
                case Server.Model.ActivityType.TrophyHunt: return TrackTrophyHunt;
                case Server.Model.ActivityType.Race: return TrackRace;
                default: return TrackWorldBoss;
            }
        }

        internal static bool IsSkillExcluded(Skills.SkillType skill) {
            if (excludedSkillsCache == null) {
                excludedSkillsCache = new HashSet<string>(
                    (XpShareExcludedSkills.Value ?? "")
                        .Split(',')
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0),
                    StringComparer.OrdinalIgnoreCase);
            }
            return excludedSkillsCache.Contains(skill.ToString());
        }
    }
}
