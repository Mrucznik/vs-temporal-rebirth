"""Where players out of lives stay: marked places, exile, zones of land claims, the leash."""
import time
from harness import check, distance
from steps import consistent, exile, inside, last_life, phase, point, serve, settings, slow, wait_inside, wait_phase, zone


def exile_to_the_marked_place(w):
    admin, ala = w.join("ExlA", "Exl")
    admin.go(300, 0, 0)
    point(w, admin, "start")
    here = admin.state()
    exile(w, ala, **{"Place.Radius": 20})
    since = w.bots.mark()
    ala.kill()
    s = ala.until(mode="Survival", alive=True, lives=0, timeout=15)
    wait_phase(w, ala, "Serving")
    check(distance(ala.state(), here) < 5, "at the marked place")
    ala.heard("this is where you stay", since)
    # Leaving is not possible.
    w.trt("move Exl 60 5 0")
    time.sleep(3)
    check(distance(ala.state(), here, "xz") < 22, "pulled back")
    # Dying does not set free and does not restart anything.
    ala.kill()
    ala.until(alive=False)
    ala.until(mode="Survival", alive=True, lives=0, timeout=15)
    check(distance(ala.state(), here, "xz") < 22, "comes back to the same place")
    check(phase(w, ala) == "Serving", "still exiled")
    # Gears are no way out.
    w.trt("clearrifts")
    check(not ala.gear(), "a gear does nothing")
    check(ala.state()["lives"] == 0, "no life from a gear")
    ala.quit(), admin.quit()


def exile_survives_relog_and_restart(w):
    admin, ala = w.join("EsrA", "Esr")
    admin.go(200, 0, 0)
    point(w, admin, "start")
    here = admin.state()
    exile(w, ala)
    ala.kill()
    for wait in (0.5, 1.2):
        time.sleep(wait)
        ala.relog()
    wait_phase(w, ala, "Serving")
    ala.until(mode="Survival", alive=True, timeout=15)
    time.sleep(1)
    check(distance(ala.state(), here) < 6, "relogging on the way still ends at the marked place")
    ala.quit(), admin.quit()
    w.restart()
    ala, = w.join("Esr")
    ala.until(mode="Survival", alive=True, lives=0)
    check(phase(w, ala) == "Serving" and distance(ala.state(), here) < 30, "still exiled after a restart")
    ala.quit()


def exile_ends_with_death_and_all_lives(w):
    ala, = w.join("Eed")
    point(w, ala, "start")
    exile(w, ala)
    ala.kill()
    ala.until(mode="Survival", alive=True, lives=0, timeout=15)
    wait_phase(w, ala, "Serving")
    time.sleep(2)
    deaths, since = ala.state()["deaths"], w.bots.mark()
    w.trt("age Eed 25")
    w.trt("checkreturns")
    s = consistent(w, ala, 15)
    check(s["deaths"] == deaths + 1, "died at the end")
    check(s["mode"] == "Survival" and s["lives"] == 2, f"all lives back, that death cost none: {s['lives']}")
    ala.heard("You have returned", since)
    ala.quit()


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


def missing_places_fall_back(w):
    w.console("/rebirth point clear start")
    settings(w, **{"Place.At": "Fixed", "Return.At": "Fixed"})
    ala, = w.join("Mpf")
    last_life(w, ala)
    home = ala.state()
    ala.kill()
    s = ala.until(mode="Spectator", alive=True)
    check(distance(s, home) < 5, "stays where they died")
    check(w.log_has("No place is marked for players out of lives", 2), "and the log says so")
    w.trt("age Mpf 48")
    w.trt("checkreturns")
    check(consistent(w, ala, 15)["mode"] == "Survival", "comes back to the spawn point")
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


