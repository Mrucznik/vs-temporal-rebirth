#!/usr/bin/env python3
"""Writes an animated page with the mod's echo and wisp shaders over a blocky world: test/shaders/live.py [output.html].

Open it in a browser and play with the light and distance. Run it again after changing a shader, then reload the page.
Not the game's compiler, lighting or colors; for the real thing record a scene.
"""
import json, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from preview import source, REPO, WORK


def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else WORK / "shaders-live.html").resolve()
    out.parent.mkdir(parents=True, exist_ok=True)
    shaders = {p.name: source(p) for p in sorted((REPO / "assets/temporalrebirth/shaders").iterdir())}
    page = Path(__file__).with_name("live.html").read_text(encoding="utf8").replace("/*SHADERS*/{}", json.dumps(shaders))
    out.write_text(page, encoding="utf8")
    print(out)


if __name__ == "__main__":
    main()
