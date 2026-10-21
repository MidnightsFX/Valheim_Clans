using Clans.Core;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Clans.Server {
    // Founding, joining, leaving and running a clan. Every handler checks the sender's standing itself:
    // the client's UI hides what a player cannot do, but any request can arrive from any client.
    internal static class ClanService {
        private const int MaxDescription = 500;
        private const int MaxStatus = 200;
        private const int MaxApplicationMessage = 200;
        private const int MaxRankName = 24;
        private const long ReservationSeconds = 60;

        // A clan name held while its founder pays the fee.
        private sealed class Reservation {
            internal long Id;
            internal string Account;
            internal string Name;
            internal string Description;
            internal EmblemData Emblem;
            internal bool Recruiting;
            internal long Expires;
        }

        private static readonly Dictionary<long, Reservation> reservations = new Dictionary<long, Reservation>();
        private static long nextReservationId = 1;

        internal static void Register() {
            ClanNet.RegisterServer(Op.CreateClan, OnCreateClan);
            ClanNet.RegisterServer(Op.ConfirmCreateFee, OnConfirmCreateFee);
            ClanNet.RegisterServer(Op.CancelCreate, OnCancelCreate);
            ClanNet.RegisterServer(Op.Apply, OnApply);
            ClanNet.RegisterServer(Op.WithdrawApplication, OnWithdrawApplication);
            ClanNet.RegisterServer(Op.ResolveApplication, OnResolveApplication);
            ClanNet.RegisterServer(Op.Leave, OnLeave);
            ClanNet.RegisterServer(Op.Kick, OnKick);
            ClanNet.RegisterServer(Op.SetRank, OnSetRank);
            ClanNet.RegisterServer(Op.TransferLeader, OnTransferLeader);
            ClanNet.RegisterServer(Op.Disband, OnDisband);
            ClanNet.RegisterServer(Op.EditInfo, OnEditInfo);
            ClanNet.RegisterServer(Op.EditEmblem, OnEditEmblem);
            ClanNet.RegisterServer(Op.EditRanks, OnEditRanks);
            ClanNet.RegisterServer(Op.RequestDetail, OnRequestDetail);
            ClanNet.RegisterServer(Op.DonateTreasury, OnDonateTreasury);
            ClanNet.RegisterServer(Op.WithdrawTreasury, OnWithdrawTreasury);
        }

        internal static void OnStateLoaded() {
            ClansWorldState state = ClanStore.State;
            WeeklyClock.DebugOffsetSeconds = state.DebugWeekOffsetSeconds;
            // Repair account links a hand edit or crash could have left inconsistent.
            foreach (AccountRecord account in state.Accounts.Values) {
                if (account.ClanId != 0 && (state.Clans.TryGetValue(account.ClanId, out ClanRecord clan) == false || clan.Members.ContainsKey(account.Account) == false)) {
                    account.ClanId = 0;
                }
            }
            foreach (ClanRecord clan in state.Clans.Values) {
                foreach (MemberRecord member in clan.Members.Values) {
                    AccountRecord account = ClanRules.Account(member.Account, create: true);
                    account.ClanId = clan.Id;
                }
                // Saved before levels were bought: the clan keeps the level its experience gave it.
                if (clan.Level < 1) { clan.Level = ClansSettings.Levels.LevelFor(clan.Xp); }
                if (clan.LevelContributions == null) { clan.LevelContributions = new Dictionary<string, int>(); }
                if (clan.Ranks.Count < 2) {
                    Logger.LogWarning($"Clan '{clan.Name}' had fewer than two ranks; its ranks were reset to the defaults.");
                    clan.Ranks = ClansSettings.Ranks.CreateClanRanks();
                    clan.NextRankId = clan.Ranks.Count + 1;
                    foreach (MemberRecord member in clan.Members.Values) { member.RankId = ClanRules.EntryRank(clan).Id; }
                }
                if (clan.Members.Count > 0 && ClanRules.Leader(clan) == null) {
                    // No leader: promote whoever has been in the clan longest.
                    MemberRecord oldest = clan.Members.Values.OrderBy(m => m.JoinedUtc).First();
                    oldest.RankId = ClanRules.LeaderRank(clan).Id;
                    Logger.LogWarning($"Clan '{clan.Name}' had no leader; {oldest.LastCharacterName} is now its leader.");
                }
            }
        }

        internal static void Reset() {
            reservations.Clear();
        }

        internal static void OnAccountOnline(ClanSender sender) {
            long now = ClanStore.Now;
            AccountRecord account = ClanRules.Account(sender.Account, create: true);
            account.LastCharacterName = sender.CharacterName;
            account.LastSeenUtc = now;
            ClanRecord clan = ClanRules.Clan(account.ClanId);
            MemberRecord member = ClanRules.Member(clan, sender.Account);
            if (member != null) {
                member.LastCharacterName = sender.CharacterName;
                member.LastSeenUtc = now;
                Broadcaster.MarkDetail(clan.Id);
            }
            Broadcaster.MarkRoster();
            ClanStore.MarkDirty();
        }

        internal static void OnAccountOffline(string account) {
            long now = ClanStore.Now;
            AccountRecord record = ClanRules.Account(account);
            if (record != null) { record.LastSeenUtc = now; }
            ClanRecord clan = ClanRules.ClanOf(account);
            MemberRecord member = ClanRules.Member(clan, account);
            if (member != null) {
                member.LastSeenUtc = now;
                Broadcaster.MarkDetail(clan.Id);
            }
            PartyService.OnAccountOffline(account);
            StorageService.OnAccountOffline(account);
            PositionRelay.OnAccountOffline(account);
            Broadcaster.MarkRoster();
            ClanStore.MarkDirty();
        }

        internal static void Housekeeping(long now) {
            if (reservations.Count > 0) {
                foreach (long id in reservations.Where(r => r.Value.Expires <= now).Select(r => r.Key).ToList()) {
                    reservations.Remove(id);
                }
            }

            long expiry = ClansConfig.ApplicationExpiryDays.Value * 86400L;
            foreach (ClanRecord clan in ClanStore.State.Clans.Values) {
                if (clan.Applications.Count == 0) { continue; }
                List<ApplicationRecord> expired = clan.Applications.Where(a => now - a.CreatedUtc > expiry).ToList();
                foreach (ApplicationRecord application in expired) {
                    RemoveApplication(clan, application.Account);
                }
            }
        }

        // ---------------------------------------------------------------- notices

        internal static void Notify(string account, string token, params string[] args) {
            ClanSender sender = Sessions.Get(account);
            if (sender == null) { return; }
            ClanNet.Send(sender, Op.Notify, 0, pkg => {
                pkg.Write((byte)0);
                pkg.Write(token ?? "");
                pkg.Write(args?.Length ?? 0);
                if (args != null) { foreach (string arg in args) { pkg.Write(arg ?? ""); } }
            });
        }

        internal static void NotifyClan(ClanRecord clan, string exceptAccount, string token, params string[] args) {
            foreach (string account in clan.Members.Keys) {
                if (account != exceptAccount) { Notify(account, token, args); }
            }
        }

        private static string NameOf(string account) {
            AccountRecord record = ClanRules.Account(account);
            if (record != null && string.IsNullOrEmpty(record.LastCharacterName) == false) { return record.LastCharacterName; }
            return account;
        }

        // ---------------------------------------------------------------- founding

        private static void OnCreateClan(ServerRequest request) {
            string name = ClanRules.CleanText(request.Body.ReadString(), 64);
            string description = ClanRules.CleanMultiline(request.Body.ReadString(), MaxDescription);
            EmblemData emblem = ClansSettings.Emblems.Sanitize(EmblemData.Read(request.Body));
            bool recruiting = request.Body.ReadBool();

            AccountRecord account = ClanRules.Account(request.Sender.Account, create: true);
            if (account.ClanId != 0) { request.Fail("$clans_err_already_in_clan"); return; }
            string nameError = ClanRules.CheckName(name);
            if (nameError != null) { request.Fail(nameError, ClansConfig.NameMinLength.Value.ToString(), ClansConfig.NameMaxLength.Value.ToString()); return; }
            if (reservations.Values.Any(r => r.Account != request.Sender.Account && ClanRules.NameKey(r.Name) == ClanRules.NameKey(name))) {
                request.Fail("$clans_err_name_taken");
                return;
            }

            // Only one reservation per founder.
            foreach (long stale in reservations.Where(r => r.Value.Account == request.Sender.Account).Select(r => r.Key).ToList()) {
                reservations.Remove(stale);
            }

            int fee = ClansConfig.CreationFee.Value;
            if (fee <= 0) {
                ClanRecord clan = Found(request.Sender.Account, request.Sender.CharacterName, name, description, emblem, recruiting);
                request.Ok(pkg => { pkg.Write(0L); pkg.Write(0); pkg.Write(clan.Id); });
                return;
            }

            Reservation reservation = new Reservation {
                Id = nextReservationId++,
                Account = request.Sender.Account,
                Name = name,
                Description = description,
                Emblem = emblem,
                Recruiting = recruiting,
                Expires = ClanStore.Now + ReservationSeconds,
            };
            reservations[reservation.Id] = reservation;
            // The client takes the fee from the founder's inventory, then confirms with this id.
            request.Ok(pkg => { pkg.Write(reservation.Id); pkg.Write(fee); pkg.Write(0L); });
        }

        // The founder has paid. If this fails the client refunds the fee.
        private static void OnConfirmCreateFee(ServerRequest request) {
            long id = request.Body.ReadLong();
            if (reservations.TryGetValue(id, out Reservation reservation) == false || reservation.Account != request.Sender.Account) {
                request.Fail("$clans_err_reservation_expired");
                return;
            }
            reservations.Remove(id);
            AccountRecord account = ClanRules.Account(request.Sender.Account, create: true);
            if (account.ClanId != 0) { request.Fail("$clans_err_already_in_clan"); return; }
            string nameError = ClanRules.CheckName(reservation.Name);
            if (nameError != null) { request.Fail(nameError, ClansConfig.NameMinLength.Value.ToString(), ClansConfig.NameMaxLength.Value.ToString()); return; }

            ClanRecord clan = Found(request.Sender.Account, request.Sender.CharacterName, reservation.Name, reservation.Description, reservation.Emblem, reservation.Recruiting);
            request.Ok(pkg => pkg.Write(clan.Id));
        }

        private static void OnCancelCreate(ServerRequest request) {
            long id = request.Body.ReadLong();
            if (reservations.TryGetValue(id, out Reservation reservation) && reservation.Account == request.Sender.Account) {
                reservations.Remove(id);
            }
            request.Ok();
        }

        internal static ClanRecord Found(string founderAccount, string founderName, string name, string description, EmblemData emblem, bool recruiting) {
            ClansWorldState state = ClanStore.State;
            long now = ClanStore.Now;
            ClanRecord clan = new ClanRecord {
                Id = state.NextClanId++,
                Name = name,
                Description = description ?? "",
                Emblem = emblem ?? ClansSettings.Emblems.Default.Clone(),
                Recruiting = recruiting,
                CreatedUtc = now,
                Level = 1,
                Ranks = ClansSettings.Ranks.CreateClanRanks(),
            };
            clan.NextRankId = clan.Ranks.Count + 1;
            state.Clans[clan.Id] = clan;
            AddMember(clan, founderAccount, founderName, ClanRules.LeaderRank(clan).Id);
            ClanRules.Log(clan, "$clans_log_founded", founderName, name);
            Broadcaster.MarkDirectory();
            ClanStore.MarkDirtySoon();
            Logger.LogInfo($"{founderName} ({founderAccount}) founded the clan '{name}' (id {clan.Id}).");
            return clan;
        }

        // Joining through an invite a member posted in Global chat. The poster must still be allowed to invite.
        internal static void JoinFromChat(ServerRequest request, long clanId, string poster) {
            ClanRecord clan = ClanRules.Clan(clanId);
            if (clan == null || clan.Members.ContainsKey(poster) == false || ClanRules.Can(clan, poster, ClanPermission.Invite) == false) {
                request.Fail("$clans_err_invite_gone");
                return;
            }
            string account = request.Sender.Account;
            AccountRecord record = ClanRules.Account(account, create: true);
            if (record.ClanId != 0) { request.Fail("$clans_err_already_in_clan"); return; }
            if (clan.Members.Count >= ClanRules.MaxMembers(clan)) { request.Fail("$clans_err_clan_full", ClanRules.MaxMembers(clan).ToString()); return; }
            string name = request.Sender.CharacterName;
            RemoveApplication(clan, account);
            AddMember(clan, account, name, ClanRules.EntryRank(clan).Id);
            ClanRules.Log(clan, "$clans_log_joined", name, clan.Members.TryGetValue(poster, out MemberRecord inviter) ? inviter.LastCharacterName : "");
            NotifyClan(clan, account, "$clans_notice_member_joined_invite", name);
            Notify(account, "$clans_notice_joined_clan", clan.Name);
            request.Ok();
        }

        internal static void AddMember(ClanRecord clan, string account, string characterName, int rankId) {
            long now = ClanStore.Now;
            clan.Members[account] = new MemberRecord {
                Account = account,
                RankId = rankId,
                LastCharacterName = characterName ?? "",
                JoinedUtc = now,
                LastSeenUtc = now,
            };
            AccountRecord record = ClanRules.Account(account, create: true);
            record.ClanId = clan.Id;
            if (string.IsNullOrEmpty(characterName) == false) { record.LastCharacterName = characterName; }
            // Joining one clan withdraws every other application.
            foreach (long other in record.PendingApplications.ToList()) {
                ClanRecord otherClan = ClanRules.Clan(other);
                if (otherClan != null) { RemoveApplication(otherClan, account); }
            }
            record.PendingApplications.Clear();
            PartyService.OnClanChanged(account);
            BountyRelay.OnMemberAdded(clan, account);
            Broadcaster.MarkMember(clan.Id, account);
        }

        private static void RemoveMember(ClanRecord clan, string account) {
            clan.Members.Remove(account);
            AccountRecord record = ClanRules.Account(account);
            if (record != null && record.ClanId == clan.Id) { record.ClanId = 0; }
            StorageService.OnMemberRemoved(clan, account);
            PartyService.OnClanChanged(account);
            BountyRelay.OnMemberRemoved(clan, account);
            Broadcaster.MarkMember(clan.Id, account);
            ClanStore.MarkDirtySoon();
        }

        // ---------------------------------------------------------------- applications

        private static void OnApply(ServerRequest request) {
            long clanId = request.Body.ReadLong();
            string message = ClanRules.CleanText(request.Body.ReadString(), MaxApplicationMessage);
            AccountRecord account = ClanRules.Account(request.Sender.Account, create: true);
            ClanRecord clan = ClanRules.Clan(clanId);
            if (clan == null) { request.Fail("$clans_err_no_such_clan"); return; }
            if (account.ClanId != 0) { request.Fail("$clans_err_already_in_clan"); return; }
            if (clan.Recruiting == false) { request.Fail("$clans_err_not_recruiting", clan.Name); return; }
            if (account.PendingApplications.Contains(clanId)) { request.Fail("$clans_err_already_applied", clan.Name); return; }
            if (account.PendingApplications.Count >= ClansConfig.MaxPendingApplications.Value) {
                request.Fail("$clans_err_too_many_applications", ClansConfig.MaxPendingApplications.Value.ToString());
                return;
            }

            account.PendingApplications.Add(clanId);
            clan.Applications.Add(new ApplicationRecord {
                Account = request.Sender.Account,
                CharacterName = request.Sender.CharacterName,
                Message = message,
                CreatedUtc = ClanStore.Now,
            });
            foreach (MemberRecord member in clan.Members.Values) {
                if (ClanRules.Can(clan, member.Account, ClanPermission.ManageApplications)) {
                    Notify(member.Account, "$clans_notice_new_application", request.Sender.CharacterName);
                }
            }
            Broadcaster.MarkDetail(clan.Id);
            Broadcaster.MarkAccount(request.Sender.Account);
            ClanStore.MarkDirty();
            request.Ok();
        }

        private static void OnWithdrawApplication(ServerRequest request) {
            long clanId = request.Body.ReadLong();
            ClanRecord clan = ClanRules.Clan(clanId);
            if (clan != null) { RemoveApplication(clan, request.Sender.Account); }
            AccountRecord account = ClanRules.Account(request.Sender.Account);
            account?.PendingApplications.Remove(clanId);
            Broadcaster.MarkAccount(request.Sender.Account);
            request.Ok();
        }

        private static void RemoveApplication(ClanRecord clan, string account) {
            int removed = clan.Applications.RemoveAll(a => a.Account == account);
            ClanRules.Account(account)?.PendingApplications.Remove(clan.Id);
            if (removed > 0) {
                Broadcaster.MarkDetail(clan.Id);
                Broadcaster.MarkAccount(account);
                ClanStore.MarkDirty();
            }
        }

        private static void OnResolveApplication(ServerRequest request) {
            string applicant = request.Body.ReadString();
            bool accept = request.Body.ReadBool();
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.ManageApplications) == false) { request.Fail("$clans_err_no_permission"); return; }
            ApplicationRecord application = clan.Applications.FirstOrDefault(a => a.Account == applicant);
            if (application == null) { request.Fail("$clans_err_no_such_application"); return; }

            if (accept == false) {
                RemoveApplication(clan, applicant);
                Notify(applicant, "$clans_notice_application_declined", clan.Name);
                request.Ok();
                return;
            }

            AccountRecord record = ClanRules.Account(applicant, create: true);
            if (record.ClanId != 0) {
                RemoveApplication(clan, applicant);
                request.Fail("$clans_err_target_in_clan", application.CharacterName);
                return;
            }
            if (clan.Members.Count >= ClanRules.MaxMembers(clan)) {
                request.Fail("$clans_err_clan_full", ClanRules.MaxMembers(clan).ToString());
                return;
            }

            string name = string.IsNullOrEmpty(record.LastCharacterName) ? application.CharacterName : record.LastCharacterName;
            RemoveApplication(clan, applicant);
            AddMember(clan, applicant, name, ClanRules.EntryRank(clan).Id);
            ClanRules.Log(clan, "$clans_log_joined", name, request.Sender.CharacterName);
            Notify(applicant, "$clans_notice_application_accepted", clan.Name);
            request.Ok();
        }

        // ---------------------------------------------------------------- membership

        private static void OnLeave(ServerRequest request) {
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.IsLeader(clan, request.Sender.Account)) {
                if (clan.Members.Count > 1) { request.Fail("$clans_err_leader_must_transfer"); return; }
                // The last member leaving ends the clan.
                DisbandClan(clan, request.Sender.CharacterName);
                request.Ok();
                return;
            }
            RemoveMember(clan, request.Sender.Account);
            ClanRules.Log(clan, "$clans_log_left", request.Sender.CharacterName);
            request.Ok();
        }

        private static void OnKick(ServerRequest request) {
            string target = request.Body.ReadString();
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            MemberRecord member = ClanRules.Member(clan, target);
            if (member == null) { request.Fail("$clans_err_not_a_member"); return; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.Kick) == false) { request.Fail("$clans_err_no_permission"); return; }
            if (ClanRules.Outranks(clan, request.Sender.Account, target) == false) { request.Fail("$clans_err_outranked"); return; }

            RemoveMember(clan, target);
            ClanRules.Log(clan, "$clans_log_kicked", member.LastCharacterName, request.Sender.CharacterName);
            Notify(target, "$clans_notice_kicked", clan.Name);
            request.Ok();
        }

        private static void OnSetRank(ServerRequest request) {
            string target = request.Body.ReadString();
            int rankId = request.Body.ReadInt();
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            MemberRecord member = ClanRules.Member(clan, target);
            MemberRecord actor = ClanRules.Member(clan, request.Sender.Account);
            if (member == null) { request.Fail("$clans_err_not_a_member"); return; }
            int newIndex = clan.Ranks.FindIndex(r => r.Id == rankId);
            if (newIndex < 0) { request.Fail("$clans_err_no_such_rank"); return; }
            if (newIndex == 0) { request.Fail("$clans_err_use_transfer"); return; }

            int actorIndex = ClanRules.RankIndex(clan, actor.RankId);
            int oldIndex = ClanRules.RankIndex(clan, member.RankId);
            if (oldIndex == newIndex) { request.Ok(); return; }
            bool promote = newIndex < oldIndex;
            ClanPermission needed = promote ? ClanPermission.Promote : ClanPermission.Demote;
            if (ClanRules.Can(clan, request.Sender.Account, needed) == false) { request.Fail("$clans_err_no_permission"); return; }
            // Only members below you, and only to ranks below your own.
            if (actorIndex >= oldIndex || actorIndex >= newIndex) { request.Fail("$clans_err_outranked"); return; }

            member.RankId = rankId;
            // Storage rights may have changed: a shared tab open under the old rank is closed.
            StorageService.CloseLeasesOf(target, "$clans_storage_closed_rank", sharedOnly: true);
            string rankName = clan.Ranks[newIndex].Name;
            ClanRules.Log(clan, promote ? "$clans_log_promoted" : "$clans_log_demoted", member.LastCharacterName, rankName, request.Sender.CharacterName);
            Notify(target, promote ? "$clans_notice_promoted" : "$clans_notice_demoted", rankName);
            Broadcaster.MarkMember(clan.Id, target);
            ClanStore.MarkDirty();
            request.Ok();
        }

        private static void OnTransferLeader(ServerRequest request) {
            string target = request.Body.ReadString();
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.IsLeader(clan, request.Sender.Account) == false) { request.Fail("$clans_err_leader_only"); return; }
            MemberRecord member = ClanRules.Member(clan, target);
            if (member == null || target == request.Sender.Account) { request.Fail("$clans_err_not_a_member"); return; }

            MemberRecord self = ClanRules.Member(clan, request.Sender.Account);
            member.RankId = ClanRules.LeaderRank(clan).Id;
            // The old leader steps down one rank.
            self.RankId = clan.Ranks[Math.Min(1, clan.Ranks.Count - 1)].Id;
            StorageService.CloseLeasesOf(request.Sender.Account, "$clans_storage_closed_rank", sharedOnly: true);
            ClanRules.Log(clan, "$clans_log_new_leader", member.LastCharacterName, request.Sender.CharacterName);
            NotifyClan(clan, request.Sender.Account, "$clans_notice_new_leader", member.LastCharacterName);
            Broadcaster.MarkMember(clan.Id, target);
            Broadcaster.MarkMember(clan.Id, request.Sender.Account);
            ClanStore.MarkDirty();
            request.Ok();
        }

        private static void OnDisband(ServerRequest request) {
            string confirmName = request.Body.ReadString();
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.IsLeader(clan, request.Sender.Account) == false) { request.Fail("$clans_err_leader_only"); return; }
            if (ClanRules.NameKey(confirmName) != ClanRules.NameKey(clan.Name)) { request.Fail("$clans_err_confirm_name"); return; }
            DisbandClan(clan, request.Sender.CharacterName);
            request.Ok();
        }

        internal static void DisbandClan(ClanRecord clan, string byName) {
            ClansWorldState state = ClanStore.State;
            MemberRecord leader = ClanRules.Leader(clan);
            string leaderAccount = leader?.Account;

            WarService.OnClanDisbanded(clan);
            ActivityService.OnClanDisbanded(clan);
            StorageService.OnClanDisbanded(clan, leaderAccount);
            if (ClansConfig.DisbandRefund.Value && leaderAccount != null && clan.Treasury > 0) {
                MailboxService.Add(leaderAccount, new Delivery { Reason = "$clans_mail_treasury_refund", Coins = (int)Math.Min(int.MaxValue, clan.Treasury) });
            }

            foreach (string account in clan.Members.Keys.ToList()) {
                AccountRecord record = ClanRules.Account(account);
                if (record != null && record.ClanId == clan.Id) { record.ClanId = 0; }
                Notify(account, "$clans_notice_disbanded", clan.Name);
                PartyService.OnClanChanged(account);
                BountyRelay.OnMemberRemoved(clan, account);
                Broadcaster.MarkAccount(account);
            }
            foreach (ApplicationRecord application in clan.Applications) {
                ClanRules.Account(application.Account)?.PendingApplications.Remove(clan.Id);
                Broadcaster.MarkAccount(application.Account);
            }

            state.Clans.Remove(clan.Id);
            Broadcaster.MarkDirectory();
            Broadcaster.MarkRoster();
            ClanStore.MarkDirtySoon();
            Logger.LogInfo($"Clan '{clan.Name}' (id {clan.Id}) was disbanded by {byName}.");
        }

        // ---------------------------------------------------------------- info, emblem, ranks

        private static void OnEditInfo(ServerRequest request) {
            string status = ClanRules.CleanText(request.Body.ReadString(), MaxStatus);
            string description = ClanRules.CleanMultiline(request.Body.ReadString(), MaxDescription);
            bool recruiting = request.Body.ReadBool();
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.EditInfo) == false) { request.Fail("$clans_err_no_permission"); return; }

            bool statusChanged = clan.Status != status;
            clan.Status = status;
            clan.Description = description;
            clan.Recruiting = recruiting;
            if (statusChanged && status.Length > 0) { ClanRules.Log(clan, "$clans_log_status", request.Sender.CharacterName, status); }
            Broadcaster.MarkDetail(clan.Id);
            Broadcaster.MarkDirectory();
            ClanStore.MarkDirty();
            request.Ok();
        }

        private static void OnEditEmblem(ServerRequest request) {
            EmblemData emblem = ClansSettings.Emblems.Sanitize(EmblemData.Read(request.Body));
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.EditEmblem) == false) { request.Fail("$clans_err_no_permission"); return; }
            clan.Emblem = emblem;
            ClanRules.Log(clan, "$clans_log_emblem", request.Sender.CharacterName);
            // Members' own view too: the window's header and Overview draw the emblem from the clan's detail.
            Broadcaster.MarkDetail(clan.Id);
            Broadcaster.MarkDirectory();
            ClanStore.MarkDirty();
            request.Ok();
        }

        // The whole rank list comes in, top to bottom. Ranks with Id 0 are new. The first and last must be the
        // clan's existing leader and entry ranks.
        //
        // The leader may change anything else. A member whose rank allows editing ranks only changes the ranks
        // below their own: everything from the top down to their own rank stays exactly as it was, and the
        // ranks below can only be given rights the editor has themselves, never the right to edit ranks.
        private static void OnEditRanks(ServerRequest request) {
            int count = request.Body.ReadInt();
            if (count < 0 || count > 64) { request.Fail("$clans_err_bad_request"); return; }
            List<RankDef> proposed = new List<RankDef>();
            for (int i = 0; i < count; i++) { proposed.Add(RankDef.Read(request.Body)); }

            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.EditRanks) == false) { request.Fail("$clans_err_no_permission"); return; }
            int maxRanks = Math.Max(2, ClansSettings.Ranks.MaxRanks);
            if (proposed.Count < 2 || proposed.Count > maxRanks) { request.Fail("$clans_err_rank_count", maxRanks.ToString()); return; }
            RankDef leader = ClanRules.LeaderRank(clan);
            RankDef entry = ClanRules.EntryRank(clan);
            if (proposed[0].Id != leader.Id || proposed[proposed.Count - 1].Id != entry.Id) { request.Fail("$clans_err_fixed_ranks"); return; }

            bool isLeader = ClanRules.IsLeader(clan, request.Sender.Account);
            int ownIndex = ClanRules.RankIndex(clan, ClanRules.Member(clan, request.Sender.Account).RankId);
            ClanPermission grantable = isLeader ? ClanPermissions.All
                : ClanRules.Permissions(clan, ClanRules.Member(clan, request.Sender.Account)) & ~ClanPermission.EditRanks;
            if (isLeader == false) {
                // The ranks at and above the editor's own stay where and what they are.
                if (proposed.Count <= ownIndex) { request.Fail("$clans_err_rank_above"); return; }
                for (int i = 0; i <= ownIndex; i++) {
                    if (proposed[i].Id != clan.Ranks[i].Id) { request.Fail("$clans_err_rank_above"); return; }
                }
            }

            HashSet<int> existingIds = new HashSet<int>(clan.Ranks.Select(r => r.Id));
            HashSet<int> seen = new HashSet<int>();
            List<RankDef> result = new List<RankDef>();
            for (int position = 0; position < proposed.Count; position++) {
                RankDef rank = proposed[position];
                if (isLeader == false && position <= ownIndex) {
                    RankDef kept = clan.Ranks[position];
                    seen.Add(kept.Id);
                    result.Add(new RankDef { Id = kept.Id, Name = kept.Name, Permissions = kept.Permissions, Fixed = false });
                    continue;
                }
                RankDef clean = new RankDef {
                    Id = rank.Id,
                    Name = ClanRules.CleanText(rank.Name, MaxRankName),
                    Permissions = rank.Permissions & ClanPermissions.All & grantable,
                    Fixed = false,
                };
                if (clean.Id <= 0) {
                    clean.Id = clan.NextRankId++;
                } else if (existingIds.Contains(clean.Id) == false || seen.Add(clean.Id) == false) {
                    request.Fail("$clans_err_bad_request");
                    return;
                }
                result.Add(clean);
            }
            result[0].Fixed = true;
            result[0].Permissions = ClanPermissions.All;
            result[result.Count - 1].Fixed = true;
            if (string.IsNullOrEmpty(result[0].Name)) { result[0].Name = leader.Name; }
            if (string.IsNullOrEmpty(result[result.Count - 1].Name)) { result[result.Count - 1].Name = entry.Name; }

            // Members of a removed rank drop to the highest rank (in the new order) of those that were below it,
            // so nobody ends up above a rank that outranked them.
            HashSet<int> keptIds = new HashSet<int>(result.Select(r => r.Id));
            foreach (MemberRecord member in clan.Members.Values) {
                if (keptIds.Contains(member.RankId)) { continue; }
                int oldIndex = clan.Ranks.FindIndex(r => r.Id == member.RankId);
                HashSet<int> below = new HashSet<int>(clan.Ranks.Skip(Math.Max(0, oldIndex) + 1).Select(r => r.Id));
                RankDef target = result.FirstOrDefault(r => below.Contains(r.Id));
                member.RankId = target != null ? target.Id : entry.Id;
                Broadcaster.MarkAccount(member.Account);
            }
            // Storage rights may have changed for anyone: open shared tabs are closed.
            foreach (string account in clan.Members.Keys) { StorageService.CloseLeasesOf(account, "$clans_storage_closed_rank", sharedOnly: true); }

            clan.Ranks = result;
            ClanRules.Log(clan, "$clans_log_ranks", request.Sender.CharacterName);
            foreach (string account in clan.Members.Keys) { Broadcaster.MarkAccount(account); }
            Broadcaster.MarkDetail(clan.Id);
            ClanStore.MarkDirty();
            request.Ok();
        }

        private static void OnRequestDetail(ServerRequest request) {
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            Broadcaster.SendMyAccount(request.Sender);
            if (clan != null) { Broadcaster.SendDetail(request.Sender, clan); }
            request.Ok();
        }

        // ---------------------------------------------------------------- treasury

        // The client has already taken the coins; if this fails it gives them back.
        private static void OnDonateTreasury(ServerRequest request) {
            int coins = request.Body.ReadInt();
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (coins <= 0) { request.Fail("$clans_err_bad_request"); return; }
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.TreasuryDonate) == false) { request.Fail("$clans_err_no_permission"); return; }
            ClanRules.AddTreasury(clan, coins);
            ClanRules.Log(clan, "$clans_log_donated", request.Sender.CharacterName, coins.ToString());
            request.Ok();
        }

        // Paid out through the mailbox, so the coins survive a disconnect between here and the client.
        private static void OnWithdrawTreasury(ServerRequest request) {
            int coins = request.Body.ReadInt();
            ClanRecord clan = ClanRules.ClanOf(request.Sender.Account);
            if (coins <= 0) { request.Fail("$clans_err_bad_request"); return; }
            if (clan == null) { request.Fail("$clans_err_not_in_clan"); return; }
            if (ClanRules.Can(clan, request.Sender.Account, ClanPermission.TreasuryWithdraw) == false) { request.Fail("$clans_err_no_permission"); return; }
            if (clan.Treasury < coins) { request.Fail("$clans_err_treasury_low", clan.Treasury.ToString()); return; }
            ClanRules.AddTreasury(clan, -coins);
            ClanRules.Log(clan, "$clans_log_withdrew", request.Sender.CharacterName, coins.ToString());
            MailboxService.Add(request.Sender.Account, new Delivery { Reason = "$clans_mail_treasury_withdraw", Coins = coins });
            request.Ok();
        }
    }
}
