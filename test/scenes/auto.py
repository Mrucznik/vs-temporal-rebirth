#!/usr/bin/env python3
"""Records scenes with no human: own X display (Xvfb), the logged in test client on it, xdotool for input.

test/scenes/auto.py TRIAL | PILOT [--display 99]

Software rendering only gives a frame or two per second, so the game's own recorder is used with a fixed step
(.vrec tickfps 30): every drawn frame is 1/30 s of the client's time. The server keeps real time, so waits are counted in
drawn frames. Screenshots of each step go next to the video. Never touches any display but its own.
"""
import argparse, math, os, re, signal, subprocess, sys, threading, time
from pathlib import Path

import clip
from stage import PORT, Camera, Failed, Stage, harness
from play import meadow, others_running, pilot_people, pilot_place
from stage import AIR, GRASS

CLIENT = Path.home() / ".var/app/at.vintagestory.VintageStory/trt-client"
WIDTH, HEIGHT = 1280, 720
SHOTS = clip.OUT / "steps"
STARTED = []


def log(text):
    print(time.strftime("%H:%M:%S ") + text, flush=True)


class Display:
    """An X server of our own; every command gets its number explicitly."""

    def __init__(self, number, attach=False):
        self.name = f":{number}"
        self.env = {k: v for k, v in os.environ.items() if k not in ("DISPLAY", "XAUTHORITY", "WAYLAND_DISPLAY")} | {"DISPLAY": self.name}
        self.proc = None
        if attach:
            # A display started elsewhere (Xwayland in a headless GNOME Shell, with the GPU): used, never stopped.
            if not Path(f"/tmp/.X11-unix/X{number}").exists():
                raise Failed(f"display :{number} is not there")
            return
        if Path(f"/tmp/.X11-unix/X{number}").exists() or Path(f"/tmp/.X{number}-lock").exists():
            raise Failed(f"display :{number} is taken")
        self.proc = subprocess.Popen(["Xvfb", self.name, "-screen", "0", f"{WIDTH}x{HEIGHT}x24", "-nolisten", "tcp"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
        STARTED.append(self.proc)
        for _ in range(50):
            if Path(f"/tmp/.X11-unix/X{number}").exists():
                return
            time.sleep(0.2)
        raise Failed("Xvfb did not start")

    def run(self, *args):
        return subprocess.run([str(a) for a in args], env=self.env, capture_output=True, text=True)

    def x(self, *args):
        return self.run("xdotool", *args).stdout.strip()

    def shot(self, name):
        SHOTS.mkdir(parents=True, exist_ok=True)
        path = SHOTS / f"{time.strftime('%H%M%S')}-{name}.png"
        self.grab(path)
        return path

    def grab(self, path):
        """ImageMagick's import sometimes returns an old picture on Xwayland; ffmpeg reads what is shown now."""
        self.run("ffmpeg", "-y", "-v", "error", "-f", "x11grab", "-video_size", f"{WIDTH}x{HEIGHT}", "-i", self.name, "-frames:v", "1", path)

    def pixel(self, x, y):
        return self.area(x, y, 1, 1)

    def dialog_open(self):
        """The character dialog: its title bar at both ends has the colour of the game's dialogs."""
        return all(self.pixel(x, 99) == (86, 71, 54) for x in (600, 900))

    def near(self, x, y, rgb, tolerance=12):
        got = self.pixel(x, y)
        return got is not None and all(abs(a - b) <= tolerance for a, b in zip(got, rgb))

    def area(self, x, y, w, h):
        """Average colour of a rectangle of the screen."""
        tmp = clip.OUT / "pixel.png"
        self.grab(tmp)
        out = subprocess.run(["convert", str(tmp), "-crop", f"{w}x{h}+{x}+{y}", "-scale", "1x1!", "-depth", "8", "txt:-"], capture_output=True, text=True).stdout
        m = re.search(r"\((\d+),(\d+),(\d+)", out.splitlines()[-1] if out else "")
        return tuple(map(int, m.groups())) if m else None

    def intro_open(self):
        """The intro dialog (opened by holding K) is a brown box in the middle; its texture averages to about this."""
        got = self.area(600, 420, 60, 40)
        return got is not None and all(abs(a - b) <= 18 for a, b in zip(got, (78, 63, 47)))

    def minimap_visible(self):
        """The right edge of the minimap frame, top right."""
        return self.near(1268, 137, (67, 54, 44))

    def window(self):
        found = self.x("search", "--name", "Vintage Story").split()
        return found[0] if found else None

    def key(self, *keys):
        self.x("key", "--delay", "120", *keys)

    def chat(self, text):
        """Opens the chat, types a line and sends it; at a frame a second every key needs time."""
        self.key("t")
        time.sleep(2)
        self.x("type", "--delay", "80", text)
        time.sleep(2)
        self.key("Return")
        time.sleep(3)

    def look(self, dx, dy, wait=2.5):
        """Turns the camera: the game puts the pointer back into the middle every frame. Software rendering needs a long
        wait before the next move counts; with the GPU a fraction of a second is enough."""
        self.x("mousemove", WIDTH // 2 + dx, HEIGHT // 2 + dy)
        time.sleep(wait)

    def stop(self):
        if self.proc:
            self.proc.terminate()


class Client:
    def __init__(self, display):
        command = ["nice", "-n", "10", "dbus-run-session", "--", "flatpak", "run", "--filesystem=xdg-videos/Vintagestory:create",
                   "--env=LP_NUM_THREADS=6", "at.vintagestory.VintageStory", "--dataPath", str(CLIENT), "--connect", f"127.0.0.1:{PORT}"]
        self.log = open(clip.OUT / "client.log", "w")
        if os.environ.get("TRT_GPU"):
            # The group that may use the GPU is not in this login yet; sg gives it to the client only.
            command = ["sg", "render", "-c", " ".join(command)]
        self.proc = subprocess.Popen(command, env=display.env, stdout=self.log, stderr=subprocess.STDOUT, start_new_session=True)
        STARTED.append(self.proc)

    def stop(self):
        """The whole process group: dbus, flatpak, bwrap, the game."""
        try:
            os.killpg(self.proc.pid, signal.SIGTERM)
            self.proc.wait(20)
        except (ProcessLookupError, subprocess.TimeoutExpired):
            try:
                os.killpg(self.proc.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass


def frames_per_second(d, seconds=6):
    """Drawn frames per second, by grabbing the display and counting frames that differ."""
    file = clip.OUT / "fps.mkv"
    d.run("ffmpeg", "-y", "-v", "error", "-f", "x11grab", "-framerate", "30", "-video_size", f"{WIDTH}x{HEIGHT}", "-draw_mouse", "0", "-i", d.name, "-t", seconds, "-c:v", "libx264", "-preset", "ultrafast", "-qp", "0", file)
    hashes = [l.split(",")[-1] for l in subprocess.run(["ffmpeg", "-v", "error", "-i", file, "-f", "framemd5", "-"], capture_output=True, text=True).stdout.splitlines() if not l.startswith("#")]
    file.unlink(missing_ok=True)
    return max(0.2, (1 + sum(a != b for a, b in zip(hashes, hashes[1:]))) / seconds)


def join(s, d):
    log("waiting for the client to join")
    end = time.monotonic() + 300
    while not (names := s.w.trt("players")) and time.monotonic() < end:
        time.sleep(2)
    if not names:
        raise Failed("the client did not join, see client.log")
    s.camera = Camera(s.w, names[0], True)
    time.sleep(15)
    d.x("windowsize", d.window(), WIDTH, HEIGHT)
    d.x("windowfocus", d.window())
    # A new world asks for a character: confirm the skin, then the class (both buttons bottom right). The server cannot
    # tell whether the dialog is still open, the picture can: its title bar is a flat brown.
    for attempt in range(6):
        time.sleep(4)
        if not d.dialog_open():
            break
        d.shot(f"character-{attempt}")
        d.x("mousemove", 934, 607, "click", "1")
    d.x("mousemove", WIDTH // 2, HEIGHT // 2)
    time.sleep(3)
    d.shot("joined")
    if d.dialog_open():
        raise Failed("could not get past the character dialog; see the screenshots")


def clean_screen(d):
    """Hides what does not belong in the picture and returns what to put back with restore_screen:
    - the start tip: holding K opens the intro dialog and removes the tip; the dialog is closed with 'Close & never show
      again', saved in the recording profile only, so it no longer pops up during shots;
    - the minimap (F6) and the block name overlay (Ctrl+B): both are saved settings of the player, so both are put back;
    - the chat: closed with Tab if open (also a saved setting, put back); the death message is off in the scene world,
      the mod's own messages still open it (see the report)."""
    d.x("keydown", "k")
    time.sleep(1.6)
    d.x("keyup", "k")
    dismiss_intro(d, 4)
    changed = {"minimap": d.minimap_visible(), "blockinfo": True}
    if changed["minimap"]:
        d.key("F6")
        time.sleep(1)
    # The overlay is on unless the player turned it off; its text cannot be told from the world by one pixel, so it is
    # switched off once and switched back by restore_screen.
    d.key("ctrl+b")
    time.sleep(1)
    return changed


def dismiss_intro(d, seconds=0.0):
    """Closes the intro dialog for good ('Close & never show again') if it is open now or opens within the given time."""
    end = time.monotonic() + seconds
    while True:
        if d.intro_open():
            # "Close & never show again": the recording profile should never see it again.
            d.x("mousemove", 562, 483, "click", "1")
            time.sleep(1)
            d.x("mousemove", WIDTH // 2, HEIGHT // 2)
            time.sleep(0.5)
            if not d.intro_open():
                return True
        if time.monotonic() > end:
            return False
        time.sleep(0.5)


class ChatCloser:
    """Closes the chat each time a message opens it, while a shot is recorded. The client opens the chat on every new
    line; the bots hear the same lines at the same moment, so a line in the bots' output means: the chat is open now,
    press Tab (once per burst of lines). Watching pixels instead failed in brighter light, where the chat's overlay
    takes the colour of the sky behind it.
    Tab stores 'chat hidden' as a setting of the player; stop() presses it once more, which stores 'chat shown' back,
    the state any chat typing (T) leaves it in. The reopened chat closes by itself after 15 s, as usual."""

    def __init__(self, d, world, listener):
        self.d, self.world, self.listener = d, world, listener
        self.closed, self.running = 0, True
        self.since = world.bots.mark()
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def run(self):
        pattern = rf"^\[{re.escape(self.listener)}\] chat:"
        while self.running:
            m = self.world.bots.wait(pattern, self.since, 0.2)
            if not m:
                continue
            # Let the burst arrive and the client draw the chat once, then close it.
            time.sleep(0.12)
            self.since = self.world.bots.mark()
            self.d.key("Tab")
            self.closed += 1

    def stop(self):
        self.running = False
        self.thread.join(5)
        if self.closed:
            self.d.key("Tab")
            time.sleep(0.5)
        return self.closed


def restore_screen(d, changed):
    if changed.get("minimap") and not d.minimap_visible():
        d.key("F6")
        time.sleep(1)
    if changed.get("blockinfo"):
        d.key("ctrl+b")
        time.sleep(1)


def aim(s, d, radians, wait=0.12):
    """Tilts the camera until the server sees the wanted pitch (the game counts it from straight up: level is pi)."""
    pitch = s.w.trt("view " + s.camera.name)["pitch"]
    for _ in range(60):
        if abs(pitch - radians) < 0.03:
            break
        d.look(0, int(max(-120, min(120, (radians - pitch) * 180))) or (1 if radians > pitch else -1), wait)
        time.sleep(0.05)
        pitch = s.w.trt("view " + s.camera.name)["pitch"]
    return pitch


def aim_down(s, d, radians):
    """For software rendering: slow steps."""
    return aim(s, d, radians, 2.5)


LEVEL = math.pi + 0.06
# 0.45 below level puts a ground block about 3.4 blocks ahead under the crosshair, within reach: the gear needs a block.
GROUND = math.pi + 0.45


def film(d, path):
    """Records the display in real time until stopped; for a client drawing at least 30 frames a second."""
    return subprocess.Popen(["ffmpeg", "-y", "-v", "error", "-f", "x11grab", "-framerate", "30", "-video_size", f"{WIDTH}x{HEIGHT}", "-draw_mouse", "0",
                             "-i", d.name, "-c:v", "libx264", "-preset", "veryfast", "-crf", "14", "-pix_fmt", "yuv420p", str(path)],
                            env=d.env, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def pilot_realtime(s, d):
    """The pilot on a display with the GPU: everything in real time, recorded with x11grab, no chat commands."""
    meadow(s)
    pilot_people(s)
    s.box(s.camera, GRASS, -12, -3, -12, 12, -1, 12)
    s.box(s.camera, AIR, -12, 0, -12, 12, 5, 12)
    d.key("2")
    time.sleep(3)
    close_dialogs(d)
    pilot_place(s)
    funia = s.bots["Funia"]
    # Closer than in the slow version: the camera looks down at the ground within reach (about 4.5 blocks), and Funia
    # must still be in the picture.
    # Six blocks: far enough that she stays in the picture when she flies up; the gear only needs ground within reach
    # in front of the camera, the ritual (NearPlayer) works up to 8 blocks from her.
    s.arrive(funia, f"front Funia {s.camera.name} 6")
    # On a hilly spot "front" can stand her on a wall of the mown meadow; the meadow itself is level with the camera.
    a, c = funia.state(), s.camera.state()
    if abs(a["y"] - c["y"]) > 0.5:
        s.beside(funia, s.camera, round(a["x"] - c["x"]), 0, round(a["z"] - c["z"]))
    a = funia.state()
    log(f"Funia at {a['x'] - c['x']:.0f} {a['y'] - c['y']:.0f} {a['z'] - c['z']:.0f} from the camera")
    # Level while Funia is shown, so her name and head are in the picture; down only for the gear.
    log("pitch " + str(aim(s, d, LEVEL)))
    changed = clean_screen(d)
    d.chat(".clearchat")
    time.sleep(16)
    dismiss_intro(d)
    d.shot("pilot-ready")
    file = clip.OUT / f"PILOT-{time.strftime('%Y%m%d-%H%M%S')}.mkv"
    a, c = funia.state(), s.camera.state()
    vx, vz = a["x"] - c["x"], a["z"] - c["z"]
    norm = math.hypot(vx, vz) or 1
    side = (-vz / norm, vx / norm)
    recorder = film(d, file)
    # The chat stays: its lines (died, last life, ritual) tell what happens. TRT_HIDE_CHAT=1 closes it instead.
    chat = ChatCloser(d, s.w, "Funia") if os.environ.get("TRT_HIDE_CHAT") == "1" else None
    marks = {}
    try:
        time.sleep(1.5)
        marks["death"] = time.time()
        s.w.console("/rebirth setlives Funia 1")
        funia.until(lives=1)
        funia.kill()
        funia.until(mode="Spectator", alive=True, lives=0, timeout=20)
        time.sleep(1.2)
        # A ghost flies: drift aside and up, hover, then (with the gear in hand, as an echo) come back down to the spot.
        glide(s, funia, side[0] * 2.5, 2.5, side[1] * 2.5, 2.4)
        time.sleep(0.8)
        d.key("1")
        time.sleep(1.2)
        glide(s, funia, -side[0] * 2.5, -2.5, -side[1] * 2.5, 2.2)
        time.sleep(0.6)
        aim(s, d, GROUND, 0.04)
        d.x("mousedown", "3")
        try:
            funia.until(mode="Survival", alive=True, timeout=8)
            log("the held gear performed the ritual")
        except Failed:
            log("the held gear did nothing; the helper does the ritual")
            s.w.trt("gear " + s.camera.name)
            funia.until(mode="Survival", alive=True, timeout=30)
        finally:
            d.x("mouseup", "3")
        marks["back"] = time.time()
        aim(s, d, LEVEL, 0.04)
        time.sleep(2.5)
    finally:
        recorder.communicate(b"q", timeout=30)
        if chat:
            log(f"chat closed {chat.stop()} times during the shot")
        restore_screen(d, changed)
    return file


def glide(s, who, dx, dy, dz, seconds):
    """Smooth flight by an offset in whole blocks (helper /trt glide, 50 ms steps with easing); waits until done."""
    since = s.w.server.mark()
    s.w.trt(f"glide {who.name} {round(dx)} {round(dy)} {round(dz)} {int(seconds * 1000)}")
    if not s.w.server.wait("GLIDED " + who.name + "$", since, seconds + 10):
        raise Failed(who.name + " did not finish gliding")


def ghost_scene(s, d):
    """Funia as a ghost 5 blocks ahead, level camera, gear in hand: the echo, ready to be looked at."""
    meadow(s)
    pilot_people(s)
    s.box(s.camera, GRASS, -12, -3, -12, 12, -1, 12)
    s.box(s.camera, AIR, -12, 0, -12, 12, 5, 12)
    d.key("2")
    time.sleep(3)
    close_dialogs(d)
    pilot_place(s)
    funia = s.bots["Funia"]
    a, c = funia.state(), s.camera.state()
    if abs(a["y"] - c["y"]) > 0.5:
        s.beside(funia, s.camera, round(a["x"] - c["x"]), 0, round(a["z"] - c["z"]))
    aim(s, d, LEVEL)
    s.w.console("/rebirth setlives Funia 1")
    funia.until(lives=1)
    funia.kill()
    funia.until(mode="Spectator", alive=True, lives=0, timeout=20)
    d.key("1")
    return funia


def rift_compare(s, d):
    """A real rift of the game beside the echo, at the pilot's hour: a still of both, then the echo close up, moving."""
    funia = ghost_scene(s, d)
    a, c = funia.state(), s.camera.state()
    # Beside her, across the view: the view runs from the camera to Funia.
    vx, vz = a["x"] - c["x"], a["z"] - c["z"]
    norm = math.hypot(vx, vz) or 1
    sx, sz = -vz / norm, vx / norm
    s.w.trt(f"riftnear Funia {round(sx * 2.5)} 1 {round(sz * 2.5)} 3")
    changed = clean_screen(d)
    d.chat(".clearchat")
    time.sleep(16)
    try:
        dismiss_intro(d)
        shots = [d.shot(f"rift-compare-{i}") for i in range(2)]
        # Close up: three blocks from her, still looking at her; without the rift, whose nearness tints the picture.
        s.w.trt("clearrifts")
        s.beside(s.camera, funia, -round(vx / norm * 3), 0, -round(vz / norm * 3))
        aim(s, d, LEVEL)
        time.sleep(2)
        dismiss_intro(d)
        file = clip.OUT / f"ECHO-close-{time.strftime('%Y%m%d-%H%M%S')}.mkv"
        recorder = film(d, file)
        time.sleep(4)
        recorder.communicate(b"q", timeout=30)
        # The wisp alone (no gear in hand) at the distance of the scene, at the pilot's hour and at night.
        d.key("2")
        s.beside(s.camera, funia, -round(vx / norm * 6), 0, -round(vz / norm * 6))
        aim(s, d, LEVEL)
        time.sleep(2)
        dismiss_intro(d)
        subprocess.run(["cp", str(d.shot("wisp-dusk")), str(clip.OUT / "wisp-dusk.png")])
        s.time("23:30")
        time.sleep(6)
        log(f"night: {s.w.trt('view ' + s.camera.name)}")
        subprocess.run(["cp", str(d.shot("wisp-night")), str(clip.OUT / "wisp-night.png")])
    finally:
        restore_screen(d, changed)
    still = clip.OUT / "rift-vs-echo.png"
    subprocess.run(["cp", str(shots[-1]), str(still)])
    frames = clip.OUT / "echo-close-4frames.png"
    subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", str(file), "-vf", "fps=1,scale=640:-2,tile=4x1", "-frames:v", "1", str(frames)])
    log(f"rift and echo: {still}; echo close up: {file} and {frames}")
    return file


def close_dialogs(d):
    for attempt in range(6):
        if not d.dialog_open():
            return
        d.shot(f"character-again-{attempt}")
        d.x("mousemove", 934, 607, "click", "1")
        time.sleep(4)
    d.x("mousemove", WIDTH // 2, HEIGHT // 2)


def video_seconds(fps, seconds):
    """Wall time for a number of seconds of video when every drawn frame is 1/30 s."""
    return seconds * 30 / fps


def record(d, s, name, scene):
    """The game's recorder with a fixed step around a scene; returns the new file."""
    began = time.time()
    d.chat(".vrec tickfps 30")
    d.chat(".vrec videofps 30")
    d.chat(".clearchat")
    d.chat(".vrec start")
    d.chat(".clearchat")
    try:
        scene()
    finally:
        d.chat(".vrec stop")
        d.chat(".vrec tickfps -1")
    return clip.newest(began)


def trial(s, d, fps):
    meadow(s)
    s.time("12:00")
    time.sleep(5)
    d.shot("trial-start")

    def walk():
        time.sleep(16)
        # Five seconds of video: turning slowly.
        end = time.monotonic() + video_seconds(fps, 5)
        while time.monotonic() < end:
            d.look(25, 0)
        d.shot("trial-end")

    return record(d, s, "TRIAL", walk)


def pilot(s, d, fps):
    meadow(s)
    pilot_people(s)
    # Tall grass would hide Funia: a mown meadow around.
    s.box(s.camera, GRASS, -12, -3, -12, 12, -1, 12)
    s.box(s.camera, AIR, -12, 0, -12, 12, 5, 12)
    d.key("2")
    time.sleep(4)
    pilot_place(s)
    funia = s.bots["Funia"]
    time.sleep(8)
    dismiss_intro(d)
    d.shot("pilot-ready")
    wait = lambda seconds: time.sleep(video_seconds(fps, seconds))

    def scene():
        # The chat closes 15 s after the last line, counted in real time.
        time.sleep(16)
        wait(1.5)
        s.w.console("/rebirth setlives Funia 1")
        funia.until(lives=1)
        funia.kill()
        funia.until(mode="Spectator", alive=True, lives=0, timeout=20)
        d.shot("pilot-wisp")
        wait(2)
        d.key("1")
        wait(2.5)
        d.shot("pilot-echo")
        # Holding the gear with xdotool does not reach the game (seen on screenshots), so the helper performs the ritual
        # the way the game would; the picture shows the gear in hand and the echo, not the arm moving.
        wait(1)
        d.shot("pilot-aim")
        s.w.trt("gear " + s.camera.name)
        funia.until(mode="Survival", alive=True, timeout=30)
        d.shot("pilot-back")
        wait(2.5)

    return record(d, s, "PILOT", scene)


def main():
    options = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    options.add_argument("name", choices=["TRIAL", "PILOT", "RIFT"])
    options.add_argument("--display", type=int, default=99)
    options.add_argument("--attach", action="store_true", help="use a running display instead of starting Xvfb")
    options.add_argument("--realtime", action="store_true", help="the client draws 30 frames a second or more: grab the display")
    args = options.parse_args()
    if others_running():
        sys.exit("another test server or test/run.py is running")
    signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
    clip.OUT.mkdir(parents=True, exist_ok=True)
    stage = display = client = None
    try:
        display = Display(args.display, args.attach)
        stage = Stage(False)
        client = Client(display)
        join(stage, display)
        fps = frames_per_second(display)
        log(f"the client draws {fps:.1f} frames a second at {WIDTH}x{HEIGHT}")
        if args.realtime:
            if args.name == "RIFT":
                rift_compare(stage, display)
                return 0
            if args.name != "PILOT":
                raise Failed("--realtime has the pilot and RIFT")
            file = pilot_realtime(stage, display)
            # The first 0.8 s are the still picture before the death, the last second the camera coming back up.
            clip.report(file, args.name, trim=(0.8, 1.0))
            return 0
        file = (trial if args.name == "TRIAL" else pilot)(stage, display, fps)
        if not file:
            log("no video file: " + ", ".join(map(str, clip.look_in())))
            return 1
        clip.report(file, args.name, retimed=True, trim=(1.5, 0.7))
        return 0
    except Failed as e:
        log("failed: " + str(e))
        if display:
            display.shot("failed")
        return 1
    except KeyboardInterrupt:
        return 130
    finally:
        signal.signal(signal.SIGINT, signal.SIG_IGN)
        if client:
            client.stop()
        if stage:
            stage.stop()
        for process in harness.RUNNING:
            process.proc.kill()
        if display:
            display.stop()
        log("stopped")


if __name__ == "__main__":
    sys.exit(main())
