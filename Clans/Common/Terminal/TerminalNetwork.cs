using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

#pragma warning disable IDE0130
namespace Clans {
#pragma warning restore IDE0130

    // The admin-client -> server command relay.
    //
    // Needed because a dedicated server has no Terminal of its own -- Console.Awake and
    // Terminal.InitTerminal only ever run on a client -- so a command that acts on server-owned state
    // is otherwise untypeable anywhere. Vanilla does have a relay (ConsoleCommand remoteCommand ->
    // ZNet.RPC_RemoteCommand), but it ends in Console.instance.TryRunCommand and null-references
    // headless, so it cannot be used for this.
    //
    // Both directions use the peer connection itself (ZNetPeer.m_rpc), as RPC_RemoteCommand does, never
    // ZRoutedRpc, which is what Jotunn's CustomRPC is built on. A routed RPC's sender id is written by the
    // sending client and relayed untouched by the server, so any client could claim to be an admin. A
    // ZRpc handler is bound to the socket the call arrived on: the server reads the requester's identity
    // from that socket, and a client registers the output handler only on its connection to the server,
    // so nothing else can print into its console.
    internal static class TerminalNetwork {
        // GUID-prefixed: every connection has one table of RPC name hashes, shared with vanilla and every
        // other mod, and a second Register under the same hash silently replaces the first.
        private const string RequestRpc = Clans.PluginGUID + ".CommandRequest";
        private const string OutputRpc = Clans.PluginGUID + ".CommandOutput";

        // Takes TerminalManager's private Harmony instance, for the same drop-in reason it has one.
        internal static void Init(Harmony harmony) {
            try {
                MethodInfo target = AccessTools.Method(typeof(ZNet), "OnNewConnection");
                if (target == null) {
                    Logger.LogError("ZNet.OnNewConnection not found; server-authoritative commands cannot be relayed.");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(TerminalNetwork), nameof(RegisterRpcs))));
            } catch (Exception e) {
                Logger.LogError($"Could not patch ZNet.OnNewConnection for the command relay: {e.Message}");
            }
        }

        // ZNet registers its own per-connection RPCs here, on both ends of every connection: on the server
        // once per client, and on a client once, for its link to the server.
        private static void RegisterRpcs(ZNet __instance, ZNetPeer peer) {
            if (__instance.IsServer()) {
                peer.m_rpc.Register<ZPackage>(RequestRpc, OnServerReceiveCommandRequest);
            } else {
                peer.m_rpc.Register<ZPackage>(OutputRpc, OnClientReceiveCommandOutput);
            }
        }

        internal static void SendRequest(ZRpc server, string command, string[] args) {
            ZPackage package = new ZPackage();
            package.Write(command);
            package.Write(args.Length);
            foreach (string arg in args) { package.Write(arg); }

            // The requesting player's position travels with the request: it is the only centre point a
            // dedicated server can act around, having no local player of its own.
            bool hasCenter = Player.m_localPlayer != null;
            package.Write(hasCenter);
            package.Write(hasCenter ? Player.m_localPlayer.transform.position : Vector3.zero);

            server.Invoke(RequestRpc, package);
        }

        internal static void SendOutput(ZNetPeer peer, ZPackage package) {
            peer.m_rpc.Invoke(OutputRpc, package);
        }

        // Server handler. Gate on admin because any peer could craft this RPC; the client-side check is
        // only there to give a clearer message. The admin check reads the socket the request arrived on,
        // which the client cannot choose, the same check vanilla's RPC_RemoteCommand makes.
        private static void OnServerReceiveCommandRequest(ZRpc rpc, ZPackage package) {
            ZNetPeer requester = ZNet.instance.GetPeer(rpc);
            // Not ready means the handshake has not finished, so this is not yet a player in the world.
            if (requester == null || requester.IsReady() == false) { return; }

            string command = package.ReadString();
            string hostName = rpc.GetSocket().GetHostName();
            if (RequiresAdmin(command) && ZNet.instance.IsAdmin(hostName) == false) {
                Logger.LogWarning($"Rejecting '{command}' from non-admin {requester.m_playerName} ({hostName}).");
                // Answer rather than going quiet, so the sender sees a refusal instead of nothing.
                TerminalOutput refusal = TerminalOutput.Remote(requester);
                refusal.Error($"Only server admins can run {command}.", log: false);
                refusal.Flush();
                return;
            }

            int argCount = package.ReadInt();
            string[] args = new string[argCount];
            for (int i = 0; i < argCount; i++) { args[i] = package.ReadString(); }
            bool hasCenter = package.ReadBool();
            Vector3 center = package.ReadVector3();

            TerminalManager.ExecuteFromNetwork(command, args, center, hasCenter, TerminalOutput.Remote(requester));
        }

        // Client handler: a batch of output lines from a command this client asked the server to run.
        // Severity travels as a byte and the colour is applied on arrival, so the server's log never
        // contains markup and each client honours its own EnableTerminalColors setting.
        private static void OnClientReceiveCommandOutput(ZRpc rpc, ZPackage package) {
            int count = package.ReadInt();
            for (int i = 0; i < count; i++) {
                OutputLevel level = (OutputLevel)package.ReadByte();
                TerminalManager.PrintResponse(level, package.ReadString());
            }
        }

        // Unknown names default to admin-only: an unregistered command is rejected downstream anyway, and
        // this way a typo can never be treated as public.
        private static bool RequiresAdmin(string command) {
            return TerminalManager.Registry.TryGetValue(command.ToLowerInvariant(), out ModCommand found) == false
                || found.RequiresAdmin;
        }
    }
}
