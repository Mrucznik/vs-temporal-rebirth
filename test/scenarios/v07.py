"""What 0.7.0 adds: config file, presets and commands, bans, normal players and zombies, places, rituals."""
import json, subprocess, time
from harness import REPO, Failed, check, distance
from races import consistent, ghost, last_life
from v06_baseline import read_config, slow


def settings(w, preset="Classic", **values):
    """Starts from a preset and changes single settings, like an admin in the console."""
    w.console("/rebirth preset " + preset)
    for key, value in {"Lives.Max": -1, "Lives.PerGear": 1, **values}.items():
        since = w.console(f"/rebirth config {key} {value}")
        if not w.server.wait(rf"{key} is now ", since, 5):
            raise Failed(f"could not set {key} to {value}")


def point(w, bot, name):
    """Marks a place where the bot stands."""
    w.console(f"/player {bot.name} privilege grant temporalrebirth.points")
    time.sleep(0.5)
    bot.ask(f"/rebirth point set {name}", f"Place '{name}' marked")


def phase(w, bot):
    entry = w.trt("registry").get("bot-" + bot.name)
    return entry and entry["phase"]


def wait_phase(w, bot, wanted, timeout=10):
    end = time.monotonic() + timeout
    while phase(w, bot) != wanted:
        if time.monotonic() > end:
            raise Failed(f"{bot.name}: wanted phase {wanted}, have {phase(w, bot)}")
        time.sleep(0.3)


def refused(w, bot):
    """Tries to join and returns the text shown instead."""
    since = w.bots.mark()
    bot.join(expect=False)
    m = w.bots.wait(rf"^\[{bot.name}\] (kicked: .*|joined)", since, 20)
    check(m and m.group(1).startswith("kicked: "), f"should be refused, got {m and m.group(1)}")
    return m.group(1)[8:]


# ---------- config file ----------

def node(code):
    files = "".join(f"vm.runInContext(fs.readFileSync({json.dumps(str(REPO / 'docs/configurator' / f))},'utf8'),c);" for f in ("schema.js", "logic.js", "i18n/en.js", "i18n.js"))
    script = "const fs=require('fs'),vm=require('vm');const w={};const c=vm.createContext({window:w,TextEncoder,TextDecoder,btoa,atob,Uint8Array});" + files + "const T=w.TR;T.localize('en');" + code
    return subprocess.run(["node", "-e", script], capture_output=True, text=True, check=True).stdout


def new_file_matches_the_configurator(w):
    w.console("/rebirth preset Exile")
    time.sleep(0.5)
    check(w.config_path.read_text(encoding="utf8") == node("process.stdout.write(T.toFile(T.applyPreset('Exile'),true))"), "the mod and the page write the same file")


def configurator_code_is_imported(w):
    code = node("const s=T.applyPreset('Timeout');s['Messages.Ban']='Żółw {time}';s['Lives.Max']=4;s['Admin.AllowRevive']=false;process.stdout.write(T.encode('Timeout',s))")
    w.console("/rebirth import " + code)
    time.sleep(0.5)
    saved = read_config(w)
    check(saved["OutOfLives"] == "Ban" and saved["Time"]["Duration"] == "24h" and saved["Lives"]["Max"] == 4 and saved["Messages"]["Ban"] == "Żółw {time}", "everything in the code is used")
    check(saved["Admin"]["AllowRevive"] is True, "administration is not changed by a code")
    since = w.console("/rebirth import zzzz")
    check(w.server.wait("cannot be read", since, 5), "a broken code is refused")
    w.console('/rebirth config Messages.Ban ""')
    settings(w)


def every_preset_imports_like_it_applies(w):
    for preset in ("Classic", "Rescue", "Undead", "Exile", "Timeout", "Hardcore"):
        w.console("/rebirth import " + node(f"process.stdout.write(T.encode('{preset}',T.applyPreset('{preset}')))"))
        time.sleep(0.4)
        check(w.config_path.read_text(encoding="utf8") == node(f"process.stdout.write(T.toFile(T.applyPreset('{preset}'),true))"), preset)


def config_commands(w):
    since = w.console("/rebirth config Place.Radius -5")
    check(w.server.wait(r"Place.Radius: value not accepted. Accepted: 0 … 10000", since, 5), "bad values list what is accepted")
    since = w.console("/rebirth config Nope 1")
    check(w.server.wait("No such setting", since, 5), "unknown settings are refused")
    since = w.console("/rebirth config Admin.AllowEdit false")
    check(w.server.wait("only be changed in the config file", since, 5), "administration is file only")
    since = w.console("/rebirth config time.duration next day 07:30")
    check(w.server.wait(r'Time.Duration is now "next day 07:30"', since, 5), "values with spaces, keys in any case")
    since = w.console("/rebirth preset Exile")
    check(w.server.wait("need a marked place", since, 5), "a preset reminds of the place it needs")
    settings(w)


def rights(w):
    ala, = w.join("Rgt")
    ala.ask("/rebirth config Place.Radius 5", "do not have permission")
    ala.ask("/rebirth revive Rgt", "do not have permission")
    w.console("/player Rgt privilege grant temporalrebirth.revive")
    time.sleep(0.5)
    ala.ask("/rebirth revive Rgt", "still has lives left")
    ala.ask("/rebirth preset Undead", "do not have permission")
    w.console("/player Rgt privilege grant temporalrebirth.edit")
    time.sleep(0.5)
    ala.ask("/rebirth preset Undead", "Using preset Undead")
    ala.ask("/rebirth config", "OutOfLives")
    settings(w)
    ala.quit()