def zone_holds_players(w):
    admin, ala = w.join("ZhpA", "Zhp")
    middle = zone(w, admin, 200)
    settings(w, OutOfLives="Player", **{"Place.At": "Zone", "Place.Radius": 3})
    serve(w, ala)
    check(distance(ala.state(), middle) < 5, "starts at the marked place")
    ala.go(7, 0, 0)
    time.sleep(2.5)
    inner = ala.state()
    check(inner["x"] - middle["x"] > 5, "walks freely in the zone, the distance limit does not apply")
    since = w.bots.mark()
    ala.go(12, 0, 0)
    time.sleep(2.5)
    check(distance(ala.state(), inner, "xz") < 1.5, "pulled back to the last step inside")
    ala.heard("cannot go any farther", since)
    admin.go(60, 0, 0)
    time.sleep(3)
    admin.go(-60, 0, 0)
    time.sleep(1)
    check(admin.state()["mode"] == "Survival" and inside(admin, middle), "others come and go")
    ala.quit(), admin.quit()


def zone_holds_ghosts(w):
    admin, ala = w.join("ZhgA", "Zhg")
    middle = zone(w, admin, 220)
    settings(w, **{"Place.At": "Zone"})
    serve(w, ala, "Ghost")
    w.trt("move Zhg 5 3 0")
    time.sleep(2.5)
    ala.go(300, 3, 0)
    wait_inside(ala, middle, "pulled back from far away", 3)
    ala.quit(), admin.quit()


def zone_survives_relog_and_restart(w):
    admin, ala = w.join("ZsrA", "Zsr")
    middle = zone(w, admin, 240)
    settings(w, OutOfLives="Zombie", **{"Place.At": "Zone"})
    serve(w, ala)
    ala.relog()
    time.sleep(1.5)
    check(phase(w, ala) == "Serving" and inside(ala, middle), "still there after a relog")
    w.trt("move Zsr 40 0 0")
    ala.relog()
    wait_inside(ala, middle, "leaving while outside must not help")
    ala.quit(), admin.quit()
    w.restart()
    ala, = w.join("Zsr")
    time.sleep(1.5)
    check(inside(ala, middle), "still there after a restart")
    check(ala.state()["zoneAccess"] == "All", "the client still knows what is allowed")
    ala.go(60, 0, 0)
    wait_inside(ala, middle, "held after a restart")
    ala.until(mode="Survival", alive=True)
    ala.quit()


def zone_death_returns_to_the_zone(w):
    admin, ala = w.join("ZdrA", "Zdr")
    middle = zone(w, admin, 260)
    settings(w, OutOfLives="Player", **{"Place.At": "Zone"})
    serve(w, ala)
    ala.kill()
    ala.until(alive=False)
    ala.until(alive=True, mode="Survival", lives=0, timeout=15)
    wait_inside(ala, middle, "dying is no way out")
    w.console("/rebirth revive Zdr")
    s = consistent(w, ala, 15)
    check(s["zoneAccess"] is None and s["form"] is None, "nothing is left on the player")
    ala.go(60, 0, 0)
    time.sleep(2.5)
    check(not inside(ala, middle), "free again")
    ala.quit(), admin.quit()


def zone_access(w):
    admin, ala, ola = w.join("ZacA", "Zac", "ZacB")
    for dx, access, everyone, use, build in ((200, "Traverse", "true", False, False), (30, "Use", "false", True, False), (30, "All", "false", True, True)):
        zone(w, admin, dx, everyone=everyone)
        settings(w, OutOfLives="Player", **{"Place.At": "Zone", "Place.Access": access})
        serve(w, ala)
        got = w.trt("access Zac 0")
        check((got["use"] == "Granted", got["build"] == "Granted") == (use, build), f"{access} inside: {got}")
        check(ala.state()["zoneAccess"] == access, "the client is told")
        ola.go_near(ala)
        other = w.trt("access ZacB 0")
        check(other["build"] == "LandClaimed" and (other["use"] == "Granted") == (everyone == "true"), f"others keep the claim's rules: {other}")
        w.console("/rebirth revive Zac")
        consistent(w, ala, 15)
    ala.go_near(admin)
    check(w.trt("access Zac 0")["build"] == "LandClaimed", "back to normal after the return")
    ala.quit(), ola.quit(), admin.quit()


