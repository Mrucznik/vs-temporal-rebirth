# State and next steps

As of 2026-11-25, everything is on `main` (0.10.0-dev.1). Released: 0.6.1.

## Done

- **Web configurator** (`docs/configurator`) in en, pl, de, ru, picked with flags. Not published. Polish texts are in `schema.js`,
  the other languages in `i18n/`. After changing a text run `node test/configurator/i18n.mjs --write`: it names what is missing
  and regenerates `i18n/comments.json`, the comments the mod writes into its config file (in the server's language).
- **0.7.0 mechanics** (`0.7.0-dev.1`): config with migration, presets, commands, privileges, forms (Ghost, Spectator, Zombie,
  Player, Ban), places, time, return, rituals, lives limit, custom messages (en, pl).
- **0.8.0** (`0.8.0-dev.1`): zones, `Lives.Count` Downs and DownsFinalDeath, timer, wisp.
- **0.9.0** (`0.9.0-dev.1`): protection after rising, leash on the client, echo.
- **0.10.0** (`0.10.0-dev.1`): rituals you can see. The gear's held help says "Perform the ritual (N waiting)" or why not (too
  far, cooldown, nobody to bring back, caster cannot); with nobody waiting it stays vanilla. The server sends this state to
  whoever holds a gear (each second when it changes, at once on switching to the gear) and decides everything again. Only a hold
  that will be a ritual looks like one: rising sound, rust motes, a bar under the crosshair (others nearby see motes sent by
  the server). With several players to bring back a chooser lists them (name, form, time left, online); the gear is taken
  only when the server accepts the click. One candidate, `/rebirth target` or NearPlayer (nearest) need no chooser. Anywhere
  always opens it while someone waits, because it also offers the vanilla "bind me here". Scenes R1–R3 in `test/scenes/shoot.py`
  (hint, hold, chooser) are written but not recorded. `Messages.RitualChoose` is no longer used.
- **After the test session of 2026-11-25** (notes, screenshots and the crash log in `docs/sessions/2026-11-25/`):
  - Leash on the client (`LeashGuard`): no more freezing where the edge meets blocks (the bottom of a sphere, a crack by a
    cliff): the step tries several ways along the edge and takes the first that is free; the edge from below is ground to
    walk and jump on. A position that is not a number is put back to the last good one (it crashed the game at a zone's
    edge, and a zone handed back an argument that was then moved twice). Spheres hold the player 0.6 blocks inside the drawn
    wall, so the camera no longer slips past it. The "Something holds you here" chat message is gone.
  - Barrier (`leash.fsh`): the noise is taken in 3D on the wall, so the sphere has no pinch at its top and bottom and no seam;
    gears fade out there. The dark veil only near the player. Faint own embers near the player and where it is hit, so it
    shows in a dark cave. Zones (land claims, up to 8 nearest boxes) are drawn too, the same look, from 50 blocks. About
    10 % dearer than right after the optimization, still about 20 % cheaper than before it (`test/shaders/bench.py`, which
    now also looks at the top of a sphere and at an L of claims).
  - Rituals next to ghosts: the ghost looked at is brought back (`RitualAim`; a hold can start in the air at a wisp); several
    and none looked at opens the chooser. The hint says to look at the ghost. Motes are big enough to be seen and gather on
    the ghost; the end is heard (`translocate-breakdimension` and a low bell). The revived player turns to the caster. A
    cooldown switched on later does not count rituals from before (`temporalrebirth-cooldownsince` in the save).
  - The gear's description says it can call back a friend, and where, when the server has rituals.
  - Zombies that may not use items do not swing what they hold, and are told to fight bare-handed.
  - The timer says what is waited for (ghost, undead, exile, ...). The death screen says "1 life" and, at no lives with a
    respawn (lives counted by downs), that the next fatal blow is the last.
  - Echo: the silhouette has the game's player proportions (small head, thin long arms), not a blocky Minecraft one.
  - Scenes: `play.py` waits for Enter before stopping the server, keeps each run's logs in `~/.cache/temporalrebirth-gifs/logs`,
    resets `Lives.Count` between scenes, and the test worlds have temporal stability off. T12 rewritten, T13 and T14 new.
  - Not a bug: "no ghost after the last death" in the session was `Lives.Count` Downs left over from an earlier scene (presets
    keep the lives section); with Downs a death without a down costs a life and the next fatal hit is the last.
- **Recording (2026-11-26)**: particles are spawned and moved at the speed of time (`Calendar.SpeedOfTime / 60`), so the
  scenes' old `/time stop` hid every particle: motes, the flash, the wisp's trail; scenes no longer stop time. The motes were
  also blue (red and blue were swapped on a wrong guess). The wisp's trail is dark smoke now, not a bright streak.
  `test/recorder` is a client mod for recording only: it hides the hotbar and the bars, and Scroll Lock holds the right
  button (a right click sent with xdotool on the GPU display frees the pointer instead). `shoot.py` installs it, switches the
  minimap off, and removes both afterwards. M1 re-recorded with a real gear hold.
