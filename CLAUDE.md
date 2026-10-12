# CLAUDE.md — Temporal Rebirth

Vintage Story code mod (C#, .NET 10, Harmony). What it does for players: `README.md`.
Knowledge-base entry with context and decisions: `~/repos/my/claude/projekty/vs-temporal-rebirth.md`.

## Build and test

- SDK lives in `~/.dotnet` (not system-wide): `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`.
- Game DLLs come from `~/Games/VintageStory` (1.22.7, same as the server).
- `dotnet build -c Release -t:Package` → `dist/temporalrebirth_<version>.zip`.
- Smoke test without a client: run the dedicated server from the game install against a scratch
  data path and check `Logs/server-main.log` for errors:
  `dotnet ~/Games/VintageStory/VintagestoryServer.dll --dataPath <tmp> --port 42999`
  (put the zip, plus `downed_*.zip`, into `<tmp>/Mods/`). This only proves loading and patching;
  gameplay needs a client.
- Decompile vanilla to check patch targets: `ilspycmd -p -o <out> VintagestoryLib.dll`
  (and `Mods/VSSurvivalMod.dll`). Installed as a dotnet global tool.

## Where things hook into vanilla

| what | vanilla | here |
|---|---|---|
| respawn allowed? | `ServerSystemEntitySimulation.HandleSpecialKey` (private) | `Patches/RespawnPatch.cs` |
| lives on death screen | `GuiDialogDead.ComposeDialog`, field `livesLeft` | `Patches/DeathDialogPatch.cs` |
| gear use (set spawn) | `ItemTemporalGear.OnHeldInteractStop`, 3.45 s threshold | `Patches/TemporalGearPatch.cs` |
| rifts | `ModSystemRifts.riftsById` (both sides), `Rift.GetNowSize` | `Rifts.cs` |
| game mode switch | `/player <name> gamemode <mode>` run as console | `Rebirth.SetGameMode` |

Vanilla increments `WorldData.Deaths` before raising `PlayerDeath` and opens the death screen
2.5 s later; the mod turns an out-of-lives player into a spectator at 1.5 s.

## Rules

- Never copy code from Temporal Lives (git.gay/lunardiver/temporallives): it has no license.
  Same idea, own implementation.
- Downed compatibility is soft (no assembly reference): it reads `downed` and clears
  `downeddied` / `downedexecuted` watched attributes. Re-check after Downed updates.
- Commits in English.
