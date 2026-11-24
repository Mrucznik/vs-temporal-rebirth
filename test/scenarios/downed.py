"""Together with the Downed mod: being downed is not a death, and downs can take lives. Needs downed_<version>.zip in the test cache."""
import time
from harness import check
from steps import consistent, down, downs, get_up, phase, respawn, settings, slow, wait_phase


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
    # Nothing can hurt them right after they rise.
    time.sleep(6)
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
    {"name": "downed", "world": {'mods': ['downed'], 'files': {'ModConfig/downed.json': {'DurationSeconds': 6}}}, "tests": [
        downed_is_not_a_death,
        bleeding_out_costs_a_life,
        bleeding_out_on_the_last_life_makes_a_ghost,
        downed_player_cannot_perform_a_ritual,
    ]},
    {"name": "downs", "world": {'mods': ['downed'], 'files': {'ModConfig/downed.json': {'DurationSeconds': 6}}}, "tests": [
        three_lives_are_three_downs,
        death_after_a_down_is_free,
        death_without_a_down_takes_a_life,
        final_death_takes_everything,
        relog_while_downed,
        restart_while_downed,
        last_hit_cannot_be_dodged_by_relogging,
        downed_while_out_of_lives,
        changing_what_counts_keeps_lives,
    ]},
    {"name": "nodowned", "tests": [
        downs_need_the_downed_mod,
    ]},
]