- **Don't Starve preset and rift bargains (2026-11-26)**: `Ritual.HealthCost` (bars, 0 off): at an active rift that a ghost is
  at too, a living player holds the right button without a gear and the revived player loses that much maximum health for
  good (`maxhealthExtraPoints`, code `temporalrebirth`, kept by the game; `temporalrebirthHealthLost` on the entity says how
  much). `Ritual.HealthMin` (5): who would drop below comes back only with a gear. A gear offered to a rift (hint state
  `Offer`) gives back as much as a revival takes. `Ritual.SelfAtRift`: a ghost hanging in a rift for 5 s comes back alone,
  paying the health cost (free when it is 0); works with any preset. Preset DontStarve: ghost, free, no time limit, rituals at
  rifts, `InPlace`, cost 2, alone at rifts. The bare-hand hold is watched by the client (no item to hold) and reported with
  `HandHoldPacket`; the server checks again. Scenarios in `test/scenarios/riftbargain.py`, scene T15 for a client.
- **Review of 0.7.0**: 11 faults, each with a test and fixed.
- **Structure and performance**: `Rebirth.cs` split up, Harmony patches reach the server side through
  `TemporalRebirthModSystem.Server`, how a player comes back is one saved object, less work per frame and tick (not measured).
  Second pass (branch `refactor`, 2026-11-24): settings kept from the time of death come from `schema.js` (`atDeath` on a
  section), the rules of a player out of lives are put together by `Afterlife.Rules`, `Rebirth.Enter` split into steps, one log
  prefix (`ModLog`), scenarios grouped by mechanic with shared steps in `test/steps.py`.
- **Tests**: 116 scenarios on a real server plus configurator tests. Last full run 116/116 in about 10 min.

## Decided by the user

- Lives out: form, place and time are independent. Presets: Ghost (the default), Rift, Undead, Exile, Timeout, DontStarve, Hardcore.
- Ghost (was Classic): rituals next to the ghost, who rises where the ghost is. Rift (was Rescue): no time limit, rituals at
  rifts, revived next to the caster. A migrated 0.6.x file gets rituals at rifts next to the caster written out, as 0.6 did.
  Test worlds without a config get the same (`harness.World`, `steps.settings`), since most scenarios assume it.
- Old configs must not change behaviour on update: ghost invisible, timer off.
- `/rebirth revive` returns a player to "Return.At". No exception for hosts in bans. No `/rebirth kill`, no character wipe.
- Rituals only by a normal, living player. `InPlace` = nearest safe ground. `NearPlayer` needs the target online.
- Running sentences keep the settings from the time of death; rituals use the current ones.
- `Lives.Count`: Deaths; Downs (3 lives = 3 downs, the next fatal hit is the last; death after a down costs nothing, death
  without one costs a life); DownsFinalDeath (a death takes all lives). Changing it keeps everyone's lives.
- Being downed while out of lives costs nothing.
- Ghost look: no player model needed. Wisp = floating smouldering light in the rift's dark rust, with sparks (was teal; the
  user confirmed the rust look 2026-11-25). Echo = silhouette refracting the world like a rift, rust rim, dissolving downwards, with name; only for players holding a temporal gear. If the shader fails, wisp only.
  Own shader, do not copy the game's. Spectators stay invisible.
- Timer: always visible on screen, top centre to start with; off switch `Time.ShowTimer`.
- Zones: land claims shared with the group `Place.Group`. The mod grants `Place.Access` itself; claims only give the shape.
  Others may enter. No claims = nobody is held, and the log says so.
- Languages at the end: en, pl, ru, de. German and Russian are a nice-to-have, nobody will proofread them. Custom messages replace the text for everyone.
- Limits of walking forms (2026-11-26): six switches each for the zombie (`Zombie.Can...`) and the exile (`Exile.Can...`):
  items in hand, building, using blocks, creatures, picking up, hitting. A zombie has all but hitting off (fights
  bare-handed), an exile has none. Who may not use items does not even swing them, who may not hit does not swing at
  creatures, both with a message. Dropping items stays allowed. What is lost at death follows the world's own setting.
  The wiki still describes only `Zombie.CanUseItems`: Settings-Guide needs `#zombie` and `#exile` sections for the switches.
- Rituals next to ghosts (2026-11-25): aim at the ghost; several and none aimed at: the chooser. The revived rises where the
  ghost was, turned to the caster. A cooldown counts only rituals after it was switched on; with nobody waiting the gear's
  hint stays vanilla, also during a cooldown.
