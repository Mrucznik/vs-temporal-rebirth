"""What rifts take and give back: revivals for health, a gear offered to a rift, a ghost coming back alone (Don't Starve)."""
import time
from harness import check
from steps import consistent, ghost, leave, settings, slow

FULL = 15.0


def max_health(bot):
    return bot.state()["maxHealth"]


@slow(15)
def ghost_comes_back_alone_at_a_rift(w):
    settings(w, "DontStarve")
    ala, = w.join("Gca")
    ghost(w, ala)
    w.trt("clearrifts")
    w.trt("riftnear Gca 0 1 0 3")
    since = w.bots.mark()
    s = ala.until(mode="Survival", alive=True, timeout=20)
    check(s["lives"] >= 1, f"alive with lives: {s}")
    ala.heard("tear yourself out of the rift", since)
    check(max_health(ala) == FULL - 2, f"the rift kept 2 health: {max_health(ala)}")
    w.trt("clearrifts")
    ala.quit()


def a_gear_offered_to_a_rift_gives_health_back(w):
    settings(w, "DontStarve")
    ala, = w.join("Ogb")
    ghost(w, ala)
    w.trt("clearrifts")
    w.trt("riftnear Ogb 0 1 0 3")
    ala.until(mode="Survival", alive=True, timeout=20)
    consistent(w, ala)
    check(max_health(ala) == FULL - 2, f"lost 2 first: {max_health(ala)}")
    hint = w.trt("hint Ogb")
    check(hint["state"] == "Offer" and hint["health"] == 2, f"the gear offers itself to the rift: {hint}")
    check(ala.gear(), "the gear is taken")
    check(max_health(ala) == FULL, f"health back: {max_health(ala)}")
    check(w.trt("hint Ogb")["state"] != "Offer", "nothing more to buy back")
    w.trt("clearrifts")
    ala.quit()


def revival_without_a_gear_costs_health(w):
    settings(w, "DontStarve", **{"Ritual.SelfAtRift": "false"})
    ala, ola = w.join("Rwh", "RwhB")
    ghost(w, ala)
    w.trt("clearrifts")
    w.trt("riftnear RwhB 3 1 0 3")
    ala.go_near(ola)
    hint = w.trt("hint RwhB")
    check(hint["byHand"] and hint["health"] == 2, f"the bare hand can bring the ghost back: {hint}")
    w.trt("handhold RwhB 0")
    time.sleep(3.6)
    w.trt("handhold RwhB 2")
    s = ala.until(mode="Survival", alive=True, timeout=20)
    check(max_health(ala) == FULL - 2, f"the revived paid 2 health: {max_health(ala)}")
    check(max_health(ola) == FULL, "the caster paid nothing")
    w.trt("clearrifts")
    ala.quit(), ola.quit()


def letting_go_too_soon_brings_nobody(w):
    settings(w, "DontStarve", **{"Ritual.SelfAtRift": "false"})
    ala, ola = w.join("Lgs", "LgsB")
    ghost(w, ala)
    w.trt("clearrifts")
    w.trt("riftnear LgsB 3 1 0 3")
    ala.go_near(ola)
    w.trt("handhold LgsB 0")
    time.sleep(1)
    w.trt("handhold LgsB 2")
    time.sleep(2)
    check(ala.state()["mode"] == "Spectator", "still a ghost")
    leave(w, [ala], [ola])
    w.trt("clearrifts")


def too_weak_to_cross_alone(w):
    settings(w, "DontStarve", **{"Ritual.HealthMin": 14})
    ala, ola = w.join("Twc", "TwcB")
    ghost(w, ala)
    w.trt("clearrifts")
    since = w.bots.mark()
    w.trt("riftnear Twc 0 1 0 3")
    ala.heard("too weak to cross the rift alone", since, timeout=10)
    check(ala.state()["mode"] == "Spectator", "still a ghost")
    ola.go_near(ala)
    check(not w.trt("hint TwcB")["byHand"], "no revival for health either")
    check(ola.gear(), "a gear still works")
    ala.until(mode="Survival", alive=True, timeout=20)
    check(max_health(ala) == FULL, f"a gear costs no health: {max_health(ala)}")
    w.trt("clearrifts")
    ala.quit(), ola.quit()


def coming_back_alone_works_in_other_presets(w):
    settings(w, "Ghost", **{"Ritual.SelfAtRift": "true"})
    ala, = w.join("Cba")
    ghost(w, ala)
    w.trt("clearrifts")
    w.trt("riftnear Cba 0 1 0 3")
    ala.until(mode="Survival", alive=True, timeout=20)
    check(max_health(ala) == FULL, f"without a health cost it is free: {max_health(ala)}")
    w.trt("clearrifts")
    ala.quit()


GROUPS = [
    {"name": "riftbargain", "tests": [
        ghost_comes_back_alone_at_a_rift,
        a_gear_offered_to_a_rift_gives_health_back,
        revival_without_a_gear_costs_health,
        letting_go_too_soon_brings_nobody,
        too_weak_to_cross_alone,
        coming_back_alone_works_in_other_presets,
    ]},
]
