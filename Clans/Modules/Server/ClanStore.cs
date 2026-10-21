using Clans.Server.Model;
using HarmonyLib;
using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Clans.Server {
    // Loads and saves ClansWorldState, one file per world:
    //
    //   BepInEx/config/Clans/SavedData/clans.<world name>-<world uid>.yaml
    //
    // Next to the other config rather than inside the world folder, because the game's save code deletes any
    // file in a world folder whose extension it does not recognise.
    //
    // Written when the world is saved and on shutdown, and a few seconds after anything players would hate
    // to lose (storage, treasury) changes. Each write goes to a temp file first and keeps the previous file
    // as .bak, so a crash mid-write never leaves nothing. A file that will not parse is set aside as .corrupt
    // and the .bak is tried before starting fresh.
    internal static class ClanStore {
        private const float SoonDelaySeconds = 3f;
        // While changes keep coming (someone sorting clan storage), saves are at least this far apart.
        private const float SoonMinGapSeconds = 10f;

        internal static ClansWorldState State { get; private set; }
        private static string loadedKey;
        private static World loadedWorld;
        private static string loadedPath;
        private static bool dirty;
        private static float soonTimer = -1f;
        private static float lastSave = -1000f;

        // The file is written on a worker thread, so a slow disk never stalls the server: one write at a
        // time, and a newer text replaces one still waiting. The text itself is made on the main thread.
        private static readonly object writeLock = new object();
        private static string pendingText;
        private static string pendingPath;
        private static bool writerRunning;
        private static volatile bool writeFailed;

        internal static bool Loaded => State != null;

        // Loads the current world's file on first use. Server only, and only once the world is known: on a
        // dedicated server the world arrives after ZNet.Awake.
        internal static bool EnsureLoaded() {
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return false; }
            World world = ZNet.World;
            if (world == null || string.IsNullOrEmpty(world.m_name)) { return false; }
            // Called every frame; skip building the key while the same world stays loaded.
            if (State != null && ReferenceEquals(world, loadedWorld)) { return true; }

            string key = WorldKey(world);
            if (State != null && loadedKey == key) { return true; }
            if (State != null) {
                // A different world on the same process (back to the menu, into another world).
                Save();
                Unload();
            }

            loadedKey = key;
            loadedWorld = world;
            loadedPath = Path.Combine(YamlConfigManager.ConfigDirectory("SavedData"), "clans." + key + ".yaml");
            State = LoadFile(loadedPath, world) ?? NewState(world);
            dirty = false;
            Logger.LogInfo($"Clans data for world '{world.m_name}': {State.Clans.Count} clans, {State.Accounts.Count} accounts ({loadedPath}).");
            ClanServer.OnStateLoaded();
            return true;
        }

        private static string WorldKey(World world) {
            StringBuilder name = new StringBuilder();
            foreach (char c in world.m_name) {
                name.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }
            return world.m_uid == 0 ? name.ToString() : $"{name}-{world.m_uid:x}";
        }

        private static ClansWorldState NewState(World world) {
            return new ClansWorldState {
                SchemaVersion = ClansWorldState.CurrentSchema,
                WorldUid = world.m_uid,
                WorldName = world.m_name,
                NextClanId = 1,
                NextWarId = 1,
                NextActivityId = 1,
                NextDeliveryId = 1,
            };
        }

        private static ClansWorldState LoadFile(string path, World world) {
            if (File.Exists(path)) {
                ClansWorldState state = TryRead(path, out string error);
                if (state != null) { return Normalize(state, world); }
                string aside = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                Logger.LogError($"Could not read {path} ({error}). Keeping it as {Path.GetFileName(aside)} and trying the backup.");
                try { File.Move(path, aside); } catch (Exception e) { Logger.LogError($"Could not set the file aside: {e.Message}"); }
            }
            string backup = path + ".bak";
            if (File.Exists(backup)) {
                ClansWorldState state = TryRead(backup, out string error);
                if (state != null) {
                    Logger.LogWarning($"Loaded clans data from the backup {Path.GetFileName(backup)}.");
                    return Normalize(state, world);
                }
                Logger.LogError($"The backup {backup} could not be read either ({error}). Starting with no clans.");
            }
            return null;
        }

        private static ClansWorldState TryRead(string path, out string error) {
            error = null;
            try {
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) { error = "empty file"; return null; }
                return SaveFormat.Deserializer.Deserialize<ClansWorldState>(text);
            } catch (Exception strict) {
                // A key this version does not know (written by a newer version) should not cost everything.
                try {
                    ClansWorldState tolerant = SaveFormat.TolerantDeserializer.Deserialize<ClansWorldState>(File.ReadAllText(path));
                    Logger.LogWarning($"{Path.GetFileName(path)} has entries this version does not understand; they were skipped. ({strict.Message})");
                    return tolerant;
                } catch (Exception e) {
                    error = e.Message;
                    return null;
                }
            }
        }

        // Fills in anything a hand edit or an older version left null.
        private static ClansWorldState Normalize(ClansWorldState state, World world) {
            state.Clans ??= new System.Collections.Generic.Dictionary<long, ClanRecord>();
            state.Accounts ??= new System.Collections.Generic.Dictionary<string, AccountRecord>();
            state.Wars ??= new System.Collections.Generic.List<WarRecord>();
            state.WarHistory ??= new System.Collections.Generic.List<WarRecord>();
            state.Mailbox ??= new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Delivery>>();
            if (state.WorldUid != 0 && state.WorldUid != world.m_uid) {
                Logger.LogWarning($"The clans file belongs to world uid {state.WorldUid:x}, but this world is {world.m_uid:x}. Using it anyway.");
            }
            state.WorldUid = world.m_uid;
            state.WorldName = world.m_name;
            state.SchemaVersion = ClansWorldState.CurrentSchema;
            if (state.NextClanId < 1) { state.NextClanId = 1; }
            if (state.NextWarId < 1) { state.NextWarId = 1; }
            if (state.NextActivityId < 1) { state.NextActivityId = 1; }
            if (state.NextDeliveryId < 1) { state.NextDeliveryId = 1; }
            foreach (ClanRecord clan in state.Clans.Values) {
                clan.Ranks ??= new System.Collections.Generic.List<Core.RankDef>();
                clan.Members ??= new System.Collections.Generic.Dictionary<string, MemberRecord>();
                clan.Applications ??= new System.Collections.Generic.List<ApplicationRecord>();
                clan.Log ??= new System.Collections.Generic.List<LogEntry>();
                clan.Storage ??= new ClanStorageRecord();
                clan.Storage.SharedTabs ??= new System.Collections.Generic.List<string>();
                clan.Storage.PersonalTabs ??= new System.Collections.Generic.Dictionary<string, string>();
                clan.Lockouts ??= new System.Collections.Generic.Dictionary<string, long>();
                clan.Activities ??= new System.Collections.Generic.List<ActivityInstance>();
                clan.Emblem ??= new Core.EmblemData();
                if (clan.Id >= state.NextClanId) { state.NextClanId = clan.Id + 1; }
            }
            foreach (WarRecord war in state.Wars) {
                if (war.Id >= state.NextWarId) { state.NextWarId = war.Id + 1; }
            }
            return state;
        }

        // Something changed that the next world save should include.
        internal static void MarkDirty() {
            dirty = true;
        }

        // Something changed that should reach disk within a few seconds (storage, treasury, rewards).
        internal static void MarkDirtySoon() {
            dirty = true;
            if (soonTimer < 0f) { soonTimer = Mathf.Max(SoonDelaySeconds, SoonMinGapSeconds - (Time.unscaledTime - lastSave)); }
        }

        internal static void Tick(float dt) {
            if (writeFailed) {
                writeFailed = false;
                MarkDirtySoon();
            }
            if (soonTimer < 0f) { return; }
            soonTimer -= dt;
            if (soonTimer <= 0f) {
                soonTimer = -1f;
                Save();
            }
        }

        // Names are whatever players typed: a clan called "Null", "yes" or "123" is written in quotes so it
        // reads back as that text, not as nothing, true or a number.
        private static readonly YamlFormat SaveFormat = YamlFormat.Build(
            s => s.WithNamingConvention(YamlDotNet.Serialization.NamingConventions.PascalCaseNamingConvention.Instance).WithQuotingNecessaryStrings(), null);

        // force: the world is being saved or the server is stopping, so the file is on disk before this returns.
        internal static void Save(bool force = false) {
            if (State == null || string.IsNullOrEmpty(loadedPath)) { return; }
            if (dirty == false && force == false) { return; }
            string yaml;
            try {
                yaml = "# Clans save data. Edit only with the server stopped.\n" + SaveFormat.Serializer.Serialize(State);
            } catch (Exception e) {
                Logger.LogError($"Could not save clans data: {e}");
                return;
            }
            dirty = false;
            soonTimer = -1f;
            lastSave = Time.unscaledTime;
            if (force) {
                lock (writeLock) { pendingText = null; }
                WaitForWriter();
                if (Write(loadedPath, yaml) == false) { dirty = true; }
                return;
            }
            lock (writeLock) {
                pendingText = yaml;
                pendingPath = loadedPath;
                if (writerRunning) { return; }
                writerRunning = true;
            }
            ThreadPool.QueueUserWorkItem(_ => WritePending());
        }

        private static void WritePending() {
            while (true) {
                string text;
                string path;
                lock (writeLock) {
                    if (pendingText == null) {
                        writerRunning = false;
                        return;
                    }
                    text = pendingText;
                    path = pendingPath;
                    pendingText = null;
                }
                if (Write(path, text) == false) { writeFailed = true; }
            }
        }

        private static void WaitForWriter() {
            DateTime until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until) {
                lock (writeLock) {
                    if (writerRunning == false) { return; }
                }
                Thread.Sleep(5);
            }
            Logger.LogWarning("The clans data writer is still busy after ten seconds; writing anyway.");
        }

        // Writes beside the file, then swaps it in and keeps the previous one as .bak.
        private static bool Write(string path, string yaml) {
            try {
                string temp = path + ".tmp";
                File.WriteAllText(temp, yaml);
                if (File.Exists(path)) {
                    string backup = path + ".bak";
                    try {
                        File.Replace(temp, path, backup);
                    } catch (Exception) {
                        // File.Replace is not available everywhere; fall back to copy then move.
                        File.Copy(path, backup, true);
                        File.Delete(path);
                        File.Move(temp, path);
                    }
                } else {
                    File.Move(temp, path);
                }
                Logger.LogDebug($"Saved clans data ({yaml.Length} chars).");
                return true;
            } catch (Exception e) {
                Logger.LogError($"Could not save clans data to {path}: {e}");
                return false;
            }
        }

        internal static void Unload() {
            WaitForWriter();
            State = null;
            loadedKey = null;
            loadedWorld = null;
            loadedPath = null;
            dirty = false;
            soonTimer = -1f;
        }

        internal static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ---------------------------------------------------------------- lifecycle hooks

        [HarmonyPatch(typeof(ZNet), "SaveWorld")]
        private static class SaveWorldPatch {
            private static void Prefix() {
                if (ZNet.instance != null && ZNet.instance.IsServer()) { Save(force: true); }
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
        private static class ShutdownPatch {
            private static void Prefix(bool save) {
                // ZNet.Shutdown saves the world first when asked to; the Clans file follows the same rule so the
                // two never disagree about what happened.
                if (ZNet.instance != null && ZNet.instance.IsServer() && save) { Save(force: true); }
                ClanServer.OnWorldUnload();
                Client.ClanClient.OnWorldUnload();
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.ShutdownWithoutSave))]
        private static class ShutdownWithoutSavePatch {
            private static void Prefix() {
                ClanServer.OnWorldUnload();
                Client.ClanClient.OnWorldUnload();
            }
        }
    }
}
