#!/usr/bin/env python3
"""Turns the recorded scenes into web video for the site: docs/site/media.py [recordings folder].

Each clip becomes docs/media/<name>.mp4 (H.264, 960x540, no audio, faststart) and <name>.jpg (its poster).
The recordings are the .mkv files of test/scenes, by default in ~/Videos/temporal-rebirth/{modpage,wiki}.
"""
import subprocess, sys
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "media"
# name: (start second, longest length in seconds or None, second of the poster)
CLIPS = {
    "M1": (0, None, 9.5), "M2-zombie": (0, None, 1.5), "M2-exile": (0, None, 1.5),
    "M2-ban": (0, None, 2), "M3": (0, None, 9), "M4": (0, None, 6),
    "W1": (0, 16, 10), "W2": (0, None, 5), "W4": (0, None, 6), "W5": (0, None, 6), "W6": (0, None, 5),
    "W7": (0, None, 4), "W8": (0, None, 4), "W9": (0, None, 5), "W10": (0, None, 8), "W11": (0, 18, 12),
    "W12": (0, None, 6), "W14": (8, None, 6), "W15": (0, None, 9), "W16": (0, None, 9),
    "W17": (0, None, 9),
}


def find(root, name):
    for sub in ("modpage", "wiki"):
        for ext in (".mkv", ".mp4"):
            if (p := root / sub / (name + ext)).exists():
                return p
    raise SystemExit("missing recording: " + name)


def main():
    root = Path(sys.argv[1] if len(sys.argv) > 1 else Path.home() / "Videos/temporal-rebirth")
    OUT.mkdir(exist_ok=True)
    for name, (start, length, poster) in CLIPS.items():
        src = find(root, name)
        trim = ["-ss", str(start)] + (["-t", str(length)] if length else [])
        subprocess.run(["ffmpeg", "-v", "error", "-y", *trim, "-i", str(src), "-an", "-vf", "scale=960:540:flags=lanczos,fps=30",
                        "-c:v", "libx264", "-preset", "slow", "-crf", "28", "-pix_fmt", "yuv420p", "-profile:v", "high",
                        "-movflags", "+faststart", str(OUT / (name + ".mp4"))], check=True)
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", str(poster), "-i", str(OUT / (name + ".mp4")), "-frames:v", "1",
                        "-q:v", "5", str(OUT / (name + ".jpg"))], check=True)
        print(name, (OUT / (name + ".mp4")).stat().st_size // 1024, "KB")
    print("total", sum(p.stat().st_size for p in OUT.iterdir()) // 1024, "KB")


if __name__ == "__main__":
    main()
