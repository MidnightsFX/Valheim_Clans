using Clans.Net;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Clans.Compat {
    // Reads the JSON that Epic Loot's bounty API returns: GetActiveBounties on the owner's client, and the
    // bounty-target-slain event on the server. Anything missing reads as empty or zero.
    internal static class EpicLootBounties {
        // The bounties being hunted, and those finished but not yet claimed.
        internal static List<ReportedBounty> ParseActive(string json) {
            List<ReportedBounty> bounties = new List<ReportedBounty>();
            if (!(Parse(json) is IList<object> list)) { return bounties; }
            foreach (object item in list) {
                if (!(item is IDictionary<string, object> entry)) { continue; }
                string state = Text(entry, "State");
                if (state != "InProgress" && state != "Complete") { continue; }
                ReportedBounty bounty = new ReportedBounty {
                    Id = Text(entry, "Id"),
                    MonsterId = Text(entry, "MonsterId"),
                    Name = Text(entry, "Name"),
                    Biome = Text(entry, "Biome"),
                    Position = new Vector3(Number(entry, "X"), Number(entry, "Y"), Number(entry, "Z")),
                    Radius = Number(entry, "Radius"),
                    Slain = Flag(entry, "Slain"),
                    Complete = state == "Complete",
                };
                if (entry.TryGetValue("Adds", out object adds) && adds is IList<object> addList) {
                    foreach (object value in addList) {
                        if (value is IDictionary<string, object> add) {
                            bounty.Adds.Add(new ReportedBountyAdd { MonsterId = Text(add, "MonsterId"), Count = (int)Number(add, "Count") });
                        }
                    }
                }
                if (string.IsNullOrEmpty(bounty.Id) == false) { bounties.Add(bounty); }
            }
            return bounties;
        }

        // { BountyId, PlayerId, MonsterId, IsAdd }, from the server-side kill event.
        internal static bool TryParseKill(string json, out string bountyId, out string monsterId, out bool isAdd) {
            bountyId = "";
            monsterId = "";
            isAdd = false;
            if (!(Parse(json) is IDictionary<string, object> kill)) { return false; }
            bountyId = Text(kill, "BountyId");
            monsterId = Text(kill, "MonsterId");
            isAdd = Flag(kill, "IsAdd");
            return string.IsNullOrEmpty(bountyId) == false;
        }

        private static object Parse(string json) {
            if (string.IsNullOrEmpty(json)) { return null; }
            try {
                return SimpleJson.SimpleJson.DeserializeObject(json);
            } catch (Exception e) {
                Logger.LogWarning($"Could not read Epic Loot's bounty data: {e.Message}");
                return null;
            }
        }

        private static string Text(IDictionary<string, object> values, string key) {
            return values.TryGetValue(key, out object value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }

        private static float Number(IDictionary<string, object> values, string key) {
            if (values.TryGetValue(key, out object value) == false) { return 0f; }
            switch (value) {
                case double d: return (float)d;
                case long l: return l;
                case int i: return i;
                case float f: return f;
                case decimal m: return (float)m;
                default: return 0f;
            }
        }

        private static bool Flag(IDictionary<string, object> values, string key) {
            return values.TryGetValue(key, out object value) && value is bool on && on;
        }
    }
}
