using System;
using System.Collections.Generic;

namespace Clans {
    // What Clans.API forwards to: one member per endpoint, of the same name and signature, so the public class holds
    // no logic at all. What each returns is documented once, on the endpoint in API.cs.
    //
    // ApiReceiver.Current is the receiver for this machine: ServerReceiver on the server (dedicated, listen host or
    // single player) once the world's clans are loaded, ClientReceiver everywhere else.
    internal interface IApiReceiver {
        // status
        bool IsReady();
        bool HasFullData();

        // players and accounts
        string GetLocalAccountId();
        string GetAccountId(Player player);
        string GetAccountIdOfPeer(long peerId);
        string GetAccountIdOfPlayerId(long playerId);
        string GetPlayerName(string accountId);
        bool IsOnline(string accountId);

        // clans
        List<long> GetClanIds();
        bool ClanExists(long clanId);
        string GetClanName(long clanId);
        long FindClanByName(string name);
        int GetClanLevel(long clanId);
        int GetMaxClanLevel();
        int GetMemberCount(long clanId);
        string GetLeaderId(long clanId);
        bool AreAtWar(long clanA, long clanB);
        string GetClanJson(long clanId);
        string GetClansJson();

        // membership
        long GetClanIdOf(string accountId);
        long GetClanIdOfPlayer(Player player);
        bool IsInClan(string accountId);
        bool AreClanmates(string accountA, string accountB);
        List<string> GetMemberIds(long clanId);
        List<string> GetOnlineMemberIds(long clanId);
        string GetMembersJson(long clanId);

        // ranks and rights
        int GetRankIndex(string accountId);
        int GetRankId(string accountId);
        string GetRankName(string accountId);
        bool IsLeader(string accountId);
        bool HasPermission(string accountId, string permission);
        List<string> GetPermissionNames();
        string GetRanksJson(long clanId);

        // parties and raids
        long GetPartyId(string accountId);
        bool ArePartymates(string accountA, string accountB);
        string GetPartyLeaderId(long partyId);
        List<string> GetPartyMemberIds(long partyId);
        string GetPartyJson(long partyId);
        long GetRaidId(string accountId);
        int GetSquad(string accountId);
        bool AreRaidmates(string accountA, string accountB);
        string GetRaidLeaderId(long raidId);
        List<string> GetRaidMemberIds(long raidId);
        string GetRaidJson(long raidId);

        // treasury
        long GetTreasury(long clanId);
        bool AddTreasury(long clanId, long coins, string reason);
        bool TakeTreasury(long clanId, long coins, string reason);

        // storage
        int GetSharedTabCount(long clanId);
        string GetStorageJson(long clanId);
        int CountStorageItem(long clanId, string prefab);
        void RequestStorageJson(Action<bool, string> callback);
        bool TakeStorageItems(long clanId, string itemsJson, string reason, out string error);
        bool AddStorageItems(long clanId, string itemsJson, string reason, out string error);
        bool AddStorageItem(long clanId, ItemDrop.ItemData item, string reason, out string error);

        // listeners
        void AddChangedListener(Action listener);
        void RemoveChangedListener(Action listener);
        void AddServerEventListener(Action<string> listener);
        void RemoveServerEventListener(Action<string> listener);
    }
}
