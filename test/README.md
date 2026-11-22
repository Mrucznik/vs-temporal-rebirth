# Tests

- `node test/configurator/logic.mjs` — config logic of the web configurator.
- `test/run.py [-k TEXT] [-j SERVERS] [--no-build]` — scenarios on a real local server (127.0.0.1, account check off, throwaway worlds in
  `~/.cache/temporalrebirth-test`). Needs the .NET 10 SDK in `~/.dotnet` and the game (`VINTAGE_STORY`, default install or Flatpak).
  The Downed scenarios need `downed_<version>.zip` in `~/.cache/temporalrebirth-test/mods`.

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

`@known_bug(text, failure)` expects the test to fail with `failure`. When the bug is fixed the run reports `FIXED`: remove the mark.

- `node test/configurator/ui.mjs file://$PWD/docs/configurator/index.html <out dir>` — drives the page in headless Chrome, screenshots included.

Not covered: anything drawn on a client (timer, wisp, echo, leash, dialogs) and real item input. Bots do not move on their own.
