# ClansAPI

This is a typed, soft-dependency wrapper over the Clans mod's `Clans.API`: who is in which clan, clan levels,
members' ranks and rights, parties and raids, and each clan's treasury and storage. The full reference is
[docs/API.md](../docs/API.md).

## Use it

1. Get it into your plugin in one of two ways:
   - **Copy** [`src/Clans.cs`](src/Clans.cs) into your project. It is one self-contained file.
   - **Bundle** `ClansAPI.dll` into your plugin with ILRepack.
2. Declare a soft dependency on the plugin, so Clans loads first:

   ```csharp
   [BepInDependency("MidnightsFX.Clans", BepInDependency.DependencyFlags.SoftDependency)]
   ```

3. Call it:

   ```csharp
   using ClansAPI;

   long clan = Clans.GetClanIdOfPlayer(player);
   if (clan != 0 && Clans.GetClanLevel(clan) >= 3) { /* ... */ }
   ```

Every method is safe when Clans is not installed. Queries return `0`, `-1`, `false`, `""` or an empty list;
changes return `false` with the error `notInstalled`. Use `Clans.IsLoaded()` when you need to know which case
you are in.

Each endpoint is bound once to a typed delegate on first use, so a call costs no reflection. If the installed
Clans is older and lacks an endpoint, that one method keeps returning its default. Probe with
`HasEndpoint(name)` or `GetApiVersion()`.

## Build

```bash
dotnet build ClansAPI/ClansAPI.csproj -c Release
```

The build finds the game and BepInEx through the repository's `Directory.Build.props`. The DLL has version
0.0.0.0 on purpose, so consumers never need a rebuild when Clans' version changes.
