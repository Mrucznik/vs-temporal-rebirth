# Tests

- `node test/configurator/logic.mjs` — config logic of the web configurator.
- `node test/configurator/i18n.mjs [--write]` — translations are complete; `--write` regenerates the config comments.
- `test/run.py [-k TEXT] [-j SERVERS] [--no-build]` — scenarios on a real local server (127.0.0.1, account check off, throwaway
  worlds in `~/.cache/temporalrebirth-test`). Builds the mod, the bots and the helper first.

| | |
|---|---|
| `bots/` | players without graphics: join, chat, respawn, leave |
| `helper/` | server mod for tests only: reads state, uses gears, adds rifts, deals damage, moves clocks |
| `harness.py` | starts servers and bots, waits for what should happen |
| `scenarios/v06_baseline.py` | what 0.6.x does; a migrated config must keep passing after the rewrite |
| `scenarios/races.py` | leaving, rejoining, crashing or acting twice while the mod waits for something |
| `scenarios/v07.py`, `v08.py`, `v09.py` | what these versions add |
| `scenarios/review07.py` | faults found by reading 0.7.0 |
| `scenarios/downed.py` | together with Downed |

Worlds are flat unless a group says `"flat": False` (races in unfinished land need real terrain). Move bots with `Bot.go`, which
waits until they arrive; fixed waits fail at random under load.

`@known_bug(text, failure)` expects the test to fail with `failure`. When the bug is fixed the run reports `FIXED`: remove the mark.

- `node test/configurator/ui.mjs file://$PWD/docs/configurator/index.html <out dir>` — drives the page in headless Chrome, screenshots included.

Not covered: anything drawn on a client (timer, wisp, echo, leash, dialogs) and real item input. Bots do not move on their own.

## On another computer

Linux only: the runner uses POSIX signals and replaces paths in server configs. Checked from a fresh clone with an empty work
folder. Everything needed that is not in the repository:

| what | where the tests look for it | how to get it |
|---|---|---|
| .NET 10 SDK | `~/.dotnet/dotnet`, or the path in `DOTNET` | `curl -sSL https://dot.net/v1/dotnet-install.sh \| bash -s -- --channel 10.0` |
| Vintage Story 1.22.7 | `~/Games/VintageStory`, the Flatpak install, or the folder in `VINTAGE_STORY` | the game's own download, or `flatpak install at.vintagestory.VintageStory`; no login is needed for these tests |
| Python 3 | `python3` | used with 3.14, standard library only |
| Node | `node` | used with 22; the configurator tests and some of `v07.py` need it, `ui.mjs` needs at least 22 |
| Chrome | `google-chrome` | only for `configurator/ui.mjs` |
| Downed 2.9.4 | `~/.cache/temporalrebirth-test/mods/downed_2.9.4.zip` | https://mods.vintagestory.at/downed; only for scenarios with Downed, the others run without |
| ilspycmd | anywhere | `dotnet tool install ilspycmd --tool-path <folder>`; only to look into the game after an update |

`TRT_WORK` moves the work folder. It grows to about 600 MB and can be deleted at any time except for `mods/`; the template
worlds in it are made again on the first run (about 30 s each). After a game update delete them too.

After a game update also change the versions in `bots/Bot.cs` (`MdProtocolVersion`, `NetworkVersion`, `ShortGameVersion`) and
check the packet ids there; bots that "could not join" are the sign.

Seeing what a client draws needs a logged in game as well: see "Level 3 tests and GIFs" in `docs/next-steps.md`.
