using Clans.Core;
using Clans.Server.Model;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Clans.Server {
    // Lookups and rules over the saved state that more than one service needs.
    internal static class ClanRules {
        internal static ClansWorldState State => ClanStore.State;

        internal static AccountRecord Account(string account, bool create = false) {
            if (State == null || string.IsNullOrEmpty(account)) { return null; }
            if (State.Accounts.TryGetValue(account, out AccountRecord record)) { return record; }
            if (create == false) { return null; }
            record = new AccountRecord { Account = account };
            State.Accounts[account] = record;
            return record;
        }

        internal static ClanRecord Clan(long id) {
            if (State == null || id == 0) { return null; }
            State.Clans.TryGetValue(id, out ClanRecord clan);
            return clan;
        }

        internal static ClanRecord ClanOf(string account) {
            AccountRecord record = Account(account);
            return record == null ? null : Clan(record.ClanId);
        }

        internal static ClanRecord ClanByName(string name) {
            if (State == null || string.IsNullOrEmpty(name)) { return null; }
            string key = NameKey(name);
            return State.Clans.Values.FirstOrDefault(c => NameKey(c.Name) == key);
        }

        internal static MemberRecord Member(ClanRecord clan, string account) {
            if (clan == null || account == null) { return null; }
            clan.Members.TryGetValue(account, out MemberRecord member);
            return member;
        }

        // Within the levels levels.yaml has now, which may be fewer than when the clan bought its level.
        internal static int Level(ClanRecord clan) => Math.Max(1, Math.Min(clan.Level, ClansSettings.Levels.MaxLevel));

        // What the clan's level gives it.
        internal static ClanLevelDef Perks(ClanRecord clan) => ClansSettings.Levels.Get(Level(clan));

        // The level the clan can upgrade to next, null at the top.
        internal static ClanLevelDef NextLevel(ClanRecord clan) {
            int level = Level(clan);
            return level < ClansSettings.Levels.MaxLevel ? ClansSettings.Levels.Get(level + 1) : null;
        }

        internal static int MaxMembers(ClanRecord clan) => Perks(clan).MaxMembers;

        internal static RankDef LeaderRank(ClanRecord clan) => clan.Ranks.Count > 0 ? clan.Ranks[0] : null;
        internal static RankDef EntryRank(ClanRecord clan) => clan.Ranks.Count > 0 ? clan.Ranks[clan.Ranks.Count - 1] : null;

        // Position in the hierarchy: 0 is the leader, larger is lower. A rank that no longer exists counts as
        // the entry rank, so a broken record never outranks anyone.
        internal static int RankIndex(ClanRecord clan, int rankId) {
            int index = clan.Ranks.FindIndex(r => r.Id == rankId);
            return index < 0 ? clan.Ranks.Count - 1 : index;
        }

        internal static bool IsLeader(ClanRecord clan, string account) {
            MemberRecord member = Member(clan, account);
            return member != null && RankIndex(clan, member.RankId) == 0;
        }

        internal static MemberRecord Leader(ClanRecord clan) {
            return clan.Members.Values.FirstOrDefault(m => RankIndex(clan, m.RankId) == 0);
        }

        internal static ClanPermission Permissions(ClanRecord clan, MemberRecord member) {
            if (clan == null || member == null) { return ClanPermission.None; }
            int index = RankIndex(clan, member.RankId);
            return ClanPermissions.Effective(index, index > 0 ? clan.Ranks[index].Permissions : ClanPermission.None);
        }

        internal static bool Can(ClanRecord clan, string account, ClanPermission permission) {
            return Permissions(clan, Member(clan, account)).Has(permission);
        }

        // Whether actor sits strictly above target in the clan's ranks.
        internal static bool Outranks(ClanRecord clan, string actor, string target) {
            MemberRecord a = Member(clan, actor);
            MemberRecord t = Member(clan, target);
            if (a == null || t == null) { return false; }
            return RankIndex(clan, a.RankId) < RankIndex(clan, t.RankId);
        }

        // ---------------------------------------------------------------- names

        internal static string NameKey(string name) => (name ?? "").Trim().ToLowerInvariant();

        internal static string CleanText(string text, int maxLength) {
            if (text == null) { return ""; }
            // Strip anything that would be read as rich text markup or break a line in a list.
            string cleaned = new string(text.Where(c => c != '<' && c != '>' && c != '\n' && c != '\r' && c != '\t').ToArray()).Trim();
            return cleaned.Length > maxLength ? cleaned.Substring(0, maxLength) : cleaned;
        }

        internal static string CleanMultiline(string text, int maxLength) {
            if (text == null) { return ""; }
            string cleaned = new string(text.Where(c => c != '<' && c != '>' && c != '\r' && c != '\t').ToArray()).Trim();
            return cleaned.Length > maxLength ? cleaned.Substring(0, maxLength) : cleaned;
        }

        // Returns an error token, or null when the name is acceptable.
        internal static string CheckName(string name, long exceptClanId = 0) {
            int min = ClansConfig.NameMinLength.Value;
            int max = Math.Max(min, ClansConfig.NameMaxLength.Value);
            if (string.IsNullOrEmpty(name) || name.Length < min || name.Length > max) { return "$clans_err_name_length"; }
            if (name.Any(c => char.IsLetterOrDigit(c) == false && c != ' ' && c != '-' && c != '_' && c != '\'')) {
                return "$clans_err_name_chars";
            }
            ClanRecord existing = ClanByName(name);
            if (existing != null && existing.Id != exceptClanId) { return "$clans_err_name_taken"; }
            return null;
        }

        // ---------------------------------------------------------------- log

        internal static void Log(ClanRecord clan, string token, params string[] args) {
            clan.Log.Add(new LogEntry { Utc = ClanStore.Now, Token = token, Args = new List<string>(args ?? new string[0]) });
            int keep = ClansConfig.LogEntriesKept.Value;
            if (clan.Log.Count > keep) { clan.Log.RemoveRange(0, clan.Log.Count - keep); }
            Broadcaster.MarkDetail(clan.Id);
            ClanStore.MarkDirty();
        }

        // ---------------------------------------------------------------- experience

        // Experience never raises the level by itself (LevelService does, once the resources are in too); the log
        // says when the clan first has enough for its next one.
        internal static void AddXp(ClanRecord clan, long amount, string reasonToken) {
            if (amount <= 0) { return; }
            long before = clan.Xp;
            clan.Xp += amount;
            Log(clan, "$clans_log_xp", amount.ToString(), reasonToken ?? "");
            ClanLevelDef next = NextLevel(clan);
            if (next != null && before < next.XpRequired && clan.Xp >= next.XpRequired) {
                Log(clan, "$clans_log_level_ready", (Level(clan) + 1).ToString());
            }
            Broadcaster.MarkDetail(clan.Id);
            ClanStore.MarkDirty();
        }

        internal static void AddTreasury(ClanRecord clan, long coins) {
            long before = clan.Treasury;
            clan.Treasury = Math.Max(0, clan.Treasury + coins);
            Broadcaster.MarkDetail(clan.Id);
            ClanStore.MarkDirtySoon();
            if (clan.Treasury != before) { ApiEvents.TreasuryChanged(clan, clan.Treasury - before); }
        }
    }
}
