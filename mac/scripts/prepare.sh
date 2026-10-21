#!/bin/sh
# Copies the shared lexicon (built for Windows as a .gz) into the Swift package as plain TSV.
set -eu
root=$(cd "$(dirname "$0")/../.." && pwd)
mkdir -p "$root/mac/Sources/HanglishCore/Resources"
gunzip -c "$root/src/Hanglish.Core/Resources/lexicon.tsv.gz" > "$root/mac/Sources/HanglishCore/Resources/lexicon.tsv"
