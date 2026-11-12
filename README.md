# MDSAPI

An API layer over Schedule I, by BlessingMds. Mods written against it are plain C# — one
codebase that runs on both of the game's runtimes, depending on a surface we control
rather than on anyone else's release schedule.

**Status: v0.2 — the surface StackLimit Plus needs.** Items, products, stations, storage,
vehicles, player controls, input, lobby data, phone apps and HUD pieces, on top of v0.1's
lifecycle, save and interop plumbing. Scoped by measurement: these are the 45 game types a
real, shipped mod (StackLimit Plus, ~50k downloads) actually touches. The public surface is
not frozen until 1.0; `0.x` means it can still change.

**Built against:** Schedule I 0.4.7f12, MelonLoader 0.7.3, Unity 2022.3.62f2.
**Verified on:** both runtimes, running in-game. Loader, registration, lifecycle hooks and
save-root resolution confirmed from real sessions on the default (IL2CPP) and alternate
(Mono) branches.

## What's here

| | |
|---|---|
| `MDSAPILoader` | MelonLoader plugin. Picks the Mono or IL2CPP build at startup so players get one download. |
| `GameLifecycle` | `OnPreLoad`, `OnLoadComplete`, `OnSaveStart`, `OnSaveComplete`. Subscribe in `OnInitializeMelon`, act when they fire. |
| `Saveable` | Mark a private field `[SaveableField("key")]` and it persists beside the player's save. For per-save data: progress, grudges, employee skills. |
| `ModConfig` | Player settings that apply to every save — keybinds, limits, window sizes. Readable JSON in UserData, available from `OnInitializeMelon` before any save exists. |
| `GameWrapper<T>` | Base for wrappers, with one `IsValid` that handles Unity's destroyed-object semantics and IL2CPP's collected-object throw. |
| `Log` | One startup line, then only what someone would act on. |
| `Singletons` | Resolves the game's managers by name, cached, cleared on scene unload. |
| `Money` | Cash, online balance, net worth, formatted amounts. |
| `Items` | `ItemDefinition` (ID, name, icon, category, `StackLimit`), `ItemInstance`, `ItemSlot`; `AddCapacityFilter` (enforced for every slot in the game), `DefinitionRegistered` (mixes as they appear), `ItemDrag.BeforeDrop`. |
| `Products` | Drug type of a product, safely; the base product each mix is cloned from. |
| `World` | `Stations` and `StationType`, `Buildable` (GUID, name, station capacity, beds), `Storage`, `SlotOwner`, `Vehicles`, `Interaction` (what the player is looking at, the open storage). |
| `Players` | `LocalPlayer.Exists`; `PlayerControls.Block/Unblock` — take and return the camera, cursor, movement and equipping for a menu, correctly; `Controls.IsTyping`, `AddExitHandler`. |
| `Multiplayer` | `Lobby` — host/client, and lobby data for sending settings to clients. |
| `UI` | `Phone` (open state, home-screen icon, the app area, close-apps), `InteractionPrompt` (the game's "[E] Use" prompt, to copy), `HudQuest` (a quest in the game's HUD list). |
| `Patching` | `Patch` (Harmony by candidate names), `CodeConstant` (override a compiled-in `const`). |
| `Updates` | `UpdateCheck.Register` — once a day, checks GitHub for a newer release of MDSAPI or your mod and puts one quiet, clickable line on the main menu. `MdsApi.Requires("0.2.0", "My Mod")` — a mod that needs a newer MDSAPI says so clearly instead of failing with missing-method errors. |
| `Unity.Cross` | The Unity calls spelled differently per runtime: UnityEvent listeners, real type names, `ObjectKey` for identity. |

Every game object comes back as a small handle (`ItemSlot`, `Buildable`, ...) whose members
are compiled delegates bound by candidate name — about the cost of a direct access, and a
renamed member costs one entry in a list here instead of a broken mod. Each module reports
whether it bound; a module that did not is off on its own and takes nothing else with it.

## Update check and privacy

MDSAPI asks `api.github.com` (or Thunderstore, for mods published there) for the latest release
of MDSAPI and of every installed mod that uses it or is by BlessingMds — at most once a day per
repository (cached in `UserData/MDSAPI/update-check.json`). Nothing is sent but those requests,
and nothing is ever downloaded or installed.

A newer version shows as a card on the main menu: a warning mark, the mod and version (click to
open its download page), and an underlined *Opt out of automated checks*, which asks for
confirmation and explains what the check connects to. The card takes the first screen corner no
other UI (the game's or another mod's) is drawn in; if all four are taken it appears over the
Continue screen instead. It never shows during play.

Where each mod is checked: BlessingMds mods from `mods.json` in this repository (a new mod is
covered by adding a line there); any other mod built on MDSAPI from `UpdateCheck.Register`, or
failing that the GitHub or Thunderstore link in its `MelonInfo` download link. A mod with only a
Nexus link can't be checked (Nexus's API needs a personal key) and is named once in the log. The
card only opens https pages on github.com, nexusmods.com and thunderstore.io.

**For mod authors:** to get update notices for your MDSAPI mod, either put your GitHub or
Thunderstore page in `MelonInfo`'s download link, or call
`UpdateCheck.Register("My Mod", "1.2.0", "You/MyMod", "https://www.nexusmods.com/...")`.

