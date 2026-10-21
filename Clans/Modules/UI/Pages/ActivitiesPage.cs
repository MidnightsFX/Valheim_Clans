using Clans.Client;
using Clans.Compat;
using Clans.Core;
using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI.Pages {
    // The weekly clan activities: one tile each for the Trophy Hunt, the Race and World Boss Battles, with
    // what each needs, how a running one is going, and (for ranks that may) difficulty, boss, start and cancel.
    internal sealed class ActivitiesPage : ClanPage {
        internal override string Id => "activities";
        internal override string Prefab => "Clans_Page_Activities";
        internal override bool AvailableNow() => ClanClient.InClan;

        private static readonly ActivityType[] Types = { ActivityType.TrophyHunt, ActivityType.Race, ActivityType.WorldBoss };

        private TemplateList tiles;
        private readonly Dictionary<ActivityType, int> difficulty = new Dictionary<ActivityType, int>();
        private int bossChoice;

        protected override void Bind() {
            tiles = new TemplateList(UiBind.Find(T, "List"), "Tile");
            ActivityClient.Changed += OnActivitiesChanged;
        }

        internal override void Destroy() => ActivityClient.Changed -= OnActivitiesChanged;

        private void OnActivitiesChanged() {
            if (Root != null && Root.activeSelf) { Refresh(); }
        }

        // Time left and the weekly countdown.
        internal override void Tick() => Refresh();

        internal override void Refresh() {
            if (ClanClient.InClan == false) { return; }
            long resetIn = ActivityClient.PeriodEndUtc - ClanClient.ServerNow;
            UiBind.SetText(T, "Summary", ActivityClient.PeriodEndUtc > 0 ? UiBind.Loc("$clans_activities_reset", UiFormat.Span(resetIn)) : "");

            List<ActivityType> shown = Types.Where(Enabled).ToList();
            UiBind.SetActive(T, "Empty", shown.Count == 0 || ClansConfig.EnableActivities.Value == false);
            if (ClansConfig.EnableActivities.Value == false) { shown.Clear(); }
            tiles.Sync(shown.Count, (tile, i) => Fill(tile.transform, shown[i]));
        }

        private static bool Enabled(ActivityType type) {
            ActivitiesSettings settings = ClansSettings.Activities;
            switch (type) {
                case ActivityType.TrophyHunt: return settings.TrophyHunt.Enabled;
                case ActivityType.Race: return settings.Race.Enabled;
                default: return settings.WorldBoss.Enabled;
            }
        }

        private void Fill(Transform tile, ActivityType type) {
            Image icon = UiBind.Get<Image>(tile, "Icon");
            if (icon != null) {
                Sprite sprite = ClansAssets.BundleSprite(IconName(type));
                if (sprite != null) { icon.sprite = sprite; }
            }
            UiBind.SetText(tile, "Texts/Title", UiBind.Loc(Server.ActivityService.Name(type)));
            UiBind.SetText(tile, "Texts/Body", UiBind.Loc(Server.ActivityService.Name(type) + "_desc"));

            ActivityInfo mine = ActivityClient.MineOf(type);
            List<WorldBossDef> bosses = type == ActivityType.WorldBoss ? AvailableBosses() : null;
            string requirement = Requirement(type, bosses);
            UiBind.SetText(tile, "Texts/Requirement", requirement);
            UiBind.SetActive(tile, "Texts/Requirement", string.IsNullOrEmpty(requirement) == false);

            string progress = mine != null ? Progress(mine) : "";
            UiBind.SetText(tile, "Texts/Progress", progress);
            UiBind.SetActive(tile, "Texts/Progress", progress.Length > 0);

            bool locked = mine == null && ActivityClient.LockedOut(type);
            bool permitted = ClanClient.HasPermission(ClanPermission.StartActivities);
            bool startable = mine == null && locked == false && string.IsNullOrEmpty(requirement);
            string lockout = "";
            if (locked) {
                lockout = UiBind.Loc("$clans_activity_locked", UiFormat.Span(ActivityClient.PeriodEndUtc - ClanClient.ServerNow));
            } else if (startable && permitted == false) {
                lockout = UiBind.Loc("$clans_activity_rank_needed");
            } else if (startable) {
                lockout = UiBind.Loc("$clans_activity_available");
            }
            UiBind.SetText(tile, "Texts/Lockout", lockout);
            UiBind.SetActive(tile, "Texts/Lockout", lockout.Length > 0);

            bool canStart = startable && permitted;
            int chosen = Difficulty(type);
            UiBind.SetActive(tile, "Controls/Difficulty", canStart);
            UiBind.SetActive(tile, "Controls/Boss", canStart && type == ActivityType.WorldBoss);
            UiBind.SetActive(tile, "Controls/Start", canStart);
            UiBind.SetActive(tile, "Controls/Cancel", mine != null && permitted);

            Button difficultyButton = UiBind.Get<Button>(tile, "Controls/Difficulty");
            if (difficultyButton != null) { UiBind.SetButtonText(difficultyButton, UiBind.Loc("$clans_activity_difficulty", UiBind.Loc(Server.ActivityService.Difficulty(chosen)))); }
            UiBind.OnClick(tile, "Controls/Difficulty", () => {
                difficulty[type] = (Difficulty(type) + 1) % 3;
                Refresh();
            });

            WorldBossDef boss = null;
            if (bosses != null && bosses.Count > 0) {
                bossChoice = Mathf.Clamp(bossChoice, 0, bosses.Count - 1);
                boss = bosses[bossChoice];
                Button bossButton = UiBind.Get<Button>(tile, "Controls/Boss");
                if (bossButton != null) { UiBind.SetButtonText(bossButton, UiBind.Loc("$clans_activity_boss", CreatureName(boss.Prefab, boss.Id))); }
                int count = bosses.Count;
                UiBind.OnClick(tile, "Controls/Boss", () => {
                    bossChoice = (bossChoice + 1) % count;
                    Refresh();
                });
            }

            string bossId = boss?.Id ?? "";
            string bossName = boss != null ? CreatureName(boss.Prefab, boss.Id) : "";
            UiBind.OnClick(tile, "Controls/Start", () => Start(type, chosen, bossId, bossName));
            if (mine != null) {
                long id = mine.Id;
                bool refund = mine.State == ActivityState.Placed;
                UiBind.OnClick(tile, "Controls/Cancel", () => ConfirmDialog.Ask(ClansWindow.Root, "$clans_activity_cancel",
                    UiBind.Loc(refund ? "$clans_activity_cancel_body_refund" : "$clans_activity_cancel_body", UiBind.Loc(Server.ActivityService.Name(type))),
                    () => ActivityActions.Cancel(id, ClansWindow.ReportFailure)));
            }
        }

        private int Difficulty(ActivityType type) => difficulty.TryGetValue(type, out int value) ? value : 0;

        private static void Start(ActivityType type, int chosen, string bossId, string bossName) {
            string name = UiBind.Loc(Server.ActivityService.Name(type));
            string level = UiBind.Loc(Server.ActivityService.Difficulty(chosen));
            string body = type == ActivityType.WorldBoss
                ? UiBind.Loc("$clans_activity_start_boss_body", name, level, bossName)
                : UiBind.Loc("$clans_activity_start_body", name, level);
            ConfirmDialog.Ask(ClansWindow.Root, "$clans_activity_start", body, () => ActivityActions.Start(type, chosen, bossId, result => {
                if (result.Ok) {
                    ClansWindow.Status(UiBind.Loc(type == ActivityType.TrophyHunt ? "$clans_activity_started_hunt" : "$clans_activity_started_totem", name));
                } else {
                    ClansWindow.ReportFailure(result);
                }
            }));
        }

        // Why this activity cannot be started on this server, or "" when nothing stands in the way.
        private static string Requirement(ActivityType type, List<WorldBossDef> bosses) {
            if (type != ActivityType.WorldBoss) { return ""; }
            if ((ClanClient.ServerCapabilities & ModPresence.WorldBossNeeds) != ModPresence.WorldBossNeeds) {
                return UiBind.Loc("$clans_wb_requires");
            }
            return bosses == null || bosses.Count == 0 ? UiBind.Loc("$clans_wb_no_bosses") : "";
        }

        // Bosses that are switched on and that this world has beaten.
        private static List<WorldBossDef> AvailableBosses() {
            return ClansSettings.Activities.WorldBoss.Bosses
                .Where(b => b.Enabled && (string.IsNullOrEmpty(b.RequiredKey) || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(b.RequiredKey))))
                .ToList();
        }

        private static string Progress(ActivityInfo info) {
            long left = info.DeadlineUtc - ClanClient.ServerNow;
            string time = info.DeadlineUtc > 0 ? " " + UiBind.Loc("$clans_activity_time_left", UiFormat.Span(left)) : "";
            if (info.State == ActivityState.Placed) {
                return UiBind.Loc("$clans_activity_placed") + time;
            }
            switch (info.Type) {
                case ActivityType.TrophyHunt:
                    return string.Join(", ", info.Targets.Select(t => $"{CreatureName(t.Prefab, t.Prefab)} {Mathf.Min(t.Count, t.Required)}/{t.Required}")) + "." + time;
                case ActivityType.Race:
                    return UiBind.Loc("$clans_race_progress", info.Delivered, info.ChestsTotal) + time;
                default:
                    if (info.State == ActivityState.BossPhase) { return UiBind.Loc("$clans_wb_boss_phase") + time; }
                    return UiBind.Loc("$clans_wb_progress", Mathf.Max(1, info.Phase), ClansSettings.Activities.WorldBoss.Waves) + time;
            }
        }

        // A creature's display name from its prefab.
        internal static string CreatureName(string prefab, string fallback) {
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            Character character = go != null ? go.GetComponent<Character>() : null;
            return character != null ? Localization.instance.Localize(character.m_name) : fallback;
        }

        internal static string IconName(ActivityType type) {
            switch (type) {
                case ActivityType.TrophyHunt: return "tile_hunt";
                case ActivityType.Race: return "tile_race";
                default: return "tile_worldboss";
            }
        }
    }
}
