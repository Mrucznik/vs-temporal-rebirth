#!/usr/bin/env python3
"""Runs the scenarios: test/run.py [-k TEXT] [-j SERVERS] [--no-build]. Logs stay in ~/.cache/temporalrebirth-test/runs."""
import argparse, importlib.util, os, signal, sys, threading, time, traceback
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, str(Path(__file__).parent / "scenarios"))
import harness

# A server takes about 20 s to start, so parts much smaller than this cost more than they save.
PART_SECONDS = 60
PRINT = threading.Lock()


def say(text):
    with PRINT:
        print(text, flush=True)


def parts(module_name, group, wanted):
    """Splits a group into pieces that can run on servers of their own. Ordered groups stay whole."""
    tests = [t for t in group["tests"] if wanted in f"{module_name} {group['name']} {t.__name__}"]
    if group.get("ordered"):
        return [tests] if tests else []
    pieces, piece, seconds = [], [], 0
    for test in tests:
        cost = getattr(test, "seconds", 10)
        if piece and seconds + cost > PART_SECONDS:
            pieces.append(piece)
            piece, seconds = [], 0
        piece.append(test)
        seconds += cost
    return pieces + ([piece] if piece else [])


def run_part(name, group, tests):
    results, world = [], None
    try:
        world = harness.World(name, **group.get("world", {}))
        for test in tests:
            results.append(run(name, test, world))
    except Exception as e:
        say(traceback.format_exc())
        results.append(("FAIL", name, str(e)))
    finally:
        if world:
            world.stop()
    return results


def run(name, test, world):
    known, started = getattr(test, "known_bug", None), time.monotonic()
    try:
        test(world)
        status, note = ("FIXED", "marked as a known bug, but passes: " + known[0]) if known else ("ok", "")
    except harness.Failed as e:
        # A known bug only excuses its own failure, not any other.
        status, note = ("known", known[0]) if known and known[1] in str(e) else ("FAIL", str(e))
    except Exception as e:
        status, note = "FAIL", f"{type(e).__name__}: {e}\n{traceback.format_exc()}"
    if status != "ok":
        world.drop_bots()
    say(f"  {status:5} {name}: {test.__name__} ({time.monotonic() - started:.0f}s) {note}")
    return status, test.__name__, note


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("-k", default="", help="only scenarios whose file, group or name contains this")
    parser.add_argument("-j", type=int, default=4, help="servers at once")
    parser.add_argument("--no-build", action="store_true")
    args = parser.parse_args()
    for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(number, lambda *_: (harness.kill_everything(), os._exit(130)))
    started = time.monotonic()
    if not args.no_build:
        harness.build()
    work = []
    for path in sorted((Path(__file__).parent / "scenarios").glob("*.py")):
        spec = importlib.util.spec_from_file_location(path.stem, path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        for group in module.GROUPS:
            pieces = parts(path.stem, group, args.k)
            for i, tests in enumerate(pieces):
                work.append((group["name"] + (f"-{i + 1}" if len(pieces) > 1 else ""), group, tests))
    for lives in {group.get("world", {}).get("lives", 2) for _, group, _ in work}:
        harness.template(lives)
    # Longest first, so nothing long is left running alone at the end.
    work.sort(key=lambda w: -sum(getattr(t, "seconds", 10) for t in w[2]))
    with ThreadPoolExecutor(args.j) as pool:
        results = [r for part in pool.map(lambda w: run_part(*w), work) for r in part]
    bad = [r for r in results if r[0] in ("FAIL", "FIXED")]
    say(f"\n{sum(r[0] == 'ok' for r in results)} ok, {sum(r[0] == 'known' for r in results)} known bugs, {len(bad)} failed ({time.monotonic() - started:.0f}s)")
    sys.exit(bool(bad))


if __name__ == "__main__":
    main()
