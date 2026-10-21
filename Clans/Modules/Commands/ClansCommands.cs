using Clans.Client;
using Clans.Core;
using Clans.Net;
using Clans.Server;
using Clans.Server.Model;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

#pragma warning disable IDE0130
namespace Clans {
#pragma warning restore IDE0130

    // Console commands. The player commands do exactly what the Clans window does, so every flow can be
    // tried from the console; the clans-admin command acts on the server's data directly.
    internal static partial class TerminalManager {
        // Called from Init().
        private static void RegisterCommands() {
            RegisterHelpCommand();
            RegisterPlayerCommands();
            RegisterPartyCommands();
            RegisterAdminCommands();
        }

        // ---------------------------------------------------------------- player commands

        private static void RegisterPlayerCommands() {
            _ = new ModCommand($"{CommandPrefix}-state", "Shows what your client knows: your clan, rank, applications and the clans on this server.", State, "clan");
            _ = new ModCommand($"{CommandPrefix}-list", "Lists every clan on this server.", ListClans, "clan");
            _ = new ModCommand($"{CommandPrefix}-ping", "Measures the round trip to the Clans server.", Ping, "debug");
            _ = new ModCommand($"{CommandPrefix}-net", "Shows Clans network traffic on this machine, per message type.", NetStats, "debug");

            _ = new ModCommand($"{CommandPrefix}-create", "Format: [name] Founds a clan, paying the creation fee.", args => {
                string name = args.Args.GetStringFrom(0);
                ClientActions.CreateClan(name, "", ClansSettings.Emblems.Default, true, Report(args, $"Founded {name}."));
            }, "clan");
            _ = new ModCommand($"{CommandPrefix}-apply", "Format: [clan name] Applies to join a clan.", args => {
                ClanSummary clan = FindClan(args, args.Args.GetStringFrom(0));
                if (clan != null) { ClientActions.Apply(clan.Id, "", Report(args, $"Applied to {clan.Name}.")); }
            }, "clan", ClanNameOptions);
            _ = new ModCommand($"{CommandPrefix}-withdraw", "Format: [clan name] Withdraws an application.", args => {
                ClanSummary clan = FindClan(args, args.Args.GetStringFrom(0));
                if (clan != null) { ClientActions.WithdrawApplication(clan.Id, Report(args, $"Withdrew from {clan.Name}.")); }
            }, "clan", ClanNameOptions);
            _ = new ModCommand($"{CommandPrefix}-accept", "Format: [player] Accepts an application to your clan.", args => {
                ApplicationInfo application = FindApplication(args, args.Args.GetStringFrom(0));
                if (application != null) { ClientActions.ResolveApplication(application.Account, true, Report(args, $"Accepted {application.Name}.")); }
            }, "clan", ApplicantOptions);
            _ = new ModCommand($"{CommandPrefix}-decline", "Format: [player] Declines an application to your clan.", args => {
                ApplicationInfo application = FindApplication(args, args.Args.GetStringFrom(0));
                if (application != null) { ClientActions.ResolveApplication(application.Account, false, Report(args, $"Declined {application.Name}.")); }
            }, "clan", ApplicantOptions);
            _ = new ModCommand($"{CommandPrefix}-leave", "Leaves your clan. A leader must hand over leadership first, unless they are the last member.", args => {
                ClientActions.Leave(Report(args, "You left your clan."));
            }, "clan");
            _ = new ModCommand($"{CommandPrefix}-kick", "Format: [player] Removes a member below your rank.", args => {
                MemberInfo member = FindMember(args, args.Args.GetStringFrom(0));
                if (member != null) { ClientActions.Kick(member.Account, Report(args, $"Kicked {member.Name}.")); }
            }, "clan", MemberOptions);
            _ = new ModCommand($"{CommandPrefix}-promote", "Format: [player] Moves a member one rank up.", args => MoveRank(args, -1), "clan", MemberOptions);
            _ = new ModCommand($"{CommandPrefix}-demote", "Format: [player] Moves a member one rank down.", args => MoveRank(args, +1), "clan", MemberOptions);
            _ = new ModCommand($"{CommandPrefix}-transfer", "Format: [player] Makes another member the leader. You step down one rank.", args => {
                MemberInfo member = FindMember(args, args.Args.GetStringFrom(0));
                if (member != null) { ClientActions.TransferLeader(member.Account, Report(args, $"{member.Name} now leads the clan.")); }
            }, "clan", MemberOptions);
            _ = new ModCommand($"{CommandPrefix}-disband", "Format: [clan name] Ends your clan. Type its name to confirm.", args => {
                ClientActions.Disband(args.Args.GetStringFrom(0), Report(args, "Your clan was disbanded."));
            }, "clan");
            _ = new ModCommand($"{CommandPrefix}-status", "Format: [text] Sets your clan's status message.", args => {
                ClanDetailInfo clan = ClanClient.MyClan;
                if (clan == null) { args.Output.Error("You are not in a clan."); return; }
                ClientActions.EditInfo(args.Args.GetStringFrom(0), clan.Description, clan.Recruiting, Report(args, "Status updated."));
            }, "clan");
            _ = new ModCommand($"{CommandPrefix}-recruiting", "Format: [on/off] Opens or closes your clan to applications.", args => {
                ClanDetailInfo clan = ClanClient.MyClan;
                if (clan == null) { args.Output.Error("You are not in a clan."); return; }
                bool open = args.Args.GetString(0, "on").Equals("off", StringComparison.OrdinalIgnoreCase) == false;
                ClientActions.EditInfo(clan.Status, clan.Description, open, Report(args, open ? "Your clan is recruiting." : "Your clan is closed to applications."));
            }, "clan", input => new List<string> { "on", "off" });
            _ = new ModCommand($"{CommandPrefix}-donate", "Format: [coins] Puts coins from your inventory into the clan treasury.", args => {
                int coins = args.Args.GetInt(0, 0);
                ClientActions.Donate(coins, Report(args, $"Donated {coins} coins."));
            }, "clan");
            _ = new ModCommand($"{CommandPrefix}-takecoins", "Format: [coins] Takes coins out of the clan treasury, if your rank may.", args => {
                int coins = args.Args.GetInt(0, 0);
                ClientActions.WithdrawTreasury(coins, Report(args, $"{coins} coins are on their way."));
            }, "clan");
        }

