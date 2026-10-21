using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Clans.UI {
    // Valheim's TextMeshPro fonts, for the prefabs' placeholder font to be swapped to at runtime.
    //
    // Ported from Epic Loot's MagicFontManager. A font is paired with one of its own materials, checked
    // against the font's atlas: other mods ship copies of these fonts and materials under the same names,
    // and a font drawn with another font's material renders as garbage.
    internal static class VanillaFonts {
        internal enum Role {
            // Titles and section headers.
            NorseBoldOutline,
            // Names, labels and button captions (bold).
            AveriaSerifLibreOutline,
            // Text typed into fields.
            AveriaSerifLibre,
            // Body text and numbers.
            AveriaSansLibreOutline,
        }

        private sealed class FontData {
            internal TMP_FontAsset Font;
            internal Material Material;
        }

        private static readonly Dictionary<Role, (string Font, string Material)> names = new Dictionary<Role, (string, string)> {
            { Role.NorseBoldOutline, ("Valheim-Norsebold", "Valheim-Norsebold - Outline") },
            { Role.AveriaSerifLibreOutline, ("Valheim-AveriaSerifLibre", "Valheim-AveriaSerifLibre - Outline") },
            { Role.AveriaSerifLibre, ("Valheim-AveriaSerifLibre", "Valheim-AveriaSerifLibre") },
            { Role.AveriaSansLibreOutline, ("Valheim-AveriaSansLibre", "Valheim-AveriaSansLibre - Outline") },
        };

        private static readonly Dictionary<Role, FontData> cache = new Dictionary<Role, FontData>();

        internal static bool Apply(TMP_Text text, Role role) {
            if (text == null) { return false; }
            FontData data = Get(role);
            if (data?.Font == null || data.Font.material == null) { return false; }
            text.font = data.Font;
            text.fontSharedMaterial = IsOnAtlas(data.Font, data.Material) ? data.Material : data.Font.material;
            return true;
        }

        private static FontData Get(Role role) {
            if (cache.TryGetValue(role, out FontData data) && data.Font != null) { return data; }
            (string fontName, string materialName) = names[role];
            List<TMP_FontAsset> fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(f => f.name == fontName && f.material != null).ToList();
            List<Material> materials = Resources.FindObjectsOfTypeAll<Material>().Where(m => m.name == materialName).ToList();
            data = new FontData();
            foreach (TMP_FontAsset font in fonts) {
                Material onAtlas = materials.FirstOrDefault(m => IsOnAtlas(font, m));
                if (onAtlas != null) {
                    data.Font = font;
                    data.Material = onAtlas;
                    break;
                }
            }
            if (data.Font == null) {
                data.Font = fonts.FirstOrDefault();
                data.Material = data.Font?.material;
            }
            // Only a hit is cached: a miss can mean the font is not loaded yet.
            if (data.Font != null) { cache[role] = data; }
            return data;
        }

        private static bool IsOnAtlas(TMP_FontAsset font, Material material) {
            if (font == null || material == null || font.atlasTextures == null || font.atlasTextures.Length == 0) { return false; }
            ShaderUtilities.GetShaderPropertyIDs();
            if (material.HasProperty(ShaderUtilities.ID_MainTex) == false) { return false; }
            Texture materialAtlas = material.GetTexture(ShaderUtilities.ID_MainTex);
            Texture fontAtlas = font.atlasTexture;
            return materialAtlas != null && fontAtlas != null && materialAtlas.GetInstanceID() == fontAtlas.GetInstanceID();
        }
    }
}