def broken_file_survives_an_edit(w):
    check(w.log_has("temporalrebirth.json is invalid"), "the error is logged")
    w.console("/rebirth config Place.Radius 30")
    time.sleep(0.5)
    check(read_config(w)["Place"]["Radius"] == 30, "editing works")
    check("oops" in (w.config_path.parent / (w.config_path.name + ".bak")).read_text(encoding="utf8"), "the broken file is kept as .bak")


def apply_preset_in_file(w):
    saved = read_config(w)
    check(saved["ApplyPreset"] == "", "the field is cleared")
    check(saved["OutOfLives"] == "Ban" and saved["Time"]["Duration"] == "7d", "the preset is filled in")
    check(saved["Lives"]["PerGear"] == 3, "lives are kept")


def bad_values_stay_in_the_file(w):
    check(w.log_has(r'Place.Radius: "dużo" is not accepted, using 24. Accepted: 0'), "logged with what is accepted")
    check(w.log_has("Nonsense: unknown setting"), "unknown settings are logged")
    check('"dużo"' in w.config_path.read_text(encoding="utf8"), "the admin's file is not rewritten")


def admin_actions_can_be_turned_off(w):
    ala, ola = w.join("Aao", "AaoB")
    w.console("/player AaoB role admin")
    time.sleep(0.5)
    ghost(w, ala)
    ola.ask("/rebirth revive Aao", "turned off in the server's config")
    ola.ask("/rebirth preset Undead", "turned off in the server's config")
    w.console("/rebirth revive Aao")
    ala.until(mode="Survival")
    ala.quit(), ola.quit()


# ---------- bans ----------

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


# ---------- normal players and zombies ----------

def exile(w, bot, **extra):
    settings(w, "Exile", **extra)
    last_life(w, bot)


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


def spectator_passes_walls(w):
    settings(w, OutOfLives="Spectator")
    ala, = w.join("Spw")
    last_life(w, ala)
    ala.kill()
    ala.until(mode="Spectator", noclip=True)
    ala.quit()


# ---------- rituals, places, lives ----------

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
    settings(w, "Rescue")
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


def lives_limit(w):
    settings(w, **{"Lives.Max": 4, "Lives.PerGear": 3})
    ala, = w.join("Lil")
    w.trt("clearrifts")
    check(ala.gear(), "used")
    ala.until(lives=4)
    since = w.bots.mark()
    check(ala.gear(), "the gear still sets the spawn point")
    ala.heard("gears will not give you more", since)
    check(ala.state()["lives"] == 4, "not above the limit")
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


def custom_messages(w):
    settings(w, **{"Messages.Ghost": "Witaj w zaświatach, {player}!"})
    ala, = w.join("Cms")
    since = w.bots.mark()
    ghost(w, ala)
    ala.heard("Witaj w zaświatach, Cms!", since)
    since = w.console("/rebirth config Messages.Ghost {nick}")
    check(w.server.wait(r"value not accepted. Accepted: \{player\}", since, 5), "unknown placeholders are refused")
    since = w.console('/rebirth config Messages.Ghost ""')
    check(w.server.wait(r'Messages.Ghost is now ""', since, 5), "a message can be cleared")
    ala.quit()


GROUPS = [
    {"name": "config", "tests": [new_file_matches_the_configurator, configurator_code_is_imported, every_preset_imports_like_it_applies, config_commands, rights]},
    {"name": "brokenedit", "world": {"raw_config": "{ oops"}, "tests": [broken_file_survives_an_edit]},
    {"name": "applypreset", "world": {"raw_config": '{ "ApplyPreset": "hardcore", "OutOfLives": "Ghost", "Lives": { "PerGear": 3 } }'}, "tests": [apply_preset_in_file]},
    {"name": "badvalues", "world": {"raw_config": '{ "OutOfLives": "Ghost", "Place": { "Radius": "dużo" }, "Nonsense": 1 }'}, "tests": [bad_values_stay_in_the_file]},
    {"name": "adminoff", "world": {"raw_config": '{ "OutOfLives": "Ghost", "Admin": { "AllowEdit": false, "AllowRevive": false } }'}, "tests": [admin_actions_can_be_turned_off]},
    {"name": "bans", "tests": [ban_and_back_after_the_time, ban_message_and_forever, ban_lifted_by_a_ritual, ban_cannot_be_dodged_by_relogging]},
    {"name": "forms", "tests": [exile_to_the_marked_place, exile_survives_relog_and_restart, exile_ends_with_death_and_all_lives, relog_while_dying_at_the_end,
                                 zombie_walks_until_revived_nearby, players_out_of_lives_cannot_perform_rituals, spectator_passes_walls]},
    {"name": "rules", "tests": [ritual_at_a_marked_place_only, ritual_anywhere_needs_a_choice, ritual_disabled, lives_limit, return_place_lives_and_vitals,
                                 missing_places_fall_back, changed_settings_do_not_touch_running_sentences, waiting_list_shows_everyone, custom_messages]},
]
