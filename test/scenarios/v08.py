"""What 0.8.0 adds: zones of land claims, downs that take lives, the timer and the look of ghosts."""
import time
from harness import Failed, check, distance
from races import consistent, last_life
from v06_baseline import slow
from v07 import phase, point, settings, wait_phase


# ---------- zones ----------

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
        w.trt("near ZacB Zac")
        time.sleep(1)
        other = w.trt("access ZacB 0")
        check(other["build"] == "LandClaimed" and (other["use"] == "Granted") == (everyone == "true"), f"others keep the claim's rules: {other}")
        w.console("/rebirth revive Zac")
        consistent(w, ala, 15)
    w.trt("near Zac ZacA")
    time.sleep(1)
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
    settings(w, OutOfLives="Zombie", **{"Place.At": "Zone", "Place.Access": "All", "Zombie.CanUseItems": "true"})
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
    check(w.server.wait("any more; nobody is held", since, 2), "the log says so")
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


# ---------- timer and looks ----------

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


def ghosts_are_seen_as_configured(w):
    ala, ola = w.join("Gsc", "GscB")
    for form, visible, look in (("Ghost", "Nobody", None), ("Ghost", "Wisp", "Wisp"), ("Ghost", "WispAndEcho", "WispAndEcho"), ("Spectator", "Wisp", None)):
        settings(w, OutOfLives=form, **{"Ghost.Visible": visible})
        serve(w, ala, form)
        check(ala.state()["look"] == look, f"{form} {visible}: {ala.state()['look']}")
        w.trt("near GscB Gsc")
        time.sleep(1.5)
        check(w.trt("tracks GscB Gsc")["tracked"], "others are sent where the ghost is")
        w.console("/rebirth config Ghost.Visible Wisp")
        ala.relog()
        check(ala.state()["look"] == look, "the same after a relog and a change of settings")
        w.console("/rebirth revive Gsc")
        check(consistent(w, ala)["look"] is None, "gone after the return")
    ala.quit(), ola.quit()


def old_configs_keep_ghosts_invisible(w):
    ala, = w.join("Ock")
    ala.lose_all_lives()
    s = ala.until(mode="Spectator", alive=True)
    check(s["look"] is None and s["seconds"] == -1, f"no look and no timer: {s['look']} {s['seconds']}")
    ala.quit()


# ---------- downs take lives ----------

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


@slow(40)
def three_lives_are_three_downs(w):
    ala, = w.join("Tld")
    downs(w, ala, 3)
    for left in (2, 1, 0):
        down(w, ala)
        check(ala.state()["lives"] == left, f"a down takes one life: {ala.state()['lives']}")
        time.sleep(2)
        check(ala.state()["lives"] == left, "once")
        get_up(w, ala)
    check(phase(w, ala) is None, "still playing with no lives")
    since = w.bots.mark()
    w.trt("hurt Tld 500")
    ala.until(mode="Spectator", alive=True, downed=False, lives=0)
    ala.heard("Your last life is spent", since)
    check(phase(w, ala) == "Serving", "the next fatal hit is the last")
    w.console("/rebirth revive Tld")
    check(consistent(w, ala)["lives"] == 1, "back with a life")
    down(w, ala)
    ala.until(lives=0)
    get_up(w, ala)
    ala.quit()


@slow(40)
def death_after_a_down_is_free(w):
    ala, = w.join("Dad")
    downs(w, ala, 2)
    down(w, ala)
    ala.until(lives=1)
    ala.until(alive=False, timeout=15)
    check(ala.state()["lives"] == 1, "bleeding out takes nothing more")
    respawn(ala)
    down(w, ala)
    ala.until(lives=0)
    s = ala.until(alive=False, timeout=15)
    time.sleep(1)
    s = ala.state()
    check(s["lives"] == 0 and s["mayRespawn"] and phase(w, ala) is None, f"not the last death: {s['lives']} {s['mayRespawn']}")
    s = respawn(ala)
    check(not s["mayRespawn"] and s["lives"] == 0, "plays on with no lives")
    w.trt("hurt Dad 500")
    ala.until(mode="Spectator", alive=True, downed=False)
    w.console("/rebirth revive Dad")
    consistent(w, ala)
    ala.quit()


def death_without_a_down_takes_a_life(w):
    ala, = w.join("Dwd")
    downs(w, ala, 1)
    ala.kill()
    ala.until(alive=False, lives=0)
    time.sleep(0.5)
    check(ala.state()["mayRespawn"], "like a down, not the last yet")
    respawn(ala)
    ala.kill()
    ala.until(mode="Spectator", alive=True)
    check(phase(w, ala) == "Serving", "the next one is")
    w.console("/rebirth revive Dwd")
    consistent(w, ala)
    ala.quit()


@slow(40)
def final_death_takes_everything(w):
    ala, = w.join("Fdt")
    downs(w, ala, 3, "DownsFinalDeath")
    down(w, ala)
    ala.until(lives=2)
    get_up(w, ala)
    check(ala.state()["lives"] == 2, "getting up keeps the rest")
    down(w, ala)
    ala.until(mode="Spectator", alive=True, downed=False, lives=0, timeout=15)
    w.console("/rebirth revive Fdt")
    consistent(w, ala)
    downs(w, ala, 3, "DownsFinalDeath")
    ala.kill()
    ala.until(mode="Spectator", alive=True, lives=0)
    w.console("/rebirth revive Fdt")
    consistent(w, ala)
    ala.quit()


