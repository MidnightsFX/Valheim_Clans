using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Generates the Clans UI prefabs under Assets/Clans/Bundle/UI.
//
// The contract with the mod code (prefab names, child names, templates) is Clans/UI/README.md in the mod
// project; keep the two in step. The prefabs carry no Clans components: code finds children by name, so a
// prefab never depends on the mod assembly and maintainers can move and restyle anything here freely.
//
// "Build Shell And Overlays" overwrites the window, dialog and emblem editor. "Build Missing Pages" only
// creates pages that do not exist yet, so hand edits survive; "Rebuild All Pages" is the deliberate reset.
//
// Sprites are the placeholders in Sprites/Placeholders, named like the vanilla UI sprites they stand in
// for; the mod swaps them for the real ones at runtime. Texts are TextMeshPro with the default font; the
// mod switches them to Valheim's fonts at runtime.
public static class ClansUiBuilder {
    private const string Root = "Assets/Clans/Bundle/UI";
    private const string PagesDir = Root + "/Pages";
    private const string HudDir = Root + "/Hud";
    private const string Placeholders = "Assets/Clans/Bundle/Sprites/Placeholders/";
    private const string Icons = "Assets/Clans/Bundle/Sprites/Icons/";

    private const float WindowW = 1060f;
    private const float WindowH = 740f;
    private const float RowH = 34f;

    // Vanilla's inventory screen colours (Trophies, Skills and Crafting panels in the game's main scene).
    // TitleColor: panel titles, section headers and accents. Gold: names in lists. Orange: button and checkbox
    // captions. TabText: tab captions. Body: all other text, which vanilla draws white with a black outline.
    private static readonly Color TitleColor = new Color(1f, 0.718f, 0.36f, 1f);
    private static readonly Color Gold = new Color(1f, 0.848f, 0f, 1f);
    private static readonly Color Orange = new Color(1f, 0.631f, 0.235f, 1f);
    private static readonly Color TabText = new Color(0.847f, 0.847f, 0.847f, 1f);
    private static readonly Color Body = Color.white;
    private static readonly Color Grey = new Color(0.66f, 0.66f, 0.66f, 1f);
    private static readonly Color Amber = new Color(1f, 0.807f, 0f, 1f);
    private static readonly Color CheckColor = new Color(1f, 0.641f, 0f, 1f);
    private static readonly Color InputText = new Color(0.957f, 0.957f, 0.957f, 1f);
    private static readonly Color PlaceholderText = new Color(0.671f, 0.671f, 0.671f, 0.5f);
    // Vanilla's popup backdrop, dark list wells, row shading and bar backgrounds.
    private static readonly Color Dim = new Color(0f, 0f, 0f, 0.314f);
    private static readonly Color Well = new Color(0f, 0f, 0f, 0.565f);
    private static readonly Color Shade = new Color(0f, 0f, 0f, 0.39f);
    private static readonly Color BarBack = new Color(0f, 0f, 0f, 0.718f);
    // The HUD layout editor's outlines: a faint grey box with a grey rim, which the mod tints while selected.
    private static readonly Color OutlineFill = new Color(0.6f, 0.6f, 0.6f, 0.18f);
    private static readonly Color OutlineLine = new Color(0.75f, 0.75f, 0.75f, 0.9f);
    private static readonly Color OutlineText = new Color(0.9f, 0.9f, 0.9f, 1f);

    [MenuItem("Clans/UI/Build Everything")]
    public static void BuildEverything() {
        BuildShellAndOverlays();
        BuildPages(overwrite: true);
    }

    [MenuItem("Clans/UI/Build Shell And Overlays")]
    public static void BuildShellAndOverlays() {
        EnsureFolders();
        BuildWindow();
        BuildConfirm();
        BuildEmblemEditor();
        BuildPartyFrame();
        BuildInviteToast();
        BuildInvitePanel();
        BuildStorageTabs();
        BuildChatPanel();
        BuildTracker();
        BuildTrackerMenu();
        BuildLayoutEditor();
        AssetDatabase.SaveAssets();
        Debug.Log("[Clans] Built the window, dialog, emblem editor, party frame, invite, chat, tracker and layout editor prefabs.");
    }

    [MenuItem("Clans/UI/Build Missing Pages")]
    public static void BuildMissingPages() => BuildPages(overwrite: false);

    [MenuItem("Clans/UI/Rebuild All Pages (overwrite)")]
    public static void RebuildAllPages() => BuildPages(overwrite: true);

    // Headless entry point:
    //   Unity.exe -batchmode -quit -projectPath ClansUnity -executeMethod ClansUiBuilder.BuildAllFromCommandLine -logFile ui.log
    public static void BuildAllFromCommandLine() {
        BuildEverything();
        ClansBundleBuilder.Build();
    }

    // Same, then renders the previews (see ClansUiPreview for -previewOut).
    public static void BuildAndPreviewFromCommandLine() {
        BuildAllFromCommandLine();
        ClansUiPreview.RenderFromCommandLine();
    }

    // Only adds the pages that do not exist yet (hand edits to the others survive), rebuilds the bundle and renders
    // the previews:
    //   Unity.exe -batchmode -quit -projectPath ClansUnity -executeMethod ClansUiBuilder.BuildMissingAndPreviewFromCommandLine -previewOut <dir> -logFile ui.log
    public static void BuildMissingAndPreviewFromCommandLine() {
        BuildMissingPages();
        ClansBundleBuilder.Build();
        ClansUiPreview.RenderFromCommandLine();
    }

    // Rebuilds only the pages named by -pages (comma-separated, hand edits to the others survive), rebuilds the
    // bundle and renders the previews:
    //   Unity.exe -batchmode -quit -projectPath ClansUnity -executeMethod ClansUiBuilder.RebuildPagesAndPreviewFromCommandLine -pages Clans_Page_Members -previewOut <dir> -logFile ui.log
    public static void RebuildPagesAndPreviewFromCommandLine() {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-pages");
        if (index < 0 || index + 1 >= args.Length) {
            Debug.LogError("[Clans] -pages is missing: give the page prefab names, comma-separated.");
            return;
        }
        BuildPages(overwrite: true, only: new HashSet<string>(args[index + 1].Split(',')));
        ClansBundleBuilder.Build();
        ClansUiPreview.RenderFromCommandLine();
    }

    private static void BuildPages(bool overwrite, HashSet<string> only = null) {
        EnsureFolders();
        (string Name, Action<Transform> Build)[] pages = {
            ("Clans_Page_NoClan", BuildNoClanPage),
            ("Clans_Page_Create", BuildCreatePage),
            ("Clans_Page_Join", BuildJoinPage),
            ("Clans_Page_Overview", BuildOverviewPage),
            ("Clans_Page_Members", BuildMembersPage),
            ("Clans_Page_Party", BuildPartyPage),
            ("Clans_Page_War", BuildWarPage),
            ("Clans_Page_Storage", BuildStoragePage),
            ("Clans_Page_Activities", BuildActivitiesPage),
            ("Clans_Page_Level", BuildLevelPage),
        };
        int built = 0;
        foreach ((string name, Action<Transform> build) in pages) {
            if (only != null && only.Contains(name) == false) { continue; }
            string path = PagesDir + "/" + name + ".prefab";
            if (overwrite == false && File.Exists(path)) { continue; }
            GameObject root = NewUI(name, null);
            Stretch(root);
            build(root.transform);
            SavePrefab(root, path);
            built++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Clans] Built {built} page prefab(s).");
    }

    // ------------------------------------------------------------------------------------------------
    //  Window
    // ------------------------------------------------------------------------------------------------

    private static void BuildWindow() {
        GameObject root = NewUI("Clans_UI_Window", null);
        Stretch(root);

        // Like vanilla's Trophies and Skills popups: the screen behind is darkened, and clicking it closes.
        GameObject backdrop = NewUI("Backdrop", root.transform);
        Stretch(backdrop);
        Image dim = backdrop.AddComponent<Image>();
        dim.color = Dim;
        backdrop.AddComponent<Button>().transition = Selectable.Transition.None;

        GameObject panel = MakePanel(root.transform, "Panel", WindowW, WindowH);

        // Header: emblem, name, level, experience, the PvP toggle's slot, close.
        GameObject header = NewUI("Header", panel.transform);
        Place(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -112f), new Vector2(-24f, -16f));

