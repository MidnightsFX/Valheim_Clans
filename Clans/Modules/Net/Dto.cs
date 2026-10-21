using Clans.Core;
using Clans.Server.Model;
using System.Collections.Generic;

namespace Clans.Net {
    // What clients are told. Each type writes itself from the server's records and reads itself back on the
    // client; keep Write and Read next to each other and in the same order.

    // One clan as everyone sees it (Join list, War page, nameplates).
    internal sealed class ClanSummary {
        internal long Id;
        internal string Name = "";
        internal EmblemData Emblem = new EmblemData();
        internal int Level;
        internal int MemberCount;
        internal bool Recruiting;
        internal string LeaderName = "";
        internal string Description = "";

        internal static void Write(ZPackage pkg, ClanRecord clan, int level, string leaderName) {
            pkg.Write(clan.Id);
            pkg.Write(clan.Name ?? "");
            (clan.Emblem ?? new EmblemData()).Write(pkg);
            pkg.Write(level);
            pkg.Write(clan.Members.Count);
            pkg.Write(clan.Recruiting);
            pkg.Write(leaderName ?? "");
            pkg.Write(clan.Description ?? "");
        }

        internal static ClanSummary Read(ZPackage pkg) {
            return new ClanSummary {
                Id = pkg.ReadLong(),
                Name = pkg.ReadString(),
                Emblem = EmblemData.Read(pkg),
                Level = pkg.ReadInt(),
                MemberCount = pkg.ReadInt(),
                Recruiting = pkg.ReadBool(),
                LeaderName = pkg.ReadString(),
                Description = pkg.ReadString(),
            };
        }
    }

    internal sealed class WarSummary {
        internal long Id;
        internal long ClanA;
        internal long ClanB;
        internal WarState State;
        internal long ProposedUtc;
        internal long StartedUtc;
        internal long EndsUtc;
        internal int KillTarget;
        internal int KillsA;
        internal int KillsB;
        internal long PeaceOfferedBy;

        internal bool Involves(long clanId) => ClanA == clanId || ClanB == clanId;
        internal long Other(long clanId) => ClanA == clanId ? ClanB : ClanA;
        internal int KillsOf(long clanId) => ClanA == clanId ? KillsA : KillsB;

        internal static void Write(ZPackage pkg, WarRecord war) {
            pkg.Write(war.Id);
            pkg.Write(war.ClanA);
            pkg.Write(war.ClanB);
            pkg.Write((byte)war.State);
            pkg.Write(war.ProposedUtc);
            pkg.Write(war.StartedUtc);
            pkg.Write(war.EndsUtc);
            pkg.Write(war.KillTarget);
            pkg.Write(war.KillsA);
            pkg.Write(war.KillsB);
            pkg.Write(war.PeaceOfferedBy);
        }

        internal static WarSummary Read(ZPackage pkg) {
            return new WarSummary {
                Id = pkg.ReadLong(),
                ClanA = pkg.ReadLong(),
                ClanB = pkg.ReadLong(),
                State = (WarState)pkg.ReadByte(),
                ProposedUtc = pkg.ReadLong(),
                StartedUtc = pkg.ReadLong(),
                EndsUtc = pkg.ReadLong(),
                KillTarget = pkg.ReadInt(),
                KillsA = pkg.ReadInt(),
                KillsB = pkg.ReadInt(),
                PeaceOfferedBy = pkg.ReadLong(),
            };
        }
    }

    // One online player as everyone sees them.
    internal sealed class RosterEntry {
        internal string Account = "";
        internal string Name = "";
        internal long ClanId;
        internal long PartyId;
        internal long RaidId;
        internal int Squad;
        internal bool PartyLeader;

        internal void Write(ZPackage pkg) {
            pkg.Write(Account ?? "");
            pkg.Write(Name ?? "");
            pkg.Write(ClanId);
            pkg.Write(PartyId);
            pkg.Write(RaidId);
            pkg.Write(Squad);
            pkg.Write(PartyLeader);
        }

