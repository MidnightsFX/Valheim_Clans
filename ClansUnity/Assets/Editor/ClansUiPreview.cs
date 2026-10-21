using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Renders the Clans UI prefabs to PNGs, so a layout can be checked without entering play mode.
//
// From the editor: menu Clans/UI/Render Previews (writes ClansUnity/Previews).
// Headless:
//   Unity.exe -batchmode -quit -projectPath ClansUnity -executeMethod ClansUiPreview.RenderFromCommandLine -previewOut <dir> -logFile preview.log
//
// Pages are shown inside the window shell, as in game. The game's own fonts and panel art are not in this
// project: with Tools/fetch_preview_vanilla.py run once, ClansPreviewVanilla swaps them in like the mod does in
// game; otherwise text uses TextMeshPro's default font and panels the placeholder sprites.
public static class ClansUiPreview {
    private const string UiRoot = "Assets/Clans/Bundle/UI/";
    private const int Width = 1920;
    private const int Height = 1080;

    [MenuItem("Clans/UI/Render Previews")]
    public static void RenderMenu() {
        Render(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Previews")));
    }

    public static void RenderFromCommandLine() {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-previewOut");
        string output = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Previews"));
        Render(output);
    }

    // Imports TextMeshPro's essential resources (default font and settings) when the project lacks them.
    // Run once on a fresh checkout, in its own editor session, before building prefabs.
    public static void SetupProject() {
        if (Resources.Load<TMP_Settings>("TMP Settings") != null) {
            Debug.Log("[Clans] TextMeshPro essentials are already imported.");
            return;
        }
        string package = Directory.GetFiles(Path.GetFullPath("Library/PackageCache"), "TMP Essential Resources.unitypackage", SearchOption.AllDirectories).FirstOrDefault();
        if (package == null) {
            Debug.LogError("[Clans] Could not find TMP Essential Resources.unitypackage in the package cache.");
            return;
        }
        AssetDatabase.ImportPackage(package, false);
        AssetDatabase.Refresh();
        Debug.Log("[Clans] Imported TextMeshPro essentials from " + package);
    }

    public static void Render(string outputDir) {
        Directory.CreateDirectory(outputDir);
        // In the editor, an additive scratch scene leaves the open scene untouched. Headless there is only an
        // untitled scene, which cannot have an additive scene next to it, so replace it instead.
        bool additive = string.IsNullOrEmpty(SceneManager.GetActiveScene().path) == false;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, additive ? NewSceneMode.Additive : NewSceneMode.Single);
        try {
            RenderWindowWithPage(scene, outputDir, "Clans_Page_NoClan", null);
            RenderWindowWithPage(scene, outputDir, "Clans_Page_Create", page => {
                Transform host = page.transform.Find("EmblemHost");
                GameObject editor = Load("Clans_UI_EmblemEditor");
                if (host != null && editor != null) {
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(editor, host);
                    FillGrids(instance);
                    instance.transform.Find("Buttons").gameObject.SetActive(false);
                }
            });
            RenderWindowWithPage(scene, outputDir, "Clans_Page_Join", page => ShowTemplates(page, 3));
            RenderWindowWithPage(scene, outputDir, "Clans_Page_Overview", page => ShowTemplates(page, 4));
            RenderWindowWithPage(scene, outputDir, "Clans_Page_Members", page => {
                ShowTemplates(page, 6);
                // As the page fills them: your own row shows no buttons, the others what your rank allows.
                string[] buttons = { "Promote", "Demote", "Kick", "Transfer" };
                List<Transform> rows = page.transform.Find("List").GetComponentsInChildren<Transform>()
                    .Where(t => t.name == "Item(Clone)").ToList();
                for (int i = 0; i < rows.Count; i++) {
                    for (int b = 0; b < buttons.Length; b++) {
                        rows[i].Find("Actions/" + buttons[b]).gameObject.SetActive(b >= buttons.Length - i % 5);
                    }
                }
            });
            RenderWindowWithPage(scene, outputDir, "Clans_Page_Members", page => {
                Transform editor = page.transform.Find("RankEditor");
                editor.gameObject.SetActive(true);
                ShowTemplates(editor.gameObject, 3);
                foreach (Transform flags in editor.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Perms").ToList()) {
                    Transform flag = flags.Find("Flag");
                    if (flag == null) { continue; }
                    for (int i = 0; i < 14; i++) { Object.Instantiate(flag.gameObject, flags).SetActive(true); }
                }
            }, "Clans_Page_Members_RankEditor");
            RenderWindowWithPage(scene, outputDir, "Clans_Page_Party", page => ShowTemplates(page, 3));
            RenderWindowWithPage(scene, outputDir, "Clans_Page_War", page => ShowTemplates(page, 4));
            RenderWindowWithPage(scene, outputDir, "Clans_Page_Storage", page => ShowTemplates(page, 5));
            RenderWindowWithPage(scene, outputDir, "Clans_Page_Activities", page => ShowTemplates(page, 3, "Tile"));
            RenderWindowWithPage(scene, outputDir, "Clans_Page_Level", page => ShowTemplates(page, 4));
            RenderAlone(scene, outputDir, "Clans_UI_Confirm");
            RenderAlone(scene, outputDir, "Clans_HUD_PartyFrame", root => { ShowTemplates(root, 3, "Member"); ShowTemplates(root, 6); });
            RenderAlone(scene, outputDir, "Clans_HUD_InviteToast");
            RenderAlone(scene, outputDir, "Clans_INV_InvitePanel", root => ShowTemplates(root, 2));
            RenderAlone(scene, outputDir, "Clans_HUD_Chat", FillChat);
            RenderAlone(scene, outputDir, "Clans_HUD_Tracker", root => { ShowTemplates(root, 2, "Activity"); ShowTemplates(root, 3, "Line"); });
            RenderAlone(scene, outputDir, "Clans_INV_TrackerMenu", root => ShowTemplates(root, 2));
            RenderAlone(scene, outputDir, "Clans_HUD_LayoutEditor", root => ShowTemplates(root, 1, "Outline"));
        } finally {
            if (additive) { EditorSceneManager.CloseScene(scene, true); }
        }
        Debug.Log("[Clans] Wrote UI previews to " + outputDir);
    }

