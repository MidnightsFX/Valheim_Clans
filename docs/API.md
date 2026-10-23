# Clans API

Clans gives other mods a read of its clans and, on the server, access to their coins and items:

- **Membership:** which clan a player is in, who is in a clan, who leads it, and who is online.
- **Levels:** each clan's level and the highest level the server defines.
- **Ranks and rights:** a member's rank (position, id and name) and whether their rank allows something,
  such as withdrawing from storage.
- **Parties and raids:** who is in which party, raid and squad, and who leads each.
- **Treasury and storage:** read a clan's coins and its shared storage tabs, and on the server add or take
  coins and items.
- **Listeners:** a callback whenever any of that changes, and on the server one JSON event per change
  (joins, leaves, rank changes, level ups, party and raid changes, treasury and storage changes).

Clans' plugin GUID is `MidnightsFX.Clans`. Every player has Clans installed (the server requires it), so the
API is on every machine. What each machine knows differs, and is covered under
[Where answers come from](#where-answers-come-from).

## Contents

- [Getting started](#getting-started)
- [Rules every endpoint follows](#rules-every-endpoint-follows)
- [Where answers come from](#where-answers-come-from)
- [Versioning](#versioning)
- [Players and accounts](#players-and-accounts)
- [Clans](#clans)
- [Membership](#membership)
- [Ranks and rights](#ranks-and-rights)
- [Parties and raids](#parties-and-raids)
- [Treasury](#treasury)
- [Storage](#storage)
- [Listeners and server events](#listeners-and-server-events)
- [Calling Clans without the shim](#calling-clans-without-the-shim)
- [Recipes](#recipes)
- [Testing your integration](#testing-your-integration)
- [Adding an endpoint (for Clans contributors)](#adding-an-endpoint-for-clans-contributors)

## Getting started

### Three ways in

| | What it is | When to use it |
|---|---|---|
| **`ClansAPI.Clans`** | A small shim ([`ClansAPI/`](../ClansAPI/)) that binds every endpoint to a typed delegate once. Copy its single source file into your project, or bundle the DLL with ILRepack. | Almost always. Safe to call when Clans is not installed. |
| **`Clans.API`** | The public static class inside `Clans.dll`. | You hard-depend on Clans and reference its DLL directly. |
| **Reflection** | `Type.GetType("Clans.API, Clans")` | You already have your own glue. Every signature can be bound with `Delegate.CreateDelegate`. |

The rest of this page uses the shim.

### Set up the shim

1. Add the shim to your plugin in one of two ways:
   - **Copy** [`Clans.cs`](../ClansAPI/src/Clans.cs) into your project. It is one file with no dependencies
     beyond the game.
   - **Bundle** `ClansAPI.dll` into your plugin with ILRepack. How to build it is in the
     [shim's README](../ClansAPI/README.md).
2. Declare a soft dependency, so Clans loads before your plugin.
3. Call it.

```csharp
using BepInEx;
using ClansAPI;

[BepInPlugin("you.MyMod", "My Mod", "1.0.0")]
[BepInDependency(Clans.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
public class MyMod : BaseUnityPlugin {
    private void Awake() {
        Logger.LogInfo(Clans.IsLoaded()
            ? $"Clans {Clans.GetPluginVersion()} found, API {Clans.GetApiVersion()}."
            : "Clans is not installed.");
    }

    internal static bool SameClan(Player a, Player b) {
        long clan = Clans.GetClanIdOfPlayer(a);
        return clan != 0 && clan == Clans.GetClanIdOfPlayer(b);
    }
}
```

### When Clans is missing or older

Without Clans every call is safe: queries return their "nothing" value (below), changes return `false` with
the error `notInstalled`, and listeners are never called. `Clans.IsLoaded()` tells you which case you are in.

An endpoint an older Clans lacks behaves the same way. Check `Clans.GetApiVersion()` or
`Clans.HasEndpoint("Name")` before relying on anything newer than the oldest version you support.

## Rules every endpoint follows

- **Main thread only.** Call from Unity's main thread, as you would any game code. Listeners are called on it.
- **Nothing values.** When there is no answer, queries return `0` for ids, counts and levels, `-1` for
  positions and amounts that could be 0 (rank index, treasury, tab count, item count), `""` for text and
  single JSON objects, `false`, an empty list, or `"[]"` for JSON lists.
- **Players are accounts.** Clans keeps membership per account, the platform id of the person playing
  (`"Steam_76561198..."`), so all of one person's characters share a clan and rank. Turn what you have into an
  account id with `GetAccountId(Player)`, `GetAccountIdOfPeer(long)` or `GetAccountIdOfPlayerId(long)`.
- **Names are display text.** Rank names come back in the language of the machine asking. Clan and character
  names are as players typed them.
- **The signature rule.** Only primitives, `string`, game and Unity types, and `System.Func`/`System.Action` of
  those cross the boundary. Structures come as JSON strings.

### Reading the JSON

The JSON is small and flat. Jotunn ships SimpleJson, which every Valheim mod using Jotunn already has:

```csharp
var members = (IList<object>)SimpleJson.SimpleJson.DeserializeObject(Clans.GetMembersJson(clanId));
foreach (IDictionary<string, object> member in members) {
    string name = (string)member["name"];
    long rankIndex = (long)member["rankIndex"];   // numbers arrive as long
}
```

Any JSON library works. Numbers are whole numbers written in the invariant culture.

## Where answers come from

Clans' server decides everything and keeps every clan's full record. Clients get a part of it pushed to them.

| | Server (dedicated, listen host, single player) | Plain client |
|---|---|---|
| Clans: name, level, member count, leader | every clan | every clan |
| Wars | every clan | every clan |
| A player's clan and rank | everyone, online or not | anyone online, and every member of their own clan |
| A clan's member list | every clan, offline members too | their own clan in full; other clans' online members |
| Ranks list and rights | every clan | their own clan |
| A player's party, raid and squad | everyone in one | anyone online, and everyone in their own party or raid |
| A party's or raid's members and leader | every one, members in the disconnect grace period too | their own in full; others' online members (a raid's leader when online) |
| Treasury, experience, tab count | every clan | their own clan |
| Storage contents | every clan | ask with `RequestStorageJson` (their own clan, rank permitting) |
| Changing treasury and storage | yes | no |

`Clans.HasFullData()` is `true` on the server. `Clans.IsReady()` is `true` once there is anything to answer
from. On the server that is once the world's clans are loaded. On a client it is once the server has welcomed
the player, a moment after their character spawns. Before that, everyone looks clanless.

## Versioning

| Endpoint | Returns |
|---|---|
| `IsLoaded()` | Clans is installed and its API was found (shim only). |
| `GetApiVersion()` | The API version, `1` for this one. `0` without Clans. |
| `GetPluginVersion()` | Clans' version, e.g. `"1.0.0"`. |
| `HasEndpoint(string name)` | Whether the installed Clans has the endpoint. |
| `IsReady()` | There is clan data to answer from. See above. |
| `HasFullData()` | This machine is the server and knows every clan in full. |

Existing signatures never change. New endpoints raise the API version.

## Players and accounts

| Endpoint | Returns |
|---|---|
| `GetLocalAccountId()` | The local player's account. `""` before the server has welcomed them, and on a dedicated server. |
| `GetAccountId(Player player)` | The player's account. `""` when unknown. |
| `GetAccountIdOfPeer(long peerId)` | The account of a connection: `ZNetPeer.m_uid`, or the sender of a routed RPC. |
| `GetAccountIdOfPlayerId(long playerId)` | The account by `Player.GetPlayerID()`. On a client the character must be loaded nearby. |
| `GetPlayerName(string accountId)` | The character name the account was last seen with. |
| `IsOnline(string accountId)` | The account is connected. |

## Clans

Clan ids are whole numbers above 0 that never change for the clan's lifetime and are never reused.

| Endpoint | Returns |
|---|---|
| `GetClanIds()` | Every clan's id. |
| `ClanExists(long clanId)` | The clan exists. |
| `GetClanName(long clanId)` | Its name. |
| `FindClanByName(string name)` | The id of the clan with that name, ignoring case and outer spaces. `0` when none. |
| `GetClanLevel(long clanId)` | Its level, `1` and up. `0` when there is no such clan. |
| `GetMaxClanLevel()` | The highest level the server's `levels.yaml` defines. |
| `GetMemberCount(long clanId)` | How many members it has. |
| `GetLeaderId(long clanId)` | Its leader's account. |
| `AreAtWar(long clanA, long clanB)` | The two clans are fighting an active war. |
| `GetClanJson(long clanId)` | The clan as JSON, below. |
| `GetClansJson()` | Every clan, as a JSON array of the same. |

```json
{"id":3,"name":"Iron Wolves","description":"We hunt at dusk.","level":2,"maxLevel":5,"maxMembers":15,
 "memberCount":6,"recruiting":true,"leaderId":"Steam_76561198000000001","leaderName":"Ragna",
 "enemies":[5],"detail":true,"xp":1250,"treasury":340,"sharedTabs":2}
```

`enemies` lists the clans it is at war with. `detail` is `false` when this machine only has the clan's summary,
which is another clan's case on a client. `xp`, `treasury` and `sharedTabs` are then `-1`.

## Membership

| Endpoint | Returns |
|---|---|
| `GetClanIdOf(string accountId)` | The account's clan. `0` when in none, or unknown here. |
| `GetClanIdOfPlayer(Player player)` | The same, for a player in the world. |
| `IsInClan(string accountId)` | The account is in a clan. |
| `AreClanmates(string accountA, string accountB)` | Both are in the same clan. `false` when either is in none. |
| `GetMemberIds(long clanId)` | The members' accounts. |
| `GetOnlineMemberIds(long clanId)` | The members who are online. |
| `GetMembersJson(long clanId)` | The members as JSON, below. |

```json
[{"id":"Steam_76561198000000001","name":"Ragna","rankId":1,"rankIndex":0,"rank":"Leader","leader":true,
  "online":true,"joinedUtc":1760000000,"lastSeenUtc":1761100000}]
```

Times are Unix seconds. On a client, other clans' members are only those online, with both times `0`.

## Ranks and rights

A clan has at least 2 ranks and at most the server's limit (8 unless `ranks.yaml` says otherwise), top to
bottom. The first is the leader's and the last is the one new members join at. The leader can rename, reorder,
add and remove the ones between.

- **Rank index** is the position: `0` is the leader, higher numbers are lower ranks. Use it to compare members.
- **Rank id** stays the same through renames and reordering. Use it to remember a rank.

| Endpoint | Returns |
|---|---|
| `GetRankIndex(string accountId)` | The member's position. `-1` when in no clan, or unknown here. |
| `GetRankId(string accountId)` | The member's rank id. `0` when unknown. |
| `GetRankName(string accountId)` | The rank's name in this machine's language. |
| `IsLeader(string accountId)` | The account leads its clan. |
| `HasPermission(string accountId, string permission)` | The member's rank allows it. |
| `GetPermissionNames()` | Every permission name. |
| `GetRanksJson(long clanId)` | The clan's ranks as JSON, below. |

```json
[{"id":1,"index":0,"name":"Leader","storedName":"$clans_rank_leader","leader":true,"entry":false,
  "permissions":["ManageApplications","Invite","Kick", "..."]},
 {"id":5,"index":1,"name":"Quartermaster","storedName":"Quartermaster","leader":false,"entry":false,
  "permissions":["StorageDeposit","StorageWithdraw"]}]
```

`storedName` is the name as saved, a `$token` for an untouched default. `permissions` are what the rank may
actually do. The leader may do everything. When the server turns treasury withdrawals off, nobody may.

Permission names, also the shim's `Permission*` constants:

| Name | Lets the rank |
|---|---|
| `ManageApplications` | accept or decline applications |
| `Invite` | invite players |
| `Kick` | kick lower-ranked members |
| `Promote`, `Demote` | move lower-ranked members up or down |
| `EditInfo`, `EditEmblem` | change the clan's status, description and emblem |
| `EditRanks` | edit the ranks below their own |
| `ManageWar` | declare war and make peace |
| `StartActivities` | start the weekly activities |
| `UpgradeStorage` | buy storage tabs |
| `StorageDeposit`, `StorageWithdraw` | put items into, or take them from, the shared tabs |
| `TreasuryDonate`, `TreasuryWithdraw` | give coins to, or take them from, the treasury |
| `UpgradeClan` | buy the next clan level |
| `ClanFeasts` | have the clan pay for a guild feast |

On a client, `HasPermission` is known for the local player's own clan only. Everyone else answers `false`.

## Parties and raids

Players form parties regardless of clan. A raid joins several parties under one raid leader; each party in it
is a **squad**, numbered from 1 in the order they joined. Parties and raids last while the server runs, and
their ids are never reused within that time. A member who disconnects keeps their place for a grace period,
shown offline, and is then removed.

| Endpoint | Returns |
|---|---|
| `GetPartyId(string accountId)` | The account's party. `0` when in none, or unknown here. |
| `ArePartymates(string accountA, string accountB)` | Both are in the same party. `false` when either is in none. |
| `GetPartyLeaderId(long partyId)` | The party leader's account. |
| `GetPartyMemberIds(long partyId)` | The party's members. |
| `GetPartyJson(long partyId)` | The party as JSON, below. |
| `GetRaidId(string accountId)` | The account's raid. `0` when in none, or unknown here. |
| `GetSquad(string accountId)` | The account's squad number in its raid, `1` and up. `0` outside a raid. |
| `AreRaidmates(string accountA, string accountB)` | Both are in the same raid, in any squads. |
| `GetRaidLeaderId(long raidId)` | The raid leader's account. On a client, `""` for another raid whose leader is offline. |
| `GetRaidMemberIds(long raidId)` | Every member of every squad. |
| `GetRaidJson(long raidId)` | The raid as JSON, below. |

```json
{"id":4,"leaderId":"Steam_76561198000000001","raidId":9,"squad":1,
 "members":[{"id":"Steam_76561198000000001","name":"Ragna","leader":true,"online":true,"clanId":3},
            {"id":"Steam_76561198000000002","name":"Bjorn","leader":false,"online":false,"clanId":0}]}
```

A raid is `{"id":9,"leaderId":"...","squads":[...]}`, each squad in the party shape above, in squad order. A party
outside a raid has `raidId` and `squad` `0`.

## Treasury

| Endpoint | Returns |
|---|---|
| `GetTreasury(long clanId)` | Coins in the treasury. `-1` when unknown here. |
| `AddTreasury(long clanId, long coins, string reason)` | Server only. Adds the coins. `false` on a client, for no such clan, or coins below 1. |
| `TakeTreasury(long clanId, long coins, string reason)` | Server only. Takes the coins, all or none. `false` when it holds fewer. |

Coins in the treasury are a number on the clan's record, not items. Members withdraw them through Clans'
own window, which delivers real coins.

**Reasons.** Every change takes a `reason`. When it is not empty it goes into the clan's log, which the members
see, e.g. `"Bounty board: 200 coins went into the treasury."`. Pass `""` to change it silently. A `$token` in the
reason is translated for each reader, so you can pass your own localization token.

## Storage

A clan buys shared storage tabs, each a 5 × 4 grid. Members can also unlock personal tabs, which belong to
their owners and are not part of this API.

| Endpoint | Returns |
|---|---|
| `GetSharedTabCount(long clanId)` | Shared tabs the clan has bought. `-1` when unknown here. |
| `GetStorageJson(long clanId)` | Server only. What the shared tabs hold, below. |
| `CountStorageItem(long clanId, string prefab)` | Server only. How many of the item the shared tabs hold, any quality. `-1` on a client. |
| `RequestStorageJson(Action<bool, string> callback)` | Client. Asks the server for the local player's clan's storage. |
| `TakeStorageItems(long clanId, string itemsJson, string reason, out string error)` | Server only. Takes items, all or none. |
| `AddStorageItems(long clanId, string itemsJson, string reason, out string error)` | Server only. Adds new items, all or none. |
| `AddStorageItem(long clanId, ItemDrop.ItemData item, string reason, out string error)` | Server only. Adds a copy of an existing item. |

The shim adds `TakeStorageItem(clanId, prefab, amount, reason, out error)` and
`AddStorageItem(clanId, prefab, amount, reason, out error)` for one kind of item, and `ItemsToJson(dictionary)`.

```json
{"clan":3,"tabs":[
  {"index":0,"openBy":"","items":[
    {"prefab":"Wood","stack":50,"quality":1,"x":0,"y":0,"custom":false},
    {"prefab":"SwordIron","stack":1,"quality":3,"x":1,"y":0,"custom":true}]},
  {"index":1,"openBy":"Ragna","items":[]}]}
```

- `openBy` names the member with that tab open, or is `""`. An open tab's items are as that member last sent
  them.
- `custom` means the item carries custom data, such as an Epic Loot enchantment.
- `prefab` is `""` for an item the server does not know.

**Items JSON.** `TakeStorageItems` and `AddStorageItems` take prefab names and amounts:
`{"Wood":10,"Stone":5}`. Amounts are whole numbers above 0.

- **Taking** matches by prefab whatever the quality, from the first tab and cell on. Stacks are lowered in
  place, and every other item stays exactly as it was.
- **Adding** makes new items at quality 1. They top up stacks of the same item first, then fill empty cells.
- **`AddStorageItem`** stores a copy of your item as it is: quality, durability and custom data such as
  enchantments. A stack larger than the item's maximum is split. The item needs its prefab
  (`m_dropPrefab`), which any item made by the game has. Your own item is not changed or destroyed.

**Open tabs are never changed.** A member with a tab open has their own copy of it on screen, which would
undo the change. Takes and adds skip those tabs, and say `tabOpen` when the change would only have worked by
counting them. Try again a little later.

**The server's rules apply.** Adds follow `storage.yaml`'s list of items clan storage accepts, and race chests
are never accepted. With the server's `Features/Clan Storage` switch off, storage changes fail with
`storageOff`.

**Errors.** On `false`, `error` is one of these (also the shim's `Error*` constants):

| Error | Meaning |
|---|---|
| `notInstalled` | Clans is not installed (shim only). |
| `notServer` | Called on a client. |
| `noClan` | No such clan. |
| `badItems` | Not `{"Prefab":amount,...}` with whole amounts above 0, or no item given. |
| `unknownItem` | An item the server does not know, or an item with no prefab. |
| `storageOff` | The server has clan storage switched off. |
| `notAccepted` | The server's storage rules refuse one of the items. |
| `notEnough` | The shared tabs hold too few. |
| `noRoom` | The shared tabs have too little free space. |
| `tabOpen` | It would only work counting tabs a member has open. |

**Asking from a client.** `RequestStorageJson` asks the server and calls back once, a moment later, with
`(true, json)` in `GetStorageJson`'s shape, or `(false, error)`. The local player's rank must allow depositing
or withdrawing, the same rule as opening a shared tab in the game. Its errors are `notReady` (the server has
not welcomed the player yet), `notInClan`, `noPermission`, `storageOff`, `noServer` and `timeout`.

## Listeners and server events

| Endpoint | Called |
|---|---|
| `AddChangedListener(Action listener)` / `RemoveChangedListener` | After anything the endpoints answer may have changed. |
| `AddServerEventListener(Action<string> listener)` / `RemoveServerEventListener` | Server only: once per change, with a JSON event. |

**Changed listeners** carry nothing. Re-query what you show. On a client they run when the server's updates
arrive and when the player leaves the world. On the server they run at most twice a second, once for a burst
of changes. A listener that throws is logged and does not stop the others.

**Server events** are JSON objects with `"event"` and the id of what changed (`"clan"`, `"party"` or `"raid"`),
sent right after the change is made. Clan events carry `"clan"`:

| `event` | Other fields | When |
|---|---|---|
| `clanFounded` | `name`, `account`, `playerName` | A clan was founded. A `memberJoined` for the founder follows. |
| `clanDisbanded` | `name` | A clan was disbanded. A `memberLeft` for every member comes first. |
| `memberJoined` | `account`, `playerName`, `rankId`, `rankIndex`, `rank` | Someone joined. |
| `memberLeft` | `account`, `playerName`, `reason` | Someone left: `reason` is `left`, `kicked` or `disbanded`. |
| `rankChanged` | `account`, `playerName`, `rankId`, `rankIndex`, `rank` | A member was promoted, demoted or made leader, or their rank was removed. |
| `ranksChanged` | | The clan's ranks were renamed, reordered, added or removed. |
| `levelChanged` | `level`, `previous` | The clan's level changed. |
| `treasuryChanged` | `treasury`, `change` | The treasury changed. `change` is negative for coins out. |
| `storageChanged` | `tab` | A shared tab's contents changed, or the tab was just bought. |

Party events carry `"party"`, and raid events `"raid"`:

| `event` | Other fields | When |
|---|---|---|
| `partyJoined` | `account`, `playerName` | Someone joined a party. A new party sends this for its leader. |
| `partyLeft` | `account`, `playerName`, `reason` | Someone left: `reason` is `left`, `kicked`, `timedOut` (offline past the grace period) or `moved` (to another squad, which sends `partyJoined` next). |
| `partyLeaderChanged` | `account`, `playerName` | The party has a new leader. |
| `partyDisbanded` | | Its last member left. |
| `raidJoined` | `party`, `squad` | A party became a squad of the raid. A new raid sends this for its first squad, then its second. |
| `raidLeft` | `party` | A party is no longer a squad of the raid. |
| `raidLeaderChanged` | `account`, `playerName` | The raid has a new leader. |
| `raidDisbanded` | | Down to one squad, which is a plain party again. A `raidLeft` for each squad comes first. |

```json
{"event":"memberLeft","clan":3,"account":"Steam_76561198000000002","playerName":"Bjorn","reason":"kicked"}
{"event":"partyLeft","party":4,"account":"Steam_76561198000000002","playerName":"Bjorn","reason":"timedOut"}
```

`storageChanged` comes with every item a member moves in or out of a tab. If you rebuild something from the
whole storage, wait for the changed listener instead.

## Calling Clans without the shim

With a hard reference to `Clans.dll`, call `Clans.API` directly. It has the same endpoints, except that
`IsLoaded`, the constants, `ItemsToJson` and the single-item storage helpers are the shim's own. It also has
`GetPluginId()` and `GetEndpointNames()`.

With reflection, find `Type.GetType("Clans.API, Clans")` and bind each public static method with
`Delegate.CreateDelegate`. The storage changes declare `error` as an `out string`, which a delegate with a
`ref string` parameter binds to. [`ClansAPI/src/Clans.cs`](../ClansAPI/src/Clans.cs) is a working example.

## Recipes

### Leave clanmates alone

```csharp
// A client-side check, e.g. before your mod's area spell hits other players.
static bool Friendly(Player caster, Player target) {
    string a = Clans.GetAccountId(caster), b = Clans.GetAccountId(target);
    return Clans.AreClanmates(a, b) || Clans.AreRaidmates(a, b) || Clans.ArePartymates(a, b);
}
```

Every online player's clan, party and raid is known on every client, so this works anywhere.

### Share a kill with the party

```csharp
// On the server, when your mod hands out something for a kill.
string killer = Clans.GetAccountIdOfPeer(killerPeerId);
long party = Clans.GetPartyId(killer);
List<string> share = party == 0 ? new List<string> { killer } : Clans.GetPartyMemberIds(party);
foreach (string account in share) {
    if (Clans.IsOnline(account)) { /* reward account */ }
}
```

### Only some ranks may use your feature

```csharp
string me = Clans.GetLocalAccountId();
int rank = Clans.GetRankIndex(me);
bool officer = rank == 0 || rank == 1;                            // the leader and the rank below
bool quartermaster = Clans.HasPermission(me, Clans.PermissionStorageWithdraw);
```

Check again on the server before doing anything that matters. A client's answer only decides what to show.

### Pay a reward into the clan's treasury

```csharp
// On the server, e.g. when your bounty board's quest is turned in.
long clan = Clans.GetClanIdOf(Clans.GetAccountIdOfPeer(senderPeerId));
if (clan != 0) {
    Clans.AddTreasury(clan, 200, "$mymod_bounty_reward");
}
```

### Charge a clan in materials

```csharp
// On the server. All or nothing: nothing is taken unless everything is there.
string items = Clans.ItemsToJson(new Dictionary<string, int> { ["Wood"] = 50, ["Stone"] = 20 });
if (Clans.TakeStorageItems(clan, items, "Watchtower built", out string error) == false) {
    // notEnough, tabOpen, storageOff, ...
}
```

### Put a rolled item into clan storage

```csharp
// On the server, with an ItemDrop.ItemData your mod made (enchantments and all).
if (Clans.AddStorageItem(clan, item, "Raid loot", out string error) == false && error == Clans.ErrorNoRoom) {
    // deliver it some other way
}
```

### Keep a clan tag in your UI current

```csharp
private void Awake() {
    Clans.AddChangedListener(RefreshTags);
}

private void RefreshTags() {
    foreach (Player player in Player.GetAllPlayers()) {
        long clan = Clans.GetClanIdOfPlayer(player);
        string tag = clan == 0 ? "" : $"[{Clans.GetClanName(clan)}]";
        // ... show tag
    }
}
```

### Announce clan changes from the server

```csharp
Clans.AddServerEventListener(json => {
    var e = (IDictionary<string, object>)SimpleJson.SimpleJson.DeserializeObject(json);
    if ((string)e["event"] == Clans.EventMemberJoined) {
        string clan = Clans.GetClanName((long)e["clan"]);
        Logger.LogInfo($"{e["playerName"]} joined {clan}.");
    }
});
```

## Testing your integration

On a listen host or in single player you are the server, so every endpoint answers in full and the changes
work. To see what a plain client sees, join a dedicated server.

The `clans-admin` console command (server admins, or the host) makes test data:

| Command | Does |
|---|---|
| `clans-admin list` | Lists every clan with its id, level, members, treasury and tabs. |
| `clans-admin fakeclan Name 5` | Makes a clan called Name with 5 fake members. |
| `clans-admin gold clan 500` | Adds 500 coins to a clan's treasury (a negative number takes). |
| `clans-admin level clan 3` | Sets a clan's level. |
| `clans-admin disband clan` | Disbands a clan. |

`clan` is a clan's name, with `_` for spaces. Fake members have accounts that start with `fake:`.

## Adding an endpoint (for Clans contributors)

The public class only forwards: `Clans/API/API.cs` holds each endpoint and its documentation, and nothing else.
Behind it, in `Clans/API/Receiver/`:

| File | Holds |
|---|---|
| `IApiReceiver.cs` | One member per endpoint, of the same name and signature. |
| `ApiReceiver.cs` | Answers both sides give the same way: lookups built from others, every JSON shape, the listeners. Picks the receiver for this machine (`ApiReceiver.Current`). |
| `ServerReceiver.cs` | Answers from the server's records, sessions and parties. The only place coins and items change. |
| `ClientReceiver.cs` | Answers from what the server has pushed to a client. Changes are refused. |
| `ApiViews.cs` | The plain data the receivers hand to the shared answers. |
| `ApiEvents.cs` | Changed listeners and the server's JSON events, raised from the services. |

To add an endpoint, add it to `API.cs` as one line that calls `Receiver`, add the same member to
`IApiReceiver`, and answer it in `ApiReceiver` (when both sides can share the answer) or in both receivers.
Then add it to the shim (`ClansAPI/src/Clans.cs`) and to this page, and raise `ApiVersion`.
