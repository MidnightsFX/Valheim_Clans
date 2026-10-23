using Clans.Compat;
using Clans.Core;
using Clans.Net;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Client {
    // What this player's client knows: every clan (Directory), every online player (Roster), its own standing
    // and its own clan's detail. Everything here is pushed by the server; nothing is decided locally.
    internal static class ClanClient {
        private const float HelloRetrySeconds = 5f;

        internal static bool Welcomed { get; private set; }
        internal static string MyAccount { get; private set; } = "";
        internal static ModPresence.Capability ServerCapabilities { get; private set; }
        internal static long PeriodStartUtc { get; private set; }
        internal static long PeriodEndUtc { get; private set; }
        // Server UTC minus local UTC, so countdowns follow the server's clock.
        private static long serverClockOffset;

        internal static readonly Dictionary<long, ClanSummary> Clans = new Dictionary<long, ClanSummary>();
        internal static readonly List<WarSummary> Wars = new List<WarSummary>();
        internal static readonly Dictionary<string, RosterEntry> Roster = new Dictionary<string, RosterEntry>();
        internal static MyAccountInfo Me { get; private set; } = new MyAccountInfo();
        internal static ClanDetailInfo MyClan { get; private set; }

        internal static int DirectoryVersion { get; private set; }
        internal static int RosterVersion { get; private set; }

        internal static event Action DirectoryChanged;
        internal static event Action RosterChanged;
        internal static event Action AccountChanged;
        internal static event Action DetailChanged;

        private static bool helloWanted;
        private static float helloTimer;
        // The clan's status goes into this player's chat once per login, as soon as the clan's detail is in.
        private static bool loginStatusDue;

        internal static void Init() {
            ClanRequests.Init();
            ClanNet.RegisterClient(Op.Welcome, OnWelcome);
            ClanNet.RegisterClient(Op.DirectoryFull, OnDirectory);
            ClanNet.RegisterClient(Op.RosterFull, OnRoster);
            ClanNet.RegisterClient(Op.MyAccount, OnMyAccount);
            ClanNet.RegisterClient(Op.ClanDetail, OnDetail);
            ClanNet.RegisterClient(Op.Notify, OnNotify);
            ClanNet.RegisterClient(Op.Pong, OnPong);
            RewardClient.Init();
            PartyClient.Init();
            SkillShareClient.Init();
            ClanStorageClient.Init();
            ActivityClient.Init();
            ChatClient.Init();
            ClanMap.Init();
            BountyShare.Init();
        }

        internal static long ServerNow => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + serverClockOffset;

        internal static bool InClan => Me != null && Me.ClanId != 0;

        internal static void Tick(float dt) {
            ClanRequests.Tick();
            if (helloWanted && Welcomed == false) {
                helloTimer -= dt;
                if (helloTimer <= 0f) {
                    helloTimer = HelloRetrySeconds;
                    ClanRequests.Report(Op.Hello, pkg => pkg.Write((int)ClanNet.Proto));
                }
            }
            RewardClient.Tick(dt);
            PartyClient.Tick();
            SkillShareClient.Tick(dt);
            ClanStorageClient.Tick(dt);
            Gameplay.RaceChests.Tick(dt);
            ActivityPins.Tick(dt);
            BountyShare.Tick(dt);
            BountyPins.Tick(dt);
            ChatClient.Tick();
            UI.ChatPanel.Ensure();
            // After the chat panel had its chance to appear, so the line lands where chat is shown.
            if (loginStatusDue && MyClan != null && Chat.instance != null) {
                loginStatusDue = false;
                ChatLog.AddClanStatus(MyClan.Name, MyClan.Status);
            }
        }

        internal static void OnWorldUnload() {
            Welcomed = false;
            helloWanted = false;
            loginStatusDue = false;
            MyAccount = "";
            Clans.Clear();
            Wars.Clear();
            Roster.Clear();
            Me = new MyAccountInfo();
            MyClan = null;
            DirectoryVersion = 0;
            RosterVersion = 0;
            ClanRequests.Reset();
            RewardClient.Reset();
            PartyClient.Reset();
            SkillShareClient.Reset();
            ClanStorageClient.Reset();
            ActivityClient.Reset();
            ActivityPins.Reset();
            ChatClient.Reset();
            ClanMap.Reset();
            BountyShare.Reset();
            BountyPins.Reset();
            PlayerMap.Reset();
            WeeklyClock.DebugOffsetSeconds = 0;
            // Other mods' cached answers are now stale: nobody is in a clan until the next world says otherwise.
            ApiEvents.RaiseChanged();
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class SpawnedPatch {
            private static void Postfix(Player __instance) {
                if (__instance != Player.m_localPlayer || Welcomed) { return; }
                helloWanted = true;
                helloTimer = 0f;
            }
        }

        // ---------------------------------------------------------------- lookups

        internal static ClanSummary Clan(long id) {
            if (id == 0) { return null; }
            Clans.TryGetValue(id, out ClanSummary clan);
            return clan;
        }

        internal static RosterEntry RosterOf(string account) {
            if (string.IsNullOrEmpty(account)) { return null; }
            Roster.TryGetValue(account, out RosterEntry entry);
            return entry;
        }

        internal static long ClanIdOf(string account) {
            if (account == MyAccount) { return Me.ClanId; }
            return RosterOf(account)?.ClanId ?? 0;
        }

        internal static ClanSummary ClanOfPlayer(Player player) {
            return Clan(ClanIdOf(PlayerMap.AccountOf(player)));
        }

        // The war between two clans, if they are fighting one.
        internal static WarSummary ActiveWar(long clanA, long clanB) {
            if (clanA == 0 || clanB == 0 || clanA == clanB) { return null; }
            foreach (WarSummary war in Wars) {
                if (war.State == Server.Model.WarState.Active && war.Involves(clanA) && war.Involves(clanB)) { return war; }
            }
            return null;
        }

        internal static bool HasPermission(ClanPermission permission) => Me != null && Me.Permissions.Has(permission);

        // ---------------------------------------------------------------- handlers

        private static void OnWelcome(uint reqId, ZPackage body) {
            int proto = body.ReadInt();
            MyAccount = body.ReadString();
            long serverNow = body.ReadLong();
            PeriodStartUtc = body.ReadLong();
            PeriodEndUtc = body.ReadLong();
            ServerCapabilities = (ModPresence.Capability)body.ReadInt();
            serverClockOffset = serverNow - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // A retried hello can be welcomed twice; the status is shown once.
            if (Welcomed == false) { loginStatusDue = true; }
            Welcomed = true;
            ClanMap.ReportSharing();
            BountyShare.OnWelcome();
            Logger.LogDebug($"Clans server says hello: account {MyAccount}, protocol {proto}.");
        }

        private static void OnDirectory(uint reqId, ZPackage body) {
            DirectoryVersion = body.ReadInt();
            Clans.Clear();
            int count = body.ReadInt();
            for (int i = 0; i < count; i++) {
                ClanSummary clan = ClanSummary.Read(body);
                Clans[clan.Id] = clan;
            }
            Wars.Clear();
            int wars = body.ReadInt();
            for (int i = 0; i < wars; i++) { Wars.Add(WarSummary.Read(body)); }
            Raise(DirectoryChanged);
        }

        private static void OnRoster(uint reqId, ZPackage body) {
            RosterVersion = body.ReadInt();
            Roster.Clear();
            int count = body.ReadInt();
            for (int i = 0; i < count; i++) {
                RosterEntry entry = RosterEntry.Read(body);
                Roster[entry.Account] = entry;
            }
            Raise(RosterChanged);
        }

        private static void OnMyAccount(uint reqId, ZPackage body) {
            Me = MyAccountInfo.Read(body);
            if (string.IsNullOrEmpty(Me.Account) == false) { MyAccount = Me.Account; }
            if (Me.ClanId == 0) {
                MyClan = null;
                // No clan at login: nothing to show, nor later on joining one.
                loginStatusDue = false;
            }
            Raise(AccountChanged);
        }

        private static void OnDetail(uint reqId, ZPackage body) {
            ClanDetailInfo detail = ClanDetailInfo.Read(body);
            // A detail for a clan this player has since left can arrive late; ignore it.
            if (Me.ClanId != 0 && detail.Id != Me.ClanId) { return; }
            MyClan = detail;
            Raise(DetailChanged);
        }

        private static void OnNotify(uint reqId, ZPackage body) {
            byte severity = body.ReadByte();
            string token = body.ReadString();
            int count = body.ReadInt();
            string[] args = new string[count];
            for (int i = 0; i < count; i++) { args[i] = body.ReadString(); }
            string text = new NetResult(severity == 0, token, args, null).Message();
            if (severity == 0) { ClanNotices.ShowInfo(text); } else { ClanNotices.ShowError(text); }
        }

        internal static event Action<float> PongReceived;

        private static void OnPong(uint reqId, ZPackage body) {
            long sentTicks = body.ReadLong();
            float ms = (float)TimeSpan.FromTicks(DateTime.UtcNow.Ticks - sentTicks).TotalMilliseconds;
            PongReceived?.Invoke(ms);
        }

        private static void Raise(Action handler) {
            if (handler == null) { return; }
            foreach (Delegate subscriber in handler.GetInvocationList()) {
                try {
                    ((Action)subscriber)();
                } catch (Exception e) {
                    // One broken listener (a destroyed UI element) must not stop the others.
                    Logger.LogError($"A Clans update listener failed: {e}");
                }
            }
        }
    }
}
