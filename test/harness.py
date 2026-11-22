"""Starts a local test server with headless bots and lets scenarios drive them. Standard library only."""
import json, os, queue, re, shutil, socket, subprocess, sys, threading, time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
GAME = Path(os.environ.get("VINTAGE_STORY") or next(
    (p for p in (Path.home() / "Games/VintageStory", Path("/var/lib/flatpak/app/at.vintagestory.VintageStory/x86_64/stable/active/files/extra/vintagestory")) if p.exists()), "missing"))
DOTNET = os.environ.get("DOTNET") or str(Path.home() / ".dotnet/dotnet")
WORK = Path(os.environ.get("TRT_WORK") or Path.home() / ".cache/temporalrebirth-test")
ENV = {**os.environ, "VINTAGE_STORY": str(GAME), "DOTNET_ROOT": str(Path(DOTNET).parent), "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
SERVER_CONFIG = "{ VerifyPlayerAuth: false, AdvertiseServer: false, Upnp: false }"
FLAT_CONFIG = SERVER_CONFIG[:-2] + ", WorldConfig: { WorldType: 'superflat' } }"


class Failed(Exception):
    pass


def build():
    for project, target in ((REPO / "TemporalRebirth.csproj", ["-t:Package"]), (REPO / "test/bots", []), (REPO / "test/helper", [])):
        done = subprocess.run([DOTNET, "build", str(project), "-c", "Release", "-v", "q", "-nologo", *target], env=ENV, capture_output=True, text=True, cwd=REPO)
        if done.returncode:
            sys.exit(done.stdout + done.stderr)


def newest(folder, mod):
    zips = sorted(folder.glob(mod + "_*.zip"), key=lambda p: p.stat().st_mtime)
    if not zips:
        raise Failed(f"put {mod}_<version>.zip into {folder}")
    return zips[-1]


def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


RUNNING = []


def kill_everything():
    """An interrupted run must not leave servers behind."""
    for process in list(RUNNING):
        process.proc.kill()


class Process:
    """A child process whose output lines are kept and can be waited for."""

    def __init__(self, args, log, cwd=None):
        self.lines, self.cond, self.log = [], threading.Condition(), open(log, "a", encoding="utf8")
        self.proc = subprocess.Popen(args, cwd=cwd, env=ENV, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf8", errors="replace", bufsize=1)
        RUNNING.append(self)
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        for line in self.proc.stdout:
            self.log.write(line)
            self.log.flush()
            with self.cond:
                self.lines.append(line.rstrip("\n"))
                self.cond.notify_all()

    def send(self, text):
        self.proc.stdin.write(text + "\n")
        self.proc.stdin.flush()

    def mark(self):
        return len(self.lines)

    def wait(self, pattern, since=0, timeout=15):
        rx, end = re.compile(pattern), time.monotonic() + timeout
        with self.cond:
            i = since
            while True:
                while i < len(self.lines):
                    m = rx.search(self.lines[i])
                    if m:
                        return m
                    i += 1
                left = end - time.monotonic()
                if left <= 0:
                    return None
                self.cond.wait(left)


class World:
    """One server run: its config, its bots and helper commands."""

    def __init__(self, name, config=None, lives=2, mods=(), raw_config=None, files=None, flat=True):
        self.name, self.lives, self.mods = name, lives, mods
        self.dir = WORK / "runs" / name
        shutil.rmtree(self.dir, ignore_errors=True)
        self.dir.parent.mkdir(parents=True, exist_ok=True)
        source = template(lives, flat)
        shutil.copytree(source, self.dir)
        # The generated config holds absolute paths (world, mods); a copy must not touch the template.
        server_config = self.dir / "serverconfig.json"
        server_config.write_text(server_config.read_text(encoding="utf8").replace(str(source), str(self.dir)), encoding="utf8")
        (self.dir / "Mods").mkdir(exist_ok=True)
        for mod in (newest(REPO / "dist", "temporalrebirth"), *(newest(WORK / "mods", m) for m in mods)):
            shutil.copy(mod, self.dir / "Mods")
        shutil.copytree(REPO / "test/helper/bin/mod", self.dir / "Mods/trt", dirs_exist_ok=True)
        (self.dir / "ModConfig").mkdir(exist_ok=True)
        if raw_config is not None:
            self.config_path.write_text(raw_config, encoding="utf8")
        elif config is not None:
            self.config_path.write_text(json.dumps(config), encoding="utf8")
        for path, data in (files or {}).items():
            (self.dir / path).write_text(json.dumps(data), encoding="utf8")
        self.server = self.bots = None
        self.start()

    @property
    def config_path(self):
        return self.dir / "ModConfig/temporalrebirth.json"

    def start(self):
        self.port = free_port()
        self.server = start_server(self.dir, self.port)
        self.start_bots()
        if not self.log_has("Mods, sorted by dependency: .*temporalrebirth") or not self.log_has("Mods, sorted by dependency: .*trt"):
            raise Failed("the mod or the test helper did not load")

    def start_bots(self):
        self.bots = Process([DOTNET, str(REPO / "test/bots/bin/Release/net10.0/bots.dll"), str(self.port)], self.dir / "bots.log")

    def drop_bots(self):
        """Disconnects everyone, so a failed test leaves nobody behind."""
        if self.bots:
            self.bots.proc.kill()
        self.start_bots()

    def stop(self):
        if self.bots:
            self.bots.proc.kill()
            self.bots = None
        if self.server:
            stop_server(self.server)
            self.server = None

    def crash(self):
        """Kills the server without saving, like a power cut."""
        self.bots.proc.kill()
        self.server.proc.kill()
        self.server.proc.wait()
        self.bots = self.server = None

    def restart(self):
        self.stop()
        self.start()

    def console(self, text):
        """Runs a console command and waits until the server took it, so two commands never merge into one line."""
        since = self.server.mark()
        self.server.send(text)
        if not self.server.wait(r"Handling Console Command " + re.escape(text), since):
            raise Failed("server did not take: " + text)
        return since

    def trt(self, text):
        since = self.console("/trt " + text)
        m = self.server.wait(r"TRT (.*)$", since)
        if not m:
            raise Failed("no answer to /trt " + text)
        answer = json.loads(m.group(1))
        if isinstance(answer, dict) and "error" in answer:
            raise Failed(answer["error"])
        return answer

    def log_has(self, pattern, timeout=0.1):
        return bool(self.server.wait(pattern, 0, timeout))

    def join(self, *names):
        return [Bot(self, n).join() for n in names]


class Bot:
    def __init__(self, world, name):
        self.world, self.name = world, name

    def join(self, expect=True):
        since = self.world.bots.mark()
        self.world.bots.send("join " + self.name)
        if expect:
            if not self.world.bots.wait(rf"^\[{self.name}\] joined", since, 30):
                raise Failed(self.name + " could not join")
            time.sleep(1)
        return self

    def vanish(self):
        """Leaves without waiting for anything."""
        self.world.bots.send(self.name + " quit")

    def relog(self, away=0.45):
        self.vanish()
        time.sleep(away)
        return self.join()

    def quit(self):
        since = self.world.bots.mark()
        self.world.bots.send(self.name + " quit")
        self.world.bots.wait(rf"^\[{self.name}\] closed", since, 5)
        time.sleep(0.5)

    def say(self, text):
        since = self.world.bots.mark()
        self.world.bots.send(f"{self.name} say {text}")
        return since

    def ask(self, text, pattern, timeout=8):
        """Says something and returns the chat line matching the pattern, failing without one."""
        return self.heard(pattern, self.say(text), timeout)

    def heard(self, pattern, since=0, timeout=8):
        m = self.world.bots.wait(rf"^\[{self.name}\] chat: .*{pattern}", since, timeout)
        if not m:
            raise Failed(f"{self.name} did not hear /{pattern}/")
        return m.group(0)

    def silent(self, pattern, since, seconds=2):
        m = self.world.bots.wait(rf"^\[{self.name}\] chat: .*{pattern}", since, seconds)
        if m:
            raise Failed(f"{self.name} heard {m.group(0)!r}")

    def respawn(self):
        self.world.bots.send(self.name + " respawn")

    def kill(self):
        self.world.console(f"/entity cmd {self.name} kill")

    def state(self):
        return self.world.trt("state " + self.name)

    def until(self, timeout=10, **want):
        """Waits until the state matches, e.g. until(mode='Spectator', alive=True)."""
        end = time.monotonic() + timeout
        while True:
            now = self.state()
            if all(now[k] == v for k, v in want.items()):
                return now
            if time.monotonic() > end:
                raise Failed(f"{self.name}: wanted {want}, have { {k: now[k] for k in want} }")
            time.sleep(0.4)

    def go(self, dx, dy, dz):
        """Teleports and waits until the land there is loaded, which can take seconds under load."""
        since = self.world.server.mark()
        self.world.trt(f"move {self.name} {dx} {dy} {dz}")
        self._arrive(since)

    def go_near(self, other):
        since = self.world.server.mark()
        self.world.trt(f"near {self.name} {other.name}")
        self._arrive(since)

    def _arrive(self, since):
        if not self.world.server.wait("ARRIVED " + self.name + "$", since, 40):
            raise Failed(self.name + ": the land there did not load")

    def lose_all_lives(self):
        """Dies until no life is left, respawning in between."""
        while True:
            left = self.state()["lives"]
            self.kill()
            if left <= 1:
                return self
            self.until(alive=False)
            time.sleep(3)
            self.respawn()
            self.until(alive=True)

    def gear(self):
        return self.world.trt("gear " + self.name)["consumed"]


def distance(a, b, axes="xyz"):
    return sum((a[k] - b[k]) ** 2 for k in axes) ** 0.5


def check(condition, text):
    if not condition:
        raise Failed(text)


def start_server(directory, port, config=SERVER_CONFIG):
    server = Process([DOTNET, str(GAME / "VintagestoryServer.dll"), "--dataPath", str(directory), "--ip", "127.0.0.1", "--port", str(port), "--withconfig=" + config], directory / "server.log", cwd=directory)
    if not server.wait("Dedicated Server now running", 0, 180):
        server.proc.kill()
        raise Failed("server did not start, see " + str(directory / "server.log"))
    time.sleep(1)
    return server


def stop_server(server):
    try:
        server.send("/stop")
        server.proc.wait(60)
    except (subprocess.TimeoutExpired, BrokenPipeError):
        server.proc.kill()


def template(lives, flat=True):
    """A generated, peaceful world that every run starts from. Flat land takes no time to generate."""
    path = WORK / f"template-{lives}{'-flat' if flat else ''}"
    if (path / "ready").exists():
        return path
    shutil.rmtree(path, ignore_errors=True)
    path.mkdir(parents=True)
    server = start_server(path, free_port(), FLAT_CONFIG if flat else SERVER_CONFIG)
    for setting in (f"playerlives {lives}", "creatureHostility off", "temporalStorms off"):
        since = server.mark()
        server.send("/worldconfig " + setting)
        server.wait(r"Handling Console Command /worldconfig " + setting, since)
        time.sleep(1)
    stop_server(server)
    (path / "ready").touch()
    return path
