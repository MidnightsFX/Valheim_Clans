"""Generates the starter emblem and icon sprites for the Clans asset bundle.

White shapes on a transparent background, so the game can tint them with any colour. Writes into
Assets/Clans/Bundle/Sprites/, which ClansSpriteImporter imports as sprites and the bundle build packs.

    python ClansUnity/Tools/generate_emblem_sprites.py

Re-running overwrites the generated files; hand-made replacements should use new names instead.
"""
import math
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SPRITES = os.path.normpath(os.path.join(HERE, "..", "Assets", "Clans", "Bundle", "Sprites"))
EMBLEMS = os.path.join(SPRITES, "Emblems")
ICONS = os.path.join(SPRITES, "Icons")

SIZE = 256
SUPER = 4  # drawn this many times larger, then scaled down for smooth edges
WHITE = (255, 255, 255, 255)


def canvas():
    img = Image.new("RGBA", (SIZE * SUPER, SIZE * SUPER), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def px(points):
    s = SIZE * SUPER
    return [(x * s, y * s) for x, y in points]


def save(img, folder, name, size=SIZE):
    os.makedirs(folder, exist_ok=True)
    img.resize((size, size), Image.LANCZOS).save(os.path.join(folder, name + ".png"))


def stroke(draw, points, width):
    """A polyline with round joints and caps."""
    pts = px(points)
    w = int(width * SIZE * SUPER)
    draw.line(pts, fill=WHITE, width=w, joint="curve")
    r = w / 2
    for x, y in (pts[0], pts[-1]):
        draw.ellipse((x - r, y - r, x + r, y + r), fill=WHITE)


def curve(p0, p1, p2, steps=24):
    """Points along a quadratic Bezier."""
    out = []
    for i in range(steps + 1):
        t = i / steps
        x = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t ** 2 * p2[0]
        y = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t ** 2 * p2[1]
        out.append((x, y))
    return out


def regular_polygon(sides, radius, rotation=0.0, cx=0.5, cy=0.5):
    return [(cx + radius * math.cos(rotation + 2 * math.pi * i / sides),
             cy + radius * math.sin(rotation + 2 * math.pi * i / sides)) for i in range(sides)]


# ---------------------------------------------------------------- backgrounds

def heater_shield(margin=0.08):
    top = margin
    left, right = margin + 0.04, 1 - margin - 0.04
    side = 0.45
    tip = (0.5, 1 - margin)
    pts = [(left, top), (right, top), (right, side)]
    pts += curve((right, side), (right, 0.78), tip)[1:]
    pts += curve(tip, (left, 0.78), (left, side))[1:]
    return pts


def backgrounds():
    shapes = {}

    img, d = canvas()
    d.ellipse(px([(0.06, 0.06), (0.94, 0.94)]), fill=WHITE)
    shapes["bg_circle"] = img

    img, d = canvas()
    d.ellipse(px([(0.06, 0.06), (0.94, 0.94)]), fill=WHITE)
    d.ellipse(px([(0.2, 0.2), (0.8, 0.8)]), fill=(0, 0, 0, 0))
    shapes["bg_ring"] = img

    img, d = canvas()
    d.polygon(px(heater_shield()), fill=WHITE)
    shapes["bg_shield"] = img

    # A round shield: rim, planks left open, and a central boss.
    img, d = canvas()
    d.ellipse(px([(0.05, 0.05), (0.95, 0.95)]), fill=WHITE)
    d.ellipse(px([(0.14, 0.14), (0.86, 0.86)]), fill=(0, 0, 0, 0))
    d.ellipse(px([(0.2, 0.2), (0.8, 0.8)]), fill=WHITE)
    d.ellipse(px([(0.36, 0.36), (0.64, 0.64)]), fill=(0, 0, 0, 0))
    d.ellipse(px([(0.41, 0.41), (0.59, 0.59)]), fill=WHITE)
    shapes["bg_shield_round"] = img

    img, d = canvas()
    d.polygon(px([(0.5, 0.04), (0.96, 0.5), (0.5, 0.96), (0.04, 0.5)]), fill=WHITE)
    shapes["bg_diamond"] = img

    img, d = canvas()
    d.rounded_rectangle(px([(0.08, 0.08), (0.92, 0.92)]), radius=int(0.1 * SIZE * SUPER), fill=WHITE)
    shapes["bg_square"] = img

    img, d = canvas()
    d.polygon(px(regular_polygon(6, 0.46, math.pi / 6)), fill=WHITE)
    shapes["bg_hexagon"] = img

    img, d = canvas()
    d.polygon(px([(0.16, 0.04), (0.84, 0.04), (0.84, 0.96), (0.5, 0.74), (0.16, 0.96)]), fill=WHITE)
    shapes["bg_banner"] = img

    img, d = canvas()
    d.polygon(px(regular_polygon(4, 0.47, 0)), fill=WHITE)
    d.polygon(px(regular_polygon(4, 0.47, math.pi / 4)), fill=WHITE)
    shapes["bg_star"] = img

    img, d = canvas()
    d.polygon(px([(0.36, 0.04), (0.64, 0.04), (0.64, 0.3), (0.96, 0.3), (0.96, 0.58), (0.64, 0.58),
                  (0.64, 0.96), (0.36, 0.96), (0.36, 0.58), (0.04, 0.58), (0.04, 0.3), (0.36, 0.3)]), fill=WHITE)
    shapes["bg_cross"] = img

    return shapes


# ---------------------------------------------------------------- runes (Elder Futhark)

STAFF = [(0.35, 0.06), (0.35, 0.94)]
RUNES = {
    "fehu": [STAFF, [(0.35, 0.36), (0.74, 0.1)], [(0.35, 0.62), (0.74, 0.36)]],
    "uruz": [[(0.3, 0.94), (0.3, 0.06), (0.72, 0.36), (0.72, 0.94)]],
    "thurisaz": [STAFF, [(0.35, 0.3), (0.7, 0.5), (0.35, 0.7)]],
    "ansuz": [STAFF, [(0.35, 0.06), (0.72, 0.3)], [(0.35, 0.34), (0.72, 0.58)]],
    "raidho": [STAFF, [(0.35, 0.06), (0.7, 0.28), (0.35, 0.5), (0.72, 0.94)]],
    "kaunan": [[(0.66, 0.2), (0.36, 0.5), (0.66, 0.8)]],
    "gebo": [[(0.2, 0.1), (0.8, 0.9)], [(0.8, 0.1), (0.2, 0.9)]],
    "wunjo": [STAFF, [(0.35, 0.06), (0.7, 0.26), (0.35, 0.46)]],
    "hagalaz": [[(0.26, 0.06), (0.26, 0.94)], [(0.74, 0.06), (0.74, 0.94)], [(0.26, 0.36), (0.74, 0.62)]],
    "naudiz": [[(0.5, 0.06), (0.5, 0.94)], [(0.28, 0.34), (0.72, 0.62)]],
    "isa": [[(0.5, 0.06), (0.5, 0.94)]],
    "jera": [[(0.46, 0.1), (0.22, 0.34), (0.46, 0.58)], [(0.54, 0.42), (0.78, 0.66), (0.54, 0.9)]],
    "eihwaz": [[(0.5, 0.06), (0.5, 0.94)], [(0.5, 0.06), (0.76, 0.26)], [(0.5, 0.94), (0.24, 0.74)]],
    "perthro": [[(0.3, 0.06), (0.3, 0.94)], [(0.3, 0.06), (0.62, 0.3), (0.78, 0.14)], [(0.3, 0.94), (0.62, 0.7), (0.78, 0.86)]],
    "algiz": [[(0.5, 0.94), (0.5, 0.06)], [(0.5, 0.46), (0.2, 0.1)], [(0.5, 0.46), (0.8, 0.1)]],
    "sowilo": [[(0.66, 0.06), (0.3, 0.36), (0.7, 0.64), (0.34, 0.94)]],
    "tiwaz": [[(0.5, 0.06), (0.5, 0.94)], [(0.5, 0.06), (0.2, 0.36)], [(0.5, 0.06), (0.8, 0.36)]],
    "berkanan": [[(0.3, 0.06), (0.3, 0.94)], [(0.3, 0.06), (0.7, 0.28), (0.3, 0.5), (0.7, 0.72), (0.3, 0.94)]],
    "ehwaz": [[(0.24, 0.94), (0.24, 0.06), (0.5, 0.36), (0.76, 0.06), (0.76, 0.94)]],
    "mannaz": [[(0.24, 0.94), (0.24, 0.06)], [(0.76, 0.94), (0.76, 0.06)], [(0.24, 0.06), (0.76, 0.46)], [(0.76, 0.06), (0.24, 0.46)]],
    "laguz": [[(0.4, 0.06), (0.4, 0.94)], [(0.4, 0.06), (0.72, 0.32)]],
    "ingwaz": [[(0.5, 0.18), (0.78, 0.5), (0.5, 0.82), (0.22, 0.5), (0.5, 0.18)]],
    "dagaz": [[(0.18, 0.14), (0.18, 0.86)], [(0.82, 0.14), (0.82, 0.86)], [(0.18, 0.14), (0.82, 0.86)], [(0.82, 0.14), (0.18, 0.86)]],
    "othala": [[(0.22, 0.38), (0.5, 0.06), (0.78, 0.38)], [(0.78, 0.38), (0.28, 0.94)], [(0.22, 0.38), (0.72, 0.94)]],
}


def runes():
    out = {}
    for name, lines in RUNES.items():
        img, d = canvas()
        for line in lines:
            # Fit the 0-1 rune grid into the middle of the image, leaving room for the stroke.
            fitted = [(0.14 + 0.72 * x, 0.1 + 0.8 * y) for x, y in line]
            stroke(d, fitted, 0.085)
        out["rune_" + name] = img
    return out


# ---------------------------------------------------------------- icons

def icons():
    out = {}

    # Clan button: a shield with Algiz cut out, in the style of the inventory's round white icons.
    img, d = canvas()
    d.polygon(px(heater_shield(0.06)), fill=WHITE)
    cut = Image.new("RGBA", img.size, (0, 0, 0, 0))
    cd = ImageDraw.Draw(cut)
    for line in RUNES["algiz"]:
        fitted = [(0.3 + 0.4 * x, 0.2 + 0.52 * y) for x, y in line]
        stroke(cd, fitted, 0.07)
    img.paste((0, 0, 0, 0), mask=cut)
    out["clans_button"] = img

    # World Boss: a crown.
    img, d = canvas()
    d.polygon(px([(0.1, 0.78), (0.1, 0.3), (0.3, 0.52), (0.5, 0.18), (0.7, 0.52), (0.9, 0.3), (0.9, 0.78)]), fill=WHITE)
    d.rectangle(px([(0.1, 0.82), (0.9, 0.9)]), fill=WHITE)
    out["tile_worldboss"] = img

    # Race: a heavy chest.
    img, d = canvas()
    d.rounded_rectangle(px([(0.1, 0.42), (0.9, 0.86)]), radius=int(0.04 * SIZE * SUPER), fill=WHITE)
    d.pieslice(px([(0.1, 0.16), (0.9, 0.68)]), 180, 360, fill=WHITE)
    d.rectangle(px([(0.1, 0.38), (0.9, 0.46)]), fill=(0, 0, 0, 0))
    d.rectangle(px([(0.44, 0.44), (0.56, 0.6)]), fill=(0, 0, 0, 0))
    out["tile_race"] = img

    # Trophy Hunt: a target struck by an arrow.
    img, d = canvas()
    d.ellipse(px([(0.06, 0.14), (0.86, 0.94)]), fill=WHITE)
    d.ellipse(px([(0.18, 0.26), (0.74, 0.82)]), fill=(0, 0, 0, 0))
    d.ellipse(px([(0.3, 0.38), (0.62, 0.7)]), fill=WHITE)
    stroke(d, [(0.46, 0.54), (0.92, 0.08)], 0.05)
    d.polygon(px([(0.92, 0.04), (0.98, 0.2), (0.8, 0.16)]), fill=WHITE)
    out["tile_hunt"] = img

    # HUD layout: four arrows, for moving things around the screen.
    img, d = canvas()
    stroke(d, [(0.5, 0.2), (0.5, 0.8)], 0.08)
    stroke(d, [(0.2, 0.5), (0.8, 0.5)], 0.08)
    for tip, left, right in (
        ((0.5, 0.04), (0.34, 0.22), (0.66, 0.22)),
        ((0.5, 0.96), (0.34, 0.78), (0.66, 0.78)),
        ((0.04, 0.5), (0.22, 0.34), (0.22, 0.66)),
        ((0.96, 0.5), (0.78, 0.34), (0.78, 0.66)),
    ):
        d.polygon(px([tip, left, right]), fill=WHITE)
    out["clans_layout"] = img

    return out


# ---------------------------------------------------------------- placeholders

# Stand-ins for vanilla UI sprites, named exactly like them. The game swaps each for the real sprite of the
# same name at runtime (ClansStyle), so these only need to look plausible in the editor. 9-sliced by
# ClansSpriteImporter with a 16 px border.
PLACEHOLDERS = {
    "woodpanel_trophys": ((92, 64, 40, 255), (58, 38, 22, 255)),
    "button": ((120, 84, 50, 255), (70, 46, 26, 255)),
    "text_field": ((34, 26, 20, 235), (20, 15, 11, 235)),
    "checkbox": ((34, 26, 20, 255), (110, 80, 48, 255)),
    "checkbox_marker": ((236, 170, 60, 255), (236, 170, 60, 255)),
    "woodpanel_512x512": ((92, 64, 40, 255), (58, 38, 22, 255)),
    "woodpanel_password": ((92, 64, 40, 255), (58, 38, 22, 255)),
    "button_tab": ((70, 52, 36, 255), (40, 28, 18, 255)),
    "item_background": ((255, 255, 255, 255), (255, 255, 255, 255)),
    "panel_separator": ((150, 98, 40, 255), (150, 98, 40, 255)),
    "bar_monster_hp_20": ((255, 255, 255, 255), (255, 255, 255, 255)),
    "darken_blob": ((0, 0, 0, 160), (0, 0, 0, 160)),
}


def placeholders():
    out = {}
    for name, (fill, edge) in PLACEHOLDERS.items():
        size = 64
        img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        if name == "checkbox_marker":
            d.rounded_rectangle((14, 14, size - 14, size - 14), radius=6, fill=fill)
        elif name == "darken_blob":
            # A soft shadow: alpha falls off from the middle.
            for r in range(size // 2, 0, -1):
                a = int(fill[3] * (1 - r / (size / 2)) ** 0.7)
                d.ellipse((size / 2 - r, size / 2 - r, size / 2 + r, size / 2 + r), fill=(0, 0, 0, a))
        else:
            d.rounded_rectangle((0, 0, size - 1, size - 1), radius=10, fill=edge)
            d.rounded_rectangle((4, 4, size - 5, size - 5), radius=8, fill=fill)
        out[name] = img
    return out


def main():
    for name, img in placeholders().items():
        os.makedirs(os.path.join(SPRITES, "Placeholders"), exist_ok=True)
        img.save(os.path.join(SPRITES, "Placeholders", name + ".png"))
    for name, img in backgrounds().items():
        save(img, EMBLEMS, name)
    for name, img in runes().items():
        save(img, EMBLEMS, name)
    for name, img in icons().items():
        save(img, ICONS, name, 128)
    print("Wrote sprites to", SPRITES)


if __name__ == "__main__":
    main()
