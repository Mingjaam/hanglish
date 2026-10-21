"""Adds harder evaluation sets: single words without context, and Korean personal names.

Usage: build_hard_corpus.py <corpus_dir>   (reads the *_dev/_test sentence files written by build_corpus.py)
"""
import random, re, sys
from pathlib import Path

out = Path(sys.argv[1])
random.seed(11)

def words(name, pattern):
    seen = []
    for line in (out / name).read_text(encoding="utf-8").splitlines():
        for w in re.findall(pattern, line):
            if w not in seen:
                seen.append(w)
    random.shuffle(seen)
    return seen

surnames = "김 이 박 최 정 강 조 윤 장 임 한 오 서 신 권 황 안 송 류 전 홍 고 문 양 손 배 백 허 유 남 심 노 하 곽 성 차 주 우 구 민 진 나 지 엄 채 원 천 방 공 현 함 변 염 여 추 도 소 석 선 설 마 길 연 위 표 명 기 반 왕 금 옥 육 인 맹 제 모 탁 국".split()
given = "민 재 서 준 지 훈 현 우 수 연 영 진 호 은 하 윤 정 아 예 원 희 성 혁 태 동 경 주 빈 나 린 승 채 다 유 시 미 혜 석 철 규 범 찬 율 온 결 솔 별 람 슬".split()
names = list({random.choice(surnames) + random.choice(given) + random.choice(given) for _ in range(3000)})
random.shuffle(names)

for split, (start, end) in {"dev": (0, 400), "test": (400, 1200)}.items():
    ko = [w for w in words(f"ko_{split}.txt", r"[\uac00-\ud7a3]+")][: end - start]
    en = [w for w in words(f"en_{split}.txt", r"[A-Za-z]+")][: end - start]
    (out / f"word_ko_{split}.txt").write_text("\n".join(ko) + "\n", encoding="utf-8")
    (out / f"word_en_{split}.txt").write_text("\n".join(en) + "\n", encoding="utf-8")
    (out / f"name_{split}.txt").write_text("\n".join(names[start:end]) + "\n", encoding="utf-8")
print("ok")
