using System;
using System.Collections.Generic;
using UnityEngine;

namespace Clans.Net {
    // The server's answer to one request.
    internal sealed class NetResult {
        internal readonly bool Ok;
        internal readonly string Token;
        internal readonly string[] Args;
        // Anything the handler wrote besides ok/error. Empty for most requests.
        internal readonly ZPackage Extra;

        internal NetResult(bool ok, string token, string[] args, ZPackage extra) {
            Ok = ok;
            Token = token ?? "";
            Args = args ?? new string[0];
            Extra = extra ?? new ZPackage();
        }

        internal static NetResult Failed(string token) => new NetResult(false, token, null, null);

        internal const string TimeoutToken = "$clans_err_timeout";

        // No answer came. The server may still have done it, so nothing paid for it is given back.
        internal bool TimedOut => Ok == false && Token == TimeoutToken;

        // The error translated, with its arguments filled in.
        internal string Message() {
            if (string.IsNullOrEmpty(Token)) { return ""; }
            string text = Localization.instance != null ? Localization.instance.Localize(Token) : Token;
            if (Args.Length == 0) { return text; }
            string[] localized = new string[Args.Length];
            for (int i = 0; i < Args.Length; i++) {
                localized[i] = Localization.instance != null ? Localization.instance.Localize(Args[i]) : Args[i];
            }
            try {
                return string.Format(text, localized);
            } catch (FormatException) {
                return text + " (" + string.Join(", ", localized) + ")";
            }
        }
    }

    // Client side of request/response: hands out request ids, remembers what to call back, and gives up on
    // requests the server never answers.
    internal static class ClanRequests {
        private const float TimeoutSeconds = 20f;

        private sealed class Pending {
            internal Action<NetResult> Callback;
            internal float Deadline;
            internal Op Op;
        }

        private static readonly Dictionary<uint, Pending> pending = new Dictionary<uint, Pending>();
        private static uint nextId = 1;

        internal static void Init() {
            ClanNet.RegisterClient(Op.Response, OnResponse);
        }

        // Sends a request. callback runs once, with the server's answer or a timeout. It may be null for
        // fire-and-forget calls whose failures should just be shown to the player.
        internal static void Send(Op op, Action<ZPackage> writeBody, Action<NetResult> callback = null) {
            uint id = nextId++;
            if (nextId == 0) { nextId = 1; }
            pending[id] = new Pending { Callback = callback, Deadline = Time.unscaledTime + TimeoutSeconds, Op = op };
            if (ClanNet.SendToServer(op, id, writeBody) == false) {
                pending.Remove(id);
                Complete(callback, NetResult.Failed("$clans_err_no_server"));
            }
        }

        // Reports and periodic updates the server does not answer unless something is wrong.
        internal static void Report(Op op, Action<ZPackage> writeBody) {
            ClanNet.SendToServer(op, 0, writeBody);
        }

        private static void OnResponse(uint reqId, ZPackage body) {
            bool ok = body.ReadBool();
            string token = body.ReadString();
            int count = body.ReadInt();
            string[] args = new string[count];
            for (int i = 0; i < count; i++) { args[i] = body.ReadString(); }
            ZPackage extra = body.ReadPackage();
            NetResult result = new NetResult(ok, token, args, extra);

            if (reqId != 0 && pending.TryGetValue(reqId, out Pending entry)) {
                pending.Remove(reqId);
                Complete(entry.Callback, result);
                return;
            }
            // Unsolicited, or the request already timed out: still tell the player what went wrong.
            if (ok == false) { Client.ClanNotices.ShowError(result.Message()); }
        }

        internal static void Tick() {
            if (pending.Count == 0) { return; }
            float now = Time.unscaledTime;
            List<uint> expired = null;
            foreach (KeyValuePair<uint, Pending> entry in pending) {
                if (entry.Value.Deadline <= now) {
                    expired ??= new List<uint>();
                    expired.Add(entry.Key);
                }
            }
            if (expired == null) { return; }
            foreach (uint id in expired) {
                Pending entry = pending[id];
                pending.Remove(id);
                Logger.LogWarning($"No answer from the server to {entry.Op}.");
                Complete(entry.Callback, NetResult.Failed(NetResult.TimeoutToken));
            }
        }

        internal static void Reset() {
            pending.Clear();
        }

        private static void Complete(Action<NetResult> callback, NetResult result) {
            if (callback == null) {
                if (result.Ok == false) { Client.ClanNotices.ShowError(result.Message()); }
                return;
            }
            try {
                callback(result);
            } catch (Exception e) {
                Logger.LogError($"A Clans request callback failed: {e}");
            }
        }
    }
}
