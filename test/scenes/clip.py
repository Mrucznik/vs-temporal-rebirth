"""From what a recorder left on the disk to a judged GIF: find the file, copy it, measure it, cut it, convert it."""
import json, os, re, shutil, subprocess, time
from datetime import datetime
from pathlib import Path

HOME = Path.home()
VIDEOS = Path(os.environ.get("TRT_VIDEOS") or HOME / "Videos")
OUT = VIDEOS / "temporal-rebirth/test"
# The game's recorder, then the desktop's (its folder has a translated name, so every folder directly in Videos is looked at).
FOLDERS = [VIDEOS / "Vintagestory", VIDEOS, *([] if os.environ.get("TRT_VIDEOS") else [HOME / "Pictures/Vintagestory"])]
KINDS = {".avi", ".webm", ".mp4", ".mkv"}
LIMIT_MB = 8


def look_in():
    found = list(FOLDERS)
    if VIDEOS.is_dir():
        found += [d for d in sorted(VIDEOS.iterdir()) if d.is_dir() and d.name != "temporal-rebirth" and d not in found]
    return found


def newest(since):
    """The newest video changed after a moment, once nobody writes to it any more."""
    files = [f for d in look_in() if d.is_dir() for f in d.iterdir() if f.suffix.lower() in KINDS and f.is_file() and f.stat().st_mtime >= since]
    if not files:
        return None
    file = max(files, key=lambda f: f.stat().st_mtime)
    for _ in range(60):
        size = file.stat().st_size
        time.sleep(1)
        if size and file.stat().st_size == size:
            break
    return file


def run(*args):
    return subprocess.run([str(a) for a in args], capture_output=True, text=True)


def born(file):
    """When the recording began: the birth of the file, else the time in its name."""
    text = run("stat", "-c", "%w", file).stdout.strip()
    m = re.match(r"(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)(\.\d+)?", text)
    if m:
        return datetime.fromisoformat(m.group(1)).timestamp() + float(m.group(2) or 0)
    m = re.search(r"(\d{4}-\d\d-\d\d)[ _](\d\d)[-:](\d\d)[-:](\d\d)", file.name)
    return datetime.fromisoformat(f"{m.group(1)} {m.group(2)}:{m.group(3)}:{m.group(4)}").timestamp() if m else None


def measure(file):
    """Width, height, seconds, frames, and how many of the frames differ from the one before."""
    probe = run("ffprobe", "-v", "error", "-select_streams", "v:0", "-count_frames", "-show_entries", "stream=width,height,avg_frame_rate,nb_read_frames:format=duration", "-of", "json", file)
    try:
        data = json.loads(probe.stdout)
        stream = data["streams"][0]
        frames = int(stream["nb_read_frames"])
        a, b = map(int, stream["avg_frame_rate"].split("/"))
        seconds = float(data["format"].get("duration") or frames * b / a)
    except (ValueError, KeyError, IndexError, ZeroDivisionError):
        return None
    if not frames or seconds <= 0:
        return None
    hashes = [line.split(",")[-1].strip() for line in run("ffmpeg", "-v", "error", "-i", file, "-map", "0:v:0", "-fps_mode", "passthrough", "-f", "framemd5", "-").stdout.splitlines() if not line.startswith("#")]
    exact = 1 + sum(a != b for a, b in zip(hashes, hashes[1:])) if hashes else 0
    # Frames that differ visibly; a lossy recorder never repeats a frame exactly.
    text = run("ffmpeg", "-hide_banner", "-i", file, "-map", "0:v:0", "-vf", "mpdecimate=hi=256:lo=128:frac=0.05", "-fps_mode", "passthrough", "-f", "null", "-").stderr
    kept = re.findall(r"frame=\s*(\d+)", text)
    return {"width": stream["width"], "height": stream["height"], "seconds": seconds, "frames": frames, "exact": exact, "visible": int(kept[-1]) if kept else 0, "mb": file.stat().st_size / 1e6}


# Tried in order until the GIF fits LIMIT_MB: frames per second, colours, denoise, dither scale. Grass and noise of the game
# make GIFs big; a light denoise (hqdn3d) keeps 15 frames a second at 800 px (the pilot: 6 MB for 11.8 s with the 4th).
VARIANTS = ((15, 256, "", 4), (15, 128, "hqdn3d=4:3:8:8,", 4), (15, 96, "hqdn3d=6:4:10:10,", 5), (15, 80, "hqdn3d=6:4:10:10,", 5),
            (15, 64, "hqdn3d=6:4:10:10,", 5), (12, 64, "hqdn3d=6:4:10:10,", 5), (10, 64, "hqdn3d=6:4:10:10,", 5))


