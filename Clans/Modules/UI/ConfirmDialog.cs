using System;
using TMPro;
using UnityEngine;

namespace Clans.UI {
    // The Clans_UI_Confirm dialog: a question with OK and Cancel, optionally with a text field.
    internal static class ConfirmDialog {
        private static GameObject open;

        internal static bool IsOpen => open != null;

        internal static void Ask(Transform parent, string title, string body, Action onOk) {
            Show(parent, title, body, null, 0, false, _ => onOk?.Invoke());
        }

        // A dialog with a text field. onOk receives what was typed.
        internal static void Prompt(Transform parent, string title, string body, string initial, int characterLimit, bool numbersOnly, Action<string> onOk) {
            Show(parent, title, body, initial ?? "", characterLimit, numbersOnly, onOk);
        }

        internal static void Close() {
            if (open != null) { UnityEngine.Object.Destroy(open); }
            open = null;
        }

        private static void Show(Transform parent, string title, string body, string input, int characterLimit, bool numbersOnly, Action<string> onOk) {
            Close();
            GameObject prefab = ClansAssets.Prefab("Clans_UI_Confirm");
            if (prefab == null || parent == null) { return; }
            open = UnityEngine.Object.Instantiate(prefab, parent, false);
            open.transform.SetAsLastSibling();
            ClansStyle.Apply(open);
            Transform root = open.transform;

            UiBind.SetText(root, "Overlay/Panel/Title", UiBind.Loc(title));
            UiBind.SetText(root, "Overlay/Panel/Body", UiBind.Loc(body));
            TMP_InputField field = UiBind.Get<TMP_InputField>(root, "Overlay/Panel/Input");
            if (field != null) {
                field.gameObject.SetActive(input != null);
                if (input != null) {
                    field.text = input;
                    field.characterLimit = characterLimit;
                    field.contentType = numbersOnly ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.Standard;
                    field.ActivateInputField();
                }
            }
            UiBind.OnClick(root, "Overlay/Panel/Buttons/Ok", () => {
                string typed = field != null ? field.text : "";
                Close();
                onOk?.Invoke(typed);
            });
            UiBind.OnClick(root, "Overlay/Panel/Buttons/Cancel", Close);
        }
    }
}
