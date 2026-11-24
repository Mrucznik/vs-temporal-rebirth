"""Rituals: where they work, whom they bring back and where to."""
import time
from harness import check, distance
from steps import consistent, ghost, last_life, leave, point, read_config, settings, slow, two_ghosts_at_a_rift


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
    used = ola.gear_keep()
    check(not used["consumed"], "the gear stays until someone is chosen")
    check(used["offered"] == ["Rca", "Rcb"], f"the chooser offers both: {used['offered']}")
    ola.choose("cancel")
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


def ritual_at_a_marked_place_only(w):
    settings(w, **{"Ritual.Where": "FixedPlace", "Ritual.Range": 6})
    ala, ola = w.join("Rmp", "RmpB")
    ghost(w, ala)
    w.trt("clearrifts")
    w.trt("rift RmpB 2")
    before = ola.state()["lives"]
    point(w, ola, "ritual")
    ola.go(40, 0, 0)
    check(ola.gear() and ola.state()["lives"] == before + 1, "elsewhere, even at a rift, a gear works as usual")
    ola.go(-40, 0, 0)
    check(ola.gear(), "at the marked place it is a ritual")
    check(consistent(w, ala)["mode"] == "Survival", "revived")
    w.trt("clearrifts")
    ala.quit(), ola.quit()


def ritual_anywhere_needs_a_choice(w):
    settings(w, **{"Time.Duration": "forever", "Ritual.Where": "Anywhere"})
    ala, ola = w.join("Ran", "RanB")
    ghost(w, ala)
    w.trt("clearrifts")
    before = ola.state()["lives"]
    used = ola.gear_keep()
    check(not used["consumed"] and used["offered"] == ["Ran"], f"without a choice the gear opens the chooser, even for one: {used}")
    check(ola.choose("bind") and ola.state()["lives"] == before + 1, "which can bind to the place as usual")
    check(ala.state()["mode"] == "Spectator", "still a ghost")
    ola.gear_keep()
    check(ola.choose("Ran"), "or bring back whoever is chosen")
    check(consistent(w, ala)["mode"] == "Survival", "revived")
    ghost(w, ala)
    ola.ask("/rebirth target Ran", "will bring back Ran")
    check(ola.gear(), "with a choice it is a ritual")
    check(consistent(w, ala)["mode"] == "Survival", "revived")
    ala.quit(), ola.quit()


def ritual_disabled(w):
    settings(w, "Hardcore", OutOfLives="Ghost")
    ala, ola = w.join("Rdi", "RdiB")
    ghost(w, ala)
    w.trt("clearrifts")
    w.trt("rift RdiB 2")
    before = ola.state()["lives"]
    check(ola.gear() and ola.state()["lives"] == before + 1, "a gear at a rift only gives a life")
    check(ala.state()["mode"] == "Spectator", "still a ghost")
    w.trt("clearrifts")
    ala.quit(), ola.quit()


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


def ritual_turned_off_in_0_6_stays_off(w):
    saved = read_config(w)
    check(saved["Ritual"]["Where"] == "Disabled", f"rituals: {saved['Ritual']['Where']}")
    check(saved["Time"]["Duration"] == "0.01h", f"time: {saved['Time']['Duration']}")
    check(saved["Place"]["Radius"] == 0, "no distance limit")
    check(not w.log_has("is not accepted"), "nothing left to complain about")


def chooser_choice_revives_and_takes_the_gear(w):
    a, b, ola = two_ghosts_at_a_rift(w, "Cca", "Ccb", "CcC")
    used = ola.gear_keep()
    check(not used["consumed"], "the gear stays while the chooser is open")
    check(used["offered"] == ["Cca", "Ccb"], f"both are offered: {used['offered']}")
    since = w.bots.mark()
    check(ola.choose("Ccb"), "the choice uses the gear")
    check(ola.state()["gears"] == 0, "one gear taken")
    b.until(mode="Survival", alive=True)
    ola.heard("The gear shatters", since)
    check(a.state()["mode"] == "Spectator", "the other one stays")
    check(not ola.choose("Cca"), "a second choice finds no chooser")
    check(a.state()["mode"] == "Spectator", "and brings nobody back")
    w.trt("clearrifts")
    leave(w, [a], [b, ola])


def chooser_closed_keeps_the_gear(w):
    a, b, ola = two_ghosts_at_a_rift(w, "Ckga", "Ckgb", "CkgC")
    ola.gear_keep()
    check(not ola.choose("cancel"), "closing it uses nothing")
    check(ola.state()["gears"] == 1, "the gear stays")
    check(not ola.choose("Ckga"), "a choice after closing is refused")
    check(ola.state()["gears"] == 1, "still the gear")
    check(a.state()["mode"] == "Spectator" and b.state()["mode"] == "Spectator", "both still ghosts")
    w.trt("clearrifts")
    leave(w, [a, b], [ola])


def one_candidate_needs_no_chooser(w):
    ala, ola = w.join("Ocn", "OcnB")
    ghost(w, ala)
    w.trt("clearrifts")
    w.trt("rift OcnB 4")
    used = ola.gear_keep()
    check(used["consumed"] and used["offered"] == [], f"revived at once: {used}")
    check(consistent(w, ala)["mode"] == "Survival", "back")
    w.trt("clearrifts")
    ala.quit(), ola.quit()


