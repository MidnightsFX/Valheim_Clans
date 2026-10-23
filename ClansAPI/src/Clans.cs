using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

// Clans public API shim (API 1). Copy this one file into your project, or bundle ClansAPI.dll with ILRepack.

namespace ClansAPI {
    /// <summary>
    /// Typed access to the Clans mod's <c>Clans.API</c> without referencing it: who is in which clan, clan levels,
    /// members' ranks and rights, parties and raids, and each clan's treasury and storage.
    ///
    /// Declare a soft dependency so Clans loads first:
    /// <c>[BepInDependency("MidnightsFX.Clans", BepInDependency.DependencyFlags.SoftDependency)]</c>
    ///
    /// Every method is safe to call when Clans is not installed: queries answer 0, -1, false, "" or an empty list,
    /// and changes answer false. Each endpoint is bound once to a typed delegate, so a call costs no reflection.
    /// Call from the main thread.
    ///
    /// Players are named by account id, the platform id clan membership is kept under ("Steam_7656..."), which all
    /// of one person's characters share. On the server every answer is complete; a client knows every clan's
    /// summary, every online player's clan, rank and party, and its own clan, party and raid in full (see
    /// <see cref="HasFullData"/>).
    /// </summary>
    public static class Clans {
        /// <summary>The Clans BepInEx plugin GUID.</summary>
        public const string PluginGuid = "MidnightsFX.Clans";

        /// <summary>Error: Clans is not installed (or too old to have the endpoint).</summary>
        public const string ErrorNotInstalled = "notInstalled";
        /// <summary>Error: a change only the server can make was asked for on a client.</summary>
        public const string ErrorNotServer = "notServer";
        /// <summary>Error: no such clan.</summary>
        public const string ErrorNoClan = "noClan";
        /// <summary>Error: the items were not {"Prefab":amount,...} with whole amounts above 0, or no item was given.</summary>
        public const string ErrorBadItems = "badItems";
        /// <summary>Error: an item the server does not know.</summary>
        public const string ErrorUnknownItem = "unknownItem";
        /// <summary>Error: the server has clan storage switched off.</summary>
        public const string ErrorStorageOff = "storageOff";
        /// <summary>Error: the server's storage rules refuse one of the items.</summary>
        public const string ErrorNotAccepted = "notAccepted";
        /// <summary>Error: the shared tabs hold too few of the items.</summary>
        public const string ErrorNotEnough = "notEnough";
        /// <summary>Error: the shared tabs have too little space.</summary>
        public const string ErrorNoRoom = "noRoom";
        /// <summary>Error: it would only work counting tabs a member has open; try again once they close them.</summary>
        public const string ErrorTabOpen = "tabOpen";
        /// <summary>Error (RequestStorageJson): there is nothing to answer yet, as the server has not welcomed the player.</summary>
        public const string ErrorNotReady = "notReady";
        /// <summary>Error (RequestStorageJson): the local player is in no clan.</summary>
        public const string ErrorNotInClan = "notInClan";
        /// <summary>Error (RequestStorageJson): the local player's rank may neither deposit nor withdraw.</summary>
        public const string ErrorNoPermission = "noPermission";
        /// <summary>Error (RequestStorageJson): no connection to a server.</summary>
        public const string ErrorNoServer = "noServer";
        /// <summary>Error (RequestStorageJson): the server did not answer.</summary>
        public const string ErrorTimeout = "timeout";

