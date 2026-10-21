using Jotunn.Managers;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI {
    // The runtime style pass for the Clans prefabs, run on every freshly instantiated piece of UI. It makes them
    // look and behave like the game's own inventory panels (Trophies, Skills, Crafting).
    //
    // The prefabs are authored with placeholder sprites named exactly like vanilla UI sprites; each is swapped
    // for the game's own sprite of that name, so no vanilla art ships in the bundle and the window follows
    // vanilla re-skins. As in vanilla:
    // - wood panels get the lit panel material (they dim at night) and buttons and tabs the lit HUD material;
    // - buttons swap to their highlight, pressed and disabled sprites, and grey out their label when disabled;
    // - tabs swap sprites too, and the current tab is a disabled one whose label turns the title colour;
    // - checkboxes and text fields use vanilla's colours and sprites;
    // - fonts follow the text's role: Norse for titles, bold Averia Serif for names and button labels,
    //   Averia Sans for everything else, plain Averia Serif for typed text.
    // Texts starting with $ are localized.
    internal static class ClansStyle {
        private static readonly HashSet<string> vanillaSprites = new HashSet<string> {
            "woodpanel_trophys", "woodpanel_settings", "woodpanel_512x512", "woodpanel_password", "darken_blob",
            "button", "button_tab", "text_field", "checkbox", "checkbox_marker", "item_background", "panel_separator",
            "bar_monster_hp_20",
        };
        private static readonly HashSet<string> hudSprites = new HashSet<string> { "button", "button_tab", "panel_separator" };
        private const string PanelPrefix = "woodpanel";
        private const float ScrollSensitivity = 800f;
        private const float ScrollbarHandleSize = 0.4f;

        // Vanilla's colours (Inventory_screen in the game's main scene).
        internal static readonly Color TitleColor = new Color(1f, 0.718f, 0.36f, 1f);
        private static readonly Color DisabledLabel = new Color(0.616f, 0.616f, 0.616f, 1f);

        private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
        private static Material litPanel;
        private static Material litHud;
        private static GameObject buttonSfx;
        private static GameObject tooltipPrefab;

        internal static void Apply(GameObject root) {
            if (root == null) { return; }
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) {
                try {
                    VanillaFonts.Apply(text, RoleOf(text));
                    if (text.text != null && text.text.StartsWith("$")) { text.text = Localize(text.text); }
                } catch (Exception e) {
                    Logger.LogDebug($"Clans could not restyle '{text.name}': {e.Message}");
                }
            }
            foreach (Image image in root.GetComponentsInChildren<Image>(true)) { SwapSprite(image); }
            foreach (Button button in root.GetComponentsInChildren<Button>(true)) { StyleButton(button); }
            foreach (Toggle toggle in root.GetComponentsInChildren<Toggle>(true)) { StyleToggle(toggle); }
            foreach (TMP_InputField field in root.GetComponentsInChildren<TMP_InputField>(true)) { StyleField(field); }
            foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true)) { FixScroll(scroll); }
        }

        internal static string Localize(string text) {
            if (string.IsNullOrEmpty(text) || Localization.instance == null) { return text ?? ""; }
            return Localization.instance.Localize(text);
        }

        // The prefab convention (see UI/README.md): titles and section headers are named Title, ClanName or
        // *Header; bold texts are names and labels; the rest is body text. ClansUnity's preview uses the same
        // rule (ClansPreviewVanilla.IsTitle); keep the two in step.
        private static VanillaFonts.Role RoleOf(TMP_Text text) {
            if (text.GetComponentInParent<TMP_InputField>(true) != null) { return VanillaFonts.Role.AveriaSerifLibre; }
            string name = text.name;
            if (name == "Title" || name == "ClanName" || name.EndsWith("Header")) { return VanillaFonts.Role.NorseBoldOutline; }
            if ((text.fontStyle & FontStyles.Bold) != 0) { return VanillaFonts.Role.AveriaSerifLibreOutline; }
            return VanillaFonts.Role.AveriaSansLibreOutline;
        }

        private static void SwapSprite(Image image) {
            if (image.sprite == null || vanillaSprites.Contains(image.sprite.name) == false) { return; }
            Sprite vanilla = VanillaSprite(image.sprite.name);
            if (vanilla == null) { return; }
            image.sprite = vanilla;
            Material material = vanilla.name.StartsWith(PanelPrefix, StringComparison.Ordinal) ? LitPanel()
                : hudSprites.Contains(vanilla.name) ? LitHud() : null;
            if (material != null) { image.material = material; }
        }

        private static Sprite VanillaSprite(string name) {
            if (spriteCache.TryGetValue(name, out Sprite sprite) && sprite != null) { return sprite; }
            sprite = GUIManager.Instance != null ? GUIManager.Instance.GetSprite(name) : null;
            if (sprite != null) { spriteCache[name] = sprite; }
            return sprite;
        }

        // Vanilla's buttons (TakeAll, Craft) and tabs (Crafting/Upgrade) swap sprites rather than tint.
        private static void StyleButton(Button button) {
            string sprite = button.targetGraphic is Image image && image.sprite != null ? image.sprite.name : "";
            if (sprite == "button") {
                SpriteSwap(button, "button_highlight", "button_pressed", "button_highlight", "button_disabled",
                    Colors(Color.white, Grey(0.961f), Grey(0.784f), new Color(0.784f, 0.784f, 0.784f, 0.5f)));
                LabelColors(button, DisabledLabel);
            } else if (sprite == "button_tab") {
                SpriteSwap(button, "button_tab_hover", "button_tab_selected", "button_tab_hover", "button_tab_selected",
                    Colors(Color.white, Grey(0.824f), Grey(0.537f), new Color(0.565f, 0.565f, 0.565f, 0.5f)));
                // The current tab is disabled (UiBind.SetTabSelected); its label takes the title colour.
                LabelColors(button, TitleColor);
            }
            if (button.GetComponent<ButtonSfx>() == null) {
                GameObject sfx = ButtonSound();
                if (sfx != null) { button.gameObject.AddComponent<ButtonSfx>().m_sfxPrefab = sfx; }
            }
        }

        private static void SpriteSwap(Selectable selectable, string highlighted, string pressed, string selected, string disabled, ColorBlock colors) {
            Sprite highlight = VanillaSprite(highlighted);
            if (highlight == null) { return; }
            selectable.transition = Selectable.Transition.SpriteSwap;
            selectable.spriteState = new SpriteState {
                highlightedSprite = highlight,
                pressedSprite = VanillaSprite(pressed),
                selectedSprite = VanillaSprite(selected),
                disabledSprite = VanillaSprite(disabled),
            };
            selectable.colors = colors;
        }

        private static ColorBlock Colors(Color normal, Color highlighted, Color pressed, Color disabled) {
            return new ColorBlock {
                normalColor = normal,
                highlightedColor = highlighted,
                pressedColor = pressed,
                selectedColor = highlighted,
                disabledColor = disabled,
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };
        }

        private static Color Grey(float value) => new Color(value, value, value, 1f);

        // Vanilla's ButtonTextColor: keeps the label's own colour while the button can be used, and shows
        // the disabled colour while it cannot.
        private static void LabelColors(Button button, Color disabled) {
            if (button.GetComponentInChildren<TMP_Text>(true) == null) { return; }
            ButtonTextColor colors = button.GetComponent<ButtonTextColor>();
            if (colors == null) { colors = button.gameObject.AddComponent<ButtonTextColor>(); }
            colors.m_disabledColor = disabled;
        }

        private static void StyleToggle(Toggle toggle) {
            if (toggle.targetGraphic is Image image && image.sprite != null && image.sprite.name == "checkbox") {
                toggle.transition = Selectable.Transition.ColorTint;
                toggle.colors = Colors(Grey(0.714f), Grey(0.961f), Grey(0.784f), new Color(0.784f, 0.784f, 0.784f, 0.5f));
            }
        }

        private static void StyleField(TMP_InputField field) {
            if (field.targetGraphic is Image image && image.sprite != null && image.sprite.name == "text_field") {
                SpriteSwap(field, "text_field_highlight", "text_field_highlight", "text_field_highlight", "text_field_disabled",
                    Colors(Color.white, Color.white, Color.white, new Color(0.784f, 0.784f, 0.784f, 0.5f)));
            }
        }

        private static void FixScroll(ScrollRect scroll) {
            if (scroll.GetComponent<ScrollFixed>() != null) { return; }
            scroll.gameObject.AddComponent<ScrollFixed>();
            scroll.scrollSensitivity = ScrollSensitivity;
            Scrollbar scrollbar = scroll.verticalScrollbar;
            if (scrollbar == null) { return; }
            scrollbar.size = ScrollbarHandleSize;
            scroll.onValueChanged.AddListener(_ => scrollbar.size = ScrollbarHandleSize);
        }

        private sealed class ScrollFixed : MonoBehaviour { }

        private static Material LitPanel() {
            if (litPanel == null) {
                try { litPanel = PrefabManager.Cache.GetPrefab<Material>("litpanel"); } catch (Exception) { /* panels stay unlit */ }
            }
            return litPanel;
        }

        private static Material LitHud() {
            if (litHud == null) {
                try { litHud = PrefabManager.Cache.GetPrefab<Material>("lithud"); } catch (Exception) { /* buttons stay unlit */ }
            }
            return litHud;
        }

        private static GameObject ButtonSound() {
            if (buttonSfx == null) {
                try { buttonSfx = PrefabManager.Cache.GetPrefab<GameObject>("sfx_gui_button"); } catch (Exception) { /* silent buttons */ }
            }
            return buttonSfx;
        }

        // Gives an element a vanilla hover tooltip. The tooltip prefab is borrowed from the inventory screen.
        internal static void Tooltip(GameObject target, string text) {
            if (target == null) { return; }
            if (tooltipPrefab == null && InventoryGui.instance != null && InventoryGui.instance.m_pvp != null) {
                UITooltip vanilla = InventoryGui.instance.m_pvp.GetComponent<UITooltip>();
                if (vanilla != null) { tooltipPrefab = vanilla.m_tooltipPrefab; }
            }
            if (tooltipPrefab == null) { return; }
            UITooltip tooltip = target.GetComponent<UITooltip>();
            if (tooltip == null) { tooltip = target.AddComponent<UITooltip>(); }
            tooltip.m_tooltipPrefab = tooltipPrefab;
            tooltip.m_text = text ?? "";
        }
    }
}
