using BepInEx.Bootstrap;
using System;

namespace Clans.Compat {
    // Which related mods are installed. Checked once at startup; every plugin has loaded by the time the
    // game reaches a world, and Chainloader's table is complete once Awake has run for all of them.
    internal static class ModPresence {
        internal const string StarLevelSystemGuid = "MidnightsFX.StarLevelSystem";
        internal const string EpicLootGuid = "randyknapp.mods.epicloot";
        internal const string ValheimFortressGuid = "MidnightsFX.ValheimFortress";
        // Incompatible: BepInEx does not load Clans when either is installed (see ClansPlugin).
        internal const string GroupsGuid = "org.bepinex.plugins.groups";
        internal const string GuildsGuid = "org.bepinex.plugins.guilds";
        internal const string AugaGuid = "randyknapp.mods.auga";
        internal const string ChatterGuid = "redseiko.valheim.chatter";

        [Flags]
        internal enum Capability {
            None = 0,
            StarLevelSystem = 1,
            EpicLoot = 2,
            ValheimFortress = 4,
            // 8 and 16 were Groups and Guilds.
            Auga = 32,
            Chatter = 64,
        }

        private static bool checkedOnce;
        private static Capability local;

        internal static void Init() {
            // Plugins after this one in load order are not in the table yet during Awake; look again lazily.
            checkedOnce = false;
        }

        internal static Capability Local {
            get {
                if (checkedOnce == false) {
                    checkedOnce = true;
                    local = Capability.None;
                    if (Has(StarLevelSystemGuid)) { local |= Capability.StarLevelSystem; }
                    if (Has(EpicLootGuid)) { local |= Capability.EpicLoot; }
                    if (Has(ValheimFortressGuid)) { local |= Capability.ValheimFortress; }
                    if (Has(AugaGuid)) { local |= Capability.Auga; }
                    if (Has(ChatterGuid)) { local |= Capability.Chatter; }
                }
                return local;
            }
        }

        internal static bool Is(Capability capability) => (Local & capability) == capability;

        // What the server reports to clients in Welcome. The World Boss needs all three of SLS, Epic Loot and
        // Valheim Fortress on the server.
        internal static Capability ServerCapabilities() => Local;

        internal const Capability WorldBossNeeds = Capability.StarLevelSystem | Capability.EpicLoot | Capability.ValheimFortress;

        private static bool Has(string guid) => Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(guid);
    }
}
