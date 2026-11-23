#!/usr/bin/env python3
"""M and W scenes on a GPU display, one client for many scenes (the pattern of auto.pilot_realtime).

    test/scenes/gpu-display.sh start 7
    TRT_GPU=1 test/scenes/shoot.py M1 W5 W6 --display 7            (scenes that need no extra mod)
    TRT_GPU=1 test/scenes/shoot.py W2 W3A --display 7 --downed     (server and client with the Downed mod)

Each scene stages itself from data (a preset, options, who stands where) and a few steps, records with x11grab and
leaves NAME.gif, NAME.mkv and NAME.png (contact sheet) in the folder of its group. Never touches another display.
"""
import argparse, base64, contextlib, json, math, signal, sys, time
from pathlib import Path

import auto, clip
from auto import GROUND, LEVEL, Client, Display, aim, clean_screen, close_dialogs, dismiss_intro, glide, grab, join, log, restore_screen
from play import meadow, point
from stage import AIR, GRASS, STONE, Camera, Failed, Stage, harness, use

VIDEOS = Path.home() / "Videos/temporal-rebirth"
RAW = Path.home() / ".cache/trt-gifs/raw"
SCENES = {}
RESULTS = []
# Where the Respawn button of the death screen is (checked on a screenshot of the death screen).
RESPAWN = (640, 400)

DUSK = "18:45"
# The settings of the pilot: a friend near the dead one is enough, the dead one comes back where she lies.
CLASSIC = {"Ghost.Visible": "WispAndEcho", "Ritual.Where": "NearPlayer", "Ritual.ReviveAt": "InPlace"}


# The saved setting 'block name overlay' was left off by earlier runs; this run keeps it off while recording and switches it
# on again at the end (the player's original state).
BLOCKINFO_NOW_OFF = True


def tidy(d):
    """Like auto.clean_screen, but the block overlay is already off (see above)."""
    changed = {"minimap": d.minimap_visible(), "blockinfo": True}
    if changed["minimap"]:
        d.key("F6")
        time.sleep(1)
    d.key("ctrl+b")
    time.sleep(1)
    return changed


def tip_off(d):
    """Holding K opens the intro dialog and removes the start tip for this session; Close (not 'never show again')."""
    d.x("keydown", "k")
    time.sleep(1.6)
    d.x("keyup", "k")
    dismiss_intro(d, 3)


def scene(name, folder, mods=(), trim=(0.4, 0.5)):
    def add(f):
        SCENES[name] = (f, folder, mods, trim)
        return f
    return add


