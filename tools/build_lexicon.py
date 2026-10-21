"""Builds src/Hanglish.Core/Resources/lexicon.tsv.gz.

Inputs:
  ko_full.txt - FrequencyWords (OpenSubtitles 2018, CC BY-SA 4.0), "word count" lines;
                words seen fewer than MIN_COUNT times are dropped as noise
  wordfreq    - Korean morpheme frequencies fill words the subtitle list lacks (e.g. 뭐)
  wordfreq    - English word frequencies (wordfreq, data CC BY-SA 4.0)
Output lines: "<lang>\t<word>\t<frequency>" where lang is k or e and frequency is a probability.
"""
import gzip, re, sys
from pathlib import Path
import wordfreq

ko_path, out_path = Path(sys.argv[1]), Path(sys.argv[2])
hangul = re.compile(r"^[\uac00-\ud7a3]+$")
english = re.compile(r"^[a-z]+$")

MIN_COUNT = 3
ko = []
for line in ko_path.read_text(encoding="utf-8").splitlines():
    parts = line.rsplit(" ", 1)
    if len(parts) == 2 and hangul.match(parts[0]) and int(parts[1]) >= MIN_COUNT:
        ko.append((parts[0], int(parts[1])))
total = sum(c for _, c in ko)
korean = {word: count / total for word, count in ko}
for word, frequency in wordfreq.get_frequency_dict("ko").items():
    if hangul.match(word) and word not in korean:
        korean[word] = frequency

with gzip.open(out_path, "wt", encoding="utf-8", newline="\n") as out:
    for word, frequency in korean.items():
        out.write(f"k\t{word}\t{frequency:.4e}\n")
    n = 0
    for word in wordfreq.top_n_list("en", 120000):
        if english.match(word):
            out.write(f"e\t{word}\t{wordfreq.word_frequency(word, 'en'):.4e}\n")
            n += 1
print(f"korean={len(korean)} english={n}")
