#!/bin/sh
# Builds a universal Hanglish.app and packs it as a drag-to-Applications disk image (and a zip):
#   scripts/bundle.sh <version> <output-dir>
set -eu
version=$1
out=$2
here=$(cd "$(dirname "$0")/.." && pwd)
sh "$here/scripts/prepare.sh"
cd "$here"
swift build -c release --arch arm64 --arch x86_64
bin=$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)

app="$out/Hanglish.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp "$bin/Hanglish" "$app/Contents/MacOS/Hanglish"
# The lexicon goes straight into Resources, where Decider looks first.
cp "$here/Sources/HanglishCore/Resources/lexicon.tsv" "$app/Contents/Resources/"
# App icon and the menu bar template image (drawn from assets/*.svg by tools/make_icons.py).
iconutil -c icns "$here/scripts/Hanglish.iconset" -o "$app/Contents/Resources/Hanglish.icns"
cp "$here/scripts/menubar.png" "$here/scripts/menubar@2x.png" "$app/Contents/Resources/"
sed "s/__VERSION__/$version/g" "$here/scripts/Info.plist" > "$app/Contents/Info.plist"
# Ad-hoc signature: enough to run locally; distribution without warnings needs a Developer ID.
codesign --force --deep --sign - "$app"
(cd "$out" && ditto -c -k --keepParent Hanglish.app "Hanglish-macos.zip")

# Disk image: the app next to an Applications shortcut on a custom background, the usual way
# Mac apps install. dmgbuild writes the window layout itself, so it works without Finder on CI.
tools="$out/.dmgtools"
python3 -m venv "$tools"
"$tools/bin/pip" install --quiet dmgbuild
tiffutil -cathidpicheck "$here/scripts/dmg-background.png" "$here/scripts/dmg-background@2x.png"     -out "$out/dmg-background.tiff"
rm -f "$out/Hanglish.dmg"
# hdiutil (used by dmgbuild) sometimes fails with "Resource busy" on CI machines; a short retry clears it.
for attempt in 1 2 3 4 5; do
    if "$tools/bin/dmgbuild" -s "$here/scripts/dmg_settings.py"         -D app="$app" -D background="$out/dmg-background.tiff" -D icon="$app/Contents/Resources/Hanglish.icns"         "한글리쉬" "$out/Hanglish.dmg"; then break; fi
    [ "$attempt" = 5 ] && exit 1
    sleep 5
done
rm -rf "$tools" "$out/dmg-background.tiff"
