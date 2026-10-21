using Clans.Client;
using Clans.Core;
using Clans.Net;
using Clans.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI.Pages {
    // Every member with their rank, and what the viewer may do to each. The leader edits ranks here too.
    internal sealed class MembersPage : ClanPage {
        internal override string Id => "members";
        internal override string Prefab => "Clans_Page_Members";
        internal override bool AvailableNow() => ClanClient.InClan;

        private static readonly Color OnlineColor = new Color(0.4f, 0.85f, 0.35f, 1f);
        private static readonly Color OfflineColor = new Color(0.35f, 0.33f, 0.3f, 1f);

        private TemplateList members;
        private Transform editor;
        private TemplateList editorRanks;
        // The ranks being edited, top to bottom; new ones have Id 0.
        private List<RankDef> staged;
        // Each rank's stored name, so an untouched default ("$clans_rank_elder") is sent back as its token
        // and stays translated for everyone.
        private readonly Dictionary<int, string> originalNames = new Dictionary<int, string>();
        private readonly Dictionary<GameObject, TemplateList> flagLists = new Dictionary<GameObject, TemplateList>();
        private bool saving;

        protected override void Bind() {
            members = new TemplateList(UiBind.Find(T, "List"));
            editor = UiBind.Find(T, "RankEditor");
            editorRanks = new TemplateList(UiBind.Find(editor, "Ranks"));
            UiBind.OnClick(T, "EditRanks", OpenEditor);
            UiBind.OnClick(editor, "Buttons/Add", AddRank);
            UiBind.OnClick(editor, "Buttons/Cancel", CloseEditor);
            UiBind.OnClick(editor, "Buttons/Save", SaveRanks);
        }

        internal override void OnHide() => CloseEditor();

        internal override void Refresh() {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null) { return; }
            int max = ClansSettings.Levels.Get(clan.Level).MaxMembers;
            UiBind.SetText(T, "Count", UiBind.Loc("$clans_members_count", clan.Members.Count, max));
            UiBind.SetActive(T, "EditRanks", ClanClient.HasPermission(ClanPermission.EditRanks));

            int myIndex = clan.RankIndex(ClanClient.Me.RankId);
            List<MemberInfo> sorted = clan.Members
                .OrderBy(m => clan.RankIndex(m.RankId))
                .ThenByDescending(m => m.Online)
                .ThenBy(m => m.Name)
                .ToList();
            members.Sync(sorted.Count, (item, i) => FillMember(item.transform, clan, sorted[i], myIndex));

            if (editor != null && editor.gameObject.activeSelf && saving == false) { FillEditor(); }
        }

        private void FillMember(Transform item, ClanDetailInfo clan, MemberInfo member, int myIndex) {
            int index = clan.RankIndex(member.RankId);
            bool self = member.Account == ClanClient.MyAccount;
            Image dot = UiBind.Get<Image>(item, "Online");
            if (dot != null) { dot.color = member.Online ? OnlineColor : OfflineColor; }
            UiBind.SetText(item, "Name", member.Name);
            UiBind.SetText(item, "Rank", UiFormat.RankName(clan.Rank(member.RankId)));
            UiBind.SetText(item, "LastSeen", member.Online ? UiBind.Loc("$clans_members_online") : UiFormat.Ago(member.LastSeenUtc));

            // The same limits the server applies: only members below you, only to ranks below your own.
            bool below = self == false && myIndex >= 0 && myIndex < index;
            bool canPromote = below && ClanClient.HasPermission(ClanPermission.Promote) && index - 1 > myIndex && index - 1 > 0;
            bool canDemote = below && ClanClient.HasPermission(ClanPermission.Demote) && index + 1 < clan.Ranks.Count;
            bool canKick = below && ClanClient.HasPermission(ClanPermission.Kick);
            bool canTransfer = self == false && myIndex == 0;
            UiBind.SetActive(item, "Actions/Promote", canPromote);
            UiBind.SetActive(item, "Actions/Demote", canDemote);
            UiBind.SetActive(item, "Actions/Kick", canKick);
            UiBind.SetActive(item, "Actions/Transfer", canTransfer);

            string account = member.Account;
            string name = member.Name;
            if (canPromote) { UiBind.OnClick(item, "Actions/Promote", () => ClientActions.SetRank(account, clan.Ranks[index - 1].Id, ClansWindow.ReportFailure)); }
            if (canDemote) { UiBind.OnClick(item, "Actions/Demote", () => ClientActions.SetRank(account, clan.Ranks[index + 1].Id, ClansWindow.ReportFailure)); }
            if (canKick) {
                UiBind.OnClick(item, "Actions/Kick", () => ConfirmDialog.Ask(ClansWindow.Root, "$clans_members_kick", UiBind.Loc("$clans_members_kick_body", name),
                    () => ClientActions.Kick(account, ClansWindow.ReportFailure)));
            }
            if (canTransfer) {
                UiBind.OnClick(item, "Actions/Transfer", () => ConfirmDialog.Ask(ClansWindow.Root, "$clans_members_transfer", UiBind.Loc("$clans_members_transfer_body", name),
                    () => ClientActions.TransferLeader(account, ClansWindow.ReportFailure)));
            }
        }

        // ---------------------------------------------------------------- rank editor

        private void OpenEditor() {
            ClanDetailInfo clan = ClanClient.MyClan;
            if (clan == null || editor == null) { return; }
            staged = clan.Ranks.Select(r => r.Clone()).ToList();
            originalNames.Clear();
            foreach (RankDef rank in staged) {
                originalNames[rank.Id] = rank.Name;
                rank.Name = UiBind.Loc(rank.Name);
            }
            editor.gameObject.SetActive(true);
            FillEditor();
        }

        private void CloseEditor() {
            if (editor != null) { editor.gameObject.SetActive(false); }
            staged = null;
            saving = false;
        }

        private void AddRank() {
            if (staged == null) { return; }
            if (staged.Count >= ClansSettings.Ranks.MaxRanks) {
                ClansWindow.Status(UiBind.Loc("$clans_err_rank_count", ClansSettings.Ranks.MaxRanks), true);
                return;
            }
            RankDef entry = staged[staged.Count - 1];
            staged.Insert(staged.Count - 1, new RankDef { Id = 0, Name = UiBind.Loc("$clans_ranks_new"), Permissions = entry.Permissions });
            FillEditor();
        }

        private void Move(int index, int step) {
            int target = index + step;
            // The leader rank stays first and the entry rank last.
            if (index <= 0 || index >= staged.Count - 1 || target <= 0 || target >= staged.Count - 1) { return; }
            RankDef rank = staged[index];
            staged.RemoveAt(index);
            staged.Insert(target, rank);
            FillEditor();
        }

        private void Remove(int index) {
            if (index <= 0 || index >= staged.Count - 1) { return; }
            staged.RemoveAt(index);
            FillEditor();
        }

        private void FillEditor() {
            if (staged == null) { return; }
            ClanPermission[] all = ClanPermissions.Split(ClanPermissions.All).ToArray();
            editorRanks.Sync(staged.Count, (item, i) => {
                RankDef rank = staged[i];
                bool leader = i == 0;
                bool entry = i == staged.Count - 1;
                Transform head = UiBind.Find(item.transform, "Head");

                TMP_InputField name = UiBind.Get<TMP_InputField>(head, "Name");
                if (name != null) {
                    name.onValueChanged.RemoveAllListeners();
                    name.SetTextWithoutNotify(rank.Name);
                    name.onValueChanged.AddListener(text => rank.Name = text);
                }
                UiBind.SetText(head, "Note", leader ? UiBind.Loc("$clans_ranks_leader_note") : (entry ? UiBind.Loc("$clans_ranks_entry_note") : ""));
                bool movable = leader == false && entry == false;
                int index = i;
                UiBind.SetActive(head, "Up", movable && index > 1);
                UiBind.SetActive(head, "Down", movable && index < staged.Count - 2);
                UiBind.SetActive(head, "Remove", movable);
                UiBind.OnClick(head, "Up", () => Move(index, -1));
                UiBind.OnClick(head, "Down", () => Move(index, +1));
                UiBind.OnClick(head, "Remove", () => Remove(index));

                if (flagLists.TryGetValue(item, out TemplateList flags) == false) {
                    flags = new TemplateList(UiBind.Find(item.transform, "Perms"), "Flag");
                    flagLists[item] = flags;
                }
                flags.Sync(all.Length, (flag, f) => {
                    ClanPermission permission = all[f];
                    UiBind.SetText(flag.transform, "Label", UiBind.Loc(ClanPermissions.Token(permission)));
                    Toggle toggle = UiBind.Get<Toggle>(flag.transform, "Toggle");
                    if (toggle == null) { return; }
                    toggle.onValueChanged.RemoveAllListeners();
                    toggle.SetIsOnWithoutNotify(leader || rank.Permissions.Has(permission));
                    toggle.interactable = leader == false;
                    toggle.onValueChanged.AddListener(on => {
                        if (on) { rank.Permissions |= permission; } else { rank.Permissions &= ~permission; }
                    });
                });
            });
        }

        private void SaveRanks() {
            if (staged == null || saving) { return; }
            saving = true;
            List<RankDef> send = staged.Select(r => r.Clone()).ToList();
            foreach (RankDef rank in send) {
                if (rank.Id > 0 && originalNames.TryGetValue(rank.Id, out string original) && rank.Name == UiBind.Loc(original)) {
                    rank.Name = original;
                }
            }
            ClientActions.EditRanks(send, result => {
                saving = false;
                if (result.Ok) {
                    CloseEditor();
                    ClansWindow.Status(UiBind.Loc("$clans_ranks_saved"));
                } else {
                    ClansWindow.ReportFailure(result);
                }
            });
        }
    }
}