        /// <summary>Server event: a clan was founded. {name, account, playerName}</summary>
        public const string EventClanFounded = "clanFounded";
        /// <summary>Server event: a clan was disbanded, after a memberLeft for each member. {name}</summary>
        public const string EventClanDisbanded = "clanDisbanded";
        /// <summary>Server event: someone joined a clan. {account, playerName, rankId, rankIndex, rank}</summary>
        public const string EventMemberJoined = "memberJoined";
        /// <summary>Server event: someone left a clan. {account, playerName, reason: left, kicked or disbanded}</summary>
        public const string EventMemberLeft = "memberLeft";
        /// <summary>Server event: a member's rank changed. {account, playerName, rankId, rankIndex, rank}</summary>
        public const string EventRankChanged = "rankChanged";
        /// <summary>Server event: the clan's rank list was edited (names, rights or order).</summary>
        public const string EventRanksChanged = "ranksChanged";
        /// <summary>Server event: the clan's level changed. {level, previous}</summary>
        public const string EventLevelChanged = "levelChanged";
        /// <summary>Server event: the clan's treasury changed. {treasury, change}</summary>
        public const string EventTreasuryChanged = "treasuryChanged";
        /// <summary>Server event: a shared storage tab's contents changed, or it was bought. {tab}</summary>
        public const string EventStorageChanged = "storageChanged";
        /// <summary>Server event, with "party": someone joined a party, a new party's leader included. {account, playerName}</summary>
        public const string EventPartyJoined = "partyJoined";
        /// <summary>Server event, with "party": someone left a party. {account, playerName, reason: left, kicked, timedOut or moved}</summary>
        public const string EventPartyLeft = "partyLeft";
        /// <summary>Server event, with "party": the party has a new leader. {account, playerName}</summary>
        public const string EventPartyLeaderChanged = "partyLeaderChanged";
        /// <summary>Server event, with "party": the party's last member left.</summary>
        public const string EventPartyDisbanded = "partyDisbanded";
        /// <summary>Server event, with "raid": a party became a squad of the raid. {party, squad}</summary>
        public const string EventRaidJoined = "raidJoined";
        /// <summary>Server event, with "raid": a party is no longer a squad of the raid. {party}</summary>
        public const string EventRaidLeft = "raidLeft";
        /// <summary>Server event, with "raid": the raid has a new leader. {account, playerName}</summary>
        public const string EventRaidLeaderChanged = "raidLeaderChanged";
        /// <summary>Server event, with "raid": the raid is down to one squad, a plain party again.</summary>
        public const string EventRaidDisbanded = "raidDisbanded";

        /// <summary>Permission names <see cref="HasPermission"/> takes.</summary>
        public const string PermissionManageApplications = "ManageApplications";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionInvite = "Invite";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionKick = "Kick";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionPromote = "Promote";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionDemote = "Demote";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionEditInfo = "EditInfo";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionEditEmblem = "EditEmblem";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionManageWar = "ManageWar";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionStartActivities = "StartActivities";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionUpgradeStorage = "UpgradeStorage";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionStorageDeposit = "StorageDeposit";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionStorageWithdraw = "StorageWithdraw";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionTreasuryDonate = "TreasuryDonate";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionTreasuryWithdraw = "TreasuryWithdraw";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionEditRanks = "EditRanks";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionUpgradeClan = "UpgradeClan";
        /// <inheritdoc cref="PermissionManageApplications"/>
        public const string PermissionClanFeasts = "ClanFeasts";

        private const string ApiTypeName = "Clans.API, Clans";

        // The endpoints with out parameters need their own delegate types.
        private delegate bool StorageEditFn(long clanId, string itemsJson, string reason, ref string error);
        private delegate bool StorageItemFn(long clanId, ItemDrop.ItemData item, string reason, ref string error);

        private static bool bound;
        private static Type apiType;

        private static Func<int> getApiVersion;
        private static Func<string> getPluginVersion;
        private static Func<string, bool> hasEndpoint;
        private static Func<bool> isReady;
        private static Func<bool> hasFullData;
        private static Func<string> getLocalAccountId;
        private static Func<Player, string> getAccountId;
        private static Func<long, string> getAccountIdOfPeer;
        private static Func<long, string> getAccountIdOfPlayerId;
        private static Func<string, string> getPlayerName;
        private static Func<string, bool> isOnline;
        private static Func<List<long>> getClanIds;
        private static Func<long, bool> clanExists;
        private static Func<long, string> getClanName;
        private static Func<string, long> findClanByName;
        private static Func<long, int> getClanLevel;
        private static Func<int> getMaxClanLevel;
        private static Func<long, int> getMemberCount;
        private static Func<long, string> getLeaderId;
        private static Func<long, long, bool> areAtWar;
        private static Func<long, string> getClanJson;
        private static Func<string> getClansJson;
        private static Func<string, long> getClanIdOf;
        private static Func<Player, long> getClanIdOfPlayer;
        private static Func<string, bool> isInClan;
        private static Func<string, string, bool> areClanmates;
        private static Func<long, List<string>> getMemberIds;
        private static Func<long, List<string>> getOnlineMemberIds;
        private static Func<long, string> getMembersJson;
        private static Func<string, int> getRankIndex;
        private static Func<string, int> getRankId;
        private static Func<string, string> getRankName;
        private static Func<string, bool> isLeader;
        private static Func<string, string, bool> hasPermission;
        private static Func<List<string>> getPermissionNames;
        private static Func<long, string> getRanksJson;
        private static Func<string, long> getPartyId;
        private static Func<string, string, bool> arePartymates;
        private static Func<long, string> getPartyLeaderId;
        private static Func<long, List<string>> getPartyMemberIds;
        private static Func<long, string> getPartyJson;
        private static Func<string, long> getRaidId;
        private static Func<string, int> getSquad;
        private static Func<string, string, bool> areRaidmates;
        private static Func<long, string> getRaidLeaderId;
        private static Func<long, List<string>> getRaidMemberIds;
        private static Func<long, string> getRaidJson;
        private static Action<Action> addChangedListener;
        private static Action<Action> removeChangedListener;
        private static Action<Action<string>> addServerEventListener;
        private static Action<Action<string>> removeServerEventListener;
        private static Func<long, long> getTreasury;
        private static Func<long, long, string, bool> addTreasury;
        private static Func<long, long, string, bool> takeTreasury;
        private static Func<long, int> getSharedTabCount;
        private static Func<long, string> getStorageJson;
        private static Func<long, string, int> countStorageItem;
        private static Action<Action<bool, string>> requestStorageJson;
        private static StorageEditFn takeStorageItems;
        private static StorageEditFn addStorageItems;
        private static StorageItemFn addStorageItem;

