using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Import settings for everything under Assets/Clans/Bundle/Sprites, so a dropped-in PNG is ready to use:
// a single UI sprite, no mipmaps, clamped edges.
//
// The game's own sprites copied in for previews (Assets/Editor/PreviewVanilla, see Tools/fetch_preview_vanilla.py)
// get the 9-slice border and pixels per unit the game gives them, from that folder's sprites.json.
//
// Emblem and icon images are white on transparent and get tinted in game. Placeholder images (named like
// the vanilla UI sprites they stand in for) are 9-sliced so panels and buttons stretch; the game swaps them
// for the real vanilla sprites at runtime, so their look here only matters in the editor.
public class ClansSpriteImporter : AssetPostprocessor {
    private const string Root = "Assets/Clans/Bundle/Sprites/";
    private const string Placeholders = Root + "Placeholders/";
    private const int PlaceholderBorder = 16;
    private const string PreviewVanilla = "Assets/Editor/PreviewVanilla/";

    private void OnPreprocessTexture() {
        if (assetPath.StartsWith(PreviewVanilla + "Sprites/")) {
            ImportPreviewVanilla((TextureImporter)assetImporter);
            return;
        }
        if (assetPath.StartsWith(Root) == false) { return; }
        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spritePixelsPerUnit = 100;
        if (assetPath.StartsWith(Placeholders)) {
            importer.spriteBorder = new Vector4(PlaceholderBorder, PlaceholderBorder, PlaceholderBorder, PlaceholderBorder);
        }
    }

    private void ImportPreviewVanilla(TextureImporter importer) {
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        string json = Path.Combine(Path.GetDirectoryName(Application.dataPath), PreviewVanilla + "sprites.json");
        if (File.Exists(json) == false) { return; }
        string name = Path.GetFileNameWithoutExtension(assetPath);
        // { "name": { "border": [l, b, r, t], "ppu": 50.0 }, ... }
        Match entry = Regex.Match(File.ReadAllText(json),
            "\"" + Regex.Escape(name) + "\":\\s*\\{\\s*\"border\":\\s*\\[([^\\]]*)\\],\\s*\"ppu\":\\s*([\\d.]+)");
        if (entry.Success == false) { return; }
        string[] border = entry.Groups[1].Value.Split(',');
        if (border.Length == 4) {
            importer.spriteBorder = new Vector4(Parse(border[0]), Parse(border[1]), Parse(border[2]), Parse(border[3]));
        }
        importer.spritePixelsPerUnit = Parse(entry.Groups[2].Value);
    }

    private static float Parse(string value) => float.Parse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture);
}
