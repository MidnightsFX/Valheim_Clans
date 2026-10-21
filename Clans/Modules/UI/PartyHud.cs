using Clans.Client;
using Clans.Net;
using Clans.Settings;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI {
    // On the HUD, at the left: your squad (and the rest of your raid), and a notice for each new party
    // invitation. Lives under Hud.m_rootObject, so it hides with the rest of the HUD. The player can move the
    // frame elsewhere (see HudLayout); the notice goes with it.
    internal sealed class PartyHud : MonoBehaviour {
        private const float RefreshSeconds = 0.25f;
        private const float FadeSeconds = 0.6f;
        // The frame's outline in the layout editor while you are in no party.
        private static readonly Vector2 EmptySize = new Vector2(240f, 140f);

        private static PartyHud instance;

        private GameObject frame;
        private RectTransform frameRect;
        private HudLayout.Movable movable;
        // The prefab's spot for the frame, and where the notice sits from it.
        private Vector2 defaultFrame;
        private Vector3 toastOffset;
        private float toastGap;
        private TemplateList members;
        private TemplateList raid;
        private GameObject toast;
        private CanvasGroup toastGroup;
        private Image toastTimer;
        private float toastShown;
        private float toastUntil;
        private readonly HashSet<long> toasted = new HashSet<long>();
        private float refreshTimer;

        // Called every frame from the client tick; builds the HUD pieces once the HUD exists.
        internal static void Ensure() {
            if (instance != null || Hud.instance == null || Hud.instance.m_rootObject == null) { return; }
            GameObject framePrefab = ClansAssets.Prefab("Clans_HUD_PartyFrame");
            GameObject toastPrefab = ClansAssets.Prefab("Clans_HUD_InviteToast");
            if (framePrefab == null || toastPrefab == null) { return; }

            GameObject host = new GameObject("Clans_PartyHud", typeof(RectTransform));
            host.transform.SetParent(Hud.instance.m_rootObject.transform, false);
            RectTransform rect = (RectTransform)host.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            instance = host.AddComponent<PartyHud>();
            instance.Build(framePrefab, toastPrefab);
        }

        private void Build(GameObject framePrefab, GameObject toastPrefab) {
            frame = Instantiate(framePrefab, transform, false);
            ClansStyle.Apply(frame);
            members = new TemplateList(UiBind.Find(frame.transform, "Members"), "Member");
            raid = new TemplateList(UiBind.Find(frame.transform, "Raid"));
            frame.SetActive(false);

            toast = Instantiate(toastPrefab, transform, false);
            ClansStyle.Apply(toast);
            toastGroup = toast.GetComponent<CanvasGroup>();
            toastTimer = UiBind.Get<Image>(toast.transform, "Timer/Bar/Fill");
            toast.SetActive(false);

            frameRect = (RectTransform)frame.transform;
            RectTransform toastRect = (RectTransform)toast.transform;
            defaultFrame = frameRect.anchoredPosition;
            toastOffset = toastRect.localPosition - frameRect.localPosition;
            float frameTop = frameRect.localPosition.y + (1f - frameRect.pivot.y) * frameRect.rect.height;
            float toastBottom = toastRect.localPosition.y - toastRect.pivot.y * toastRect.rect.height;
            toastGap = Mathf.Max(4f, toastBottom - frameTop);

            PartyClient.InvitesChanged += OnInvites;
            ClansConfig.PartyFramePosition.SettingChanged += OnPositionChanged;

            movable = new HudLayout.Movable {
                Label = "$clans_layout_party",
                Rect = frameRect,
                EmptySize = EmptySize,
                HasContent = () => frame.activeSelf,
                Place = PlaceFrame,
                Save = () => ClansConfig.PartyFramePosition.Value = HudLayout.Format(HudLayout.FractionOf(frameRect)),
                Reset = () => {
                    ClansConfig.PartyFramePosition.Value = "";
                    PlaceFrame();
                },
            };
            HudLayout.Register(movable);
            PlaceFrame();
        }

        private void OnDestroy() {
            PartyClient.InvitesChanged -= OnInvites;
            ClansConfig.PartyFramePosition.SettingChanged -= OnPositionChanged;
            HudLayout.Unregister(movable);
            if (instance == this) { instance = null; }
        }

        private void OnPositionChanged(object sender, System.EventArgs e) {
            if (HudLayout.Editing == false) { PlaceFrame(); }
        }

        // Where the player put the frame, or the prefab's spot at the left; kept on screen as it grows.
        private void PlaceFrame() {
            if (HudLayout.TryParse(ClansConfig.PartyFramePosition.Value, out Vector2 saved)) {
                HudLayout.ApplyFraction(frameRect, saved);
            } else if (frameRect.anchoredPosition != defaultFrame) {
                frameRect.anchoredPosition = defaultFrame;
            }
            HudLayout.Clamp(frameRect, movable.Size);
            FollowFrame();
        }

        // The invite notice sits just above the frame, or just below it when there is no room above.
        private void FollowFrame() {
            RectTransform toastRect = (RectTransform)toast.transform;
            Vector2 toastSize = toastRect.rect.size;
            Rect area = ((RectTransform)toastRect.parent).rect;
            Vector3 point = frameRect.localPosition + toastOffset;
            if (point.y + (1f - toastRect.pivot.y) * toastSize.y > area.yMax) {
                float frameBottom = frameRect.localPosition.y - frameRect.pivot.y * movable.Size.y;
                point.y = frameBottom - toastGap - (1f - toastRect.pivot.y) * toastSize.y;
            }
            point.z = 0f;
            if (toastRect.localPosition != point) { toastRect.localPosition = point; }
            HudLayout.Clamp(toastRect, toastSize);
        }

        private void OnInvites() {
            PartyClient.InviteInfo newest = null;
            foreach (PartyClient.InviteInfo invite in PartyClient.Invites) {
                if (toasted.Contains(invite.Id) == false && (newest == null || invite.ReceivedAt > newest.ReceivedAt)) { newest = invite; }
            }
            if (newest == null) {
                // The invite on show was answered or lapsed.
                if (toast.activeSelf && PartyClient.Invites.Count == 0) { toastUntil = Mathf.Min(toastUntil, Time.unscaledTime + FadeSeconds); }
                return;
            }
            toasted.Add(newest.Id);
            ShowToast(newest);
        }

        private void ShowToast(PartyClient.InviteInfo invite) {
            Transform t = toast.transform;
            UiBind.SetText(t, "Title", UiBind.Loc(invite.Raid ? "$clans_invite_raid_title" : "$clans_invite_party_title"));
            UiBind.SetText(t, "Body", invite.Raid
                ? UiBind.Loc("$clans_invite_raid_body", invite.FromName)
                : UiBind.Loc("$clans_invite_party_body", invite.FromName, invite.MemberCount, ClansConfig.MaxPartySize.Value));
            toastShown = Time.unscaledTime;
            toastUntil = Time.unscaledTime + Mathf.Max(2f, ClansConfig.InviteToastSeconds.Value);
            toast.SetActive(true);
        }

        private void Update() {
            UpdateToast();
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f) { return; }
            refreshTimer = RefreshSeconds;
            RefreshFrame();
        }

        private void UpdateToast() {
            if (toast == null || toast.activeSelf == false) { return; }
            float now = Time.unscaledTime;
            if (now >= toastUntil) {
                toast.SetActive(false);
                return;
            }
            float span = Mathf.Max(0.01f, toastUntil - toastShown);
            UiBind.SetBar(toastTimer, (toastUntil - now) / span);
            if (toastGroup != null) { toastGroup.alpha = Mathf.Clamp01((toastUntil - now) / FadeSeconds); }
        }

        private void RefreshFrame() {
            // While the layout is being edited, the editor has the frame; the notice still follows it.
            if (HudLayout.Editing) { FollowFrame(); } else { PlaceFrame(); }
            PartyClient.Squad mine = PartyClient.MySquad;
            bool show = ClansConfig.ShowPartyFrame.Value && mine != null && mine.Members.Count > 1;
            if (frame.activeSelf != show) { frame.SetActive(show); }
            if (show == false) { return; }

            Player local = Player.m_localPlayer;
            List<PartyClient.Member> others = mine.Members.FindAll(m => m.Account != ClanClient.MyAccount);
            UiBind.SetText(frame.transform, "Title", PartyClient.InRaid
                ? UiBind.Loc("$clans_frame_squad", PartyClient.SquadNumber(mine.PartyId))
                : UiBind.Loc("$clans_frame_party"));
            members.Sync(others.Count, (item, i) => {
                PartyClient.Member member = others[i];
                Transform t = item.transform;
                Read(member.Account, local, out float health, out float distance);
                UiBind.SetText(t, "Texts/Name", member.Name);
                UiBind.SetBar(UiBind.Get<Image>(t, "Texts/Health/Fill"), member.Online ? health : 0f);
                UiBind.SetText(t, "Distance", member.Online && distance >= 0f ? UiBind.Loc("$clans_party_distance", Mathf.RoundToInt(distance)) : "");
                UiBind.SetActive(t, "Leader", member.Account == mine.Leader);
                CanvasGroup group = item.GetComponent<CanvasGroup>();
                if (group != null) { group.alpha = member.Online ? 1f : 0.45f; }
                Transform emblem = UiBind.Find(t, "Emblem");
                ClanSummary clan = ClanClient.Clan(member.ClanId);
                if (emblem != null) {
                    emblem.gameObject.SetActive(clan != null);
                    if (clan != null) { EmblemView.Apply(emblem, clan.Emblem); }
                }
            });

            List<(int Squad, PartyClient.Member Member)> rest = new List<(int, PartyClient.Member)>();
            if (PartyClient.InRaid) {
                for (int s = 0; s < PartyClient.Squads.Count; s++) {
                    if (PartyClient.Squads[s].PartyId == mine.PartyId) { continue; }
                    foreach (PartyClient.Member m in PartyClient.Squads[s].Members) { rest.Add((s + 1, m)); }
                }
            }
            UiBind.SetActive(frame.transform, "RaidTitle", rest.Count > 0);
            if (rest.Count > 0) { UiBind.SetText(frame.transform, "RaidTitle", UiBind.Loc("$clans_frame_raid")); }
            raid.Sync(rest.Count, (item, i) => {
                (int squad, PartyClient.Member member) = rest[i];
                Read(member.Account, local, out float health, out _);
                UiBind.SetText(item.transform, "Name", $"{squad} {member.Name}");
                UiBind.SetBar(UiBind.Get<Image>(item.transform, "HealthHolder/Health/Fill"), member.Online ? health : 0f);
                CanvasGroup group = item.GetComponent<CanvasGroup>();
                if (group != null) { group.alpha = member.Online ? 1f : 0.45f; }
            });
        }

        // Health and distance: from the player's own object when it is loaded nearby, else from the server.
        private static void Read(string account, Player local, out float health, out float distance) {
            health = 1f;
            distance = -1f;
            Player player = PlayerMap.PlayerOf(account);
            if (player != null) {
                health = player.GetHealthPercentage();
                if (local != null) { distance = Vector3.Distance(local.transform.position, player.transform.position); }
                return;
            }
            if (PartyClient.TryCompanion(account, out PartyClient.Companion companion)) {
                health = companion.Health;
                if (local != null) { distance = Vector3.Distance(local.transform.position, companion.Position); }
            }
        }
    }

    // The invitations waiting for an answer, at the top of the inventory screen.
    internal sealed class InvitePanel : MonoBehaviour {
        private TemplateList list;
        private GameObject panel;

        internal static void Create(InventoryGui gui) {
            GameObject prefab = ClansAssets.Prefab("Clans_INV_InvitePanel");
            Transform parent = gui != null && gui.m_trophiesPanel != null ? gui.m_trophiesPanel.transform.parent : null;
            if (prefab == null || parent == null) { return; }
            GameObject go = Instantiate(prefab, parent, false);
            go.name = "Clans_InvitePanel";
            ClansStyle.Apply(go);
            InvitePanel panel = go.AddComponent<InvitePanel>();
            panel.panel = go;
            panel.list = new TemplateList(UiBind.Find(go.transform, "List"));
            PartyClient.InvitesChanged += panel.Refresh;
            panel.Refresh();
        }

        private void OnDestroy() {
            PartyClient.InvitesChanged -= Refresh;
        }

        private void Refresh() {
            if (panel == null) { return; }
            List<PartyClient.InviteInfo> invites = PartyClient.Invites;
            list.Sync(invites.Count, (item, i) => {
                PartyClient.InviteInfo invite = invites[i];
                string text = invite.Raid
                    ? UiBind.Loc("$clans_invite_raid_body", invite.FromName)
                    : UiBind.Loc("$clans_invite_party_body", invite.FromName, invite.MemberCount, ClansConfig.MaxPartySize.Value);
                UiBind.SetText(item.transform, "Text", text);
                long id = invite.Id;
                UiBind.OnClick(item.transform, "Accept", () => PartyActions.Respond(id, true, Report));
                UiBind.OnClick(item.transform, "Decline", () => PartyActions.Respond(id, false, Report));
            });
            UpdateVisibility();
        }

        private static void Report(NetResult result) {
            if (result.Ok == false) { ClanNotices.ShowError(result.Message()); }
        }

        // Answering removes the invite through the server's InviteClosed; until then the row stays.
        private void LateUpdate() => UpdateVisibility();

        private void UpdateVisibility() {
            bool show = PartyClient.Invites.Count > 0 && InventoryGui.IsVisible();
            // The panel object carries this component, so it hides its children rather than itself.
            for (int i = 0; i < panel.transform.childCount; i++) {
                GameObject child = panel.transform.GetChild(i).gameObject;
                if (child.activeSelf != show) { child.SetActive(show); }
            }
            Image back = panel.GetComponent<Image>();
            if (back != null && back.enabled != show) { back.enabled = show; }
        }
    }
}
