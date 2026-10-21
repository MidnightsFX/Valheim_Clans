using Clans.Compat;
using Clans.Net;
using Splatform;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Client {
    // Map pins for the clanmates' shared Epic Loot bounties: the circle the owner's map shows, and Epic Loot's
    // bounty icon labelled with the target and the owner. The icon follows Epic Loot's bounty filter on the
    // large map, and the circle goes with it. Pins are never saved; they follow the server's list.
    internal static class BountyPins {
        private const float RefreshSeconds = 2f;

        private sealed class Pin {
            internal Minimap.PinData Area;
            internal Minimap.PinData Icon;
            internal Vector3 Position;
            internal float Radius;
            internal Minimap.PinType Type;
            internal string Label;
        }

        private static readonly Dictionary<string, Pin> pins = new Dictionary<string, Pin>();
        private static float timer;
        private static int drawnVersion = -1;

        internal static void Tick(float dt) {
            timer -= dt;
            if (BountyShare.Version != drawnVersion) { timer = 0f; }
            if (timer > 0f) { return; }
            timer = RefreshSeconds;
            drawnVersion = BountyShare.Version;
            Minimap map = Minimap.instance;
            if (map == null) {
                pins.Clear();
                return;
            }

            Minimap.PinType type = IconType(map);
            Dictionary<string, SharedBounty> wanted = new Dictionary<string, SharedBounty>();
            if (BountyShare.Available && Shown(map, type)) {
                foreach (SharedBounty bounty in BountyShare.Shared) { wanted[bounty.Id] = bounty; }
            }

            List<string> gone = new List<string>();
            foreach (KeyValuePair<string, Pin> pin in pins) {
                if (wanted.TryGetValue(pin.Key, out SharedBounty want) == false || Vector3.Distance(want.Position, pin.Value.Position) > 1f
                    || Mathf.Abs(want.Radius - pin.Value.Radius) > 0.5f || pin.Value.Type != type || pin.Value.Label != Label(want)) { gone.Add(pin.Key); }
            }
            foreach (string key in gone) {
                Remove(map, pins[key]);
                pins.Remove(key);
            }
            foreach (KeyValuePair<string, SharedBounty> want in wanted) {
                if (pins.ContainsKey(want.Key)) { continue; }
                SharedBounty bounty = want.Value;
                string label = Label(bounty);
                Minimap.PinData area = map.AddPin(bounty.Position, Minimap.PinType.EventArea, "", false, false, 0L, new PlatformUserID());
                area.m_worldSize = bounty.Radius * 2f;
                Minimap.PinData icon = map.AddPin(bounty.Position, type, label, false, false, 0L, new PlatformUserID());
                pins[want.Key] = new Pin { Area = area, Icon = icon, Position = bounty.Position, Radius = bounty.Radius, Type = type, Label = label };
            }
        }

        // Epic Loot's bounty pin type once its minimap has registered it; a plain marker until then.
        private static Minimap.PinType IconType(Minimap map) {
            int type = EpicLootApi.BountyPinType();
            bool[] visible = map.m_visibleIconTypes;
            return type >= 0 && visible != null && type < visible.Length ? (Minimap.PinType)type : Minimap.PinType.Icon3;
        }

        // Whether the large map's filter shows this pin type.
        private static bool Shown(Minimap map, Minimap.PinType type) {
            bool[] visible = map.m_visibleIconTypes;
            return visible == null || (int)type >= visible.Length || visible[(int)type];
        }

        // "Bounty: Troll (Bob)".
        private static string Label(SharedBounty bounty) {
            return UI.UiBind.Loc("$clans_pin_bounty", Localization.instance.Localize(bounty.Name), bounty.OwnerName);
        }

        private static void Remove(Minimap map, Pin pin) {
            if (pin.Area != null) { map.RemovePin(pin.Area); }
            if (pin.Icon != null) { map.RemovePin(pin.Icon); }
        }

        internal static void Reset() {
            if (Minimap.instance != null) {
                foreach (Pin pin in pins.Values) { Remove(Minimap.instance, pin); }
            }
            pins.Clear();
            timer = 0f;
            drawnVersion = -1;
        }
    }
}
