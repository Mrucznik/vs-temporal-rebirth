"""Who a player out of lives becomes (ghost, spectator, zombie, normal player) and how clients see them."""
import time
from harness import check, distance
from steps import consistent, ghost, last_life, phase, serve, settings, slow, wait_phase, walks


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


def spectator_passes_walls(w):
    settings(w, OutOfLives="Spectator")
    ala, = w.join("Spw")
    last_life(w, ala)
    ala.kill()
    ala.until(mode="Spectator", noclip=True)
    ala.quit()


def zombie_walks_until_revived_nearby(w):
    settings(w, "Undead")
    ala, ola = w.join("Zwu", "ZwuB")
    last_life(w, ala)
    ala.kill()
    ala.until(mode="Survival", alive=True, lives=0)
    wait_phase(w, ala, "Serving")
    w.trt("clearrifts")
    ola.go(120, 0, 0)
    before = ola.state()
    check(ola.gear(), "far away a gear works as usual")
    check(ola.state()["lives"] == before["lives"] + 1, "and gives a life")
    ola.go_near(ala)
    where = ala.state()
    check(ola.gear(), "next to the zombie it is a ritual")
    s = consistent(w, ala)
    check(s["mode"] == "Survival" and s["lives"] == 1 and distance(s, where) < 3, "revived where it stood")
    ala.quit(), ola.quit()


@slow(25)
def zombie_does_not_rise_in_lava(w):
    settings(w, "Undead")
    ala, = w.join("Zrl")
    last_life(w, ala)
    w.trt("surround Zrl lava-still-7 2")
    home = ala.state()
    check(home["liquid"], "the bot stands in lava")
    ala.kill()
    walks(w, ala)
    time.sleep(3)
    s = ala.state()
    check(s["alive"] and not s["liquid"], f"out of the lava: alive={s['alive']} liquid={s['liquid']}")
    check(distance(s, home, "xz") < 20, "but close to where they died")
    w.trt("surround Zrl air 3")
    w.console("/rebirth revive Zrl")
    consistent(w, ala, 15)
    ala.quit()


@slow(25)
def second_quick_death_moves_to_the_spawn_point(w):
    settings(w, "Exile", **{"Place.At": "Body"})
    ala, = w.join("Sqd")
    w.trt("farspawn Sqd 250")
    last_life(w, ala)
    home = ala.state()
    ala.kill()
    walks(w, ala)
    check(distance(ala.state(), home, "xz") < 20, "first where they died")
    time.sleep(1)
    ala.kill()
    ala.until(alive=False)
    walks(w, ala)
    time.sleep(4)
    s = ala.state()
    check(abs(s["x"] - s["spawnX"]) < 20 and abs(s["z"] - s["spawnZ"]) < 20, f"then at the spawn point: {s['x'] - s['spawnX']:.0f}, {s['z'] - s['spawnZ']:.0f}")
    w.console("/rebirth revive Sqd")
    consistent(w, ala, 15)
    ala.quit()


def zombie_without_items_fights_bare_handed(w):
    settings(w, "Undead")
    ala, ola = w.join("Zwf", "ZwfB")
    check(w.trt("interact Zwf ZwfB game:blade-falx-copper 0")["allowed"], "before: a weapon")
    last_life(w, ala)
    ala.kill()
    walks(w, ala)
    ola.go_near(ala)
    got = {f"{item} {button}": w.trt(f"interact Zwf ZwfB {item} {button}")["allowed"] for item in ("none", "game:blade-falx-copper") for button in (0, 2)}
    check(got == {"none 0": True, "none 2": False, "game:blade-falx-copper 0": False, "game:blade-falx-copper 2": False}, str(got))
    check(w.trt("interact ZwfB Zwf game:blade-falx-copper 2")["allowed"], "others are not limited")
    w.console("/rebirth revive Zwf")
    consistent(w, ala)
    settings(w, "Undead", **{"Zombie.CanUseItems": "true"})
    last_life(w, ala)
    ala.kill()
    walks(w, ala)
    check(w.trt("interact Zwf ZwfB game:blade-falx-copper 0")["allowed"], "a zombie with items")
    ala.quit(), ola.quit()


