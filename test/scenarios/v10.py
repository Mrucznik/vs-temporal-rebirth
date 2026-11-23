"""What 0.10.0 adds: the chooser for whom a ritual brings back. Bots cannot click, so the choice goes in as the server gets it."""
import time
from harness import check
from races import consistent, ghost
from v07 import settings


def two_ghosts_at_a_rift(w, *names):
    a, b, ola = w.join(*names)
    ghost(w, a), ghost(w, b)
    w.trt("clearrifts")
    w.trt("rift " + ola.name + " 4")
    return a, b, ola


def leave(w, ghosts, others=()):
    """Brings back who is still a ghost before everyone leaves: an offline ghost still waits and would change later hints."""
    for bot in ghosts:
        w.console("/rebirth revive " + bot.name)
        consistent(w, bot)
    for bot in (*ghosts, *others):
        bot.quit()


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
    {"name": "chooser", "tests": [chooser_choice_revives_and_takes_the_gear, chooser_closed_keeps_the_gear, one_candidate_needs_no_chooser,
                                  chooser_checks_again_before_the_revival, chooser_closes_when_the_caster_walks_away,
                                  chooser_lists_banned_players_who_left, hint_says_what_the_gear_would_do]},
]