class Ctx:
    def __init__(self, s, d):
        self.s, self.d, self.cam = s, d, s.camera
        self.cleaned, self.relaunch = {}, False

    # ---------- staging ----------

    def base(self, preset="Classic", clock=DUSK, people=("Funia",), dist=6, mow=12, **config):
        """Fresh world state, a mown meadow, the settings, the people and the time. The first of `people` stands `dist`
        blocks in front of the camera; the camera looks level with an empty hand."""
        s, d = self.s, self.d
        s.reset()
        meadow(s)
        use(s.w, preset, **{"Time.Duration": "24h", **(CLASSIC if preset == "Classic" else {}), **config})
        for name in people:
            s.bot(name)
        for name, bot in s.bots.items():
            if name not in people:
                s.beside(bot, self.cam, 80, 0, 80)
        s.box(self.cam, GRASS, -mow, -3, -mow, mow, -1, mow)
        s.box(self.cam, AIR, -mow, 0, -mow, mow, 6, mow)
        d.key("2")
        time.sleep(1.5)
        close_dialogs(d)
        s.weather("clearsky")
        tip_off(d)
        self.type(".clearchat", 0.8)
        first = None
        if people:
            first = s.bots[people[0]]
            s.arrive(first, f"front {first.name} {self.cam.name} {dist}")
            a, c = first.state(), self.cam.state()
            if abs(a["y"] - c["y"]) > 0.5:
                s.beside(first, self.cam, round(a["x"] - c["x"]), 0, round(a["z"] - c["z"]))
        s.time(clock)
        aim(s, d, LEVEL)
        return first

    def vectors(self):
        """Unit vector from the camera to where it looks (found with a bot put in front of it) and the one to its right."""
        s = self.s
        probe = s.bot("Finn")
        s.arrive(probe, f"front Finn {self.cam.name} 6")
        a, c = probe.state(), self.cam.state()
        vx, vz = a["x"] - c["x"], a["z"] - c["z"]
        n = math.hypot(vx, vz) or 1
        return (vx / n, vz / n), (-vz / n, vx / n)

    def place(self, bot, ahead, side=0.0, up=0):
        """A bot `ahead` blocks in front of the camera and `side` to the right of the view."""
        (fx, fz), (sx, sz) = self.vectors()
        self.s.beside(bot, self.cam, round(fx * ahead + sx * side), up, round(fz * ahead + sz * side))

    def gears(self, n):
        if self.cam.state()["gears"] < n:
            self.s.wear(self.cam, "game:gear-temporal")

    def lose(self, bot, lives=1):
        self.s.w.console(f"/rebirth setlives {bot.name} {lives}")
        bot.until(lives=lives)
        bot.kill()

    def make_ghost(self, bot):
        self.lose(bot)
        bot.until(mode="Spectator", alive=True, lives=0, timeout=20)

    # ---------- the screen ----------

    def type(self, text, pause=1.2):
        d = self.d
        d.key("t")
        time.sleep(0.6)
        d.x("type", "--delay", "35", text)
        time.sleep(0.5)
        d.key("Return")
        time.sleep(pause)

    def hold_gear(self, until, seconds=8):
        """Gear in hand, looking at the ground: hold the right button until `until()` or time is out."""
        s, d = self.s, self.d
        aim(s, d, GROUND, 0.04)
        d.x("mousedown", "3")
        end = time.monotonic() + seconds
        try:
            while time.monotonic() < end and not until():
                time.sleep(0.25)
        finally:
            d.x("mouseup", "3")
        return until()

    def respawn(self):
        """Clicks Respawn on the death screen until the player is alive."""
        d = self.d
        for _ in range(8):
            if self.cam.state()["alive"]:
                break
            d.x("mousemove", *RESPAWN, "click", "1")
            time.sleep(1.5)
        d.x("mousemove", auto.WIDTH // 2, auto.HEIGHT // 2)
        self.cam.until(alive=True, timeout=20)

    @contextlib.contextmanager
    def recording(self, name, folder, trim=(0.4, 0.5)):
        RAW.mkdir(parents=True, exist_ok=True)
        file = RAW / f"{name}-{time.strftime('%H%M%S')}.mkv"
        recorder = grab(self.d, file)
        time.sleep(0.8)
        try:
            yield
        finally:
            recorder.communicate(b"q", timeout=30)
            self.finish(name, folder, file, trim)

    def finish(self, name, folder, file, trim):
        out = VIDEOS / folder
        out.mkdir(parents=True, exist_ok=True)
        clip.OUT = out
        before = set(out.iterdir())
        ok = clip.report(file, name, trim=trim)
        for f in [f for f in out.iterdir() if f not in before and f.name.startswith(name + "-")]:
            f.replace(out / (name + f.suffix))
        gif = out / f"{name}.gif"
        info = clip.measure(gif) if gif.exists() else None
        RESULTS.append((name, str(gif), round(gif.stat().st_size / 1e6, 1) if gif.exists() else 0, round(info["seconds"], 1) if info else 0, ok))
        clip.OUT = VIDEOS / "test"


# ---------- shared flows ----------

def revive_by_gear(c, name, folder, *, people=("Funia",), config=None, clock=DUSK, dist=6, before=None, after=2.5, trim=(0.8, 1.0), hold=8, fly=True):
    """The pilot: the last death, a ghost, the gear, the ritual, the way back. Used by M1, W15, W16."""
    s, d = c.s, c.d
    funia = c.base(people=people, dist=dist, clock=clock, **(config or {}))
    c.gears(1)
    if before:
        before(c, funia)
    (fx, fz), (sx, sz) = c.vectors()
    # The probe bot of vectors() stands in the picture; Finn leaves it.
    s.beside(s.bots["Finn"], c.cam, 60, 0, 60)
    d.key("2")
    time.sleep(1)
    aim(s, d, LEVEL)
    with c.recording(name, folder, trim):
        time.sleep(0.6)
        c.make_ghost(funia)
        time.sleep(0.8)
        if fly:
            glide(s, funia, sx * 2.5, 2.5, sz * 2.5, 1.8)
        d.key("1")
        time.sleep(0.8)
        if fly:
            glide(s, funia, -sx * 2.5, -2.5, -sz * 2.5, 1.6)
            time.sleep(0.3)
        done = lambda: funia.state()["mode"] == "Survival" and funia.state()["alive"]
        if not c.hold_gear(done, hold):
            log(f"{name}: the held gear did nothing; the helper does the ritual")
            s.w.trt("gear " + c.cam.name)
        funia.until(mode="Survival", alive=True, timeout=30)
        aim(s, d, LEVEL, 0.04)
        time.sleep(after)
    d.key("2")
    return funia


def away(c, name):
    """Finn (the probe of vectors) is moved out of the picture."""
    c.s.beside(c.s.bots[name], c.cam, 60, 0, 60)


# ---------- M: the mod page ----------

@scene("M1", "modpage")
def m1(c):
    revive_by_gear(c, "M1", "modpage")


@scene("M2", "modpage", trim=(0.0, 0.0))
def m2(c):
    s, d = c.s, c.d
    # Ghost: the echo, close, gear in hand.
    funia = c.base(dist=4)
    c.gears(1)
    c.make_ghost(funia)
    s.arrive(funia, f"front Funia {c.cam.name} 4")
    d.key("1")
    time.sleep(1.5)
    aim(s, d, LEVEL)
    with c.recording("M2-ghost", "modpage", (0, 0)):
        time.sleep(2.2)
    d.key("2")
    # Zombie.
    funia = c.base("Undead", dist=3)
    s.lose(funia, "Survival")
    s.age_revive()
    s.time(DUSK)
    s.arrive(funia, f"front Funia {c.cam.name} 3")
    aim(s, d, LEVEL)
    with c.recording("M2-zombie", "modpage", (0, 0)):
        time.sleep(2.2)
    s.back(funia)
    # Exile: the camera at the edge of its place.
    c.base("Exile", clock="17:30", mow=28, **{"Place.At": "Body", "Place.Radius": 24, "Return.DieAtEnd": "false"})
    s.lose(c.cam, "Survival")
    time.sleep(2)
    d.x("keydown", "w")
    d.x("keydown", "ctrl")
    try:
        time.sleep(3.2)
        with c.recording("M2-exile", "modpage", (0, 0)):
            time.sleep(2.2)
    finally:
        d.x("keyup", "ctrl")
        d.x("keyup", "w")
    s.back(c.cam)


@scene("M2B", "modpage")
def m2_ban(c):
    """Ban: the camera is thrown out and the screen says until when; a new client is started afterwards."""
    s, d = c.s, c.d
    c.base("Timeout", people=())
    s.w.console(f"/rebirth setlives {c.cam.name} 1")
    c.cam.until(lives=1)
    RAW.mkdir(parents=True, exist_ok=True)
    file = RAW / f"M2-ban-{time.strftime('%H%M%S')}.mkv"
    recorder = grab(d, file)
    try:
        time.sleep(1)
        c.cam.kill()
        time.sleep(5)
        d.shot("ban-screen")
    finally:
        recorder.communicate(b"q", timeout=30)
    c.finish("M2-ban", "modpage", file, (0.0, 0.0))
    s.w.console(f"/rebirth revive {c.cam.name}")
    c.relaunch = True


@scene("M3", "modpage")
def m3(c):
    s, d = c.s, c.d
    c.base(people=(), clock="12:00", **{"Lives.PerGear": 1, "Lives.Max": -1})
    s.w.console(f"/rebirth setlives {c.cam.name} 2")
    c.cam.until(lives=2)
    c.gears(1)
    d.key("1")
    time.sleep(1)
    with c.recording("M3", "modpage"):
        time.sleep(1)
        c.hold_gear(lambda: c.cam.state()["lives"] == 3, 8)
        time.sleep(2)
        c.cam.kill()
        c.cam.until(alive=False, lives=2)
        time.sleep(4.5)
        d.shot("death-screen")
    c.respawn()
    d.key("2")


@scene("M4", "modpage")
def m4(c):
    s, d = c.s, c.d
    c.base("Exile", clock="17:30", mow=28, **{"Place.At": "Body", "Place.Radius": 24, "Return.DieAtEnd": "false"})
    s.lose(c.cam, "Survival")
    time.sleep(2)
    with c.recording("M4", "modpage"):
        time.sleep(1)
        d.x("keydown", "w")
        d.x("keydown", "ctrl")
        try:
            time.sleep(8)
        finally:
            d.x("keyup", "ctrl")
            d.x("keyup", "w")
        time.sleep(1.5)
    s.back(c.cam)


# ---------- W: the wiki ----------

@scene("W1", "wiki")
def w1(c):
    s, d = c.s, c.d
    c.base(people=(), clock="12:00", **{"Lives.Max": 3, "Lives.PerGear": 1})
    s.w.console(f"/rebirth setlives {c.cam.name} 2")
    c.cam.until(lives=2)
    c.gears(1)
    d.key("1")
    time.sleep(1)
    with c.recording("W1", "wiki"):
        time.sleep(1)
        c.hold_gear(lambda: c.cam.state()["lives"] == 3, 8)
        time.sleep(1.5)
        c.gears(1)
        d.key("1")
        time.sleep(0.5)
        c.hold_gear(lambda: False, 4.5)
        time.sleep(1.5)
        c.type("/rebirth lives", 2.5)
    log(f"W1: lives {c.cam.state()['lives']} gears {c.cam.state()['gears']}")
    d.key("2")


@scene("W2", "wiki", mods=("downed",))
def w2(c):
    s, d = c.s, c.d
    c.base(clock="12:00", dist=3, **{"Lives.Count": "Downs"})
    time.sleep(1)
    with c.recording("W2", "wiki"):
        time.sleep(1)
        s.w.trt(f"hurt {c.cam.name} 500")
        c.cam.until(downed=True, lives=2, timeout=10)
        time.sleep(4)
        s.w.console(f"/downed revive {c.cam.name}")
        c.cam.until(downed=False, timeout=10)
        time.sleep(3)
    log(f"W2: lives {c.cam.state()['lives']}")


def bleed(c, name, mode, ghost):
    s, d = c.s, c.d
    c.base(clock="12:00", dist=3, **{"Lives.Count": mode})
    time.sleep(1)
    with c.recording(name, "wiki"):
        time.sleep(1)
        s.w.trt(f"hurt {c.cam.name} 500")
        c.cam.until(downed=True, timeout=10)
        c.cam.until(alive=False, timeout=40) if not ghost else c.cam.until(mode="Spectator", alive=True, lives=0, timeout=40)
        time.sleep(4.5)
        d.shot(name + "-end")
    log(f"{name}: lives {c.cam.state()['lives']}")
    if ghost:
        s.back(c.cam)
    else:
        c.respawn()


@scene("W3A", "wiki", mods=("downed",))
def w3a(c):
    bleed(c, "W3-downs", "Downs", False)


@scene("W3B", "wiki", mods=("downed",))
def w3b(c):
    bleed(c, "W3-final", "DownsFinalDeath", True)


@scene("W4", "wiki")
def w4(c):
    # Another player sees only a wisp: nothing in hand. It drifts up and aside.
    s, d = c.s, c.d
    funia = c.base(dist=7)
    (fx, fz), (sx, sz) = c.vectors()
    away(c, "Finn")
    with c.recording("W4", "wiki"):
        time.sleep(1.2)
        c.make_ghost(funia)
        time.sleep(1.5)
        glide(s, funia, sx * 3, 1.5, sz * 3, 2.5)
        glide(s, funia, -sx * 6, 0.5, -sz * 6, 3.5)
        time.sleep(1)


@scene("W5", "wiki")
def w5(c):
    s, d = c.s, c.d
    funia = c.base(dist=5)
    c.gears(1)
    c.make_ghost(funia)
    s.arrive(funia, f"front Funia {c.cam.name} 5")
    d.key("2")
    aim(s, d, LEVEL)
    with c.recording("W5", "wiki"):
        time.sleep(2)
        d.key("1")
        time.sleep(3)
        d.key("2")
        time.sleep(2)
        d.key("1")
        time.sleep(2.5)
    d.key("2")


def ghost_view(c, name, form, radius, keys, seconds):
    s, d = c.s, c.d
    c.base(clock=DUSK, dist=6, **{"OutOfLives": form, "Place.At": "Body", "Place.Radius": radius})
    (fx, fz), _ = c.vectors()
    away(c, "Finn")
    away(c, "Funia")
    cx, cz = round(fx * 7), round(fz * 7)
    s.box(c.cam, STONE, cx - 3, 0, cz - 3, cx + 3, 6, cz + 3)
    s.w.console(f"/rebirth setlives {c.cam.name} 1")
    c.cam.until(lives=1)
    c.cam.kill()
    c.cam.until(mode="Spectator", alive=True, lives=0, timeout=20)
    time.sleep(2.5)
    aim(s, d, LEVEL)
    with c.recording(name, "wiki"):
        time.sleep(1)
        for key in keys:
            d.x("keydown", key)
        try:
            time.sleep(seconds)
        finally:
            for key in keys:
                d.x("keyup", key)
        time.sleep(1)
    s.back(c.cam)


@scene("W6", "wiki")
def w6(c):
    ghost_view(c, "W6", "Ghost", 14, ["w", "space"], 7.5)


@scene("W7", "wiki")
def w7(c):
    ghost_view(c, "W7", "Spectator", 0, ["w"], 6)


@scene("W8", "wiki")
def w8(c):
    s, d = c.s, c.d
    funia = c.base("Undead", clock="23:30", dist=7)
    s.lose(funia, "Survival")
    s.age_revive()
    s.time("23:30")
    (fx, fz), (sx, sz) = c.vectors()
    c.place(funia, 6, 2.5)
    away(c, "Finn")
    cx, cz = round(fx * 2), round(fz * 2)
    s.box(c.cam, "game:chest-east", cx, 0, cz, cx, 0, cz)
    s.lose(c.cam, "Survival")
    time.sleep(2)
    aim(s, d, GROUND, 0.04)
    with c.recording("W8", "wiki"):
        time.sleep(2.5)
        d.x("click", "3")
        time.sleep(2.5)
        d.x("click", "3")
        time.sleep(2.5)
    s.back(c.cam, funia)


EXILE24 = {"Place.At": "Body", "Place.Radius": 24, "Return.DieAtEnd": "false"}


@scene("W9", "wiki")
def w9(c):
    s, d = c.s, c.d
    c.base("Exile", clock="17:30", mow=28, **EXILE24)
    s.lose(c.cam, "Survival")
    time.sleep(2)
    with c.recording("W9", "wiki"):
        time.sleep(1.5)
        s.w.trt(f"hurt {c.cam.name} 500")
        c.cam.until(alive=False, timeout=10)
        time.sleep(4)
        d.shot("exile-dead")
        c.respawn()
        time.sleep(4)
    log(f"W9: {c.cam.state()['form']} {c.cam.state()['seconds']}")
    s.back(c.cam)


def timer_scene(c, name, config, wait_until):
    """Exile with a timer of 36 s (0.01 h), recorded from `wait_until` seconds before the end."""
    s, d = c.s, c.d
    c.base("Exile", clock="17:30", mow=28, **{**EXILE24, "Time.Duration": "0.01h", **config})
    s.lose(c.cam, "Survival")
    while 0 <= c.cam.state()["seconds"] > wait_until:
        time.sleep(0.5)
    with c.recording(name, "wiki"):
        end = time.monotonic() + 45
        while time.monotonic() < end and c.cam.state()["form"] is not None:
            time.sleep(0.5)
        time.sleep(1)
        if not c.cam.state()["alive"]:
            time.sleep(3)
            d.shot(name + "-dead")
            c.respawn()
        time.sleep(4)
    st = c.cam.state()
    log(f"{name}: form {st['form']} lives {st['lives']} alive {st['alive']}")


@scene("W10", "wiki")
def w10(c):
    timer_scene(c, "W10", {"Return.DieAtEnd": "true"}, 8)


@scene("W14", "wiki")
def w14(c):
    timer_scene(c, "W14", {"Return.DieAtEnd": "false"}, 12)


@scene("W11", "wiki")
def w11(c):
    s, d = c.s, c.d
    funia = c.base("Timeout", dist=6, **{"Ritual.Where": "Anywhere", "Ritual.CanLiftBan": "true", "Ritual.ReviveAt": "Caster"})
    c.gears(1)
    s.w.console("/rebirth setlives Funia 1")
    funia.until(lives=1)
    funia.kill()
    time.sleep(4)
    d.key("1")
    time.sleep(0.5)
    with c.recording("W11", "wiki"):
        time.sleep(1)
        c.type("/rebirth ghosts", 2)
        c.type("/rebirth target Funia", 2)
        c.hold_gear(lambda: "Funia" not in s.w.trt("players") and False, 5)
        time.sleep(0.5)
        funia.join()
        time.sleep(4)
    d.key("2")


@scene("W12", "wiki")
def w12(c):
    s, d = c.s, c.d
    c.base("Exile", people=(), clock="12:00", **{"Place.Radius": 24})
    time.sleep(1)
    with c.recording("W12", "wiki"):
        time.sleep(1)
        c.type("/rebirth point set start", 2.5)
        c.type("/rebirth point show", 3)


@scene("W13", "wiki")
def w13(c):
    s, d = c.s, c.d
    finn = c.base("Exile", people=("Finn",), clock="12:00", dist=2, **{"Place.At": "Zone", "Return.DieAtEnd": "false"})
    for dx, dz in ((0, 0), (16, 0), (-16, 16)):
        if (dx, dz) != (0, 0):
            s.put(finn, dx, dz, of=finn)
        s.w.trt("claim Finn afterlife 8 false")
    s.put(finn, 0, -16, of=finn)
    point(s, finn, "start")
    s.lose(c.cam, "Survival")
    time.sleep(2.5)
    aim(s, d, LEVEL)
    with c.recording("W13", "wiki"):
        time.sleep(1)
        d.x("keydown", "w")
        try:
            time.sleep(5)
        finally:
            d.x("keyup", "w")
        time.sleep(1.5)
    s.back(c.cam)


@scene("W15", "wiki")
def w15(c):
    def rift(c, funia):
        (fx, fz), (sx, sz) = c.vectors()
        c.s.w.trt(f"riftnear {c.cam.name} {round(sx * 4 + fx * 2)} 1 {round(sz * 4 + fz * 2)} 3")
    revive_by_gear(c, "W15", "wiki", config={"Ritual.Where": "Rift", "Ritual.ReviveAt": "Caster"}, before=rift, fly=False)
    c.s.w.trt("clearrifts")


@scene("W16", "wiki")
def w16(c):
    def second(c, funia):
        s = c.s
        mira = s.bot("Mira")
        s.w.console("/rebirth setlives Mira 1")
        mira.until(lives=1)
        mira.kill()
        mira.until(mode="Spectator", alive=True, timeout=20)
        c.place(mira, 14, -6)
    # Two ghosts: the ritual returns the nearest one, where she is.
    revive_by_gear(c, "W16", "wiki", before=second, fly=False)
    c.s.back(c.s.bots["Mira"])


@scene("W17", "wiki")
def w17(c):
    s, d = c.s, c.d
    funia = c.base(dist=6, **{"Ritual.Where": "Anywhere", "Ritual.ReviveAt": "Caster"})
    c.gears(1)
    c.make_ghost(funia)
    s.beside(funia, c.cam, 90, 3, 0)
    away(c, "Finn") if "Finn" in s.bots else None
    d.key("1")
    time.sleep(1)
    aim(s, d, LEVEL)
    with c.recording("W17", "wiki"):
        time.sleep(1.5)
        c.type("/rebirth target Funia", 2.5)
        c.hold_gear(lambda: funia.state()["mode"] == "Survival" and funia.state()["alive"], 8)
        funia.until(mode="Survival", alive=True, timeout=30)
        aim(s, d, LEVEL, 0.04)
        time.sleep(3)
    d.key("2")


def import_code(preset, **values):
    data = {"v": 1, "p": preset, "s": values}
    return base64.urlsafe_b64encode(json.dumps(data, separators=(",", ":")).encode()).decode().rstrip("=")


@scene("W18", "wiki")
def w18(c):
    s, d = c.s, c.d
    c.base("Classic", people=(), clock="12:00")
    time.sleep(1)
    code = import_code("Undead", **{"Time.Duration": "12h", "Lives.Max": 5})
    with c.recording("W18", "wiki"):
        time.sleep(1)
        c.type("/rebirth import " + code, 5)
        time.sleep(3)
    s.w.console("/rebirth preset Classic")


# ---------- the run ----------

def main():
    options = argparse.ArgumentParser()
    options.add_argument("names", nargs="*")
    options.add_argument("--display", type=int, default=7)
    options.add_argument("--downed", action="store_true")
    args = options.parse_args()
    if not args.names:
        print(" ".join(f"{n}{'*' if m else ''}" for n, (_, _, m, _) in SCENES.items()))
        return 0
    names = [n.upper() for n in args.names]
    unknown = [n for n in names if n not in SCENES]
    if unknown:
        sys.exit("no such scene: " + ", ".join(unknown))
    mods = ("downed",) if args.downed else ()
    if any(bool(SCENES[n][2]) != args.downed for n in names):
        sys.exit("scenes marked * need --downed, the others must not have it")
    if auto.others_running():
        sys.exit("another test server or test/run.py is running")
    signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
    stage = display = client = c = None
    code = 0
    try:
        display = Display(args.display, True)
        stage = Stage(False, mods)
        stage.do = lambda *a, **k: None
        client = Client(display)
        join(stage, display)
        time.sleep(3)
        stage.w.console(f"/op {stage.camera.name}")
        c = Ctx(stage, display)
        c.cleaned = tidy(display)
        display.chat(".clearchat")
        time.sleep(15)
        dismiss_intro(display)
        for name in names:
            play = SCENES[name][0]
            log(f"=== {name}")
            c.relaunch = False
            try:
                play(c)
            except Failed as e:
                log(f"{name} FAILED: {e}")
                RESULTS.append((name, "", 0, 0, False))
                display.shot("failed-" + name)
                code = 1
                with contextlib.suppress(Exception):
                    if not stage.camera.state()["alive"]:
                        c.respawn()
            if c.relaunch:
                restore_screen(display, c.cleaned)
                client.stop()
                time.sleep(3)
                client = Client(display)
                join(stage, display)
                time.sleep(3)
                stage.camera = Camera(stage.w, stage.camera.name, True)
                c.cam = stage.camera
                c.cleaned = tidy(display)
    except Failed as e:
        log("failed: " + str(e))
        code = 1
    except KeyboardInterrupt:
        code = 130
    finally:
        signal.signal(signal.SIGINT, signal.SIG_IGN)
        if client and display and c:
            with contextlib.suppress(Exception):
                restore_screen(display, c.cleaned)
        if client:
            client.stop()
        if stage:
            stage.stop()
        for process in harness.RUNNING:
            process.proc.kill()
        for r in RESULTS:
            print("RESULT", r)
        log("stopped")
    return code


if __name__ == "__main__":
    sys.exit(main())
