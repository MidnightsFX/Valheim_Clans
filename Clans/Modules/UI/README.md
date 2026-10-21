# Clans UI: prefab ↔ code contract

The Clans window is **authored in Unity** (`ClansUnity/Assets/Clans/Bundle/UI/`, shipped in the `clans`
asset bundle) and **bound by code** in this folder. Move, resize and restyle anything in the editor; the
code only finds children by GameObject name and wires them. Nothing in the code positions widgets.

The generator that made the first prefabs is `ClansUnity/Assets/Editor/ClansUiBuilder.cs`:

| Menu | Does |
|---|---|
| `Clans/UI/Build Shell And Overlays` | Overwrites the window, the dialog and the emblem editor. |
| `Clans/UI/Build Missing Pages` | Creates page prefabs that do not exist yet; hand edits survive. |
| `Clans/UI/Rebuild All Pages (overwrite)` | Resets every page to the generator's layout. |
| `Clans/UI/Render Previews` | Renders each prefab (pages inside the window) to `ClansUnity/Previews`. |

The previews show the placeholders unless the game's own UI art has been copied in once with
`python Tools/fetch_preview_vanilla.py <AssetRipper export>/Assets` (into the gitignored
`ClansUnity/Assets/Editor/PreviewVanilla`, never bundled); then they are dressed like the game, with English text.
| `Clans/Build Asset Bundle` | Builds `clans` and copies it to `Clans/Assets/clans`, where the mod build embeds it. |

Headless (Unity closed), from the repo root:

```
Unity.exe -batchmode -quit -projectPath ClansUnity -executeMethod ClansUiBuilder.BuildAllFromCommandLine -logFile build.log
```

`ClansUiBuilder.BuildAndPreviewFromCommandLine` also renders the previews (`-previewOut <dir>`).

## Rules

- Prefabs carry **no Clans components**, so they never depend on the mod assembly.
- Prefab names are `Clans_<Area>_<Name>` and unique in the bundle; code loads them by that name.
- Lists hold an **inactive template** child, which code clones per entry and never shows itself:
  `Item` in a scroll list's content, `Tab` in the window's `Tabs`, `Flag` in a rank's `Perms`.
- Text is TextMeshPro. Any text starting with `$` is localized by code.
- Sprites are placeholders named exactly like vanilla UI sprites (`woodpanel_trophys`, `woodpanel_512x512`,
  `woodpanel_password`, `darken_blob`, `button`, `button_tab`, `text_field`, `checkbox`, `checkbox_marker`,
  `item_background`, `panel_separator`, `bar_monster_hp_20`); `ClansStyle` swaps each for the game's sprite
  of that name at runtime. As in the game's inventory panels, `woodpanel*` images get the lit panel
  material and buttons, tabs and separators the lit HUD material; `button` and `button_tab` buttons swap to
  their highlight, pressed and disabled sprites and grey their caption when disabled; checkboxes and text
  fields get vanilla's colours; every `ScrollRect` gets vanilla's scroll speed.
- **Tabs** are `button_tab` buttons. The current one is **disabled** (`UiBind.SetTabSelected`), which shows
  vanilla's selected tab sprite and turns its caption the title colour.
- **Panels** are an invisible click-blocking rect with a `Bkg` child (the wood, spilling 10 px past the rect)
  and, for floating panels, a `Darken` child (a soft shadow); both ignore layout groups.
- **Fonts** follow the text's role, Valheim's fonts with their black outline: Norse for `Title`, `ClanName` and
  names ending in `Header`; bold Averia Serif for any **bold** text (names in lists, button and checkbox
  captions); plain Averia Serif inside input fields; Averia Sans for everything else. Colours follow vanilla:
  titles and accents `#FFB75C`, names `#FFD800`, captions `#FFA13C`, body text white.
- An emblem is any element with an `Image` child `Back` and an `Image` child `Front`.

## Prefabs and the children code binds