        internal static RosterEntry Read(ZPackage pkg) {
            return new RosterEntry {
                Account = pkg.ReadString(),
                Name = pkg.ReadString(),
                ClanId = pkg.ReadLong(),
                PartyId = pkg.ReadLong(),
                RaidId = pkg.ReadLong(),
                Squad = pkg.ReadInt(),
                PartyLeader = pkg.ReadBool(),
            };
        }
    }

    // The receiving player's own standing.
    internal sealed class MyAccountInfo {
        internal string Account = "";
        internal long ClanId;
        internal int RankId;
        internal ClanPermission Permissions;
        internal List<long> Applications = new List<long>();

        internal static void Write(ZPackage pkg, string account, AccountRecord record, int rankId, ClanPermission permissions) {
            pkg.Write(account ?? "");
            pkg.Write(record?.ClanId ?? 0L);
            pkg.Write(rankId);
            pkg.Write((int)permissions);
            List<long> applications = record?.PendingApplications ?? new List<long>();
            pkg.Write(applications.Count);
            foreach (long id in applications) { pkg.Write(id); }
        }

        internal static MyAccountInfo Read(ZPackage pkg) {
            MyAccountInfo info = new MyAccountInfo {
                Account = pkg.ReadString(),
                ClanId = pkg.ReadLong(),
                RankId = pkg.ReadInt(),
                Permissions = (ClanPermission)(uint)pkg.ReadInt(),
            };
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++) { info.Applications.Add(pkg.ReadLong()); }
            return info;
        }
    }

    internal sealed class MemberInfo {
        internal string Account = "";
        internal string Name = "";
        internal int RankId;
        internal long JoinedUtc;
        internal long LastSeenUtc;
        internal bool Online;
    }

    internal sealed class ApplicationInfo {
        internal string Account = "";
        internal string Name = "";
        internal string Message = "";
        internal long CreatedUtc;
    }

    internal sealed class LogInfo {
        internal long Utc;
        internal string Token = "";
        internal string[] Args = new string[0];
    }

    // Everything a member sees about their own clan.
    internal sealed class ClanDetailInfo {
        internal long Id;
        internal string Name = "";
        internal string Description = "";
        internal string Status = "";
        internal EmblemData Emblem = new EmblemData();
        internal bool Recruiting;
        internal long Xp;
        internal int Level;
        internal long Treasury;
        internal List<RankDef> Ranks = new List<RankDef>();
        internal List<MemberInfo> Members = new List<MemberInfo>();
        internal List<ApplicationInfo> Applications = new List<ApplicationInfo>();
        internal List<LogInfo> Log = new List<LogInfo>();
        internal int SharedTabs;
        internal bool PersonalUnlocked;
        // Activity type -> start of the week it was last started in.
        internal Dictionary<ActivityType, long> Lockouts = new Dictionary<ActivityType, long>();
        // Items handed in toward the next level: prefab name -> amount.
        internal Dictionary<string, int> Contributions = new Dictionary<string, int>();

        internal static void Write(ZPackage pkg, ClanRecord clan, int level, System.Func<string, bool> isOnline, int logEntries) {
            pkg.Write(clan.Id);
            pkg.Write(clan.Name ?? "");
            pkg.Write(clan.Description ?? "");
            pkg.Write(clan.Status ?? "");
            (clan.Emblem ?? new EmblemData()).Write(pkg);
            pkg.Write(clan.Recruiting);
            pkg.Write(clan.Xp);
            pkg.Write(level);
            pkg.Write(clan.Treasury);

            pkg.Write(clan.Ranks.Count);
            foreach (RankDef rank in clan.Ranks) { rank.Write(pkg); }

            pkg.Write(clan.Members.Count);
            foreach (MemberRecord member in clan.Members.Values) {
                pkg.Write(member.Account ?? "");
                pkg.Write(member.LastCharacterName ?? "");
                pkg.Write(member.RankId);
                pkg.Write(member.JoinedUtc);
                pkg.Write(member.LastSeenUtc);
                pkg.Write(isOnline(member.Account));
            }

            pkg.Write(clan.Applications.Count);
            foreach (ApplicationRecord application in clan.Applications) {
                pkg.Write(application.Account ?? "");
                pkg.Write(application.CharacterName ?? "");
                pkg.Write(application.Message ?? "");
                pkg.Write(application.CreatedUtc);
            }

            int start = clan.Log.Count > logEntries ? clan.Log.Count - logEntries : 0;
            pkg.Write(clan.Log.Count - start);
            for (int i = start; i < clan.Log.Count; i++) {
                LogEntry entry = clan.Log[i];
                pkg.Write(entry.Utc);
                pkg.Write(entry.Token ?? "");
                List<string> args = entry.Args ?? new List<string>();
                pkg.Write(args.Count);
                foreach (string arg in args) { pkg.Write(arg ?? ""); }
            }

            pkg.Write(clan.Storage.SharedTabs.Count);
            pkg.Write(clan.Storage.PersonalUnlocked);
            pkg.Write(clan.Lockouts.Count);
            foreach (KeyValuePair<string, long> lockout in clan.Lockouts) {
                pkg.Write(lockout.Key ?? "");
                pkg.Write(lockout.Value);
            }
            pkg.Write(clan.LevelContributions.Count);
            foreach (KeyValuePair<string, int> item in clan.LevelContributions) {
                pkg.Write(item.Key ?? "");
                pkg.Write(item.Value);
            }
        }

        internal static ClanDetailInfo Read(ZPackage pkg) {
            ClanDetailInfo info = new ClanDetailInfo {
                Id = pkg.ReadLong(),
                Name = pkg.ReadString(),
                Description = pkg.ReadString(),
                Status = pkg.ReadString(),
                Emblem = EmblemData.Read(pkg),
                Recruiting = pkg.ReadBool(),
                Xp = pkg.ReadLong(),
                Level = pkg.ReadInt(),
                Treasury = pkg.ReadLong(),
            };

            int ranks = pkg.ReadInt();
            for (int i = 0; i < ranks; i++) { info.Ranks.Add(RankDef.Read(pkg)); }

            int members = pkg.ReadInt();
            for (int i = 0; i < members; i++) {
                info.Members.Add(new MemberInfo {
                    Account = pkg.ReadString(),
                    Name = pkg.ReadString(),
                    RankId = pkg.ReadInt(),
                    JoinedUtc = pkg.ReadLong(),
                    LastSeenUtc = pkg.ReadLong(),
                    Online = pkg.ReadBool(),
                });
            }

            int applications = pkg.ReadInt();
            for (int i = 0; i < applications; i++) {
                info.Applications.Add(new ApplicationInfo {
                    Account = pkg.ReadString(),
                    Name = pkg.ReadString(),
                    Message = pkg.ReadString(),
                    CreatedUtc = pkg.ReadLong(),
                });
            }

            int log = pkg.ReadInt();
            for (int i = 0; i < log; i++) {
                LogInfo entry = new LogInfo { Utc = pkg.ReadLong(), Token = pkg.ReadString() };
                int count = pkg.ReadInt();
                entry.Args = new string[count];
                for (int a = 0; a < count; a++) { entry.Args[a] = pkg.ReadString(); }
                info.Log.Add(entry);
            }

            info.SharedTabs = pkg.ReadInt();
            info.PersonalUnlocked = pkg.ReadBool();
            int lockouts = pkg.ReadInt();
            for (int i = 0; i < lockouts; i++) {
                string type = pkg.ReadString();
                long start = pkg.ReadLong();
                if (System.Enum.TryParse(type, out ActivityType parsed)) { info.Lockouts[parsed] = start; }
            }
            int contributions = pkg.ReadInt();
            for (int i = 0; i < contributions; i++) {
                string item = pkg.ReadString();
                info.Contributions[item] = pkg.ReadInt();
            }
            return info;
        }

        internal RankDef Rank(int rankId) => Ranks.Find(r => r.Id == rankId);
        internal int RankIndex(int rankId) => Ranks.FindIndex(r => r.Id == rankId);
        internal MemberInfo Member(string account) => Members.Find(m => m.Account == account);

        internal int Contributed(string item) => Contributions.TryGetValue(item, out int amount) ? amount : 0;
    }
}
