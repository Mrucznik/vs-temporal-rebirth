#!/usr/bin/env python3
"""Scenes for screenshots and GIFs: test/scenes/play.py T1 [M1 ...]   on a local server, with bots, for one real client.

Prints what to do and when. --check plays the scenes without a client (a bot stands in), --list names them,
--no-build skips the build. Never start this while test/run.py of someone else is running.
"""
import argparse, subprocess, sys, time
from stage import AIR, GLASS, GRASS, LAVA, LEAVES, STONE, WATER, Failed, Stage, harness, use

GEAR_USE = "hold right mouse on the ground block for 4 s"
SCENES = {}


def scene(name, about, mods=()):
    def add(f):
        SCENES[name] = (f, about, mods)
        return f
    return add


def ghost_of(s, name="Funia", dx=8, dz=0, above=1, **config):
    """A ghost a few blocks east of the camera. Returns its bot."""
    ghost = s.bot(name)
    use(s.w, "Ghost", **{"Ghost.Visible": "WispAndEcho", "Time.Duration": "24h", "Place.Radius": 0, **config})
    s.put(ghost, dx, dz)
    s.lose(ghost)
    s.put(ghost, dx, dz, above)
    s.expect(ghost, dx, dz)
    state = ghost.state()
    if state["look"] != config.get("Ghost.Visible", "WispAndEcho"):
        raise Failed(f"{name} looks like {state['look']}")
    return ghost


def zombie_of(s, name="Funia", dx=5, dz=0, **config):
    zombie = s.bot(name)
    use(s.w, "Undead", **{"Time.Duration": "24h", "Zombie.Glow": "true", **config})
    s.put(zombie, dx, dz)
    s.lose(zombie, "Survival")
    s.put(zombie, dx, dz)
    s.age_revive()
    s.expect(zombie, dx, dz)
    if zombie.state()["form"] != "Zombie":
        raise Failed(f"{name} is {zombie.state()['form']}")
    return zombie


def become(s, form, preset="Ghost", **config):
    """The camera player loses the last life."""
    use(s.w, preset, **{"Time.Duration": "24h", **config})
    s.lose(s.camera, "Spectator" if form == "Ghost" else "Survival")


def room(s, who, half=7, height=5, depth=18):
    """A dark cave below someone; moves them into it."""
    who.go(0, -depth, 0)
    s.box(who, STONE, -half - 2, -2, -half - 2, half + 2, height + 2, half + 2)
    s.box(who, AIR, -half, 0, -half, half, height, half)
    who.go(0, 0, 0)


# ---------- the pilot ----------

def meadow(s):
    """Moves the camera to a flat spot among natural land."""
    found = s.arrive(s.camera, "meadow " + s.camera.name)
    if not found["found"] or found["range"] > 4:
        s.note(f"no flat spot near here: {found}")
    return found


def pilot_people(s):
    """Settings, weather and Funia, before the camera player looks around."""
    s.bot("Funia")
    use(s.w, "Ghost", **{"Time.Duration": "24h", "Ghost.Visible": "WispAndEcho", "Ritual.Where": "NearPlayer", "Ritual.ReviveAt": "InPlace"})
    s.weather("clearsky")


# The pilot's hour: late dusk with sky light. None: the first half hour from 18:00 whose daylight falls below PILOT_DAYLIGHT
# (on a real client 19:30 gave 0.79, which looked too dark at night-adapted brightness).
PILOT_TIME = "18:45"
PILOT_DAYLIGHT = 0.95


def pilot_place(s):
    """Funia where the camera player looks now, dusk, a gear in the first slot."""
    cam, funia = s.camera, s.bots["Funia"]
    s.arrive(funia, f"front Funia {cam.name} 5")
    s.age_revive()
    if PILOT_TIME:
        s.time(PILOT_TIME)
    else:
        # Sunset depends on the season and the latitude.
        for half in range(36, 46):
            s.time(f"{half // 2}:{half % 2 * 3}0")
            if s.w.trt("view " + cam.name)["daylight"] < PILOT_DAYLIGHT:
                break
    for _ in range(cam.state()["gears"], 1):
        s.wear(cam, "game:gear-temporal")
    if cam.state()["gears"] != 1:
        raise Failed("no gear in the first slot")


