"""Leaving, rejoining, crashing or acting twice while the mod is waiting for something."""
import time
from harness import Failed, check, distance
from v06_baseline import slow


def consistent(w, bot, timeout=8):
    """Waits until the mod is done with the player. Nobody may end up alive with no lives, or listed without a name."""
    end = time.monotonic() + timeout
    while True:
        s, entry = bot.state(), w.trt("registry").get("bot-" + bot.name)
        if entry is None:
            good = s["mode"] == "Survival" and s["alive"] and s["lives"] > 0
        else:
            good = entry["name"] == bot.name and entry["phase"] == "Serving" and s["mode"] == "Spectator" and s["alive"] and s["lives"] == 0
        if good:
            return s
        if time.monotonic() > end:
            raise Failed(f"inconsistent {bot.name}: {s['mode']} alive={s['alive']} lives={s['lives']} entry={entry}")
        time.sleep(0.4)


def at_spawn(bot, timeout=10):
    """The teleport may still be on its way when the player already counts as back."""
    end = time.monotonic() + timeout
    while True:
        s = bot.state()
        if abs(s["x"] - s["spawnX"]) < 60 and abs(s["z"] - s["spawnZ"]) < 60:
            return
        if time.monotonic() > end:
            raise Failed(f"came back {abs(s['x'] - s['spawnX']):.0f} blocks away from the spawn point")
        time.sleep(0.4)


def last_life(w, bot):
    w.console(f"/rebirth setlives {bot.name} 1")
    bot.until(lives=1)


def ghost(w, bot):
    last_life(w, bot)
    bot.kill()
    bot.until(mode="Spectator", alive=True)


# ---------- 1.5 s between the last death and becoming a ghost ----------

@slow(60)
def relog_at_every_moment_after_death(w):
    failures = []
    for i, wait in enumerate((0.0, 0.3, 0.6, 0.9, 1.2, 1.5, 1.8)):
        bot, = w.join(f"Rem{i}")
        last_life(w, bot)
        bot.kill()
        time.sleep(wait)
        bot.relog()
        try:
            check(consistent(w, bot, 5)["mode"] == "Spectator", "should be a ghost")
        except Failed as e:
            failures.append(f"{wait}s: {e}")
        bot.quit()
    check(not failures, "inconsistent after a relog: " + " | ".join(failures))


@slow(60)
def early_respawn_key_after_last_death(w):
    dodged = []
    for i in range(10):
        bot, = w.join(f"Erk{i}")
        last_life(w, bot)
        w.server.send(f"/entity cmd {bot.name} kill")
        for _ in range(20):
            bot.respawn()
            time.sleep(0.05)
        time.sleep(3)
        s = bot.state()
        if s["mode"] != "Spectator":
            dodged.append(f"{bot.name}: {s['mode']} alive={s['alive']} deaths={s['deaths']} lives={s['lives']}")
        bot.quit()
    check(not dodged, "dodged becoming a ghost: " + " | ".join(dodged))


def lives_granted_before_becoming_a_ghost(w):
    bot, = w.join("Lgb")
    last_life(w, bot)
    bot.kill()
    time.sleep(0.5)
    w.console("/rebirth setlives Lgb 3")
    time.sleep(3)
    if not bot.state()["alive"]:
        bot.respawn()
    s = consistent(w, bot)
    check(s["mode"] == "Survival" and s["lives"] == 3, f"keeps the granted lives: {s['mode']} {s['lives']}")
    bot.quit()


def revived_by_admin_before_becoming_a_ghost(w):
    bot, = w.join("Rab")
    last_life(w, bot)
    bot.kill()
    time.sleep(0.5)
    w.console("/rebirth revive Rab")
    time.sleep(3)
    check(consistent(w, bot)["mode"] == "Survival", "stays revived")
    bot.quit()