**`Clans_UI_Window`** (root stretched; placed beside vanilla's Trophies panel inside the inventory screen)
`Backdrop` (full-screen dim `Button`; a click closes the window, like vanilla's Trophies panel); `Panel` →
`Header` → `Emblem`, `ClanName`, `ClanSub`, `XpBar` (`Fill` Image, `Text`), `PvpSlot` (the vanilla PvP toggle
is moved in here), `PvpLabel`, `LayoutButton` (opens the HUD layout editor; child `Icon` with the bundle's
`clans_layout` sprite), `LayoutLabel`; `Tabs` → `Tab` template (`button_tab` `Button`, child `Text`); `TabBorder`;
`Pages` (page prefabs are instantiated in here); `Status`; `Close`.

**`Clans_UI_Confirm`**: `Overlay` → `Panel` → `Title`, `Body`, `Input` (`TMP_InputField`, shown only for
prompts), `Buttons` → `Ok`, `Cancel`.

**`Clans_UI_EmblemEditor`**: `PreviewRow` → `Preview` (emblem); `Backgrounds`, `Symbols` (grids whose `Item`
is a `Button` with `Selected`, `Back` and `Icon`); `BackColors`, `FrontColors` (grids whose `Item` is a
`Button` with `Selected`, `Back` and `Swatch`); `Buttons` → `Save`, `Cancel` (hidden when embedded in the
Create page). `Selected` sits behind `Back` and is slightly larger, so it shows as a gold rim around the tile.

**Pages** (`Pages/Clans_Page_*`, root stretched):

| Page | Children |
|---|---|
| `Clans_Page_NoClan` | `Intro`, `Tile_Create` and `Tile_Join` (`Button`s with `Icon`, `Title`, `Body`), `Pending` |
| `Clans_Page_Create` | `Form` → `Name`, `Description` (`TMP_InputField`s), `RecruitingRow/Recruiting` (`Toggle`), `Fee`, `Buttons` → `Back`, `Create`; `EmblemHost` (the emblem editor is instantiated in here) |
| `Clans_Page_Join` | `Search` (`TMP_InputField`), `Back`, `List` → `Item`: `Emblem`, `Texts` → `Name`, `Info`, `Description`; `State`, `Apply`, `Withdraw`; `Empty` |
| `Clans_Page_Overview` | `Left` → `EmblemRow/Emblem`, `EditEmblem`, `Description`, `Status`, `StatusButtons` → `EditStatus`, `PostInvite`; `RecruitingRow/Recruiting`, `Treasury`, `TreasuryButtons` → `Donate`, `Withdraw`; `MembershipButtons` → `Leave`, `Disband`. `Middle/Log` → `Item`: `Time`, `Text`. `Right/Ranks` → `Item`: `Name`, `Perms`. `Right/Applications` → `Item`: `Name`, `Message`, `Accept`, `Decline`; `Right/Applications/None` |
| `Clans_Page_Members` | `Count`, `EditRanks`, `List` → `Item`: `Online` (Image), `Name`, `Rank`, `LastSeen`, `Actions` (fixed width, so the columns stay under `Headings`) → `Promote`, `Demote`, `Kick`, `Transfer`. `RankEditor` (inactive) → `Ranks` → `Item`: `Head` → `Name` (`TMP_InputField`), `Note`, `Up`, `Down`, `Remove`; `Perms` → `Flag` template (`Toggle`, `Label`); `Buttons` → `Add`, `Cancel`, `Save` |

| `Clans_Page_Party` | `Left` → `PartyHeader`, `Members` → `Item`: `Online`, `Emblem`, `Name`, `Role`, `Promote`, `Kick`; `PartyButtons` → `Create`, `Leave`, `PostInvite`; `RaidHeader`, `LeaveRaid`, `Squads` → `Item`: `Head` → `Title`, `MakeRaidLeader`; `Members`. `Right` → `Filters` → `Nearby`, `Clanmates`, `Everyone` (tabs; the current filter is disabled); `Search`; `Players` → `Item`: `Emblem`, `Texts` → `Name`, `Info`; `Invite`, `InviteRaid`; `Players/Empty` |

| `Clans_Page_War` | `Summary`, `Rules`, `List` → `Item`: `Emblem`, `Texts` → `Name`, `Info`, `State`; `Buttons` → `Declare`, `Accept`, `Decline`, `Cancel`, `OfferPeace`, `AcceptPeace`, `RejectPeace` (code shows only those that apply); `Empty` |
| `Clans_Page_Storage` | `Treasury`, `Access`, `Note`, `List` → `Item`: `Texts` → `Title`, `Info`; `Open`, `Buy` |
| `Clans_Page_Level` | `Left` → `LevelHeader`, `Summary`, `XpBar` (`Fill` Image, `Text`), `ResourcesHeader`, `Resources` → `Item`: `Icon` (Image; code sets the item's icon), `Texts` → `Name`, `Count`; `Give`; `Requirements`, `Upgrade`, `UpgradeNote`. `Middle` → `LevelsHeader`, `Levels` → `Item`: `Name`, `Perks` (grows with its text). `Right` → `FeastsHeader`, `FeastNote`, `Feasts` → `Item`: `Icon`, `Texts` → `Name`, `Info`, `Cost`; `Buttons` → `Buy`, `ClanBuy` (code hides `ClanBuy` from ranks without ClanFeasts) |
| `Clans_Page_Activities` | `Summary`, `Note`, `List` → `Tile`: `Icon` (Image; code sets the bundle's `tile_hunt`, `tile_race` or `tile_worldboss`), `Texts` → `Title`, `Body`, `Requirement`, `Progress`, `Lockout` (code hides the empty ones); `Controls` → `Difficulty`, `Boss`, `Start`, `Cancel` (code shows only those that apply); `Empty` |

A page prefab missing from the bundle is skipped with a warning, and its tab is not shown.

**HUD and inventory** (`Hud/`):

| Prefab | Children |
|---|---|
| `Clans_HUD_PartyFrame` (under the HUD root, left) | `Title`, `Members` → `Member` template: `Emblem`, `Texts` → `Name`, `Health/Fill`; `Leader`, `Distance` (a `CanvasGroup` dims offline members); `RaidTitle`, `Raid` → `Item`: `Name`, `HealthHolder/Health/Fill` |
| `Clans_HUD_InviteToast` (under the HUD root, left) | `Icon`, `Title`, `Body`, `Hint`, `Timer/Bar/Fill`; a `CanvasGroup` fades it out |
| `Clans_INV_InvitePanel` (inventory screen, top) | `Title`, `List` → `Item`: `Text`, `Accept`, `Decline` |
| `Clans_HUD_Chat` (on the HUD's canvas beside the HUD root, bottom-left, with its own canvas at vanilla's chat window order; replaces vanilla's chat window) | `Header` (drag handle) → `Tabs` → `Tab` template (`button_tab` `Button`, child `Text`, child `Unread`); `List` → `Row` template: `Head` → `Channel`, `Name`, `Time`; `Body` (rich text with `<link>`s); `InputRow` → `Channel`, `InputSlot` (vanilla's chat input is moved in; the slot's own image is hidden); `Resize` (drag handle) |
| `Clans_INV_StorageTabs` (top edge of vanilla's container panel) | `Tab` template (`button_tab` `Button`, child `Text`; the open tab is disabled), cloned per storage tab while a clan tab is open |
| `Clans_HUD_Tracker` (under the HUD root; code puts its top-right corner, the pivot, left of the minimap and below vanilla's status effect row) | `Activity` template: `Head` → `Icon` (code sets the activity's `tile_*` sprite), `Title`, `Time`; `Lines` → `Line` template: `Text`, `Count` (code greys a finished line and greens its count) |
| `Clans_INV_TrackerMenu` (inventory screen's root, drawn last; code puts its top-right corner, the pivot, just left of the tracker button) | `Title`, `List` → `Item`: `Toggle`, `Label` |
| `Clans_HUD_LayoutEditor` (root stretched, beside the HUD root so it stays when the HUD is hidden; code gives it its own canvas, drawn on top) | `Outlines` → `Outline` template: `Fill` (the drag surface), `Border` → `Top`, `Bottom`, `Left`, `Right` (code tints them while selected), `Label` (shown while the element has nothing to show); `Center` → `Hint`, `Done`, `Reset` |

The tracker, the party frame (the invite toast follows it) and the chat panel can be moved by the player in
the layout editor; code then places them from their Client settings instead of the spots above, keeping them
on screen.

The tracker button itself is a clone of vanilla's repair button (as Dverger Automation's switch is), placed
left of the compendium, with the bundle's `clans_tracker` icon.

The large map's "Visible to clan" and "Ping clan only" switches (`MapToggles`) are clones of vanilla's
`PublicPanel` (the "Visible to other players" switch, minus its gamepad button), stacked above vanilla's
`SharedPanel`; no prefab.

Bars are sized by the `Fill` image's right anchor, not its fill amount, so a `Fill` needs no sprite.
