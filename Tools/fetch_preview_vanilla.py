"""Copies Valheim's own UI sprites and fonts from a local AssetRipper export into ClansUnity, for the UI
previews only.

    python Tools/fetch_preview_vanilla.py <rip>/Assets

The files land in ClansUnity/Assets/Editor/PreviewVanilla, which is gitignored and, being under an Editor
folder, never goes into the asset bundle: the game's art stays out of the repository and the mod. The mod
itself swaps its placeholder sprites and fonts for the game's at runtime (UI/ClansStyle.cs); with this kit
ClansUiPreview does the same, so the rendered previews look like the game.

sprites.json records each sprite's 9-slice border and pixels per unit, which ClansSpriteImporter applies.
"""
import json
import os
import re
import shutil
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "ClansUnity", "Assets", "Editor", "PreviewVanilla")

SPRITES = [
    # Panels and backgrounds.
    "woodpanel_trophys", "woodpanel_settings", "woodpanel_playerinventory", "woodpanel_container", "woodpanel_crafting",
    "woodpanel_512x512", "woodpanel_320x320", "panel_bkg", "panel_bkg_128", "panel_bkg_128_transparent",
    "crafting_panel_bkg", "item_background", "item_background_sunken", "panel_separator", "panel_interior_bkg_128",
    # Widgets.
    "button", "button_highlight", "button_pressed", "button_disabled", "button_glow",
    "button_small", "button_small_highlight", "button_small_pressed", "button_small_disabled",
    "button_tab", "button_tab_hover", "button_tab_selected", "button_tab_disabled",
    "text_field", "text_field_highlight", "text_field_disabled", "checkbox", "checkbox_marker",
    "scrollbar_bkg", "scrollbar_handle", "scroll_bar", "scroll_handle", "UISprite", "Background", "Knob",
    "bar_gradient", "bar_gradient_8", "bar_gradient_16", "selected", "selection_frame", "item_selected",
    "darken_blob", "gradient", "tooltip_bkg", "InputFieldBackground", "dropdown_bkg",
    "skill_bkg", "sunken", "woodpanel_password", "woodpanel_info_180", "woodpanel_crafting_240",
    "BraidLineHorisontalMedium", "BraidLineHorisontalHalfL", "BraidLineHorisontalHalfR",
    "bar_monster_hp_20", "bar_gradient_40", "point3",
]
FONTS = [
    "Norse.otf", "Norsebold.otf",
    "AveriaSansLibre-Regular.ttf", "AveriaSansLibre-Bold.ttf",
    "AveriaSerifLibre-Regular.ttf", "AveriaSerifLibre-Bold.ttf",
]


def field(text, name):
    match = re.search(r"^\s*" + name + r":\s*(.+)$", text, re.M)
    return match.group(1).strip() if match else None


def numbers(value):
    return {k: float(v) for k, v in re.findall(r"(\w+):\s*(-?[\d.]+)", value or "")}


def texture_index(assets):
    """guid -> png path, for cropping sprites out of atlases."""
    index = {}
    folder = os.path.join(assets, "Texture2D")
    for name in os.listdir(folder):
        if not name.endswith(".png.meta"):
            continue
        with open(os.path.join(folder, name), encoding="utf-8", errors="ignore") as f:
            guid = field(f.read(), "guid")
        if guid:
            index[guid] = os.path.join(folder, name[:-5])
    return index


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    assets = sys.argv[1]
    sprite_dir = os.path.join(assets, "Sprite")
    os.makedirs(os.path.join(OUT, "Sprites"), exist_ok=True)
    os.makedirs(os.path.join(OUT, "Fonts"), exist_ok=True)
    textures = None
    meta = {}
    for name in SPRITES:
        path = os.path.join(sprite_dir, name + ".asset")
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8", errors="ignore") as f:
            text = f.read()
        rect = re.search(r"m_Rect:\s*\n\s*serializedVersion: \d+\s*\n\s*x: ([\d.]+)\s*\n\s*y: ([\d.]+)\s*\n\s*width: ([\d.]+)\s*\n\s*height: ([\d.]+)", text)
        border = numbers(field(text, "m_Border"))
        ppu = float(field(text, "m_PixelsToUnits") or 100)
        x, y, w, h = (float(v) for v in rect.groups())
        target = os.path.join(OUT, "Sprites", name + ".png")
        direct = os.path.join(assets, "Texture2D", name + ".png")
        if os.path.exists(direct) and Image.open(direct).size == (int(w), int(h)):
            shutil.copyfile(direct, target)
        else:
            if textures is None:
                textures = texture_index(assets)
            guid = re.search(r"texture: \{fileID: \d+, guid: (\w+)", text)
            atlas = textures.get(guid.group(1)) if guid else None
            if atlas is None:
                print("skip (no texture):", name)
                continue
            image = Image.open(atlas)
            top = image.size[1] - (y + h)
            image.crop((int(x), int(top), int(x + w), int(top + h))).save(target)
        meta[name] = {"border": [border.get("x", 0), border.get("y", 0), border.get("z", 0), border.get("w", 0)], "ppu": ppu}
    with open(os.path.join(OUT, "sprites.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=1, sort_keys=True)
    for font in FONTS:
        source = os.path.join(assets, "Font", font)
        if os.path.exists(source):
            shutil.copyfile(source, os.path.join(OUT, "Fonts", font))
    print("copied %d sprites and fonts to %s" % (len(meta), os.path.normpath(OUT)))


if __name__ == "__main__":
    main()