        // Prints the outcome of a request into the console it was typed in.
        private static Action<NetResult> Report(ModCommandArgs args, string success) {
            return result => {
                if (result.Ok) { args.Output.Info(success); } else { args.Output.Error(result.Message()); }
                args.Output.Flush();
            };
        }

        private static void State(ModCommandArgs args) {
            if (ClanClient.Welcomed == false) {
                args.Output.Warning("Not connected to a Clans server yet.");
                return;
            }
            args.Output.Info($"Account   : {ClanClient.MyAccount}", log: false);
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) {
                args.Output.Info("Clan      : none", log: false);
                foreach (long id in ClanClient.Me.Applications) {
                    args.Output.Detail($"Applied to: {ClanClient.Clan(id)?.Name ?? id.ToString()}", log: false);
                }
            } else {
                RankDef rank = clan.Rank(ClanClient.Me.RankId);
                args.Output.Info($"Clan      : {clan.Name} (level {clan.Level}, {clan.Xp} xp, treasury {clan.Treasury})", log: false);
                args.Output.Detail($"Rank      : {Loc(rank?.Name)}", log: false);
                args.Output.Detail($"Can       : {string.Join(", ", ClanPermissions.Split(ClanClient.Me.Permissions))}", log: false);
                args.Output.Detail($"Status    : {clan.Status}", log: false);
                foreach (MemberInfo member in clan.Members.OrderBy(m => clan.RankIndex(m.RankId))) {
                    args.Output.Detail($"  {Loc(clan.Rank(member.RankId)?.Name),-12} {member.Name}{(member.Online ? " (online)" : "")}", log: false);
                }
                foreach (ApplicationInfo application in clan.Applications) {
                    args.Output.Detail($"  applicant    {application.Name} {application.Message}", log: false);
                }
                int start = Math.Max(0, clan.Log.Count - 5);
                for (int i = start; i < clan.Log.Count; i++) {
                    LogInfo entry = clan.Log[i];
                    args.Output.Detail($"  log: {new NetResult(true, entry.Token, entry.Args, null).Message()}", log: false);
                }
            }
            args.Output.Detail($"Clans on server: {ClanClient.Clans.Count}, players online: {ClanClient.Roster.Count}, wars: {ClanClient.Wars.Count}", log: false);
            args.Output.Detail($"Week resets in {FormatSpan(ClanClient.PeriodEndUtc - ClanClient.ServerNow)}", log: false);
        }

