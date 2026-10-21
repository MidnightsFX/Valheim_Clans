using Clans.Core;
using Clans.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI {
    // Draws an emblem into a two-layer element: children "Back" and "Front", each an Image.
    internal static class EmblemView {
        internal static void Apply(Transform emblem, EmblemData data) {
            if (emblem == null) { return; }
            EmblemSettings catalog = ClansSettings.Emblems;
            EmblemData clean = catalog.Sanitize(data);
            Layer(emblem.Find("Back"), catalog.FindBackground(clean.BackId), clean.BackColor);
            Layer(emblem.Find("Front"), catalog.FindSymbol(clean.FrontId), clean.FrontColor);
        }

        internal static void Layer(Transform layer, EmblemPart part, string colour) {
            Image image = layer != null ? layer.GetComponent<Image>() : null;
            if (image == null) { return; }
            Sprite sprite = ClansAssets.EmblemSprite(part);
            image.enabled = sprite != null;
            if (sprite == null) { return; }
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = part.Tint && EmblemColors.TryParse(colour, out Color tint) ? tint : Color.white;
        }
    }
}
