"""What all scenes share: one server on a fixed port, the player in front of the camera, steps printed one by one."""
import functools, os, shutil, subprocess, sys, time, types
from pathlib import Path

os.environ.setdefault("TRT_WORK", str(Path.home() / ".cache/temporalrebirth-gifs"))
os.environ.setdefault("VINTAGE_STORY", "/var/lib/flatpak/app/at.vintagestory.VintageStory/x86_64/stable/active/files/extra/vintagestory")
HERE = Path(__file__).resolve().parent
sys.path[:0] = [str(HERE.parent)]

import harness  # noqa: E402
from harness import Bot, Failed, World  # noqa: E402
from steps import settings as use  # noqa: E402

PORT = int(os.environ.get("TRT_PORT", "42430"))
harness.free_port = lambda: PORT
# Ctrl+C in the terminal goes to this script only; it stops server and bots in order.
harness.subprocess = types.SimpleNamespace(**vars(subprocess))
harness.subprocess.Popen = functools.partial(subprocess.Popen, start_new_session=True)

AIR, STONE, WATER, LAVA = "game:air", "game:rock-granite", "game:water-still-7", "game:lava-still-7"
GLASS, LEAVES, GRASS = "game:glass-plain", "game:leaves-placed-oak", "game:soil-medium-normal"


class Camera(Bot):
    """The player whose screen is recorded: a real client, or in a check without one a bot named Viewer."""

    def __init__(self, world, name, real):
        super().__init__(world, name)
        self.real = real

    def respawn(self):
        if not self.real:
            super().respawn()