def pilot_action(s, at_death=lambda: None):
    cam, funia = s.camera, s.bots["Funia"]
    s.w.console("/rebirth setlives Funia 1")
    funia.until(lives=1)
    at_death()
    home = funia.state()
    funia.kill()
    funia.until(mode="Spectator", alive=True, lives=0, timeout=10)
    if funia.state()["look"] != "WispAndEcho":
        raise Failed("no echo")
    if s.check:
        time.sleep(1)
        if not cam.gear():
            raise Failed("the ritual did not work")
    funia.until(mode="Survival", alive=True, timeout=90)
    if harness.distance(funia.state(), home) > 3:
        raise Failed("Funia came back somewhere else")


@scene("PILOT", "M1 in short: last death, wisp, gear, echo, ritual, back (about 13 s); recorded with record.py")
def pilot(s):
    meadow(s)
    pilot_people(s)
    s.do("Look where Funia shall stand; nothing in hand.", "key 2")
    pilot_place(s)
    s.note("Wisp for 2 s, key 1 (gear, echo) for 2 s, then hold the right mouse button on the ground in front of her for 4 s.")
    s.cue("the pilot", 8)
    pilot_action(s)
    time.sleep(2)
    s.note("Funia is back. Stop recording.")


# ---------- to judge ----------

@scene("T1", "the wisp by day, at night, in a cave, in fog and from afar")
def t1(s):
    funia = ghost_of(s)
    s.weather("clearsky")
    s.do("Face east (+x): the wisp is 8 blocks away. No gear in hand.", "turn with the mouse")
    s.shot("wisp by day")
    s.do("Walk around it once.", "W/A/D")
    s.time("23:30")
    s.shot("wisp at night, its light on the ground")
    s.time("19:00")
    s.shot("wisp at dusk")
    s.time("8:00")
    s.weather("stronghaze")
    s.shot("wisp in fog")
    s.weather("clearsky")
    for far in (30, 60, 90):
        s.put(funia, far, 0, 2)
        s.expect(funia, far, 0)
        s.shot(f"wisp {far} blocks away, day")
        s.time("23:30")
        s.shot(f"wisp {far} blocks away, night")
        s.time("12:00")
    room(s, s.camera)
    since = s.camera.state()
    bx = funia.state()
    funia.go(round(since["x"] - bx["x"]) + 5, round(since["y"] - bx["y"]) + 1, round(since["z"] - bx["z"]))
    s.expect(funia, 5, 0, 1)
    s.shot("wisp in a cave, the only light")


@scene("T2", "the echo near water, glass and leaves, close and from behind, taking and putting away the gear")
def t2(s):
    funia = ghost_of(s, dx=6)
    s.weather("clearsky")
    s.w.trt(f"wear {s.camera.name} game:gear-temporal")
    s.note("A temporal gear is in the active hotbar slot.")
    s.do("Face east. Scroll to an empty slot, then back to the gear, three times.", "key 2, wait 1.5 s, key 1, wait 1.5 s, three times")
    s.shot("taking and putting away the gear, 6 blocks")
    s.do("Walk up to one block from the echo.", "hold W about 1 s")
    s.shot("echo close up")
    s.do("Walk around it to the other side.", "W/A/D")
    s.shot("echo from behind")
    s.put(funia, 6, 0, 1)
    s.box(s.camera, GLASS, 3, 0, -2, 3, 2, 2)
    s.shot("echo behind glass")
    s.box(s.camera, LEAVES, 3, 0, -2, 3, 2, 2)
    s.shot("echo behind leaves")
    s.box(s.camera, AIR, 3, 0, -2, 3, 2, 2)
    s.box(s.camera, WATER, 2, -1, -4, 9, -1, 4)
    s.shot("echo above water")
    s.do("Put your head under water and look at the echo.", "walk into the pool, hold Shift")
    s.shot("echo from under water")
    s.time("23:30")
    s.shot("echo at night")


