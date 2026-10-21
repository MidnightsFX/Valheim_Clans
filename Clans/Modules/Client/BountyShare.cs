using Clans.Compat;
using Clans.Net;
using Clans.Settings;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Client {
    // Epic Loot bounties shared with the clan, switched by the player on the large map (and in the Client
    // config) while the server allows it.
    //
    // While sharing, this client tells the server its open bounties whenever Epic Loot saves a change; the
    // server keeps them and passes every member's on to the clan. BountyPins draws the clanmates' bounties;
    // Epic Loot draws the player's own.
    internal static class BountyShare {
        // Reports are at most this far apart; a kill spree saves Epic Loot's data on every kill.
        private const float ReportGapSeconds = 1f;

        // The clanmates' open bounties, as the server last sent them.
        internal static readonly List<SharedBounty> Shared = new List<SharedBounty>();
        internal static int Version { get; private set; }

        private static bool dirty;
        private static float gapTimer;
        private static long reportedClan;
        // What the last report said, so an unchanged save sends nothing.
        private static string lastReport;

        internal static bool Available => ClansConfig.ClanBounties.Value && ClanClient.InClan && EpicLootApi.BountiesAvailable;

        internal static void Init() {
            ClanNet.RegisterClient(Op.ClanBounties, OnBounties);
            ClansConfig.ShareBounties.SettingChanged += (_, _) => dirty = true;
            ClansConfig.ClanBounties.SettingChanged += (_, _) => dirty = true;
            if (EpicLootApi.BountiesAvailable) { EpicLootApi.AddAdventureDataChangedListener(() => dirty = true); }
        }

        internal static void Reset() {
            Shared.Clear();
            Version++;
            dirty = false;
            gapTimer = 0f;
            reportedClan = 0;
            lastReport = null;
        }

        // A fresh report on every login: the server keeps the last one, which may be from before a change made
        // while this player was away (another character, an edited config).
        internal static void OnWelcome() {
            dirty = true;
            lastReport = null;
        }

        internal static void Tick(float dt) {
            if (ClanClient.Welcomed == false || EpicLootApi.BountiesAvailable == false) { return; }
            long clan = ClanClient.Me?.ClanId ?? 0;
            // Joining a clan shares the bounties with it; leaving one, the server already forgot them.
            if (clan != reportedClan) { dirty = true; }
            gapTimer -= dt;
            if (dirty == false || gapTimer > 0f) { return; }
            dirty = false;
            gapTimer = ReportGapSeconds;
            Report(clan);
        }

        private static void Report(long clan) {
            bool sharing = clan != 0 && ClansConfig.ClanBounties.Value && ClansConfig.ShareBounties.Value && Player.m_localPlayer != null;
            List<ReportedBounty> bounties = sharing ? EpicLootBounties.ParseActive(EpicLootApi.ActiveBountiesJson()) : new List<ReportedBounty>();
            if (bounties.Count > BountyLimits.MaxPerAccount) { bounties.RemoveRange(BountyLimits.MaxPerAccount, bounties.Count - BountyLimits.MaxPerAccount); }

            ZPackage probe = new ZPackage();
            probe.Write(clan);
            WriteBounties(probe, bounties);
            string fingerprint = probe.GetBase64();
            reportedClan = clan;
            if (fingerprint == lastReport) { return; }
            lastReport = fingerprint;

            bool announce = ClansConfig.AnnounceBounties.Value;
            ClanRequests.Report(Op.BountyReport, pkg => {
                pkg.Write(announce);
                WriteBounties(pkg, bounties);
            });
        }

        private static void WriteBounties(ZPackage pkg, List<ReportedBounty> bounties) {
            pkg.Write(bounties.Count);
            foreach (ReportedBounty bounty in bounties) { bounty.Write(pkg); }
        }

        private static void OnBounties(uint reqId, ZPackage body) {
            Shared.Clear();
            int count = body.ReadInt();
            for (int i = 0; i < count; i++) {
                SharedBounty bounty = SharedBounty.Read(body);
                if (bounty.OwnerAccount != ClanClient.MyAccount) { Shared.Add(bounty); }
            }
            Version++;
        }

        // Opens the large map at a shared bounty (from its chat link).
        internal static void ShowOnMap(Vector3 position) {
            if (Minimap.instance == null || Player.m_localPlayer == null) { return; }
            Minimap.instance.ShowPointOnMap(position);
        }
    }
}
