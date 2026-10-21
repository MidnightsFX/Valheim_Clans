using Clans.Core;
using System.Collections.Generic;

namespace Clans.Server.Model {
    // Everything Clans saves for one world. Server only; clients receive views of it, never this.
    //
    // Serialized as yaml (YamlFormat.Default, which omits default values), so every member with a non-default
    // initializer would need [DefaultValue]; there are none, by design. Times are Unix seconds, UTC.
    public class ClansWorldState {
        public int SchemaVersion { get; set; }
        public long WorldUid { get; set; }
        public string WorldName { get; set; } = "";

        public long NextClanId { get; set; }
        public long NextWarId { get; set; }
        public long NextActivityId { get; set; }
        public long NextDeliveryId { get; set; }

        public Dictionary<long, ClanRecord> Clans { get; set; } = new Dictionary<long, ClanRecord>();
        // Every account the server has seen, keyed by account id.
        public Dictionary<string, AccountRecord> Accounts { get; set; } = new Dictionary<string, AccountRecord>();
        public List<WarRecord> Wars { get; set; } = new List<WarRecord>();
        // Ended wars, newest last, capped.
        public List<WarRecord> WarHistory { get; set; } = new List<WarRecord>();
        // Rewards and returned items waiting for each account, delivered when they are next online.
        public Dictionary<string, List<Delivery>> Mailbox { get; set; } = new Dictionary<string, List<Delivery>>();
        // Shifts the weekly clock for testing (clans-admin week).
        public long DebugWeekOffsetSeconds { get; set; }

        internal const int CurrentSchema = 1;
    }

    public class ClanRecord {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "";
        public EmblemData Emblem { get; set; } = new EmblemData();
        public bool Recruiting { get; set; }
        public long CreatedUtc { get; set; }
        public long Xp { get; set; }
        // Bought with experience and resources (Server.LevelService). 0 in saves from before levels were bought,
        // which then take the level their experience gave them.
        public int Level { get; set; }
        // Items the members have handed in toward the next level: prefab name -> amount.
        public Dictionary<string, int> LevelContributions { get; set; } = new Dictionary<string, int>();
        public long Treasury { get; set; }
        public int NextRankId { get; set; }
        // Top to bottom: index 0 is the leader rank, the last is the entry rank.
        public List<RankDef> Ranks { get; set; } = new List<RankDef>();
        public Dictionary<string, MemberRecord> Members { get; set; } = new Dictionary<string, MemberRecord>();
        public List<ApplicationRecord> Applications { get; set; } = new List<ApplicationRecord>();
        // Newest last, capped by the Log Entries Kept setting.
        public List<LogEntry> Log { get; set; } = new List<LogEntry>();
        public ClanStorageRecord Storage { get; set; } = new ClanStorageRecord();
        // Activity type -> start of the weekly period it was last started in.
        public Dictionary<string, long> Lockouts { get; set; } = new Dictionary<string, long>();
        public List<ActivityInstance> Activities { get; set; } = new List<ActivityInstance>();
        public int WarsWon { get; set; }
        public int ActivitiesCompleted { get; set; }
    }

    public class MemberRecord {
        public string Account { get; set; } = "";
        public int RankId { get; set; }
        public string LastCharacterName { get; set; } = "";
        public long JoinedUtc { get; set; }
        public long LastSeenUtc { get; set; }
    }

    public class AccountRecord {
        public string Account { get; set; } = "";
        // 0 when not in a clan.
        public long ClanId { get; set; }
        public List<long> PendingApplications { get; set; } = new List<long>();
        public string LastCharacterName { get; set; } = "";
        public long LastSeenUtc { get; set; }
        // The Epic Loot bounties this account shares with its clan, as last reported (Server.BountyRelay).
        public List<SharedBountyRecord> Bounties { get; set; } = new List<SharedBountyRecord>();
    }

    // One shared bounty: where its circle is and what is left alive. Kept while the owner is offline, so
    // clanmates can still clear it for them.
    public class SharedBountyRecord {
        // Epic Loot's id, which holds the owner's player id.
        public string Id { get; set; } = "";
        public string MonsterId { get; set; } = "";
        // The target's name as the owner's game showed it.
        public string Name { get; set; } = "";
        public string Biome { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Radius { get; set; }
        public bool Slain { get; set; }
        public List<BountyAddRecord> Adds { get; set; } = new List<BountyAddRecord>();
        // Every creature is dead; the owner has yet to claim the reward.
        public bool Complete { get; set; }
        public long SharedUtc { get; set; }
    }

    public class BountyAddRecord {
        public string MonsterId { get; set; } = "";
        public int Count { get; set; }
    }

