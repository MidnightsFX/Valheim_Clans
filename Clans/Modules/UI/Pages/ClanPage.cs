using Clans.Client;
using System;
using UnityEngine;

namespace Clans.UI.Pages {
    // One page of the Clans window, bound to its Clans_Page_* prefab instance.
    internal abstract class ClanPage {
        internal abstract string Id { get; }
        internal abstract string Prefab { get; }
        // The tab that is highlighted while this page shows.
        internal virtual string TabId => Id;
        internal GameObject Root { get; private set; }
        protected Transform T => Root.transform;

        internal void Attach(GameObject root) {
            Root = root;
            Bind();
        }

        // Wires buttons and finds widgets. Runs once.
        protected abstract void Bind();

        // Fills the page from ClanClient. Runs on show and whenever the data changes while it shows.
        internal abstract void Refresh();

        internal virtual void OnShow() => Refresh();
        internal virtual void OnHide() { }

        // Once a second while the page shows, for things that change without an event (distances).
        internal virtual void Tick() { }

        // The window is going away; drop any event subscriptions.
        internal virtual void Destroy() { }

        // Whether the page makes sense right now (clan pages need a clan, founding needs none).
        internal virtual bool AvailableNow() => true;
    }

    internal static class UiFormat {
        // "now", "5m", "3h", "2d" since a server time.
        internal static string Ago(long utc) {
            long seconds = Math.Max(0, ClanClient.ServerNow - utc);
            if (seconds < 60) { return UiBind.Loc("$clans_time_now"); }
            if (seconds < 3600) { return UiBind.Loc("$clans_time_minutes", seconds / 60); }
            if (seconds < 86400) { return UiBind.Loc("$clans_time_hours", seconds / 3600); }
            return UiBind.Loc("$clans_time_days", seconds / 86400);
        }

        // "4d 3h", "3h 20m", "12m" for a length of time in seconds.
        internal static string Span(long seconds) {
            TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (span.TotalDays >= 1) { return $"{(int)span.TotalDays}d {span.Hours}h"; }
            if (span.TotalHours >= 1) { return $"{span.Hours}h {span.Minutes}m"; }
            return $"{Math.Max(1, span.Minutes)}m";
        }

        internal static string RankName(Core.RankDef rank) {
            return rank == null ? "" : UiBind.Loc(rank.Name);
        }

        internal static bool TryParseCoins(string text, out int coins) {
            return int.TryParse((text ?? "").Trim(), out coins) && coins > 0;
        }
    }
}
