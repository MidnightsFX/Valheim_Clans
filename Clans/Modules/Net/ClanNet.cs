using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Clans.Net {
    internal delegate void ServerHandler(ServerRequest request);
    internal delegate void ClientHandler(uint reqId, ZPackage body);

    // The Clans wire. Two RPCs on each peer connection (ZNetPeer.m_rpc), one per direction:
    //
    //   <guid>.C2S   client -> server requests and reports
    //   <guid>.S2C   server -> client responses and pushes
    //
    // Per-connection RPCs rather than routed ones: a routed RPC's sender id is written by the sending client,
    // while a ZRpc handler is bound to the socket the call arrived on, so the server knows who is asking.
    //
    // Every message is   u8 proto | u16 op | u32 reqId | u8 flags | body (a nested ZPackage)
    // reqId 0 means no answer is expected. flags bit 0 means the body is compressed.
    //
    // The listen host and single player are the server, with no connection to themselves. Their messages go
    // through a loopback queue instead, still serialized so the same code paths are exercised, and handled
    // on the next frame so a UI callback never re-enters the server mid-update.
    internal static class ClanNet {
        // 2: ranks and raid leaders on the roster, the leader's account in the directory (for Clans.API).
        internal const byte Proto = 2;
        private const string C2S = Clans.PluginGUID + ".C2S";
        private const string S2C = Clans.PluginGUID + ".S2C";

        // Steam refuses reliable messages over 512 KB; stay well under it and refuse anything bigger on receipt.
        internal const int MaxMessageBytes = 250000;
        private const int CompressAboveBytes = 32000;
        // A compressed body may not unpack to more than this: a small message must not fill memory.
        private const int MaxInflatedBytes = 8 * 1024 * 1024;
        private const byte FlagCompressed = 1;

        private static readonly Dictionary<Op, ServerHandler> serverHandlers = new Dictionary<Op, ServerHandler>();
        private static readonly Dictionary<Op, ClientHandler> clientHandlers = new Dictionary<Op, ClientHandler>();

        private struct Looped {
            internal bool ToServer;
            internal byte[] Bytes;
        }
        private static readonly Queue<Looped> loopback = new Queue<Looped>();

        // Bytes and counts per op, for `clans-net`.
        internal static readonly Dictionary<Op, long> BytesSent = new Dictionary<Op, long>();
        internal static readonly Dictionary<Op, long> BytesReceived = new Dictionary<Op, long>();

        internal static void Init() {
            ClansRunner.Ensure();
        }

        internal static void RegisterServer(Op op, ServerHandler handler) => serverHandlers[op] = handler;
        internal static void RegisterClient(Op op, ClientHandler handler) => clientHandlers[op] = handler;

        internal static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();
        internal static bool IsDedicated => ZNet.instance != null && ZNet.instance.IsDedicated();
        // Something on this machine plays: a client, a listen host or single player.
        internal static bool HasLocalPlayer => ZNet.instance != null && ZNet.instance.IsDedicated() == false;

        // ---------------------------------------------------------------- sending

        // Client -> server. Returns false when there is no server to send to.
        internal static bool SendToServer(Op op, uint reqId, Action<ZPackage> writeBody) {
            if (ZNet.instance == null) { return false; }
            byte[] bytes = Frame(op, reqId, writeBody);
            if (bytes == null) { return false; }
            Count(BytesSent, op, bytes.Length);
            if (ZNet.instance.IsServer()) {
                loopback.Enqueue(new Looped { ToServer = true, Bytes = bytes });
                return true;
            }
            ZRpc server = ZNet.instance.GetServerRPC();
            if (server == null) { return false; }
            server.Invoke(C2S, new ZPackage(bytes));
            return true;
        }

        // Server -> one client.
        internal static void Send(ClanSender to, Op op, uint reqId, Action<ZPackage> writeBody) {
            if (to == null) { return; }
            byte[] bytes = Frame(op, reqId, writeBody);
            if (bytes == null) { return; }
            SendFramed(to.Peer, to.IsLocalHost, op, bytes);
        }

        // Server -> several clients, the message built (and compressed) once for all of them.
        internal static void SendToEach(IEnumerable<ClanSender> recipients, Op op, Action<ZPackage> writeBody) {
            byte[] bytes = null;
            foreach (ClanSender to in recipients) {
                if (to == null) { continue; }
                if (bytes == null) {
                    bytes = Frame(op, 0, writeBody);
                    if (bytes == null) { return; }
                }
                SendFramed(to.Peer, to.IsLocalHost, op, bytes);
            }
        }

        // Server -> every ready client, the local host's player included.
        internal static void Broadcast(Op op, Action<ZPackage> writeBody) {
            if (ZNet.instance == null) { return; }
            byte[] bytes = Frame(op, 0, writeBody);
            if (bytes == null) { return; }
            if (HasLocalPlayer) { SendFramed(null, true, op, bytes); }
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) {
                if (peer != null && peer.IsReady()) { SendFramed(peer, false, op, bytes); }
            }
        }

        private static void SendFramed(ZNetPeer peer, bool localHost, Op op, byte[] bytes) {
            Count(BytesSent, op, bytes.Length);
            if (localHost) {
                // Only meaningful when this machine both serves and plays.
                if (HasLocalPlayer) { loopback.Enqueue(new Looped { ToServer = false, Bytes = bytes }); }
                return;
            }
            if (peer == null || peer.m_rpc == null) { return; }
            peer.m_rpc.Invoke(S2C, new ZPackage(bytes));
        }

        private static byte[] Frame(Op op, uint reqId, Action<ZPackage> writeBody) {
            ZPackage body = new ZPackage();
            try {
                writeBody?.Invoke(body);
            } catch (Exception e) {
                Logger.LogError($"Could not write {op}: {e}");
                return null;
            }

            ZPackage frame = new ZPackage();
            frame.Write(Proto);
            frame.Write((ushort)op);
            frame.Write(reqId);
            if (body.Size() > CompressAboveBytes) {
                frame.Write(FlagCompressed);
                frame.WriteCompressed(body);
            } else {
                frame.Write((byte)0);
                frame.Write(body);
            }
            byte[] bytes = frame.GetArray();
            if (bytes.Length > MaxMessageBytes) {
                Logger.LogError($"{op} is {bytes.Length} bytes, over the {MaxMessageBytes} byte limit; not sent.");
                return null;
            }
            return bytes;
        }

        // ---------------------------------------------------------------- receiving

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        private static class RegisterRpcsPatch {
            // ZNet registers its own per-connection RPCs here, on both ends of every connection: on the
            // server once per client, and on a client once, for its link to the server.
            private static void Postfix(ZNet __instance, ZNetPeer peer) {
                if (peer?.m_rpc == null) { return; }
                if (__instance.IsServer()) {
                    peer.m_rpc.Register<ZPackage>(C2S, OnServerReceive);
                } else {
                    peer.m_rpc.Register<ZPackage>(S2C, OnClientReceive);
                }
            }
        }

        private static void OnServerReceive(ZRpc rpc, ZPackage package) {
            if (ZNet.instance == null) { return; }
            ZNetPeer peer = ZNet.instance.GetPeer(rpc);
            // Not ready means the handshake has not finished, so this is not yet a player in the world.
            if (peer == null || peer.IsReady() == false) { return; }
            DispatchServer(ClanSender.FromPeer(peer), package.GetArray());
        }

        private static void OnClientReceive(ZRpc rpc, ZPackage package) {
            DispatchClient(package.GetArray());
        }

        private static bool Unframe(byte[] bytes, out Op op, out uint reqId, out ZPackage body) {
            op = Op.None;
            reqId = 0;
            body = null;
            if (bytes == null || bytes.Length > MaxMessageBytes) { return false; }
            try {
                ZPackage frame = new ZPackage(bytes);
                byte proto = frame.ReadByte();
                if (proto != Proto) {
                    Logger.LogWarning($"Clans message with protocol {proto}, expected {Proto}; ignored. Is every player on the same Clans version?");
                    return false;
                }
                op = (Op)frame.ReadUShort();
                reqId = frame.ReadUInt();
                byte flags = frame.ReadByte();
                body = (flags & FlagCompressed) != 0 ? new ZPackage(Inflate(frame.ReadByteArray())) : frame.ReadPackage();
                return true;
            } catch (Exception e) {
                Logger.LogWarning($"Malformed Clans message ({bytes.Length} bytes): {e.Message}");
                return false;
            }
        }

        // What ZPackage.WriteCompressed wrote (gzip), unpacked with a size limit.
        private static byte[] Inflate(byte[] packed) {
            using (MemoryStream input = new MemoryStream(packed))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream()) {
                byte[] buffer = new byte[16384];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0) {
                    if (output.Length + read > MaxInflatedBytes) { throw new InvalidDataException($"unpacks to more than {MaxInflatedBytes} bytes"); }
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        private static void DispatchServer(ClanSender sender, byte[] bytes) {
            if (Unframe(bytes, out Op op, out uint reqId, out ZPackage body) == false) { return; }
            Count(BytesReceived, op, bytes.Length);
            if (serverHandlers.TryGetValue(op, out ServerHandler handler) == false) {
                Logger.LogDebug($"No server handler for {op} from {sender}.");
                return;
            }
            ServerRequest request = new ServerRequest(sender, op, reqId, body);
            try {
                handler(request);
            } catch (Exception e) {
                Logger.LogError($"Handling {op} from {sender} failed: {e}");
                request.Fail("$clans_err_internal");
            }
        }

        private static void DispatchClient(byte[] bytes) {
            if (Unframe(bytes, out Op op, out uint reqId, out ZPackage body) == false) { return; }
            Count(BytesReceived, op, bytes.Length);
            if (clientHandlers.TryGetValue(op, out ClientHandler handler) == false) {
                Logger.LogDebug($"No client handler for {op}.");
                return;
            }
            try {
                handler(reqId, body);
            } catch (Exception e) {
                Logger.LogError($"Handling {op} from the server failed: {e}");
            }
        }

        // Called every frame by ClansRunner.
        internal static void PumpLoopback() {
            // Bounded so a handler that queues more work cannot spin this frame forever.
            int count = loopback.Count;
            for (int i = 0; i < count && loopback.Count > 0; i++) {
                Looped item = loopback.Dequeue();
                if (item.ToServer) {
                    if (IsServer) { DispatchServer(ClanSender.LocalHost(), item.Bytes); }
                } else {
                    DispatchClient(item.Bytes);
                }
            }
        }

        internal static void ResetSession() {
            loopback.Clear();
        }

        private static void Count(Dictionary<Op, long> table, Op op, int bytes) {
            table.TryGetValue(op, out long total);
            table[op] = total + bytes;
        }
    }

    // One request as a server handler sees it, with the ways to answer it.
    internal sealed class ServerRequest {
        internal readonly ClanSender Sender;
        internal readonly Op Op;
        internal readonly uint ReqId;
        internal readonly ZPackage Body;
        private bool answered;

        internal ServerRequest(ClanSender sender, Op op, uint reqId, ZPackage body) {
            Sender = sender;
            Op = op;
            ReqId = reqId;
            Body = body;
        }

        internal void Ok(Action<ZPackage> writeExtra = null) => Answer(true, "", null, writeExtra);

        // token is a localization token ("$clans_err_..."); args fill {0}, {1}... in its translation.
        internal void Fail(string token, params string[] args) => Answer(false, token, args, null);

        private void Answer(bool ok, string token, string[] args, Action<ZPackage> writeExtra) {
            if (answered) { return; }
            answered = true;
            // A report sent without a request id expects no answer; a failure still tells the player why.
            if (ReqId == 0 && ok) { return; }
            ClanNet.Send(Sender, Op.Response, ReqId, pkg => {
                pkg.Write(ok);
                pkg.Write(token ?? "");
                int count = args == null ? 0 : args.Length;
                pkg.Write(count);
                for (int i = 0; i < count; i++) { pkg.Write(args[i] ?? ""); }
                ZPackage extra = new ZPackage();
                writeExtra?.Invoke(extra);
                pkg.Write(extra);
            });
        }
    }

    // Drives the per-frame work of every Clans system from one persistent object.
    internal sealed class ClansRunner : MonoBehaviour {
        private static ClansRunner instance;

        internal static void Ensure() {
            if (instance != null) { return; }
            GameObject go = new GameObject(Clans.PluginName + "_Runner");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ClansRunner>();
        }

        private void Update() {
            if (ZNet.instance == null) { return; }
            float dt = Time.unscaledDeltaTime;
            ClanNet.PumpLoopback();
            try {
                if (ClanNet.IsServer) { Server.ClanServer.Tick(dt); }
            } catch (Exception e) {
                Logger.LogError($"Clans server tick failed: {e}");
            }
            try {
                if (ClanNet.HasLocalPlayer) {
                    Client.ClanClient.Tick(dt);
                    UI.InventoryHooks.Tick();
                }
            } catch (Exception e) {
                Logger.LogError($"Clans client tick failed: {e}");
            }
        }
    }
}
