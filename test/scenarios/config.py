"""The config file, presets, commands, privileges, messages."""
import json, time
from harness import check
from steps import ghost, node, read_config, settings


def fresh_install_is_the_ghost_preset(w):
    saved = read_config(w)
    check(saved["Ritual"]["Where"] == "NearPlayer" and saved["Ritual"]["ReviveAt"] == "InPlace", f"rituals next to the ghost: {saved['Ritual']}")
    check(w.config_path.read_text(encoding="utf8") == node("process.stdout.write(T.toFile(T.applyPreset('Ghost'),true))"), "the configurator's Ghost preset")


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
    for preset in ("Ghost", "Rift", "Undead", "Exile", "Timeout", "Hardcore"):
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


def import_lists_what_changed(w):
    settings(w, **{"Lives.Max": 5, "Messages.Ghost": "Witaj"})
    code = node("const s=T.applyPreset('Ghost');s['Place.Radius']=40;process.stdout.write(T.encode('Ghost',s))")
    admin, = w.join("Ilc")
    w.console("/player Ilc role admin")
    time.sleep(0.5)
    since = admin.say("/rebirth import " + code)
    line = admin.heard("Settings imported.*", since)
    for wanted in ("Place.Radius: 24 → 40", "Lives.Max: 5 → no limit", "Messages.Ghost: custom → the mod's text", "temporalrebirth.json.bak"):
        check(wanted in line, f"missing {wanted!r} in {line}")
    check("Return.At" not in line and "Messages.Ban" not in line, f"only what changed: {line}")
    bak = (w.config_path.parent / (w.config_path.name + ".bak")).read_text(encoding="utf8")
    check('"Witaj"' in bak, "the previous file is kept")
    admin.ask("/rebirth import " + code, "Nothing changed")
    check((w.config_path.parent / (w.config_path.name + ".bak")).read_text(encoding="utf8") == bak, "and not replaced by an import without changes")
    settings(w)
    admin.quit()


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


def broken_config_is_not_overwritten(w):
    check(w.log_has("temporalrebirth.json is invalid"), "the error is logged")
    check('"GhostTetherRadius": 99' in w.config_path.read_text(encoding="utf8"), "the admin's settings are still in the file")


def old_config_is_migrated(w):
    saved = read_config(w)
    check(saved["Lives"]["PerGear"] == 4, "the admin's value is kept")
    check(saved["Time"]["Duration"] == "next day 06:00" and saved["Ritual"]["Range"] == 8, "the rest is filled in")
    check(saved["Ghost"]["Visible"] == "Nobody", "an update does not change what players see")
    check(saved["Ritual"]["Where"] == "Rift" and saved["Ritual"]["ReviveAt"] == "Caster", f"rituals stay at rifts, next to the caster: {saved['Ritual']}")
    check(json.loads((w.config_path.parent / (w.config_path.name + ".bak")).read_text(encoding="utf8")) == {"LivesPerGear": 4, "ReturnMode": "NextDay"}, "the old file is kept as .bak")


GROUPS = [
    {"name": "fresh", "world": {'bare': True}, "tests": [
        fresh_install_is_the_ghost_preset,
    ]},
    {"name": "config", "tests": [
        new_file_matches_the_configurator,
        configurator_code_is_imported,
        every_preset_imports_like_it_applies,
        config_commands,
        rights,
        custom_messages,
        import_lists_what_changed,
    ]},
    {"name": "brokenedit", "world": {'raw_config': '{ oops'}, "tests": [
        broken_file_survives_an_edit,
    ]},
    {"name": "applypreset", "world": {'raw_config': '{ "ApplyPreset": "hardcore", "OutOfLives": "Ghost", "Lives": { "PerGear": 3 } }'}, "tests": [
        apply_preset_in_file,
    ]},
    {"name": "badvalues", "world": {'raw_config': '{ "OutOfLives": "Ghost", "Place": { "Radius": "dużo" }, "Nonsense": 1 }'}, "tests": [
        bad_values_stay_in_the_file,
    ]},
    {"name": "adminoff", "world": {'raw_config': '{ "OutOfLives": "Ghost", "Admin": { "AllowEdit": false, "AllowRevive": false } }'}, "tests": [
        admin_actions_can_be_turned_off,
    ]},
    {"name": "oldbrokenconfig", "world": {'raw_config': '{ "GhostTetherRadius": 99, oops'}, "tests": [
        broken_config_is_not_overwritten,
    ]},
    {"name": "oldconfig", "world": {'config': {'LivesPerGear': 4, 'ReturnMode': 'NextDay'}}, "tests": [
        old_config_is_migrated,
    ]},
]
