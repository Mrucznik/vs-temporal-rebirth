"""Faults found by reading 0.7.0, each with the test that shows it."""
import time
from harness import check, distance
from races import consistent, ghost, last_life
from v06_baseline import read_config, slow
from v07 import node, phase, point, refused, settings, wait_phase


def walks(w, bot):
    bot.until(mode="Survival", alive=True, lives=0, timeout=15)
    wait_phase(w, bot, "Serving")


# ---------- 1. zombies and players rise where it is safe ----------

@slow(25)
def zombie_does_not_rise_in_lava(w):
    settings(w, "Undead")
    ala, = w.join("Zrl")
    last_life(w, ala)
    w.trt("surround Zrl lava-still-7 2")
    home = ala.state()
    check(home["liquid"], "the bot stands in lava")
    ala.kill()
    walks(w, ala)
    time.sleep(3)
    s = ala.state()
    check(s["alive"] and not s["liquid"], f"out of the lava: alive={s['alive']} liquid={s['liquid']}")
    check(distance(s, home, "xz") < 20, "but close to where they died")
    w.trt("surround Zrl air 3")
    w.console("/rebirth revive Zrl")
    consistent(w, ala, 15)
    ala.quit()


@slow(25)
def second_quick_death_moves_to_the_spawn_point(w):
    settings(w, "Exile", **{"Place.At": "Body"})
    ala, = w.join("Sqd")
    w.trt("farspawn Sqd 250")
    last_life(w, ala)
    home = ala.state()
    ala.kill()
    walks(w, ala)
    check(distance(ala.state(), home, "xz") < 20, "first where they died")
    time.sleep(1)
    ala.kill()
    ala.until(alive=False)
    walks(w, ala)
    time.sleep(4)
    s = ala.state()
    check(abs(s["x"] - s["spawnX"]) < 20 and abs(s["z"] - s["spawnZ"]) < 20, f"then at the spawn point: {s['x'] - s['spawnX']:.0f}, {s['z'] - s['spawnZ']:.0f}")
    w.console("/rebirth revive Sqd")
    consistent(w, ala, 15)
    ala.quit()


# ---------- 2. rituals anywhere only for a valid choice ----------

def old_choice_does_not_turn_gears_into_rituals(w):
    settings(w, "Rescue")
    x, y, ola = w.join("Oca", "Ocb", "OcC")
    ghost(w, x)
    ola.ask("/rebirth target Oca", "will bring back Oca")
    w.console("/rebirth revive Oca")
    consistent(w, x)
    before = ola.state()["lives"]
    check(ola.gear() and ola.state()["lives"] == before + 1, "nobody chosen is waiting: a gear gives a life")
    ola.ask("/rebirth target Oca", "not out of lives")
    ghost(w, x)
    check(ola.gear() and ola.state()["lives"] == before + 2, "a choice from before does not count for a new death")
    ola.ask("/rebirth target Oca", "will bring back Oca")
    w.console("/rebirth revive Oca")
    consistent(w, x)
    ghost(w, y)
    check(ola.gear() and ola.state()["lives"] == before + 3, "still a life with another ghost waiting")
    check(y.state()["mode"] == "Spectator", "who was not chosen stays")
    w.console("/rebirth revive Ocb")
    consistent(w, y)
    x.quit(), y.quit(), ola.quit()


# ---------- 3. rituals lift bans of players who left at once ----------

def ritual_lifts_the_ban_of_someone_who_left_at_once(w):
    settings(w, "Timeout")
    ala, ola = w.join("Rlb", "RlbB")
    last_life(w, ala)
    ala.kill()
    ala.until(alive=False, lives=0)
    ala.vanish()
    time.sleep(3)
    check(phase(w, ala) == "Dying", "left before being banned")
    refused(w, ala)
    w.trt("clearrifts")
    w.trt("rift RlbB 4")
    check(ola.gear(), "the ritual works")
    ala.join()
    check(consistent(w, ala, 15)["mode"] == "Survival", "back")
    w.trt("clearrifts")
    ala.quit(), ola.quit()


