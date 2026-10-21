using Clans.Net;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Client {
    // This player's party and raid, invitations waiting for an answer, and where companions are.
    internal static class PartyClient {
        // Positions older than this are dropped: the companion left, or the server stopped sending.
        private const float PositionLifetime = 12f;

        internal sealed class Member {
            internal string Account = "";
            internal string Name = "";
            internal bool Online;
            internal long ClanId;
        }

        internal sealed class Squad {
            internal long PartyId;
            internal string Leader = "";
            internal readonly List<Member> Members = new List<Member>();
        }

        internal sealed class InviteInfo {
            internal long Id;
            internal bool Raid;
            internal string FromAccount = "";
            internal string FromName = "";
            internal int MemberCount;
            internal float ExpiresAt;
            internal float ReceivedAt;
        }

        internal struct Companion {
            internal string Account;
            internal Vector3 Position;
            internal float Health;
            internal float Received;
        }

        internal static long PartyId { get; private set; }
        internal static long RaidId { get; private set; }
        internal static string RaidLeader { get; private set; } = "";
        internal static readonly List<Squad> Squads = new List<Squad>();
        internal static readonly List<InviteInfo> Invites = new List<InviteInfo>();
        // Sorted by account, so map pins keep a stable order.
        internal static readonly List<Companion> Companions = new List<Companion>();

        internal static bool InParty => PartyId != 0;
        internal static bool InRaid => RaidId != 0;

        internal static event Action Changed;
        internal static event Action InvitesChanged;
        internal static event Action PositionsChanged;

        internal static void Init() {
            ClanNet.RegisterClient(Op.PartyState, OnState);
            ClanNet.RegisterClient(Op.Invite, OnInvite);
            ClanNet.RegisterClient(Op.InviteClosed, OnInviteClosed);
            ClanNet.RegisterClient(Op.PartyPositions, OnPositions);
        }

        internal static void Reset() {
            PartyId = 0;
            RaidId = 0;
            RaidLeader = "";
            Squads.Clear();
            Invites.Clear();
            Companions.Clear();
        }

        // ---------------------------------------------------------------- lookups

        internal static Squad MySquad => Squads.Find(s => s.PartyId == PartyId);

        internal static bool IsLeader => MySquad != null && MySquad.Leader == ClanClient.MyAccount;
        internal static bool IsRaidLeader => InRaid && RaidLeader == ClanClient.MyAccount;

        internal static int SquadNumber(long partyId) => Squads.FindIndex(s => s.PartyId == partyId) + 1;

        // Whether an account is in this player's squad (not merely their raid).
        internal static bool InMySquad(string account) {
            Squad squad = MySquad;
            return squad != null && squad.Members.Exists(m => m.Account == account);
        }

        internal static bool TryCompanion(string account, out Companion companion) {
            foreach (Companion c in Companions) {
                if (c.Account == account) {
                    companion = c;
                    return true;
                }
            }
            companion = default;
            return false;
        }

        // ---------------------------------------------------------------- handlers

        private static void OnState(uint reqId, ZPackage body) {
            PartyId = body.ReadLong();
            RaidId = body.ReadLong();
            RaidLeader = body.ReadString();
            Squads.Clear();
            int squads = body.ReadInt();
            for (int s = 0; s < squads; s++) {
                Squad squad = new Squad { PartyId = body.ReadLong(), Leader = body.ReadString() };
                int members = body.ReadInt();
                for (int m = 0; m < members; m++) {
                    squad.Members.Add(new Member {
                        Account = body.ReadString(),
                        Name = body.ReadString(),
                        Online = body.ReadBool(),
                        ClanId = body.ReadLong(),
                    });
                }
                Squads.Add(squad);
            }
            // Positions of anyone no longer with us go.
            HashSet<string> still = new HashSet<string>();
            foreach (Squad squad in Squads) { foreach (Member m in squad.Members) { still.Add(m.Account); } }
            if (Companions.RemoveAll(c => still.Contains(c.Account) == false) > 0) { Raise(PositionsChanged); }
            Raise(Changed);
        }

        private static void OnInvite(uint reqId, ZPackage body) {
            InviteInfo invite = new InviteInfo {
                Id = body.ReadLong(),
                Raid = body.ReadByte() == 1,
                FromAccount = body.ReadString(),
                FromName = body.ReadString(),
                MemberCount = body.ReadInt(),
            };
            float lifetime = body.ReadSingle();
            invite.ReceivedAt = Time.unscaledTime;
            invite.ExpiresAt = Time.unscaledTime + lifetime;
            Invites.RemoveAll(i => i.Id == invite.Id);
            Invites.Add(invite);
            Raise(InvitesChanged);
        }

        private static void OnInviteClosed(uint reqId, ZPackage body) {
            long id = body.ReadLong();
            string reason = body.ReadString();
            if (Invites.RemoveAll(i => i.Id == id) > 0) {
                if (string.IsNullOrEmpty(reason) == false) { ClanNotices.ShowInfo(Localization.instance.Localize(reason)); }
                Raise(InvitesChanged);
            }
        }

        private static void OnPositions(uint reqId, ZPackage body) {
            int count = body.ReadInt();
            float now = Time.unscaledTime;
            for (int i = 0; i < count; i++) {
                Companion c = new Companion {
                    Account = body.ReadString(),
                    Position = body.ReadVector3(),
                    Health = body.ReadSingle(),
                    Received = now,
                };
                int index = Companions.FindIndex(x => x.Account == c.Account);
                if (index >= 0) { Companions[index] = c; } else { Companions.Add(c); }
            }
            Companions.RemoveAll(c => now - c.Received > PositionLifetime);
            Companions.Sort((a, b) => string.CompareOrdinal(a.Account, b.Account));
            Raise(PositionsChanged);
        }

        internal static void Tick() {
            if (Invites.Count > 0 && Invites.RemoveAll(i => i.ExpiresAt <= Time.unscaledTime) > 0) { Raise(InvitesChanged); }
        }

        private static void Raise(Action handler) {
            if (handler == null) { return; }
            foreach (Delegate subscriber in handler.GetInvocationList()) {
                try {
                    ((Action)subscriber)();
                } catch (Exception e) {
                    Logger.LogError($"A Clans party listener failed: {e}");
                }
            }
        }
    }
}
