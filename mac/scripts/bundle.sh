#!/bin/sh
# Builds a universal Hanglish.app and zips it: scripts/bundle.sh <version> <output-dir>
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