        GameObject emblem = MakeEmblem(header.transform, "Emblem", 88f);
        Place(emblem, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, -44f), new Vector2(88f, 44f));

        TextMeshProUGUI clanName = AddText(header.transform, "ClanName", "Clan name", 32, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(clanName.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(104f, -46f), new Vector2(-200f, -2f));

        TextMeshProUGUI sub = AddText(header.transform, "ClanSub", "Level 1 - 3 members", 16, Body, TextAlignmentOptions.MidlineLeft);
        Place(sub.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(104f, -70f), new Vector2(-200f, -48f));

        GameObject xp = NewUI("XpBar", header.transform);
        Place(xp, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(104f, -92f), new Vector2(404f, -74f));
        // Vanilla's skill level bar: a dark back with a bar_monster_hp_20 fill.
        Image xpBack = xp.AddComponent<Image>();
        xpBack.color = BarBack;
        GameObject fill = NewUI("Fill", xp.transform);
        Stretch(fill, 2f);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.sprite = Sprite(Placeholders + "bar_monster_hp_20.png");
        fillImage.color = Amber;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillAmount = 0.4f;
        fillImage.raycastTarget = false;
        TextMeshProUGUI xpText = AddText(xp.transform, "Text", "400 / 1000", 14, Color.white, TextAlignmentOptions.Center);
        Stretch(xpText.gameObject);

        // The vanilla PvP toggle is moved into this slot at runtime.
        GameObject pvpSlot = NewUI("PvpSlot", header.transform);
        Place(pvpSlot, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-150f, -14f), new Vector2(-86f, 50f));
        TextMeshProUGUI pvpLabel = AddText(header.transform, "PvpLabel", "$clans_pvp", 14, Body, TextAlignmentOptions.Center);
        Place(pvpLabel.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-160f, -40f), new Vector2(-76f, -16f));

        // Beside it: the HUD layout editor's button, an icon on a vanilla button.
        Button layout = MakeButton(header.transform, "LayoutButton", "", 52f, 52f, 14);
        RemoveLayoutElement(layout);
        Object.DestroyImmediate(layout.transform.Find("Text").gameObject);
        Place(layout.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-66f, -8f), new Vector2(-14f, 44f));
        GameObject layoutIcon = NewUI("Icon", layout.transform);
        Stretch(layoutIcon, 10f);
        Image layoutImage = layoutIcon.AddComponent<Image>();
        layoutImage.sprite = Sprite(Icons + "clans_layout.png");
        layoutImage.preserveAspect = true;
        layoutImage.raycastTarget = false;
        TextMeshProUGUI layoutLabel = AddText(header.transform, "LayoutLabel", "$clans_layout", 14, Body, TextAlignmentOptions.Center);
        Place(layoutLabel.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-74f, -40f), new Vector2(-6f, -16f));

        // Tabs: the code clones Tab per page and hides the template.
        GameObject tabs = NewUI("Tabs", panel.transform);
        Place(tabs, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -162f), new Vector2(-24f, -126f));
        HorizontalLayoutGroup tabsLayout = tabs.AddComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = 4f;
        tabsLayout.childAlignment = TextAnchor.MiddleLeft;
        tabsLayout.childControlWidth = true;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = false;
        tabsLayout.childForceExpandHeight = true;
        Button tab = MakeTab(tabs.transform, "Tab", "Tab", 136f, 36f);
        tab.gameObject.SetActive(false);
        // Vanilla's line under the crafting tabs.
        MakeSeparator(panel.transform, "TabBorder", -164f);

        // Pages are instantiated in here.
        GameObject pages = NewUI("Pages", panel.transform);
        Place(pages, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24f, 74f), new Vector2(-24f, -176f));

        // Bottom row: what just happened on the left, vanilla's Close button on the right.
        TextMeshProUGUI status = AddText(panel.transform, "Status", "", 16, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(status.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 18f), new Vector2(-210f, 64f));
        Button close = MakeButton(panel.transform, "Close", "$clans_close", 170f, 46f, 20);
        RemoveLayoutElement(close);
        Place(close.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-194f, 18f), new Vector2(-24f, 64f));

        SavePrefab(root, Root + "/Clans_UI_Window.prefab");
    }

    // A yes/no dialog, optionally with a text field.
    private static void BuildConfirm() {
        GameObject root = NewUI("Clans_UI_Confirm", null);
        Stretch(root);
        GameObject overlay = NewUI("Overlay", root.transform);
        Stretch(overlay);
        Image dim = overlay.AddComponent<Image>();
        dim.color = Dim;

        GameObject panel = MakePanel(overlay.transform, "Panel", 540f, 280f, "woodpanel_512x512.png");
        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 24, 24);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI title = AddText(panel.transform, "Title", "Are you sure?", 28, TitleColor, TextAlignmentOptions.Center);
        Prefer(title.gameObject, 0f, 32f);
        TextMeshProUGUI body = AddText(panel.transform, "Body", "Body text", 16, Body, TextAlignmentOptions.Center);
        Prefer(body.gameObject, 0f, 70f, flexibleHeight: 1f);
        TMP_InputField input = MakeInputField(panel.transform, "Input", 0f, 40f, TMP_InputField.ContentType.Standard);
        input.gameObject.SetActive(false);

        GameObject buttons = NewUI("Buttons", panel.transform);
        Prefer(buttons, 0f, 48f);
        HorizontalLayoutGroup row = buttons.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 20f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        MakeButton(buttons.transform, "Ok", "$clans_ok", 170f, 46f, 20);
        MakeButton(buttons.transform, "Cancel", "$clans_cancel", 170f, 46f, 20);

        SavePrefab(root, Root + "/Clans_UI_Confirm.prefab");
    }

    // Used inside the Create page and on its own (with Save/Cancel) when a clan changes its emblem. Laid out
    // top to bottom, the two picture grids sharing whatever height the host gives it.
    private static void BuildEmblemEditor() {
        GameObject root = NewUI("Clans_UI_EmblemEditor", null);
        Stretch(root);
        VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        GameObject previewRow = NewUI("PreviewRow", root.transform);
        Prefer(previewRow, 0f, 104f);
        GameObject preview = MakeEmblem(previewRow.transform, "Preview", 100f);
        Place(preview, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-50f, -50f), new Vector2(50f, 50f));

        Prefer(AddText(root.transform, "BackLabel", "$clans_emblem_background", 17, TitleColor, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 24f);
        MakeGrid(root.transform, "Backgrounds", 48f, 60f, 1f, iconCells: true);
        MakeGrid(root.transform, "BackColors", 24f, 62f, 0f, iconCells: false);
        Prefer(AddText(root.transform, "FrontLabel", "$clans_emblem_symbol", 17, TitleColor, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 24f);
        MakeGrid(root.transform, "Symbols", 48f, 110f, 2f, iconCells: true);
        MakeGrid(root.transform, "FrontColors", 24f, 62f, 0f, iconCells: false);

        GameObject buttons = NewUI("Buttons", root.transform);
        Prefer(buttons, 0f, 42f);
        HorizontalLayoutGroup row = buttons.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 16f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        MakeButton(buttons.transform, "Save", "$clans_save", 150f, 40f, 17);
        MakeButton(buttons.transform, "Cancel", "$clans_cancel", 150f, 40f, 17);

        SavePrefab(root, Root + "/Clans_UI_EmblemEditor.prefab");
    }

    // A scrolling grid whose content holds an inactive Item template: a Button with an Icon image and a
    // Selected frame (iconCells), or a colour Swatch with a Selected frame.
    // Selected is a gold square drawn behind the tile's Back and a little larger than it, so only a rim
    // shows around the tile and the icon or swatch stays fully visible.
    private static void MakeGrid(Transform parent, string name, float cell, float minHeight, float flexibleHeight, bool iconCells) {
        GameObject holder = NewUI(name, parent);
        Prefer(holder, 0f, minHeight, flexibleHeight: flexibleHeight);
        Transform content = MakeScrollList(holder, grid: true, cell: new Vector2(cell, cell));

        GameObject item = NewUI("Item", content);
        GameObject selected = NewUI("Selected", item.transform);
        Stretch(selected, -3f);
        Image selectedImage = selected.AddComponent<Image>();
        selectedImage.color = Gold;
        selectedImage.raycastTarget = false;
        selected.SetActive(false);

        GameObject backObject = NewUI("Back", item.transform);
        Stretch(backObject);
        Image back = backObject.AddComponent<Image>();
        back.sprite = Sprite(Placeholders + "text_field.png");
        back.type = Image.Type.Sliced;
        Button button = item.AddComponent<Button>();
        button.targetGraphic = back;
        if (iconCells) {
            GameObject icon = NewUI("Icon", item.transform);
            Stretch(icon, 6f);
            Image iconImage = icon.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
        } else {
            GameObject swatch = NewUI("Swatch", item.transform);
            Stretch(swatch, 3f);
            Image swatchImage = swatch.AddComponent<Image>();
            swatchImage.raycastTarget = false;
        }
        item.SetActive(false);
    }

    // ------------------------------------------------------------------------------------------------
    //  Pages
    // ------------------------------------------------------------------------------------------------

    private static void BuildNoClanPage(Transform page) {
        TextMeshProUGUI intro = AddText(page, "Intro", "$clans_noclan_intro", 18, Body, TextAlignmentOptions.Center);
        Place(intro.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -70f), new Vector2(-40f, -10f));

        MakeTile(page, "Tile_Create", "$clans_tile_create", "$clans_tile_create_body", "clans_button.png", new Vector2(0.27f, 0.5f));
        MakeTile(page, "Tile_Join", "$clans_tile_join", "$clans_tile_join_body", "tile_hunt.png", new Vector2(0.73f, 0.5f));

        TextMeshProUGUI pending = AddText(page, "Pending", "", 15, Grey, TextAlignmentOptions.Center);
        Place(pending.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(40f, 4f), new Vector2(-40f, 60f));
    }

    private static void MakeTile(Transform page, string name, string title, string body, string icon, Vector2 anchor) {
        GameObject tile = NewUI(name, page);
        RectTransform rt = (RectTransform)tile.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(400f, 330f);
        Image back = tile.AddComponent<Image>();
        back.sprite = Sprite(Placeholders + "button.png");
        back.type = Image.Type.Sliced;
        Button button = tile.AddComponent<Button>();
        button.targetGraphic = back;

        GameObject iconGo = NewUI("Icon", tile.transform);
        Place(iconGo, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-64f, -158f), new Vector2(64f, -30f));
        Image iconImage = iconGo.AddComponent<Image>();
        iconImage.sprite = Sprite(Icons + icon);
        iconImage.preserveAspect = true;
        iconImage.color = TitleColor;
        iconImage.raycastTarget = false;

        TextMeshProUGUI titleText = AddText(tile.transform, "Title", title, 26, TitleColor, TextAlignmentOptions.Center);
        Place(titleText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -206f), new Vector2(-16f, -166f));
        TextMeshProUGUI bodyText = AddText(tile.transform, "Body", body, 16, Body, TextAlignmentOptions.Top);
        Place(bodyText.gameObject, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24f, 16f), new Vector2(-24f, -212f));
    }

    private static void BuildCreatePage(Transform page) {
        GameObject left = NewUI("Form", page);
        Place(left, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(10f, 0f), new Vector2(-20f, 0f));
        VerticalLayoutGroup layout = left.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI title = AddText(left.transform, "Title", "$clans_create_title", 30, TitleColor, TextAlignmentOptions.MidlineLeft);
        Prefer(title.gameObject, 0f, 40f);
        Prefer(AddText(left.transform, "NameLabel", "$clans_create_name", 16, Body, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 24f);
        MakeInputField(left.transform, "Name", 0f, 38f, TMP_InputField.ContentType.Standard);
        Prefer(AddText(left.transform, "DescriptionLabel", "$clans_create_description", 16, Body, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 24f);
        TMP_InputField description = MakeInputField(left.transform, "Description", 0f, 140f, TMP_InputField.ContentType.Standard);
        description.lineType = TMP_InputField.LineType.MultiLineNewline;
        description.textComponent.textWrappingMode = TextWrappingModes.Normal;
        description.textComponent.alignment = TextAlignmentOptions.TopLeft;

        GameObject recruitingRow = NewRow(left.transform, "RecruitingRow", RowH);
        MakeToggle(recruitingRow.transform, "Recruiting", 26f);
        AddToggleLabel(recruitingRow.transform, "Label", "$clans_create_recruiting", 16);

        TextMeshProUGUI fee = AddText(left.transform, "Fee", "Founding costs 1000 coins.", 16, TitleColor, TextAlignmentOptions.MidlineLeft);
        Prefer(fee.gameObject, 0f, 48f);

        GameObject spacer = NewUI("Spacer", left.transform);
        Prefer(spacer, 0f, 0f, flexibleHeight: 1f);

        GameObject buttons = NewRow(left.transform, "Buttons", 44f);
        MakeButton(buttons.transform, "Back", "$clans_back", 150f, 40f, 17);
        GameObject gap = NewUI("Gap", buttons.transform);
        Prefer(gap, 0f, 0f, flexibleWidth: 1f);
        MakeButton(buttons.transform, "Create", "$clans_create_button", 200f, 40f, 17);

        // The emblem editor is instantiated in here.
        GameObject host = NewUI("EmblemHost", page);
        Place(host, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(20f, 0f), new Vector2(-10f, 0f));
        Image well = host.AddComponent<Image>();
        well.sprite = Sprite(Placeholders + "item_background.png");
        well.type = Image.Type.Sliced;
        well.color = Well;
    }

    private static void BuildJoinPage(Transform page) {
        TMP_InputField search = MakeInputField(page, "Search", 320f, 36f, TMP_InputField.ContentType.Standard);
        RemoveLayoutElement(search);
        Place(search.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -36f), new Vector2(320f, 0f));
        SetPlaceholder(search, "$clans_join_search");

        TextMeshProUGUI hint = AddText(page, "Hint", "$clans_join_hint", 15, Grey, TextAlignmentOptions.MidlineLeft);
        Place(hint.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(340f, -36f), new Vector2(-180f, 0f));

        Button back = MakeButton(page, "Back", "$clans_back", 150f, 36f, 16);
        RemoveLayoutElement(back);
        Place(back.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-150f, -36f), new Vector2(0f, 0f));

        GameObject list = NewUI("List", page);
        Place(list, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, -46f));
        Transform content = MakeScrollList(list);

        GameObject item = NewRow(content, "Item", 84f);
        Image itemBack = item.AddComponent<Image>();
        ShadeRow(itemBack);
        HorizontalLayoutGroup row = item.GetComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(10, 10, 6, 6);
        row.spacing = 12f;
        GameObject emblem = MakeEmblem(item.transform, "Emblem", 68f);
        Prefer(emblem, 68f, 68f);
        GameObject texts = NewUI("Texts", item.transform);
        Prefer(texts, 0f, 72f, flexibleWidth: 1f);
        VerticalLayoutGroup textLayout = texts.AddComponent<VerticalLayoutGroup>();
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;
        Prefer(AddName(texts.transform, "Name", "Clan name", 20, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 26f);
        Prefer(AddText(texts.transform, "Info", "Level 1 · 3 members · led by Someone", 14, Body, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 20f);
        Prefer(AddText(texts.transform, "Description", "Description", 13, Grey, TextAlignmentOptions.TopLeft).gameObject, 0f, 22f);
        AddLabel(item.transform, "State", "", 15, TitleColor, flexible: false, preferredWidth: 120f);
        MakeButton(item.transform, "Apply", "$clans_join_apply", 130f, 38f, 16);
        MakeButton(item.transform, "Withdraw", "$clans_join_withdraw", 130f, 38f, 16);
        item.SetActive(false);

        TextMeshProUGUI empty = AddText(page, "Empty", "$clans_join_empty", 17, Grey, TextAlignmentOptions.Center);
        Place(empty.gameObject, new Vector2(0f, 0.4f), new Vector2(1f, 0.6f), Vector2.zero, Vector2.zero);
    }

    private static void BuildOverviewPage(Transform page) {
        // Left: emblem, identity, status, treasury, membership buttons.
        GameObject left = NewUI("Left", page);
        Place(left, new Vector2(0f, 0f), new Vector2(0.31f, 1f), Vector2.zero, new Vector2(-10f, 0f));
        VerticalLayoutGroup layout = left.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        GameObject emblemRow = NewUI("EmblemRow", left.transform);
        Prefer(emblemRow, 0f, 110f);
        GameObject emblem = MakeEmblem(emblemRow.transform, "Emblem", 104f);
        Place(emblem, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-52f, -52f), new Vector2(52f, 52f));
        Button editEmblem = MakeButton(left.transform, "EditEmblem", "$clans_overview_edit_emblem", 0f, 38f, 18);
        Prefer(editEmblem.gameObject, 0f, 38f);
        Prefer(AddText(left.transform, "Description", "Description", 15, Body, TextAlignmentOptions.TopLeft).gameObject, 0f, 48f);
        Prefer(AddText(left.transform, "StatusHeader", "$clans_overview_status", 22, TitleColor, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 26f);
        Prefer(AddText(left.transform, "Status", "No status set.", 15, Body, TextAlignmentOptions.TopLeft).gameObject, 0f, 32f);
        GameObject statusButtons = NewRow(left.transform, "StatusButtons", 38f);
        MakeButton(statusButtons.transform, "EditStatus", "$clans_overview_edit_status", 0f, 36f, 17).GetComponent<LayoutElement>().flexibleWidth = 1f;
        MakeButton(statusButtons.transform, "PostInvite", "$clans_chat_post_clan_invite", 0f, 36f, 17).GetComponent<LayoutElement>().flexibleWidth = 1f;
        GameObject recruitingRow = NewRow(left.transform, "RecruitingRow", 30f);
        MakeToggle(recruitingRow.transform, "Recruiting", 24f);
        AddToggleLabel(recruitingRow.transform, "Label", "$clans_create_recruiting", 14);
        Prefer(AddText(left.transform, "Treasury", "Treasury: 0 coins", 17, TitleColor, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 26f);
        GameObject treasury = NewRow(left.transform, "TreasuryButtons", 38f);
        MakeButton(treasury.transform, "Donate", "$clans_overview_donate", 0f, 36f, 17).GetComponent<LayoutElement>().flexibleWidth = 1f;
        MakeButton(treasury.transform, "Withdraw", "$clans_overview_withdraw", 0f, 36f, 17).GetComponent<LayoutElement>().flexibleWidth = 1f;
        GameObject spacer = NewUI("Spacer", left.transform);
        Prefer(spacer, 0f, 0f, flexibleHeight: 1f);
        GameObject membership = NewRow(left.transform, "MembershipButtons", 40f);
        MakeButton(membership.transform, "Leave", "$clans_overview_leave", 0f, 38f, 17).GetComponent<LayoutElement>().flexibleWidth = 1f;
        MakeButton(membership.transform, "Disband", "$clans_overview_disband", 0f, 38f, 17).GetComponent<LayoutElement>().flexibleWidth = 1f;

        // Middle: recent activity.
        GameObject middle = NewUI("Middle", page);
        Place(middle, new Vector2(0.31f, 0f), new Vector2(0.65f, 1f), new Vector2(10f, 0f), new Vector2(-10f, 0f));
        TextMeshProUGUI logHeader = AddText(middle.transform, "LogHeader", "$clans_overview_activity", 22, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(logHeader.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -28f), Vector2.zero);
        GameObject log = NewUI("Log", middle.transform);
        Place(log, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -32f));
        Transform logContent = MakeScrollList(log);
        GameObject logItem = NewRow(logContent, "Item", 0f);
        logItem.GetComponent<LayoutElement>().minHeight = 22f;
        logItem.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
        AddLabel(logItem.transform, "Time", "2d", 13, Grey, flexible: false, preferredWidth: 46f);
        TextMeshProUGUI logText = AddLabel(logItem.transform, "Text", "Something happened.", 14, Body, flexible: true);
        logText.overflowMode = TextOverflowModes.Overflow;
        logItem.SetActive(false);

        // Right: ranks above, applications below.
        GameObject right = NewUI("Right", page);
        Place(right, new Vector2(0.65f, 0f), new Vector2(1f, 1f), new Vector2(10f, 0f), Vector2.zero);
        TextMeshProUGUI ranksHeader = AddText(right.transform, "RanksHeader", "$clans_overview_ranks", 22, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(ranksHeader.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -28f), Vector2.zero);
        GameObject ranks = NewUI("Ranks", right.transform);
        Place(ranks, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(0f, 6f), new Vector2(0f, -32f));
        Transform ranksContent = MakeScrollList(ranks);
        GameObject rankItem = NewUI("Item", ranksContent);
        VerticalLayoutGroup rankLayout = rankItem.AddComponent<VerticalLayoutGroup>();
        rankLayout.padding = new RectOffset(4, 4, 2, 4);
        rankLayout.childControlWidth = true;
        rankLayout.childControlHeight = true;
        rankLayout.childForceExpandWidth = true;
        rankLayout.childForceExpandHeight = false;
        Prefer(AddName(rankItem.transform, "Name", "Leader", 16, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 22f);
        TextMeshProUGUI perms = AddText(rankItem.transform, "Perms", "Everything", 12, Body, TextAlignmentOptions.TopLeft);
        perms.overflowMode = TextOverflowModes.Overflow;
        rankItem.SetActive(false);

        TextMeshProUGUI appsHeader = AddText(right.transform, "ApplicationsHeader", "$clans_overview_applications", 22, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(appsHeader.gameObject, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -30f), new Vector2(0f, -2f));
        GameObject apps = NewUI("Applications", right.transform);
        Place(apps, Vector2.zero, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, -34f));
        Transform appsContent = MakeScrollList(apps);
        GameObject appItem = NewUI("Item", appsContent);
        Prefer(appItem, 0f, 64f);
        Image appBack = appItem.AddComponent<Image>();
        ShadeRow(appBack);
        TextMeshProUGUI appName = AddName(appItem.transform, "Name", "Applicant", 16, TextAlignmentOptions.MidlineLeft);
        Place(appName.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(8f, -26f), new Vector2(-150f, -2f));
        TextMeshProUGUI appMessage = AddText(appItem.transform, "Message", "Message", 12, Body, TextAlignmentOptions.TopLeft);
        Place(appMessage.gameObject, Vector2.zero, new Vector2(1f, 1f), new Vector2(8f, 4f), new Vector2(-150f, -26f));
        Button accept = MakeButton(appItem.transform, "Accept", "$clans_accept", 66f, 32f, 16);
        RemoveLayoutElement(accept);
        Place(accept.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-144f, -15f), new Vector2(-74f, 15f));
        Button decline = MakeButton(appItem.transform, "Decline", "$clans_decline", 66f, 32f, 16);
        RemoveLayoutElement(decline);
        Place(decline.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-70f, -15f), new Vector2(-4f, 15f));
        appItem.SetActive(false);
        TextMeshProUGUI noApps = AddText(apps.transform, "None", "$clans_overview_no_applications", 14, Grey, TextAlignmentOptions.Center);
        Place(noApps.gameObject, new Vector2(0f, 0.4f), new Vector2(1f, 0.6f), Vector2.zero, Vector2.zero);
    }

    private static void BuildMembersPage(Transform page) {
        TextMeshProUGUI count = AddText(page, "Count", "12 / 15 members", 17, Body, TextAlignmentOptions.MidlineLeft);
        Place(count.gameObject, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), Vector2.zero);
        Button editRanks = MakeButton(page, "EditRanks", "$clans_members_edit_ranks", 180f, 36f, 16);
        RemoveLayoutElement(editRanks);
        Place(editRanks.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-180f, -36f), Vector2.zero);

        // Room for all four row buttons, kept in every row so the columns stay put whichever buttons show.
        float actionsWidth = 4 * 84f + 3 * 6f;

        // Column headings. The padding puts them over a row's columns: on the left the list's inset (4 + 2), the
        // row's padding (8) and the online dot (14 + 6); on the right the scrollbar's gap (18 + 2) and the row's
        // padding (8), less the 14 the headings already stop short.
        GameObject heads = NewRow(page, "Headings", 24f);
        Place(heads, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -68f), new Vector2(-14f, -44f));
        heads.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(34, 14, 0, 0);
        AddLabel(heads.transform, "Name", "$clans_members_name", 14, TitleColor, flexible: true);
        AddLabel(heads.transform, "Rank", "$clans_members_rank", 14, TitleColor, flexible: false, preferredWidth: 150f);
        AddLabel(heads.transform, "LastSeen", "$clans_members_last_seen", 14, TitleColor, flexible: false, preferredWidth: 130f);
        AddLabel(heads.transform, "Actions", "", 14, TitleColor, flexible: false, preferredWidth: actionsWidth);
        foreach (TextMeshProUGUI heading in heads.GetComponentsInChildren<TextMeshProUGUI>(true)) { heading.fontStyle = FontStyles.Bold; }

        GameObject list = NewUI("List", page);
        Place(list, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -72f));
        Transform content = MakeScrollList(list);
        GameObject item = NewRow(content, "Item", 44f);
        Image itemBack = item.AddComponent<Image>();
        ShadeRow(itemBack);
        item.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(8, 8, 2, 2);
        GameObject online = NewUI("Online", item.transform);
        Prefer(online, 14f, 14f);
        Image dot = online.AddComponent<Image>();
        dot.color = new Color(0.4f, 0.85f, 0.35f, 1f);
        AddNameLabel(item.transform, "Name", "Member", 16);
        AddLabel(item.transform, "Rank", "Rank", 15, Body, flexible: false, preferredWidth: 150f);
        AddLabel(item.transform, "LastSeen", "online", 14, Grey, flexible: false, preferredWidth: 130f);
        // The buttons a row shows (your own shows none) line up on the right of a fixed-width column.
        GameObject actions = NewRow(item.transform, "Actions", 0f);
        Prefer(actions, actionsWidth, 0f);
        HorizontalLayoutGroup actionsLayout = actions.GetComponent<HorizontalLayoutGroup>();
        actionsLayout.padding = new RectOffset(0, 0, 0, 0);
        actionsLayout.childAlignment = TextAnchor.MiddleRight;
        MakeButton(actions.transform, "Promote", "$clans_members_promote", 84f, 34f, 16);
        MakeButton(actions.transform, "Demote", "$clans_members_demote", 84f, 34f, 16);
        MakeButton(actions.transform, "Kick", "$clans_members_kick", 84f, 34f, 16);
        MakeButton(actions.transform, "Transfer", "$clans_members_transfer", 84f, 34f, 16);
        item.SetActive(false);

        BuildRankEditor(page);
    }

    // Covers the member list while the leader edits ranks.
    private static void BuildRankEditor(Transform page) {
        GameObject editor = NewUI("RankEditor", page);
        Stretch(editor);
        editor.AddComponent<Image>().color = Color.clear;
        AddPanelBackground(editor.transform, "woodpanel_512x512.png", 0f, darken: false);

        TextMeshProUGUI title = AddText(editor.transform, "Title", "$clans_ranks_title", 20, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(title.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -42f), new Vector2(-16f, -8f));

        GameObject list = NewUI("Ranks", editor.transform);
        Place(list, Vector2.zero, Vector2.one, new Vector2(12f, 56f), new Vector2(-12f, -46f));
        Transform content = MakeScrollList(list);

        GameObject item = NewUI("Item", content);
        Image itemBack = item.AddComponent<Image>();
        ShadeRow(itemBack);
        VerticalLayoutGroup layout = item.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 6, 8);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        GameObject head = NewRow(item.transform, "Head", RowH);
        TMP_InputField name = MakeInputField(head.transform, "Name", 260f, 30f, TMP_InputField.ContentType.Standard);
        name.characterLimit = 24;
        AddLabel(head.transform, "Note", "", 13, Grey, flexible: true);
        MakeButton(head.transform, "Up", "$clans_ranks_up", 70f, 32f, 16);
        MakeButton(head.transform, "Down", "$clans_ranks_down", 70f, 32f, 16);
        MakeButton(head.transform, "Remove", "$clans_ranks_remove", 90f, 32f, 16);

        GameObject flags = NewUI("Perms", item.transform);
        GridLayoutGroup grid = flags.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(310f, 24f);
        grid.spacing = new Vector2(6f, 2f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        GameObject flag = NewUI("Flag", flags.transform);
        HorizontalLayoutGroup flagRow = flag.AddComponent<HorizontalLayoutGroup>();
        flagRow.spacing = 6f;
        flagRow.childAlignment = TextAnchor.MiddleLeft;
        flagRow.childControlWidth = true;
        flagRow.childControlHeight = true;
        flagRow.childForceExpandWidth = false;
        flagRow.childForceExpandHeight = false;
        MakeToggle(flag.transform, "Toggle", 20f);
        AddToggleLabel(flag.transform, "Label", "Permission", 13);
        flag.SetActive(false);
        item.SetActive(false);

        GameObject buttons = NewUI("Buttons", editor.transform);
        Place(buttons, Vector2.zero, new Vector2(1f, 0f), new Vector2(16f, 10f), new Vector2(-16f, 50f));
        HorizontalLayoutGroup row = buttons.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 14f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        MakeButton(buttons.transform, "Add", "$clans_ranks_add", 160f, 38f, 16);
        GameObject gap = NewUI("Gap", buttons.transform);
        Prefer(gap, 0f, 0f, flexibleWidth: 1f);
        MakeButton(buttons.transform, "Cancel", "$clans_cancel", 150f, 38f, 16);
        MakeButton(buttons.transform, "Save", "$clans_save", 150f, 38f, 16);

        editor.SetActive(false);
    }

    // Your squad and raid on the left; players to invite on the right.
    private static void BuildPartyPage(Transform page) {
        GameObject left = NewUI("Left", page);
        Place(left, Vector2.zero, new Vector2(0.48f, 1f), Vector2.zero, new Vector2(-10f, 0f));

        TextMeshProUGUI partyHeader = AddText(left.transform, "PartyHeader", "Your party (2/5)", 22, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(partyHeader.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -30f), Vector2.zero);

        GameObject members = NewUI("Members", left.transform);
        Place(members, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -250f), new Vector2(0f, -34f));
        Transform memberContent = MakeScrollList(members);
        GameObject member = NewRow(memberContent, "Item", 38f);
        Image memberBack = member.AddComponent<Image>();
        ShadeRow(memberBack);
        member.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(8, 6, 2, 2);
        GameObject online = NewUI("Online", member.transform);
        Prefer(online, 12f, 12f);
        online.AddComponent<Image>().color = new Color(0.4f, 0.85f, 0.35f, 1f);
        GameObject memberEmblem = MakeEmblem(member.transform, "Emblem", 26f);
        Prefer(memberEmblem, 26f, 26f);
        AddNameLabel(member.transform, "Name", "Member", 16);
        AddLabel(member.transform, "Role", "Leader", 13, Body, flexible: false, preferredWidth: 80f);
        MakeButton(member.transform, "Promote", "$clans_party_make_leader", 110f, 32f, 16);
        MakeButton(member.transform, "Kick", "$clans_party_kick", 70f, 32f, 16);
        member.SetActive(false);

        GameObject partyButtons = NewUI("PartyButtons", left.transform);
        Place(partyButtons, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -296f), new Vector2(0f, -258f));
        HorizontalLayoutGroup partyRow = partyButtons.AddComponent<HorizontalLayoutGroup>();
        partyRow.spacing = 10f;
        partyRow.childControlWidth = true;
        partyRow.childControlHeight = true;
        partyRow.childForceExpandWidth = false;
        MakeButton(partyButtons.transform, "Create", "$clans_party_create", 170f, 38f, 18);
        MakeButton(partyButtons.transform, "Leave", "$clans_party_leave", 170f, 38f, 18);
        MakeButton(partyButtons.transform, "PostInvite", "$clans_chat_post_party_invite", 200f, 38f, 18);

        TextMeshProUGUI raidHeader = AddText(left.transform, "RaidHeader", "Raid (2/4 squads)", 20, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(raidHeader.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -334f), new Vector2(-180f, -304f));
        Button leaveRaid = MakeButton(left.transform, "LeaveRaid", "$clans_raid_leave", 170f, 32f, 16);
        RemoveLayoutElement(leaveRaid);
        Place(leaveRaid.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-170f, -334f), new Vector2(0f, -304f));

        GameObject squads = NewUI("Squads", left.transform);
        Place(squads, Vector2.zero, new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, -340f));
        Transform squadContent = MakeScrollList(squads);
        GameObject squad = NewUI("Item", squadContent);
        Image squadBack = squad.AddComponent<Image>();
        ShadeRow(squadBack);
        VerticalLayoutGroup squadLayout = squad.AddComponent<VerticalLayoutGroup>();
        squadLayout.padding = new RectOffset(8, 8, 4, 6);
        squadLayout.spacing = 2f;
        squadLayout.childControlWidth = true;
        squadLayout.childControlHeight = true;
        squadLayout.childForceExpandWidth = true;
        squadLayout.childForceExpandHeight = false;
        GameObject squadHead = NewRow(squad.transform, "Head", 30f);
        AddLabel(squadHead.transform, "Title", "Squad 1 - led by Someone", 15, TitleColor, flexible: true);
        MakeButton(squadHead.transform, "MakeRaidLeader", "$clans_raid_make_leader", 150f, 28f, 15);
        Prefer(AddText(squad.transform, "Members", "Name, Name, Name", 13, Body, TextAlignmentOptions.TopLeft).gameObject, 0f, 20f);
        squad.SetActive(false);

        // Right: who to invite.
        GameObject right = NewUI("Right", page);
        Place(right, new Vector2(0.48f, 0f), Vector2.one, new Vector2(10f, 0f), Vector2.zero);
        GameObject filters = NewUI("Filters", right.transform);
        Place(filters, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -36f), Vector2.zero);
        HorizontalLayoutGroup filterRow = filters.AddComponent<HorizontalLayoutGroup>();
        filterRow.spacing = 6f;
        filterRow.childControlWidth = true;
        filterRow.childControlHeight = true;
        filterRow.childForceExpandWidth = false;
        foreach ((string name, string text) in new[] { ("Nearby", "$clans_party_nearby"), ("Clanmates", "$clans_party_clanmates"), ("Everyone", "$clans_party_everyone") }) {
            MakeTab(filters.transform, name, text, 140f, 34f);
        }
        TMP_InputField search = MakeInputField(right.transform, "Search", 0f, 32f, TMP_InputField.ContentType.Standard);
        RemoveLayoutElement(search);
        Place(search.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -76f), new Vector2(0f, -44f));
        SetPlaceholder(search, "$clans_party_search");

        GameObject players = NewUI("Players", right.transform);
        Place(players, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -84f));
        Transform playerContent = MakeScrollList(players);
        GameObject player = NewRow(playerContent, "Item", 44f);
        Image playerBack = player.AddComponent<Image>();
        ShadeRow(playerBack);
        player.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(8, 6, 2, 2);
        GameObject playerEmblem = MakeEmblem(player.transform, "Emblem", 30f);
        Prefer(playerEmblem, 30f, 30f);
        GameObject playerTexts = NewUI("Texts", player.transform);
        Prefer(playerTexts, 0f, 40f, flexibleWidth: 1f);
        VerticalLayoutGroup textsLayout = playerTexts.AddComponent<VerticalLayoutGroup>();
        textsLayout.childControlWidth = true;
        textsLayout.childControlHeight = true;
        textsLayout.childForceExpandWidth = true;
        textsLayout.childForceExpandHeight = false;
        Prefer(AddName(playerTexts.transform, "Name", "Player", 16, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 22f);
        Prefer(AddText(playerTexts.transform, "Info", "12 m - Clan name", 12, Body, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 16f);
        MakeButton(player.transform, "Invite", "$clans_party_invite", 90f, 34f, 16);
        MakeButton(player.transform, "InviteRaid", "$clans_raid_invite", 110f, 34f, 16);
        player.SetActive(false);
        TextMeshProUGUI empty = AddText(players.transform, "Empty", "$clans_party_nobody", 15, Grey, TextAlignmentOptions.Center);
        Place(empty.gameObject, new Vector2(0f, 0.4f), new Vector2(1f, 0.6f), Vector2.zero, Vector2.zero);
    }

    // The left-side list of your squad (and, in a raid, the other squads), on the HUD.
    private static void BuildPartyFrame() {
        GameObject root = NewUI("Clans_HUD_PartyFrame", null);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(16f, 140f);
        rect.sizeDelta = new Vector2(240f, 0f);
        VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Prefer(AddText(root.transform, "Title", "Party", 15, TitleColor, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 20f);

        GameObject members = NewUI("Members", root.transform);
        VerticalLayoutGroup membersLayout = members.AddComponent<VerticalLayoutGroup>();
        membersLayout.spacing = 3f;
        membersLayout.childControlWidth = true;
        membersLayout.childControlHeight = true;
        membersLayout.childForceExpandWidth = true;
        membersLayout.childForceExpandHeight = false;
        GameObject member = NewRow(members.transform, "Member", 36f);
        Image back = member.AddComponent<Image>();
        ShadeRow(back);
        member.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(4, 6, 2, 2);
        member.AddComponent<CanvasGroup>();
        GameObject emblem = MakeEmblem(member.transform, "Emblem", 26f);
        Prefer(emblem, 26f, 26f);
        GameObject texts = NewUI("Texts", member.transform);
        Prefer(texts, 0f, 32f, flexibleWidth: 1f);
        VerticalLayoutGroup textsLayout = texts.AddComponent<VerticalLayoutGroup>();
        textsLayout.spacing = 2f;
        textsLayout.childControlWidth = true;
        textsLayout.childControlHeight = true;
        textsLayout.childForceExpandWidth = true;
        textsLayout.childForceExpandHeight = false;
        Prefer(AddName(texts.transform, "Name", "Member", 14, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 18f);
        MakeBar(texts.transform, "Health", 8f, new Color(0.8f, 0.15f, 0.12f, 1f));
        GameObject leader = NewUI("Leader", member.transform);
        Prefer(leader, 16f, 16f);
        Image crown = leader.AddComponent<Image>();
        crown.sprite = Sprite(Icons + "tile_worldboss.png");
        crown.color = TitleColor;
        crown.preserveAspect = true;
        crown.raycastTarget = false;
        AddLabel(member.transform, "Distance", "35m", 12, Body, flexible: false, preferredWidth: 40f).alignment = TextAlignmentOptions.MidlineRight;
        member.SetActive(false);

        Prefer(AddText(root.transform, "RaidTitle", "Raid", 13, TitleColor, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 18f);
        GameObject raid = NewUI("Raid", root.transform);
        VerticalLayoutGroup raidLayout = raid.AddComponent<VerticalLayoutGroup>();
        raidLayout.spacing = 1f;
        raidLayout.childControlWidth = true;
        raidLayout.childControlHeight = true;
        raidLayout.childForceExpandWidth = true;
        raidLayout.childForceExpandHeight = false;
        GameObject item = NewRow(raid.transform, "Item", 16f);
        item.AddComponent<CanvasGroup>();
        AddLabel(item.transform, "Name", "2 Name", 12, Body, flexible: true);
        GameObject barHolder = NewUI("HealthHolder", item.transform);
        Prefer(barHolder, 70f, 6f);
        MakeBar(barHolder.transform, "Health", 6f, new Color(0.8f, 0.15f, 0.12f, 1f));
        item.SetActive(false);

        SavePrefab(root, HudDir + "/Clans_HUD_PartyFrame.prefab");
    }

    // A health-style bar: dark back, filled Fill.
    private static void MakeBar(Transform parent, string name, float height, Color color) {
        GameObject bar = NewUI(name, parent);
        Prefer(bar, 0f, height);
        if (parent.GetComponent<LayoutGroup>() == null) { Stretch(bar); }
        Image back = bar.AddComponent<Image>();
        back.color = BarBack;
        back.raycastTarget = false;
        GameObject fill = NewUI("Fill", bar.transform);
        Stretch(fill, 1f);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.sprite = Sprite(Placeholders + "bar_monster_hp_20.png");
        fillImage.color = color;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillAmount = 0.7f;
        fillImage.raycastTarget = false;
    }

    // A party invitation, on the HUD at the left.
    private static void BuildInviteToast() {
        GameObject root = NewUI("Clans_HUD_InviteToast", null);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = new Vector2(16f, 160f);
        rect.sizeDelta = new Vector2(320f, 84f);
        root.AddComponent<Image>().color = Color.clear;
        AddPanelBackground(root.transform, "woodpanel_password.png", 0f, darken: false);
        root.AddComponent<CanvasGroup>();

        GameObject icon = NewUI("Icon", root.transform);
        Place(icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, -24f), new Vector2(58f, 24f));
        Image iconImage = icon.AddComponent<Image>();
        iconImage.sprite = Sprite(Icons + "clans_button.png");
        iconImage.color = Orange;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        TextMeshProUGUI title = AddText(root.transform, "Title", "Party invite", 16, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(title.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(66f, -30f), new Vector2(-10f, -6f));
        TextMeshProUGUI body = AddText(root.transform, "Body", "Someone invites you to their party.", 13, Body, TextAlignmentOptions.MidlineLeft);
        Place(body.gameObject, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(66f, 28f), new Vector2(-10f, -30f));
        TextMeshProUGUI hint = AddText(root.transform, "Hint", "$clans_invite_hint", 12, Grey, TextAlignmentOptions.MidlineLeft);
        Place(hint.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(66f, 10f), new Vector2(-10f, 28f));
        GameObject timer = NewUI("Timer", root.transform);
        Place(timer, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(8f, 4f), new Vector2(-8f, 8f));
        MakeBar(timer.transform, "Bar", 4f, Orange);

        SavePrefab(root, HudDir + "/Clans_HUD_InviteToast.prefab");
    }

    // Invitations to answer, shown at the top of the inventory screen while any are waiting.
    private static void BuildInvitePanel() {
        GameObject root = NewUI("Clans_INV_InvitePanel", null);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -20f);
        rect.sizeDelta = new Vector2(460f, 0f);
        root.AddComponent<Image>().color = Color.clear;
        AddPanelBackground(root.transform, "woodpanel_512x512.png", 0f, darken: true);
        VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 14, 16);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Prefer(AddText(root.transform, "Title", "$clans_invite_panel_title", 17, TitleColor, TextAlignmentOptions.Center).gameObject, 0f, 24f);
        GameObject list = NewUI("List", root.transform);
        VerticalLayoutGroup listLayout = list.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 4f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;
        GameObject item = NewRow(list.transform, "Item", 36f);
        AddLabel(item.transform, "Text", "Someone invites you to their party (2/5)", 14, Body, flexible: true);
        MakeButton(item.transform, "Accept", "$clans_accept", 80f, 32f, 16);
        MakeButton(item.transform, "Decline", "$clans_decline", 80f, 32f, 16);
        item.SetActive(false);

        SavePrefab(root, HudDir + "/Clans_INV_InvitePanel.prefab");
    }

    // The activity tracker on the HUD: one block per tracked activity, stacked downward from the top-right
    // corner (the code puts that corner left of the minimap, under the status effects). Each block is a
    // heading (icon, name, time left) over its lines (a hunt target and its kills, the race's chests, the boss
    // fight's wave).
    private static void BuildTracker() {
        GameObject root = NewUI("Clans_HUD_Tracker", null);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-271f, -154f);
        rect.sizeDelta = new Vector2(220f, 0f);
        VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject block = NewUI("Activity", root.transform);
        Image back = block.AddComponent<Image>();
        ShadeRow(back);
        back.raycastTarget = false;
        VerticalLayoutGroup blockLayout = block.AddComponent<VerticalLayoutGroup>();
        blockLayout.padding = new RectOffset(8, 8, 5, 6);
        blockLayout.spacing = 1f;
        blockLayout.childControlWidth = true;
        blockLayout.childControlHeight = true;
        blockLayout.childForceExpandWidth = true;
        blockLayout.childForceExpandHeight = false;

        GameObject head = NewRow(block.transform, "Head", 24f);
        head.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(0, 0, 0, 0);
        GameObject icon = NewUI("Icon", head.transform);
        Prefer(icon, 18f, 18f);
        Image iconImage = icon.AddComponent<Image>();
        iconImage.sprite = Sprite(Icons + "tile_hunt.png");
        iconImage.color = TitleColor;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        AddLabel(head.transform, "Title", "Trophy Hunt", 17, TitleColor, flexible: true);
        AddLabel(head.transform, "Time", "4d 3h", 12, Grey, flexible: false, preferredWidth: 52f).alignment = TextAlignmentOptions.MidlineRight;

        GameObject lines = NewUI("Lines", block.transform);
        VerticalLayoutGroup linesLayout = lines.AddComponent<VerticalLayoutGroup>();
        linesLayout.spacing = 0f;
        linesLayout.childControlWidth = true;
        linesLayout.childControlHeight = true;
        linesLayout.childForceExpandWidth = true;
        linesLayout.childForceExpandHeight = false;
        GameObject line = NewRow(lines.transform, "Line", 19f);
        line.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(22, 0, 0, 0);
        AddLabel(line.transform, "Text", "Greydwarf", 14, Body, flexible: true);
        AddLabel(line.transform, "Count", "3/8", 14, Body, flexible: false, preferredWidth: 44f).alignment = TextAlignmentOptions.MidlineRight;
        line.SetActive(false);
        block.SetActive(false);

        SavePrefab(root, HudDir + "/Clans_HUD_Tracker.prefab");
    }

    // The tracker's menu in the inventory: a switch per activity under way. The code hangs it to the left of the
    // tracker button, by its top-right corner.
    private static void BuildTrackerMenu() {
        GameObject root = NewUI("Clans_INV_TrackerMenu", null);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(250f, 0f);
        root.AddComponent<Image>().color = Color.clear;
        AddPanelBackground(root.transform, "woodpanel_512x512.png", 0f, darken: true);
        VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 14);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Prefer(AddText(root.transform, "Title", "$clans_tracker_menu_title", 17, TitleColor, TextAlignmentOptions.Center).gameObject, 0f, 24f);
        GameObject list = NewUI("List", root.transform);
        VerticalLayoutGroup listLayout = list.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 4f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;
        GameObject item = NewRow(list.transform, "Item", RowH);
        MakeToggle(item.transform, "Toggle", 26f);
        AddToggleLabel(item.transform, "Label", "Trophy Hunt", 16);
        item.SetActive(false);

        SavePrefab(root, HudDir + "/Clans_INV_TrackerMenu.prefab");
    }

    // The HUD layout editor: an outline per piece of the HUD the player can move (the code clones the Outline
    // template per piece and lays each over its piece), and Done and Reset in the middle of the screen. The code
    // gives the root its own canvas, so it draws over the HUD.
    private static void BuildLayoutEditor() {
        GameObject root = NewUI("Clans_HUD_LayoutEditor", null);
        Stretch(root);
        // Catches clicks on empty space (they drop the selection), so they never reach the HUD below.
        root.AddComponent<Image>().color = Color.clear;

        GameObject outlines = NewUI("Outlines", root.transform);
        Stretch(outlines);
        GameObject outline = NewUI("Outline", outlines.transform);
        RectTransform outlineRect = (RectTransform)outline.transform;
        outlineRect.anchorMin = outlineRect.anchorMax = new Vector2(0.5f, 0.5f);
        outlineRect.pivot = Vector2.zero;
        outlineRect.sizeDelta = new Vector2(220f, 120f);
        // Only where the template sits in the previews; the code places every outline.
        outlineRect.anchoredPosition = new Vector2(-440f, 60f);
        GameObject fill = NewUI("Fill", outline.transform);
        Stretch(fill);
        fill.AddComponent<Image>().color = OutlineFill;
        GameObject border = NewUI("Border", outline.transform);
        Stretch(border);
        AddOutlineLine(border.transform, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), Vector2.zero);
        AddOutlineLine(border.transform, "Bottom", Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 2f));
        AddOutlineLine(border.transform, "Left", Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(2f, 0f));
        AddOutlineLine(border.transform, "Right", new Vector2(1f, 0f), Vector2.one, new Vector2(-2f, 0f), Vector2.zero);
        TextMeshProUGUI label = AddText(outline.transform, "Label", "$clans_layout_tracker", 14, OutlineText, TextAlignmentOptions.TopLeft);
        Stretch(label.gameObject, 6f);
        outline.SetActive(false);

        GameObject center = NewUI("Center", root.transform);
        RectTransform centerRect = (RectTransform)center.transform;
        centerRect.anchorMin = centerRect.anchorMax = centerRect.pivot = new Vector2(0.5f, 0.5f);
        centerRect.sizeDelta = new Vector2(320f, 0f);
        center.AddComponent<Image>().color = Color.clear;
        AddPanelBackground(center.transform, "woodpanel_512x512.png", 0f, darken: true);
        VerticalLayoutGroup layout = center.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 18);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        center.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        Prefer(AddText(center.transform, "Hint", "$clans_layout_hint", 16, Body, TextAlignmentOptions.Center).gameObject, 280f, 44f);
        MakeButton(center.transform, "Done", "$clans_layout_done", 220f, 46f, 20);
        MakeButton(center.transform, "Reset", "$clans_layout_reset", 170f, 34f, 16);

        SavePrefab(root, HudDir + "/Clans_HUD_LayoutEditor.prefab");
    }

    // One edge of a layout outline, 2 px thick.
    private static void AddOutlineLine(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) {
        GameObject line = NewUI(name, parent);
        RectTransform rect = (RectTransform)line.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        Image image = line.AddComponent<Image>();
        image.color = OutlineLine;
        image.raycastTarget = false;
    }

    // Every other clan, those at war with yours first, with what your rank can do about each.
    private static void BuildWarPage(Transform page) {
        TextMeshProUGUI summary = AddText(page, "Summary", "At war with 1 clan.", 18, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(summary.gameObject, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -32f), Vector2.zero);
        TextMeshProUGUI rules = AddText(page, "Rules", "Wars last 7 days. Kill targets from 5 to 100.", 14, Grey, TextAlignmentOptions.MidlineRight);
        Place(rules.gameObject, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(0f, -32f), Vector2.zero);

        GameObject list = NewUI("List", page);
        Place(list, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -40f));
        Transform content = MakeScrollList(list);
        GameObject item = NewRow(content, "Item", 84f);
        Image back = item.AddComponent<Image>();
        ShadeRow(back);
        HorizontalLayoutGroup row = item.GetComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(10, 10, 6, 6);
        row.spacing = 12f;
        GameObject emblem = MakeEmblem(item.transform, "Emblem", 64f);
        Prefer(emblem, 64f, 64f);
        GameObject texts = NewUI("Texts", item.transform);
        Prefer(texts, 0f, 72f, flexibleWidth: 1f);
        VerticalLayoutGroup textLayout = texts.AddComponent<VerticalLayoutGroup>();
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;
        Prefer(AddName(texts.transform, "Name", "Clan name", 20, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 26f);
        Prefer(AddText(texts.transform, "Info", "Level 3 - 12 members", 13, Body, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 18f);
        Prefer(AddText(texts.transform, "State", "At war: 3 - 5, first to 10. 4 days left.", 15, TitleColor, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 22f);
        GameObject buttons = NewUI("Buttons", item.transform);
        Prefer(buttons, 260f, 72f);
        GridLayoutGroup grid = buttons.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(126f, 32f);
        grid.spacing = new Vector2(6f, 6f);
        grid.childAlignment = TextAnchor.MiddleRight;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        foreach ((string name, string text) in new[] {
            ("Declare", "$clans_war_declare"), ("Accept", "$clans_war_accept"), ("Decline", "$clans_war_decline"),
            ("Cancel", "$clans_war_cancel"), ("OfferPeace", "$clans_war_offer_peace"), ("AcceptPeace", "$clans_war_accept_peace"),
            ("RejectPeace", "$clans_war_reject_peace") }) {
            MakeButton(buttons.transform, name, text, 126f, 32f, 16);
        }
        item.SetActive(false);

        TextMeshProUGUI empty = AddText(page, "Empty", "$clans_war_empty", 17, Grey, TextAlignmentOptions.Center);
        Place(empty.gameObject, new Vector2(0f, 0.4f), new Vector2(1f, 0.6f), Vector2.zero, Vector2.zero);
    }

    // The clan's storage tabs: open the ones bought, buy the next.
    private static void BuildStoragePage(Transform page) {
        TextMeshProUGUI treasury = AddText(page, "Treasury", "Treasury: 1200 coins", 18, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(treasury.gameObject, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), Vector2.zero);
        TextMeshProUGUI access = AddText(page, "Access", "You can put items in and take them out.", 14, Body, TextAlignmentOptions.MidlineRight);
        Place(access.gameObject, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(0f, -30f), Vector2.zero);
        TextMeshProUGUI note = AddText(page, "Note", "$clans_storage_note", 14, Grey, TextAlignmentOptions.MidlineLeft);
        Place(note.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -58f), new Vector2(0f, -32f));

        GameObject list = NewUI("List", page);
        Place(list, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -66f));
        Transform content = MakeScrollList(list);
        GameObject item = NewRow(content, "Item", 56f);
        Image back = item.AddComponent<Image>();
        ShadeRow(back);
        item.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(12, 10, 4, 4);
        GameObject texts = NewUI("Texts", item.transform);
        Prefer(texts, 0f, 48f, flexibleWidth: 1f);
        VerticalLayoutGroup textLayout = texts.AddComponent<VerticalLayoutGroup>();
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;
        Prefer(AddText(texts.transform, "Title", "Tab 1", 18, TitleColor, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 24f);
        Prefer(AddText(texts.transform, "Info", "20 slots", 13, Body, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 18f);
        MakeButton(item.transform, "Open", "$clans_storage_open", 130f, 36f, 15);
        MakeButton(item.transform, "Buy", "$clans_storage_buy", 130f, 36f, 15);
        item.SetActive(false);
    }

    // The weekly clan activities, one tile each: what it is, what it needs, how it is going, and the controls
    // to pick a difficulty (and boss) and start or cancel it.
    private static void BuildActivitiesPage(Transform page) {
        TextMeshProUGUI summary = AddText(page, "Summary", "The week resets in 4d 6h.", 18, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(summary.gameObject, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), Vector2.zero);
        TextMeshProUGUI note = AddText(page, "Note", "$clans_activities_note", 14, Grey, TextAlignmentOptions.MidlineRight);
        Place(note.gameObject, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(0f, -30f), Vector2.zero);

        GameObject list = NewUI("List", page);
        Place(list, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -38f));
        Transform content = MakeScrollList(list);
        GameObject tile = NewRow(content, "Tile", 182f);
        Image back = tile.AddComponent<Image>();
        ShadeRow(back);
        HorizontalLayoutGroup row = tile.GetComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(12, 12, 10, 10);
        row.spacing = 14f;
        row.childAlignment = TextAnchor.UpperLeft;

        GameObject icon = NewUI("Icon", tile.transform);
        Prefer(icon, 112f, 112f);
        Image iconImage = icon.AddComponent<Image>();
        iconImage.sprite = Sprite(Icons + "tile_hunt.png");
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        GameObject texts = NewUI("Texts", tile.transform);
        Prefer(texts, 0f, 162f, flexibleWidth: 1f);
        VerticalLayoutGroup textLayout = texts.AddComponent<VerticalLayoutGroup>();
        textLayout.spacing = 2f;
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;
        Prefer(AddText(texts.transform, "Title", "Trophy Hunt", 22, TitleColor, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 28f);
        Prefer(AddText(texts.transform, "Body", "Hunt the creatures the clan is given before the week ends.", 14, Body, TextAlignmentOptions.TopLeft).gameObject, 0f, 38f);
        Prefer(AddText(texts.transform, "Requirement", "Requires Star Level System, Epic Loot and Valheim Fortress.", 14, TitleColor, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 20f);
        Prefer(AddText(texts.transform, "Progress", "Boar 3/5 - Neck 1/5 - Eikthyr 0/1. 5d 2h left.", 15, Body, TextAlignmentOptions.TopLeft).gameObject, 0f, 40f);
        Prefer(AddText(texts.transform, "Lockout", "Done this week. Available again in 4d 6h.", 13, Grey, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 18f);

        GameObject controls = NewUI("Controls", tile.transform);
        Prefer(controls, 210f, 162f);
        VerticalLayoutGroup controlLayout = controls.AddComponent<VerticalLayoutGroup>();
        controlLayout.spacing = 6f;
        controlLayout.childAlignment = TextAnchor.UpperRight;
        controlLayout.childControlWidth = true;
        controlLayout.childControlHeight = true;
        controlLayout.childForceExpandWidth = true;
        controlLayout.childForceExpandHeight = false;
        MakeButton(controls.transform, "Difficulty", "$clans_difficulty_1", 210f, 34f, 16);
        MakeButton(controls.transform, "Boss", "Eikthyr", 210f, 34f, 16);
        MakeButton(controls.transform, "Start", "$clans_activity_start", 210f, 40f, 20);
        MakeButton(controls.transform, "Cancel", "$clans_activity_cancel", 210f, 34f, 16);
        tile.SetActive(false);

        TextMeshProUGUI empty = AddText(page, "Empty", "$clans_activities_none", 17, Grey, TextAlignmentOptions.Center);
        Place(empty.gameObject, new Vector2(0f, 0.4f), new Vector2(1f, 0.6f), Vector2.zero, Vector2.zero);
        empty.gameObject.SetActive(false);
    }

    // The clan's level, in three columns. Left: the current level and what the next one needs (experience bar, the
    // resources members hand in, treasury coins) with the upgrade button. Middle: what every level gives and costs.
    // Right: the guild feasts members can set out.
    private static void BuildLevelPage(Transform page) {
        GameObject left = NewUI("Left", page);
        Place(left, new Vector2(0f, 0f), new Vector2(0.34f, 1f), Vector2.zero, new Vector2(-10f, 0f));
        TextMeshProUGUI level = AddText(left.transform, "LevelHeader", "Level 3", 26, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(level.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -32f), Vector2.zero);
        TextMeshProUGUI summary = AddText(left.transform, "Summary", "Up to 15 members. Clanmates in a party share 30% of their skill gains within 60 m.", 14, Body, TextAlignmentOptions.TopLeft);
        Place(summary.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -74f), new Vector2(0f, -34f));

        GameObject xp = NewUI("XpBar", left.transform);
        Place(xp, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -100f), new Vector2(0f, -78f));
        Image xpBack = xp.AddComponent<Image>();
        xpBack.color = BarBack;
        xpBack.raycastTarget = false;
        GameObject fill = NewUI("Fill", xp.transform);
        Stretch(fill, 2f);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.sprite = Sprite(Placeholders + "bar_monster_hp_20.png");
        fillImage.color = Amber;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillAmount = 0.5f;
        fillImage.raycastTarget = false;
        TextMeshProUGUI xpText = AddText(xp.transform, "Text", "2600 / 5000 experience", 14, Color.white, TextAlignmentOptions.Center);
        Stretch(xpText.gameObject);

        TextMeshProUGUI resourcesHeader = AddText(left.transform, "ResourcesHeader", "To reach level 4", 22, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(resourcesHeader.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -136f), new Vector2(0f, -108f));
        GameObject list = NewUI("Resources", left.transform);
        Place(list, Vector2.zero, Vector2.one, new Vector2(0f, 128f), new Vector2(0f, -140f));
        Transform content = MakeScrollList(list);
        GameObject item = NewRow(content, "Item", 48f);
        Image back = item.AddComponent<Image>();
        ShadeRow(back);
        HorizontalLayoutGroup row = item.GetComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(6, 6, 4, 4);
        row.spacing = 8f;
        GameObject icon = NewUI("Icon", item.transform);
        Prefer(icon, 36f, 36f);
        Image iconImage = icon.AddComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconImage.sprite = Sprite(Icons + "tile_hunt.png");
        GameObject texts = NewUI("Texts", item.transform);
        Prefer(texts, 0f, 40f, flexibleWidth: 1f);
        VerticalLayoutGroup textLayout = texts.AddComponent<VerticalLayoutGroup>();
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;
        Prefer(AddName(texts.transform, "Name", "Iron", 15, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 20f);
        Prefer(AddText(texts.transform, "Count", "Handed in 35 / 50. You carry 12.", 12, Body, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 18f);
        MakeButton(item.transform, "Give", "$clans_level_give", 86f, 32f, 15);
        item.SetActive(false);

        TextMeshProUGUI requirements = AddText(left.transform, "Requirements", "Experience: 2600 / 5000\nTreasury: 1200 / 1000 coins", 14, Body, TextAlignmentOptions.TopLeft);
        Place(requirements.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 84f), new Vector2(0f, 124f));
        Button upgrade = MakeButton(left.transform, "Upgrade", "$clans_level_upgrade", 0f, 38f, 18);
        RemoveLayoutElement(upgrade);
        Place(upgrade.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 42f), new Vector2(0f, 80f));
        TextMeshProUGUI note = AddText(left.transform, "UpgradeNote", "Needs 2400 more experience.", 13, Grey, TextAlignmentOptions.TopLeft);
        Place(note.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 38f));

        // Middle: every level.
        GameObject middle = NewUI("Middle", page);
        Place(middle, new Vector2(0.34f, 0f), new Vector2(0.62f, 1f), new Vector2(10f, 0f), new Vector2(-10f, 0f));
        TextMeshProUGUI levelsHeader = AddText(middle.transform, "LevelsHeader", "$clans_level_levels", 22, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(levelsHeader.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -28f), Vector2.zero);
        GameObject levels = NewUI("Levels", middle.transform);
        Place(levels, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -32f));
        Transform levelsContent = MakeScrollList(levels);
        GameObject levelItem = NewUI("Item", levelsContent);
        Image levelBack = levelItem.AddComponent<Image>();
        ShadeRow(levelBack);
        VerticalLayoutGroup levelLayout = levelItem.AddComponent<VerticalLayoutGroup>();
        levelLayout.padding = new RectOffset(8, 6, 4, 6);
        levelLayout.childControlWidth = true;
        levelLayout.childControlHeight = true;
        levelLayout.childForceExpandWidth = true;
        levelLayout.childForceExpandHeight = false;
        Prefer(AddName(levelItem.transform, "Name", "Level 4 (next)", 16, TextAlignmentOptions.MidlineLeft).gameObject, 0f, 22f);
        TextMeshProUGUI perks = AddText(levelItem.transform, "Perks", "Up to 18 members. Clanmates in a party share 32% of their skill gains within 65 m.\nUnlocks storage tab 3, Guild Feast of the Mountains.\nNeeds 5000 experience, 1000 treasury coins and 100 Elder bark, 50 Iron.", 12, Body, TextAlignmentOptions.TopLeft);
        perks.overflowMode = TextOverflowModes.Overflow;
        levelItem.SetActive(false);

        // Right: guild feasts.
        GameObject right = NewUI("Right", page);
        Place(right, new Vector2(0.62f, 0f), new Vector2(1f, 1f), new Vector2(10f, 0f), Vector2.zero);
        TextMeshProUGUI feastsHeader = AddText(right.transform, "FeastsHeader", "$clans_level_feasts", 22, TitleColor, TextAlignmentOptions.MidlineLeft);
        Place(feastsHeader.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -28f), Vector2.zero);
        TextMeshProUGUI feastNote = AddText(right.transform, "FeastNote", "Set out right in front of you, for anyone to eat. It stands for 30 minutes and its food lasts 30 minutes.", 13, Grey, TextAlignmentOptions.TopLeft);
        Place(feastNote.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -70f), new Vector2(0f, -30f));
        GameObject feasts = NewUI("Feasts", right.transform);
        Place(feasts, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -74f));
        Transform feastsContent = MakeScrollList(feasts);
        GameObject feast = NewRow(feastsContent, "Item", 96f);
        Image feastBack = feast.AddComponent<Image>();
        ShadeRow(feastBack);
        HorizontalLayoutGroup feastRow = feast.GetComponent<HorizontalLayoutGroup>();
        feastRow.padding = new RectOffset(6, 6, 4, 4);
        feastRow.spacing = 8f;
        GameObject feastIcon = NewUI("Icon", feast.transform);
        Prefer(feastIcon, 40f, 40f);
        Image feastIconImage = feastIcon.AddComponent<Image>();
        feastIconImage.preserveAspect = true;
        feastIconImage.raycastTarget = false;
        feastIconImage.sprite = Sprite(Icons + "tile_hunt.png");
        GameObject feastTexts = NewUI("Texts", feast.transform);
        Prefer(feastTexts, 0f, 88f, flexibleWidth: 1f);
        VerticalLayoutGroup feastTextLayout = feastTexts.AddComponent<VerticalLayoutGroup>();
        feastTextLayout.childControlWidth = true;
        feastTextLayout.childControlHeight = true;
        feastTextLayout.childForceExpandWidth = true;
        feastTextLayout.childForceExpandHeight = false;
        // Long names (the Black Forest's) shrink to fit rather than lose their end.
        TextMeshProUGUI feastName = AddName(feastTexts.transform, "Name", "Guild Feast of the Black Forest", 14, TextAlignmentOptions.MidlineLeft);
        feastName.enableAutoSizing = true;
        feastName.fontSizeMin = 10f;
        feastName.fontSizeMax = 14f;
        Prefer(feastName.gameObject, 0f, 20f);
        Prefer(AddText(feastTexts.transform, "Info", "+75 health, +75 stamina, +38 eitr. 14 servings.", 12, Body, TextAlignmentOptions.TopLeft).gameObject, 0f, 32f);
        Prefer(AddText(feastTexts.transform, "Cost", "Costs 2 Plains feast, 2000 Coins.", 12, Body, TextAlignmentOptions.TopLeft).gameObject, 0f, 32f);
        // Paid by the member, or by the clan (shown only to ranks allowed to spend the clan's stock on feasts).
        GameObject feastButtons = NewUI("Buttons", feast.transform);
        Prefer(feastButtons, 84f, 70f);
        VerticalLayoutGroup buttonLayout = feastButtons.AddComponent<VerticalLayoutGroup>();
        buttonLayout.spacing = 4f;
        buttonLayout.childAlignment = TextAnchor.MiddleCenter;
        buttonLayout.childControlWidth = true;
        buttonLayout.childControlHeight = true;
        buttonLayout.childForceExpandWidth = true;
        buttonLayout.childForceExpandHeight = false;
        MakeButton(feastButtons.transform, "Buy", "$clans_feast_buy", 84f, 32f, 15);
        MakeButton(feastButtons.transform, "ClanBuy", "$clans_feast_clan_buy", 84f, 32f, 15);
        feast.SetActive(false);
    }

    // The Clans chat panel, in place of vanilla's chat window: channel tabs (and a drag handle) on top, lines
    // grouped under their sender, and a slot vanilla's own input field is moved into. Bottom-left by default;
    // players drag and resize it.
    private static void BuildChatPanel() {
        GameObject root = NewUI("Clans_HUD_Chat", null);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(10f, 110f);
        rect.sizeDelta = new Vector2(540f, 300f);
        root.AddComponent<CanvasGroup>();
        GameObject bkg = NewUI("Bkg", root.transform);
        Stretch(bkg);
        Image back = bkg.AddComponent<Image>();
        back.sprite = Sprite(Placeholders + "item_background.png");
        back.type = Image.Type.Sliced;
        back.color = new Color(0f, 0f, 0f, 0.6f);

        // The header is the drag handle; the tabs sit on it.
        GameObject header = NewUI("Header", root.transform);
        Place(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(4f, -32f), new Vector2(-4f, -4f));
        header.AddComponent<Image>().color = Color.clear;
        GameObject tabs = NewUI("Tabs", header.transform);
        Stretch(tabs);
        HorizontalLayoutGroup tabRow = tabs.AddComponent<HorizontalLayoutGroup>();
        tabRow.spacing = 3f;
        tabRow.childAlignment = TextAnchor.MiddleLeft;
        tabRow.childControlWidth = true;
        tabRow.childControlHeight = true;
        tabRow.childForceExpandWidth = false;
        tabRow.childForceExpandHeight = false;
        Button tab = MakeTab(tabs.transform, "Tab", "All", 84f, 26f);
        tab.GetComponentInChildren<TextMeshProUGUI>(true).fontSizeMax = 15f;
        GameObject unread = NewUI("Unread", tab.transform);
        Place(unread, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-10f, -10f), new Vector2(-3f, -3f));
        Image dot = unread.AddComponent<Image>();
        dot.color = Gold;
        dot.raycastTarget = false;
        unread.SetActive(false);
        tab.gameObject.SetActive(false);

        GameObject list = NewUI("List", root.transform);
        Place(list, Vector2.zero, Vector2.one, new Vector2(4f, 40f), new Vector2(-4f, -34f));
        Transform content = MakeScrollList(list);
        list.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
        VerticalLayoutGroup contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 3f;
        contentLayout.padding = new RectOffset(6, 6, 4, 4);

        // One sender's consecutive lines: a header (channel, name, time) and the lines below it.
        GameObject row = NewUI("Row", content);
        VerticalLayoutGroup rowLayout = row.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 0f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;
        GameObject head = NewRow(row.transform, "Head", 18f);
        head.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(0, 0, 0, 0);
        AddLabel(head.transform, "Channel", "[Clan]", 13, TitleColor, flexible: false).fontStyle = FontStyles.Bold;
        AddNameLabel(head.transform, "Name", "Name", 14);
        AddLabel(head.transform, "Time", "12:34", 12, Grey, flexible: false, preferredWidth: 44f).alignment = TextAlignmentOptions.MidlineRight;
        TextMeshProUGUI body = AddText(row.transform, "Body", "A line of chat.", 15, Body, TextAlignmentOptions.TopLeft);
        body.overflowMode = TextOverflowModes.Overflow;
        body.richText = true;
        row.SetActive(false);

        // Vanilla's input field is moved into InputSlot at runtime; the slot's own field look is only a
        // stand-in for the editor and is hidden then.
        GameObject inputRow = NewUI("InputRow", root.transform);
        Place(inputRow, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(4f, 4f), new Vector2(-22f, 36f));
        HorizontalLayoutGroup inputLayout = inputRow.AddComponent<HorizontalLayoutGroup>();
        inputLayout.spacing = 6f;
        inputLayout.childAlignment = TextAnchor.MiddleLeft;
        inputLayout.childControlWidth = true;
        inputLayout.childControlHeight = true;
        inputLayout.childForceExpandWidth = false;
        inputLayout.childForceExpandHeight = true;
        TextMeshProUGUI channel = AddLabel(inputRow.transform, "Channel", "Say", 14, Body, flexible: false, preferredWidth: 64f);
        channel.fontStyle = FontStyles.Bold;
        channel.alignment = TextAlignmentOptions.MidlineRight;
        GameObject slot = NewUI("InputSlot", inputRow.transform);
        Prefer(slot, 0f, 30f, flexibleWidth: 1f);
        Image slotImage = slot.AddComponent<Image>();
        slotImage.sprite = Sprite(Placeholders + "text_field.png");
        slotImage.type = Image.Type.Sliced;

        GameObject resize = NewUI("Resize", root.transform);
        Place(resize, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 4f), new Vector2(-4f, 18f));
        resize.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.3f);

        SavePrefab(root, HudDir + "/Clans_HUD_Chat.prefab");
    }

    // Tab buttons above vanilla's container panel while a clan storage tab is open there.
    private static void BuildStorageTabs() {
        GameObject root = NewUI("Clans_INV_StorageTabs", null);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 4f);
        rect.sizeDelta = new Vector2(0f, 34f);
        HorizontalLayoutGroup row = root.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 4f;
        row.childAlignment = TextAnchor.LowerLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        Button tab = MakeTab(root.transform, "Tab", "1", 56f, 32f);
        tab.gameObject.SetActive(false);
        SavePrefab(root, HudDir + "/Clans_INV_StorageTabs.prefab");
    }

    // ------------------------------------------------------------------------------------------------
    //  Widget factories
    // ------------------------------------------------------------------------------------------------

    // A clan emblem: a background image with a symbol image on top, both tinted by code.
    private static GameObject MakeEmblem(Transform parent, string name, float size) {
        GameObject emblem = NewUI(name, parent);
        ((RectTransform)emblem.transform).sizeDelta = new Vector2(size, size);
        GameObject back = NewUI("Back", emblem.transform);
        Stretch(back);
        Image backImage = back.AddComponent<Image>();
        backImage.preserveAspect = true;
        backImage.raycastTarget = false;
        backImage.sprite = Sprite("Assets/Clans/Bundle/Sprites/Emblems/bg_shield.png");
        backImage.color = new Color(0.29f, 0.2f, 0.13f, 1f);
        GameObject front = NewUI("Front", emblem.transform);
        Stretch(front, size * 0.2f);
        Image frontImage = front.AddComponent<Image>();
        frontImage.preserveAspect = true;
        frontImage.raycastTarget = false;
        frontImage.sprite = Sprite("Assets/Clans/Bundle/Sprites/Emblems/rune_algiz.png");
        frontImage.color = new Color(0.91f, 0.77f, 0.28f, 1f);
        return emblem;
    }

    private static GameObject NewUI(string name, Transform parent) {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        if (parent != null) { go.transform.SetParent(parent, false); }
        return go;
    }

    private static void Stretch(GameObject go, float inset = 0f) {
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    private static void Place(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) {
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static void Prefer(GameObject go, float width, float height, float flexibleWidth = 0f, float flexibleHeight = 0f) {
        // Not ??: in the editor a missing component comes back as a fake null that ?? treats as real.
        LayoutElement le = go.GetComponent<LayoutElement>();
        if (le == null) { le = go.AddComponent<LayoutElement>(); }
        if (width > 0f) { le.preferredWidth = width; le.minWidth = width; }
        if (height > 0f) { le.preferredHeight = height; le.minHeight = height; }
        le.flexibleWidth = flexibleWidth;
        le.flexibleHeight = flexibleHeight;
    }

    private static void RemoveLayoutElement(Component component) {
        LayoutElement le = component.GetComponent<LayoutElement>();
        if (le != null) { Object.DestroyImmediate(le); }
    }

    private static GameObject NewRow(Transform parent, string name, float height) {
        GameObject row = NewUI(name, parent);
        LayoutElement le = row.AddComponent<LayoutElement>();
        if (height > 0f) {
            le.preferredHeight = height;
            le.minHeight = height;
        }
        le.flexibleWidth = 1f;
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(4, 4, 0, 0);
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        return row;
    }

    private static TextMeshProUGUI AddText(Transform parent, string name, string text, int size, Color color, TextAlignmentOptions alignment) {
        GameObject go = NewUI(name, parent);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static TextMeshProUGUI AddLabel(Transform parent, string name, string text, int size, Color color, bool flexible, float preferredWidth = 0f) {
        TextMeshProUGUI tmp = AddText(parent, name, text, size, color, TextAlignmentOptions.MidlineLeft);
        LayoutElement le = tmp.gameObject.AddComponent<LayoutElement>();
        if (preferredWidth > 0f) { le.preferredWidth = preferredWidth; le.minWidth = preferredWidth; }
        if (flexible) { le.flexibleWidth = 1f; }
        return tmp;
    }

    // A vanilla panel: the rect itself is an invisible click blocker; the wood (Bkg) spills 10 px past it on
    // every side and a soft shadow (Darken) 50 px, as on the game's own panels.
    private static GameObject MakePanel(Transform parent, string name, float w, float h, string sprite = "woodpanel_trophys.png") {
        GameObject panel = NewUI(name, parent);
        RectTransform rt = (RectTransform)panel.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        Image img = panel.AddComponent<Image>();
        img.color = Color.clear;
        img.raycastTarget = true;
        AddPanelBackground(panel.transform, sprite, 10f, darken: true);
        return panel;
    }

    // Darken and Bkg children, first in order and ignored by layout groups.
    private static void AddPanelBackground(Transform panel, string sprite, float spill, bool darken) {
        GameObject bkg = NewUI("Bkg", panel);
        Stretch(bkg, -spill);
        bkg.transform.SetAsFirstSibling();
        bkg.AddComponent<LayoutElement>().ignoreLayout = true;
        Image wood = bkg.AddComponent<Image>();
        wood.sprite = Sprite(Placeholders + sprite);
        wood.type = Image.Type.Sliced;
        wood.raycastTarget = false;
        if (darken == false) { return; }
        GameObject shadow = NewUI("Darken", panel);
        Stretch(shadow, -50f);
        shadow.transform.SetAsFirstSibling();
        shadow.AddComponent<LayoutElement>().ignoreLayout = true;
        Image blob = shadow.AddComponent<Image>();
        blob.sprite = Sprite(Placeholders + "darken_blob.png");
        blob.raycastTarget = false;
    }

    // Vanilla's thin rule under tabs, across the panel at a distance from its top.
    private static void MakeSeparator(Transform parent, string name, float top) {
        GameObject line = NewUI(name, parent);
        Place(line, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, top - 2f), new Vector2(-16f, top));
        Image image = line.AddComponent<Image>();
        image.sprite = Sprite(Placeholders + "panel_separator.png");
        image.raycastTarget = false;
    }

    // A vanilla tab (the crafting panel's Craft/Upgrade): the mod swaps sprites on hover, and the current tab is
    // a disabled button (UiBind.SetTabSelected) that shows the selected sprite with a title-coloured caption.
    private static Button MakeTab(Transform parent, string name, string text, float w, float h) {
        Button tab = MakeButton(parent, name, text, w, h, 18);
        ((Image)tab.targetGraphic).sprite = Sprite(Placeholders + "button_tab.png");
        TextMeshProUGUI label = tab.GetComponentInChildren<TextMeshProUGUI>(true);
        label.color = TabText;
        return tab;
    }

    // A list row's background: vanilla's item_background, lightly shaded.
    private static void ShadeRow(Image image) {
        image.sprite = Sprite(Placeholders + "item_background.png");
        image.type = Image.Type.Sliced;
        image.color = Shade;
    }

    // Names in lists: bold Averia Serif in gold, like vanilla's skill and recipe names.
    private static TextMeshProUGUI AddName(Transform parent, string name, string text, int size, TextAlignmentOptions alignment) {
        TextMeshProUGUI tmp = AddText(parent, name, text, size, Gold, alignment);
        tmp.fontStyle = FontStyles.Bold;
        return tmp;
    }

    private static TextMeshProUGUI AddNameLabel(Transform parent, string name, string text, int size) {
        TextMeshProUGUI tmp = AddLabel(parent, name, text, size, Gold, flexible: true);
        tmp.fontStyle = FontStyles.Bold;
        return tmp;
    }

    // A checkbox's caption: bold orange, like vanilla's.
    private static TextMeshProUGUI AddToggleLabel(Transform parent, string name, string text, int size) {
        TextMeshProUGUI tmp = AddLabel(parent, name, text, size, Orange, flexible: true);
        tmp.fontStyle = FontStyles.Bold;
        return tmp;
    }

    private static Button MakeButton(Transform parent, string name, string text, float w, float h, int fontSize) {
        GameObject go = NewUI(name, parent);
        Image img = go.AddComponent<Image>();
        img.sprite = Sprite(Placeholders + "button.png");
        img.type = Image.Type.Sliced;
        Button button = go.AddComponent<Button>();
        button.targetGraphic = img;
        LayoutElement le = go.AddComponent<LayoutElement>();
        if (w > 0f) { le.preferredWidth = w; le.minWidth = w; }
        le.preferredHeight = h;
        le.minHeight = h;
        ((RectTransform)go.transform).sizeDelta = new Vector2(w > 0f ? w : 100f, h);

        // Vanilla's captions: bold Averia Serif, sized down to fit (TakeAll, Craft).
        TextMeshProUGUI label = AddText(go.transform, "Text", text, fontSize, Orange, TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10f;
        label.fontSizeMax = Mathf.Max(fontSize, 16);
        Stretch(label.gameObject);
        RectTransform lrt = label.rectTransform;
        lrt.offsetMin = new Vector2(6f, 2f);
        lrt.offsetMax = new Vector2(-6f, -2f);
        return button;
    }

    private static Toggle MakeToggle(Transform parent, string name, float size) {
        GameObject go = NewUI(name, parent);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredWidth = size;
        le.preferredHeight = size;
        le.minWidth = size;
        le.minHeight = size;
        ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);

        GameObject bg = NewUI("Background", go.transform);
        Stretch(bg);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.sprite = Sprite(Placeholders + "checkbox.png");

        GameObject check = NewUI("Checkmark", bg.transform);
        Stretch(check, 3f);
        Image checkImg = check.AddComponent<Image>();
        checkImg.sprite = Sprite(Placeholders + "checkbox_marker.png");
        checkImg.color = CheckColor;
        checkImg.raycastTarget = false;

        Toggle toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = bgImg;
        toggle.graphic = checkImg;
        toggle.isOn = false;
        return toggle;
    }

    private static TMP_InputField MakeInputField(Transform parent, string name, float w, float h, TMP_InputField.ContentType contentType) {
        GameObject go = NewUI(name, parent);
        Image bg = go.AddComponent<Image>();
        bg.sprite = Sprite(Placeholders + "text_field.png");
        bg.type = Image.Type.Sliced;
        LayoutElement le = go.AddComponent<LayoutElement>();
        if (w > 0f) { le.preferredWidth = w; le.minWidth = w; }
        le.preferredHeight = h;
        le.minHeight = h;
        ((RectTransform)go.transform).sizeDelta = new Vector2(w > 0f ? w : 200f, h);

        GameObject area = NewUI("Text Area", go.transform);
        Stretch(area);
        RectTransform art = (RectTransform)area.transform;
        art.offsetMin = new Vector2(10f, 6f);
        art.offsetMax = new Vector2(-10f, -6f);
        area.AddComponent<RectMask2D>();

        int fontSize = (int)Mathf.Clamp(h * 0.5f, 14f, 20f);
        TextMeshProUGUI placeholder = AddText(area.transform, "Placeholder", "", fontSize, PlaceholderText, TextAlignmentOptions.MidlineLeft);
        Stretch(placeholder.gameObject);
        placeholder.fontStyle = FontStyles.Bold | FontStyles.Italic;
        placeholder.overflowMode = TextOverflowModes.Overflow;

        TextMeshProUGUI text = AddText(area.transform, "Text", "", fontSize, InputText, TextAlignmentOptions.MidlineLeft);
        text.fontStyle = FontStyles.Bold;
        Stretch(text.gameObject);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;

        TMP_InputField field = go.AddComponent<TMP_InputField>();
        field.targetGraphic = bg;
        field.textViewport = art;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.contentType = contentType;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.pointSize = fontSize;
        field.customCaretColor = false;
        field.selectionColor = new Color(0.659f, 0.808f, 1f, 0.753f);
        return field;
    }

    private static void SetPlaceholder(TMP_InputField field, string text) {
        if (field.placeholder is TextMeshProUGUI placeholder) { placeholder.text = text; }
    }

    // A vertical list (or grid) that scrolls. Returns the content transform the caller fills.
    private static Transform MakeScrollList(GameObject holder, bool grid = false, Vector2 cell = default) {
        Image bg = holder.AddComponent<Image>();
        bg.sprite = Sprite(Placeholders + "item_background.png");
        bg.type = Image.Type.Sliced;
        bg.color = Well;
        bg.raycastTarget = true;
        ScrollRect scroll = holder.AddComponent<ScrollRect>();

        GameObject viewport = NewUI("Viewport", holder.transform);
        Stretch(viewport);
        RectTransform vrt = (RectTransform)viewport.transform;
        vrt.offsetMin = new Vector2(4f, 4f);
        vrt.offsetMax = new Vector2(-(ScrollbarW + 8f), -4f);
        viewport.AddComponent<RectMask2D>();
        Image vimg = viewport.AddComponent<Image>();
        vimg.color = new Color(0f, 0f, 0f, 0f);

        GameObject content = NewUI("Content", viewport.transform);
        RectTransform crt = (RectTransform)content.transform;
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.offsetMin = Vector2.zero;
        crt.offsetMax = Vector2.zero;
        if (grid) {
            GridLayoutGroup glg = content.AddComponent<GridLayoutGroup>();
            glg.cellSize = cell;
            glg.spacing = new Vector2(6f, 6f);
            glg.padding = new RectOffset(4, 4, 4, 4);
        } else {
            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = vrt;
        scroll.content = crt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = false;
        scroll.scrollSensitivity = 800f;
        scroll.verticalScrollbar = MakeScrollbar(holder.transform);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return content.transform;
    }

    private const float ScrollbarW = 10f;

    private static Scrollbar MakeScrollbar(Transform holder) {
        GameObject bar = NewUI("Scrollbar", holder);
        RectTransform rt = (RectTransform)bar.transform;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-3f, -4f);
        rt.sizeDelta = new Vector2(ScrollbarW, -8f);
        // Vanilla's list scrollbar: Unity's built-in Background and UISprite, tinted brown and orange.
        Image track = bar.AddComponent<Image>();
        track.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        track.type = Image.Type.Sliced;
        track.color = new Color(0.192f, 0.126f, 0.078f, 1f);

        GameObject area = NewUI("Sliding Area", bar.transform);
        Stretch(area);
        GameObject handle = NewUI("Handle", area.transform);
        Stretch(handle);
        Image handleImage = handle.AddComponent<Image>();
        handleImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        handleImage.type = Image.Type.Sliced;

        Scrollbar scrollbar = bar.AddComponent<Scrollbar>();
        scrollbar.handleRect = (RectTransform)handle.transform;
        scrollbar.targetGraphic = handleImage;
        scrollbar.colors = new ColorBlock {
            normalColor = new Color(0.925f, 0.647f, 0.341f, 1f),
            highlightedColor = new Color(1f, 0.784f, 0.09f, 1f),
            pressedColor = new Color(0.839f, 0.647f, 0.031f, 1f),
            selectedColor = new Color(1f, 0.784f, 0.09f, 1f),
            disabledColor = new Color(0.784f, 0.784f, 0.784f, 0.5f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f,
        };
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        return scrollbar;
    }

    // ------------------------------------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------------------------------------

    private static void EnsureFolders() {
        foreach (string dir in new[] { "Assets/Clans", "Assets/Clans/Bundle", Root, PagesDir, HudDir }) {
            if (AssetDatabase.IsValidFolder(dir) == false) {
                string parent = Path.GetDirectoryName(dir).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
            }
        }
    }

    private static void SavePrefab(GameObject go, string path) {
        PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
        Object.DestroyImmediate(go);
        if (ok == false) { Debug.LogError("[Clans] Failed to save " + path); }
    }

    private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

    private static Sprite Sprite(string path) {
        if (sprites.TryGetValue(path, out Sprite cached) && cached != null) { return cached; }
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) { Debug.LogWarning("[Clans] Sprite not found: " + path); }
        sprites[path] = sprite;
        return sprite;
    }
}