    private static GameObject Load(string name) {
        string path = UiRoot + name + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiRoot + "Pages/" + name + ".prefab"); }
        if (prefab == null) { prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiRoot + "Hud/" + name + ".prefab"); }
        if (prefab == null) { Debug.LogWarning("[Clans] Preview: no prefab " + name); }
        return prefab;
    }

    private static void RenderWindowWithPage(Scene scene, string outputDir, string pageName, Action<GameObject> prepare, string fileName = null) {
        GameObject window = Load("Clans_UI_Window");
        GameObject page = Load(pageName);
        if (window == null || page == null) { return; }
        Canvas canvas = NewCanvas(scene, out Camera camera);
        GameObject windowInstance = (GameObject)PrefabUtility.InstantiatePrefab(window, canvas.transform);
        Transform pages = windowInstance.transform.Find("Panel/Pages");
        GameObject pageInstance = (GameObject)PrefabUtility.InstantiatePrefab(page, pages);
        ShowTabs(windowInstance);
        prepare?.Invoke(pageInstance);
        Capture(canvas, camera, Path.Combine(outputDir, (fileName ?? pageName) + ".png"));
        Object.DestroyImmediate(canvas.gameObject);
        Object.DestroyImmediate(camera.gameObject);
    }

    private static void RenderAlone(Scene scene, string outputDir, string name, Action<GameObject> prepare = null) {
        GameObject prefab = Load(name);
        if (prefab == null) { return; }
        Canvas canvas = NewCanvas(scene, out Camera camera);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
        prepare?.Invoke(instance);
        Capture(canvas, camera, Path.Combine(outputDir, name + ".png"));
        Object.DestroyImmediate(canvas.gameObject);
        Object.DestroyImmediate(camera.gameObject);
    }

    // Shows a few clones of every inactive Item template under the root, as a filled list would.
    private static void ShowTemplates(GameObject root, int count, string template = "Item") {
        foreach (Transform item in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == template).ToList()) {
            for (int i = 0; i < count; i++) { Object.Instantiate(item.gameObject, item.parent).SetActive(true); }
        }
        Transform none = root.transform.Find("Right/Applications/None");
        if (none != null) { none.gameObject.SetActive(false); }
        Transform empty = root.transform.Find("Empty");
        if (empty != null) { empty.gameObject.SetActive(false); }
    }

    // A chat panel mid-conversation: the tabs, and rows from a few channels.
    private static void FillChat(GameObject root) {
        Transform tab = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Tab");
        string[] tabs = { "All", "Global", "Clan", "Party", "Raid" };
        for (int i = 0; tab != null && i < tabs.Length; i++) {
            GameObject clone = Object.Instantiate(tab.gameObject, tab.parent);
            clone.SetActive(true);
            clone.GetComponentInChildren<TMP_Text>(true).text = tabs[i];
            clone.GetComponent<Button>().interactable = i != 0;
            clone.transform.Find("Unread").gameObject.SetActive(i == 2);
        }
        Transform row = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Row");
        (string channel, string name, string body)[] lines = {
            ("", "Ragnhild", "Anyone up for the swamp crypts tonight?"),
            ("<color=#40FF40>[Clan]</color>", "Eirik", "World Boss totem is up near the river.\nBring frost resist."),
            ("<color=#F2C48C>[Global]</color>", "Sigrun", "Showing off: <color=#d078ff>[Frostfang Blackmetal Sword]</color>"),
            ("<color=#AAAAFF>[Party]</color>", "Bjorn", "<color=#AAAAFF><u>[Join Bjorn's party]</u></color>"),
        };
        foreach ((string channel, string name, string body) in lines) {
            GameObject clone = Object.Instantiate(row.gameObject, row.parent);
            clone.SetActive(true);
            clone.transform.Find("Head/Channel").GetComponent<TMP_Text>().text = channel;
            clone.transform.Find("Head/Channel").gameObject.SetActive(channel.Length > 0);
            clone.transform.Find("Head/Name").GetComponent<TMP_Text>().text = name;
            clone.transform.Find("Body").GetComponent<TMP_Text>().text = body;
        }
    }

    // The grids show the placeholder emblem's parts (ClansUiBuilder.MakeEmblem) as selected.
    private static void FillGrids(GameObject editor) {
        string[] backgrounds = Directory.GetFiles("Assets/Clans/Bundle/Sprites/Emblems", "bg_*.png");
        string[] symbols = Directory.GetFiles("Assets/Clans/Bundle/Sprites/Emblems", "rune_*.png");
        Fill(editor.transform.Find("Backgrounds"), backgrounds, "bg_shield.png");
        Fill(editor.transform.Find("Symbols"), symbols, "rune_algiz.png");
        Color[] colors = {
            new Color(0.95f, 0.9f, 0.81f), new Color(0.79f, 0.7f, 0.54f), new Color(0.55f, 0.42f, 0.25f), new Color(0.29f, 0.2f, 0.13f),
            new Color(0.12f, 0.1f, 0.09f), new Color(0.56f, 0.11f, 0.11f), new Color(0.77f, 0.27f, 0.17f), new Color(0.85f, 0.54f, 0.17f),
            new Color(0.91f, 0.77f, 0.28f), new Color(0.31f, 0.48f, 0.16f), new Color(0.17f, 0.42f, 0.56f), new Color(0.36f, 0.23f, 0.48f),
        };
        FillColors(editor.transform.Find("BackColors"), colors, 3);
        FillColors(editor.transform.Find("FrontColors"), colors, 8);
    }

    private static void Fill(Transform grid, string[] spritePaths, string selectedFile) {
        Transform item = grid?.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Item");
        if (item == null) { return; }
        foreach (string path in spritePaths) {
            GameObject clone = Object.Instantiate(item.gameObject, item.parent);
            clone.SetActive(true);
            clone.transform.Find("Icon").GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path.Replace('\\', '/'));
            clone.transform.Find("Selected")?.gameObject.SetActive(Path.GetFileName(path) == selectedFile);
        }
    }

    private static void FillColors(Transform grid, Color[] colors, int selected) {
        Transform item = grid?.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Item");
        if (item == null) { return; }
        for (int i = 0; i < colors.Length; i++) {
            GameObject clone = Object.Instantiate(item.gameObject, item.parent);
            clone.SetActive(true);
            clone.transform.Find("Swatch").GetComponent<Image>().color = colors[i];
            clone.transform.Find("Selected")?.gameObject.SetActive(i == selected);
        }
    }

    private static void ShowTabs(GameObject window) {
        Transform tab = window.transform.Find("Panel/Tabs/Tab");
        if (tab == null) { return; }
        string[] names = { "Overview", "Members", "War", "Activities", "Storage", "Party" };
        for (int i = 0; i < names.Length; i++) {
            GameObject clone = Object.Instantiate(tab.gameObject, tab.parent);
            clone.SetActive(true);
            clone.GetComponentInChildren<TMP_Text>(true).text = names[i];
            // The current tab is disabled, as the mod does it (UiBind.SetTabSelected).
            clone.GetComponent<Button>().interactable = i != 0;
        }
    }

    private static Canvas NewCanvas(Scene scene, out Camera camera) {
        GameObject cameraGo = new GameObject("PreviewCamera");
        SceneManager.MoveGameObjectToScene(cameraGo, scene);
        camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.16f, 0.18f, 0.2f, 1f);
        camera.orthographic = true;

        GameObject canvasGo = new GameObject("PreviewCanvas", typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(canvasGo, scene);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 10f;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(Width, Height);
        // As on the game's inventory screen, where vanilla's 50 pixels-per-unit sprites draw their borders 1:1.
        scaler.referencePixelsPerUnit = 50f;
        return canvas;
    }

    private static void Capture(Canvas canvas, Camera camera, string file) {
        ClansPreviewVanilla.Apply(canvas.gameObject);
        RenderTexture target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.aspect = (float)Width / Height;
        // A camera-space canvas sizes itself from its camera's target on the next update; force it now.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
        foreach (RectTransform rt in canvas.GetComponentsInChildren<RectTransform>(true)) {
            LayoutRebuilder.MarkLayoutForRebuild(rt);
        }
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
        Canvas.ForceUpdateCanvases();
        camera.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(file, image.EncodeToPNG());
        camera.targetTexture = null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(target);
    }
}