def ritual_before_the_target_is_a_ghost(w):
    bot, ola = w.join("Rbt", "RbtB")
    w.trt("clearrifts")
    w.trt("rift RbtB 4")
    last_life(w, bot)
    bot.kill()
    check(not ola.gear(), "nobody to revive yet, the gear stays")
    consistent(w, bot)
    check(ola.gear(), "now the ritual works")
    consistent(w, bot)
    w.trt("clearrifts")
    bot.quit(), ola.quit()


@slow(20)
def server_stops_right_after_last_death(w):
    bot, = w.join("Ssr")
    last_life(w, bot)
    bot.kill()
    # The game counts a death a tick later; stopping before that is simply no death.
    bot.until(alive=False, lives=0)
    w.restart()
    bot, = w.join("Ssr")
    check(consistent(w, bot)["mode"] == "Spectator", "a ghost after the restart")
    bot.quit()


# ---------- the teleport home waits for chunks ----------

@slow(30)
def relog_during_return(w):
    failures = []
    for i, wait in enumerate((0.0, 0.3, 1.0, 2.5)):
        bot, = w.join(f"Rdr{i}")
        w.trt(f"farspawn Rdr{i} {4000 + 3000 * i}")
        ghost(w, bot)
        w.trt(f"age Rdr{i} 48")
        w.trt("checkreturns")
        time.sleep(wait)
        bot.relog()
        try:
            check(consistent(w, bot, 20)["mode"] == "Survival", "should be back")
            at_spawn(bot)
        except Failed as e:
            failures.append(f"{wait}s: {e}")
        bot.quit()
    check(not failures, "stuck after leaving mid-return: " + " | ".join(failures))


def return_twice_at_once(w):
    bot, = w.join("Rta")
    w.trt("farspawn Rta -5000")
    ghost(w, bot)
    w.trt("age Rta 48")
    since = w.bots.mark()
    w.trt("checkreturns")
    w.trt("checkreturns")
    s = consistent(w, bot, 30)
    check(s["mode"] == "Survival" and s["lives"] == 1, f"one life, not more: {s['lives']}")
    time.sleep(2)
    heard = [line for line in w.bots.lines[since:] if line.startswith("[Rta] chat: You have returned")]
    check(len(heard) == 1, f"told once, not {len(heard)} times")
    bot.quit()


def admin_revive_during_return(w):
    bot, = w.join("Ard")
    w.trt("farspawn Ard -9000")
    ghost(w, bot)
    w.trt("age Ard 48")
    w.trt("checkreturns")
    w.console("/rebirth revive Ard")
    s = consistent(w, bot, 30)
    check(s["mode"] == "Survival" and s["lives"] == 1, "revived once")
    bot.quit()


def killed_during_return(w):
    bot, = w.join("Kdr")
    w.trt("farspawn Kdr 13000")
    ghost(w, bot)
    w.trt("age Kdr 48")
    w.trt("checkreturns")
    bot.kill()
    time.sleep(6)
    consistent(w, bot, 20)
    bot.quit()


@slow(35)
def server_stops_during_return(w):
    bot, = w.join("Ssd")
    w.trt("farspawn Ssd 17000")
    ghost(w, bot)
    w.trt("age Ssd 48")
    w.trt("checkreturns")
    s = bot.state()
    check(s["mode"] == "Spectator" and w.trt("registry")["bot-Ssd"]["phase"] == "Returning", "the server stops while the return is under way")
    w.restart()
    bot, = w.join("Ssd")
    check(consistent(w, bot, 20)["mode"] == "Survival", "the return is finished after the restart")
    at_spawn(bot)
    bot.quit()


# ---------- rituals ----------

def target_leaves_during_the_ritual(w):
    bot, ola = w.join("Tld", "TldB")
    ghost(w, bot)
    ola.go(0, 0, 900)
    w.trt("clearrifts")
    w.trt("rift TldB 4")
    ola.gear()
    bot.relog()
    check(consistent(w, bot, 20)["mode"] == "Survival", "revived despite leaving")
    w.trt("clearrifts")
    bot.quit(), ola.quit()


