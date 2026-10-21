using Clans.Core;
using Clans.Settings;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Clans.UI {
    // Binds a Clans_UI_EmblemEditor instance: pick a background, a symbol and a colour for each.
    internal sealed class EmblemEditor {
        internal EmblemData Value { get; private set; }
        internal event Action Changed;

        private readonly Transform root;
        private readonly TemplateList backgrounds;
        private readonly TemplateList symbols;
        private readonly TemplateList backColors;
        private readonly TemplateList frontColors;

        internal GameObject GameObject => root.gameObject;

        // Instantiates the editor into host. With buttons, Save and Cancel are shown and call back.
        internal static EmblemEditor Create(Transform host, EmblemData initial, Action<EmblemData> onSave = null, Action onCancel = null) {
            GameObject prefab = ClansAssets.Prefab("Clans_UI_EmblemEditor");
            if (prefab == null || host == null) { return null; }
            GameObject instance = UnityEngine.Object.Instantiate(prefab, host, false);
            ClansStyle.Apply(instance);
            EmblemEditor editor = new EmblemEditor(instance.transform, initial);
            bool buttons = onSave != null || onCancel != null;
            UiBind.SetActive(instance.transform, "Buttons", buttons);
            if (buttons) {
                UiBind.OnClick(instance.transform, "Buttons/Save", () => onSave?.Invoke(editor.Value.Clone()));
                UiBind.OnClick(instance.transform, "Buttons/Cancel", () => onCancel?.Invoke());
            }
            return editor;
        }

        private EmblemEditor(Transform root, EmblemData initial) {
            this.root = root;
            Value = ClansSettings.Emblems.Sanitize(initial);
            backgrounds = new TemplateList(UiBind.Find(root, "Backgrounds"));
            symbols = new TemplateList(UiBind.Find(root, "Symbols"));
            backColors = new TemplateList(UiBind.Find(root, "BackColors"));
            frontColors = new TemplateList(UiBind.Find(root, "FrontColors"));
            Refresh();
        }

        internal void Set(EmblemData value) {
            Value = ClansSettings.Emblems.Sanitize(value);
            Refresh();
        }

        internal void Refresh() {
            EmblemSettings catalog = ClansSettings.Emblems;
            EmblemView.Apply(UiBind.Find(root, "Preview"), Value);

            FillParts(backgrounds, catalog.Backgrounds, Value.BackId, Value.BackColor, id => { Value.BackId = id; });
            FillParts(symbols, catalog.Symbols, Value.FrontId, Value.FrontColor, id => { Value.FrontId = id; });
            List<string> palette = catalog.Palette.FindAll(c => EmblemColors.TryParse(c, out _));
            FillColors(backColors, palette, Value.BackColor, c => { Value.BackColor = c; });
            FillColors(frontColors, palette, Value.FrontColor, c => { Value.FrontColor = c; });
        }

        private void FillParts(TemplateList list, List<EmblemPart> parts, string selected, string colour, Action<string> pick) {
            list.Sync(parts.Count, (item, i) => {
                EmblemPart part = parts[i];
                EmblemView.Layer(item.transform.Find("Icon"), part, part.Tint ? "#F2E6CF" : null);
                item.transform.Find("Selected")?.gameObject.SetActive(part.Id == selected);
                UiBind.OnClick(item.GetComponent<Button>(), () => {
                    pick(part.Id);
                    Refresh();
                    Changed?.Invoke();
                });
            });
        }

        private void FillColors(TemplateList list, List<string> palette, string selected, Action<string> pick) {
            list.Sync(palette.Count, (item, i) => {
                string colour = palette[i];
                Image swatch = item.transform.Find("Swatch")?.GetComponent<Image>();
                if (swatch != null && EmblemColors.TryParse(colour, out Color c)) { swatch.color = c; }
                item.transform.Find("Selected")?.gameObject.SetActive(string.Equals(colour, selected, StringComparison.OrdinalIgnoreCase));
                UiBind.OnClick(item.GetComponent<Button>(), () => {
                    pick(colour);
                    Refresh();
                    Changed?.Invoke();
                });
            });
        }
    }
}
