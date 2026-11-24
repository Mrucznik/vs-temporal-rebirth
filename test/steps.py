"""Steps shared by the scenarios: waiting for states, setting up players, places and settings."""
import json, re, subprocess, time
from harness import REPO, Failed, check


def slow(seconds):
    """How long a test takes, so the runner can split groups evenly."""
    def mark(test):
        test.seconds = seconds
        return test
    return mark


def known_bug(text, failure):
    """Marks a test expected to fail with a message containing `failure`."""
    def mark(test):
        test.known_bug = text, failure
        return test
    return mark


def read_config(w):
    """The file without its comments."""
    return json.loads(re.sub(r"^\s*//.*$", "", w.config_path.read_text(encoding="utf8"), flags=re.M))


def consistent(w, bot, timeout=8):
    """Waits until the mod is done with the player. Nobody may end up alive with no lives, or listed without a name."""
    end = time.monotonic() + timeout
    while True:
        s, entry = bot.state(), w.trt("registry").get("bot-" + bot.name)
        if entry is None:
            good = s["mode"] == "Survival" and s["alive"] and s["lives"] > 0
        else:
            good = entry["name"] == bot.name and entry["phase"] == "Serving" and s["mode"] == "Spectator" and s["alive"] and s["lives"] == 0
        if good:
            return s
        if time.monotonic() > end:
            raise Failed(f"inconsistent {bot.name}: {s['mode']} alive={s['alive']} lives={s['lives']} entry={entry}")
        time.sleep(0.4)


def at_spawn(bot, timeout=10):
    """The teleport may still be on its way when the player already counts as back."""
    end = time.monotonic() + timeout
    while True:
        s = bot.state()
        if abs(s["x"] - s["spawnX"]) < 60 and abs(s["z"] - s["spawnZ"]) < 60:
            return
        if time.monotonic() > end:
            raise Failed(f"came back {abs(s['x'] - s['spawnX']):.0f} blocks away from the spawn point")
        time.sleep(0.4)


def last_life(w, bot):
    w.console(f"/rebirth setlives {bot.name} 1")
    bot.until(lives=1)


def ghost(w, bot):
    last_life(w, bot)
    bot.kill()
    bot.until(mode="Spectator", alive=True)


def walks(w, bot):
    bot.until(mode="Survival", alive=True, lives=0, timeout=15)
    wait_phase(w, bot, "Serving")


# Most scenarios were written for rituals at rifts that bring players back next to the caster, the defaults before the
# Ghost preset (rituals next to the ghost) became the default; the harness writes the same into a world without a config.
RIFT_RITUALS = {"Ritual.Where": "Rift", "Ritual.ReviveAt": "Caster"}


def settings(w, preset="Ghost", **values):
    """Starts from a preset and changes single settings, like an admin in the console. Ghost comes with rituals at rifts."""
    w.console("/rebirth preset " + preset)
    pinned = RIFT_RITUALS if preset == "Ghost" else {}
    for key, value in {"Lives.Max": -1, "Lives.PerGear": 1, **pinned, **values}.items():
        since = w.console(f"/rebirth config {key} {value}")
        if not w.server.wait(rf"{key} is now ", since, 5):
            raise Failed(f"could not set {key} to {value}")


def point(w, bot, name):
    """Marks a place where the bot stands."""
    w.console(f"/player {bot.name} privilege grant temporalrebirth.points")
    time.sleep(0.5)
    bot.ask(f"/rebirth point set {name}", f"Place '{name}' marked")


def phase(w, bot):
    entry = w.trt("registry").get("bot-" + bot.name)
    return entry and entry["phase"]


def wait_phase(w, bot, wanted, timeout=10):
    end = time.monotonic() + timeout
    while phase(w, bot) != wanted:
        if time.monotonic() > end:
            raise Failed(f"{bot.name}: wanted phase {wanted}, have {phase(w, bot)}")
        time.sleep(0.3)


def refused(w, bot):
    """Tries to join and returns the text shown instead."""
    since = w.bots.mark()
    bot.join(expect=False)
    m = w.bots.wait(rf"^\[{bot.name}\] (kicked: .*|joined)", since, 20)
    check(m and m.group(1).startswith("kicked: "), f"should be refused, got {m and m.group(1)}")
    return m.group(1)[8:]


def node(code):
    files = "".join(f"vm.runInContext(fs.readFileSync({json.dumps(str(REPO / 'docs/configurator' / f))},'utf8'),c);" for f in ("schema.js", "logic.js", "i18n/en.js", "i18n.js"))
    script = "const fs=require('fs'),vm=require('vm');const w={};const c=vm.createContext({window:w,TextEncoder,TextDecoder,btoa,atob,Uint8Array});" + files + "const T=w.TR;T.localize('en');" + code
    return subprocess.run(["node", "-e", script], capture_output=True, text=True, check=True).stdout


def exile(w, bot, **extra):
    settings(w, "Exile", **extra)
    last_life(w, bot)


def zone(w, admin, dx, size=10, group="afterlife", everyone="false"):
    """A fresh zone around a place, with the starting place in the middle."""
    w.trt("unclaim")
    admin.go(dx, 0, 0)
    time.sleep(1)
    w.trt(f"claim {admin.name} {group} {size} {everyone}")
    point(w, admin, "start")
    return admin.state()


def serve(w, bot, form="Player"):
    last_life(w, bot)
    bot.kill()
    bot.until(mode="Spectator" if form in ("Ghost", "Spectator") else "Survival", alive=True, lives=0, timeout=15)
    wait_phase(w, bot, "Serving")
    time.sleep(1.5)


def inside(bot, middle, size=10):
    s = bot.state()
    return abs(s["x"] - middle["x"]) <= size + 1 and abs(s["z"] - middle["z"]) <= size + 1


def wait_inside(bot, middle, text, timeout=12):
    end = time.monotonic() + timeout
    while not inside(bot, middle):
        if time.monotonic() > end:
            s = bot.state()
            raise Failed(f"{text}: {s['x'] - middle['x']:.0f}, {s['z'] - middle['z']:.0f} from the middle")
        time.sleep(0.4)


def downs(w, bot, lives, mode="Downs"):
    settings(w, **{"Lives.Count": mode})
    w.console(f"/rebirth setlives {bot.name} {lives}")
    bot.until(lives=lives, alive=True)


def down(w, bot):
    w.trt(f"hurt {bot.name} 500")
    bot.until(downed=True, alive=True)
    time.sleep(0.5)


def get_up(w, bot):
    w.console("/downed revive " + bot.name)
    bot.until(downed=False, alive=True)
    time.sleep(0.5)


def respawn(bot):
    time.sleep(1)
    bot.respawn()
    return bot.until(alive=True, mode="Survival", downed=False)


def two_ghosts_at_a_rift(w, *names):
    a, b, ola = w.join(*names)
    ghost(w, a), ghost(w, b)
    w.trt("clearrifts")
    w.trt("rift " + ola.name + " 4")
    return a, b, ola


def leave(w, ghosts, others=()):
    """Brings back who is still a ghost before everyone leaves: an offline ghost still waits and would change later hints."""
    for bot in ghosts:
        w.console("/rebirth revive " + bot.name)
        consistent(w, bot)
    for bot in (*ghosts, *others):
        bot.quit()

