using Clans.Compat;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Clans.Server {
    // Epic Loot bounties shared with the clan.
    //
    // A bounty lives in its owner's character save, so the owner's client reports its open bounties whenever
    // Epic Loot saves a change (while the player shares them). The reports are kept with the account and saved,
    // so clanmates still see an offline member's bounty. The server also hears every bounty kill from Epic
    // Loot, so a bounty cleared while its owner is away leaves everyone's map. Epic Loot itself credits the
    // owner for any kill, whoever lands it, and holds an offline owner's kills until they log in.
    internal static class BountyRelay {
        private const float FlushSeconds = 1f;
        // A member gone this long takes their bounties off the clan's maps.
        private const long StaleSeconds = 14 * 86400L;
        private const long StaleCheckSeconds = 600;

        // Clans whose online members need the list again.
        private static readonly HashSet<long> dirtyClans = new HashSet<long>();
        // Epic Loot bounty id -> the account sharing it, for kills.
        private static readonly Dictionary<string, string> ownerById = new Dictionary<string, string>();
        private static bool listening;
        private static float flushTimer;
        private static long nextStaleCheck;

        internal static bool Enabled => ClansConfig.ClanBounties.Value && EpicLootApi.BountiesAvailable;

        internal static void Register() {
            ClanNet.RegisterServer(Op.BountyReport, OnReport);
            ClansConfig.ClanBounties.SettingChanged += (_, _) => MarkAllClans();
            // Epic Loot raises this on the server only; it lasts as long as the game, so it is added once.
            if (listening == false && EpicLootApi.BountiesAvailable) {
                listening = EpicLootApi.AddBountyTargetSlainListener(OnTargetSlain);
            }
        }

        internal static void Reset() {
            dirtyClans.Clear();
            ownerById.Clear();
            flushTimer = 0f;
            nextStaleCheck = 0;
        }

        internal static void OnStateLoaded() {
            ownerById.Clear();
            foreach (AccountRecord account in ClanStore.State.Accounts.Values) {
                foreach (SharedBountyRecord bounty in account.Bounties) { ownerById[bounty.Id] = account.Account; }
            }
        }

        internal static void Tick(float dt) {
            flushTimer -= dt;
            if (flushTimer > 0f || dirtyClans.Count == 0) { return; }
            flushTimer = FlushSeconds;
            foreach (long clanId in dirtyClans) {
                ClanRecord clan = ClanRules.Clan(clanId);
                if (clan == null) { continue; }
                List<ClanSender> online = clan.Members.Keys.Select(Sessions.Get).Where(s => s != null).ToList();
                if (online.Count == 0) { continue; }
                List<Open> open = OpenBounties(clan);
                ClanNet.SendToEach(online, Op.ClanBounties, pkg => WriteList(pkg, open));
            }
            dirtyClans.Clear();
        }

        internal static void Housekeeping(long now) {
            if (now < nextStaleCheck) { return; }
            nextStaleCheck = now + StaleCheckSeconds;
            foreach (AccountRecord account in ClanStore.State.Accounts.Values) {
                if (account.Bounties.Count == 0 || Sessions.IsOnline(account.Account) || now - account.LastSeenUtc <= StaleSeconds) { continue; }
                Clear(account);
                ClanRecord clan = ClanRules.Clan(account.ClanId);
                if (clan != null) { dirtyClans.Add(clan.Id); }
            }
        }

        // ---------------------------------------------------------------- membership

        // A member who comes online gets their clan's list.
        internal static void OnAccountOnline(ClanSender sender) {
            ClanRecord clan = ClanRules.ClanOf(sender.Account);
            if (clan == null) { return; }
            List<Open> open = OpenBounties(clan);
            ClanNet.Send(sender, Op.ClanBounties, 0, pkg => WriteList(pkg, open));
        }

        // The newcomer needs the clan's bounties, and the clan theirs once their client reports them.
        internal static void OnMemberAdded(ClanRecord clan, string account) {
            dirtyClans.Add(clan.Id);
        }

        // A bounty is shared with a clan, not carried to the next one; the client reports again if it joins one.
        internal static void OnMemberRemoved(ClanRecord clan, string account) {
            AccountRecord record = ClanRules.Account(account);
            if (record != null) { Clear(record); }
            dirtyClans.Add(clan.Id);
            ClanSender session = Sessions.Get(account);
            if (session != null) { ClanNet.Send(session, Op.ClanBounties, 0, pkg => pkg.Write(0)); }
        }

        private static void MarkAllClans() {
            if (ClanStore.State == null) { return; }
            foreach (long id in ClanStore.State.Clans.Keys) { dirtyClans.Add(id); }
        }

        private static void Clear(AccountRecord account) {
            if (account.Bounties.Count == 0) { return; }
            foreach (SharedBountyRecord bounty in account.Bounties) { ownerById.Remove(bounty.Id); }
            account.Bounties.Clear();
            ClanStore.MarkDirty();
        }

        // ---------------------------------------------------------------- reports

        private static void OnReport(ServerRequest request) {
            ZPackage body = request.Body;
            bool announce = body.ReadBool();
            int count = body.ReadInt();
            if (count < 0 || count > BountyLimits.MaxPerAccount) {
                request.Fail("$clans_err_bad_request");
                return;
            }
            List<ReportedBounty> reported = new List<ReportedBounty>();
            for (int i = 0; i < count; i++) { reported.Add(ReportedBounty.Read(body)); }

            ClanSender sender = request.Sender;
            AccountRecord record = ClanRules.Account(sender.Account);
            if (record == null) { return; }
            ClanRecord clan = ClanRules.Clan(record.ClanId);
            // Outside a clan, or with sharing switched off on the server, nothing is kept.
            if (clan == null || Enabled == false) { reported.Clear(); }

            long now = ClanStore.Now;
            Dictionary<string, SharedBountyRecord> before = new Dictionary<string, SharedBountyRecord>();
            foreach (SharedBountyRecord old in record.Bounties) { before[old.Id] = old; }
            List<SharedBountyRecord> after = new List<SharedBountyRecord>();
            List<SharedBountyRecord> shared = new List<SharedBountyRecord>();
            List<SharedBountyRecord> finished = new List<SharedBountyRecord>();
            bool changed = false;
            foreach (ReportedBounty report in reported) {
                SharedBountyRecord bounty = Clean(report, now);
                if (bounty == null || after.Exists(b => b.Id == bounty.Id)) { continue; }
                if (before.TryGetValue(bounty.Id, out SharedBountyRecord old)) {
                    KeepProgress(old, bounty);
                    if (old.Complete == false && bounty.Complete) { finished.Add(bounty); }
                    if (Same(old, bounty) == false) { changed = true; }
                } else {
                    // A bounty already finished when first shared is never shown, so it is not news either.
                    if (bounty.Complete == false) { shared.Add(bounty); }
                    changed = true;
                }
                after.Add(bounty);
            }
            foreach (string id in before.Keys) {
                if (after.Exists(b => b.Id == id)) { continue; }
                // Claimed, abandoned, or no longer shared.
                ownerById.Remove(id);
                changed = true;
            }
            if (changed == false) { return; }

            foreach (SharedBountyRecord bounty in after) { ownerById[bounty.Id] = sender.Account; }
            record.Bounties = after;
            ClanStore.MarkDirty();
            if (clan == null) { return; }
            foreach (SharedBountyRecord bounty in shared) {
                ClanRules.Log(clan, "$clans_log_bounty_shared", sender.CharacterName, bounty.Name);
                if (announce) { ChatService.PostBounty(sender, bounty); }
            }
            foreach (SharedBountyRecord bounty in finished) {
                ClanRules.Log(clan, "$clans_log_bounty_done", sender.CharacterName, bounty.Name);
            }
            dirtyClans.Add(clan.Id);
        }

        // What a report may say, kept to sane values: names are drawn in rich text on every clanmate's map.
        private static SharedBountyRecord Clean(ReportedBounty report, long now) {
            string id = ChatService.Clip(report.Id, BountyLimits.MaxId);
            Vector3 at = report.Position;
            if (string.IsNullOrEmpty(id) || Usable(at.x) == false || Usable(at.y) == false || Usable(at.z) == false) { return null; }
            float radius = float.IsNaN(report.Radius) ? BountyLimits.MinRadius : Mathf.Clamp(report.Radius, BountyLimits.MinRadius, BountyLimits.MaxRadius);
            SharedBountyRecord bounty = new SharedBountyRecord {
                Id = id,
                MonsterId = ChatService.Clip(ChatService.NoMarkup(report.MonsterId), BountyLimits.MaxMonsterId),
                Name = ChatService.Clip(ChatService.NoMarkup(report.Name).Trim(), BountyLimits.MaxName),
                // A Heightmap.Biome name; clients look up "$biome_" + it.
                Biome = ChatService.Clip(new string((report.Biome ?? "").Where(char.IsLetter).ToArray()), BountyLimits.MaxBiome),
                X = at.x,
                Y = at.y,
                Z = at.z,
                Radius = radius,
                Slain = report.Slain,
                Complete = report.Complete,
                SharedUtc = now,
            };
            foreach (ReportedBountyAdd add in report.Adds.Take(BountyLimits.MaxAdds)) {
                bounty.Adds.Add(new BountyAddRecord {
                    MonsterId = ChatService.Clip(ChatService.NoMarkup(add.MonsterId), BountyLimits.MaxMonsterId),
                    Count = Mathf.Clamp(add.Count, 0, 1000),
                });
            }
            return bounty;
        }

        private static bool Usable(float value) => float.IsNaN(value) == false && float.IsInfinity(value) == false && Mathf.Abs(value) <= BountyLimits.MaxCoordinate;

        // Progress only moves forward. A report the owner sent just before a kill can arrive after the server
        // already counted that kill; it must not bring the creature back.
        private static void KeepProgress(SharedBountyRecord old, SharedBountyRecord bounty) {
            bounty.SharedUtc = old.SharedUtc;
            bounty.Slain |= old.Slain;
            bounty.Complete |= old.Complete;
            if (old.Adds.Count != bounty.Adds.Count) { return; }
            for (int i = 0; i < bounty.Adds.Count; i++) {
                if (old.Adds[i].MonsterId == bounty.Adds[i].MonsterId) { bounty.Adds[i].Count = Mathf.Min(bounty.Adds[i].Count, old.Adds[i].Count); }
            }
        }

        private static bool Same(SharedBountyRecord a, SharedBountyRecord b) {
            if (a.Name != b.Name || a.Biome != b.Biome || a.MonsterId != b.MonsterId || a.Slain != b.Slain || a.Complete != b.Complete
                || a.X != b.X || a.Y != b.Y || a.Z != b.Z || a.Radius != b.Radius || a.Adds.Count != b.Adds.Count) { return false; }
            for (int i = 0; i < a.Adds.Count; i++) {
                if (a.Adds[i].MonsterId != b.Adds[i].MonsterId || a.Adds[i].Count != b.Adds[i].Count) { return false; }
            }
            return true;
        }

        // ---------------------------------------------------------------- kills

        // Counted as Epic Loot counts them: the main target, or the first add of that kind still alive. Kills of
        // bounties nobody shares (or an abandoned bounty's leftovers) are not found and change nothing.
        private static void OnTargetSlain(string json) {
            if (ClanStore.State == null || EpicLootBounties.TryParseKill(json, out string id, out string monsterId, out bool isAdd) == false) { return; }
            if (ownerById.TryGetValue(id, out string account) == false) { return; }
            AccountRecord record = ClanRules.Account(account);
            SharedBountyRecord bounty = record?.Bounties.Find(b => b.Id == id);
            if (bounty == null || bounty.Complete) { return; }
            if (isAdd) {
                BountyAddRecord add = bounty.Adds.Find(a => a.MonsterId == monsterId && a.Count > 0);
                if (add == null) { return; }
                add.Count--;
            } else {
                if (bounty.MonsterId != monsterId || bounty.Slain) { return; }
                bounty.Slain = true;
            }
            ClanStore.MarkDirty();
            if (bounty.Slain == false || bounty.Adds.Any(a => a.Count > 0)) { return; }
            bounty.Complete = true;
            ClanRecord clan = ClanRules.Clan(record.ClanId);
            if (clan == null) { return; }
            string owner = ClanRules.Member(clan, account)?.LastCharacterName;
            ClanRules.Log(clan, "$clans_log_bounty_done", string.IsNullOrEmpty(owner) ? record.LastCharacterName : owner, bounty.Name);
            dirtyClans.Add(clan.Id);
        }

        // ---------------------------------------------------------------- lists

        private struct Open {
            internal string Account;
            internal string OwnerName;
            internal SharedBountyRecord Bounty;
        }

        // Every member's bounties that still have something alive. Each client skips its own.
        private static List<Open> OpenBounties(ClanRecord clan) {
            List<Open> open = new List<Open>();
            if (Enabled == false) { return open; }
            foreach (KeyValuePair<string, MemberRecord> member in clan.Members) {
                AccountRecord account = ClanRules.Account(member.Key);
                if (account == null) { continue; }
                foreach (SharedBountyRecord bounty in account.Bounties) {
                    if (bounty.Complete == false) { open.Add(new Open { Account = member.Key, OwnerName = member.Value.LastCharacterName, Bounty = bounty }); }
                }
            }
            return open;
        }

        private static void WriteList(ZPackage pkg, List<Open> open) {
            pkg.Write(open.Count);
            foreach (Open entry in open) { SharedBounty.Write(pkg, entry.Account, entry.OwnerName, entry.Bounty); }
        }

        // For clans-admin: every shared bounty, or one clan's.
        internal static IEnumerable<string> Describe(ClanRecord only) {
            if (ClanStore.State == null) { yield break; }
            foreach (ClanRecord clan in ClanStore.State.Clans.Values) {
                if (only != null && clan.Id != only.Id) { continue; }
                foreach (KeyValuePair<string, MemberRecord> member in clan.Members) {
                    AccountRecord account = ClanRules.Account(member.Key);
                    if (account == null) { continue; }
                    foreach (SharedBountyRecord bounty in account.Bounties) {
                        int adds = bounty.Adds.Sum(a => a.Count);
                        string state = bounty.Complete ? "complete" : bounty.Slain ? $"target slain, {adds} adds left" : $"target alive, {adds} adds left";
                        yield return $"[{clan.Name}] {member.Value.LastCharacterName}: {bounty.Name} ({bounty.Biome}) at {bounty.X:0}, {bounty.Z:0} r {bounty.Radius:0}, {state} - {bounty.Id}";
                    }
                }
            }
        }
    }
}
