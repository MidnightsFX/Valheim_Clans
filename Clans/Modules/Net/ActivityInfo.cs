using Clans.Server.Model;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Net {
    // One running (or waiting) clan activity, as every client sees it. Sent to everyone, not just the clan:
    // whoever happens to own a totem's object in the world runs its part of the activity (counting a race's
    // delivered chests, running the waves), and a hunt counts kills made by anyone's creatures.
    internal sealed class ActivityInfo {
        internal long Id;
        internal long ClanId;
        internal ActivityType Type;
        internal int Difficulty;
        internal ActivityState State;
        internal string BossId = "";
        internal string StartedBy = "";
        internal long BeganUtc;
        internal long DeadlineUtc;
        internal ZDOID Totem = ZDOID.None;
        internal Vector3 TotemPos;
        internal ZDOID Dest = ZDOID.None;
        internal Vector3 DestPos;
        internal int Participants;
        internal int ChestsTotal;
        internal int Delivered;
        internal int Phase;
        internal bool ChestsSpawned;
        internal string FortressHandle = "";
        internal readonly List<HuntTarget> Targets = new List<HuntTarget>();

        internal static void Write(ZPackage pkg, long clanId, ActivityInstance a, string startedByName) {
            pkg.Write(a.Id);
            pkg.Write(clanId);
            pkg.Write((byte)a.Type);
            pkg.Write(a.Difficulty);
            pkg.Write((byte)a.State);
            pkg.Write(a.BossId ?? "");
            pkg.Write(startedByName ?? "");
            pkg.Write(a.BeganUtc);
            pkg.Write(a.DeadlineUtc);
            pkg.Write(new ZDOID(a.TotemUser, a.TotemZdoId));
            pkg.Write(new Vector3(a.TotemX, a.TotemY, a.TotemZ));
            pkg.Write(new ZDOID(a.DestUser, a.DestZdoId));
            pkg.Write(new Vector3(a.DestX, a.DestY, a.DestZ));
            pkg.Write(a.Participants.Count);
            pkg.Write(a.ChestsTotal);
            pkg.Write(a.Delivered.Count);
            pkg.Write(a.Phase);
            pkg.Write(a.ChestsSpawned);
            pkg.Write(a.FortressHandle ?? "");
            pkg.Write(a.Targets.Count);
            foreach (HuntTarget target in a.Targets) {
                pkg.Write(target.Prefab ?? "");
                pkg.Write(target.Required);
                pkg.Write(target.Count);
                pkg.Write(target.IsBoss);
            }
        }

        internal static ActivityInfo Read(ZPackage pkg) {
            ActivityInfo info = new ActivityInfo {
                Id = pkg.ReadLong(),
                ClanId = pkg.ReadLong(),
                Type = (ActivityType)pkg.ReadByte(),
                Difficulty = pkg.ReadInt(),
                State = (ActivityState)pkg.ReadByte(),
                BossId = pkg.ReadString(),
                StartedBy = pkg.ReadString(),
                BeganUtc = pkg.ReadLong(),
                DeadlineUtc = pkg.ReadLong(),
                Totem = pkg.ReadZDOID(),
                TotemPos = pkg.ReadVector3(),
                Dest = pkg.ReadZDOID(),
                DestPos = pkg.ReadVector3(),
                Participants = pkg.ReadInt(),
                ChestsTotal = pkg.ReadInt(),
                Delivered = pkg.ReadInt(),
                Phase = pkg.ReadInt(),
                ChestsSpawned = pkg.ReadBool(),
                FortressHandle = pkg.ReadString(),
            };
            int targets = pkg.ReadInt();
            for (int i = 0; i < targets; i++) {
                info.Targets.Add(new HuntTarget {
                    Prefab = pkg.ReadString(),
                    Required = pkg.ReadInt(),
                    Count = pkg.ReadInt(),
                    IsBoss = pkg.ReadBool(),
                });
            }
            return info;
        }

        internal bool Active => State == ActivityState.Placed || State == ActivityState.Running || State == ActivityState.BossPhase;
    }
}
