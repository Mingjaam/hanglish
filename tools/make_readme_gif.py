"""Renders the website demos frame by frame with headless Edge and joins them into
docs/demo-windows.gif and docs/demo-macos.gif.

Usage: make_readme_gif.py <msedge.exe> <ffmpeg.exe>
"""
import json, subprocess, sys, tempfile
from pathlib import Path

edge, ffmpeg = sys.argv[1], sys.argv[2]
root = Path(__file__).resolve().parent.parent
text = (root / "site" / "frames.js").read_text(encoding="utf-8")
scenes = json.loads(text[text.index("["):text.rstrip().rindex(";")])
work = Path(tempfile.mkdtemp())
page = (root / "site" / "index.html").as_uri()


def build(os_name: str, out: Path) -> None:
    entries = []
    for si, scene in enumerate(scenes):
        for st, step in enumerate(scene["steps"]):
            png = work / f"{os_name}_{si:02d}_{st:03d}.png"
            subprocess.run([edge, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                            f"--user-data-dir={work / 'profile'}", "--window-size=760,690",
                            "--virtual-time-budget=1500", f"--screenshot={png}", f"{page}?shot={os_name}.{si}.{st}"],
                           check=True, capture_output=True, timeout=60)
            last = st == len(scene["steps"]) - 1
            hold = 1.6 if last else (0.5 if step["key"] in ("Space", "Enter", "Ctrl") else 0.16)
            entries.append((png, hold))
    listing = work / f"{os_name}.txt"
    lines = [f"file '{p.as_posix()}'\nduration {d}\n" for p, d in entries]
    lines.append(f"file '{entries[-1][0].as_posix()}'\n")   # concat needs the last frame twice
    listing.write_text("".join(lines), encoding="utf-8")
    palette = "fps=12,scale=600:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=96[p];[b][p]paletteuse=dither=bayer:bayer_scale=4"
    subprocess.run([ffmpeg, "-y", "-f", "concat", "-safe", "0", "-i", str(listing), "-vf", palette, "-loop", "0", str(out)],
                   check=True, capture_output=True)
    print(out.name, out.stat().st_size, flush=True)


docs = root / "docs"
docs.mkdir(exist_ok=True)
build("win", docs / "demo-windows.gif")
build("mac", docs / "demo-macos.gif")
