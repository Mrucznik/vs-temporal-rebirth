# State and next steps

As of 2026-11-22, branch `presets` (not pushed). Released: 0.6.1.

## Done

- **Web configurator** (`docs/configurator`), Polish only. Not published.
- **0.7.0 mechanics** (`0.7.0-dev.1`): config with migration, presets, commands, privileges, forms (Ghost, Spectator, Zombie,
  Player, Ban), places, time, return, rituals, lives limit, custom messages (en, pl).
- **Tests**: 76 scenarios on a real server plus configurator tests. Last full run before 0.7.0 scenarios: 47/47; 0.7.0
  scenarios pass. Known bugs of 0.6.x are fixed.

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

## Next: 0.8.0

1. Zones (`Place.At` Zone): `LandClaim.PositionInside`, `PermittedPlayerGroupIds`, access via `OnTestBlockAccessClaim`;
   pull back to the last step inside.
2. `Lives.Count` Downs / DownsFinalDeath: listen to the watched attribute `downed` on the server (checked in Downed 2.9.4).
   Do not subscribe to Downed's `ShouldPlayerDie`, it changes its flow.
3. Timer (`HudElement`, client).
4. Wisp (client): spectators are hidden by `EntityShapeRenderer.isSpectator` only.
5. Scenarios for each, including relogs and restarts.

Then: GIFs for the user to look at; 0.9.0 echo; localization; publish the configurator; new ModDB text. Remove
`WarnAboutUnfinished` when nothing is unfinished.

## Not verified

- Anything drawn on a client: zombie look, death screen, configurator file comments in other languages.
- Zombie item limits (bots do not use items). Loading of privileges after a restart (saving is verified).
- Client chat length for long `/rebirth import` codes (the server takes 3000 characters).
- Whether clients get positions of ghosts; the in-game recorder (`.cam rec`, AVI) on Linux.

## Level 3 tests and GIFs

Xvfb, xdotool and ffmpeg are installed. Software rendering, so screenshots rather than smooth video. The game needs a logged
in data folder: the user logs in once with the same account in a separate folder; never copy their session. The Flatpak client
lives in `at.vintagestory.VintageStory`; `--dataPath` picks the folder.
