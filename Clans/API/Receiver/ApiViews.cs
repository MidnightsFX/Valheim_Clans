using System.Collections.Generic;

namespace Clans {
    // What the receivers hand the shared answers in ApiReceiver: plain data, the same whichever side it came from.

    internal sealed class ClanView {
        internal long Id;
        internal string Name = "";
        internal string Description = "";
        internal int Level;
        internal int MemberCount;
        internal bool Recruiting;
        internal string LeaderAccount = "";
        internal string LeaderName = "";
        // Members, ranks, experience, treasury and tabs are known here: always on the server, and for the player's
        // own clan on a client. Otherwise the numbers below are -1.
        internal bool Detail;
        internal long Xp = -1;
        internal long Treasury = -1;
        internal int SharedTabs = -1;
    }

    internal sealed class MemberView {
        internal string Account = "";
        internal string Name = "";
        internal int RankId;
        internal int RankIndex = -1;
        // As saved: a $token for untouched defaults.
        internal string RankName = "";
        internal bool Online;
        // Unix seconds, 0 when not known here.
        internal long JoinedUtc;
        internal long LastSeenUtc;
    }

    // A member's place in their clan. A struct: rank checks run often.
    internal struct RankInfo {
        internal long ClanId;
        internal int Id;
        // 0 is the leader, higher is lower.
        internal int Index;
        internal string StoredName;
    }

    // A player's party and raid. A struct: party checks run often. RaidId and Squad are 0 outside a raid.
    internal struct PartyInfo {
        internal long PartyId;
        internal long RaidId;
        internal int Squad;
    }

    internal sealed class PartyMemberView {
        internal string Account = "";
        internal string Name = "";
        internal bool Online;
        internal long ClanId;
    }

    internal sealed class PartyView {
        internal long Id;
        internal string Leader = "";
        internal long RaidId;
        // 1 and up in a raid, else 0.
        internal int Squad;
        internal List<PartyMemberView> Members = new List<PartyMemberView>();
    }

    internal sealed class RaidView {
        internal long Id;
        internal string Leader = "";
        // Squad order: index 0 is squad 1.
        internal List<PartyView> Squads = new List<PartyView>();
    }
}