        // -------------------------------------------------------------------------------------------------
        // Versioning
        // -------------------------------------------------------------------------------------------------

        /// <summary>True when Clans is installed and its API was found.</summary>
        public static bool IsLoaded() {
            Bind();
            return apiType != null;
        }

        /// <summary>Clans' API version, or 0 when it is not installed.</summary>
        public static int GetApiVersion() {
            Bind();
            return getApiVersion != null ? getApiVersion() : 0;
        }

        /// <summary>Clans' plugin version, or an empty string when it is not installed.</summary>
        public static string GetPluginVersion() {
            Bind();
            return getPluginVersion != null ? getPluginVersion() : "";
        }

        /// <summary>Whether the installed Clans has an endpoint of this name. Use it to gate anything newer than the oldest version you support.</summary>
        public static bool HasEndpoint(string name) {
            Bind();
            return hasEndpoint != null && hasEndpoint(name);
        }

        /// <summary>
        /// True once there is clan data to answer from: on the server once the world's clans are loaded, on a
        /// client once the server has welcomed the player. Before that, everyone looks clanless.
        /// </summary>
        public static bool IsReady() {
            Bind();
            return isReady != null && isReady();
        }

        /// <summary>True on the server, where every clan's members, ranks, treasury and storage are known. False on a client.</summary>
        public static bool HasFullData() {
            Bind();
            return hasFullData != null && hasFullData();
        }

        // -------------------------------------------------------------------------------------------------
        // Players and accounts
        // -------------------------------------------------------------------------------------------------

        /// <summary>The local player's account id; "" before the server has welcomed them, and on a dedicated server.</summary>
        public static string GetLocalAccountId() {
            Bind();
            return getLocalAccountId != null ? getLocalAccountId() : "";
        }

        /// <summary>A player's account id, or "" when unknown.</summary>
        public static string GetAccountId(Player player) {
            Bind();
            return getAccountId != null ? getAccountId(player) : "";
        }

        /// <summary>A connection's account id, by peer id (ZNetPeer.m_uid, or the sender of a routed RPC).</summary>
        public static string GetAccountIdOfPeer(long peerId) {
            Bind();
            return getAccountIdOfPeer != null ? getAccountIdOfPeer(peerId) : "";
        }

        /// <summary>An account id by character player id (Player.GetPlayerID). On a client the character must be loaded nearby.</summary>
        public static string GetAccountIdOfPlayerId(long playerId) {
            Bind();
            return getAccountIdOfPlayerId != null ? getAccountIdOfPlayerId(playerId) : "";
        }

        /// <summary>The character name the account was last seen with, or "" when unknown here.</summary>
        public static string GetPlayerName(string accountId) {
            Bind();
            return getPlayerName != null ? getPlayerName(accountId) : "";
        }

        /// <summary>Whether the account is connected.</summary>
        public static bool IsOnline(string accountId) {
            Bind();
            return isOnline != null && isOnline(accountId);
        }