# ---------- 4. ghosts reach a marked place ----------

@slow(40)
def ghost_reaches_a_marked_place_despite_a_relog(w):
    failures = []
    for i, (form, radius, wait) in enumerate((("Ghost", 0, 0.1), ("Spectator", 0, 0.4), ("Ghost", 10, 0.1))):
        settings(w, OutOfLives=form, **{"Place.At": "Fixed", "Place.Radius": radius})
        ala, = w.join(f"Grm{i}")
        there = w.trt(f"pointfar Grm{i} start {4000 + 2500 * i}")
        last_life(w, ala)
        since = w.bots.mark()
        ala.kill()
        wait_phase(w, ala, "Serving")
        time.sleep(wait)
        ala.relog()
        end = time.monotonic() + 15
        while distance(ala.state(), there, "xz") > 12 and time.monotonic() < end:
            time.sleep(0.5)
        time.sleep(1.5)
        if distance(ala.state(), there, "xz") > 12:
            failures.append(f"{form} {radius}: {distance(ala.state(), there, 'xz'):.0f} blocks away")
        if any("cannot go any farther" in line for line in w.bots.lines[since:]):
            failures.append(f"{form} {radius}: told they went too far")
        w.console(f"/rebirth revive Grm{i}")
        consistent(w, ala, 20)
        ala.quit()
    w.console("/rebirth point clear start")
    check(not failures, " | ".join(failures))


# ---------- 5. zombies without items ----------

def zombie_without_items_fights_bare_handed(w):
    settings(w, "Undead")
    ala, ola = w.join("Zwf", "ZwfB")
    check(w.trt("interact Zwf ZwfB game:blade-falx-copper 0")["allowed"], "before: a weapon")
    last_life(w, ala)
    ala.kill()
    walks(w, ala)
    ola.go_near(ala)
    got = {f"{item} {button}": w.trt(f"interact Zwf ZwfB {item} {button}")["allowed"] for item in ("none", "game:blade-falx-copper") for button in (0, 2)}
    check(got == {"none 0": True, "none 2": False, "game:blade-falx-copper 0": False, "game:blade-falx-copper 2": False}, str(got))
    check(w.trt("interact ZwfB Zwf game:blade-falx-copper 2")["allowed"], "others are not limited")
    w.console("/rebirth revive Zwf")
    consistent(w, ala)
    settings(w, "Undead", **{"Zombie.CanUseItems": "true"})
    last_life(w, ala)
    ala.kill()
    walks(w, ala)
    check(w.trt("interact Zwf ZwfB game:blade-falx-copper 0")["allowed"], "a zombie with items")
    ala.quit(), ola.quit()


# ---------- 6. ghosts from 0.6.x ----------

@slow(40)
def ghosts_of_0_6_keep_what_they_had(w):
    settings(w)
    ala, = w.join("Gko")
    ghost(w, ala)
    ala.quit()
    w.trt("oldentry Gko")
    w.restart()
    settings(w, "Hardcore")
    ala.join()
    check(consistent(w, ala)["mode"] == "Spectator", "still a ghost, not banned")
    settings(w)
    w.console("/rebirth revive Gko")
    consistent(w, ala)
    ala.quit()


# ---------- 7. a lifted ban and the spawn point ----------

@slow(40)
def ritual_to_the_spawn_point_works_after_a_restart(w):
    settings(w, "Timeout", **{"Ritual.ReviveAt": "Spawn"})
    ala, ola = w.join("Rts", "RtsB")
    ala.go(300, 0, 0)
    last_life(w, ala)
    ala.kill()
    time.sleep(3)
    ola.quit()
    w.restart()
    ola, = w.join("RtsB")
    w.trt("clearrifts")
    w.trt("rift RtsB 4")
    check(ola.gear(), "the ritual works on a banned player nobody has seen since the start")
    ala.join()
    s = consistent(w, ala, 15)
    check(distance({"x": s["spawnX"], "z": s["spawnZ"]}, s, "xz") < 60, f"at the spawn point: {s['x'] - s['spawnX']:.0f}")
    w.trt("clearrifts")
    ala.quit(), ola.quit()


