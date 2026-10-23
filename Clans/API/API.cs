using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Clans {
    // The public integration surface for other mods: clans, members and ranks, parties and raids, and each clan's
    // treasury and storage. Every endpoint forwards to the receiver for this machine (API/Receiver): ServerReceiver
    // on the server, ClientReceiver on a client. Nothing here reaches into the mod itself.
    //
    // Three ways in:
    //   - a hard reference to Clans.dll plus [BepInDependency], calling this directly;
    //   - the ClansAPI shim (ClansAPI/ in this repository, bundled or copied), which binds these methods once and
    //     no-ops when Clans is not installed;
    //   - plain reflection on Type.GetType("Clans.API, Clans").
    //
    // The signature rule: only primitives, string, vanilla/Unity types and System.Func/Action of those cross this
    // boundary -- no type of this mod appears in any signature -- so every method can be bound with
    // Delegate.CreateDelegate by a mod that never references this assembly. Existing signatures never change;
    // additions bump ApiVersion.
    //
    // Players are named by account id: the platform id membership is kept under ("Steam_7656..."), shared by all of
    // one person's characters. On the server every answer is complete; a client knows every clan's summary, every
    // online player's clan, rank and party, and its own clan, party and raid in full. Anything else answers as
    // unknown: 0, -1, "" or false (docs/API.md). Call everything on the main thread.
    public static class API {
        public const int ApiVersion = 1;

        private static IApiReceiver Receiver => ApiReceiver.Current;

        // ---------------------------------------------------------------- versioning and status

        public static int GetApiVersion() => ApiVersion;

        public static string GetPluginVersion() => Clans.PluginVersion;

        public static string GetPluginId() => Clans.PluginGUID;

        public static bool HasEndpoint(string name) => GetEndpointNames().Contains(name);

        public static List<string> GetEndpointNames() {
            return typeof(API).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(m => m.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        // There is clan data to answer from: on the server once the world's clans are loaded, on a client once the
        // server has welcomed the player. Before that, everyone looks clanless.
        public static bool IsReady() => Receiver.IsReady();

        // This machine is the server, where every clan's members, ranks, treasury and storage are known.
        public static bool HasFullData() => Receiver.HasFullData();

        // ---------------------------------------------------------------- players and accounts

        // The local player's account; "" before the server has welcomed them, and on a dedicated server.
        public static string GetLocalAccountId() => Receiver.GetLocalAccountId();

        public static string GetAccountId(Player player) => Receiver.GetAccountId(player);

        // By a connection's peer id: ZNetPeer.m_uid, or the sender of a routed RPC.
        public static string GetAccountIdOfPeer(long peerId) => Receiver.GetAccountIdOfPeer(peerId);

        // By a character's player id (Player.GetPlayerID). On a client the character must be loaded nearby.
        public static string GetAccountIdOfPlayerId(long playerId) => Receiver.GetAccountIdOfPlayerId(playerId);

        // The character name the account was last seen with.
        public static string GetPlayerName(string accountId) => Receiver.GetPlayerName(accountId);

        public static bool IsOnline(string accountId) => Receiver.IsOnline(accountId);

        // ---------------------------------------------------------------- clans

        public static List<long> GetClanIds() => Receiver.GetClanIds();

        public static bool ClanExists(long clanId) => Receiver.ClanExists(clanId);

        public static string GetClanName(long clanId) => Receiver.GetClanName(clanId);

        // By name, ignoring case and outer spaces. 0 when there is none.
        public static long FindClanByName(string name) => Receiver.FindClanByName(name);

        // 1 and up; 0 when there is no such clan.
        public static int GetClanLevel(long clanId) => Receiver.GetClanLevel(clanId);

        // The highest level the server's levels.yaml defines.
        public static int GetMaxClanLevel() => Receiver.GetMaxClanLevel();

        public static int GetMemberCount(long clanId) => Receiver.GetMemberCount(clanId);

        // The leader's account.
        public static string GetLeaderId(long clanId) => Receiver.GetLeaderId(clanId);

        public static bool AreAtWar(long clanA, long clanB) => Receiver.AreAtWar(clanA, clanB);

        // {"id","name","description","level","maxLevel","maxMembers","memberCount","recruiting","leaderId","leaderName",
        //  "enemies":[clan ids],"detail","xp","treasury","sharedTabs"}; detail false (another clan, on a client) leaves
        // xp, treasury and sharedTabs at -1. "" when there is no such clan.
        public static string GetClanJson(long clanId) => Receiver.GetClanJson(clanId);

        // Every clan, in GetClanJson's shape.
        public static string GetClansJson() => Receiver.GetClansJson();

        // ---------------------------------------------------------------- membership

        // 0 when in no clan, or unknown here.
        public static long GetClanIdOf(string accountId) => Receiver.GetClanIdOf(accountId);

        public static long GetClanIdOfPlayer(Player player) => Receiver.GetClanIdOfPlayer(player);

        public static bool IsInClan(string accountId) => Receiver.IsInClan(accountId);

        // Both in the same clan; false when either is in none.
        public static bool AreClanmates(string accountA, string accountB) => Receiver.AreClanmates(accountA, accountB);

        // A client knows other clans' online members only.
        public static List<string> GetMemberIds(long clanId) => Receiver.GetMemberIds(clanId);

        public static List<string> GetOnlineMemberIds(long clanId) => Receiver.GetOnlineMemberIds(clanId);

        // [{"id","name","rankId","rankIndex","rank","leader","online","joinedUtc","lastSeenUtc"}]; times in Unix
        // seconds, 0 when unknown here.
        public static string GetMembersJson(long clanId) => Receiver.GetMembersJson(clanId);

        // ---------------------------------------------------------------- ranks and rights

        // 0 is the leader, higher is lower. -1 when in no clan, or unknown here.
        public static int GetRankIndex(string accountId) => Receiver.GetRankIndex(accountId);

        // Stays the same through renames and reordering. 0 when unknown.
        public static int GetRankId(string accountId) => Receiver.GetRankId(accountId);

        // In this machine's language.
        public static string GetRankName(string accountId) => Receiver.GetRankName(accountId);

        public static bool IsLeader(string accountId) => Receiver.IsLeader(accountId);

        // permission is one of GetPermissionNames, ignoring case. A client knows this for its own clan only.
        public static bool HasPermission(string accountId, string permission) => Receiver.HasPermission(accountId, permission);

        public static List<string> GetPermissionNames() => Receiver.GetPermissionNames();

        // Top to bottom: [{"id","index","name","storedName","leader","entry","permissions":[...]}]. "[]" when unknown
        // here: a client has only its own clan's.
        public static string GetRanksJson(long clanId) => Receiver.GetRanksJson(clanId);

        // ---------------------------------------------------------------- parties and raids
        //
        // Parties last while the server runs. A raid joins parties as its squads, numbered from 1. A member who
        // disconnects keeps their place for a grace period, shown offline.

        // 0 when in no party, or unknown here.
        public static long GetPartyId(string accountId) => Receiver.GetPartyId(accountId);

        // Both in the same party; false when either is in none.
        public static bool ArePartymates(string accountA, string accountB) => Receiver.ArePartymates(accountA, accountB);

        public static string GetPartyLeaderId(long partyId) => Receiver.GetPartyLeaderId(partyId);

        // A client knows other parties' online members only.
        public static List<string> GetPartyMemberIds(long partyId) => Receiver.GetPartyMemberIds(partyId);

        // {"id","leaderId","raidId","squad","members":[{"id","name","leader","online","clanId"}]}; "" when unknown.
        public static string GetPartyJson(long partyId) => Receiver.GetPartyJson(partyId);

        // 0 when in no raid, or unknown here.
        public static long GetRaidId(string accountId) => Receiver.GetRaidId(accountId);

        // The squad number in the raid, 1 and up; 0 outside a raid.
        public static int GetSquad(string accountId) => Receiver.GetSquad(accountId);

        // Both in the same raid, in any squads; false when either is in none.
        public static bool AreRaidmates(string accountA, string accountB) => Receiver.AreRaidmates(accountA, accountB);

        // "" on a client when the raid's leader is offline and it is not the player's own raid.
        public static string GetRaidLeaderId(long raidId) => Receiver.GetRaidLeaderId(raidId);

        public static List<string> GetRaidMemberIds(long raidId) => Receiver.GetRaidMemberIds(raidId);

        // {"id","leaderId","squads":[GetPartyJson's shape, in squad order]}; "" when unknown.
        public static string GetRaidJson(long raidId) => Receiver.GetRaidJson(raidId);

        // ---------------------------------------------------------------- treasury
        //
        // Changes are for the server only. A reason, when not empty, goes into the clan's log for its members to see
        // (a $token in it is translated for each reader).

        // -1 when unknown here: a client has only its own clan's.
        public static long GetTreasury(long clanId) => Receiver.GetTreasury(clanId);

        // False on a client, for no such clan, or coins below 1.
        public static bool AddTreasury(long clanId, long coins, string reason) => Receiver.AddTreasury(clanId, coins, reason);

        // All or none: false when it holds fewer.
        public static bool TakeTreasury(long clanId, long coins, string reason) => Receiver.TakeTreasury(clanId, coins, reason);

        // ---------------------------------------------------------------- storage (shared tabs)
        //
        // Changes are for the server only, all or nothing, and leave tabs a member has open alone. On false, error is
        // notServer, noClan, badItems, unknownItem, storageOff, notAccepted, notEnough, noRoom or tabOpen.
        // itemsJson is {"Wood":10,"Stone":5}.

        // -1 when unknown here.
        public static int GetSharedTabCount(long clanId) => Receiver.GetSharedTabCount(clanId);

        // Server only: {"clan","tabs":[{"index","openBy","items":[{"prefab","stack","quality","x","y","custom"}]}]};
        // "" on a client or for no such clan.
        public static string GetStorageJson(long clanId) => Receiver.GetStorageJson(clanId);

        // Server only: how many of the item, any quality. -1 on a client or for no such clan.
        public static int CountStorageItem(long clanId, string prefab) => Receiver.CountStorageItem(clanId, prefab);

        // Client: asks for the local player's clan's storage, if their rank may deposit or withdraw. callback runs
        // once, later: (true, json in GetStorageJson's shape) or (false, notReady, notInClan, noPermission,
        // storageOff, noServer or timeout).
        public static void RequestStorageJson(Action<bool, string> callback) => Receiver.RequestStorageJson(callback);

        // By prefab whatever the quality, from the first tab and slot on.
        public static bool TakeStorageItems(long clanId, string itemsJson, string reason, out string error) => Receiver.TakeStorageItems(clanId, itemsJson, reason, out error);

        // New items at quality 1: they top up stacks of the same item, then fill empty cells.
        public static bool AddStorageItems(long clanId, string itemsJson, string reason, out string error) => Receiver.AddStorageItems(clanId, itemsJson, reason, out error);

        // A copy of the item as it is (quality, durability, custom data), split if larger than its stack size. It
        // needs its prefab (m_dropPrefab).
        public static bool AddStorageItem(long clanId, ItemDrop.ItemData item, string reason, out string error) => Receiver.AddStorageItem(clanId, item, reason, out error);

        // ---------------------------------------------------------------- listeners

        // After anything these endpoints answer may have changed: on a client when the server's updates arrive, on
        // the server at most twice a second. Carries nothing: re-query. A listener that throws does not stop others.
        public static void AddChangedListener(Action listener) => Receiver.AddChangedListener(listener);

        public static void RemoveChangedListener(Action listener) => Receiver.RemoveChangedListener(listener);

        // Server only: one JSON object per change, right after it is made, with "event" and "clan", "party" or "raid":
        //   clanFounded {name, account, playerName}      clanDisbanded {name}
        //   memberJoined / rankChanged {account, playerName, rankId, rankIndex, rank}
        //   memberLeft {account, playerName, reason: left|kicked|disbanded}      ranksChanged {}
        //   levelChanged {level, previous}      treasuryChanged {treasury, change}      storageChanged {tab}
        //   partyJoined / partyLeaderChanged {account, playerName}      partyDisbanded {}
        //   partyLeft {account, playerName, reason: left|kicked|timedOut|moved}
        //   raidJoined {party, squad}      raidLeft {party}      raidLeaderChanged {account, playerName}      raidDisbanded {}
        public static void AddServerEventListener(Action<string> listener) => Receiver.AddServerEventListener(listener);

        public static void RemoveServerEventListener(Action<string> listener) => Receiver.RemoveServerEventListener(listener);
    }
}