        // -------------------------------------------------------------------------------------------------
        // Clans
        // -------------------------------------------------------------------------------------------------

        /// <summary>Every clan's id.</summary>
        public static List<long> GetClanIds() {
            Bind();
            return getClanIds != null ? getClanIds() : new List<long>();
        }

        /// <summary>Whether a clan of this id exists.</summary>
        public static bool ClanExists(long clanId) {
            Bind();
            return clanExists != null && clanExists(clanId);
        }

        /// <summary>The clan's name, or "" when there is no such clan.</summary>
        public static string GetClanName(long clanId) {
            Bind();
            return getClanName != null ? getClanName(clanId) : "";
        }

        /// <summary>A clan's id by its name, ignoring case and outer spaces; 0 when there is none.</summary>
        public static long FindClanByName(string name) {
            Bind();
            return findClanByName != null ? findClanByName(name) : 0;
        }

        /// <summary>The clan's level, 1 and up; 0 when there is no such clan.</summary>
        public static int GetClanLevel(long clanId) {
            Bind();
            return getClanLevel != null ? getClanLevel(clanId) : 0;
        }

        /// <summary>The highest clan level the server defines; 0 when Clans is not installed.</summary>
        public static int GetMaxClanLevel() {
            Bind();
            return getMaxClanLevel != null ? getMaxClanLevel() : 0;
        }

        /// <summary>How many members the clan has.</summary>
        public static int GetMemberCount(long clanId) {
            Bind();
            return getMemberCount != null ? getMemberCount(clanId) : 0;
        }

        /// <summary>The clan leader's account id, or "" when there is no such clan.</summary>
        public static string GetLeaderId(long clanId) {
            Bind();
            return getLeaderId != null ? getLeaderId(clanId) : "";
        }

        /// <summary>Whether the two clans are fighting an active war.</summary>
        public static bool AreAtWar(long clanA, long clanB) {
            Bind();
            return areAtWar != null && areAtWar(clanA, clanB);
        }

        /// <summary>
        /// {"id","name","description","level","maxLevel","maxMembers","memberCount","recruiting","leaderId","leaderName",
        /// "enemies":[...],"detail","xp","treasury","sharedTabs"}. detail is false when only the summary is known here;
        /// xp, treasury and sharedTabs are then -1. "" when there is no such clan.
        /// </summary>
        public static string GetClanJson(long clanId) {
            Bind();
            return getClanJson != null ? getClanJson(clanId) : "";
        }

        /// <summary>Every clan, as a JSON array in <see cref="GetClanJson"/>'s shape.</summary>
        public static string GetClansJson() {
            Bind();
            return getClansJson != null ? getClansJson() : "[]";
        }

        // -------------------------------------------------------------------------------------------------
        // Membership
        // -------------------------------------------------------------------------------------------------

        /// <summary>The account's clan id; 0 when in no clan, or (on a client) neither online nor in the local player's clan.</summary>
        public static long GetClanIdOf(string accountId) {
            Bind();
            return getClanIdOf != null ? getClanIdOf(accountId) : 0;
        }

        /// <summary>The player's clan id, or 0.</summary>
        public static long GetClanIdOfPlayer(Player player) {
            Bind();
            return getClanIdOfPlayer != null ? getClanIdOfPlayer(player) : 0;
        }

        /// <summary>Whether the account is in a clan.</summary>
        public static bool IsInClan(string accountId) {
            Bind();
            return isInClan != null && isInClan(accountId);
        }

        /// <summary>Both accounts are in the same clan. False when either is in none.</summary>
        public static bool AreClanmates(string accountA, string accountB) {
            Bind();
            return areClanmates != null && areClanmates(accountA, accountB);
        }

        /// <summary>The clan's members. A client knows another clan's online members only.</summary>
        public static List<string> GetMemberIds(long clanId) {
            Bind();
            return getMemberIds != null ? getMemberIds(clanId) : new List<string>();
        }

        /// <summary>The clan's members who are online.</summary>
        public static List<string> GetOnlineMemberIds(long clanId) {
            Bind();
            return getOnlineMemberIds != null ? getOnlineMemberIds(clanId) : new List<string>();
        }

        /// <summary>
        /// The members of <see cref="GetMemberIds"/>: [{"id","name","rankId","rankIndex","rank","leader","online",
        /// "joinedUtc","lastSeenUtc"}]. Times are Unix seconds, 0 when not known here.
        /// </summary>
        public static string GetMembersJson(long clanId) {
            Bind();
            return getMembersJson != null ? getMembersJson(clanId) : "[]";
        }

