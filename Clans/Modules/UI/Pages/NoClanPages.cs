using Clans.Client;
using Clans.Net;
using Clans.Settings;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI.Pages {
    // Shown to players without a clan: found one, or apply to join one.
    internal sealed class NoClanPage : ClanPage {
        internal override string Id => "noclan";
        internal override string Prefab => "Clans_Page_NoClan";
        internal override bool AvailableNow() => ClanClient.InClan == false;

        protected override void Bind() {
            UiBind.OnClick(T, "Tile_Create", () => ClansWindow.ShowPage("create"));
            UiBind.OnClick(T, "Tile_Join", () => ClansWindow.ShowPage("join"));
        }

        internal override void Refresh() {
            List<string> names = ClanClient.Me.Applications
                .Select(id => ClanClient.Clan(id)?.Name)
                .Where(n => string.IsNullOrEmpty(n) == false)
                .ToList();
            UiBind.SetText(T, "Pending", names.Count == 0 ? "" : UiBind.Loc("$clans_noclan_pending", string.Join(", ", names)));
        }
    }

    internal sealed class CreateClanPage : ClanPage {
        internal override string Id => "create";
        internal override string Prefab => "Clans_Page_Create";
        internal override string TabId => "noclan";
        internal override bool AvailableNow() => ClanClient.InClan == false;

        private TMP_InputField nameField;
        private TMP_InputField descriptionField;
        private Toggle recruiting;
        private Button create;
        private EmblemEditor emblem;
        private bool busy;

        protected override void Bind() {
            nameField = UiBind.Get<TMP_InputField>(T, "Form/Name");
            descriptionField = UiBind.Get<TMP_InputField>(T, "Form/Description");
            recruiting = UiBind.Get<Toggle>(T, "Form/RecruitingRow/Recruiting");
            if (recruiting != null) { recruiting.isOn = true; }
            if (descriptionField != null) { descriptionField.characterLimit = 500; }
            if (nameField != null) { nameField.onValueChanged.AddListener(_ => Refresh()); }
            UiBind.OnClick(T, "Form/Buttons/Back", () => ClansWindow.ShowPage("noclan"));
            create = UiBind.OnClick(T, "Form/Buttons/Create", Create);
            emblem = EmblemEditor.Create(UiBind.Find(T, "EmblemHost"), ClansSettings.Emblems.Default);
        }

        internal override void OnShow() {
            if (nameField != null) { nameField.characterLimit = ClansConfig.NameMaxLength.Value; }
            emblem?.Refresh();
            Refresh();
        }

        internal override void Refresh() {
            int fee = ClansConfig.CreationFee.Value;
            int carried = ClientActions.CoinsCarried();
            string feeText = fee <= 0 ? UiBind.Loc("$clans_create_free") : UiBind.Loc("$clans_create_fee", fee, carried);
            UiBind.SetText(T, "Form/Fee", feeText);
            string name = nameField != null ? nameField.text.Trim() : "";
            bool nameOk = name.Length >= ClansConfig.NameMinLength.Value && name.Length <= ClansConfig.NameMaxLength.Value;
            UiBind.SetInteractable(create, busy == false && nameOk && carried >= fee);
        }

        private void Create() {
            if (busy || nameField == null) { return; }
            busy = true;
            Refresh();
            string name = nameField.text.Trim();
            ClientActions.CreateClan(name, descriptionField?.text ?? "", emblem?.Value ?? ClansSettings.Emblems.Default, recruiting == null || recruiting.isOn, result => {
                busy = false;
                if (result.Ok) {
                    nameField.text = "";
                    if (descriptionField != null) { descriptionField.text = ""; }
                    ClansWindow.Status(UiBind.Loc("$clans_create_done", name));
                    // Straight to the new clan's page, without waiting for the server's next push.
                    ClientActions.RequestDetail(_ => {
                        if (ClanClient.InClan) { ClansWindow.ShowPage("overview"); }
                    });
                } else {
                    ClansWindow.ReportFailure(result);
                }
                Refresh();
            });
        }
    }

    internal sealed class JoinClanPage : ClanPage {
        internal override string Id => "join";
        internal override string Prefab => "Clans_Page_Join";
        internal override string TabId => "noclan";
        internal override bool AvailableNow() => ClanClient.InClan == false;

        private TMP_InputField search;
        private TemplateList list;

        protected override void Bind() {
            search = UiBind.Get<TMP_InputField>(T, "Search");
            if (search != null) { search.onValueChanged.AddListener(_ => Refresh()); }
            UiBind.OnClick(T, "Back", () => ClansWindow.ShowPage("noclan"));
            list = new TemplateList(UiBind.Find(T, "List"));
        }

        internal override void Refresh() {
            string filter = search != null ? search.text.Trim() : "";
            HashSet<long> applied = new HashSet<long>(ClanClient.Me.Applications);
            List<ClanSummary> clans = ClanClient.Clans.Values
                .Where(c => filter.Length == 0 || c.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(c => applied.Contains(c.Id))
                .ThenByDescending(c => c.Recruiting)
                .ThenByDescending(c => c.Level)
                .ThenBy(c => c.Name)
                .ToList();
            UiBind.SetActive(T, "Empty", clans.Count == 0);
            list.Sync(clans.Count, (item, i) => Fill(item.transform, clans[i], applied.Contains(clans[i].Id)));
        }

        private void Fill(Transform item, ClanSummary clan, bool hasApplied) {
            EmblemView.Apply(UiBind.Find(item, "Emblem"), clan.Emblem);
            UiBind.SetText(item, "Texts/Name", clan.Name);
            UiBind.SetText(item, "Texts/Info", UiBind.Loc("$clans_join_info", clan.Level, clan.MemberCount, clan.LeaderName));
            UiBind.SetText(item, "Texts/Description", clan.Description);
            string state = hasApplied ? "$clans_join_applied" : (clan.Recruiting ? "" : "$clans_join_closed");
            UiBind.SetText(item, "State", UiBind.Loc(state));
            UiBind.SetActive(item, "Apply", hasApplied == false && clan.Recruiting);
            UiBind.SetActive(item, "Withdraw", hasApplied);
            long id = clan.Id;
            string clanName = clan.Name;
            UiBind.OnClick(item, "Apply", () => ConfirmDialog.Prompt(ClansWindow.Root, "$clans_join_apply_title",
                UiBind.Loc("$clans_join_apply_body", clanName), "", 200, false,
                message => ClientActions.Apply(id, message, result => {
                    if (result.Ok) { ClansWindow.Status(UiBind.Loc("$clans_join_applied_to", clanName)); } else { ClansWindow.ReportFailure(result); }
                })));
            UiBind.OnClick(item, "Withdraw", () => ClientActions.WithdrawApplication(id, ClansWindow.ReportFailure));
        }
    }
}