def gif(source, target, start=0.0, seconds=None, stretch=1.0, fps=15, colors=256, denoise="", dither=4):
    """Cuts (in seconds of the video) and converts; stretch > 1 makes it slower."""
    cut = ["-ss", f"{start:.2f}"] + (["-t", f"{seconds:.2f}"] if seconds else [])
    chain = (f"setpts=PTS*{stretch:.4f},fps={fps},scale=800:-2:flags=lanczos,{denoise}split[a][b];[a]palettegen=max_colors={colors}:stats_mode=diff[p];"
             f"[b][p]paletteuse=dither=bayer:bayer_scale={dither}:diff_mode=rectangle")
    done = run("ffmpeg", "-y", "-v", "error", *cut, "-i", source, "-filter_complex", chain, target)
    return done.returncode == 0 and target.exists() and target.stat().st_size > 0


def sheet(source, target):
    seconds = (measure_seconds(source) or 1)
    run("ffmpeg", "-y", "-v", "error", "-i", source, "-vf", f"fps=12/{seconds:.2f},scale=640:-2,tile=3x4", "-frames:v", "1", target)


def measure_seconds(file):
    try:
        return float(run("ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", file).stdout)
    except ValueError:
        return None


def report(file, name, action=None, retimed=False, trim=(0.0, 0.0)):
    """Copies, measures and converts a recording. action = (begin, end) as clock times of the part to keep. True if it passes."""
    OUT.mkdir(parents=True, exist_ok=True)
    began, ended = born(file), file.stat().st_mtime
    copy = OUT / f"{name}-{datetime.now():%Y%m%d-%H%M%S}{file.suffix.lower()}"
    shutil.copy2(file, copy)
    print(f"\n  Nagranie: {file}\n  Kopia:    {copy}")
    m = measure(copy)
    if not m:
        print("  ffmpeg nie umie odczytać tego pliku (uszkodzony albo bez klatek).")
        return False
    # A recording made frame by frame (the game's recorder with a fixed step) has its own time.
    real = None if retimed else ended - began if began and ended - began > 0.4 * m["seconds"] else None
    if retimed:
        print(f"  Nagrywane klatka po klatce: trwało {ended - began:.0f} s, film ma {m['seconds']:.1f} s")
    print(f"  Rozmiar {m['mb']:.1f} MB, {m['width']}x{m['height']}, {m['seconds']:.1f} s, {m['frames']} klatek")
    stretch = 1.0
    if real:
        tempo = real / m["seconds"]
        print(f"  W rzeczywistości trwało około {real:.1f} s: tempo filmu {'dobre' if abs(tempo - 1) < 0.12 else ('za szybkie' if tempo > 1 else 'za wolne')} ({tempo:.2f})")
        # A short recording has a relatively large error, so small differences are left alone.
        stretch = tempo if real > 4 and 0.12 <= abs(tempo - 1) < 2 else 1.0
    visible, exact = m["visible"] / (real or m["seconds"]), m["exact"] / (real or m["seconds"])
    print(f"  Różnych klatek na sekundę: {visible:.1f} wyraźnie różnych, {exact:.1f} różniących się czymkolwiek")
    # A still picture has few visibly different frames; only a lossy recorder makes equal frames differ a little.
    smooth = exact if copy.suffix == ".avi" else visible
    start, seconds = 0.0, None
    if action and began and real:
        scale = m["seconds"] / real
        start = max(0.0, (action[0] - began) * scale)
        seconds = (action[1] - began) * scale - start
        if start >= m["seconds"] or seconds < 1:
            print("  Nagranie nie obejmuje sceny; GIF będzie z całości.")
            start, seconds = 0.0, None
        else:
            print(f"  Wycinam scenę: od {start:.1f} s, {min(seconds, m['seconds'] - start):.1f} s filmu")
    if trim != (0.0, 0.0) and m["seconds"] > sum(trim) + 1:
        # The chat with the recorder's own commands at both ends.
        start, seconds = trim[0], m["seconds"] - sum(trim)
        print(f"  Obcinam {trim[0]:.1f} s z początku i {trim[1]:.1f} s z końca")
    target = copy.with_suffix(".gif")
    for fps, colors, denoise, dither in VARIANTS:
        if not gif(copy, target, start, seconds, stretch, fps, colors, denoise, dither):
            print("  Nie udało się zrobić GIF-a.")
            return False
        if target.stat().st_size / 1e6 <= LIMIT_MB:
            break
    g, size = measure(target), target.stat().st_size / 1e6
    picture = copy.with_suffix(".png")
    sheet(target, picture)
    print(f"  GIF: {target}\n       {size:.1f} MB, {g['width']}x{g['height']}, {g['seconds']:.1f} s, {fps} klatek/s, {colors} kolorów{", odszumiony" if denoise else ""}, {g['exact'] / g['seconds']:.1f} różnych klatek/s")
    print(f"  Klatki do obejrzenia: {picture}")
    good = {"płynność (co najmniej 15 różnych klatek/s)": smooth >= 15, f"rozmiar GIF-a (do {LIMIT_MB} MB)": size <= LIMIT_MB}
    for text, passed in good.items():
        print(f"  {'OK ' if passed else 'ZLE'}  {text}")
    print("  Reszty (czytelność, właściwa scena, czat) skrypt nie oceni: trzeba obejrzeć.")
    return all(good.values())