        private static void ListClans(ModCommandArgs args) {
            if (ClanClient.Clans.Count == 0) { args.Output.Info("No clans yet.", log: false); return; }
            foreach (ClanSummary clan in ClanClient.Clans.Values.OrderByDescending(c => c.Level).ThenBy(c => c.Name)) {
                args.Output.Info($"{clan.Name} - level {clan.Level}, {clan.MemberCount} members, led by {clan.LeaderName}{(clan.Recruiting ? ", recruiting" : "")}", log: false);
            }
        }

        private static void Ping(ModCommandArgs args) {
            void OnPong(float ms) {
                ClanClient.PongReceived -= OnPong;
                args.Output.Info($"Clans round trip: {ms:0} ms");
                args.Output.Flush();
            }
            ClanClient.PongReceived += OnPong;
            ClientActions.Ping();
        }

        private static void NetStats(ModCommandArgs args) {
            args.Output.Info("Sent:", log: false);
            foreach (KeyValuePair<Op, long> entry in ClanNet.BytesSent.OrderByDescending(e => e.Value)) {
                args.Output.Detail($"  {entry.Key,-22} {entry.Value,10} bytes", log: false);
            }
            args.Output.Info("Received:", log: false);
            foreach (KeyValuePair<Op, long> entry in ClanNet.BytesReceived.OrderByDescending(e => e.Value)) {
                args.Output.Detail($"  {entry.Key,-22} {entry.Value,10} bytes", log: false);
            }
        }

        private static void MoveRank(ModCommandArgs args, int step) {
            ClanDetailInfo clan = ClanClient.MyClan;
            MemberInfo member = FindMember(args, args.Args.GetStringFrom(0));
            if (clan == null || member == null) { return; }
            int index = clan.RankIndex(member.RankId) + step;
            if (index < 1 || index >= clan.Ranks.Count) {
                args.Output.Error(step < 0 ? "Use clans-transfer to make someone the leader." : $"{member.Name} is already in the lowest rank.");
                return;
            }
            RankDef rank = clan.Ranks[index];
            ClientActions.SetRank(member.Account, rank.Id, Report(args, $"{member.Name} is now {Loc(rank.Name)}."));
        }

        private static string Loc(string text) {
            if (string.IsNullOrEmpty(text)) { return ""; }
            return Localization.instance != null ? Localization.instance.Localize(text) : text;
        }

        private static string FormatSpan(long seconds) {
            if (seconds < 0) { seconds = 0; }
            TimeSpan span = TimeSpan.FromSeconds(seconds);
            return span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h" : $"{span.Hours}h {span.Minutes}m";
        }

        private static ClanSummary FindClan(ModCommandArgs args, string name) {
            ClanSummary clan = ClanClient.Clans.Values.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (clan == null) { args.Output.Error($"No clan called '{name}'."); }
            return clan;
        }