@scene("T3", "the leash from afar, close, when hit, on a slope and underground")
def t3(s):
    s.weather("clearsky")
    become(s, "Ghost", **{"Place.Radius": 24})
    s.do("You are a ghost with a leash of 24 blocks. Stay in the middle and look around.", "turn 360 degrees")
    s.shot("leash from afar (24 blocks)")
    s.do("Fly east until 6 blocks from the wall, then until you touch it.", "hold W")
    s.shot("leash close up, then hit it")
    s.do("Slide along the wall.", "hold W and D")
    s.shot("sliding along the wall")
    s.do("Fly up to the top of the sphere and hit it.", "hold Space")
    s.shot("the top")
    s.time("23:30")
    s.shot("hit it at night")
    s.time("12:00")
    s.back(s.camera)
    for step in range(6):
        s.box(s.camera, GRASS, 2 + step * 2, 0, -3, 14, step, 3)
    become(s, "Player", "Exile", **{"Place.At": "Body", "Place.Radius": 8, "Return.DieAtEnd": "false"})
    s.do("You are a normal player with a leash of 8 blocks. Walk east up the slope into the wall.", "hold W, jump when needed")
    s.shot("hitting it on a slope")
    s.back(s.camera)
    room(s, s.camera, half=10)
    become(s, "Ghost", **{"Place.Radius": 8})
    s.do("You are a ghost in a cave with a leash of 8 blocks. Fly into the wall.", "hold W")
    s.shot("leash underground")


@scene("T4", "a zone of three land claims: walls, corners, trying to leave")
def t4(s):
    s.weather("clearsky")
    finn = s.bot("Finn")
    s.put(finn, 0, 0)
    use(s.w, "Exile", **{"Place.At": "Zone", "Time.Duration": "24h", "Return.DieAtEnd": "false"})
    # An L: the middle, one to the east, one to the south of it.
    for dx, dz in ((0, 0), (16, 0), (-16, 16)):
        if (dx, dz) != (0, 0):
            s.put(finn, dx, dz, of=finn)
        s.w.trt("claim Finn afterlife 8 false")
    s.put(finn, 0, -16, of=finn)
    point(s, finn, "start")
    become(s, "Player", "Exile", **{"Place.At": "Zone", "Return.DieAtEnd": "false"})
    s.do("You are held in an L-shaped zone: 8 blocks around you, more to the east (+x) and south (+z). Show the claims.", "/land claim list, or the claim overlay if there is one")
    s.shot("walk west into the wall")
    s.shot("walk into the inner corner, south-east of the middle")
    s.shot("walk east through to the next claim and into its far wall")
    s.do("Sprint and jump at the wall, then try the top of a hill or a pillar.", "hold Ctrl+W, Space")
    s.shot("trying to leave")
    s.w.console(f"/rebirth revive {s.camera.name}")
    s.camera.until(form=None, mode="Survival", alive=True, timeout=20)
    s.do("Back: walk out of the zone freely.", "hold W")


@scene("T5", "the timer with Downed texts, chat, open inventory, small and large interface", mods=("downed",))
def t5(s):
    use(s.w, "Undead", **{"Time.Duration": "24h"})
    s.lose(s.camera, "Survival")
    if s.camera.state()["seconds"] < 0:
        raise Failed("no timer")
    s.do("The timer is at the top. Open the chat and type something.", "T")
    s.shot("timer and chat")
    s.do("Close the chat, open the inventory and the handbook.", "Esc, E, H")
    s.shot("timer and dialogs")
    s.do("Look at a block so its name shows at the top centre, then at a creature.", "")
    s.shot("timer and block names")
    for scale in ("0.75", "1", "1.5", "2"):
        s.do(f"Set the interface scale to {scale} (Settings, Interface).", "Esc, Settings, Interface, GUI scale")
        s.shot(f"timer at scale {scale}")
    use(s.w, "Ghost", **{"Lives.Count": "Downs"})
    time.sleep(6)
    s.w.trt(f"hurt {s.camera.name} 500")
    s.cue("downed while out of lives: Downed's texts and the timer")
    s.w.console(f"/downed revive {s.camera.name}")
    s.back(s.camera)
    s.note("Alive with one life, no timer.")


