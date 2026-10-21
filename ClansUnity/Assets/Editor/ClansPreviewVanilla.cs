using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Editor only: dresses a preview in the game's own sprites and fonts, so a rendered preview looks like the game.
//
// The game's art is never part of this project. Tools/fetch_preview_vanilla.py copies it from a local
// AssetRipper export into Assets/Editor/PreviewVanilla (gitignored, and never bundled because it sits under
// an Editor folder). Without that folder the previews keep the placeholders.
//
// This mirrors what the mod does in game in UI/ClansStyle.cs (sprites swapped by name, fonts by text role, the
// current tab drawn as vanilla's selected tab); keep the font rule in step with ClansStyle.RoleOf. The lit panel
// and HUD materials are not reproduced, so panels look a little brighter here than in game.
public static class ClansPreviewVanilla {
    private const string Root = "Assets/Editor/PreviewVanilla/";

    private static Dictionary<string, Sprite> sprites;
    private static TMP_FontAsset norse;
    private static TMP_FontAsset sans;
    private static TMP_FontAsset serif;
    private static Material norseOutline;
    private static Material sansOutline;
    private static Material serifOutline;

    private static readonly Color TitleColor = new Color(1f, 0.718f, 0.36f, 1f);

    public static bool Available => Directory.Exists(Root + "Sprites");

    // The mod's English strings, so previews show real text (and real widths) instead of $tokens.
    private const string EnglishJson = "../Clans/Localizations/English.json";
    private static Dictionary<string, string> english;

    public static void Apply(GameObject root) {
        if (root == null) { return; }
        Localize(root);
        if (Available == false) { return; }
        Load();
        foreach (Image image in root.GetComponentsInChildren<Image>(true)) {
            if (image.sprite != null && sprites.TryGetValue(image.sprite.name, out Sprite vanilla)) { image.sprite = vanilla; }
        }
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) {
            bool typed = text.GetComponentInParent<TMP_InputField>(true) != null;
            if (typed) { Set(text, serif, serif != null ? serif.material : null); }
            else if (IsTitle(text)) { Set(text, norse, norseOutline); }
            else if ((text.fontStyle & FontStyles.Bold) != 0) { Set(text, serif, serifOutline); }
            else { Set(text, sans, sansOutline); }
        }
        // The current tab is a disabled tab button: vanilla's selected tab sprite and a title-coloured caption.
        foreach (Button button in root.GetComponentsInChildren<Button>(true)) {
            if (button.interactable || !(button.targetGraphic is Image image) || image.sprite == null || image.sprite.name != "button_tab") { continue; }
            if (sprites.TryGetValue("button_tab_selected", out Sprite selected)) { image.sprite = selected; }
            TMP_Text caption = button.GetComponentInChildren<TMP_Text>(true);
            if (caption != null) { caption.color = TitleColor; }
        }
    }

    private static void Set(TMP_Text text, TMP_FontAsset font, Material material) {
        if (font == null) { return; }
        text.font = font;
        text.fontSharedMaterial = material != null ? material : font.material;
    }

    // Must match ClansStyle.RoleOf: titles and headers get Norsebold, bold text Averia Serif, the rest Averia Sans.
    public static bool IsTitle(TMP_Text text) {
        string name = text.name;
        return name == "Title" || name == "ClanName" || name.EndsWith("Header");
    }

    private static void Localize(GameObject root) {
        if (english == null) {
            english = new Dictionary<string, string>();
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), EnglishJson);
            if (File.Exists(path)) {
                // A flat { "key": "value" } file; read it with a regex rather than pull in a JSON library.
                foreach (System.Text.RegularExpressions.Match pair in System.Text.RegularExpressions.Regex.Matches(
                    File.ReadAllText(path), "\"([^\"]+)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"")) {
                    english[pair.Groups[1].Value] = System.Text.RegularExpressions.Regex.Unescape(pair.Groups[2].Value);
                }
            }
        }
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) {
            string value = text.text;
            if (string.IsNullOrEmpty(value) || value[0] != '$') { continue; }
            if (english.TryGetValue(value.Substring(1), out string localized)) { text.text = localized.Replace("{0}", "3").Replace("{1}", "5").Replace("{2}", "Name"); }
        }
    }

    private static void Load() {
        if (sprites != null) { return; }
        sprites = new Dictionary<string, Sprite>();
        foreach (string file in Directory.GetFiles(Root + "Sprites", "*.png")) {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(file.Replace('\\', '/'));
            if (sprite != null) { sprites[Path.GetFileNameWithoutExtension(file)] = sprite; }
        }
        norse = MakeFont("Norsebold.otf");
        sans = MakeFont("AveriaSansLibre-Regular.ttf");
        serif = MakeFont("AveriaSerifLibre-Regular.ttf");
        norseOutline = Outline(norse);
        sansOutline = Outline(sans);
        serifOutline = Outline(serif);
    }

    private static TMP_FontAsset MakeFont(string file) {
        Font font = AssetDatabase.LoadAssetAtPath<Font>(Root + "Fonts/" + file);
        return font == null ? null : TMP_FontAsset.CreateFontAsset(font);
    }

    // The game's "- Outline" font materials: a black outline of 0.175 with the face dilated by the same.
    private static Material Outline(TMP_FontAsset font) {
        if (font == null) { return null; }
        Material material = new Material(font.material);
        material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.175f);
        material.SetFloat(ShaderUtilities.ID_FaceDilate, 0.175f);
        material.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
        return material;
    }
}