        // -------------------------------------------------------------------------------------------------
        // Ranks and rights
        // -------------------------------------------------------------------------------------------------

        /// <summary>
        /// The member's position in their clan's ranks: 0 is the leader, higher is lower. -1 when in no clan, or (on
        /// a client) neither online nor in the local player's clan.
        /// </summary>
        public static int GetRankIndex(string accountId) {
            Bind();
            return getRankIndex != null ? getRankIndex(accountId) : -1;
        }

        /// <summary>The member's rank id, which stays the same through renames and reordering; 0 when unknown.</summary>
        public static int GetRankId(string accountId) {
            Bind();
            return getRankId != null ? getRankId(accountId) : 0;
        }

        /// <summary>The member's rank name in this machine's language; "" when unknown.</summary>
        public static string GetRankName(string accountId) {
            Bind();
            return getRankName != null ? getRankName(accountId) : "";
        }

        /// <summary>Whether the account leads its clan.</summary>
        public static bool IsLeader(string accountId) {
            Bind();
            return isLeader != null && isLeader(accountId);
        }

        /// <summary>
        /// Whether the member's rank allows it; permission is one of the Permission* names, ignoring case. A client
        /// knows this for the local player's clan only, and answers false for anyone else.
        /// </summary>
        public static bool HasPermission(string accountId, string permission) {
            Bind();
            return hasPermission != null && hasPermission(accountId, permission);
        }

        /// <summary>The rights a rank can hold.</summary>
        public static List<string> GetPermissionNames() {
            Bind();
            return getPermissionNames != null ? getPermissionNames() : new List<string>();
        }

        /// <summary>
        /// The clan's ranks, top to bottom: [{"id","index","name","storedName","leader","entry","permissions":[...]}].
        /// "[]" when not known here (a client has only its own clan's).
        /// </summary>
        public static string GetRanksJson(long clanId) {
            Bind();
            return getRanksJson != null ? getRanksJson(clanId) : "[]";
        }

        // -------------------------------------------------------------------------------------------------
        // Parties and raids
        // -------------------------------------------------------------------------------------------------

        /// <summary>The account's party id; 0 when in no party, or unknown here. Parties last while the server runs.</summary>
        public static long GetPartyId(string accountId) {
            Bind();
            return getPartyId != null ? getPartyId(accountId) : 0;
        }

        /// <summary>Both accounts are in the same party. False when either is in none.</summary>
        public static bool ArePartymates(string accountA, string accountB) {
            Bind();
            return arePartymates != null && arePartymates(accountA, accountB);
        }

        /// <summary>The party leader's account id, or "" when unknown.</summary>
        public static string GetPartyLeaderId(long partyId) {
            Bind();
            return getPartyLeaderId != null ? getPartyLeaderId(partyId) : "";
        }

        /// <summary>The party's members. A client knows other parties' online members only.</summary>
        public static List<string> GetPartyMemberIds(long partyId) {
            Bind();
            return getPartyMemberIds != null ? getPartyMemberIds(partyId) : new List<string>();
        }

        /// <summary>{"id","leaderId","raidId","squad","members":[{"id","name","leader","online","clanId"}]}; "" when unknown.</summary>
        public static string GetPartyJson(long partyId) {
            Bind();
            return getPartyJson != null ? getPartyJson(partyId) : "";
        }

        /// <summary>The account's raid id; 0 when in no raid, or unknown here.</summary>
        public static long GetRaidId(string accountId) {
            Bind();
            return getRaidId != null ? getRaidId(accountId) : 0;
        }

        /// <summary>The account's squad number in its raid, 1 and up; 0 outside a raid.</summary>
        public static int GetSquad(string accountId) {
            Bind();
            return getSquad != null ? getSquad(accountId) : 0;
        }

        /// <summary>Both accounts are in the same raid, in any squads. False when either is in none.</summary>
        public static bool AreRaidmates(string accountA, string accountB) {
            Bind();
            return areRaidmates != null && areRaidmates(accountA, accountB);
        }

        /// <summary>The raid leader's account id; "" when unknown (on a client: another raid whose leader is offline).</summary>
        public static string GetRaidLeaderId(long raidId) {
            Bind();
            return getRaidLeaderId != null ? getRaidLeaderId(raidId) : "";
        }