def target_left_before_the_ritual(w):
    bot, ola = w.join("Tlb", "TlbB")
    ghost(w, bot)
    ola.ask("/rebirth target Tlb", "will bring back Tlb")
    bot.vanish()
    time.sleep(0.6)
    w.trt("clearrifts")
    w.trt("rift TlbB 4")
    check(not ola.gear(), "the gear stays when the target is gone")
    bot.join()
    check(consistent(w, bot)["mode"] == "Spectator", "still a ghost")
    w.trt("clearrifts")
    bot.quit(), ola.quit()


def two_casters_one_ghost(w):
    bot, a, b = w.join("Tco", "TcoA", "TcoB")
    ghost(w, bot)
    b.go_near(a)
    w.trt("clearrifts")
    w.trt("rift TcoA 4")
    used = [a.gear(), b.gear()]
    check(used.count(True) == 1, f"only one gear is used up: {used}")
    s = consistent(w, bot)
    check(s["lives"] == 1, "one life")
    w.trt("clearrifts")
    bot.quit(), a.quit(), b.quit()


def chosen_target_already_returned(w):
    x, y, ola = w.join("Ctx", "Cty", "CtB")
    ghost(w, x), ghost(w, y)
    ola.ask("/rebirth target Ctx", "will bring back Ctx")
    w.console("/rebirth revive Ctx")
    x.until(mode="Survival")
    w.trt("clearrifts")
    w.trt("rift CtB 4")
    check(ola.gear(), "with one ghost left, the ritual brings that one back")
    check(consistent(w, y)["mode"] == "Survival", "the other ghost is back")
    w.trt("clearrifts")
    x.quit(), y.quit(), ola.quit()


# ---------- tether, time and saving ----------

def relogs_in_a_row_as_a_ghost(w):
    bot, = w.join("Rir")
    ghost(w, bot)
    for _ in range(4):
        bot.relog()
    check(consistent(w, bot)["mode"] == "Spectator", "still a ghost")
    bot.quit()


def time_passes_while_joining(w):
    bot, = w.join("Tpj")
    ghost(w, bot)
    bot.quit()
    w.trt("age Tpj 48")
    w.bots.send("join Tpj")
    for _ in range(6):
        w.trt("checkreturns")
        time.sleep(0.3)
    time.sleep(3)
    s = consistent(w, bot, 15)
    check(s["mode"] == "Survival" and s["lives"] == 1, "back exactly once")
    bot.quit()


@slow(30)
def crash_keeps_state_consistent(w):
    bot, ola = w.join("Cks", "CksB")
    w.console("/autosavenow")
    time.sleep(4)
    ghost(w, bot)
    w.crash()
    w.start()
    bot, ola = w.join("Cks", "CksB")
    consistent(w, bot)
    consistent(w, ola)
    bot.quit(), ola.quit()


@slow(25)
def crash_after_becoming_a_ghost_and_saving(w):
    bot, = w.join("Cas")
    ghost(w, bot)
    w.console("/autosavenow")
    time.sleep(4)
    w.crash()
    w.start()
    bot, = w.join("Cas")
    check(consistent(w, bot)["mode"] == "Spectator", "still a ghost")
    bot.quit()


GROUPS = [{"name": "relogs", "tests": [relog_at_every_moment_after_death]}, {"name": "races", "world": {"flat": False}, "tests": [
    early_respawn_key_after_last_death, lives_granted_before_becoming_a_ghost,
    revived_by_admin_before_becoming_a_ghost, ritual_before_the_target_is_a_ghost, server_stops_right_after_last_death,
    relog_during_return, return_twice_at_once, admin_revive_during_return, killed_during_return, server_stops_during_return,
    target_leaves_during_the_ritual, target_left_before_the_ritual, two_casters_one_ghost, chosen_target_already_returned,
    relogs_in_a_row_as_a_ghost, time_passes_while_joining, crash_keeps_state_consistent, crash_after_becoming_a_ghost_and_saving]}]