Releases must be tagged with their version (`v0.3.0` or `0.3.0`) for the comparison to work.
`"PreviewUpdateNotice": true` in `UserData/MDSAPI.json` shows a sample card, for checking it.

## Using it

```csharp
public sealed class MyMod : MelonMod
{
    public override void OnInitializeMelon()
    {
        GameLifecycle.OnLoadComplete += () => Log.Info("World is up.");
    }
}

public sealed class MyData : Saveable
{
    [SaveableField("entries")] private List<string> _entries = new();
    protected override void OnLoaded() { /* rebuild runtime state from _entries */ }
}
```

Managers do not exist during `OnInitializeMelon` — the main scene has not loaded.
Subscribing there and acting on `OnLoadComplete` is the whole pattern.

## Building

```bash
cp example.build.props local.build.props   # then point GameDir at your install
dotnet build -c Il2Cpp
dotnet build -c Mono
```

`local.build.props` is gitignored because it holds a machine-specific path. Game and
MelonLoader assemblies are referenced from your install and never copied to output — they
are not ours to redistribute.

Both configurations compiling proves nothing about either running. Parity needs two runs.

## Runtimes

| Steam branch | Runtime | Build | Status |
|---|---|---|---|
| default / `beta` | IL2CPP | `Mods/MDSAPI/Il2Cpp/MDSAPI.dll` | v0.1 verified in-game; v0.2 pending |
| `alternate` / `alternate-beta` | Mono | `Mods/MDSAPI/Mono/MDSAPI.dll` | v0.1 verified in-game; v0.2 pending |

Both builds carry the assembly identity `MDSAPI` and differ only by folder, so a mod
references `MDSAPI`, compiles once, and runs on either branch. `MDSAPILoader` is a single
`netstandard2.1` assembly that runs on both runtimes and picks the matching MDSAPI at
startup — one plugin, one download, no branch awareness required of the player.

Type names match exactly across the two — 2,076 `ScheduleOne.*` types on each side, zero
divergence. Member *shapes* do not: `LoadManager.onPreLoad` is
a **field** on Mono and a **property** on IL2CPP, and the same holds for the other lifecycle
members. That is why `GameLifecycle` resolves them reflectively rather than naming them
directly — writing `loadManager.onPreLoad` would compile on one runtime and fail on the
other, and guessing wrong fails silently, which is the worst outcome for a lifecycle hook.
Both runs confirm it: the same binary logic logs `Lifecycle hooks attached (field)` on the
alternate branch and `(property)` on the default one.

A third difference only shows up at runtime. On Mono a UnityEvent listener is an ordinary
delegate; on IL2CPP it is an `Il2CppSystem.Action`, an interop object wrapping a native
pointer rather than a `System.Delegate`. `Delegate.CreateDelegate` throws *"Type must derive
from Delegate"* against it, and `Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<T>`
has to do the conversion. `Internal/Interop/Delegates.cs` handles both and reaches
Il2CppInterop reflectively, so the Mono build never links an assembly from the other branch.

### Notes from getting it running

Four things cost a build cycle each, all of them the host platform rather than the game:

- `LangVersion latest` compiles `$"..."` into `DefaultInterpolatedStringHandler`, a .NET 6
  type the Mono runtime does not have. The assembly loads and throws on first format.
  Both configurations use C# 9 so this cannot regress on one runtime only.
- Both loader configurations deployed to the same `Plugins\MDSAPILoader.dll`, so whichever
  built last won — a net6.0 loader on the Mono branch. Hence one loader assembly.
- `Assembly.LoadFrom` loads an assembly without registering the melon inside it, and
  `MelonAssembly.LoadMelonAssembly` creates the melon without starting it. `Register()` is
  what starts it, and it has to happen in `OnPreModsLoaded` — registration during
  `OnPreInitialization` is dropped.
- `Type.GetMethod(name, flags)` throws `AmbiguousMatchException` on any type with two
  overloads of that name, which `UnityEvent` has.

The runtime split is kept to the few places that cannot avoid it: casting and identity
(`Cast`, `Cross`), Unity's Type-taking calls (`UnityFind`), the one IL2CPP delegate field
(`Phone.OnCloseApps`), and `CodeConstant`, which is a different technique per runtime. A
`#if` anywhere else is a sign the difference belongs in one of those.

## Layout

```
MDSAPI/
  Internal/Interop/      GameTypes, Access, Cast, Seq, UnityFind, Hooks, Singletons, ...
  Items/ World/ Players/ Multiplayer/ UI/ Money/ Config/ Patching/ Unity/
  Internal/Abstraction/  GameWrapper<T>, Registerable, Saveable
  Lifecycle/             GameLifecycle
  Logging/               Log
MDSAPILoader/            the runtime-picking plugin
```

Game types never appear in a public signature — not as a parameter, a return, a generic
argument or a public field. That rule is the whole reason this layer exists: when a game
update renames something, one wrapper changes and every mod keeps compiling.

## Installing (for players)

```
Plugins/MDSAPILoader.dll
Mods/MDSAPI/MDSAPI.Mono.dll
Mods/MDSAPI/MDSAPI.Il2Cpp.dll
```

Both MDSAPI DLLs are meant to be there. The loader picks one.