        /// <summary>Every member of every squad of the raid.</summary>
        public static List<string> GetRaidMemberIds(long raidId) {
            Bind();
            return getRaidMemberIds != null ? getRaidMemberIds(raidId) : new List<string>();
        }

        /// <summary>{"id","leaderId","squads":[<see cref="GetPartyJson"/>'s shape, in squad order]}; "" when unknown.</summary>
        public static string GetRaidJson(long raidId) {
            Bind();
            return getRaidJson != null ? getRaidJson(raidId) : "";
        }

        // -------------------------------------------------------------------------------------------------
        // Listeners
        // -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Called after anything these endpoints answer may have changed: on a client when the server's updates
        /// arrive, on the server at most twice a second after changes. Re-query what you show.
        /// </summary>
        public static void AddChangedListener(Action listener) {
            Bind();
            addChangedListener?.Invoke(listener);
        }

        /// <summary>Removes a listener added with <see cref="AddChangedListener"/>.</summary>
        public static void RemoveChangedListener(Action listener) {
            Bind();
            removeChangedListener?.Invoke(listener);
        }

        /// <summary>
        /// Server only: called with one JSON object per change, right after it is made. Each has "event" (one of
        /// the Event* names) and the id of what changed ("clan", "party" or "raid"), plus the fields each Event*
        /// constant lists.
        /// </summary>
        public static void AddServerEventListener(Action<string> listener) {
            Bind();
            addServerEventListener?.Invoke(listener);
        }

        /// <summary>Removes a listener added with <see cref="AddServerEventListener"/>.</summary>
        public static void RemoveServerEventListener(Action<string> listener) {
            Bind();
            removeServerEventListener?.Invoke(listener);
        }

        // -------------------------------------------------------------------------------------------------
        // Treasury
        // -------------------------------------------------------------------------------------------------

        /// <summary>Coins in the clan's treasury; -1 when not known here (a client has only its own clan's).</summary>
        public static long GetTreasury(long clanId) {
            Bind();
            return getTreasury != null ? getTreasury(clanId) : -1;
        }

        /// <summary>
        /// Server only. Puts coins into the treasury. reason, when not empty, goes into the clan's log (a $token in
        /// it is translated for each reader). False on a client, for no such clan, or coins below 1.
        /// </summary>
        public static bool AddTreasury(long clanId, long coins, string reason) {
            Bind();
            return addTreasury != null && addTreasury(clanId, coins, reason);
        }

        /// <summary>Server only. Takes coins from the treasury, all or none: false when it holds fewer.</summary>
        public static bool TakeTreasury(long clanId, long coins, string reason) {
            Bind();
            return takeTreasury != null && takeTreasury(clanId, coins, reason);
        }

        // -------------------------------------------------------------------------------------------------
        // Storage (the clan's shared tabs)
        // -------------------------------------------------------------------------------------------------

        /// <summary>Shared tabs the clan has bought; -1 when not known here.</summary>
        public static int GetSharedTabCount(long clanId) {
            Bind();
            return getSharedTabCount != null ? getSharedTabCount(clanId) : -1;
        }

        /// <summary>
        /// Server only. {"clan":3,"tabs":[{"index":0,"openBy":"","items":[{"prefab":"Wood","stack":50,"quality":1,
        /// "x":0,"y":0,"custom":false}]}]}; "" on a client or for no such clan.
        /// </summary>
        public static string GetStorageJson(long clanId) {
            Bind();
            return getStorageJson != null ? getStorageJson(clanId) : "";
        }

        /// <summary>Server only. How many of the item the shared tabs hold, whatever their quality; -1 on a client or for no such clan.</summary>
        public static int CountStorageItem(long clanId, string prefab) {
            Bind();
            return countStorageItem != null ? countStorageItem(clanId, prefab) : -1;
        }

        /// <summary>
        /// Client. Asks the server what the local player's clan's shared tabs hold, if their rank may deposit or
        /// withdraw. callback runs once: (true, json in <see cref="GetStorageJson"/>'s shape) or (false, an Error*
        /// code). Without Clans it runs at once with <see cref="ErrorNotInstalled"/>.
        /// </summary>
        public static void RequestStorageJson(Action<bool, string> callback) {
            Bind();
            if (requestStorageJson == null) {
                callback?.Invoke(false, ErrorNotInstalled);
                return;
            }
            requestStorageJson(callback);
        }