@scene("T6", "a zombie by day and night, in armour, with a torch")
def t6(s):
    s.weather("clearsky")
    funia = zombie_of(s)
    s.do("Face east: the zombie is 5 blocks away.")
    s.time("12:00")
    s.shot("zombie by day")
    s.time("23:30")
    s.shot("zombie at night")
    s.wear(funia, "game:armor-body-plate-iron", "game:armor-head-plate-iron", "game:armor-legs-plate-iron")
    s.shot("in armour at night")
    s.time("12:00")
    s.shot("in armour by day")
    s.wear(funia, "game:torch-basic-lit-up")
    s.time("23:30")
    s.shot("with a torch at night")
    finn = s.bot("Finn")
    s.put(finn, 5, 3)
    s.time("12:00")
    s.shot("next to a normal player")
    s.back(funia)
    s.age_revive()
    s.time("12:00")
    s.shot("back to normal: no tint, no glow")


@scene("T7", "the death screen: second last life, last life, downs at no lives", mods=("downed",))
def t7(s):
    use(s.w, "Ghost")
    cam = s.camera
    s.w.console(f"/rebirth setlives {cam.name} 2")
    cam.until(lives=2)
    s.cue("dying on the second last life; the death screen opens after 2.5 s")
    cam.kill()
    cam.until(alive=False, lives=1)
    s.do("Read the death screen (1 life), then press Respawn.", "click Respawn")
    time.sleep(3 if s.check else 0)
    cam.respawn()
    cam.until(alive=True, timeout=30)
    s.cue("dying on the last life: no death screen, a ghost after 1.5 s")
    cam.kill()
    cam.until(mode="Spectator", alive=True, timeout=10)
    s.back(cam)
    use(s.w, "Ghost", **{"Lives.Count": "Downs"})
    s.w.console(f"/rebirth setlives {cam.name} 1")
    cam.until(lives=1)
    s.cue("a down that takes the last life, bleeding out after 15 s")
    s.w.trt(f"hurt {cam.name} 500")
    cam.until(downed=True, lives=0)
    cam.until(alive=False, timeout=30)
    s.do("The death screen at 0 lives must offer a respawn. Press it.", "click Respawn")
    time.sleep(3 if s.check else 0)
    cam.respawn()
    cam.until(alive=True, mode="Survival", lives=0, timeout=30)
    s.cue("a fatal hit at 0 lives: the last one")
    s.w.trt(f"hurt {cam.name} 500")
    cam.until(mode="Spectator", alive=True, timeout=15)


@scene("T8", "a zombie dying in lava and in water: no death loop")
def t8(s):
    for liquid, name in ((WATER, "water"), (LAVA, "lava")):
        # Not at the spawn point: that is where the zombie is moved.
        funia = zombie_of(s, dx=0, dz=-40)
        before = funia.state()
        s.cue(f"zombie dies in {name}, 40 blocks north (-z); watch for 40 s")
        s.box(funia, STONE, -5, -4, -5, 5, 0, 5)
        s.box(funia, liquid, -4, -2, -4, 4, 0, 4)
        for _ in range(3):
            if not funia.state()["liquid"]:
                s.note(f"moved to safe ground at {funia.state()['x']:.0f} {funia.state()['z']:.0f}")
            time.sleep(6)
            funia.kill()
            funia.until(alive=False)
            funia.until(alive=True, timeout=20)
            time.sleep(2)
        after = funia.state()
        time.sleep(15)
        deaths = funia.state()["deaths"]
        s.note(f"{name}: {after['deaths'] - before['deaths']} deaths asked for or caused, {deaths - after['deaths']} more in the next 15 s, now in liquid: {funia.state()['liquid']}")
        if deaths != after["deaths"] or funia.state()["liquid"]:
            raise Failed("keeps dying or was left in the liquid")
        s.back(funia)


