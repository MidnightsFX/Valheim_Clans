namespace Clans.Gameplay {
    // Names shared by the server (which places these objects) and the clients (which register and run them).
    internal static class ClansPrefabs {
        internal const string RaceStart = "Clans_Totem_RaceStart";
        internal const string RaceEnd = "Clans_Totem_RaceEnd";
        internal const string WorldBoss = "Clans_Totem_WorldBoss";

        internal const int RaceTiers = 3;
        internal static string RaceChest(int tier) => "Clans_RaceChest_" + tier;
        internal const string RaceChestPrefix = "Clans_RaceChest_";

        internal enum TotemKind { RaceStart = 0, RaceEnd = 1, WorldBoss = 2 }

        // The clan banners (ClanBanner), one per colour: each a copy of the vanilla banner of that colour, keeping its
        // dyes, whose cloth becomes blank fabric of the same colour. FabricBottomV is where the plain fabric inside the
        // vanilla banner's frame starts in its texture (BannerArt): the yellow banner's is stained further down.
        // The white one keeps the name the first clan banner had.
        internal sealed class BannerVariant {
            internal readonly string Prefab;
            internal readonly string Base;
            internal readonly string Colour;
            internal readonly float FabricBottomV;

            internal BannerVariant(string prefab, string vanilla, string colour, float fabricBottomV) {
                Prefab = prefab;
                Base = vanilla;
                Colour = colour;
                FabricBottomV = fabricBottomV;
            }

            internal string Name => "$clans_banner_" + Colour;
        }

        internal static readonly BannerVariant[] Banners = {
            new BannerVariant("Clans_Banner", "piece_banner11", "white", 0.31f),
            new BannerVariant("Clans_Banner_Black", "piece_banner01", "black", 0.31f),
            new BannerVariant("Clans_Banner_Blue", "piece_banner02", "blue", 0.31f),
            new BannerVariant("Clans_Banner_Red", "piece_banner04", "red", 0.31f),
            new BannerVariant("Clans_Banner_Green", "piece_banner05", "green", 0.31f),
            new BannerVariant("Clans_Banner_Yellow", "piece_banner08", "yellow", 0.46f),
            new BannerVariant("Clans_Banner_Orange", "piece_banner10", "orange", 0.31f),
        };

        internal static BannerVariant BannerOf(string prefab) {
            foreach (BannerVariant variant in Banners) {
                if (variant.Prefab == prefab) { return variant; }
            }
            return null;
        }

        // The guild feasts (GuildFeast), one per vanilla feast: a copy of it under a name of its own, with stronger
        // food that lasts a set time, that a clan member sets out for anyone to eat. Biome is the id feasts.yaml
        // uses. A feast the game does not have (an older game without the Deep North) is simply not offered.
        internal sealed class GuildFeastVariant {
            internal readonly string Biome;
            internal readonly string Base;
            internal readonly string Prefab;

            internal GuildFeastVariant(string biome, string vanilla) {
                Biome = biome;
                Base = vanilla;
                Prefab = "Clans_GuildFeast_" + biome;
            }

            internal string Name => "$clans_feast_" + Biome.ToLowerInvariant();
        }

        internal static readonly GuildFeastVariant[] GuildFeasts = {
            new GuildFeastVariant("Meadows", "FeastMeadows"),
            new GuildFeastVariant("BlackForest", "FeastBlackforest"),
            new GuildFeastVariant("Swamp", "FeastSwamps"),
            new GuildFeastVariant("Ocean", "FeastOceans"),
            new GuildFeastVariant("Mountain", "FeastMountains"),
            new GuildFeastVariant("Plains", "FeastPlains"),
            new GuildFeastVariant("Mistlands", "FeastMistlands"),
            new GuildFeastVariant("Ashlands", "FeastAshlands"),
            new GuildFeastVariant("DeepNorth", "FeastDeepNorth"),
        };

        internal static GuildFeastVariant GuildFeastOf(string biome) {
            foreach (GuildFeastVariant variant in GuildFeasts) {
                if (string.Equals(variant.Biome, biome, System.StringComparison.OrdinalIgnoreCase)) { return variant; }
            }
            return null;
        }

        // An invisible marker the server puts where a totem should go (TotemPlacer). It carries the same stamp as
        // the totem it will become, its kind being the totem's.
        internal const string Placer = "Clans_TotemPlacer";

        // How far from its placer a totem may stand: the placer looks this far for open ground, then twice and four
        // times as far.
        internal const float PlacerRadius = 30f;
        internal static readonly float[] PlacerRounds = { 1f, 2f, 4f };
        internal static float PlacerReach => PlacerRadius * PlacerRounds[PlacerRounds.Length - 1];

        internal static string TotemPrefab(TotemKind kind) {
            switch (kind) {
                case TotemKind.RaceStart: return RaceStart;
                case TotemKind.RaceEnd: return RaceEnd;
                default: return WorldBoss;
            }
        }

        // The open ground a totem needs: a box this wide (radius) and tall, nothing solid in it. The destination's
        // chest has a totem beside it (Prefabs.AddDestinationTotem), up to 2 m off its centre.
        internal static void Footprint(TotemKind kind, out float radius, out float height) {
            radius = kind == TotemKind.RaceEnd ? 3f : 2.5f;
            height = 4f;
        }

        // Totem ZDO keys.
        internal const string ActivityKey = "clans_activity";
        internal const string KindKey = "clans_kind";
        // The clan a totem or a clan banner belongs to, or a tombstone's owner was in at death (TombstoneLock).
        internal const string ClanKey = "clans_clan";
        internal const string SnappedKey = "clans_snapped";
        // On a placer: the biome the server chose its spot for, which the placer looks for open ground in first.
        internal const string BiomeKey = "clans_biome";
        internal const string FortressStartedKey = "clans_vf_started";
        internal const string FortressHandleKey = "clans_vf_handle";
        // Where a World Boss fight's waves come from, set by the totem's owner when it starts: "x,y,z;x,y,z;...".
        internal const string PortalsKey = "clans_portals";

        // Race chest item custom data.
        internal const string RaceIdData = "clans_race";
        internal const string RaceSerialData = "clans_serial";
    }
}