        /// <summary>
        /// Server only. Takes items from the shared tabs, all of them or none, by prefab name whatever their quality:
        /// itemsJson is {"Wood":10,"Stone":5} (see <see cref="ItemsToJson"/>). On false, error is an Error* code.
        /// </summary>
        public static bool TakeStorageItems(long clanId, string itemsJson, string reason, out string error) {
            Bind();
            return Edit(takeStorageItems, clanId, itemsJson, reason, out error);
        }

        /// <summary>Server only. Takes one kind of item; see <see cref="TakeStorageItems(long, string, string, out string)"/>.</summary>
        public static bool TakeStorageItem(long clanId, string prefab, int amount, string reason, out string error) {
            return TakeStorageItems(clanId, ItemsToJson(new Dictionary<string, int> { [prefab ?? ""] = amount }), reason, out error);
        }

        /// <summary>
        /// Server only. Puts new items (quality 1) into the shared tabs, all of them or none: itemsJson is
        /// {"Wood":10,"Stone":5}. They top up stacks of the same item first, then fill empty cells. On false, error
        /// is an Error* code.
        /// </summary>
        public static bool AddStorageItems(long clanId, string itemsJson, string reason, out string error) {
            Bind();
            return Edit(addStorageItems, clanId, itemsJson, reason, out error);
        }

        /// <summary>Server only. Puts new items of one kind into the shared tabs; see <see cref="AddStorageItems(long, string, string, out string)"/>.</summary>
        public static bool AddStorageItem(long clanId, string prefab, int amount, string reason, out string error) {
            return AddStorageItems(clanId, ItemsToJson(new Dictionary<string, int> { [prefab ?? ""] = amount }), reason, out error);
        }

        /// <summary>
        /// Server only. Puts a copy of the item as it is (quality, durability, custom data such as enchantments) into
        /// the shared tabs, split into stacks if it is larger than the item's maximum. The item needs its prefab
        /// (m_dropPrefab). On false, error is an Error* code.
        /// </summary>
        public static bool AddStorageItem(long clanId, ItemDrop.ItemData item, string reason, out string error) {
            Bind();
            error = ErrorNotInstalled;
            if (addStorageItem == null) { return false; }
            string why = "";
            bool done = addStorageItem(clanId, item, reason, ref why);
            error = why;
            return done;
        }

