using Clans.Core;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Clans.Settings {
    // emblems.yaml: what clan emblems can be made of. An emblem is one background and one symbol, each
    // with a colour from the palette.
    public class EmblemSettings {
        public List<EmblemPart> Backgrounds { get; set; } = new List<EmblemPart>();
        public List<EmblemPart> Symbols { get; set; } = new List<EmblemPart>();
        // "#RRGGBB" colours offered in the emblem editor.
        public List<string> Palette { get; set; } = new List<string>();
        // Used for a new clan until its founder picks something, and for any part that no longer exists.
        public EmblemData Default { get; set; } = new EmblemData();

        private static readonly string[] Runes = {
            "fehu", "uruz", "thurisaz", "ansuz", "raidho", "kaunan", "gebo", "wunjo",
            "hagalaz", "naudiz", "isa", "jera", "eihwaz", "perthro", "algiz", "sowilo",
            "tiwaz", "berkanan", "ehwaz", "mannaz", "laguz", "ingwaz", "dagaz", "othala",
        };

        private static readonly string[] BackgroundShapes = {
            "circle", "ring", "shield", "shield_round", "diamond", "square", "hexagon", "banner", "star", "cross",
        };

        private static readonly string[] Trophies = {
            "TrophyDeer", "TrophyBoar", "TrophyNeck", "TrophyGreydwarf", "TrophyForestTroll", "TrophyWolf",
            "TrophyLox", "TrophySeeker", "TrophyEikthyr", "TrophyTheElder", "TrophyBonemass", "TrophyDragonQueen",
            "TrophyGoblinKing", "TrophySeekerQueen", "TrophyFader",
        };

        internal static EmblemSettings BuildDefaults() {
            EmblemSettings settings = new EmblemSettings();
            foreach (string shape in BackgroundShapes) {
                settings.Backgrounds.Add(new EmblemPart { Id = "bg_" + shape, Sprite = "bundle:bg_" + shape });
            }
            foreach (string rune in Runes) {
                settings.Symbols.Add(new EmblemPart { Id = "rune_" + rune, Sprite = "bundle:rune_" + rune });
            }
            // Item icons are already coloured, so they are drawn as they are rather than tinted.
            foreach (string trophy in Trophies) {
                settings.Symbols.Add(new EmblemPart { Id = trophy.ToLowerInvariant(), Sprite = "item:" + trophy, Tint = false });
            }
            settings.Palette = new List<string> {
                "#F2E6CF", "#C9B38A", "#8C6B3F", "#4A3220", "#1E1A17", "#5E5E5E", "#A8A8A8", "#FFFFFF",
                "#8E1B1B", "#C4462B", "#D9892B", "#E8C547", "#4F7A28", "#2E5C45", "#2B6C8F", "#24396B",
                "#5B3A7A", "#9C3D6E",
            };
            settings.Default = new EmblemData { BackId = "bg_shield", BackColor = "#4A3220", FrontId = "rune_algiz", FrontColor = "#E8C547" };
            return settings;
        }

        internal static ValidationReport Validate(EmblemSettings next, EmblemSettings previous) {
            ValidationReport report = new ValidationReport();
            if (next.Backgrounds == null || next.Backgrounds.Count == 0) { return report.Error("Backgrounds is empty"); }
            if (next.Symbols == null || next.Symbols.Count == 0) { return report.Error("Symbols is empty"); }
            if (next.Palette == null || next.Palette.Count == 0) { return report.Error("Palette is empty"); }
            foreach (string colour in next.Palette) {
                if (EmblemColors.TryParse(colour, out _) == false) {
                    report.Warn($"palette colour '{colour}' is not #RRGGBB; it is skipped.");
                }
            }
            CheckIds(report, "Backgrounds", next.Backgrounds);
            CheckIds(report, "Symbols", next.Symbols);
            if (next.Default == null) { next.Default = BuildDefaults().Default; }
            if (next.Backgrounds.Any(p => p.Id == next.Default.BackId) == false) { next.Default.BackId = next.Backgrounds[0].Id; }
            if (next.Symbols.Any(p => p.Id == next.Default.FrontId) == false) { next.Default.FrontId = next.Symbols[0].Id; }
            return report;
        }

        private static void CheckIds(ValidationReport report, string list, List<EmblemPart> parts) {
            HashSet<string> seen = new HashSet<string>();
            foreach (EmblemPart part in parts) {
                if (string.IsNullOrEmpty(part.Id)) {
                    report.Warn($"{list} has an entry with no Id; it is skipped.");
                    continue;
                }
                if (seen.Add(part.Id) == false) {
                    report.Warn($"{list} has the Id '{part.Id}' more than once; only the first is used.");
                }
            }
        }

        internal EmblemPart FindBackground(string id) => Backgrounds.FirstOrDefault(p => p.Id == id);
        internal EmblemPart FindSymbol(string id) => Symbols.FirstOrDefault(p => p.Id == id);

        // Replaces any part or colour the catalog no longer has with the default's, so an admin removing an
        // entry never leaves a clan with an emblem that cannot be drawn.
        internal EmblemData Sanitize(EmblemData emblem) {
            EmblemData result = emblem == null ? Default.Clone() : emblem.Clone();
            if (FindBackground(result.BackId) == null) { result.BackId = Default.BackId; }
            if (FindSymbol(result.FrontId) == null) { result.FrontId = Default.FrontId; }
            if (EmblemColors.TryParse(result.BackColor, out _) == false) { result.BackColor = Default.BackColor; }
            if (EmblemColors.TryParse(result.FrontColor, out _) == false) { result.FrontColor = Default.FrontColor; }
            return result;
        }
    }

    public class EmblemPart {
        public string Id { get; set; } = "";
        // Where the image comes from:
        //   bundle:<sprite>   a white sprite shipped in this mod's asset bundle
        //   vanilla:<sprite>  a sprite already in the game, by name
        //   item:<prefab>     an item's inventory icon
        public string Sprite { get; set; } = "";
        // Tint the image with the chosen colour. Turn off for images that already have their own colours.
        [DefaultValue(true)]
        public bool Tint { get; set; } = true;
    }

    internal static class EmblemColors {
        internal static bool TryParse(string hex, out UnityEngine.Color color) {
            color = UnityEngine.Color.white;
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') { return false; }
            return UnityEngine.ColorUtility.TryParseHtmlString(hex, out color);
        }
    }
}
