using BepInEx;
using BepInEx.Logging;
using Clans.Client;
using Clans.Common;
using Clans.Net;
using Clans.Server;
using Clans.Settings;
using Clans.UI;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace Clans
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInDependency(Compat.ModPresence.StarLevelSystemGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Compat.ModPresence.EpicLootGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Compat.ModPresence.ValheimFortressGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(ItemFavoriteFrameworkAPI.ItemFavorites.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInIncompatibility(Compat.ModPresence.GroupsGuid)]
    [BepInIncompatibility(Compat.ModPresence.GuildsGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class Clans : BaseUnityPlugin
    {
        public const string PluginGUID = "MidnightsFX.Clans";
        public const string PluginName = "Clans";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal ValConfig cfg;
        // Set in Awake, not here: a static initialiser can run before BepInEx has registered this plugin, and Jotunn
        // then attributes the whole mod - its RPC names included - to Jotunn itself for the rest of the session.
        public static CustomLocalization Localization;
        public static AssetBundle EmbeddedResourceBundle;
        internal static Harmony HarmonyInstance;

        public void Awake() {
            Localization = LocalizationManager.Instance.GetLocalization();
            Log = this.Logger;
            cfg = new ValConfig(Config);
            ClansConfig.Bind();

            // All startup hooks should go after the config & Logger have been wired up

            // The UI bundle is client-only; a dedicated server never shows any of it.
            ClansAssets.Init();
            LocalizationLoader.AddLocalizations();

            // Console commands. Registered here rather than from a Terminal/Console hook so the command
            // table also exists on a dedicated server, which never builds either. See Common/Terminal.
            TerminalManager.Init();

            // Yaml configs. After ValConfig, which owns cfgFolder and the poll/apply intervals this reads.
            // See Settings/ClansYamlConfigs.cs for the files this mod registers.
            YamlConfigManager.Init();

            Compat.ModPresence.Init();
            ClanNet.Init();
            ClanServer.Init();
            ClanClient.Init();
            // Change listeners for other mods (API/API.cs).
            ApiEvents.Init();
            // Totems and race chests, on every machine: the server places them and clients run them.
            Gameplay.Prefabs.Init();

            HarmonyInstance = new Harmony(PluginGUID);
            PatchAll();

            // Configs are not written until after they are all wired up, they exist in memory before this.
            // Flushing all of the configs at once is a significant speedup in mod load time. Later changes
            // are written in batches rather than one full rewrite per value (see ValConfig.EnableDeferredSave).
            ValConfig.Save();
            ValConfig.EnableDeferredSave();
        }

        // Each patch class on its own. Harmony's PatchAll stops at the first class that fails, so one patch a game
        // update broke would otherwise take every later one (and the rest of Awake) down with it, silently.
        private static void PatchAll() {
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(typeof(Clans).Assembly)) {
                try {
                    HarmonyInstance.CreateClassProcessor(type).Patch();
                } catch (System.Exception e) {
                    Log.LogError($"Could not apply the patch {type.FullName}; the part of Clans it serves will not work: {e.Message}");
                }
            }
        }

        public void OnDestroy() {
            // A config write still waiting on its delay would otherwise be lost when the game closes.
            ValConfig.SavePending();
        }
    }
}