    public class ApplicationRecord {
        public string Account { get; set; } = "";
        public string CharacterName { get; set; } = "";
        public string Message { get; set; } = "";
        public long CreatedUtc { get; set; }
    }

    public class LogEntry {
        public long Utc { get; set; }
        // A localization token; Args fill its {0}, {1}...
        public string Token { get; set; } = "";
        public List<string> Args { get; set; } = new List<string>();
    }

    public class ClanStorageRecord {
        // One base64 Inventory.Save package per bought shared tab, in purchase order.
        public List<string> SharedTabs { get; set; } = new List<string>();
        public bool PersonalUnlocked { get; set; }
        public Dictionary<string, string> PersonalTabs { get; set; } = new Dictionary<string, string>();
    }

    public enum WarState { Proposed, Active, Ended }
    public enum WarEndReason { None, Peace, Expired, KillTarget, Disbanded, Declined, Cancelled }

    public class WarRecord {
        public long Id { get; set; }
        // ClanA declared on ClanB.
        public long ClanA { get; set; }
        public long ClanB { get; set; }
        public WarState State { get; set; }
        public string ProposedBy { get; set; } = "";
        public long ProposedUtc { get; set; }
        public long StartedUtc { get; set; }
        public long EndsUtc { get; set; }
        public long EndedUtc { get; set; }
        // 0 = no kill target.
        public int KillTarget { get; set; }
        public int KillsA { get; set; }
        public int KillsB { get; set; }
        // The clan that has offered peace, 0 if none.
        public long PeaceOfferedBy { get; set; }
        public WarEndReason EndReason { get; set; }
        public long Winner { get; set; }

        internal bool Involves(long clanId) => ClanA == clanId || ClanB == clanId;
        internal long Other(long clanId) => ClanA == clanId ? ClanB : ClanA;
    }

    public enum ActivityType { TrophyHunt, Race, WorldBoss }
    public enum ActivityState { Placed, Running, BossPhase, Completed, Failed, Cancelled }

    public class ActivityInstance {
        public long Id { get; set; }
        public ActivityType Type { get; set; }
        public int Difficulty { get; set; }
        public ActivityState State { get; set; }
        public string StartedBy { get; set; } = "";
        public long StartedUtc { get; set; }
        public long BeganUtc { get; set; }
        public long DeadlineUtc { get; set; }
        public long EndedUtc { get; set; }
        // The totem's ZDO, as user id + id, and where it stands. While TotemPending these are its placer's (the marker
        // that becomes the totem once someone is near, Gameplay.TotemPlacer), and the position is only roughly where
        // the totem will be.
        public long TotemUser { get; set; }
        public uint TotemZdoId { get; set; }
        public float TotemX { get; set; }
        public float TotemY { get; set; }
        public float TotemZ { get; set; }
        public bool TotemPending { get; set; }
        public List<string> Participants { get; set; } = new List<string>();
        public int ParticipantCountAtBegin { get; set; }

        // World Boss
        public string BossId { get; set; } = "";
        public string FortressHandle { get; set; } = "";
        public int Phase { get; set; }

        // Race. ChestsSpawned: whether any chest was taken from the start totem yet. The destination's ZDO works like
        // the totem's, DestPending included.
        public bool ChestsSpawned { get; set; }
        public int ChestsTotal { get; set; }
        public List<int> ChestSerials { get; set; } = new List<int>();
        public List<int> Delivered { get; set; } = new List<int>();
        public long DestUser { get; set; }
        public uint DestZdoId { get; set; }
        public float DestX { get; set; }
        public float DestY { get; set; }
        public float DestZ { get; set; }
        public bool DestPending { get; set; }

        // Trophy Hunt
        public List<HuntTarget> Targets { get; set; } = new List<HuntTarget>();
    }

    public class HuntTarget {
        public string Prefab { get; set; } = "";
        public int Required { get; set; }
        public int Count { get; set; }
        public bool IsBoss { get; set; }
    }

    public class Delivery {
        public long Id { get; set; }
        // A localization token saying why (shown when it arrives).
        public string Reason { get; set; } = "";
        public int Coins { get; set; }
        public List<DeliveryItem> Items { get; set; } = new List<DeliveryItem>();
        // Whole items with their data, e.g. a kicked member's personal tab: base64 Inventory.Save packages.
        public List<string> Inventories { get; set; } = new List<string>();
        public long CreatedUtc { get; set; }
    }

    public class DeliveryItem {
        public string Prefab { get; set; } = "";
        public int Stack { get; set; }
        public int Quality { get; set; }
        // Rolled into an Epic Loot magic item of Rarity (0 Magic ... 5 Ancient) when it arrives.
        public bool Magic { get; set; }
        public int Rarity { get; set; }
    }
}
