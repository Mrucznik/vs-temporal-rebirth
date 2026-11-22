"""Together with the Downed mod: being downed is not a death. Needs downed_<version>.zip in the test cache."""
import time
from harness import check


def downed_is_not_a_death(w):
    ala, = w.join("Dnd")
    before = ala.state()
    w.trt("hurt Dnd 500")
    s = ala.until(downed=True, alive=True)
    check(s["deaths"] == before["deaths"] and s["lives"] == before["lives"], "no death, no life lost")
    ala.quit()


def bleeding_out_costs_a_life(w):
    ala, = w.join("Boc")
    w.trt("hurt Boc 500")
    ala.until(downed=True)
    ala.until(alive=False, lives=1, timeout=15)
    time.sleep(3)
    ala.respawn()
    ala.until(alive=True, downed=False, mode="Survival")
    ala.quit()


def bleeding_out_on_the_last_life_makes_a_ghost(w):
    ala, = w.join("Bol")
    w.console("/rebirth setlives Bol 1")
    w.trt("hurt Bol 500")
    ala.until(downed=True)
    ala.until(mode="Spectator", alive=True, downed=False, timeout=20)
    w.console("/rebirth revive Bol")
    ala.until(mode="Survival", alive=True, downed=False)
    ala.quit()


def downed_player_cannot_perform_a_ritual(w):
    ala, ola = w.join("Dpr", "DprB")
    ala.lose_all_lives()
    ala.until(mode="Spectator", alive=True)
    w.trt("clearrifts")
    w.trt("rift DprB 4")
    w.trt("hurt DprB 500")
    ola.until(downed=True)
    check(not ola.gear(), "the gear stays")
    check(ala.state()["mode"] == "Spectator", "nobody is revived")
    w.trt("clearrifts")
    ala.quit(), ola.quit()


GROUPS = [{"name": "downed", "world": {"mods": ["downed"], "files": {"ModConfig/downed.json": {"DurationSeconds": 6}}},
           "tests": [downed_is_not_a_death, bleeding_out_costs_a_life, bleeding_out_on_the_last_life_makes_a_ghost, downed_player_cannot_perform_a_ritual]}]