        private static MemberInfo FindMember(ModCommandArgs args, string name) {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) { args.Output.Error("You are not in a clan."); return null; }
            MemberInfo member = clan.Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            if (member == null) { args.Output.Error($"No member called '{name}'."); }
            return member;
        }

        private static ApplicationInfo FindApplication(ModCommandArgs args, string name) {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) { args.Output.Error("You are not in a clan."); return null; }
            ApplicationInfo application = clan.Applications.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (application == null) { args.Output.Error($"No application from '{name}'."); }
            return application;
        }

        private static List<string> ClanNameOptions(string[] input) => ClanClient.Clans.Values.Select(c => c.Name).ToList();
        private static List<string> MemberOptions(string[] input) => ClanClient.MyClan?.Members.Select(m => m.Name).ToList() ?? new List<string>();
        private static List<string> ApplicantOptions(string[] input) => ClanClient.MyClan?.Applications.Select(a => a.Name).ToList() ?? new List<string>();

        // ---------------------------------------------------------------- admin commands

        private static readonly string[] AdminSubcommands = { "save", "list", "fakeclan", "fakeapply", "xp", "gold", "level", "war", "week", "activity", "bounties", "disband" };

        private static void RegisterAdminCommands() {
            _ = new ModCommand($"{CommandPrefix}-admin",
                "Format: [save | list | fakeclan name members | fakeapply clan count | xp clan amount | gold clan amount | level clan [+n/-n/n] | war clanA clanB start/end | week +n/-n/reset | activity list/win id/fail id/reset clan [type] | bounties [clan] | disband clan] Server-side clan administration.",
                Admin, "admin", AdminOptions,
                serverAuthoritative: true, requiresAdmin: true);
        }

        private static List<string> AdminOptions(string[] input) {
            if (input.Length <= 2) { return AdminSubcommands.ToList(); }
            string sub = input[1].ToLowerInvariant();
            if ((input.Length == 3 && (sub == "fakeapply" || sub == "xp" || sub == "gold" || sub == "level" || sub == "disband" || sub == "war" || sub == "bounties"))
                || (input.Length == 4 && sub == "war")) {
                return ClanClient.Clans.Values.Select(c => c.Name.Replace(' ', '_')).ToList();
            }
            if (sub == "level" && input.Length == 4) {
                return new List<string> { "+1", "-1" }.Concat(Enumerable.Range(1, ClansSettings.Levels.MaxLevel).Select(l => l.ToString())).ToList();
            }
            if (sub == "week" && input.Length == 3) { return new List<string> { "+1", "-1", "reset" }; }
            if (sub == "activity" && input.Length == 3) { return new List<string> { "list", "win", "fail", "reset" }; }
            if (sub == "activity" && input.Length == 4 && input[2].ToLowerInvariant() == "reset") {
                return ClanClient.Clans.Values.Select(c => c.Name.Replace(' ', '_')).ToList();
            }
            if (sub == "activity" && input.Length == 5 && input[2].ToLowerInvariant() == "reset") { return TerminalArgs.Names<ActivityType>(); }
            if (sub == "war" && input.Length == 5) { return new List<string> { "start", "end" }; }
            return new List<string>();
        }

        // Clan names in admin commands may use _ for spaces.
        private static ClanRecord AdminClan(ModCommandArgs args, int index) {
            string name = args.Args.GetString(index).Replace('_', ' ');
            ClanRecord clan = ClanRules.ClanByName(name);
            if (clan == null) { args.Output.Error($"No clan called '{name}'."); }
            return clan;
        }

        private static void Admin(ModCommandArgs args) {
            if (ClanStore.EnsureLoaded() == false) { args.Output.Error("Clans data is not loaded on this server."); return; }
            ClansWorldState state = ClanStore.State;
            string sub = args.Args.GetString(0).ToLowerInvariant();
            switch (sub) {
                case "save":
                    ClanStore.Save(force: true);
                    args.Output.Info("Clans data saved.");
                    break;
                case "list":
                    foreach (ClanRecord clan in state.Clans.Values) {
                        args.Output.Info($"{clan.Id}: {clan.Name} - level {ClanRules.Level(clan)} ({clan.Xp} xp), {clan.Members.Count} members, treasury {clan.Treasury}, {clan.Storage.SharedTabs.Count} tabs");
                    }
                    args.Output.Detail($"{state.Accounts.Count} accounts, {Sessions.Count} online, {state.Wars.Count} wars, {state.Mailbox.Sum(m => m.Value.Count)} undelivered");
                    break;
                case "fakeclan": {
                    string name = args.Args.GetString(1).Replace('_', ' ');
                    int members = Math.Max(1, Math.Min(50, args.Args.GetInt(2, 3)));
                    string error = ClanRules.CheckName(name);
                    if (error != null) { args.Output.Error($"'{name}' cannot be used: {Loc(error)}"); return; }
                    ClanRecord clan = ClanService.Found($"fake:{name}:0", $"{name} Leader", name, "A clan made for testing.", ClansSettings.Emblems.Default.Clone(), true);
                    for (int i = 1; i < members; i++) {
                        ClanService.AddMember(clan, $"fake:{name}:{i}", $"{name} Member {i}", ClanRules.EntryRank(clan).Id);
                    }
                    args.Output.Info($"Made {name} with {members} fake members.");
                    break;
                }
                case "fakeapply": {
                    ClanRecord clan = AdminClan(args, 1);
                    if (clan == null) { return; }
                    int count = Math.Max(1, Math.Min(20, args.Args.GetInt(2, 1)));
                    for (int i = 0; i < count; i++) {
                        string account = $"fake:applicant:{clan.Id}:{clan.Applications.Count + i}";
                        AccountRecord record = ClanRules.Account(account, create: true);
                        record.LastCharacterName = $"Applicant {clan.Applications.Count + 1}";
                        record.PendingApplications.Add(clan.Id);
                        clan.Applications.Add(new ApplicationRecord { Account = account, CharacterName = record.LastCharacterName, Message = "Let me in!", CreatedUtc = ClanStore.Now });
                    }
                    Broadcaster.MarkDetail(clan.Id);
                    ClanStore.MarkDirty();
                    args.Output.Info($"Added {count} fake applications to {clan.Name}.");
                    break;
                }
                case "xp": {
                    ClanRecord clan = AdminClan(args, 1);
                    if (clan == null) { return; }
                    int amount = args.Args.GetInt(2, 0);
                    if (amount <= 0) { args.Output.Error("Give the experience to add as a whole number above 0."); return; }
                    ClanRules.AddXp(clan, amount, "$clans_reason_admin");
                    // Experience never raises the level by itself, so say how far the next one is.
                    int level = ClanRules.Level(clan);
                    ClanLevelDef next = ClanRules.NextLevel(clan);
                    string progress = next == null ? "the top level"
                        : clan.Xp >= next.XpRequired ? $"has the experience for level {level + 1}"
                        : $"{next.XpRequired - clan.Xp} short of level {level + 1}";
                    args.Output.Info($"{clan.Name} now has {clan.Xp} xp (level {level}, {progress}).");
                    break;
                }
                case "gold": {
                    ClanRecord clan = AdminClan(args, 1);
                    if (clan == null) { return; }
                    ClanRules.AddTreasury(clan, args.Args.GetInt(2, 0));
                    args.Output.Info($"{clan.Name}'s treasury is now {clan.Treasury}.");
                    break;
                }
                case "level": {
                    ClanRecord clan = AdminClan(args, 1);
                    if (clan == null) { return; }
                    // +n and -n move the level, a bare number sets it, and no number raises it by one. The clan
                    // skips the experience, resources and treasury cost a member's upgrade would need.
                    string arg = args.Args.GetString(2, "+1");
                    if (int.TryParse(arg, out int value) == false) { args.Output.Error("Use +n or -n to move the level, or a number to set it."); return; }
                    int current = ClanRules.Level(clan);
                    int max = ClansSettings.Levels.MaxLevel;
                    bool relative = arg.StartsWith("+") || arg.StartsWith("-");
                    int level = Math.Max(1, Math.Min(max, relative ? current + value : value));
                    if (level == current) { args.Output.Error($"{clan.Name} is already level {current} (levels go from 1 to {max})."); return; }
                    clan.Level = level;
                    clan.LevelContributions.Clear();
                    clan.Xp = Math.Max(clan.Xp, ClansSettings.Levels.Get(level).XpRequired);
                    if (level > current) {
                        ClanRules.Log(clan, "$clans_log_levelup", level.ToString());
                        ClanService.NotifyClan(clan, null, "$clans_notice_levelup", level.ToString());
                    } else {
                        ClanRules.Log(clan, "$clans_log_level_lowered", level.ToString());
                    }
                    Broadcaster.MarkDetail(clan.Id);
                    Broadcaster.MarkDirectory();
                    ClanStore.MarkDirty();
                    args.Output.Info($"{clan.Name} is now level {level}.");
                    break;
                }
                case "war": {
                    ClanRecord a = AdminClan(args, 1);
                    ClanRecord b = AdminClan(args, 2);
                    if (a == null || b == null || a.Id == b.Id) { return; }
                    WarRecord existing = WarService.Between(a.Id, b.Id);
                    if (args.Args.GetString(3, "start").Equals("end", StringComparison.OrdinalIgnoreCase)) {
                        if (existing == null) { args.Output.Error("They are not at war."); return; }
                        WarService.End(existing, WarEndReason.Peace, 0);
                        args.Output.Info($"Ended the war between {a.Name} and {b.Name}.");
                        return;
                    }
                    if (existing != null) { WarService.End(existing, WarEndReason.Cancelled, 0); }
                    long now = ClanStore.Now;
                    state.Wars.Add(new WarRecord {
                        Id = state.NextWarId++, ClanA = a.Id, ClanB = b.Id, State = WarState.Active, ProposedBy = "admin",
                        ProposedUtc = now, StartedUtc = now, EndsUtc = now + ClansConfig.WarDurationHours.Value * 3600L,
                        KillTarget = Math.Max(0, ClansConfig.WarKillTargetMin.Value),
                    });
                    Broadcaster.MarkDirectory();
                    ClanStore.MarkDirty();
                    args.Output.Info($"{a.Name} and {b.Name} are now at war.");
                    break;
                }
                case "week": {
                    string arg = args.Args.GetString(1, "+1");
                    if (arg == "reset") {
                        state.DebugWeekOffsetSeconds = 0;
                    } else if (int.TryParse(arg, out int weeks)) {
                        state.DebugWeekOffsetSeconds += weeks * WeeklyClock.Week;
                    } else {
                        args.Output.Error("Use +n, -n or reset.");
                        return;
                    }
                    WeeklyClock.DebugOffsetSeconds = state.DebugWeekOffsetSeconds;
                    ClanStore.MarkDirty();
                    ActivityService.MarkChanged();
                    args.Output.Info($"Week clock shifted by {state.DebugWeekOffsetSeconds / WeeklyClock.Week} weeks.");
                    break;
                }
                case "activity": {
                    string action = args.Args.GetString(1, "list").ToLowerInvariant();
                    if (action == "list") {
                        List<string> lines = ActivityService.AdminDescribe();
                        if (lines.Count == 0) { args.Output.Info("No activities are running."); }
                        foreach (string line in lines) { args.Output.Info(line); }
                    } else if (action == "win" || action == "fail") {
                        long id = args.Args.GetInt(2, 0);
                        if (ActivityService.AdminEnd(id, action == "win") == false) { args.Output.Error($"No running activity {id}. See 'activity list'."); return; }
                        args.Output.Info(action == "win" ? $"Activity {id} completed; rewards paid." : $"Activity {id} failed.");
                    } else if (action == "reset") {
                        ClanRecord clan = AdminClan(args, 2);
                        if (clan == null) { return; }
                        ActivityType? only = null;
                        string typeArg = args.Args.GetString(3, "");
                        if (typeArg.Length > 0) {
                            if (Enum.TryParse(typeArg, true, out ActivityType parsed) == false || Enum.IsDefined(typeof(ActivityType), parsed) == false) {
                                args.Output.Error($"No activity type '{typeArg}'. Use one of: {string.Join(", ", TerminalArgs.Names<ActivityType>())}.");
                                return;
                            }
                            only = parsed;
                        }
                        List<ActivityType> cleared = ActivityService.AdminResetLockouts(clan, only);
                        if (cleared.Count == 0) {
                            args.Output.Info($"{clan.Name} had not done {(only == null ? "any activity" : only.ToString())} this week, so nothing was locked.");
                        } else {
                            args.Output.Info($"{clan.Name} can start {string.Join(", ", cleared)} again this week.");
                        }
                        foreach (ActivityInstance running in ActivityService.AdminRunning(clan, only)) {
                            args.Output.Warning($"{running.Type} (activity {running.Id}) is still running, so a new one cannot start until it ends ('activity fail {running.Id}' ends it).");
                        }
                    } else {
                        args.Output.Error("Use list, win id, fail id or reset clan [type].");
                    }
                    break;
                }
                case "bounties": {
                    ClanRecord only = null;
                    if (string.IsNullOrEmpty(args.Args.GetString(1, "")) == false) {
                        only = AdminClan(args, 1);
                        if (only == null) { return; }
                    }
                    List<string> lines = BountyRelay.Describe(only).ToList();
                    if (lines.Count == 0) { args.Output.Info("No bounties are shared."); }
                    foreach (string line in lines) { args.Output.Info(line); }
                    break;
                }
                case "disband": {
                    ClanRecord clan = AdminClan(args, 1);
                    if (clan == null) { return; }
                    ClanService.DisbandClan(clan, "an admin");
                    args.Output.Info($"Disbanded {clan.Name}.");
                    break;
                }
                default:
                    args.Output.Error($"Unknown subcommand '{sub}'. Use one of: {string.Join(", ", AdminSubcommands)}.");
                    break;
            }
        }
    }
}
