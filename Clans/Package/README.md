# Clans

Clans for Valheim, inspired by the guilds of Guild Wars. Levels, member ranks, weekly activities, shared storage and party support.

Around that, Clans adds what several large groups need to share one server, whether they play together or
against each other: parties and raids, shared skill experience, clan wars, chat channels, clan-only map
sharing and tombstones that only your clan can open.

![The Clans window](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/ClanLandingPage.png?raw=true)

## Reporting issues or feedback
Got a bug to report or just want to chat about the mod? Drop by the discord or github.

[![discord logo](https://i.imgur.com/uE6umQE.png)](https://discord.gg/Dmr9PQTy9m)
[![github logo](https://i.imgur.com/lvbP5OF.png)](https://github.com/MidnightsFX/Valheim_Clans)

## Installation

Install with a mod manager, or put `Clans.dll` in `BepInEx/plugins` alongside Jotunn, YamlDotNet and Item
Favorite Framework. Clans must be installed on the server and on every client.

## The Clans window

Open it with the **Clan** button in the row of round buttons at the top right of the inventory. The vanilla PvP
toggle moves into the window's header. If another mod replaces the inventory screen (such as Auga), set a
hotkey for the window in the client settings instead.

- **Without a clan**, the window lists the server's clans so you can apply to one, or found your own for a fee.
- **In a clan**, it has the Overview, Level, Members, War, Activities, Storage and Party tabs.

## Parties and raids

- **Parties of up to five.** Party members share part of the skill experience they gain, show on each other's
  map and cannot hurt each other, even with PvP on.
- **Raids** join up to four parties under one raid leader, who can move members between squads.
- **Invite from the Party tab.** Pick from the players nearby, your clanmates or everyone online, or search by
  name.
- **Invitations** pop up as a notice on the HUD and wait at the top of the inventory screen until you accept
  or decline them.
- **Invites in chat.** Post your party or clan invite to chat (`/pinvite`, `/cinvite`) and anyone who sees it
  can join from there.
- **Party frame.** Your squad and the rest of your raid, with their health, on the left of the HUD.

![Party tab](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/ClanParty.png?raw=true)

| Invitation on the HUD | Invitations in the inventory | Party frame |
|---|---|---|
| ![Party invitation notice](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/PartyInvite.png?raw=true) | ![Invitations panel](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/ClanParty2.png?raw=true) | ![Party frame](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/ClanParty3.png?raw=true) |

## Features

### Ranks and members

A clan starts with five ranks. The leader can rename, reorder, add and remove ranks, and choose what each one
may do: invite, kick, promote, manage wars, use storage, take from the treasury and more. Membership is per
account, so all of a player's characters belong to the same clan.

![Members tab](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/ClanOnline.png?raw=true)

### Emblems

Pick a background shape in a colour, then a symbol on top: a rune in a second colour, or a creature trophy.
The emblem shows above your clanmates' heads and on your clan's banners.

![Emblem editor](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/EmblemEditor.png?raw=true)

### Treasury and storage

Members donate coins to the treasury, which pays for storage tabs, clan levels and guild feasts. Shared storage
tabs are bought from the treasury, and a later upgrade gives every member a personal tab too. Storage opens
from anywhere and goes wherever its clan goes, so the server decides which items may be stored.

![Storage tab](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/ClanStorage.png?raw=true)

### Wars

One clan declares war, optionally with a kill target, and the other accepts. While at war, the two clans'
members can fight each other whatever their PvP setting. A war ends in peace, when its time runs out, or when
one side reaches the kill target.

| Declaring war | At war |
|---|---|
| ![Declaring war](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/War.png?raw=true) | ![At war](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/War2.png?raw=true) |

### Tombstone lock

Only a tombstone's owner and their clanmates can open it, so nobody can loot a player killed in PvP. Owners can
also make theirs private.

![A locked tombstone](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/War3.png?raw=true)

### Weekly activities

Each clan can run each activity once a week, on Easy, Normal or Hard, for clan experience, treasury coins and
rewards for everyone who took part.

- **Trophy Hunt:** hunt the creatures your clan is given, bosses included. Every member's kills count.
- **Race:** carry chests from a start totem to a destination far away. Carts and ships may carry them, portals
  may not.
- **World Boss Battles:** hold a totem against waves from the portals around it, then defeat a much stronger
  boss.

Party and raid members roll Need, Greed or Pass for an activity's magic item rewards (with Item Favorite
Framework and Epic Loot). The activity tracker under the minimap shows how your clan's activities are going.

![Activities tab](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/ClanActivity.png?raw=true)

![Activity tracker](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/ActivityTracker.png?raw=true)

### Chat channels

Global (`/g`), Clan (`/c`), Party (`/p`) and Raid (`/r`) channels, each with its own tab, and item links
(Shift + right-click an item). The clan's status message greets members in chat when they log in.

![Clans chat](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/Chat.png?raw=true)

### Movable HUD

Drag the party frame, the activity tracker and the chat panel wherever you like, with the Layout button in the
window header.

![Moving the HUD](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/LayoutAdjust.png?raw=true)

### Also

- **Clan map.** Send map pings to your clan only, show your position to your clan only, and share your Epic
  Loot bounties with clanmates, each with a switch on the large map.
- **Clan log.** Every important action shows in the Overview tab's recent activity.

## Clan levels

Clans earn experience from activities and wars, but never level up by themselves. Once a clan has the
experience, its members hand in the resources from the Level tab, and a rank allowed to upgrade the clan pays
the coins from the treasury.

![Level tab](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/ClanLevel.png?raw=true)

What each level gives, by default:

| Level | Clan XP | Members | Clanmate XP share | Share range | Unlocks |
|---|---|---|---|---|---|
| 1 | 0 | 10 | 25% | 50 m | 1st storage tab, Meadows feast |
| 2 | 1,000 | 12 | 27% | 55 m | 2nd storage tab, Black Forest feast |
| 3 | 2,500 | 15 | 30% | 60 m | Personal storage tab, Swamp and Ocean feasts |
| 4 | 5,000 | 18 | 32% | 65 m | 3rd storage tab, Mountain feast |
| 5 | 8,500 | 20 | 35% | Any distance | Plains feast |
| 6 | 13,000 | 25 | 37% | Any distance | 4th storage tab, Mistlands feast |
| 7 | 19,000 | 30 | 40% | Any distance | |
| 8 | 26,000 | 35 | 42% | Any distance | 5th storage tab, Ashlands feast |
| 9 | 35,000 | 40 | 45% | Any distance | |
| 10 | 50,000 | 50 | 50% | Any distance | Deep North feast |

Clanmate XP share is the part of each skill gain that clanmates in the same party receive.

What each upgrade costs, by default:

| Level | Coins | Resources |
|---|---|---|
| 2 | 250 | 200 Wood, 150 Stone, 50 Resin, 20 Deer hide |
| 3 | 500 | 150 Fine wood, 100 Core wood, 30 Bronze, 10 Troll hide |
| 4 | 1,000 | 100 Ancient bark, 50 Iron, 20 Guck, 20 Withered bone |
| 5 | 1,500 | 50 Silver, 30 Wolf pelt, 50 Obsidian, 20 Crystal |
| 6 | 2,500 | 50 Black metal, 50 Linen thread, 30 Tar, 15 Lox pelt |
| 7 | 3,500 | 100 Yggdrasil wood, 30 Refined eitr, 40 Carapace, 10 Black core |
| 8 | 5,000 | 100 Blackwood, 30 Flametal, 30 Asksvin hide, 40 Ceramic plate |
| 9 | 7,500 | 60 Flametal, 50 Charred bone, 10 Morgen sinew, 60 Refined eitr |
| 10 | 10,000 | 100 Flametal, 20 Black core, 5 Morgen heart, 80 Ceramic plate |

### Guild feasts

A member sets out a guild feast from the Level tab, right in front of them. It is a stronger copy of a vanilla
biome feast that anyone may eat from, clan or not, as often as the game allows, until it is cleared away after
half an hour. Each costs its biome's cooked meats and crops plus coins, paid by the member, or by the clan's
treasury and shared storage when their rank allows it.

| Feast | Clan level | Health / Stamina / Eitr |
|---|---|---|
| Meadows | 1 | 40 / 40 / 5 |
| Black Forest | 2 | 40 / 40 / 10 |
| Swamp | 3 | 40 / 40 / 15 |
| Ocean | 3 | 50 / 50 / 20 |
| Mountain | 4 | 50 / 50 / 25 |
| Plains | 5 | 60 / 60 / 30 |
| Mistlands | 6 | 70 / 70 / 38 |
| Ashlands | 8 | 80 / 80 / 43 |
| Deep North | 10 | 90 / 90 / 48 |

## Clan banners

Clan members can build clan banners in seven colours: white, black, blue, red, green, yellow and orange. Each
is built like the vanilla banner of its colour, from the same recipe, and shows the builder's clan emblem to
everyone. Only clan members can build them.

![Clan banners](https://github.com/MidnightsFX/Valheim_Clans/blob/master/docs/Banners.png?raw=true)

## Configuration files

The YAML files live in `BepInEx/config/Clans/`. The server's copies are sent to every player, changes are picked
up as soon as a file is saved, and server admins can also edit them in game from the Mod Config menu. Each file
explains its own settings at the top.

| File | What it sets |
|---|---|
| `ranks.yaml` | The ranks and permissions a newly founded clan starts with. |
| `levels.yaml` | Each clan level: the experience needed, member limit, upgrade cost and clanmate XP share. |
| `emblems.yaml` | The emblem backgrounds, symbols and colours players can pick from. |
| `storage.yaml` | The price and clan level of each storage tab, and which items may be stored. |
| `activities.yaml` | The weekly activities: difficulty, targets, bosses and rewards. |
| `feasts.yaml` | The guild feasts offered: the clan level each needs, its food and its cost. |

## Compatibility

- **Epic Loot:** magic item rewards, item tooltips in chat, and shared bounties.
- **Star Level System, Epic Loot and Valheim Fortress** are all needed for the World Boss Battles activity.
- **Item Favorite Framework:** Need / Greed / Pass rolls for activity loot.
- **My Little UI:** its item links show in the Clans chat.
- **Chatter:** Clans chat lines go into Chatter's window.
- **Auga:** open the Clans window with its hotkey, since Auga replaces the inventory screen.
- **Not compatible** with Smoothbrain's Groups and Guilds mods, which do the same job.

## For mod authors

Other mods can ask Clans who is in which clan, party and raid, each clan's level, and each member's rank and
rights. On the server they can also add and take a clan's treasury coins and storage items, and hear about
joins, leaves, rank changes and level ups as they happen. A one-file soft-dependency shim and the full reference are in the
Clans repository, under `ClansAPI/` and `docs/API.md`.
