using Clans.Client;
using Clans.Net;
using Clans.Server;
using Clans.Server.Model;
using System;
using System.Collections.Generic;

namespace Clans {
    // The listeners other mods add through Clans.API.
    //
    // Changed listeners carry nothing: they run after anything the API answers may have changed. On a client that
    // is whenever the server's updates arrive; on the server, when its updates to clients go out (at most twice a
    // second), so a burst of changes is one call.
    //
    // Server event listeners get one JSON object per change, right after it is made, on the server only. The
    // JSON is built only while someone listens.
    internal static class ApiEvents {
        private static readonly List<Action> changed = new List<Action>();
        private static readonly List<Action<string>> serverEvents = new List<Action<string>>();

        internal static void Init() {
            ClanClient.DirectoryChanged += RaiseChanged;
            ClanClient.RosterChanged += RaiseChanged;
            ClanClient.AccountChanged += RaiseChanged;
            ClanClient.DetailChanged += RaiseChanged;
            PartyClient.Changed += RaiseChanged;
        }

        internal static void AddChanged(Action listener) {
            if (listener != null && changed.Contains(listener) == false) { changed.Add(listener); }
        }

        internal static void RemoveChanged(Action listener) => changed.Remove(listener);

        internal static void AddServer(Action<string> listener) {
            if (listener != null && serverEvents.Contains(listener) == false) { serverEvents.Add(listener); }
        }

        internal static void RemoveServer(Action<string> listener) => serverEvents.Remove(listener);

        internal static void RaiseChanged() {
            if (changed.Count == 0) { return; }
            // A copy: a listener may remove itself.
            foreach (Action listener in changed.ToArray()) {
                try {
                    listener();
                } catch (Exception e) {
                    Logger.LogError($"A Clans API changed listener failed: {e}");
                }
            }
        }

        // ---------------------------------------------------------------- server events

        internal static void ClanFounded(ClanRecord clan, string account, string name) {
            if (serverEvents.Count == 0) { return; }
            Send(Start("clanFounded", clan).Field("name", clan.Name).Field("account", account).Field("playerName", name));
        }

        internal static void ClanDisbanded(ClanRecord clan) {
            if (serverEvents.Count == 0) { return; }
            Send(Start("clanDisbanded", clan).Field("name", clan.Name));
        }

        internal static void MemberJoined(ClanRecord clan, string account) {
            if (serverEvents.Count == 0) { return; }
            Send(Rank(Start("memberJoined", clan), clan, account));
        }

        // reason: "left", "kicked" or "disbanded".
        internal static void MemberLeft(ClanRecord clan, string account, string name, string reason) {
            if (serverEvents.Count == 0) { return; }
            Send(Start("memberLeft", clan).Field("account", account).Field("playerName", name ?? "").Field("reason", reason));
        }

        internal static void RankChanged(ClanRecord clan, string account) {
            if (serverEvents.Count == 0) { return; }
            Send(Rank(Start("rankChanged", clan), clan, account));
        }

        // The rank list was edited: names, rights or order. Members moved off a removed rank also get RankChanged.
        internal static void RanksChanged(ClanRecord clan) {
            if (serverEvents.Count == 0) { return; }
            Send(Start("ranksChanged", clan));
        }

        internal static void LevelChanged(ClanRecord clan, int previous) {
            if (serverEvents.Count == 0) { return; }
            Send(Start("levelChanged", clan).Field("level", ClanRules.Level(clan)).Field("previous", previous));
        }

        internal static void TreasuryChanged(ClanRecord clan, long change) {
            if (serverEvents.Count == 0) { return; }
            Send(Start("treasuryChanged", clan).Field("treasury", clan.Treasury).Field("change", change));
        }

        // A shared tab's contents changed, or (bought) it is new.
        internal static void StorageChanged(ClanRecord clan, int tab) {
            if (serverEvents.Count == 0) { return; }
            Send(Start("storageChanged", clan).Field("tab", tab));
        }

        // ---------------------------------------------------------------- party and raid events

        // Also for a new party, whose leader joins it first.
        internal static void PartyJoined(long partyId, string account) {
            if (serverEvents.Count == 0) { return; }
            Send(Party("partyJoined", partyId).Field("account", account).Field("playerName", NameOf(account)));
        }

        // reason: "left", "kicked", "timedOut" (offline past the grace period) or "moved" (to another squad of the raid).
        internal static void PartyLeft(long partyId, string account, string reason) {
            if (serverEvents.Count == 0) { return; }
            Send(Party("partyLeft", partyId).Field("account", account).Field("playerName", NameOf(account)).Field("reason", reason));
        }

        internal static void PartyLeaderChanged(long partyId, string account) {
            if (serverEvents.Count == 0) { return; }
            Send(Party("partyLeaderChanged", partyId).Field("account", account).Field("playerName", NameOf(account)));
        }

        // Its last member left.
        internal static void PartyDisbanded(long partyId) {
            if (serverEvents.Count == 0) { return; }
            Send(Party("partyDisbanded", partyId));
        }

        // A party became a squad of the raid; a new raid sends this for its first squad, then its second.
        internal static void RaidJoined(long raidId, long partyId, int squad) {
            if (serverEvents.Count == 0) { return; }
            Send(Raid("raidJoined", raidId).Field("party", partyId).Field("squad", squad));
        }

        internal static void RaidLeft(long raidId, long partyId) {
            if (serverEvents.Count == 0) { return; }
            Send(Raid("raidLeft", raidId).Field("party", partyId));
        }

        internal static void RaidLeaderChanged(long raidId, string account) {
            if (serverEvents.Count == 0) { return; }
            Send(Raid("raidLeaderChanged", raidId).Field("account", account).Field("playerName", NameOf(account)));
        }

        // Down to one squad, which is a plain party again (after a raidLeft for each squad).
        internal static void RaidDisbanded(long raidId) {
            if (serverEvents.Count == 0) { return; }
            Send(Raid("raidDisbanded", raidId));
        }

        // ---------------------------------------------------------------- building events

        private static JsonOut Start(string name, ClanRecord clan) => new JsonOut().Object().Field("event", name).Field("clan", clan.Id);

        private static JsonOut Party(string name, long partyId) => new JsonOut().Object().Field("event", name).Field("party", partyId);

        private static JsonOut Raid(string name, long raidId) => new JsonOut().Object().Field("event", name).Field("raid", raidId);

        private static string NameOf(string account) {
            ClanSender sender = Sessions.Get(account);
            return sender != null ? sender.CharacterName : ClanRules.Account(account)?.LastCharacterName ?? "";
        }

        private static JsonOut Rank(JsonOut json, ClanRecord clan, string account) {
            MemberRecord member = ClanRules.Member(clan, account);
            int index = member == null ? -1 : ClanRules.RankIndex(clan, member.RankId);
            return json.Field("account", account)
                .Field("playerName", member?.LastCharacterName ?? "")
                .Field("rankId", member?.RankId ?? 0)
                .Field("rankIndex", index)
                .Field("rank", index >= 0 ? ApiReceiver.Localize(clan.Ranks[index].Name) : "");
        }

        private static void Send(JsonOut json) {
            string text = json.End().ToString();
            foreach (Action<string> listener in serverEvents.ToArray()) {
                try {
                    listener(text);
                } catch (Exception e) {
                    Logger.LogError($"A Clans API event listener failed: {e}");
                }
            }
        }
    }
}
