"""What mod 0.6.x does today. These must keep passing after the 0.7.0 rewrite for a migrated 0.6.x config."""
import json, re, time
from harness import Failed, check, distance


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


# ---------- default config ----------

def death_costs_a_life(w):
    ala, = w.join("Dca")
    check(ala.state()["lives"] == 2, "starts with the world's lives")
    ala.kill()
    ala.until(alive=False, lives=1)
    time.sleep(3)
    ala.respawn()
    check(ala.until(alive=True)["mode"] == "Survival", "still in survival")
    ala.ask("/rebirth lives", "Lives left: 1")
    ala.quit()


def last_death_makes_a_ghost(w):
    ala, ola = w.join("Ldg", "LdgB")
    since = w.bots.mark()
    ala.lose_all_lives()
    s = ala.until(mode="Spectator", alive=True)
    check(s["noclip"] is False, "a ghost collides with blocks by default")
    ala.heard("Your last life is spent", since)
    ola.heard("Ldg has lost their last life", since)
    ola.ask("/rebirth ghosts", "Ldg: died .* returns .* 06:00")
    ala.quit(), ola.quit()


def ghost_is_tethered(w):
    ala, = w.join("Git")
    ala.lose_all_lives()
    home = ala.until(mode="Spectator", alive=True)
    w.trt("move Git 60 10 0")
    time.sleep(3)
    check(distance(ala.state(), home) < 26, "pulled back within 24 blocks")
    ala.go(400, 20, 0)
    end = time.monotonic() + 3
    while distance(ala.state(), home) > 26:
        check(time.monotonic() < end, "pulled back from far away")
        time.sleep(0.2)
    ala.quit()


def ghost_survives_relog(w):
    ala, = w.join("Gsr")
    ala.lose_all_lives()
    ala.until(mode="Spectator", alive=True)
    ala.quit()
    ala.join()
    ala.until(mode="Spectator", alive=True, lives=0)
    ala.quit()


def leaving_before_becoming_a_ghost(w):
    ala, = w.join("Lbg")
    w.console("/rebirth setlives Lbg 1")
    ala.kill()
    ala.quit()
    time.sleep(3)
    ala.join()
    ala.until(mode="Spectator", alive=True)
    ala.quit()


def quick_relog_after_last_death(w):
    for name in ("Qra", "Qrb", "Qrc"):
        ala, = w.join(name)
        w.console(f"/rebirth setlives {name} 1")
        time.sleep(1)
        ala.kill()
        time.sleep(0.6)
        w.bots.send(name + " quit")
        time.sleep(0.45)
        ala.join()
        ala.until(mode="Spectator", alive=True, timeout=5)
        ala.quit()


def returns_the_next_day(w):
    ala, ola = w.join("Rnd", "RndB")
    ala.lose_all_lives()
    ala.until(mode="Spectator", alive=True)
    w.trt("checkreturns")
    time.sleep(2)
    check(ala.state()["mode"] == "Spectator", "too early to return")
    since = w.bots.mark()
    w.trt("age Rnd 48")
    w.trt("checkreturns")
    s = ala.until(mode="Survival", alive=True, lives=1)
    check(distance({"x": s["spawnX"], "z": s["spawnZ"]}, s, "xz") < 60, "back near the spawn point")
    ala.heard("You have returned", since)
    ola.heard("Rnd has returned", since)
    ala.quit(), ola.quit()


def returns_on_login_when_time_passed(w):
    ala, = w.join("Rol")
    ala.lose_all_lives()
    ala.until(mode="Spectator", alive=True)
    ala.quit()
    w.trt("age Rol 48")
    ala.join()
    ala.until(mode="Survival", alive=True, lives=1)
    ala.quit()


def ritual_brings_a_ghost_back(w):
    ala, ola = w.join("Rgb", "RgbB")
    ala.lose_all_lives()
    ala.until(mode="Spectator", alive=True)
    w.trt("clearrifts")
    ola.go(30, 0, 0)
    w.trt("rift RgbB 4")
    since = w.bots.mark()
    check(ola.gear(), "the gear is used up")
    s = ala.until(mode="Survival", alive=True, lives=1)
    check(distance(s, ola.state()) < 4, "revived next to the caster")
    ala.heard("RgbB offered a temporal gear", since)
    ola.heard("The gear shatters", since)
    w.trt("clearrifts")
    ala.quit(), ola.quit()


def ritual_without_ghosts_keeps_the_gear(w):
    ola, = w.join("Rwg")
    w.trt("clearrifts")
    w.trt("rift Rwg 4")
    before, since = ola.state(), w.bots.mark()
    check(not ola.gear(), "the gear stays")
    ola.heard("Nobody is waiting", since)
    check(ola.state()["lives"] == before["lives"], "no life from a ritual")
    w.trt("clearrifts")
    ola.quit()


