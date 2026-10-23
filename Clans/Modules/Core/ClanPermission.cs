using System;
using System.Collections.Generic;

namespace Clans.Core {
    // What a rank may do. Stored per rank in the save file and in ranks.yaml as a list of names, so the
    // numeric values only matter on the wire; append new flags at the end and never renumber.
    [Flags]
    public enum ClanPermission : uint {
        None = 0,
        ManageApplications = 1 << 0,
        Invite = 1 << 1,
        Kick = 1 << 2,
        Promote = 1 << 3,
        Demote = 1 << 4,
        EditInfo = 1 << 5,
        EditEmblem = 1 << 6,
        ManageWar = 1 << 7,
        StartActivities = 1 << 8,
        UpgradeStorage = 1 << 9,
        StorageDeposit = 1 << 10,
        StorageWithdraw = 1 << 11,
        TreasuryDonate = 1 << 12,
        TreasuryWithdraw = 1 << 13,
        EditRanks = 1 << 14,
        UpgradeClan = 1 << 15,
        ClanFeasts = 1 << 16,
    }

    internal static class ClanPermissions {
        // Every defined flag. The leader rank always holds all of them, whatever its stored value says.
        internal static readonly ClanPermission All = Combine((ClanPermission[])Enum.GetValues(typeof(ClanPermission)));

        internal static ClanPermission Combine(IEnumerable<ClanPermission> flags) {
            ClanPermission result = ClanPermission.None;
            if (flags == null) { return result; }
            foreach (ClanPermission flag in flags) { result |= flag; }
            return result;
        }

        internal static List<ClanPermission> Split(ClanPermission value) {
            List<ClanPermission> result = new List<ClanPermission>();
            foreach (ClanPermission flag in Enum.GetValues(typeof(ClanPermission))) {
                if (flag != ClanPermission.None && (value & flag) == flag) { result.Add(flag); }
            }
            return result;
        }

        internal static bool Has(this ClanPermission value, ClanPermission flag) => (value & flag) == flag;

        // What a member at this rank position (0 is the leader) may actually do, given the rank's stored rights.
        internal static ClanPermission Effective(int rankIndex, ClanPermission stored) {
            ClanPermission permissions = rankIndex == 0 ? All : stored;
            // Switched off for everyone, the leader included.
            if (Settings.ClansConfig.TreasuryWithdrawEnabled.Value == false) { permissions &= ~ClanPermission.TreasuryWithdraw; }
            return permissions;
        }

        // Localization token for a permission's display name.
        internal static string Token(ClanPermission flag) => "$clans_perm_" + flag.ToString().ToLowerInvariant();
    }
}
