using Clans.Net;
using Clans.Server.Model;
using Clans.Settings;
using Splatform;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Client {
    // Map pins for clan activities: World Boss totems, and each Race's start and destination. Everyone sees
    // every clan's, labelled with the clan, unless the server keeps them to the clan's own members. Pins are
    // never saved; they follow the server's activity list and go away when an activity ends.
    internal static class ActivityPins {
        private const float RefreshSeconds = 2f;

        private struct Wanted {
            internal Vector3 Position;
            internal Minimap.PinType Type;
            internal string Label;
        }

        private static readonly Dictionary<string, Minimap.PinData> pins = new Dictionary<string, Minimap.PinData>();
        private static float timer;

        internal static void Tick(float dt) {
            timer -= dt;
            if (timer > 0f) { return; }
            timer = RefreshSeconds;
            if (Minimap.instance == null) {
                pins.Clear();
                return;
            }

            Dictionary<string, Wanted> wanted = new Dictionary<string, Wanted>();
            long mine = ClanClient.Me?.ClanId ?? 0;
            bool everyone = ClansConfig.ActivityPinsForEveryone.Value;
            foreach (ActivityInfo info in ActivityClient.Active) {
                if (everyone == false && (mine == 0 || info.ClanId != mine)) { continue; }
                string clan = ClanClient.Clan(info.ClanId)?.Name ?? "";
                if (info.Type == ActivityType.WorldBoss && info.Totem != ZDOID.None) {
                    wanted["boss:" + info.Id] = new Wanted { Position = info.TotemPos, Type = Minimap.PinType.Boss, Label = Label("$clans_pin_world_boss", clan) };
                } else if (info.Type == ActivityType.Race) {
                    if (info.Totem != ZDOID.None) {
                        wanted["start:" + info.Id] = new Wanted { Position = info.TotemPos, Type = Minimap.PinType.Hildir1, Label = Label("$clans_pin_race_start", clan) };
                    }
                    if (info.Dest != ZDOID.None) {
                        wanted["dest:" + info.Id] = new Wanted { Position = info.DestPos, Type = Minimap.PinType.Hildir3, Label = Label("$clans_pin_race_destination", clan) };
                    }
                }
            }

            List<string> gone = new List<string>();
            foreach (KeyValuePair<string, Minimap.PinData> pin in pins) {
                if (wanted.TryGetValue(pin.Key, out Wanted want) == false || Vector3.Distance(want.Position, pin.Value.m_pos) > 1f
                    || pin.Value.m_name != want.Label) { gone.Add(pin.Key); }
            }
            foreach (string key in gone) {
                Minimap.instance.RemovePin(pins[key]);
                pins.Remove(key);
            }
            foreach (KeyValuePair<string, Wanted> want in wanted) {
                if (pins.ContainsKey(want.Key)) { continue; }
                pins[want.Key] = Minimap.instance.AddPin(want.Value.Position, want.Value.Type, want.Value.Label, false, false, 0L, new PlatformUserID());
            }
        }

        // "World Boss: Clan name".
        private static string Label(string token, string clan) {
            string what = Localization.instance.Localize(token);
            return string.IsNullOrEmpty(clan) ? what : what + ": " + clan;
        }

        internal static void Reset() {
            if (Minimap.instance != null) {
                foreach (Minimap.PinData pin in pins.Values) { Minimap.instance.RemovePin(pin); }
            }
            pins.Clear();
            timer = 0f;
        }
    }
}
