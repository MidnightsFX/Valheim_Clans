using Clans.Server.Model;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Net {
    // Epic Loot bounties a clan member shares with their clan. The owner's client reports its bounties as
    // Epic Loot describes them; clanmates are told only where to look. Keep Write and Read next to each other
    // and in the same order.

    internal static class BountyLimits {
        // Epic Loot caps bounties in progress at a handful by default; this only stops a flood.
        internal const int MaxPerAccount = 20;
        internal const int MaxAdds = 10;
        internal const int MaxId = 128;
        internal const int MaxMonsterId = 64;
        internal const int MaxName = 64;
        internal const int MaxBiome = 32;
        internal const float MinRadius = 5f;
        internal const float MaxRadius = 500f;
        // Further than any world reaches, modded ones included.
        internal const float MaxCoordinate = 100000f;
    }

    internal sealed class ReportedBountyAdd {
        internal string MonsterId = "";
        // How many are still alive.
        internal int Count;
    }

    // One bounty as the owner's client reports it (Op.BountyReport).
    internal sealed class ReportedBounty {
        internal string Id = "";
        internal string MonsterId = "";
        internal string Name = "";
        internal string Biome = "";
        // The centre of the circle the owner's map shows; the creatures spawn somewhere inside it.
        internal Vector3 Position;
        internal float Radius;
        internal bool Slain;
        // Every creature is dead; the reward waits to be claimed.
        internal bool Complete;
        internal readonly List<ReportedBountyAdd> Adds = new List<ReportedBountyAdd>();

        internal void Write(ZPackage pkg) {
            pkg.Write(Id ?? "");
            pkg.Write(MonsterId ?? "");
            pkg.Write(Name ?? "");
            pkg.Write(Biome ?? "");
            pkg.Write(Position);
            pkg.Write(Radius);
            pkg.Write(Slain);
            pkg.Write(Complete);
            pkg.Write(Adds.Count);
            foreach (ReportedBountyAdd add in Adds) {
                pkg.Write(add.MonsterId ?? "");
                pkg.Write(add.Count);
            }
        }

        internal static ReportedBounty Read(ZPackage pkg) {
            ReportedBounty bounty = new ReportedBounty {
                Id = pkg.ReadString(),
                MonsterId = pkg.ReadString(),
                Name = pkg.ReadString(),
                Biome = pkg.ReadString(),
                Position = pkg.ReadVector3(),
                Radius = pkg.ReadSingle(),
                Slain = pkg.ReadBool(),
                Complete = pkg.ReadBool(),
            };
            int adds = pkg.ReadInt();
            for (int i = 0; i < adds; i++) {
                ReportedBountyAdd add = new ReportedBountyAdd { MonsterId = pkg.ReadString(), Count = pkg.ReadInt() };
                if (i < BountyLimits.MaxAdds) { bounty.Adds.Add(add); }
            }
            return bounty;
        }
    }

    // One clanmate's open bounty, as their clan sees it (Op.ClanBounties).
    internal sealed class SharedBounty {
        internal string OwnerAccount = "";
        internal string OwnerName = "";
        internal string Id = "";
        internal string MonsterId = "";
        internal string Name = "";
        internal string Biome = "";
        internal Vector3 Position;
        internal float Radius;

        internal static void Write(ZPackage pkg, string account, string ownerName, SharedBountyRecord record) {
            pkg.Write(account ?? "");
            pkg.Write(ownerName ?? "");
            pkg.Write(record.Id ?? "");
            pkg.Write(record.MonsterId ?? "");
            pkg.Write(record.Name ?? "");
            pkg.Write(record.Biome ?? "");
            pkg.Write(new Vector3(record.X, record.Y, record.Z));
            pkg.Write(record.Radius);
        }

        internal static SharedBounty Read(ZPackage pkg) {
            return new SharedBounty {
                OwnerAccount = pkg.ReadString(),
                OwnerName = pkg.ReadString(),
                Id = pkg.ReadString(),
                MonsterId = pkg.ReadString(),
                Name = pkg.ReadString(),
                Biome = pkg.ReadString(),
                Position = pkg.ReadVector3(),
                Radius = pkg.ReadSingle(),
            };
        }
    }
}
