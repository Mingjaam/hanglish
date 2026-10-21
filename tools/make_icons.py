"""Renders assets/*.svg to PNG with headless Edge and packs the Windows .ico and macOS iconset.

Usage: make_icons.py <msedge.exe>     (needs Pillow)
Outputs: assets/png/*.png, assets/Hanglish.ico, assets/tray.ico, mac/scripts/Hanglish.iconset/,
         mac/scripts/menubar*.png, site/icon.svg
"""
import shutil, subprocess, sys, tempfile
from pathlib import Path
from PIL import Image

edge = sys.argv[1]
root = Path(__file__).resolve().parent.parent
assets = root / "assets"
png_dir = assets / "png"
png_dir.mkdir(exist_ok=True)
work = Path(tempfile.mkdtemp())

# Every drawing is laid out on one transparent page and captured with a single Edge run,
# then cut apart; one browser launch per size is slow and can stall on a busy profile.
jobs = [("icon-small.svg" if s <= 32 else "icon.svg", s) for s in (16, 20, 24, 32, 48, 64, 128, 256, 512, 1024)]
jobs += [("menubar.svg", 18), ("menubar.svg", 36)]
x, places = 0, []
for svg, size in jobs:
    places.append((svg, size, x))
    x += size + 8
page = work / "sheet.html"
page.write_text('<!doctype html><body style="margin:0;background:transparent">' + "".join(
    f'<img src="{(assets / svg).as_uri()}" width="{size}" height="{size}" style="position:absolute;left:{left}px;top:0">'
    for svg, size, left in places), encoding="utf-8")
sheet = work / "sheet.png"
subprocess.run([edge, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
                f"--user-data-dir={work / 'profile'}", "--default-background-color=00000000",
                "--allow-file-access-from-files", f"--window-size={x},1030", f"--screenshot={sheet}", page.as_uri()],
               check=True, capture_output=True, timeout=120)
image = Image.open(sheet).convert("RGBA")

def cut(svg: str, size: int) -> Path:
    left = next(l for s_, z, l in places if s_ == svg and z == size)
    out = png_dir / f"{Path(svg).stem}-{size}.png"
    image.crop((left, 0, left + size, size)).save(out)
    return out

def render(svg: str, size: int) -> Path:
    return cut(svg, size)

def icon(size: int) -> Path:
    # Below 48 px the legends blur, so the face-only drawing is used.
    return cut("icon-small.svg" if size <= 32 else "icon.svg", size)

sizes = {s: icon(s) for s in (16, 20, 24, 32, 48, 64, 128, 256, 512, 1024)}

# Windows: application icon and tray icon (multi-size .ico files).
Image.open(sizes[256]).save(assets / "Hanglish.ico", sizes=[(s, s) for s in (16, 20, 24, 32, 48, 64, 128, 256)],
                            append_images=[Image.open(sizes[s]) for s in (16, 20, 24, 32, 48, 64, 128)])
Image.open(sizes[32]).save(assets / "tray.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32)],
                           append_images=[Image.open(sizes[s]) for s in (16, 20, 24)])

# macOS: iconset for iconutil (run in CI) and the menu bar template image.
iconset = root / "mac" / "scripts" / "Hanglish.iconset"
iconset.mkdir(parents=True, exist_ok=True)
for base in (16, 32, 128, 256, 512):
    shutil.copy(sizes[base], iconset / f"icon_{base}x{base}.png")
    shutil.copy(sizes[base * 2], iconset / f"icon_{base}x{base}@2x.png")
menubar = root / "mac" / "scripts"
shutil.copy(render("menubar.svg", 18), menubar / "menubar.png")
shutil.copy(render("menubar.svg", 36), menubar / "menubar@2x.png")

shutil.copy(assets / "icon.svg", root / "site" / "icon.svg")
print("ok")
