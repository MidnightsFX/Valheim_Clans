using Clans.Client;
using Clans.Net;
using System;
using System.Collections.Generic;
using System.Linq;

#pragma warning disable IDE0130
namespace Clans {
#pragma warning restore IDE0130

    // Party and raid console commands: the same requests the Party page sends.
    internal static partial class TerminalManager {
        private static void RegisterPartyCommands() {
            _ = new ModCommand($"{CommandPrefix}-party", "Shows your party and raid, and any invitations waiting.", PartyState, "party");
            _ = new ModCommand($"{CommandPrefix}-party-invite", "Format: [player] Invites an online player to your party (starting one if needed).", args => {
                string account = FindOnline(args, args.Args.GetStringFrom(0));
                if (account != null) { PartyActions.Invite(account, Report(args, "Invitation sent.")); }
            }, "party", OnlineNameOptions);
            _ = new ModCommand($"{CommandPrefix}-party-accept", "Accepts your newest party or raid invitation.", args => Answer(args, true), "party");
            _ = new ModCommand($"{CommandPrefix}-party-decline", "Declines your newest party or raid invitation.", args => Answer(args, false), "party");
            _ = new ModCommand($"{CommandPrefix}-party-leave", "Leaves your party.", args => PartyActions.Leave(Report(args, "You left the party.")), "party");
            _ = new ModCommand($"{CommandPrefix}-party-kick", "Format: [player] Removes a member from your party (leader only).", args => {
                PartyClient.Member member = FindPartyMember(args, args.Args.GetStringFrom(0));
                if (member != null) { PartyActions.Kick(member.Account, Report(args, $"Removed {member.Name}.")); }
            }, "party", PartyNameOptions);
            _ = new ModCommand($"{CommandPrefix}-party-lead", "Format: [player] Makes a member the party leader (leader only).", args => {
                PartyClient.Member member = FindPartyMember(args, args.Args.GetStringFrom(0));
                if (member != null) { PartyActions.SetLeader(member.Account, Report(args, $"{member.Name} now leads the party.")); }
            }, "party", PartyNameOptions);
            _ = new ModCommand($"{CommandPrefix}-raid-invite", "Format: [player] Invites another party's leader to bring their party into your raid.", args => {
                string account = FindOnline(args, args.Args.GetStringFrom(0));
                if (account != null) { PartyActions.InviteToRaid(account, Report(args, "Raid invitation sent.")); }
            }, "party", OnlineNameOptions);
            _ = new ModCommand($"{CommandPrefix}-raid-leave", "Takes your party out of its raid (party leader only).", args => PartyActions.LeaveRaid(Report(args, "Your party left the raid.")), "party");
            _ = new ModCommand($"{CommandPrefix}-raid-move", "Format: [player] [squad number] Moves a raid member into another squad (raid leader only).", args => {
                if (args.Length < 2) { args.Output.Error($"Format: {CommandPrefix}-raid-move [player] [squad number]"); return; }
                string name = string.Join(" ", args.Args.Take(args.Length - 1));
                int squad = args.Args.GetInt(args.Length - 1, 0);
                PartyClient.Member member = PartyClient.Squads.SelectMany(s => s.Members).FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
                if (member == null) { args.Output.Error($"No raid member called '{name}'."); return; }
                PartyActions.MoveToSquad(member.Account, squad, Report(args, $"Moved {member.Name} to squad {squad}."));
            }, "party");
        }

        private static void PartyState(ModCommandArgs args) {
            if (PartyClient.InParty == false) {
                args.Output.Info("You are not in a party.", log: false);
            }
            for (int i = 0; i < PartyClient.Squads.Count; i++) {
                PartyClient.Squad squad = PartyClient.Squads[i];
                string label = PartyClient.InRaid ? $"Squad {i + 1}" : "Party";
                if (squad.PartyId == PartyClient.PartyId) { label += " (yours)"; }
                args.Output.Info($"{label}:", log: false);
                foreach (PartyClient.Member member in squad.Members) {
                    string tags = (member.Account == squad.Leader ? " leader" : "") + (member.Account == PartyClient.RaidLeader ? " raid-leader" : "") + (member.Online ? "" : " offline");
                    args.Output.Detail($"  {member.Name}{tags}", log: false);
                }
            }
            foreach (PartyClient.InviteInfo invite in PartyClient.Invites) {
                args.Output.Detail($"Invitation: {(invite.Raid ? "raid" : "party")} from {invite.FromName}", log: false);
            }
        }

        private static void Answer(ModCommandArgs args, bool accept) {
            PartyClient.InviteInfo invite = PartyClient.Invites.OrderByDescending(i => i.ReceivedAt).FirstOrDefault();
            if (invite == null) { args.Output.Error("No invitation is waiting."); return; }
            PartyActions.Respond(invite.Id, accept, Report(args, accept ? $"Accepted {invite.FromName}'s invitation." : "Declined."));
        }

        private static string FindOnline(ModCommandArgs args, string name) {
            RosterEntry entry = ClanClient.Roster.Values.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (entry == null) { args.Output.Error($"No online player called '{name}'."); }
            return entry?.Account;
        }

        private static PartyClient.Member FindPartyMember(ModCommandArgs args, string name) {
            PartyClient.Member member = PartyClient.MySquad?.Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            if (member == null) { args.Output.Error($"No party member called '{name}'."); }
            return member;
        }

        private static List<string> OnlineNameOptions(string[] input) => ClanClient.Roster.Values.Select(r => r.Name).ToList();
        private static List<string> PartyNameOptions(string[] input) => PartyClient.MySquad?.Members.Select(m => m.Name).ToList() ?? new List<string>();
    }
}