def players_out_of_lives_cannot_perform_rituals(w):
    settings(w, "Undead")
    a, b = w.join("Poa", "Pob")
    for bot in (a, b):
        last_life(w, bot)
        bot.kill()
        bot.until(mode="Survival", alive=True, lives=0)
        wait_phase(w, bot, "Serving")
    a.go_near(b)
    check(not a.gear(), "a zombie cannot revive another")
    check(phase(w, b) == "Serving", "still a zombie")
    a.quit(), b.quit()


def ghosts_are_seen_as_configured(w):
    ala, ola = w.join("Gsc", "GscB")
    for form, visible, look in (("Ghost", "Nobody", None), ("Ghost", "Wisp", "Wisp"), ("Ghost", "WispAndEcho", "WispAndEcho"), ("Spectator", "Wisp", None)):
        settings(w, OutOfLives=form, **{"Ghost.Visible": visible})
        serve(w, ala, form)
        check(ala.state()["look"] == look, f"{form} {visible}: {ala.state()['look']}")
        ola.go_near(ala)
        time.sleep(1.5)
        check(w.trt("tracks GscB Gsc")["tracked"], "others are sent where the ghost is")
        w.console("/rebirth config Ghost.Visible Wisp")
        ala.relog()
        check(ala.state()["look"] == look, "the same after a relog and a change of settings")
        w.console("/rebirth revive Gsc")
        check(consistent(w, ala)["look"] is None, "gone after the return")
    ala.quit(), ola.quit()


@slow(40)
def ghosts_of_0_6_keep_what_they_had(w):
    settings(w)
    ala, = w.join("Gko")
    ghost(w, ala)
    ala.quit()
    w.trt("oldentry Gko")
    w.restart()
    settings(w, "Hardcore")
    ala.join()
    check(consistent(w, ala)["mode"] == "Spectator", "still a ghost, not banned")
    settings(w)
    w.console("/rebirth revive Gko")
    consistent(w, ala)
    ala.quit()


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


def noclip_and_free_roaming(w):
    ala, = w.join("Nfr")
    ala.lose_all_lives()
    home = ala.until(mode="Spectator", alive=True, noclip=True)
    w.trt("move Nfr 100 20 0")
    time.sleep(3)
    check(distance(ala.state(), home) > 80, "no tether")
    ala.quit()


def old_configs_keep_ghosts_invisible(w):
    ala, = w.join("Ock")
    ala.lose_all_lives()
    s = ala.until(mode="Spectator", alive=True)
    check(s["look"] is None and s["seconds"] == -1, f"no look and no timer: {s['look']} {s['seconds']}")
    ala.quit()


GROUPS = [
    {"name": "forms", "tests": [
        last_death_makes_a_ghost,
        ghost_is_tethered,
        ghost_survives_relog,
        leaving_before_becoming_a_ghost,
        quick_relog_after_last_death,
        spectator_passes_walls,
        zombie_walks_until_revived_nearby,
        zombie_does_not_rise_in_lava,
        second_quick_death_moves_to_the_spawn_point,
        zombie_without_items_fights_bare_handed,
        players_out_of_lives_cannot_perform_rituals,
        ghosts_are_seen_as_configured,
        ghosts_of_0_6_keep_what_they_had,
        nothing_hurts_right_after_rising,
    ]},
    {"name": "oldfreeghost", "world": {'config': {'ReturnMode': 'Hours', 'ReturnAfterHours': 2, 'GhostNoClip': True, 'GhostTetherRadius': 0, 'ReturnHealthPercent': 40, 'ReturnSatietyPercent': 20}}, "tests": [
        noclip_and_free_roaming,
    ]},
    {"name": "oldlooks", "world": {'config': {'LivesPerGear': 1, 'ReturnMode': 'NextDay', 'GhostTetherRadius': 24}}, "tests": [
        old_configs_keep_ghosts_invisible,
    ]},
]
