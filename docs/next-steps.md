# State and next steps

As of 2026-11-23, branch `presets` (not pushed). Released: 0.6.1.

## Done

- **Web configurator** (`docs/configurator`) in en, pl, de, ru, picked with flags. Not published. Polish texts are in `schema.js`,
  the other languages in `i18n/`. After changing a text run `node test/configurator/i18n.mjs --write`: it names what is missing
  and regenerates `i18n/comments.json`, the comments the mod writes into its config file (in the server's language).
- **0.7.0 mechanics** (`0.7.0-dev.1`): config with migration, presets, commands, privileges, forms (Ghost, Spectator, Zombie,
  Player, Ban), places, time, return, rituals, lives limit, custom messages (en, pl).
- **0.8.0** (`0.8.0-dev.1`): zones, `Lives.Count` Downs and DownsFinalDeath, timer, wisp.
- **0.9.0** (`0.9.0-dev.1`): protection after rising, leash on the client, echo.
- **Review of 0.7.0**: 11 faults, each with a test (`test/scenarios/review07.py`) and fixed.
- **Structure and performance**: `Rebirth.cs` split up, Harmony patches reach the server side through
  `TemporalRebirthModSystem.Server`, how a player comes back is one saved object, less work per frame and tick (not measured).
- **Tests**: 116 scenarios on a real server plus configurator tests. Last full run 116/116 in about 10 min.

## Decided by the user

- Lives out: form, place and time are independent. Presets: Classic (= 0.6.x), Rescue, Undead, Exile, Timeout, Hardcore.
- Old configs must not change behaviour on update: ghost invisible, timer off.
- `/rebirth revive` returns a player to "Return.At". No exception for hosts in bans. No `/rebirth kill`, no character wipe.
- Rituals only by a normal, living player. `InPlace` = nearest safe ground. `NearPlayer` needs the target online.
- Running sentences keep the settings from the time of death; rituals use the current ones.
- `Lives.Count`: Deaths; Downs (3 lives = 3 downs, the next fatal hit is the last; death after a down costs nothing, death
  without one costs a life); DownsFinalDeath (a death takes all lives). Changing it keeps everyone's lives.
- Being downed while out of lives costs nothing.
- Ghost look: no player model needed. Wisp = floating teal light with sparks. Echo = silhouette refracting the world like a
  rift, teal rim, dissolving downwards, with name; only for players holding a temporal gear. If the shader fails, wisp only.
  Own shader, do not copy the game's. Spectators stay invisible.
- Timer: always visible on screen, top centre to start with; off switch `Time.ShowTimer`.
- Zones: land claims shared with the group `Place.Group`. The mod grants `Place.Access` itself; claims only give the shape.
  Others may enter. No claims = nobody is held, and the log says so.
- Languages at the end: en, pl, ru, de. Custom messages replace the text for everyone.
- Zombies without items: no weapons, no interacting with creatures; attacking bare-handed is allowed.
- `/rebirth import` sets everything (what is not in the code is at its default), lists what changed and names the `.bak`.
- Leash: a sphere around the place, for everyone whose movement is limited. Nearly invisible from afar, its wall shows only up
  close; looks akin to a rift (own shader, not a copy); glows where it is hit. The client stops the player at the edge; the
  server only checks now and then for being far outside. Zones (boxes) stop smoothly too, but have no drawn walls for now.
- Zombies and normal players out of lives cannot be hurt for 5 s after they rise, in addition to the safe ground and the move to
  the spawn point after a second quick death.

## Next

1. The user looks at what 0.8.0 and 0.9.0 draw (they are logged in): GIFs of the timer, wisp, echo and leash. Frame rate with the
   leash in view.
2. German and Russian read by someone who speaks them (terms follow the game's own translations); publish the configurator; new ModDB text and README.

## Not verified

- Anything drawn on a client: timer, wisp, echo, leash (both shaders compile and look right in WebGL, not seen in the game), zombie look, death screen (with a respawn button at 0 lives for Downs),
  client prediction in zones. Being stopped at the edge (bots do not move on their own).
- Zombie item limits with real input (bots do not use items; attacks and clicks on creatures are tested as events). Loading of privileges after a restart (saving is verified).
- Client chat length for long `/rebirth import` codes (the server takes 3000 characters).
- The in-game recorder (`.cam rec`, AVI) on Linux.

## Level 3 tests and GIFs

Xvfb, xdotool and ffmpeg are installed. Software rendering, so screenshots rather than smooth video. The game needs a logged
in data folder: the user logs in once with the same account in a separate folder; never copy their session. The Flatpak client
lives in `at.vintagestory.VintageStory`; `--dataPath` picks the folder.
