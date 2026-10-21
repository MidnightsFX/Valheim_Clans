using Clans.Client;
using Clans.Core;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System.Collections.Generic;
using System.Linq;

namespace Clans.UI.Pages {
    // Every other clan, those at war with yours (or with a declaration waiting) first.
    internal sealed class WarPage : ClanPage {
        internal override string Id => "war";
        internal override string Prefab => "Clans_Page_War";
        internal override bool AvailableNow() => ClanClient.InClan;

        private TemplateList list;

        protected override void Bind() {
            list = new TemplateList(UiBind.Find(T, "List"));
        }

        // War time left counts down.
        internal override void Tick() => Refresh();

        internal override void Refresh() {
            long mine = ClanClient.Me.ClanId;
            if (mine == 0) { return; }
            List<ClanSummary> clans = ClanClient.Clans.Values.Where(c => c.Id != mine).ToList();
            clans = clans
                .OrderBy(c => Order(Between(mine, c.Id), mine))
                .ThenByDescending(c => c.Level)
                .ThenBy(c => c.Name)
                .ToList();

            int atWar = ClanClient.Wars.Count(w => w.State == WarState.Active && w.Involves(mine));
            int waiting = ClanClient.Wars.Count(w => w.State == WarState.Proposed && w.ClanB == mine);
            string summary = atWar == 0 ? UiBind.Loc("$clans_war_summary_peace") : UiBind.Loc("$clans_war_summary", atWar);
            if (waiting > 0) { summary += " " + UiBind.Loc("$clans_war_summary_waiting", waiting); }
            UiBind.SetText(T, "Summary", summary);
            int max = ClansConfig.WarKillTargetMax.Value;
            UiBind.SetText(T, "Rules", max > 0
                ? UiBind.Loc("$clans_war_rules_targets", ClansConfig.WarDurationHours.Value / 24f, ClansConfig.WarKillTargetMin.Value, max)
                : UiBind.Loc("$clans_war_rules", ClansConfig.WarDurationHours.Value / 24f));
            UiBind.SetActive(T, "Empty", clans.Count == 0);

            bool canManage = ClanClient.HasPermission(ClanPermission.ManageWar) && ClansConfig.EnableWars.Value;
            list.Sync(clans.Count, (item, i) => Fill(item.transform, clans[i], Between(mine, clans[i].Id), mine, canManage));
        }

        private static WarSummary Between(long a, long b) => ClanClient.Wars.FirstOrDefault(w => w.Involves(a) && w.Involves(b));

        // Active wars, then declarations to answer, then declarations sent, then everyone else.
        private static int Order(WarSummary war, long mine) {
            if (war == null) { return 3; }
            if (war.State == WarState.Active) { return 0; }
            return war.ClanB == mine ? 1 : 2;
        }

        private void Fill(UnityEngine.Transform item, ClanSummary clan, WarSummary war, long mine, bool canManage) {
            EmblemView.Apply(UiBind.Find(item, "Emblem"), clan.Emblem);
            UiBind.SetText(item, "Texts/Name", clan.Name);
            UiBind.SetText(item, "Texts/Info", UiBind.Loc("$clans_join_info", clan.Level, clan.MemberCount, clan.LeaderName));
            UiBind.SetText(item, "Texts/State", Describe(war, mine));

            bool active = war != null && war.State == WarState.Active;
            bool incoming = war != null && war.State == WarState.Proposed && war.ClanB == mine;
            bool outgoing = war != null && war.State == WarState.Proposed && war.ClanA == mine;
            bool peaceFromThem = active && war.PeaceOfferedBy != 0 && war.PeaceOfferedBy != mine;
            bool peaceFromUs = active && war.PeaceOfferedBy == mine;
            UiBind.SetActive(item, "Buttons/Declare", canManage && war == null);
            UiBind.SetActive(item, "Buttons/Accept", canManage && incoming);
            UiBind.SetActive(item, "Buttons/Decline", canManage && incoming);
            UiBind.SetActive(item, "Buttons/Cancel", canManage && outgoing);
            UiBind.SetActive(item, "Buttons/OfferPeace", canManage && active && peaceFromThem == false && peaceFromUs == false);
            UiBind.SetActive(item, "Buttons/AcceptPeace", canManage && peaceFromThem);
            UiBind.SetActive(item, "Buttons/RejectPeace", canManage && peaceFromThem);

            long clanId = clan.Id;
            string name = clan.Name;
            long warId = war?.Id ?? 0;
            UiBind.OnClick(item, "Buttons/Declare", () => Declare(clanId, name));
            UiBind.OnClick(item, "Buttons/Accept", () => ConfirmDialog.Ask(ClansWindow.Root, "$clans_war_accept",
                UiBind.Loc("$clans_war_accept_body", name), () => WarActions.Respond(warId, true, ClansWindow.ReportFailure)));
            UiBind.OnClick(item, "Buttons/Decline", () => WarActions.Respond(warId, false, ClansWindow.ReportFailure));
            UiBind.OnClick(item, "Buttons/Cancel", () => WarActions.Cancel(warId, ClansWindow.ReportFailure));
            UiBind.OnClick(item, "Buttons/OfferPeace", () => WarActions.OfferPeace(warId, ClansWindow.ReportFailure));
            UiBind.OnClick(item, "Buttons/AcceptPeace", () => WarActions.RespondPeace(warId, true, ClansWindow.ReportFailure));
            UiBind.OnClick(item, "Buttons/RejectPeace", () => WarActions.RespondPeace(warId, false, ClansWindow.ReportFailure));
        }

        private static string Describe(WarSummary war, long mine) {
            if (war == null) { return UiBind.Loc("$clans_war_state_peace"); }
            if (war.State == WarState.Proposed) {
                return UiBind.Loc(war.ClanB == mine ? "$clans_war_state_incoming" : "$clans_war_state_outgoing") + Target(war);
            }
            int ours = war.KillsOf(mine);
            int theirs = war.KillsOf(war.Other(mine));
            string text = UiBind.Loc("$clans_war_state_active", ours, theirs) + Target(war);
            long left = war.EndsUtc - ClanClient.ServerNow;
            if (left > 0) { text += " " + UiBind.Loc("$clans_war_state_left", UiFormat.Span(left)); }
            if (war.PeaceOfferedBy != 0) {
                text += " " + UiBind.Loc(war.PeaceOfferedBy == mine ? "$clans_war_state_peace_sent" : "$clans_war_state_peace_received");
            }
            return text;
        }

        private static string Target(WarSummary war) => war.KillTarget > 0 ? " " + UiBind.Loc("$clans_war_state_target", war.KillTarget) : "";

        private static void Declare(long clanId, string name) {
            int min = ClansConfig.WarKillTargetMin.Value;
            int max = ClansConfig.WarKillTargetMax.Value;
            if (max <= 0) {
                ConfirmDialog.Ask(ClansWindow.Root, "$clans_war_declare", UiBind.Loc("$clans_war_declare_body", name),
                    () => WarActions.Declare(clanId, 0, ClansWindow.ReportFailure));
                return;
            }
            ConfirmDialog.Prompt(ClansWindow.Root, "$clans_war_declare", UiBind.Loc("$clans_war_declare_target_body", name, min, max), "0", 5, true, text => {
                int.TryParse(text, out int target);
                WarActions.Declare(clanId, target, result => {
                    if (result.Ok) { ClansWindow.Status(UiBind.Loc("$clans_war_declared", name)); } else { ClansWindow.ReportFailure(result); }
                });
            });
        }
    }
}