def chooser_checks_again_before_the_revival(w):
    a, b, ola = two_ghosts_at_a_rift(w, "Cra", "Crb", "CrC")
    ola.gear_keep()
    w.console("/rebirth revive Cra")
    consistent(w, a)
    since = w.bots.mark()
    check(not ola.choose("Cra"), "whoever came back meanwhile is refused")
    ola.heard("can no longer be called back", since)
    check(ola.state()["gears"] == 1, "the gear stays")
    check(b.state()["mode"] == "Spectator", "nobody else is brought back instead")
    w.trt("clearrifts")
    leave(w, [b], [a, ola])


def chooser_closes_when_the_caster_walks_away(w):
    a, b, ola = two_ghosts_at_a_rift(w, "Cwa", "Cwb", "CwC")
    ola.gear_keep()
    since = w.bots.mark()
    ola.go(30, 0, 0)
    ola.heard("stepped away", since)
    check(w.trt("gearkeep CwC")["offered"] == [], "no chooser left (and far from the rift the gear sets the spawn point)")
    check(not ola.choose("Cwa"), "a late choice is refused")
    check(a.state()["mode"] == "Spectator", "still a ghost")
    w.trt("clearrifts")
    leave(w, [a, b], [ola])


def chooser_lists_banned_players_who_left(w):
    settings(w, "Timeout")
    x, y, ola = w.join("Cbx", "Cby", "CbC")
    for bot in (x, y):
        w.console(f"/rebirth setlives {bot.name} 1")
        bot.until(lives=1)
        bot.kill()
        bot.until(alive=False, lives=0)
    time.sleep(3)
    w.trt("clearrifts")
    w.trt("rift CbC 4")
    used = ola.gear_keep()
    check(sorted(used["offered"]) == ["Cbx", "Cby"], f"banned players are offered: {used['offered']}")
    ola.choose("cancel")
    w.console("/rebirth config Ritual.CanLiftBan false")
    used = ola.gear_keep()
    check(used["offered"] == [] and not used["consumed"], f"not when rituals do not lift bans: {used}")
    w.console("/rebirth config Ritual.CanLiftBan true")
    w.trt("clearrifts")
    w.console("/rebirth revive Cbx")
    w.console("/rebirth revive Cby")
    ola.quit()
    settings(w)


def hint_says_what_the_gear_would_do(w):
    ala, ola = w.join("Hsw", "HswB")
    w.trt("clearrifts")
    w.trt("rift HswB 4")
    hint = lambda: w.trt("hint HswB")
    check(hint()["state"] == "None" and hint()["atSite"], f"nobody waiting, at a rift: {hint()}")
    ghost(w, ala)
    check(hint() == {"state": "Ready", "atSite": True, "count": 1, "secondsLeft": 0}, f"one waiting: {hint()}")
    ola.go(30, 0, 0)
    check(hint()["state"] == "TooFarRift" and not hint()["atSite"], f"away from the rift: {hint()}")
    ola.go(-30, 0, 0)
    w.console("/rebirth config Ritual.CooldownHours 2")
    check(ola.gear(), "revived")
    consistent(w, ala)
    ghost(w, ala)
    h = hint()
    check(h["state"] == "Cooldown" and h["atSite"] and 7100 < h["secondsLeft"] <= 7200, f"cooldown: {h}")
    w.console("/rebirth config Ritual.CooldownHours 0")
    ala.quit()
    check(hint()["state"] == "NobodyRevivable", f"the ghost left: {hint()}")
    w.console("/rebirth revive Hsw")
    w.trt("clearrifts")
    ola.quit()


GROUPS = [
    {"name": "rituals", "tests": [
        ritual_brings_a_ghost_back,
        ritual_without_ghosts_keeps_the_gear,
        ritual_needs_a_choice_between_ghosts,
        offline_ghost_cannot_be_revived,
        ritual_at_a_marked_place_only,
        ritual_anywhere_needs_a_choice,
        ritual_disabled,
        ritual_to_the_spawn_point_works_after_a_restart,
    ]},
    {"name": "olddeathspot", "world": {'config': {'RitualReviveAt': 'DeathSpot', 'RitualCooldownHours': 5, 'LivesPerGear': 3}}, "tests": [
        ritual_at_death_spot_with_cooldown,
    ]},
    {"name": "oldritualonly", "world": {'config': {'ReturnMode': 'Disabled', 'RitualReviveAt': 'Spawn'}}, "tests": [
        ritual_to_spawn_and_no_natural_return,
    ]},
    {"name": "oldritual", "world": {'config': {'ReturnMode': 'Hours', 'ReturnAfterHours': 0, 'RitualRiftRange': 0, 'GhostTetherRadius': -1}}, "tests": [
        ritual_turned_off_in_0_6_stays_off,
    ]},
    {"name": "chooser", "tests": [
        chooser_choice_revives_and_takes_the_gear,
        chooser_closed_keeps_the_gear,
        one_candidate_needs_no_chooser,
        chooser_checks_again_before_the_revival,
        chooser_closes_when_the_caster_walks_away,
        chooser_lists_banned_players_who_left,
        hint_says_what_the_gear_would_do,
    ]},
]
