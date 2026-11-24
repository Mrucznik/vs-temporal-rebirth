"""Lives: what a death costs, what a gear gives, the limit, unlimited worlds, admin commands."""
import time
from harness import check
from steps import consistent, ghost, read_config, settings, slow


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


def lives_limit(w):
    settings(w, **{"Lives.Max": 4, "Lives.PerGear": 3})
    ala, = w.join("Lil")
    w.trt("clearrifts")
    check(ala.gear(), "used")
    ala.until(lives=4)
    since = w.bots.mark()
    check(ala.gear(), "the gear still sets the spawn point")
    ala.heard("gears will not give you more", since)
    check(ala.state()["lives"] == 4, "not above the limit")
    ala.quit()


def old_choice_does_not_turn_gears_into_rituals(w):
    settings(w, **{"Time.Duration": "forever", "Ritual.Where": "Anywhere"})
    x, y, ola = w.join("Oca", "Ocb", "OcC")
    ghost(w, x)
    ola.ask("/rebirth target Oca", "will bring back Oca")
    w.console("/rebirth revive Oca")
    consistent(w, x)
    before = ola.state()["lives"]
    check(ola.gear() and ola.state()["lives"] == before + 1, "nobody chosen is waiting: a gear gives a life")
    ola.ask("/rebirth target Oca", "not out of lives")
    ghost(w, x)
    # Since 0.10 a gear anywhere opens the chooser while someone waits; "bind here" is the gear as usual.
    used = ola.gear_keep()
    check(not used["consumed"] and used["offered"] == ["Oca"], f"a choice from before does not count for a new death: {used}")
    check(ola.choose("bind") and ola.state()["lives"] == before + 2, "bound here: a life")
    check(x.state()["mode"] == "Spectator", "nobody brought back")
    ola.ask("/rebirth target Oca", "will bring back Oca")
    w.console("/rebirth revive Oca")
    consistent(w, x)
    ghost(w, y)
    used = ola.gear_keep()
    check(not used["consumed"] and used["offered"] == ["Ocb"], f"the old choice is not offered: {used}")
    check(ola.choose("bind") and ola.state()["lives"] == before + 3, "still a life with another ghost waiting")
    check(y.state()["mode"] == "Spectator", "who was not chosen stays")
    w.console("/rebirth revive Ocb")
    consistent(w, y)
    x.quit(), y.quit(), ola.quit()


def gears_give_configured_lives(w):
    ala, = w.join("Ggc")
    w.trt("clearrifts")
    check(ala.gear(), "the gear is used up")
    ala.until(lives=5)
    ala.quit()


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


def lone_lives_per_gear_is_an_old_config(w):
    saved = read_config(w)
    check(saved["Lives"]["PerGear"] == 5 and saved["Ghost"]["Visible"] == "Nobody", "migrated")
    check(not w.log_has("unknown setting"), "nothing left to complain about")


GROUPS = [
    {"name": "lives", "tests": [
        death_costs_a_life,
        gear_away_from_rifts_gives_a_life,
        admin_commands,
        ghost_and_lives_survive_server_restart,
        lives_limit,
        old_choice_does_not_turn_gears_into_rituals,
    ]},
    {"name": "oldgearlives", "world": {'config': {'RitualReviveAt': 'DeathSpot', 'RitualCooldownHours': 5, 'LivesPerGear': 3}}, "tests": [
        gears_give_configured_lives,
    ]},
    {"name": "unlimited", "ordered": True, "tests": [
        unlimited_world_releases_ghosts,
        relimited_world_starts_lives_over,
    ]},
    {"name": "oldgear", "world": {'config': {'LivesPerGear': 5}}, "tests": [
        lone_lives_per_gear_is_an_old_config,
    ]},
]