def ritual_needs_a_choice_between_ghosts(w):
    a, b, ola = w.join("Rca", "Rcb", "RcC")
    for ghost in (a, b):
        ghost.lose_all_lives()
        ghost.until(mode="Spectator", alive=True)
    w.trt("clearrifts")
    w.trt("rift RcC 4")
    since = w.bots.mark()
    check(not ola.gear(), "the gear stays until someone is chosen")
    ola.heard("More than one player", since)
    ola.ask("/rebirth target Rcb", "will bring back Rcb")
    check(ola.gear(), "the gear is used up")
    b.until(mode="Survival", alive=True)
    check(a.state()["mode"] == "Spectator", "the other ghost stays")
    w.trt("clearrifts")
    a.quit(), b.quit(), ola.quit()


def offline_ghost_cannot_be_revived(w):
    ala, ola = w.join("Ogr", "OgrB")
    ala.lose_all_lives()
    ala.until(mode="Spectator", alive=True)
    ala.quit()
    w.trt("clearrifts")
    w.trt("rift OgrB 4")
    check(not ola.gear(), "nobody online to revive")
    w.trt("clearrifts")
    ola.quit()


def gear_away_from_rifts_gives_a_life(w):
    ala, = w.join("Gar")
    w.trt("clearrifts")
    ala.go(40, 0, 0)
    since = w.bots.mark()
    check(ala.gear(), "the gear is used up")
    s = ala.until(lives=3)
    check(abs(s["spawnX"] - s["x"]) < 3, "the spawn point moved here")
    ala.heard("The gear binds you", since)
    w.trt("rift Gar 20")
    check(ala.gear(), "a rift 20 blocks away is too far for a ritual")
    ala.until(lives=4)
    w.trt("clearrifts")
    ala.quit()


def admin_commands(w):
    ala, ola = w.join("Adm", "AdmB")
    ola.ask("/rebirth setlives Adm 5", "(privilege|permission)")
    w.console("/rebirth setlives Adm 5")
    ala.until(lives=5)
    ala.kill()
    ala.until(lives=4)
    time.sleep(3)
    ala.respawn()
    ala.until(alive=True)
    w.console("/rebirth setlives Adm 0")
    check(ala.until(lives=0)["mode"] == "Survival", "zero lives while alive is allowed")
    ala.kill()
    ala.until(mode="Spectator", alive=True)
    w.console("/rebirth revive Adm")
    ala.until(mode="Survival", alive=True, lives=1)
    ala.kill()
    ala.until(mode="Spectator", alive=True)
    w.console("/rebirth setlives Adm 3")
    ala.until(mode="Survival", alive=True, lives=3)
    ala.quit(), ola.quit()


@slow(30)
def ghost_and_lives_survive_server_restart(w):
    ala, ola = w.join("Gls", "GlsB")
    w.console("/rebirth setlives GlsB 7")
    ola.until(lives=7)
    ala.lose_all_lives()
    ala.until(mode="Spectator", alive=True)
    ala.quit(), ola.quit()
    w.restart()
    ala, ola = w.join("Gls", "GlsB")
    ala.until(mode="Spectator", alive=True, lives=0)
    ola.until(lives=7)
    ala.quit(), ola.quit()


# ---------- other configs ----------

def returns_after_hours_weakened(w):
    ala, = w.join("Rah")
    ala.lose_all_lives()
    ala.until(mode="Spectator", alive=True)
    w.trt("age Rah 1")
    w.trt("checkreturns")
    time.sleep(1)
    check(ala.state()["mode"] == "Spectator", "two hours have not passed")
    w.trt("age Rah 2")
    w.trt("checkreturns")
    s = ala.until(mode="Survival", alive=True)
    time.sleep(1)
    s = ala.state()
    check(s["health"] <= s["maxHealth"] * 0.45, f"health capped at 40%: {s['health']}/{s['maxHealth']}")
    check(s["satiety"] <= s["maxSatiety"] * 0.25, f"satiety capped at 20%: {s['satiety']}/{s['maxSatiety']}")
    ala.quit()


def noclip_and_free_roaming(w):
    ala, = w.join("Nfr")
    ala.lose_all_lives()
    home = ala.until(mode="Spectator", alive=True, noclip=True)
    w.trt("move Nfr 100 20 0")
    time.sleep(3)
    check(distance(ala.state(), home) > 80, "no tether")
    ala.quit()


def ritual_at_death_spot_with_cooldown(w):
    ala, ola = w.join("Rds", "RdsB")
    ala.lose_all_lives()
    home = ala.until(mode="Spectator", alive=True)
    ola.go(60, 0, 0)
    w.trt("clearrifts")
    w.trt("rift RdsB 4")
    check(ola.gear(), "first ritual works")
    s = ala.until(mode="Survival", alive=True)
    check(distance(s, home) < 6, "revived where they died")
    ala.kill()
    ala.until(mode="Spectator", alive=True)
    since = w.bots.mark()
    check(not ola.gear(), "cooldown keeps the gear")
    ola.heard("cannot be called back yet", since)
    w.trt("clearrifts")
    ala.quit(), ola.quit()


