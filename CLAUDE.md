# CLAUDE.md — Temporal Rebirth

Vintage Story code mod (C#, .NET 10, Harmony). What it does for players: `README.md`.
Knowledge-base entry with context and decisions: `~/repos/my/claude/projekty/vs-temporal-rebirth.md`.

## Where things stand

Read `docs/next-steps.md` first: what is done, decided, not verified, and next.

## Build and test

- SDK: `~/.dotnet` (`export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`).
- Game DLLs: `VINTAGE_STORY`, default `~/Games/VintageStory`. Flatpak install:
  `/var/lib/flatpak/app/at.vintagestory.VintageStory/x86_64/stable/active/files/extra/vintagestory`.
- `dotnet build -c Release -t:Package` -> `dist/temporalrebirth_<version>.zip`.
- `test/run.py -k TEXT` runs scenarios on a real local server with headless bots; see `test/README.md`.
  After a fix run only the affected scenarios; the full run (about 5 min, 4 servers) is for the end of a stage.
- Decompile vanilla to check patch targets: `ilspycmd -p -o <out> VintagestoryLib.dll` (also `VintagestoryAPI.dll`,
  `Mods/VSSurvivalMod.dll`, `Mods/VSEssentials.dll`).

## Where things hook into vanilla

| what | vanilla | here |
|---|---|---|
| respawn allowed? | `ServerSystemEntitySimulation.HandleSpecialKey` (private) | `Patches/RespawnPatch.cs` |
| banned players | `ServerMain.HandlePlayerIdentification` (private) | `Patches/JoinPatch.cs` |
| lives on death screen | `GuiDialogDead.ComposeDialog`, field `livesLeft` | `Patches/DeathDialogPatch.cs` |
| gear use (set spawn) | `ItemTemporalGear.OnHeldInteractStop`, 3.45 s threshold | `Patches/TemporalGearPatch.cs` |
| zombie pickup | `EntityBehaviorCollectEntities.OnFoundCollectible` | `ZombieRules.cs` |
| rifts | `ModSystemRifts.riftsById` (both sides), `Rift.GetNowSize` | `Rifts.cs` |
| game mode switch | `/player <name> gamemode <mode>` run as console | `Rebirth.SetGameMode` |

Vanilla increments `WorldData.Deaths` a tick after `Die()` (until then `EntityPlayer.DeadNotify` is set), then raises
`PlayerDeath`, and opens the death screen 2.5 s later; the mod turns the player into something at 1.5 s.

## Config

`docs/configurator/schema.js` is the single source of keys, ranges, presets and texts. It is embedded in the mod and
used by the web configurator (`docs/configurator/index.html`). `ConfigFile.Format` must equal `toFile` in `logic.js`, and
`ConfigFile.MigrateOld` must equal `migrateOld`; scenarios check the former.

## Rules

- Never copy code from Temporal Lives (git.gay/lunardiver/temporallives): it has no license.
  Same idea, own implementation.
- Downed compatibility is soft (no assembly reference): it reads `downed` and clears
  `downeddied` / `downedexecuted` watched attributes. After a Downed update, decompile it,
  check those names in `EntityBehaviorDowned` / `DownedModSystem`, then add the version to
  `DownedCompat.TestedVersions` (the server log warns about untested versions).
- Every Harmony patch is applied separately (`TryPatch`). If `RespawnPatch` fails after a game
  update, `LifeRules.Disabled` turns the whole mechanic off instead of leaving it half-working.
- Anything delayed goes by player uid and the saved `Phase`, never by a captured player object.
- `IServerPlayer.Disconnect(message)` does not show the message (1.22.7); use `ServerMain.DisconnectPlayer(client, null, text)`.
- The server console formats command answers: a lone `{x}` is dropped. `Commands.Safe` escapes it.
- Commits in English.