- Ranges kept: echo 48 blocks (name 24), wisp 96.
- `/rebirth import` sets everything (what is not in the code is at its default), lists what changed and names the `.bak`.
- Leash: a sphere around the place, for everyone whose movement is limited. Nearly invisible from afar, its wall shows only up
  close; looks akin to a rift (own shader, not a copy); glows where it is hit. The client stops the player at the edge; the
  server only checks now and then for being far outside. Zones (boxes) stop smoothly too and are drawn the same way, visible
  from close (about 50 blocks).
- Zombies and normal players out of lives cannot be hurt for 5 s after they rise, in addition to the safe ground and the move to
  the spawn point after a second quick death.

## Next

1. A second test session for what changed on 2026-11-25 (T12 rituals with aiming, motes and the end sound; the barrier at
   the top, in a cave, near the edge and under the player; zone walls; T13 zombie swing; T14 Downed; T15 Don't Starve), on a
   client; then one with two real players.
2. Zombie look rework (the user wants it; how is not decided yet). The drifter look is an idea for 1.1, see Ideas.
3. Media: the mod's page uses videos, the GitHub wiki needs no GIFs. ModDB takes a YouTube trailer and YouTube embeds in the
   description; gallery images at most 2 MB each (12). Decide ModDB media, then re-record what changed (barrier, rituals).
4. Publish the configurator; new ModDB text and README.
5. Release 0.10 in place of 0.6.1 (GitHub, ModDB, mruserv).

## Ideas (not decided)

### Learning the server's rules in the game (user's idea, 2026-11-24)

- A player should be able to find out which mechanics are active on the server (what happens after the last death,
  how long it lasts, where rituals work, ...) without the wiki or knowing the config.
- In the game and in its mood, the way Vintage Story's lore is found (notes, books, tablets, inscriptions), not as a
  chat message.

### Zombie as a drifter (user's idea, 2026-11-26; for 1.1, not before the 0.10 release)

- The zombie looks like a drifter instead of a tinted player. The limits stay as they are (no items, hotbar, building,
  breaking; bare hands may fight), now switchable per form.
- Holding the throw button throws a stone, like a drifter does, without having one in the inventory. Needs a cooldown or
  a limit so it is not a machine gun. The game's drifter shapes have the animations (`standgetrock`, `standthrowright`).
- Crouching makes it crawl (the drifter's `crawl*` animations).
- Existing mod: [Role-Playables: Drifters](https://mods.vintagestory.at/rpdrifters) (`rpdrifters`, by Greybones, 0.0.2,
  game 1.22.0-1.22.3, no license and no source shown). It swaps the player model through
  [Player Model Library](https://mods.vintagestory.at/playermodellib) (needs Overhaul Library and JSON Patches Library):
  five drifter types, their textures and voices, crawling when crouched, immunity to temporal stability loss and being
  unseen by enemy creatures. The author calls it janky (some animations borrowed from the seraph, armour scaled oddly)
  and is on a hiatus. A model is unlocked by an admin (`/player <name> enablePlayerModel rpdrifters:rpdriftersurface`)
  and chosen by the player (`.charsel`).
- Way in: a soft integration. With PML and rpdrifters on the server a zombie gets the drifter model, without them the tint
  stays. The open question: can the server set a player's model through PML (an API), or only the player with `.charsel`?
  Check PML's code first and try rpdrifters on 1.22.7.
- Without PML: our own shape swap in `EntityBehavior.OnTesselation` with the drifter shape from
  `shapes/entity/lore/drifter/` and its animations mapped to the player's codes (`standwalk` -> `walk`, `standrun` ->
  `sprint`, `standidle` -> `idle1`, ...). Unknowns: textures through the player's skin system, attachment points for held
  items and armour.

## Not verified

Seen on a client in the session of 2026-11-25 (and working): the gear's hint and its refresh, the hold's sound and bar, the
chooser, the cooldown, the hint at and away from a rift; wisp and echo by day, night, dusk, fog, from 30 and 60 blocks,
through glass, leaves, water and from under it; the barrier from inside and when hit, on a slope; a zone of claims holding the
player; the timer with chat, dialogs, block names and interface scales; zombie item limits with real input; the death screen
with Downed (respawn at 0 lives with downs); three ghosts at 30-35 frames a second on the laptop.

Not yet seen on a client:
- Everything changed after the session (see Done): the new barrier and zone walls, aiming at ghosts, motes, the end sound,
  turning to the caster, the gear's description, the zombie swing, the timer texts, the new death screen texts, the new echo.
- A zombie in lava; a zombie in armour or with a torch; relogs (bots cover them on the server); creatures and zombies.
- Loading of privileges after a restart (saving is verified). Client chat length for long `/rebirth import` codes.

## Level 3 tests and GIFs

Xvfb, xdotool and ffmpeg are installed. Software rendering, so screenshots rather than smooth video. The game needs a logged
in data folder: the user logs in once with the same account in a separate folder; never copy their session. The Flatpak client
lives in `at.vintagestory.VintageStory`; `--dataPath` picks the folder.