# ---------- 8, 9. old configs ----------

def ritual_turned_off_in_0_6_stays_off(w):
    saved = read_config(w)
    check(saved["Ritual"]["Where"] == "Disabled", f"rituals: {saved['Ritual']['Where']}")
    check(saved["Time"]["Duration"] == "0.01h", f"time: {saved['Time']['Duration']}")
    check(saved["Place"]["Radius"] == 0, "no distance limit")
    check(not w.log_has("is not accepted"), "nothing left to complain about")


def lone_lives_per_gear_is_an_old_config(w):
    saved = read_config(w)
    check(saved["Lives"]["PerGear"] == 5 and saved["Ghost"]["Visible"] == "Nobody", "migrated")
    check(not w.log_has("unknown setting"), "nothing left to complain about")


# ---------- 10. told once ----------

def told_once_when_they_return(w):
    settings(w)
    ala, = w.join("Tow")
    last_life(w, ala)
    ala.kill()
    ala.until(alive=False, lives=0)
    since = w.bots.mark()
    ala.relog()
    time.sleep(4)
    heard = [line for line in w.bots.lines[since:] if line.startswith("[Tow] chat: You will return")]
    check(len(heard) == 1, f"told {len(heard)} times")
    w.console("/rebirth revive Tow")
    consistent(w, ala)
    ala.quit()


# ---------- 11. import says what it changes ----------

def import_lists_what_changed(w):
    settings(w, **{"Lives.Max": 5, "Messages.Ghost": "Witaj"})
    code = node("const s=T.applyPreset('Classic');s['Place.Radius']=40;process.stdout.write(T.encode('Classic',s))")
    admin, = w.join("Ilc")
    w.console("/player Ilc role admin")
    time.sleep(0.5)
    since = admin.say("/rebirth import " + code)
    line = admin.heard("Settings imported.*", since)
    for wanted in ("Place.Radius: 24 → 40", "Lives.Max: 5 → no limit", "Messages.Ghost: custom → the mod's text", "temporalrebirth.json.bak"):
        check(wanted in line, f"missing {wanted!r} in {line}")
    check("Return.At" not in line and "Messages.Ban" not in line, f"only what changed: {line}")
    bak = (w.config_path.parent / (w.config_path.name + ".bak")).read_text(encoding="utf8")
    check('"Witaj"' in bak, "the previous file is kept")
    admin.ask("/rebirth import " + code, "Nothing changed")
    check((w.config_path.parent / (w.config_path.name + ".bak")).read_text(encoding="utf8") == bak, "and not replaced by an import without changes")
    settings(w)
    admin.quit()


GROUPS = [
    {"name": "review", "tests": [zombie_does_not_rise_in_lava, second_quick_death_moves_to_the_spawn_point, old_choice_does_not_turn_gears_into_rituals,
                                  ritual_lifts_the_ban_of_someone_who_left_at_once, ghost_reaches_a_marked_place_despite_a_relog,
                                  zombie_without_items_fights_bare_handed, ghosts_of_0_6_keep_what_they_had, ritual_to_the_spawn_point_works_after_a_restart,
                                  told_once_when_they_return, import_lists_what_changed]},
    {"name": "oldritual", "world": {"config": {"ReturnMode": "Hours", "ReturnAfterHours": 0, "RitualRiftRange": 0, "GhostTetherRadius": -1}}, "tests": [ritual_turned_off_in_0_6_stays_off]},
    {"name": "oldgear", "world": {"config": {"LivesPerGear": 5}}, "tests": [lone_lives_per_gear_is_an_old_config]},
]
