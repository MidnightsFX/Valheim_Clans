using Clans.Core;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Clans.Settings {
    // ranks.yaml: the ranks a new clan starts with and what each may do. Each clan copies this list when it
    // is founded; after that its leader edits the clan's own copy, so changing this file only affects clans
    // founded afterwards.
    public class RanksSettings {
        [DefaultValue(8)]
        public int MaxRanks { get; set; } = 8;

        // Top to bottom. The first entry is the leader rank, the last is the rank new members join at.
        public List<RankTemplate> DefaultRanks { get; set; } = new List<RankTemplate>();

        internal static RanksSettings BuildDefaults() {
            return new RanksSettings {
                MaxRanks = 8,
                DefaultRanks = new List<RankTemplate> {
                    new RankTemplate { Name = "$clans_rank_leader", Permissions = ClanPermissions.Split(ClanPermissions.All) },
                    new RankTemplate { Name = "$clans_rank_elder", Permissions = new List<ClanPermission> {
                        ClanPermission.ManageApplications, ClanPermission.Invite, ClanPermission.Kick,
                        ClanPermission.Promote, ClanPermission.Demote, ClanPermission.EditInfo,
                        ClanPermission.EditEmblem, ClanPermission.ManageWar, ClanPermission.StartActivities,
                        ClanPermission.UpgradeStorage, ClanPermission.StorageDeposit, ClanPermission.StorageWithdraw,
                        ClanPermission.TreasuryDonate, ClanPermission.UpgradeClan, ClanPermission.ClanFeasts } },
                    new RankTemplate { Name = "$clans_rank_respected", Permissions = new List<ClanPermission> {
                        ClanPermission.ManageApplications, ClanPermission.Invite, ClanPermission.StorageDeposit,
                        ClanPermission.StorageWithdraw, ClanPermission.TreasuryDonate } },
                    new RankTemplate { Name = "$clans_rank_known", Permissions = new List<ClanPermission> {
                        ClanPermission.StorageDeposit, ClanPermission.StorageWithdraw, ClanPermission.TreasuryDonate } },
                    new RankTemplate { Name = "$clans_rank_welp", Permissions = new List<ClanPermission> {
                        ClanPermission.StorageDeposit, ClanPermission.TreasuryDonate } },
                },
            };
        }

        internal static ValidationReport Validate(RanksSettings next, RanksSettings previous) {
            ValidationReport report = new ValidationReport();
            if (next.DefaultRanks == null || next.DefaultRanks.Count < 2) {
                return report.Error("DefaultRanks needs at least two ranks: a leader rank and a rank new members join at");
            }
            if (next.MaxRanks < 2) {
                report.Warn($"MaxRanks is {next.MaxRanks}; using 2.");
                next.MaxRanks = 2;
            }
            if (next.DefaultRanks.Count > next.MaxRanks) {
                report.Warn($"DefaultRanks has {next.DefaultRanks.Count} ranks but MaxRanks is {next.MaxRanks}; the extra ranks in the middle are dropped.");
                List<RankTemplate> trimmed = next.DefaultRanks.Take(next.MaxRanks - 1).ToList();
                trimmed.Add(next.DefaultRanks.Last());
                next.DefaultRanks = trimmed;
            }
            return report;
        }

        // A fresh clan's rank list, with stable ids 1..n top to bottom.
        internal List<RankDef> CreateClanRanks() {
            List<RankDef> ranks = new List<RankDef>();
            for (int i = 0; i < DefaultRanks.Count; i++) {
                RankTemplate template = DefaultRanks[i];
                bool isLeader = i == 0;
                bool isEntry = i == DefaultRanks.Count - 1;
                ranks.Add(new RankDef {
                    Id = i + 1,
                    Name = template.Name ?? "",
                    Permissions = isLeader ? ClanPermissions.All : ClanPermissions.Combine(template.Permissions),
                    Fixed = isLeader || isEntry,
                });
            }
            return ranks;
        }
    }

    public class RankTemplate {
        // Shown to players. A "$token" is localized; anything else is shown as written.
        public string Name { get; set; } = "";
        public List<ClanPermission> Permissions { get; set; } = new List<ClanPermission>();
    }
}
