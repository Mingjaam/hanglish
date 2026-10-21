#!/bin/sh
# Builds a universal Hanglish.app and packs it as a drag-to-Applications disk image (and a zip):
#   scripts/bundle.sh <version> <output-dir>
#
# Signing is optional and driven by the environment:
#   MAC_SIGN_IDENTITY      a "Developer ID Application: ..." identity in the keychain; without it
#                          the app gets an ad-hoc signature that only runs on the building Mac
#   MAC_NOTARY_KEY_PATH,   App Store Connect API key (.p8), its key ID and issuer ID for notarization
#   MAC_NOTARY_KEY_ID,
#   MAC_NOTARY_ISSUER
#   MAC_NOTARY_APPLE_ID,   or instead: Apple ID, app-specific password and team ID
#   MAC_NOTARY_PASSWORD,   Without either set, a signed build is produced but not notarized.
#   MAC_TEAM_ID
set -eu
version=$1
out=$2
here=$(cd "$(dirname "$0")/.." && pwd)
identity=${MAC_SIGN_IDENTITY:-}
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

if [ -n "$identity" ]; then
    # Developer ID with the hardened runtime and a secure timestamp, as notarization requires.
    codesign --force --options runtime --timestamp --sign "$identity" "$app"
else
    codesign --force --deep --sign - "$app"
fi
codesign --verify --strict --verbose=2 "$app"

# Disk image: the app next to an Applications shortcut on a custom background, the usual way
# Mac apps install. dmgbuild writes the window layout itself, so it works without Finder on CI.
tools="$out/.dmgtools"
python3 -m venv "$tools"
"$tools/bin/pip" install --quiet dmgbuild
tiffutil -cathidpicheck "$here/scripts/dmg-background.png" "$here/scripts/dmg-background@2x.png" \
    -out "$out/dmg-background.tiff"
rm -f "$out/Hanglish.dmg"
# hdiutil (used by dmgbuild) sometimes fails with "Resource busy" on CI machines; a short retry clears it.
for attempt in 1 2 3 4 5; do
    if "$tools/bin/dmgbuild" -s "$here/scripts/dmg_settings.py" \
        -D app="$app" -D background="$out/dmg-background.tiff" -D icon="$app/Contents/Resources/Hanglish.icns" \
        "한글리쉬" "$out/Hanglish.dmg"; then break; fi
    [ "$attempt" = 5 ] && exit 1
    sleep 5
done
rm -rf "$tools" "$out/dmg-background.tiff"

if [ -n "$identity" ]; then
    codesign --force --timestamp --sign "$identity" "$out/Hanglish.dmg"
    notarized=""
    # Notarizing the disk image covers the app inside it; staple both so they open offline too.
    if [ -n "${MAC_NOTARY_KEY_PATH:-}" ]; then
        xcrun notarytool submit "$out/Hanglish.dmg" --key "$MAC_NOTARY_KEY_PATH" \
            --key-id "$MAC_NOTARY_KEY_ID" --issuer "$MAC_NOTARY_ISSUER" --wait --timeout 30m
        notarized=yes
    elif [ -n "${MAC_NOTARY_APPLE_ID:-}" ]; then
        xcrun notarytool submit "$out/Hanglish.dmg" --apple-id "$MAC_NOTARY_APPLE_ID" \
            --password "$MAC_NOTARY_PASSWORD" --team-id "$MAC_TEAM_ID" --wait --timeout 30m
        notarized=yes
    fi
    if [ -n "$notarized" ]; then
        xcrun stapler staple "$out/Hanglish.dmg"
        xcrun stapler staple "$app"
        spctl --assess --type open --context context:primary-signature --verbose=2 "$out/Hanglish.dmg"
        spctl --assess --type execute --verbose=2 "$app"
    fi
fi

# The zip is made last so it carries the stapled app.
rm -f "$out/Hanglish-macos.zip"
(cd "$out" && ditto -c -k --keepParent Hanglish.app "Hanglish-macos.zip")
