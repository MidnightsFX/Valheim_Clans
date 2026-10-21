using Clans.Net;
using Clans.Server.Model;
using HarmonyLib;

namespace Clans.Server {
    // Server lifecycle and the once-a-second housekeeping. Runs on a dedicated server, a listen host and in
    // single player; never on a plain client.
    internal static class ClanServer {
        private const float HousekeepingSeconds = 1f;
        private static float housekeepingTimer;

        internal static void Init() {
            ClanNet.RegisterServer(Op.Hello, OnHello);
            ClanNet.RegisterServer(Op.Ping, OnPing);
            ClanService.Register();
            LevelService.Register();
            MailboxService.Register();
            PartyService.Register();
            XpShareService.Register();
            WarService.Register();
            StorageService.Register();
            ActivityService.Register();
            ChatService.Register();
            PositionRelay.Register();
            BountyRelay.Register();
            LootRolls.Init();
        }

        internal static ClansWorldState State => ClanStore.State;

        internal static void Tick(float dt) {
            if (ClanStore.EnsureLoaded() == false) { return; }
            ClanStore.Tick(dt);
            WorldBiomeIndex.Tick(dt);
            PartyService.Tick(dt);
            PositionRelay.Tick(dt);
            BountyRelay.Tick(dt);
            Broadcaster.Tick(dt);

            housekeepingTimer -= dt;
            if (housekeepingTimer > 0f) { return; }
            housekeepingTimer = HousekeepingSeconds;
            long now = ClanStore.Now;
            ClanService.Housekeeping(now);
            WarService.Housekeeping(now);
            StorageService.Housekeeping(now);
            ActivityService.Housekeeping(now, HousekeepingSeconds);
            BountyRelay.Housekeeping(now);
        }

        internal static void OnStateLoaded() {
            ClanService.OnStateLoaded();
            ActivityService.OnStateLoaded();
            BountyRelay.OnStateLoaded();
            Broadcaster.MarkAll();
        }

        internal static void OnWorldUnload() {
            ClanStore.Unload();
            Sessions.Clear();
            Broadcaster.Reset();
            ClanService.Reset();
            PartyService.Reset();
            XpShareService.Reset();
            WarService.Reset();
            StorageService.Reset();
            ActivityService.Reset();
            ChatService.Reset();
            PositionRelay.Reset();
            BountyRelay.Reset();
            WorldBiomeIndex.Reset();
            ClanNet.ResetSession();
        }

        // A client (or the local player of a host) is in the world and wants its state.
        private static void OnHello(ServerRequest request) {
            if (ClanStore.EnsureLoaded() == false) {
                request.Fail("$clans_err_not_ready");
                return;
            }
            int clientProto = request.Body.ReadInt();
            if (clientProto != ClanNet.Proto) {
                request.Fail("$clans_err_version");
                return;
            }
            ClanSender sender = request.Sender;
            Sessions.Touch(sender);
            ClanService.OnAccountOnline(sender);

            ClanNet.Send(sender, Op.Welcome, request.ReqId, pkg => {
                pkg.Write((int)ClanNet.Proto);
                pkg.Write(sender.Account);
                pkg.Write(ClanStore.Now);
                pkg.Write(Core.WeeklyClock.PeriodStart(ClanStore.Now));
                pkg.Write(Core.WeeklyClock.PeriodEnd(ClanStore.Now));
                pkg.Write((int)Compat.ModPresence.ServerCapabilities());
            });
            Broadcaster.SendEverythingTo(sender);
            PartyService.OnAccountOnline(sender);
            MailboxService.SendPending(sender);
            BountyRelay.OnAccountOnline(sender);
            Logger.LogInfo($"{sender} is online.");
        }

        private static void OnPing(ServerRequest request) {
            long sent = request.Body.ReadLong();
            ClanNet.Send(request.Sender, Op.Pong, request.ReqId, pkg => pkg.Write(sent));
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        private static class DisconnectPatch {
            private static void Prefix(ZNetPeer peer) {
                if (ZNet.instance == null || ZNet.instance.IsServer() == false || peer == null) { return; }
                string account = Sessions.RemovePeer(peer.m_uid);
                if (account == null) { return; }
                ClanService.OnAccountOffline(account);
                Logger.LogInfo($"{peer.m_playerName} ({account}) went offline.");
            }
        }
    }
}