def zone_access_keeps_zombie_limits(w):
    admin, ala = w.join("ZazA", "Zaz")
    zone(w, admin, 280)
    settings(w, OutOfLives="Zombie", **{"Place.At": "Zone", "Place.Access": "All"})
    serve(w, ala)
    got = w.trt("access Zaz 0")
    check(got["use"] != "Granted" and got["build"] != "Granted", f"a zombie without items: {got}")
    w.console("/rebirth revive Zaz")
    consistent(w, ala, 15)
    settings(w, OutOfLives="Zombie", **{"Place.At": "Zone", "Place.Access": "All", "Zombie.CanBuild": "true", "Zombie.CanUseBlocks": "true"})
    serve(w, ala)
    got = w.trt("access Zaz 0")
    check(got == {"use": "Granted", "build": "Granted"}, f"a zombie with items: {got}")
    ala.quit(), admin.quit()


def no_claims_hold_nobody(w):
    w.trt("unclaim")
    admin, ala = w.join("NchA", "Nch")
    point(w, admin, "start")
    settings(w, OutOfLives="Player", **{"Place.At": "Zone", "Place.Group": "nobodys"})
    since = w.console("/rebirth config Place.At Zone")
    check(w.server.wait("No land claim is shared with the group nobodys", since, 5), "an admin is told")
    since = w.server.mark()
    serve(w, ala)
    check(w.server.wait("No land claim is shared with the group nobodys; nobody is held", since, 2), "the log says so")
    here = ala.state()
    ala.go(50, 0, 0)
    time.sleep(2.5)
    check(distance(ala.state(), here, "xz") > 40, "may go anywhere")
    ala.quit(), admin.quit()


def claims_made_and_removed_while_serving(w):
    admin, ala = w.join("CmrA", "Cmr")
    middle = zone(w, admin, 300)
    settings(w, OutOfLives="Player", **{"Place.At": "Zone"})
    serve(w, ala)
    w.trt("unclaim")
    since = w.server.mark()
    time.sleep(1.5)
    ala.go(40, 0, 0)
    time.sleep(2.5)
    check(not inside(ala, middle), "free while there are no claims")
    check(w.server.wait("nobody is held", since, 2), "the log says so")
    w.trt("claim CmrA afterlife 10 false")
    wait_inside(ala, middle, "held again")
    ala.until(mode="Survival", alive=True)
    ala.quit(), admin.quit()


def zone_without_a_place_inside_it(w):
    admin, ala = w.join("ZwpA", "Zwp")
    middle = zone(w, admin, 320)
    admin.go(80, 0, 0)
    time.sleep(3)
    point(w, admin, "start")
    settings(w, OutOfLives="Player", **{"Place.At": "Zone"})
    since = w.console("/rebirth config Place.At Zone")
    check(w.server.wait("'start' is outside the zone", since, 5), "an admin is told")
    serve(w, ala)
    wait_inside(ala, middle, "put into the zone anyway")
    ala.quit(), admin.quit()


GROUPS = [
    {"name": "places", "tests": [
        exile_to_the_marked_place,
        exile_survives_relog_and_restart,
        exile_ends_with_death_and_all_lives,
        ghost_reaches_a_marked_place_despite_a_relog,
        missing_places_fall_back,
        clients_know_the_leash,
        clients_know_they_are_in_a_zone,
        server_only_pulls_back_from_far_outside,
    ]},
    {"name": "zones", "tests": [
        zone_holds_players,
        zone_holds_ghosts,
        zone_survives_relog_and_restart,
        zone_death_returns_to_the_zone,
        zone_access,
        zone_access_keeps_zombie_limits,
        no_claims_hold_nobody,
        claims_made_and_removed_while_serving,
        zone_without_a_place_inside_it,
    ]},
]