@slow(50)
def relog_while_downed(w):
    ala, = w.join("Rwdn")
    downs(w, ala, 2)
    down(w, ala)
    ala.until(lives=1)
    ala.relog()
    time.sleep(2.5)
    s = ala.state()
    check(s["lives"] == 1, f"not charged again: {s['lives']}")
    if s["downed"]:
        get_up(w, ala)
    elif not s["alive"]:
        respawn(ala)
    down(w, ala)
    ala.until(lives=0)
    ala.relog()
    time.sleep(2.5)
    check(phase(w, ala) is None, "a last life's down is not a last hit after a relog")
    ala.until(alive=False, timeout=15)
    time.sleep(1)
    ala.relog()
    check(ala.state()["mayRespawn"] and phase(w, ala) is None, "the death screen survives a relog")
    respawn(ala)
    ala.quit()


@slow(40)
def restart_while_downed(w):
    ala, = w.join("Rswd")
    downs(w, ala, 1)
    down(w, ala)
    ala.until(lives=0)
    ala.quit()
    w.restart()
    ala, = w.join("Rswd")
    time.sleep(2.5)
    check(phase(w, ala) is None, "the down was paid for before the restart")
    ala.until(alive=False, timeout=15)
    ala.quit()
    w.restart()
    ala, = w.join("Rswd")
    check(phase(w, ala) is None and ala.state()["mayRespawn"], "the death screen survives a restart")
    respawn(ala)
    ala.quit()


@slow(40)
def last_hit_cannot_be_dodged_by_relogging(w):
    dodged = []
    for i, wait in enumerate((0.0, 0.1, 0.5, 1.4)):
        bot, = w.join(f"Lhd{i}")
        downs(w, bot, 0)
        w.server.send(f"/trt hurt {bot.name} 500")
        time.sleep(wait)
        bot.relog()
        time.sleep(3)
        s = bot.state()
        if s["mode"] != "Spectator" or phase(w, bot) != "Serving":
            dodged.append(f"{wait}s: {s['mode']} alive={s['alive']} downed={s['downed']} {phase(w, bot)}")
        bot.quit()
    check(not dodged, " | ".join(dodged))


@slow(30)
def downed_while_out_of_lives(w):
    ala, = w.join("Dwo")
    settings(w, OutOfLives="Zombie", **{"Lives.Count": "Downs"})
    w.console("/rebirth setlives Dwo 0")
    ala.until(lives=0)
    w.trt("hurt Dwo 500")
    ala.until(mode="Survival", alive=True, downed=False, lives=0, timeout=15)
    wait_phase(w, ala, "Serving")
    time.sleep(1)
    down(w, ala)
    time.sleep(1)
    check(phase(w, ala) == "Serving" and ala.state()["downed"], "only downed")
    w.console("/rebirth revive Dwo")
    s = consistent(w, ala, 15)
    check(s["lives"] == 1 and not s["downed"], f"comes back standing, with the whole life: {s['lives']} {s['downed']}")
    time.sleep(2)
    check(ala.state()["lives"] == 1, "nothing charged later")
    ala.quit()


@slow(30)
def changing_what_counts_keeps_lives(w):
    ala, = w.join("Cwc")
    downs(w, ala, 3)
    down(w, ala)
    ala.until(lives=2)
    get_up(w, ala)
    w.console("/rebirth config Lives.Count Deaths")
    check(ala.state()["lives"] == 2, "the same lives")
    down(w, ala)
    check(ala.state()["lives"] == 2, "downs are free again")
    ala.until(alive=False, lives=1, timeout=15)
    respawn(ala)
    w.console("/rebirth config Lives.Count Downs")
    down(w, ala)
    ala.until(lives=0)
    w.console("/rebirth config Lives.Count Deaths")
    ala.until(alive=False, timeout=15)
    time.sleep(1)
    check(phase(w, ala) is None and ala.state()["mayRespawn"], "that down was already paid for")
    respawn(ala)
    ala.kill()
    ala.until(mode="Spectator", alive=True)
    w.console("/rebirth revive Cwc")
    consistent(w, ala)
    ala.quit()


def downs_need_the_downed_mod(w):
    settings(w, **{"Lives.Count": "Downs"})
    since = w.console("/rebirth config Lives.Count Downs")
    check(w.server.wait("needs the Downed mod", since, 5), "an admin is told")
    ala, = w.join("Dnm")
    ala.kill()
    ala.until(alive=False, lives=1)
    time.sleep(3)
    ala.respawn()
    ala.until(alive=True)
    ala.kill()
    ala.until(mode="Spectator", alive=True)
    ala.quit()
    settings(w)


GROUPS = [
    {"name": "zones", "tests": [zone_holds_players, zone_holds_ghosts, zone_survives_relog_and_restart, zone_death_returns_to_the_zone, zone_access,
                                 zone_access_keeps_zombie_limits, no_claims_hold_nobody, claims_made_and_removed_while_serving, zone_without_a_place_inside_it]},
    {"name": "looks", "tests": [timer_counts_down, timer_after_a_restart, no_timer_without_an_end, ghosts_are_seen_as_configured, downs_need_the_downed_mod]},
    {"name": "oldlooks", "world": {"config": {"LivesPerGear": 1, "ReturnMode": "NextDay", "GhostTetherRadius": 24}}, "tests": [old_configs_keep_ghosts_invisible]},
    {"name": "downs", "world": {"mods": ["downed"], "files": {"ModConfig/downed.json": {"DurationSeconds": 6}}},
     "tests": [three_lives_are_three_downs, death_after_a_down_is_free, death_without_a_down_takes_a_life, final_death_takes_everything, relog_while_downed,
               restart_while_downed, last_hit_cannot_be_dodged_by_relogging, downed_while_out_of_lives, changing_what_counts_keeps_lives]},
]
