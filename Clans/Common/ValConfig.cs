using BepInEx;
using BepInEx.Configuration;
using Clans.Common;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#pragma warning disable IDE0130
namespace Clans {
#pragma warning restore IDE0130
    internal class ValConfig {
        public static ConfigFile cfg;
        
        // Add Client sided config entries under here
        public static ConfigEntry<bool> EnableDebugMode;
        public static ConfigEntry<bool> EnableTerminalColors;

        // Add Server synced config entries under here
        public static ConfigEntry<int> InMemoryModificationsPerTick;
        public static ConfigEntry<float> ConfigApplyDelay;
        public static ConfigEntry<float> ConfigPollIntervalSeconds;

        // Derived from the plugin rather than written out, because scripts/RenameSolution.ps1 only
        // rewrites the .sln, .cs, .csproj and AssemblyInfo.cs -- it never touches Common/. A hardcoded
        // literal here means every mod copied from this template ships with its config folder still
        // called "JotunnModStub". PluginName is rewritten, so deriving it fixes that for free.
        public static readonly string cfgFolder = Clans.PluginName;

        // How long a burst of setting changes has to go quiet before the config file is written.
        private const float SaveDelaySeconds = 1f;
        private static bool savePending = false;

        public ValConfig(ConfigFile cf) {
            // ensure all the config values are created
            cfg = cf;
            // Off for the whole session. Binding writes nothing; the plugin writes once when Awake finishes,
            // then EnableDeferredSave batches every later change.
            cfg.SaveOnConfigSet = false;
            CreateConfigValues(cf);
            Logger.SetDebugLogging(EnableDebugMode.Value);

            // Watch #1. Registering before ConfigFileWatcher.Initialize() only touches a dictionary, so
            // ordering against YamlConfigManager.Init() does not matter.
            ConfigFileWatcher.Register(cfg.ConfigFilePath, OnMainConfigFileChanged);
        }

        // A client must not reload: Jotunn has already replaced its in-memory values with the server's,
        // and a reload would clobber them with whatever this machine happens to have on disk.
        //
        // Not routed through ConfigChangeDebouncer -- Reload raises SettingChanged per entry, and those
        // are already debounced individually by whichever handler is attached to them.
        private static void OnMainConfigFileChanged(string _) {
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) { return; }
            Logger.LogInfo("Configuration file has been changed, reloading settings.");
            cfg.Reload();
        }

        // Called once plugin setup has finished binding.
        //
        // With SaveOnConfigSet on, BepInEx rewrites the whole file synchronously for each value that changes.
        // A mod with many items has a config of several hundred KB, and a server config sync, or an admin
        // closing Configuration Manager after a few edits, changes dozens of entries in one frame. That was
        // dozens of full rewrites in that frame, on the server and on every client, which is enough to stall
        // past the network timeout. Changes now mark the file dirty and it is written once, after they settle.
        internal static void EnableDeferredSave() {
            cfg.SaveOnConfigSet = false;
            cfg.SettingChanged += (_, _) => {
                savePending = true;
                ConfigChangeDebouncer.Schedule(cfg, SavePending, SaveDelaySeconds);
            };
        }

        // Writes the file if anything changed since the last write. The plugin also calls this on shutdown,
        // since a write still waiting on its delay would otherwise be lost when the game closes.
        internal static void SavePending() {
            if (savePending == false) { return; }
            savePending = false;
            try {
                Save();
            } catch (IOException e) {
                // Most likely another program has the file open. Leave it pending for the next change or shutdown.
                savePending = true;
                Logger.LogWarning($"Could not write the config file, will retry: {e.Message}");
            }
        }

        // Writes the file and re-stamps the watcher, so our own write is not taken for an external edit and
        // reloaded on the next poll.
        internal static void Save() {
            cfg.Save();
            ConfigFileWatcher.RefreshStamp(cfg.ConfigFilePath);
        }