        /// <summary>Formats item amounts the way the storage endpoints take them: {"Wood":10,"Stone":5}.</summary>
        public static string ItemsToJson(IDictionary<string, int> items) {
            StringBuilder json = new StringBuilder("{");
            foreach (KeyValuePair<string, int> item in items ?? new Dictionary<string, int>()) {
                if (json.Length > 1) { json.Append(','); }
                json.Append('"').Append((item.Key ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\":");
                json.Append(item.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return json.Append('}').ToString();
        }

        private static bool Edit(StorageEditFn endpoint, long clanId, string itemsJson, string reason, out string error) {
            error = ErrorNotInstalled;
            if (endpoint == null) { return false; }
            string why = "";
            bool done = endpoint(clanId, itemsJson, reason, ref why);
            error = why;
            return done;
        }

        // Once. Declare the soft dependency: without it your plugin may load before Clans, the lookup finds
        // nothing, and every call stays a no-op for the session.
        private static void Bind() {
            if (bound) { return; }
            bound = true;

            apiType = Type.GetType(ApiTypeName, throwOnError: false);
            if (apiType == null) { return; }

            getApiVersion = Get<Func<int>>("GetApiVersion");
            getPluginVersion = Get<Func<string>>("GetPluginVersion");
            hasEndpoint = Get<Func<string, bool>>("HasEndpoint");
            isReady = Get<Func<bool>>("IsReady");
            hasFullData = Get<Func<bool>>("HasFullData");
            getLocalAccountId = Get<Func<string>>("GetLocalAccountId");
            getAccountId = Get<Func<Player, string>>("GetAccountId");
            getAccountIdOfPeer = Get<Func<long, string>>("GetAccountIdOfPeer");
            getAccountIdOfPlayerId = Get<Func<long, string>>("GetAccountIdOfPlayerId");
            getPlayerName = Get<Func<string, string>>("GetPlayerName");
            isOnline = Get<Func<string, bool>>("IsOnline");
            getClanIds = Get<Func<List<long>>>("GetClanIds");
            clanExists = Get<Func<long, bool>>("ClanExists");
            getClanName = Get<Func<long, string>>("GetClanName");
            findClanByName = Get<Func<string, long>>("FindClanByName");
            getClanLevel = Get<Func<long, int>>("GetClanLevel");
            getMaxClanLevel = Get<Func<int>>("GetMaxClanLevel");
            getMemberCount = Get<Func<long, int>>("GetMemberCount");
            getLeaderId = Get<Func<long, string>>("GetLeaderId");
            areAtWar = Get<Func<long, long, bool>>("AreAtWar");
            getClanJson = Get<Func<long, string>>("GetClanJson");
            getClansJson = Get<Func<string>>("GetClansJson");
            getClanIdOf = Get<Func<string, long>>("GetClanIdOf");
            getClanIdOfPlayer = Get<Func<Player, long>>("GetClanIdOfPlayer");
            isInClan = Get<Func<string, bool>>("IsInClan");
            areClanmates = Get<Func<string, string, bool>>("AreClanmates");
            getMemberIds = Get<Func<long, List<string>>>("GetMemberIds");
            getOnlineMemberIds = Get<Func<long, List<string>>>("GetOnlineMemberIds");
            getMembersJson = Get<Func<long, string>>("GetMembersJson");
            getRankIndex = Get<Func<string, int>>("GetRankIndex");
            getRankId = Get<Func<string, int>>("GetRankId");
            getRankName = Get<Func<string, string>>("GetRankName");
            isLeader = Get<Func<string, bool>>("IsLeader");
            hasPermission = Get<Func<string, string, bool>>("HasPermission");
            getPermissionNames = Get<Func<List<string>>>("GetPermissionNames");
            getRanksJson = Get<Func<long, string>>("GetRanksJson");
            getPartyId = Get<Func<string, long>>("GetPartyId");
            arePartymates = Get<Func<string, string, bool>>("ArePartymates");
            getPartyLeaderId = Get<Func<long, string>>("GetPartyLeaderId");
            getPartyMemberIds = Get<Func<long, List<string>>>("GetPartyMemberIds");
            getPartyJson = Get<Func<long, string>>("GetPartyJson");
            getRaidId = Get<Func<string, long>>("GetRaidId");
            getSquad = Get<Func<string, int>>("GetSquad");
            areRaidmates = Get<Func<string, string, bool>>("AreRaidmates");
            getRaidLeaderId = Get<Func<long, string>>("GetRaidLeaderId");
            getRaidMemberIds = Get<Func<long, List<string>>>("GetRaidMemberIds");
            getRaidJson = Get<Func<long, string>>("GetRaidJson");
            addChangedListener = Get<Action<Action>>("AddChangedListener");
            removeChangedListener = Get<Action<Action>>("RemoveChangedListener");
            addServerEventListener = Get<Action<Action<string>>>("AddServerEventListener");
            removeServerEventListener = Get<Action<Action<string>>>("RemoveServerEventListener");
            getTreasury = Get<Func<long, long>>("GetTreasury");
            addTreasury = Get<Func<long, long, string, bool>>("AddTreasury");
            takeTreasury = Get<Func<long, long, string, bool>>("TakeTreasury");
            getSharedTabCount = Get<Func<long, int>>("GetSharedTabCount");
            getStorageJson = Get<Func<long, string>>("GetStorageJson");
            countStorageItem = Get<Func<long, string, int>>("CountStorageItem");
            requestStorageJson = Get<Action<Action<bool, string>>>("RequestStorageJson");
            takeStorageItems = Get<StorageEditFn>("TakeStorageItems");
            addStorageItems = Get<StorageEditFn>("AddStorageItems");
            addStorageItem = Get<StorageItemFn>("AddStorageItem");
        }

        // Matches the delegate's exact signature, so an endpoint an older Clans lacks (or a changed one) stays
        // null and its wrapper keeps returning the default instead of throwing.
        private static TDelegate Get<TDelegate>(string name) where TDelegate : class {
            MethodInfo invoke = typeof(TDelegate).GetMethod("Invoke");
            Type[] parameters = invoke.GetParameters().Select(p => p.ParameterType).ToArray();
            MethodInfo method = apiType.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
            if (method == null || method.ReturnType != invoke.ReturnType) { return null; }
            return Delegate.CreateDelegate(typeof(TDelegate), method, throwOnBindFailure: false) as TDelegate;
        }
    }
}
