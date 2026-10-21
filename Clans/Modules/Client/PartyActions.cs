using Clans.Net;
using System;

namespace Clans.Client {
    // Party and raid requests, for the UI and the console commands.
    internal static class PartyActions {
        internal static void Create(Action<NetResult> done = null) => ClanRequests.Send(Op.CreateParty, null, done);

        internal static void Invite(string account, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.InviteParty, pkg => pkg.Write(account ?? ""), done);

        internal static void Respond(long inviteId, bool accept, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.RespondInvite, pkg => { pkg.Write(inviteId); pkg.Write(accept); }, done);

        internal static void Leave(Action<NetResult> done = null) => ClanRequests.Send(Op.LeaveParty, null, done);

        internal static void Kick(string account, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.KickParty, pkg => pkg.Write(account ?? ""), done);

        internal static void SetLeader(string account, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.SetPartyLeader, pkg => pkg.Write(account ?? ""), done);

        // Invites another party's leader to bring their party into this player's raid.
        internal static void InviteToRaid(string partyLeader, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.InviteSquadToRaid, pkg => pkg.Write(partyLeader ?? ""), done);

        internal static void LeaveRaid(Action<NetResult> done = null) => ClanRequests.Send(Op.LeaveRaid, null, done);

        internal static void MoveToSquad(string account, int squad, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.MoveToSquad, pkg => { pkg.Write(account ?? ""); pkg.Write(squad); }, done);

        internal static void SetRaidLeader(string account, Action<NetResult> done = null) =>
            ClanRequests.Send(Op.SetRaidLeader, pkg => pkg.Write(account ?? ""), done);
    }
}