class Stage:
    def __init__(self, check, mods=()):
        self.check, self.mods, self.number = check, mods, 0
        self.w = World("scene", lives=3, mods=mods, flat=False, files={"ModConfig/downed.json": {"DurationSeconds": int(os.environ.get("TRT_DOWNED_SECONDS", "15"))}} if "downed" in mods else None)
        self.bots, self.camera = {}, None
        # The server only listens on this computer.
        self.w.console("/serverconfig whitelistmode off")

    # ---------- people ----------

    def wait_for_camera(self):
        if self.check:
            self.w.join("Viewer")
            self.camera = Camera(self.w, "Viewer", False)
        else:
            print(f"\nServer ready. Connect the test client:  --connect 127.0.0.1:{PORT}", flush=True)
            while not (names := [n for n in self.w.trt("players") if n not in self.bots]):
                time.sleep(1)
            self.camera = Camera(self.w, names[0], True)
            time.sleep(2)
        return self.camera

    def bot(self, name):
        """Funia, Finn, Mira: joins once and stays for all scenes."""
        if name not in self.bots:
            self.bots[name] = self.w.join(name)[0]
            # Without a character a bot has no body on a real client.
            self.w.trt(f"dress {name} {len(self.bots)}")
            self.w.console(f"/op {name}")
        return self.bots[name]

    def put(self, who, dx, dz, above=0, of=None):
        """Puts someone on the ground near the camera (or another player); +x is east, +z is south."""
        name = getattr(who, "name", who)
        since = self.w.server.mark()
        self.w.trt(f"put {name} {getattr(of, 'name', of) or self.camera.name} {dx} {above} {dz}")
        if not self.w.server.wait("ARRIVED " + name + "$", since, 40):
            raise Failed(name + ": the land there did not load")
        time.sleep(0.5)

    def beside(self, who, other, dx, dy, dz):
        """Like put, but at the height of the other player."""
        a, b = who.state(), other.state()
        who.go(*(round(b[k] + d - a[k]) for k, d in zip("xyz", (dx, dy, dz))))

    def box(self, who, block, x1, y1, z1, x2, y2, z2):
        self.w.trt(f"box {who.name} {block} {x1} {y1} {z1} {x2} {y2} {z2}")

    def arrive(self, who, command):
        since = self.w.server.mark()
        answer = self.w.trt(command)
        if not self.w.server.wait("ARRIVED " + who.name + "$", since, 40):
            raise Failed(who.name + ": the land there did not load")
        return answer

    def wear(self, who, *items):
        for item in items:
            self.w.trt(f"wear {who.name} {item}")

    def lose(self, who, form="Spectator"):
        """The last death of someone, until they are what the settings say."""
        self.last_death(who)
        who.until(mode=form, alive=True, lives=0, timeout=20)
        time.sleep(2)

    def last_death(self, who, lives=1):
        self.w.console(f"/rebirth setlives {who.name} {lives}")
        who.until(lives=lives)
        who.kill()

    def back(self, *people):
        for who in people:
            self.w.console(f"/rebirth revive {who.name}")
            who.until(mode="Survival", alive=True, timeout=20)
        time.sleep(1)

    def reset(self):
        """Between scenes: everybody alive with three lives on the ground at the spawn point, noon, clear sky."""
        self.w.console("/rebirth preset Ghost")
        # Presets keep the lives section: a scene that counted downs must not leave it for the next one.
        self.w.console("/rebirth config Lives.Count Deaths")
        for who in [self.camera, *self.bots.values()]:
            lost = self.w.trt("registry").values()
            if any(entry["name"] == who.name for entry in lost):
                self.back(who)
            if not who.state()["alive"]:
                self.do("Press Respawn.", "click the Respawn button")
                who.respawn()
                who.until(alive=True, timeout=60 if who is self.camera else 15)
            self.w.console(f"/rebirth setlives {who.name} 3")
        self.w.trt("unclaim")
        self.w.trt("clearrifts")
        self.time("12:00")

    # ---------- world ----------

    def time(self, clock):
        # Not stopped: the game spawns and moves particles at the speed of time (Calendar.SpeedOfTime / 60), so a stopped
        # clock hides every particle (ritual motes, the flash, a wisp's sparks). A scene lasts a few game minutes.
        self.w.console("/time set " + clock)

    def age_revive(self):
        """The game makes players glow for 2 hours of game time after a revive; a stopped clock would keep that forever."""
        self.w.console("/time add 3 hours")

    def weather(self, pattern):
        """clearsky, stronghaze, overcast, cumulonimbusr ..."""
        # Weather commands need a player; nobody joins just for that and then stands in the picture.
        speaker = next(iter(self.bots.values()), None) or self.bot("Finn")
        speaker.say("/weather acp off")
        time.sleep(0.3)
        speaker.ask("/weather set " + pattern, "Ok weather pattern set")

    # ---------- steps ----------

    def note(self, text):
        print("       " + text, flush=True)

    def do(self, text, keys=""):
        """What the human or an input script does now. Waits for Enter, so the server side never runs ahead."""
        self.number += 1
        print(f"  {self.number:2}.  YOU: {text}" + (f"\n       keys: {keys}" if keys else ""), flush=True)
        if not self.check:
            input("       [Enter when done] ")

    def expect(self, who, dx, dz, dy=None):
        """Someone is where the scene needs them, also after the mod has had time to pull them back."""
        time.sleep(1.5)
        a, b = who.state(), self.camera.state()
        off = [abs(a["x"] - b["x"] - dx), abs(a["z"] - b["z"] - dz), 0 if dy is None else abs(a["y"] - b["y"] - dy)]
        # Zombies and walking players rise on the nearest safe ground, which can be a few blocks off.
        if max(off) > (6 if who.state()["form"] == "Zombie" else 2):
            raise Failed(f"{who.name} is {a['x'] - b['x']:.0f} {a['y'] - b['y']:.0f} {a['z'] - b['z']:.0f} from the camera, wanted {dx} {dy} {dz}")

    def shot(self, text):
        """Everything stands ready: record for as long as it takes."""
        self.number += 1
        print(f"  {self.number:2}.  SHOT: {text}", flush=True)
        if not self.check:
            input("       [Enter when recorded] ")

    def cue(self, text, seconds=3):
        """A shot in which the server does something: start recording, then Enter, then it happens."""
        self.number += 1
        print(f"  {self.number:2}.  SHOT: {text}; start recording, it happens {seconds} s after Enter", flush=True)
        if self.check:
            return
        input("       [Enter starts the countdown] ")
        time.sleep(seconds)

    def stop(self):
        # Keep this run's logs: the next run starts in the same folder.
        logs = self.w.dir / "Logs"
        if logs.is_dir():
            kept = Path(os.environ["TRT_WORK"]) / "logs" / time.strftime("%Y%m%d-%H%M%S")
            shutil.copytree(logs, kept, dirs_exist_ok=True)
            if (self.w.dir / "server.log").exists():
                shutil.copy2(self.w.dir / "server.log", kept / "server.log")
            print(f"\nServer logs kept in {kept}", flush=True)
        self.w.stop()
