"""What 0.9.0 adds: protection after rising, and the leash as clients are told about it. Clients stop players themselves; bots do not."""
import time
from harness import check, distance
from races import consistent
from v07 import settings
from v08 import serve, zone


def nothing_hurts_right_after_rising(w):
    settings(w, "Undead")
    ala, = w.join("Nhr")
    serve(w, ala)
    check(not w.trt("hurt Nhr 500")["received"], "protected")
    check(ala.state()["alive"], "still standing")
    time.sleep(5)
    check(w.trt("hurt Nhr 1")["received"], "only for a few seconds")
    # The second rise protects again.
    ala.kill()
    ala.until(alive=False)
    ala.until(alive=True, mode="Survival", timeout=15)
    time.sleep(0.5)
    check(not w.trt("hurt Nhr 500")["received"], "protected again")
    w.console("/rebirth revive Nhr")
    consistent(w, ala, 15)
    check(w.trt("hurt Nhr 1")["received"], "not after the return")
    ala.quit()


def clients_know_the_leash(w):
    ala, = w.join("Ckl")
    for radius in (24, 0):
        settings(w, **{"Place.Radius": radius})
        serve(w, ala, "Ghost")
        s = ala.state()
        if radius:
            x, y, z, r = map(float, s["leash"].split(" "))
            check(r == 24 and distance(s, {"x": x, "y": y, "z": z}) < 3, f"around where they died: {s['leash']}")
        else:
            check(s["leash"] is None, "no limit, no leash")
        ala.relog()
        time.sleep(1.5)
        check(ala.state()["leash"] == s["leash"], "the same after a relog")
        w.console("/rebirth revive Ckl")
        check(consistent(w, ala)["leash"] is None, "gone after the return")
    ala.quit()


def clients_know_they_are_in_a_zone(w):
    admin, ala = w.join("CkzA", "Ckz")
    zone(w, admin, 200)
    settings(w, **{"Place.At": "Zone"})
    serve(w, ala, "Ghost")
    s = ala.state()
    check(s["leash"] == "zone" and s["zoneAccess"] is None, f"a ghost is held, but does not use anything: {s['leash']} {s['zoneAccess']}")
    ala.quit(), admin.quit()


def server_only_pulls_back_from_far_outside(w):
    settings(w, **{"Place.Radius": 24})
    ala, = w.join("Spf")
    serve(w, ala, "Ghost")
    home = ala.state()
    ala.go(28, 0, 0)
    time.sleep(2.5)
    check(distance(ala.state(), home) > 26, "a little outside is up to the client")
    ala.go(20, 0, 0)
    time.sleep(2.5)
    check(distance(ala.state(), home) < 25, "far outside is not")
    ala.quit()


GROUPS = [{"name": "leash", "tests": [nothing_hurts_right_after_rising, clients_know_the_leash, clients_know_they_are_in_a_zone, server_only_pulls_back_from_far_outside]}]
