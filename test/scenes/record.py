#!/usr/bin/env python3
"""Records with a real client, one command, Polish steps:  test/scenes/record.py TRIAL   or   PILOT

TRIAL: 5 s of walking, to see whether recording works at all. PILOT: last death, wisp, echo, ritual, back.
Ctrl+C stops server and bots. --check plays it without a client, with a bot and a made-up video.
"""
import argparse, hashlib, os, signal, subprocess, sys, time
from pathlib import Path

import clip
from stage import PORT, Camera, Failed, Stage, harness
from play import meadow, others_running, pilot_action, pilot_people, pilot_place

CLIENT = Path.home() / ".var/app/at.vintagestory.VintageStory/trt-client"
COMMAND = (f"flatpak run --filesystem=xdg-videos/Vintagestory:create at.vintagestory.VintageStory "
           f"--dataPath {CLIENT} --connect 127.0.0.1:{PORT}")
TRIGGER_SLOT = 2  # key 3


def say(text=""):
    print(text, flush=True)


def step(text):
    say("\n>> " + text)


def enter(check, text="Enter, gdy gotowe"):
    if check:
        return ""
    return input(f"   [{text}] ").strip().lower()


def same_mod():
    """The client needs the very build the server runs; only our own zip in its Mods folder is looked at."""
    ours = harness.newest(harness.REPO / "dist", "temporalrebirth")
    theirs = CLIENT / "Mods" / ours.name
    digest = lambda f: hashlib.sha256(f.read_bytes()).hexdigest()
    if not theirs.is_file() or digest(theirs) != digest(ours):
        say(f"UWAGA: klient nie ma tej samej wersji moda co serwer. Skopiuj:\n  cp {ours} {theirs}")


def join(s):
    if s.check:
        s.wait_for_camera()
        return
    step("Serwer działa. W DRUGIM terminalu wklej:")
    say("\n   " + COMMAND + "\n")
    say("   Okno 1280x720, język English, skala interfejsu 1.0. Czekam, aż wejdziesz do świata...")
    while not (names := s.w.trt("players")):
        time.sleep(1)
    s.camera = Camera(s.w, names[0], True)
    step("Jesteś w świecie. Jeśli gra pyta o postać, zatwierdź ją.")
    enter(False)


def in_game(s, seconds=int(os.environ.get("TRT_WAIT", "90"))):
    """Waits for key 3 in the game, so nobody has to leave the window; goes on by itself after a while."""
    if s.check:
        return
    end = time.monotonic() + seconds
    shown = None
    while (left := int(end - time.monotonic())) > 0:
        if s.w.trt("view " + s.camera.name)["slot"] == TRIGGER_SLOT:
            say("\r   Klawisz 3 wciśnięty, zaczynam.            ")
            return
        if left // 5 != shown:
            shown = left // 5
            print(f"\r   Czekam na klawisz 3 w grze, najwyżej {left:3} s  ", end="", flush=True)
        time.sleep(0.4)
    say("\r   Nie doczekałem się klawisza 3, zaczynam mimo to.")


def recorder_steps(gnome):
    if gnome:
        say("   1. W grze wciśnij Ctrl+Alt+Shift+R, zaznacz okno gry i kliknij czerwony przycisk nagrywania.")
        say("   2. Kliknij w okno gry. Wciśnij T, wpisz .clearchat, Enter, potem Esc, jeśli czat nie zniknął.")
    else:
        say("   1. W grze: T, wpisz .vrec start, Enter. (Musi odpisać: Ok, Video Recording now.)")
        say("   2. T, wpisz .clearchat, Enter, potem Esc, jeśli czat nie zniknął.")


def stop_steps(gnome):
    say("   W grze: T, wpisz .vrec stop, Enter." if not gnome else "   Zatrzymaj nagrywanie czerwonym przyciskiem na górnym pasku.")


def tell_in_game(s, text):
    s.w.console("/announce " + text)


