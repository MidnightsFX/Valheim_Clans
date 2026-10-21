using Jotunn.Entities;
using Jotunn.Managers;
using Clans;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Clans.Common {
    internal static class LocalizationLoader {

        internal static string LocalizationFolder = "Localizations";

        // This loads all localizations within the localization directory.
        // Localizations should be plain JSON objects with each of the two required entries being separate eg:
        // "item_sword": "sword-name-here",
        // "item_sword_description": "sword-description-here",
        // the localization file itself should be a case matched language as defined by one of the "folder" language names from here:
        // https://valheim-modding.github.io/Jotunn/data/localization/language-list.html
        internal static void AddLocalizations() {
            CustomLocalization Localization = Clans.Localization;

            // Ensure localization folder exists
            var translationFolder = Path.Combine(BepInEx.Paths.ConfigPath, ValConfig.cfgFolder, LocalizationFolder);
            Directory.CreateDirectory(translationFolder);
            foreach (string embeddedResource in Assembly.GetExecutingAssembly().GetManifestResourceNames()) {
                if (!embeddedResource.Contains(LocalizationFolder)) { continue; }
                // Read the localization file

                string localization = ReadEmbeddedResourceFile(embeddedResource);
                // since I use comments in the localization that are not valid JSON those need to be stripped
                string cleaned_localization = Regex.Replace(localization, @"\/\/.*", "");
                Dictionary<string, string> internal_localization = SimpleJson.SimpleJson.DeserializeObject<Dictionary<string, string>>(cleaned_localization);
                // Just the localization name
                var localization_name = embeddedResource.Split('.');
                string defaults_file = $"{translationFolder}/{localization_name[2]}.defaults.json";
                if (File.Exists($"{translationFolder}/{localization_name[2]}.json")) {
                    string cached_translation_file = File.ReadAllText($"{translationFolder}/{localization_name[2]}.json");
                    try {
                        Dictionary<string, string> cached_localization = SimpleJson.SimpleJson.DeserializeObject<Dictionary<string, string>>(cached_translation_file);
                        UpdateLocalizationWithMissingKeys(internal_localization, cached_localization, ReadDefaults(defaults_file));
                        Logger.LogDebug($"Reading {translationFolder}/{localization_name[2]}.json");
                        string updated_local_translation = SimpleJson.SimpleJson.SerializeObject(cached_localization);
                        File.WriteAllText($"{translationFolder}/{localization_name[2]}.json", updated_local_translation);
                        Localization.AddJsonFile(localization_name[2], updated_local_translation);
                    } catch (Exception ex) {
                        Logger.LogWarning($"The translation file {translationFolder}/{localization_name[2]}.json could not be read ({ex.Message}); it is replaced with the built-in one.");
                        File.WriteAllText($"{translationFolder}/{localization_name[2]}.json", cleaned_localization);
                        Localization.AddJsonFile(localization_name[2], cleaned_localization);
                    }
                } else {
                    File.WriteAllText($"{translationFolder}/{localization_name[2]}.json", cleaned_localization);
                    Logger.LogDebug($"Reading {embeddedResource}");
                    Localization.AddJsonFile(localization_name[2], cleaned_localization);
                }
                // The texts the player's file was last brought up to date from, so the next version can tell
                // which of them the player changed.
                File.WriteAllText(defaults_file, SimpleJson.SimpleJson.SerializeObject(internal_localization));
                Logger.LogDebug($"Added localization: '{localization_name[2]}'");
            }
        }

        // The player's file keeps the translations they made, takes any key added since, and drops any
        // key the mod no longer has. A text the player never changed follows the mod's when the mod rewords it;
        // without the defaults file (one written before it existed) that cannot be told, so it is kept. A kept
        // text whose {0}-style placeholders differ from the mod's is replaced all the same: the mod fills in its
        // own, and string.Format would fail and show the text raw.
        private static Dictionary<string, string> UpdateLocalizationWithMissingKeys(Dictionary<string, string> internal_localization, Dictionary<string, string> cached_localization, Dictionary<string, string> previous_defaults) {
            List<string> extra_keys = new List<string>(cached_localization.Keys);
            foreach (KeyValuePair<string, string> entry in internal_localization) {
                extra_keys.Remove(entry.Key);
                if (!cached_localization.TryGetValue(entry.Key, out string cached)) {
                    Logger.LogDebug($"Adding missing localization key {entry.Key}");
                    cached_localization.Add(entry.Key, entry.Value);
                } else if (cached != entry.Value) {
                    bool unchanged = previous_defaults != null && previous_defaults.TryGetValue(entry.Key, out string previous) && previous == cached;
                    if (unchanged || Placeholders(cached) != Placeholders(entry.Value)) {
                        Logger.LogDebug($"Updating localization key {entry.Key}");
                        cached_localization[entry.Key] = entry.Value;
                    }
                }
            }
            if (extra_keys.Count > 0) {
                Logger.LogDebug($"Removing extra keys {string.Join(",", extra_keys)}.");
                foreach (string key in extra_keys) {
                    cached_localization.Remove(key);
                }
            }
            return cached_localization;
        }

        private static Dictionary<string, string> ReadDefaults(string path) {
            if (!File.Exists(path)) { return null; }
            try {
                return SimpleJson.SimpleJson.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
            } catch (Exception ex) {
                Logger.LogDebug($"The defaults file {path} could not be read ({ex.Message}).");
                return null;
            }
        }

        // "{0} paid {1:N0}" -> "0,1".
        private static string Placeholders(string text) {
            return string.Join(",", Regex.Matches(text, @"\{(\d+)[^{}]*\}").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(i => i));
        }

        // This reads an embedded file resource name, these are all resources packed into the DLL
        // they all have a format following this:
        // ValheimArmory.localizations.English.json
        private static string ReadEmbeddedResourceFile(string filename) {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(filename);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
