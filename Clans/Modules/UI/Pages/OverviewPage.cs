using Clans.Client;
using Clans.Core;
using Clans.Net;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI.Pages {
    // The clan at a glance: emblem, status, treasury, recent activity, what each rank may do, and
    // applications waiting for an answer.
    internal sealed class OverviewPage : ClanPage {
        internal override string Id => "overview";
        internal override string Prefab => "Clans_Page_Overview";
        internal override bool AvailableNow() => ClanClient.InClan;

        private TemplateList log;
        private TemplateList ranks;
        private TemplateList applications;
        private Toggle recruiting;
        private GameObject emblemModal;

        protected override void Bind() {
            log = new TemplateList(UiBind.Find(T, "Middle/Log"));
            ranks = new TemplateList(UiBind.Find(T, "Right/Ranks"));
            applications = new TemplateList(UiBind.Find(T, "Right/Applications"));
            recruiting = UiBind.Get<Toggle>(T, "Left/RecruitingRow/Recruiting");
            if (recruiting != null) { recruiting.onValueChanged.AddListener(SetRecruiting); }

            UiBind.OnClick(T, "Left/EditEmblem", EditEmblem);
            UiBind.OnClick(T, "Left/StatusButtons/EditStatus", EditStatus);
            UiBind.OnClick(T, "Left/StatusButtons/PostInvite", () => ChatClient.PostInvite(ChatLinkKind.ClanInvite, ChatChannel.Global, result => {
                if (result.Ok) { ClansWindow.Status(UiBind.Loc("$clans_chat_invite_posted")); } else { ClansWindow.ReportFailure(result); }
            }));
            UiBind.OnClick(T, "Left/TreasuryButtons/Donate", Donate);
            UiBind.OnClick(T, "Left/TreasuryButtons/Withdraw", Withdraw);
            UiBind.OnClick(T, "Left/MembershipButtons/Leave", Leave);
            UiBind.OnClick(T, "Left/MembershipButtons/Disband", Disband);
        }

        internal override void OnHide() {
            CloseEmblemModal();
        }

        internal override void Refresh() {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) { return; }
            bool leader = clan.RankIndex(ClanClient.Me.RankId) == 0;

            EmblemView.Apply(UiBind.Find(T, "Left/EmblemRow/Emblem"), clan.Emblem);
            UiBind.SetText(T, "Left/Description", string.IsNullOrEmpty(clan.Description) ? UiBind.Loc("$clans_overview_no_description") : clan.Description);
            UiBind.SetText(T, "Left/Status", string.IsNullOrEmpty(clan.Status) ? UiBind.Loc("$clans_overview_no_status") : clan.Status);
            UiBind.SetText(T, "Left/Treasury", UiBind.Loc("$clans_overview_treasury", clan.Treasury));
            UiBind.SetActive(T, "Left/EditEmblem", ClanClient.HasPermission(ClanPermission.EditEmblem));
            bool canPost = ClanClient.HasPermission(ClanPermission.Invite) && ChatClient.Available(ChatChannel.Global);
            UiBind.SetActive(T, "Left/StatusButtons", ClanClient.HasPermission(ClanPermission.EditInfo) || canPost);
            UiBind.SetActive(T, "Left/StatusButtons/EditStatus", ClanClient.HasPermission(ClanPermission.EditInfo));
            UiBind.SetActive(T, "Left/StatusButtons/PostInvite", canPost);
            UiBind.SetActive(T, "Left/TreasuryButtons/Donate", ClanClient.HasPermission(ClanPermission.TreasuryDonate));
            UiBind.SetActive(T, "Left/TreasuryButtons/Withdraw", ClanClient.HasPermission(ClanPermission.TreasuryWithdraw));
            UiBind.SetActive(T, "Left/MembershipButtons/Disband", leader);
            if (recruiting != null) {
                recruiting.SetIsOnWithoutNotify(clan.Recruiting);
                recruiting.interactable = ClanClient.HasPermission(ClanPermission.EditInfo);
            }

            // Newest first.
            List<LogInfo> entries = Enumerable.Reverse(clan.Log).ToList();
            log.Sync(entries.Count, (item, i) => {
                LogInfo entry = entries[i];
                UiBind.SetText(item.transform, "Time", UiFormat.Ago(entry.Utc));
                UiBind.SetText(item.transform, "Text", new NetResult(true, entry.Token, entry.Args, null).Message());
            });

            ranks.Sync(clan.Ranks.Count, (item, i) => {
                RankDef rank = clan.Ranks[i];
                UiBind.SetText(item.transform, "Name", UiFormat.RankName(rank));
                string perms = i == 0
                    ? UiBind.Loc("$clans_perm_everything")
                    : string.Join(", ", ClanPermissions.Split(rank.Permissions).Select(p => UiBind.Loc(ClanPermissions.Token(p))));
                UiBind.SetText(item.transform, "Perms", perms.Length == 0 ? UiBind.Loc("$clans_perm_nothing") : perms);
            });

            bool canResolve = ClanClient.HasPermission(ClanPermission.ManageApplications);
            UiBind.SetActive(T, "Right/Applications/None", clan.Applications.Count == 0);
            applications.Sync(clan.Applications.Count, (item, i) => {
                ApplicationInfo application = clan.Applications[i];
                UiBind.SetText(item.transform, "Name", application.Name);
                UiBind.SetText(item.transform, "Message", application.Message);
                UiBind.SetActive(item.transform, "Accept", canResolve);
                UiBind.SetActive(item.transform, "Decline", canResolve);
                string account = application.Account;
                UiBind.OnClick(item.transform, "Accept", () => ClientActions.ResolveApplication(account, true, ClansWindow.ReportFailure));
                UiBind.OnClick(item.transform, "Decline", () => ClientActions.ResolveApplication(account, false, ClansWindow.ReportFailure));
            });
        }

        private void SetRecruiting(bool open) {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) { return; }
            ClientActions.EditInfo(clan.Status, clan.Description, open, ClansWindow.ReportFailure);
        }

        private void EditStatus() {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) { return; }
            ConfirmDialog.Prompt(ClansWindow.Root, "$clans_overview_edit_status", "$clans_overview_status_body", clan.Status, 200, false,
                text => ClientActions.EditInfo(text, clan.Description, clan.Recruiting, result => {
                    if (result.Ok == false) { ClansWindow.ReportFailure(result); return; }
                    ClansWindow.Status(UiBind.Loc("$clans_overview_status_saved"));
                    // The page shows the new status (as the server cleaned it) and its log line straight away.
                    ClientActions.RequestDetail();
                }));
        }

        private void Donate() {
            ConfirmDialog.Prompt(ClansWindow.Root, "$clans_overview_donate", UiBind.Loc("$clans_overview_donate_body", ClientActions.CoinsCarried()), "", 7, true, text => {
                if (UiFormat.TryParseCoins(text, out int coins)) {
                    ClientActions.Donate(coins, result => {
                        if (result.Ok) { ClansWindow.Status(UiBind.Loc("$clans_overview_donated", coins)); } else { ClansWindow.ReportFailure(result); }
                    });
                }
            });
        }

        private void Withdraw() {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) { return; }
            ConfirmDialog.Prompt(ClansWindow.Root, "$clans_overview_withdraw", UiBind.Loc("$clans_overview_withdraw_body", clan.Treasury), "", 7, true, text => {
                if (UiFormat.TryParseCoins(text, out int coins)) { ClientActions.WithdrawTreasury(coins, ClansWindow.ReportFailure); }
            });
        }

        private void Leave() {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) { return; }
            ConfirmDialog.Ask(ClansWindow.Root, "$clans_overview_leave", UiBind.Loc("$clans_overview_leave_body", clan.Name),
                () => ClientActions.Leave(ClansWindow.ReportFailure));
        }

        private void Disband() {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) { return; }
            ConfirmDialog.Prompt(ClansWindow.Root, "$clans_overview_disband", UiBind.Loc("$clans_overview_disband_body", clan.Name), "", 32, false,
                typed => ClientActions.Disband(typed, ClansWindow.ReportFailure));
        }

        // The emblem editor in a dialog of its own, over the window.
        private void EditEmblem() {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null || ClansWindow.Root == null) { return; }
            CloseEmblemModal();
            emblemModal = ModalHost.Create(ClansWindow.Root, new Vector2(540f, 640f), out Transform body);
            EmblemEditor.Create(body, clan.Emblem,
                emblem => {
                    CloseEmblemModal();
                    ClientActions.EditEmblem(emblem, ClansWindow.ReportFailure);
                },
                CloseEmblemModal);
        }

        private void CloseEmblemModal() {
            if (emblemModal != null) { Object.Destroy(emblemModal); }
            emblemModal = null;
        }
    }

    // A dimmed full-window overlay with a wood panel, for content that is not a page.
    internal static class ModalHost {
        internal static GameObject Create(Transform parent, Vector2 size, out Transform body) {
            GameObject overlay = new GameObject("Clans_Modal", typeof(RectTransform));
            overlay.transform.SetParent(parent, false);
            RectTransform ort = (RectTransform)overlay.transform;
            ort.anchorMin = Vector2.zero;
            ort.anchorMax = Vector2.one;
            ort.offsetMin = ort.offsetMax = Vector2.zero;
            Image dim = overlay.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);

            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(overlay.transform, false);
            RectTransform prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = size;
            Image back = panel.AddComponent<Image>();
            back.sprite = Jotunn.Managers.GUIManager.Instance?.GetSprite("woodpanel_trophys");
            back.type = Image.Type.Sliced;

            GameObject inner = new GameObject("Body", typeof(RectTransform));
            inner.transform.SetParent(panel.transform, false);
            RectTransform irt = (RectTransform)inner.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(16f, 16f);
            irt.offsetMax = new Vector2(-16f, -16f);
            body = inner.transform;
            overlay.transform.SetAsLastSibling();
            return overlay;
        }
    }
}
