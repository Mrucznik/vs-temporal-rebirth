"""Bans as the form for players out of lives."""
import time
from harness import check, distance
from steps import consistent, last_life, phase, refused, settings, slow


def ban_and_back_after_the_time(w):
    settings(w, "Timeout")
    ala, ola = w.join("Bab", "BabB")
    last_life(w, ala)
    since = w.bots.mark()
    ala.kill()
    check(w.bots.wait(r"^\[Bab\] kicked: You are out of lives. You can return: 20", since, 10), "kicked with the reason")
    ola.heard("Bab has lost their last life", since)
    check("You can return: 20" in refused(w, ala), "cannot join")
    w.trt("age Bab 25")
    ala.join()
    s = consistent(w, ala)
    check(s["mode"] == "Survival" and s["lives"] == 1, "back with one life")
    ala.quit(), ola.quit()


def ban_message_and_forever(w):
    settings(w, "Hardcore", **{"Time.Duration": "forever", "Messages.Ban": "{player}: przerwa do {time}"})
    ala, = w.join("Bmf")
    last_life(w, ala)
    ala.kill()
    time.sleep(3)
    check(refused(w, ala) == "Bmf: przerwa do -", "the admin's own text")
    w.trt("age Bmf 9000")
    refused(w, ala)
    w.console("/rebirth revive Bmf")
    ala.join()
    check(consistent(w, ala)["mode"] == "Survival", "an admin can end it while the player is offline")
    w.console('/rebirth config Messages.Ban ""')
    ala.quit()


def ban_lifted_by_a_ritual(w):
    settings(w, "Timeout")
    ala, ola = w.join("Blr", "BlrB")
    last_life(w, ala)
    ala.kill()
    time.sleep(3)
    w.trt("clearrifts")
    w.trt("rift BlrB 4")
    w.console("/rebirth config Ritual.CanLiftBan false")
    check(not ola.gear(), "not when rituals do not lift bans")
    w.console("/rebirth config Ritual.CanLiftBan true")
    ola.ask("/rebirth target Blr", "will bring back Blr")
    check(ola.gear(), "the ritual works on an offline player")
    ala.join()
    s = consistent(w, ala, 15)
    check(s["mode"] == "Survival" and distance(s, ola.state()) < 60, "back next to the caster")
    w.trt("clearrifts")
    ala.quit(), ola.quit()


@slow(40)
def ban_cannot_be_dodged_by_relogging(w):
    settings(w, "Timeout")
    dodged = []
    for i, wait in enumerate((0.0, 0.4, 0.9, 1.4)):
        bot, = w.join(f"Bcd{i}")
        last_life(w, bot)
        w.server.send(f"/entity cmd {bot.name} kill")
        time.sleep(wait)
        bot.vanish()
        time.sleep(0.45)
        since = w.bots.mark()
        bot.join(expect=False)
        time.sleep(5)
        listed = phase(w, bot)
        joined = w.bots.wait(rf"^\[{bot.name}\] joined", since, 0.1)
        kicked = w.bots.wait(rf"^\[{bot.name}\] kicked", since, 0.1)
        # Leaving before the game even counts the death is no death; anything else must end in a ban.
        if not kicked and listed is not None:
            dodged.append(f"{wait}s: joined={bool(joined)} phase={listed}")
        w.drop_bots()
        time.sleep(1)
    check(not dodged, "still playing while banned: " + " | ".join(dodged))


def ritual_lifts_the_ban_of_someone_who_left_at_once(w):
    settings(w, "Timeout")
    ala, ola = w.join("Rlb", "RlbB")
    last_life(w, ala)
    ala.kill()
    ala.until(alive=False, lives=0)
    ala.vanish()
    time.sleep(3)
    check(phase(w, ala) == "Dying", "left before being banned")
    refused(w, ala)
    w.trt("clearrifts")
    w.trt("rift RlbB 4")
    # Other bans in this world may be waiting too.
    ola.ask("/rebirth target Rlb", "will bring back Rlb")
    check(ola.gear(), "the ritual works")
    ala.join()
    check(consistent(w, ala, 15)["mode"] == "Survival", "back")
    w.trt("clearrifts")
    ala.quit(), ola.quit()


GROUPS = [
    {"name": "bans", "tests": [
        ban_and_back_after_the_time,
        ban_message_and_forever,
        ban_lifted_by_a_ritual,
        ban_cannot_be_dodged_by_relogging,
        ritual_lifts_the_ban_of_someone_who_left_at_once,
    ]},
]