def gears_give_configured_lives(w):
    ala, = w.join("Ggc")
    w.trt("clearrifts")
    check(ala.gear(), "the gear is used up")
    ala.until(lives=5)
    ala.quit()


def ritual_to_spawn_and_no_natural_return(w):
    ala, ola = w.join("Rsn", "RsnB")
    ala.lose_all_lives()
    ala.until(mode="Spectator", alive=True)
    w.trt("age Rsn 2000")
    w.trt("checkreturns")
    time.sleep(1)
    check(ala.state()["mode"] == "Spectator", "never returns on their own")
    ola.go(200, 0, 0)
    w.trt("clearrifts")
    w.trt("rift RsnB 4")
    check(ola.gear(), "ritual works")
    s = ala.until(mode="Survival", alive=True)
    check(distance({"x": s["spawnX"], "z": s["spawnZ"]}, s, "xz") < 60, "revived at the spawn point")
    w.trt("clearrifts")
    ala.quit(), ola.quit()


@slow(30)
def unlimited_world_releases_ghosts(w):
    ala, = w.join("Uwr")
    ala.lose_all_lives()
    ala.until(mode="Spectator", alive=True)
    ala.quit()
    w.console("/worldconfig playerlives -1")
    time.sleep(1)
    w.restart()
    ala, = w.join("Uwr")
    ala.until(mode="Survival", alive=True)
    ala.ask("/rebirth lives", "unlimited lives")
    ala.quit()


@slow(25)
def relimited_world_starts_lives_over(w):
    ala, = w.join("Uwr")
    ala.quit()
    w.console("/worldconfig playerlives 3")
    time.sleep(1)
    w.restart()
    ala, = w.join("Uwr")
    ala.until(lives=3)
    ala.quit()


def broken_config_is_not_overwritten(w):
    check(w.log_has("temporalrebirth.json is invalid"), "the error is logged")
    check('"GhostTetherRadius": 99' in w.config_path.read_text(encoding="utf8"), "the admin's settings are still in the file")


def read_config(w):
    """The file without its comments."""
    return json.loads(re.sub(r"^\s*//.*$", "", w.config_path.read_text(encoding="utf8"), flags=re.M))


def old_config_is_migrated(w):
    saved = read_config(w)
    check(saved["Lives"]["PerGear"] == 4, "the admin's value is kept")
    check(saved["Time"]["Duration"] == "next day 06:00" and saved["Ritual"]["Range"] == 8, "the rest is filled in")
    check(saved["Ghost"]["Visible"] == "Nobody", "an update does not change what players see")
    check(json.loads((w.config_path.parent / (w.config_path.name + ".bak")).read_text(encoding="utf8")) == {"LivesPerGear": 4, "ReturnMode": "NextDay"}, "the old file is kept as .bak")


GROUPS = [
    {"name": "default", "tests": [
        death_costs_a_life, last_death_makes_a_ghost, ghost_is_tethered, ghost_survives_relog, leaving_before_becoming_a_ghost, quick_relog_after_last_death,
        returns_the_next_day, returns_on_login_when_time_passed, ritual_brings_a_ghost_back, ritual_without_ghosts_keeps_the_gear,
        ritual_needs_a_choice_between_ghosts, offline_ghost_cannot_be_revived, gear_away_from_rifts_gives_a_life, admin_commands,
        ghost_and_lives_survive_server_restart]},
    {"name": "hours", "world": {"config": {"ReturnMode": "Hours", "ReturnAfterHours": 2, "GhostNoClip": True, "GhostTetherRadius": 0, "ReturnHealthPercent": 40, "ReturnSatietyPercent": 20}},
     "tests": [returns_after_hours_weakened, noclip_and_free_roaming]},
    {"name": "deathspot", "world": {"config": {"RitualReviveAt": "DeathSpot", "RitualCooldownHours": 5, "LivesPerGear": 3}},
     "tests": [ritual_at_death_spot_with_cooldown, gears_give_configured_lives]},
    {"name": "ritualonly", "world": {"config": {"ReturnMode": "Disabled", "RitualReviveAt": "Spawn"}}, "tests": [ritual_to_spawn_and_no_natural_return]},
    {"name": "unlimited", "ordered": True, "tests": [unlimited_world_releases_ghosts, relimited_world_starts_lives_over]},
    {"name": "brokenconfig", "world": {"raw_config": '{ "GhostTetherRadius": 99, oops'}, "tests": [broken_config_is_not_overwritten]},
    {"name": "oldconfig", "world": {"config": {"LivesPerGear": 4, "ReturnMode": "NextDay"}}, "tests": [old_config_is_migrated]},
]
