"""Builds tests/corpus/*.txt evaluation sentences that the lexicon never saw as sentences.

Korean and English sentences come from Tatoeba (CC BY 2.0 FR). Mixed sentences are
synthesized: an English word people usually leave in Latin letters, a Korean particle,
then the rest of a real Korean Tatoeba sentence.
Usage: build_corpus.py <kor_sentences.tsv.bz2> <eng_sentences.tsv.bz2> <out_dir>
"""
import bz2, random, re, sys
from pathlib import Path

kor_path, eng_path, out = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
random.seed(7)

def sentences(path):
    with bz2.open(path, "rt", encoding="utf-8") as f:
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) == 3:
                yield parts[2].strip()

korean = [s for s in sentences(kor_path) if re.fullmatch(r"[\uac00-\ud7a3 ,.?!]+", s) and 2 <= len(s.split()) <= 12]
english = [s for s in sentences(eng_path) if re.fullmatch(r"[A-Za-z ,.?!]+", s) and 3 <= len(s.split()) <= 14]
random.shuffle(korean)
english = random.sample(english, 8000)

loanwords = """github python zoom slack excel iphone youtube google chrome notion email meeting deadline server
api bug feature model data test code file update login password app windows mac linux docker kakao netflix
instagram word pdf usb wifi bluetooth gpu cpu ram ssd ai chatgpt claude figma jira git commit branch release
build deploy cloud aws report slide demo review issue team project design ui ux backend frontend web site
link browser keyboard mouse monitor laptop tablet android galaxy spotify discord twitter facebook amazon
coupang baemin toss naver daum tistory blog vlog podcast playlist""".split()
particles = "에서 을 를 이 가 은 는 에 으로 로 도 만 이랑 하고 처럼 까지".split()

mixed = []
for s in korean[:1500]:
    words = s.split()
    if len(words) < 2:
        continue
    word = random.choice(loanwords)
    mixed.append(f"{word}{random.choice(particles)} " + " ".join(words[1:]))

def write(name, items):
    (out / name).write_text("\n".join(items) + "\n", encoding="utf-8")

half = len(korean) // 2
write("ko_dev.txt", korean[1500:1500 + 2000])
write("ko_test.txt", korean[3500:3500 + 3000])
write("en_dev.txt", english[:2000])
write("en_test.txt", english[2000:5000])
write("mixed_dev.txt", mixed[:500])
write("mixed_test.txt", mixed[500:1300])
print(len(korean), len(english), len(mixed))