        private void CreateConfigValues(ConfigFile Config) {
            // Debugmode
            EnableDebugMode = Config.Bind("Client config", "EnableDebugMode", false,
                new ConfigDescription("Enables Debug logging.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            EnableDebugMode.SettingChanged += Logger.EnableDebugLogging;

            // Read by TerminalOutput. Only affects what the console shows; the BepInEx log is always
            // written as plain text.
            EnableTerminalColors = Config.Bind("Client config", "EnableTerminalColors", true,
                new ConfigDescription("Colours this mod's console command output by severity.",
                null,
                new ConfigurationManagerAttributes { }));

            // Instantiate server synced config entries here
            InMemoryModificationsPerTick = BindServerConfig("Config", "Updates Per Tick", 20, "Number of updates per tick that are applied when modifying items or pieces.", true, 1, 150);
            ConfigApplyDelay = BindServerConfig("Config", "Config Apply Delay", 1f, "Delay in seconds before a changed config entry is applied in-game. Coalesces a burst of rapid edits (typing, file reloads, server sync) into a single apply. Set to 0 to apply instantly.", true, 0f, 10f);

            // Read by Common/Config/ConfigFileWatcher every frame, so it is bound rather than hardcoded:
            // an admin editing yaml wants a short interval, a busy server wants a long one.
            ConfigPollIntervalSeconds = BindServerConfig("Config", "Config Poll Interval", 30f, "Seconds between checks for edits to this mod's yaml config files and its BepInEx config file. Lower reacts faster to a hand edit, higher does less disk work.", true, 1f, 300f);
        }

        // Every overload below marks the entry IsAdminOnly, which is what makes Jotunn's
        // SynchronizationManager push the server's value to every client. Use cfg.Bind directly for
        // anything that is genuinely per-machine (UI, logging, client-side toggles).
        //
        // The numeric overloads come in two shapes: pass a min and max for the common bounded case, or
        // pass an AcceptableValueBase (null for unbounded) when a range is wrong for the setting.

        /// <summary>
        /// Binds a server-synced bool.
        /// </summary>
        /// <param name="category">Config file section.</param>
        /// <param name="key">Entry name within the section.</param>
        /// <param name="value">Default value.</param>
        /// <param name="description">Shown in the config file and the Configuration Manager.</param>
        /// <param name="acceptableValues">Optional constraint. Normally null for a bool.</param>
        /// <param name="advanced">Hides the entry behind the Advanced toggle.</param>
        public static ConfigEntry<bool> BindServerConfig(string category, string key, bool value, string description, AcceptableValueBase acceptableValues = null, bool advanced = false) {
            return cfg.Bind(category, key, value,
                new ConfigDescription(description,
                    acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Binds a server-synced int constrained to a range.
        /// </summary>
        /// <param name="category">Config file section.</param>
        /// <param name="key">Entry name within the section.</param>
        /// <param name="value">Default value.</param>
        /// <param name="description">Shown in the config file and the Configuration Manager.</param>
        /// <param name="advanced">Hides the entry behind the Advanced toggle.</param>
        /// <param name="valMin">Lowest accepted value.</param>
        /// <param name="valMax">Highest accepted value.</param>
        public static ConfigEntry<int> BindServerConfig(string category, string key, int value, string description, bool advanced = false, int valMin = 0, int valMax = 150) {
            return cfg.Bind(category, key, value,
                new ConfigDescription(description,
                new AcceptableValueRange<int>(valMin, valMax),
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Binds a server-synced int with an explicit constraint. Pass null for an unbounded value.
        /// </summary>
        /// <param name="category">Config file section.</param>
        /// <param name="key">Entry name within the section.</param>
        /// <param name="value">Default value.</param>
        /// <param name="description">Shown in the config file and the Configuration Manager.</param>
        /// <param name="acceptableValues">Constraint to apply, or null for no constraint.</param>
        /// <param name="advanced">Hides the entry behind the Advanced toggle.</param>
        public static ConfigEntry<int> BindServerConfig(string category, string key, int value, string description, AcceptableValueBase acceptableValues, bool advanced = false) {
            return cfg.Bind(category, key, value,
                new ConfigDescription(description,
                acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Binds a server-synced float constrained to a range.
        /// </summary>
        /// <param name="category">Config file section.</param>
        /// <param name="key">Entry name within the section.</param>
        /// <param name="value">Default value.</param>
        /// <param name="description">Shown in the config file and the Configuration Manager.</param>
        /// <param name="advanced">Hides the entry behind the Advanced toggle.</param>
        /// <param name="valMin">Lowest accepted value.</param>
        /// <param name="valMax">Highest accepted value.</param>
        public static ConfigEntry<float> BindServerConfig(string category, string key, float value, string description, bool advanced = false, float valMin = 0, float valMax = 150) {
            return cfg.Bind(category, key, value,
                new ConfigDescription(description,
                new AcceptableValueRange<float>(valMin, valMax),
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Binds a server-synced float with an explicit constraint. Pass null for an unbounded value.
        /// </summary>
        /// <param name="category">Config file section.</param>
        /// <param name="key">Entry name within the section.</param>
        /// <param name="value">Default value.</param>
        /// <param name="description">Shown in the config file and the Configuration Manager.</param>
        /// <param name="acceptableValues">Constraint to apply, or null for no constraint.</param>
        /// <param name="advanced">Hides the entry behind the Advanced toggle.</param>
        public static ConfigEntry<float> BindServerConfig(string category, string key, float value, string description, AcceptableValueBase acceptableValues, bool advanced = false) {
            return cfg.Bind(category, key, value,
                new ConfigDescription(description,
                acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Binds a server-synced string, optionally restricted to a fixed list of values.
        /// </summary>
        /// <param name="category">Config file section.</param>
        /// <param name="key">Entry name within the section.</param>
        /// <param name="value">Default value.</param>
        /// <param name="description">Shown in the config file and the Configuration Manager.</param>
        /// <param name="acceptableValues">Allowed values, or null to accept anything.</param>
        /// <param name="advanced">Hides the entry behind the Advanced toggle.</param>
        public static ConfigEntry<string> BindServerConfig(string category, string key, string value, string description, AcceptableValueList<string> acceptableValues = null, bool advanced = false) {
            return cfg.Bind(category, key, value,
                new ConfigDescription(
                    description,
                    acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }
    }
}
