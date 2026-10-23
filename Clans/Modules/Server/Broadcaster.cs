using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System.Collections.Generic;
using System.Linq;

namespace Clans.Server {
    // Pushes state to clients. Changes only mark what is stale; the pushes go out together a moment later,
    // so a burst of changes (a disband touching every member) costs one message per player, not dozens.
    internal static class Broadcaster {
        private const float FlushSeconds = 0.5f;
        private static float timer;

        private static bool directoryDirty;
        private static bool rosterDirty;
        private static readonly HashSet<long> detailDirty = new HashSet<long>();
        private static readonly HashSet<string> accountDirty = new HashSet<string>();

        private static int directoryVersion;
        private static int rosterVersion;

        internal static void MarkDirectory() => directoryDirty = true;
        internal static void MarkRoster() => rosterDirty = true;
        internal static void MarkDetail(long clanId) { if (clanId != 0) { detailDirty.Add(clanId); } }
        internal static void MarkAccount(string account) { if (string.IsNullOrEmpty(account) == false) { accountDirty.Add(account); } }

        // A member's standing changed: their own view, their clan's view, and how others see them.
        internal static void MarkMember(long clanId, string account) {
            MarkDetail(clanId);
            MarkAccount(account);
            MarkRoster();
            MarkDirectory();
        }

        internal static void MarkAll() {
            directoryDirty = true;
            rosterDirty = true;
            foreach (ClanSender sender in Sessions.All) { accountDirty.Add(sender.Account); }
            if (ClanStore.State != null) {
                foreach (long id in ClanStore.State.Clans.Keys) { detailDirty.Add(id); }
            }
        }

        internal static void Reset() {
            directoryDirty = false;
            rosterDirty = false;
            detailDirty.Clear();
            accountDirty.Clear();
            timer = 0f;
        }

        internal static void Tick(float dt) {
            timer -= dt;
            if (timer > 0f) { return; }
            timer = FlushSeconds;
            Flush();
        }

        internal static void Flush() {
            if (ClanStore.State == null) { return; }
            // Other mods on the server hear about it once per flush too (Clans.API change listeners).
            bool changed = directoryDirty || rosterDirty || accountDirty.Count > 0 || detailDirty.Count > 0;
            if (directoryDirty) {
                directoryDirty = false;
                directoryVersion++;
                ClanNet.Broadcast(Op.DirectoryFull, WriteDirectory);
            }
            if (rosterDirty) {
                rosterDirty = false;
                rosterVersion++;
                ClanNet.Broadcast(Op.RosterFull, WriteRoster);
            }
            if (accountDirty.Count > 0) {
                foreach (string account in accountDirty.ToList()) {
                    ClanSender sender = Sessions.Get(account);
                    if (sender != null) { SendMyAccount(sender); }
                }
                accountDirty.Clear();
            }
            ActivityService.Flush();
            if (detailDirty.Count > 0) {
                foreach (long clanId in detailDirty.ToList()) {
                    ClanRecord clan = ClanRules.Clan(clanId);
                    if (clan == null) { continue; }
                    // The same for every member: built once per clan.
                    List<ClanSender> online = clan.Members.Keys.Select(Sessions.Get).Where(s => s != null).ToList();
                    if (online.Count > 0) { SendDetail(online, clan); }
                }
                detailDirty.Clear();
            }
            if (changed) { ApiEvents.RaiseChanged(); }
        }

        // Everything a player needs after joining.
        internal static void SendEverythingTo(ClanSender sender) {
            ClanNet.Send(sender, Op.DirectoryFull, 0, WriteDirectory);
            ClanNet.Send(sender, Op.RosterFull, 0, WriteRoster);
            SendMyAccount(sender);
            ClanRecord clan = ClanRules.ClanOf(sender.Account);
            if (clan != null) { SendDetail(sender, clan); }
            ActivityService.SendTo(sender);
            // Everyone else needs to see the newcomer.
            rosterDirty = true;
        }

        internal static void SendMyAccount(ClanSender sender) {
            AccountRecord record = ClanRules.Account(sender.Account);
            ClanRecord clan = record == null ? null : ClanRules.Clan(record.ClanId);
            MemberRecord member = ClanRules.Member(clan, sender.Account);
            int rankId = member?.RankId ?? 0;
            Core.ClanPermission permissions = ClanRules.Permissions(clan, member);
            ClanNet.Send(sender, Op.MyAccount, 0, pkg => MyAccountInfo.Write(pkg, sender.Account, record, rankId, permissions));
        }

        internal static void SendDetail(ClanSender sender, ClanRecord clan) => SendDetail(new[] { sender }, clan);

        internal static void SendDetail(IEnumerable<ClanSender> recipients, ClanRecord clan) {
            int level = ClanRules.Level(clan);
            int logEntries = ClansConfig.LogEntriesKept.Value;
            ClanNet.SendToEach(recipients, Op.ClanDetail, pkg => ClanDetailInfo.Write(pkg, clan, level, Sessions.IsOnline, logEntries));
        }

        private static void WriteDirectory(ZPackage pkg) {
            ClansWorldState state = ClanStore.State;
            pkg.Write(directoryVersion);
            pkg.Write(state.Clans.Count);
            foreach (ClanRecord clan in state.Clans.Values) {
                ClanSummary.Write(pkg, clan, ClanRules.Level(clan), ClanRules.Leader(clan));
            }
            List<WarRecord> wars = state.Wars.Where(w => w.State != WarState.Ended).ToList();
            pkg.Write(wars.Count);
            foreach (WarRecord war in wars) { WarSummary.Write(pkg, war); }
        }

        private static void WriteRoster(ZPackage pkg) {
            pkg.Write(rosterVersion);
            List<ClanSender> online = Sessions.All.ToList();
            pkg.Write(online.Count);
            foreach (ClanSender sender in online) {
                AccountRecord record = ClanRules.Account(sender.Account);
                RosterEntry entry = new RosterEntry {
                    Account = sender.Account,
                    Name = sender.CharacterName,
                    ClanId = record?.ClanId ?? 0,
                };
                ClanRecord clan = ClanRules.Clan(entry.ClanId);
                MemberRecord member = ClanRules.Member(clan, sender.Account);
                if (member != null) {
                    entry.RankId = member.RankId;
                    entry.RankIndex = ClanRules.RankIndex(clan, member.RankId);
                    entry.RankName = entry.RankIndex >= 0 ? clan.Ranks[entry.RankIndex].Name ?? "" : "";
                }
                PartyService.FillRoster(entry);
                entry.Write(pkg);
            }
        }
    }
}
