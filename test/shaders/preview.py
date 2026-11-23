#!/usr/bin/env python3
"""Compiles the mod's shaders in headless Chrome and draws them over a made-up world: test/shaders/preview.py [output.png].

For looking at them while changing them, without the game. Not the game's own GLSL compiler, lighting or colors.
"""
import html, json, re, subprocess, sys, tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from harness import GAME, REPO, WORK

INCLUDES = GAME / "assets/game/shaderincludes"


def source(path):
    """The game's GLSL 3.30 as WebGL 2 takes it, with the game's includes."""
    text = path.read_text(encoding="utf8").replace("#version 330 core", "#version 300 es\nprecision highp float;\nprecision highp int;")
    text = re.sub(r"^#extension .*$", "", text, flags=re.M)
    return re.sub(r"^#include\s+(\S+)", lambda m: (INCLUDES / m[1]).read_text(encoding="utf8"), text, flags=re.M)


def chrome(page, folder, *args):
    return subprocess.run(["google-chrome", "--headless=new", "--no-first-run", "--use-angle=swiftshader", "--enable-unsafe-swiftshader", f"--user-data-dir={folder}/profile",
                           "--window-size=1312,1780", "--hide-scrollbars", "--virtual-time-budget=20000", *args, page.as_uri()], capture_output=True, text=True, timeout=180).stdout


def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else WORK / "shaders.png").resolve()
    out.parent.mkdir(parents=True, exist_ok=True)
    shaders = {p.name: source(p) for p in sorted((REPO / "assets/temporalrebirth/shaders").iterdir())}
    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as folder:
        page = Path(folder) / "page.html"
        page.write_text(Path(__file__).with_name("preview.html").read_text(encoding="utf8").replace("/*SHADERS*/{}", json.dumps(shaders)), encoding="utf8")
        answer = re.search(r'<pre id="log"[^>]*>(.*?)</pre>', chrome(page, folder, "--dump-dom"), re.S)
        text = html.unescape(answer[1]) if answer else "Chrome did not answer"
        chrome(page, folder, f"--screenshot={out}")
    print(text, out, sep="\n")
    sys.exit(text != "SHADERS OK")


if __name__ == "__main__":
    main()
