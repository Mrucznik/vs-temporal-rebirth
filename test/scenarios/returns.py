"""How long they stay out and how they come back: time, the timer, the return."""
import time
from harness import check, distance
from steps import consistent, exile, ghost, last_life, point, serve, settings, wait_phase


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


def return_place_lives_and_vitals(w):
    settings(w, **{"Return.At": "Fixed", "Return.Lives": 5, "Return.HealthPercent": 30, "Return.SatietyPercent": 100})
    admin, ala = w.join("RplA", "Rpl")
    admin.go(0, 0, 150)
    point(w, admin, "return")
    here = admin.state()
    ghost(w, ala)
    w.trt("age Rpl 48")
    w.trt("checkreturns")
    s = consistent(w, ala, 15)
    check(distance(s, here) < 6, "at the marked place")
    check(s["lives"] == 5, f"with the configured lives: {s['lives']}")
    check(abs(s["health"] / s["maxHealth"] - 0.3) < 0.1, f"health {s['health']}/{s['maxHealth']}")
    check(s["satiety"] / s["maxSatiety"] > 0.95, f"satiety can be full: {s['satiety']}/{s['maxSatiety']}")
    w.console("/rebirth point clear return")
    ala.quit(), admin.quit()


def relog_while_dying_at_the_end(w):
    ala, = w.join("Rwd")
    point(w, ala, "start")
    for wait in (0.2, 1.0, 1.7):
        exile(w, ala)
        ala.kill()
        ala.until(mode="Survival", alive=True, lives=0, timeout=15)
        wait_phase(w, ala, "Serving")
        time.sleep(2)
        w.trt("age Rwd 25")
        w.trt("checkreturns")
        time.sleep(wait)
        ala.relog()
        s = consistent(w, ala, 15)
        check(s["lives"] == 2, f"back with all lives after a relog at {wait}s: {s['lives']}")
    ala.quit()


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


def timer_counts_down(w):
    settings(w, **{"Time.Duration": "2h"})
    ala, = w.join("Tcd")
    serve(w, ala, "Ghost")
    s = ala.state()["seconds"]
    check(7150 < s <= 7200, f"about two hours: {s}")
    w.trt("age Tcd 1")
    ala.relog()
    s = ala.state()["seconds"]
    check(3500 < s <= 3600, f"right after a relog: {s}")
    time.sleep(6)
    check(ala.state()["seconds"] < s, "sent again every few seconds")
    w.console("/rebirth config Time.ShowTimer false")
    time.sleep(6)
    check(ala.state()["seconds"] > 0, "a running sentence keeps its timer")
    w.console("/rebirth revive Tcd")
    check(consistent(w, ala)["seconds"] == -1, "gone after the return")
    serve(w, ala, "Ghost")
    check(ala.state()["seconds"] == -1, "turned off")
    ala.quit()


def timer_after_a_restart(w):
    settings(w, OutOfLives="Zombie", **{"Time.Duration": "2h", "Messages.Timer": "Jeszcze {time}"})
    ala, = w.join("Tar")
    serve(w, ala)
    check(ala.state()["timerText"] == "Jeszcze {time}", "the admin's own text")
    ala.quit()
    w.restart()
    ala, = w.join("Tar")
    check(7000 < ala.state()["seconds"] <= 7200, "still counting")
    w.console('/rebirth config Messages.Timer ""')
    time.sleep(6)
    check(ala.state()["timerText"] is None, "back to the mod's text")
    ala.quit()


def no_timer_without_an_end(w):
    settings(w, **{"Time.Duration": "forever"})
    ala, = w.join("Tne")
    serve(w, ala, "Ghost")
    check(ala.state()["seconds"] == -1, "nothing to count")
    w.console("/rebirth revive Tne")
    consistent(w, ala)
    ala.quit()


def changed_settings_do_not_touch_running_sentences(w):
    settings(w)
    ala, = w.join("Csd")
    ghost(w, ala)
    settings(w, "Hardcore")
    ala.relog()
    check(consistent(w, ala)["mode"] == "Spectator", "still a ghost, not banned")
    w.trt("age Csd 30")
    w.trt("checkreturns")
    check(consistent(w, ala, 15)["mode"] == "Survival", "back by the old time")
    ala.quit()


def waiting_list_shows_everyone(w):
    settings(w, **{"Time.Duration": "forever"})
    ala, = w.join("Wls")
    ghost(w, ala)
    ala.ask("/rebirth ghosts", "Wls: died .* has no time limit")
    w.console("/rebirth revive Wls")
    consistent(w, ala)
    ala.quit()


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


GROUPS = [
    {"name": "returns", "tests": [
        returns_the_next_day,
        returns_on_login_when_time_passed,
        return_place_lives_and_vitals,
        relog_while_dying_at_the_end,
        told_once_when_they_return,
        timer_counts_down,
        timer_after_a_restart,
        no_timer_without_an_end,
        changed_settings_do_not_touch_running_sentences,
        waiting_list_shows_everyone,
    ]},
    {"name": "oldhours", "world": {'config': {'ReturnMode': 'Hours', 'ReturnAfterHours': 2, 'GhostNoClip': True, 'GhostTetherRadius': 0, 'ReturnHealthPercent': 40, 'ReturnSatietyPercent': 20}}, "tests": [
        returns_after_hours_weakened,
    ]},
]