@scene("T9", "relog while becoming a ghost, while returning, in exile")
def t9(s):
    cam = s.camera
    relog = "Esc, Leave world, then join again"

    def leave_and_join(text):
        s.do(text, relog)
        if s.check:
            cam.relog()
        while cam.name not in s.w.trt("players"):
            time.sleep(0.5)
        time.sleep(2)

    use(s.w, "Ghost", **{"Time.Duration": "24h"})
    s.cue("dying for good: leave the world within 1.5 s")
    s.last_death(cam)
    leave_and_join("Leave the world at once and join again.")
    cam.until(mode="Spectator", alive=True, lives=0, timeout=20)
    s.shot("a ghost with wisp, leash and timer after the relog")
    s.cue("returning: leave the world as soon as you are moved")
    s.w.console("/rebirth revive " + cam.name)
    leave_and_join("Leave the world at once and join again.")
    cam.until(mode="Survival", alive=True, timeout=20)
    s.note("Alive, no timer, no leash.")
    exile(s, radius=24)
    leave_and_join("In exile: leave the world and join again.")
    cam.until(mode="Survival", form="Player", lives=0, timeout=20)
    s.shot("still in exile: timer and leash")


@scene("T10", "nothing hurts for 5 s after rising")
def t10(s):
    cam = s.camera
    use(s.w, "Undead", **{"Time.Duration": "24h"})
    s.cue("rising as a zombie; hits of 2 every second from the start")
    s.last_death(cam)
    cam.until(alive=False)
    cam.until(alive=True, mode="Survival", timeout=20)
    for second in range(8):
        hit = s.w.trt(f"hurt {cam.name} 2")["received"]
        s.note(f"{second} s: {'hurt' if hit else 'protected'}, health {cam.state()['health']}")
        time.sleep(1)


@scene("T11", "three ghosts at once: frame rate")
def t11(s):
    use(s.w, "Ghost")
    s.weather("clearsky")
    s.do("Show the frame rate and note it.", "Ctrl+F3 or .debug fps")
    for name, dx, dz in (("Funia", 5, -3), ("Finn", 6, 0), ("Mira", 5, 3)):
        ghost_of(s, name, dx, dz)
    s.wear(s.camera, "game:gear-temporal")
    s.do("Face east. Frame rate with three wisps (other slot), then three echoes (gear in hand).", "keys 2 and 1")
    s.time("23:30")
    s.shot("three echoes at night")


# ---------- the mod page ----------

@scene("M1", "last death, wisp, a friend with a gear, the echo, ritual, back")
def m1(s):
    funia = s.bot("Funia")
    use(s.w, "Ghost", **{"Ghost.Visible": "WispAndEcho", "Time.Duration": "24h", "Ritual.Where": "NearPlayer", "Ritual.ReviveAt": "InPlace"})
    s.weather("clearsky")
    s.time("19:00")
    s.put(funia, 5, 0)
    s.wear(s.camera, "game:gear-temporal")
    s.do("Face east, Funia in the middle of the picture; empty slot 2 in hand.", "key 2")
    s.cue("recording: Funia dies, becomes a wisp")
    s.last_death(funia)
    funia.until(mode="Spectator", alive=True, timeout=10)
    s.do("Take the gear: the echo with the name Funia.", "key 1")
    s.do("The ritual, looking at the ground in front of the echo.", GEAR_USE)
    if s.check and not s.camera.gear():
        raise Failed("the ritual did not work")
    funia.until(mode="Survival", alive=True, timeout=60)
    s.note("Funia is back on the spot. Stop after 2 s.")


@scene("M2", "four shots of 2 s: Ghost, Zombie, Exile, Ban")
def m2(s):
    s.weather("clearsky")
    funia = ghost_of(s, dx=4)
    s.time("19:00")
    s.wear(s.camera, "game:gear-temporal")
    s.do("Face east, gear in hand.", "key 1")
    s.shot("Ghost, 2 s")
    s.back(funia)
    funia = zombie_of(s, dx=3)
    s.time("19:00")
    s.do("Empty hand, face east.", "key 2")
    s.shot("Zombie, 2 s")
    s.back(funia)
    exile(s, radius=24)
    s.time("12:00")
    s.do("Walk to the edge.", "hold W")
    s.shot("Exile: leash and timer, 2 s")
    s.back(s.camera)
    use(s.w, "Timeout")
    s.last_death(s.camera)
    time.sleep(3)
    s.do("You were thrown out. Join again: the join screen says until when. Record it with x11grab, not .vrec.", "Multiplayer, join")
    s.shot("Ban, 2 s")
    s.w.console("/rebirth revive " + s.camera.name)
    s.do("Join again: the ban is lifted.", "join")
    if s.check:
        s.camera.join()