def make_believe():
    """A recorder for --check that writes like the game's: MJPEG in AVI, named by the time, until it is told to stop."""
    folder = clip.VIDEOS / "Vintagestory"
    folder.mkdir(parents=True, exist_ok=True)
    file = folder / f"{time.strftime('%Y-%m-%d_%H-%M-%S')}.avi"
    return subprocess.Popen(["ffmpeg", "-y", "-v", "error", "-re", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30", "-c:v", "mjpeg", "-q:v", "5", str(file)], stdin=subprocess.PIPE)


def take(s, name, gnome):
    """One recording. Returns whether to end."""
    began = time.time()
    action = None
    recorder = make_believe() if s.check else None
    if name == "TRIAL":
        s.time("12:00")
        step("PRÓBA: 5 sekund spokojnego chodzenia. Zrób po kolei:")
        recorder_steps(gnome)
        say("   3. Wciśnij 2, a potem 3: to znak startu. Przez 5 s chodź i rozglądaj się.")
        say("   4. Gdy na czacie w grze pojawi się napis STOP, zatrzymaj nagrywanie:")
        stop_steps(gnome)
        in_game(s)
        a = time.time()
        for left in range(5, 0, -1):
            print(f"\r   Chodź: {left} s ", end="", flush=True)
            time.sleep(1)
        action = (a, time.time())
        tell_in_game(s, "STOP: stop the recording now.")
        say("\r   Koniec, zatrzymaj nagrywanie.")
    else:
        step("Ustaw kamerę: patrz prosto przed siebie na otwartą przestrzeń. Tam stanie Funia.")
        say("   Wciśnij 2 (pusta ręka) i nie ruszaj się z miejsca.")
        enter(s.check)
        pilot_place(s)
        if s.check:
            say(f"   {s.w.trt('view ' + s.camera.name)}")
        step("Funia stoi 5 bloków przed tobą, a w pierwszym slocie masz zębatkę temporalną. Przeczytaj całość, potem zrób:")
        recorder_steps(gnome)
        say("   3. Wciśnij 3: to znak startu. Po 2 s Funia ginie.")
        say("   4. Po chwili jest ognikiem. Patrz na niego 2 s.")
        say("   5. Wciśnij 1: zębatka w ręce, widać echo z imieniem. Patrz 2 s.")
        say("   6. Spójrz na ziemię tuż przed echem i TRZYMAJ prawy przycisk myszy około 4 s, aż Funia wróci.")
        say("   7. Popatrz na nią 2 s. Gdy na czacie pojawi się napis STOP, zatrzymaj nagrywanie:")
        stop_steps(gnome)
        in_game(s)
        time.sleep(2)
        marks = []
        try:
            pilot_action(s, at_death=lambda: marks.append(time.time() - 1))
        except Failed as e:
            # A failed ritual is no reason to end: the next take begins again.
            say(f"   Scena się nie dokończyła: {e}")
            tell_in_game(s, "STOP: the scene failed.")
            back(s)
        else:
            time.sleep(2.2)
            action = (marks[0], time.time())
            tell_in_game(s, "STOP: stop the recording now.")
            say("   Koniec, zatrzymaj nagrywanie.")
    if s.check:
        recorder.communicate(b"q")
    answer = enter(s.check, "Enter, gdy nagrywanie zatrzymane; n = nagrywanie nie zadziałało")
    file = None if answer == "n" else clip.newest(began)
    if file is None:
        if answer != "n":
            say("   Nie znalazłem nowego filmu. Szukałem w:\n" + "\n".join(f"     {d}" for d in clip.look_in()))
        say("\n   Plan awaryjny: nagrywarka GNOME (Ctrl+Alt+Shift+R, zapisuje w folderze Filmy/Videos)." if not gnome
            else "   Nagrywarka GNOME też nic nie zostawiła.")
        again = enter(s.check, "Enter = jeszcze raz nagrywarką GNOME, v = jeszcze raz przez .vrec, k = koniec")
        return None if again == "k" or s.check else again != "v"
    clip.report(file, name, action)
    again = enter(s.check, "Enter = koniec, j = jeszcze raz tak samo, g = jeszcze raz nagrywarką GNOME")
    return True if again == "g" else gnome if again == "j" else None


def back(s):
    """Funia alive again after a failed scene."""
    funia = s.bots["Funia"]
    s.w.console("/rebirth revive Funia")
    s.w.console("/rebirth setlives Funia 3")
    funia.until(mode="Survival", alive=True, timeout=30)


def interrupt(*_):
    raise KeyboardInterrupt


def main():
    options = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    options.add_argument("name", choices=["TRIAL", "PILOT"])
    options.add_argument("--check", action="store_true")
    args = options.parse_args()
    if others_running():
        sys.exit("Działa już jakiś serwer testowy albo test/run.py. Zatrzymaj go i spróbuj jeszcze raz.")
    for sig in (signal.SIGTERM, signal.SIGHUP):
        signal.signal(sig, interrupt)
    if not args.check:
        same_mod()
    stage = None
    try:
        step("Uruchamiam serwer (kilkanaście sekund, za pierwszym razem około minuty)...")
        stage = Stage(args.check)
        join(stage)
        if meadow(stage)["found"]:
            say("   Przeniosłem cię na płaskie miejsce.")
        if args.name == "PILOT":
            pilot_people(stage)
        gnome = False
        while gnome is not None:
            gnome = take(stage, args.name, gnome)
        return 0
    except KeyboardInterrupt:
        say("\nPrzerwane.")
        return 130
    except Failed as e:
        say(f"\nNie udało się: {e}")
        return 1
    finally:
        for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            signal.signal(sig, signal.SIG_IGN)
        say("Zatrzymuję serwer i boty (do minuty)...")
        if stage:
            stage.stop()
        for process in harness.RUNNING:
            process.proc.kill()
            process.proc.wait()
        say("Zatrzymane. Grę możesz zamknąć.")


if __name__ == "__main__":
    sys.exit(main())