@scene("M3", "a gear gives a life, lives on the death screen")
def m3(s):
    use(s.w, "Ghost")
    s.time("12:00")
    s.w.console(f"/rebirth setlives {s.camera.name} 2")
    s.wear(s.camera, "game:gear-temporal")
    s.do("Gear in hand, look at the ground.", "key 1")
    s.shot("using the gear")
    s.do("Use it.", GEAR_USE)
    if s.check:
        s.camera.gear()
    s.camera.until(lives=3, timeout=30)
    s.cue("dying")
    s.camera.kill()
    s.camera.until(alive=False, lives=2)
    s.do("The death screen says 2 lives. Press Respawn after 2 s.", "click Respawn")
    time.sleep(3 if s.check else 0)
    s.camera.respawn()
    s.camera.until(alive=True, timeout=60)


@scene("M4", "exile: walking to the edge, the leash flares, timer on top")
def m4(s):
    s.weather("clearsky")
    exile(s, radius=24)
    s.time("17:30")
    s.do("Face east, about 10 blocks from the edge.", "hold W about 3 s")
    s.shot("walk into the edge, 5 s")


def point(s, who, name):
    s.w.console(f"/player {who.name} privilege grant temporalrebirth.points")
    time.sleep(0.5)
    who.ask(f"/rebirth point set {name}", f"Place '{name}' marked")


def exile(s, radius):
    """The camera player in a marked place 300 blocks away."""
    finn = s.bot("Finn")
    finn.go(300, 0, 0)
    time.sleep(2)
    s.put(finn, 0, 0, of=finn)
    point(s, finn, "start")
    become(s, "Player", "Exile", **{"Place.Radius": radius, "Return.DieAtEnd": "false"})
    state = s.camera.state()
    if state["form"] != "Player" or state["leash"] is None or state["seconds"] < 0:
        raise Failed(f"not in exile: {state['form']} {state['leash']} {state['seconds']}")


# W1-W18, for the wiki, go here as further @scene functions.

def others_running():
    """Servers and test runs that are not ours: the laptop has no memory for both."""
    own = {process.proc.pid for process in harness.RUNNING}
    for line in subprocess.run(["ps", "-eo", "pid,args"], capture_output=True, text=True).stdout.splitlines()[1:]:
        pid, program, *rest = line.split()
        server = program.endswith("dotnet") and any(a.endswith("VintagestoryServer.dll") for a in rest)
        tests = program.endswith(("python3", "python")) and any(a.endswith("test/run.py") for a in rest)
        if (server or tests) and int(pid) not in own:
            return True
    return False


def main():
    options = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    options.add_argument("names", nargs="*")
    options.add_argument("--check", action="store_true")
    options.add_argument("--list", action="store_true")
    options.add_argument("--no-build", action="store_true")
    args = options.parse_args()
    if args.list or not args.names:
        for name, (_, about, mods) in SCENES.items():
            print(f"{name:4} {about}" + (f"  (server with {', '.join(mods)})" if mods else ""))
        return 0
    unknown = [n for n in args.names if n not in SCENES]
    if unknown:
        sys.exit("no such scene: " + ", ".join(unknown))
    if others_running():
        sys.exit("a test server or test/run.py is already running; not starting another one")
    if not args.no_build:
        harness.build()
    stage, failed = None, []
    try:
        for name in args.names:
            if args.check and others_running():
                print("stopping: other test servers were started")
                break
            play, about, mods = SCENES[name]
            if stage is None or stage.mods != mods:
                if stage:
                    stage.stop()
                stage = Stage(args.check, mods)
                stage.wait_for_camera()
            print(f"\n=== {name}: {about} ===", flush=True)
            stage.number = 0
            began = time.monotonic()
            try:
                stage.reset()
                stage.put(stage.camera, 0, 0)
                play(stage)
                print(f"=== {name} done in {time.monotonic() - began:.0f} s ===", flush=True)
            except Failed as e:
                failed.append(name)
                print(f"=== {name} FAILED: {e} ===", flush=True)
    finally:
        if stage:
            stage.stop()
        harness.kill_everything()
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
