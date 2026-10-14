"""Builds assets/english-respelling.txt.gz: English words with a Turkish-letter respelling, for Turkish voices.

Sources (fetched into cache/ by fetch.ps1):
  cmudict.dict           CMU Pronouncing Dictionary (Carnegie Mellon University, BSD-style license, see LICENSE-cmudict)
  *.dict (Zemberek)      Turkish lexicon of zemberek-nlp (Apache-2.0): every Turkish root, proper name and place name
                         is EXCLUDED, so "adam", "para", "model", "Ankara" or "Can" are never respelled as English.

Each ARPAbet phoneme maps to the letters a Turkish speaker would write for that sound; the result is the usual
"Turkish respelling" of English loanwords (download -> daunlod, server -> sörvır, update -> apdeyt).
"""
import gzip
import pathlib
import re

HERE = pathlib.Path(__file__).resolve().parent
CACHE = HERE / "cache"
OUT = HERE.parents[1] / "assets" / "english-respelling.txt.gz"

PHONES = {
    "AA": "a", "AE": "e", "AO": "o", "AW": "au", "AY": "ay", "B": "b", "CH": "ç", "D": "d", "DH": "d", "EH": "e",
    "EY": "ey", "F": "f", "G": "g", "HH": "h", "IH": "i", "IY": "i", "JH": "c", "K": "k", "L": "l", "M": "m", "N": "n",
    "NG": "ng", "OW": "o", "OY": "oy", "P": "p", "R": "r", "S": "s", "SH": "ş", "T": "t", "TH": "t", "UH": "u", "UW": "u",
    "V": "v", "W": "v", "Y": "y", "Z": "z", "ZH": "j",
}
TR_LOWER = str.maketrans("IİÂÎÛ", "ıiaiu")


def turkish_lower(word: str) -> str:
    return word.translate(TR_LOWER).lower()


def turkish_words() -> set:
    words = set()
    for path in CACHE.glob("*.dict"):
        if path.name == "cmudict.dict":
            continue
        for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
            token = re.split(r"[\s\[]", line.strip(), 1)[0]
            if token:
                words.add(turkish_lower(token))
                words.add(turkish_lower(token).replace("ı", "i"))   # ASCII typing of ı
    return words


def respell(phones: list) -> str:
    out = []
    last = len(phones) - 1
    for i, p in enumerate(phones):
        base, stress = p.rstrip("012"), p[-1] if p[-1] in "012" else ""
        if base == "AH":
            out.append("a" if stress == "1" or i == 0 or i == last else "ı")
        elif base == "ER":
            out.append("ör" if stress == "1" else "ır")
        else:
            out.append(PHONES[base])
    return "".join(out)


def main() -> None:
    turkish = turkish_words()
    entries = {}
    skipped_turkish = skipped_same = 0
    for line in (CACHE / "cmudict.dict").read_text(encoding="utf-8", errors="ignore").splitlines():
        if not line or line.startswith(";;;"):
            continue
        word, *phones = line.split()
        if "(" in word or not re.fullmatch(r"[a-z]{4,}", word):
            continue
        if word in turkish:
            skipped_turkish += 1
            continue
        try:
            spoken = respell(phones)
        except KeyError:
            continue
        if spoken == word:
            skipped_same += 1
            continue
        entries[word] = spoken
    OUT.parent.mkdir(parents=True, exist_ok=True)
    with gzip.open(OUT, "wt", encoding="utf-8", newline="\n") as f:
        for word in sorted(entries):
            f.write(f"{word}\t{entries[word]}\n")
    print(f"{len(entries)} entries -> {OUT} ({OUT.stat().st_size // 1024} KB); skipped: Turkish {skipped_turkish}, unchanged {skipped_same}")
    for w in ("download", "server", "keyboard", "spreadsheet", "update", "button", "google", "data", "about", "network",
              "mouse", "firewall", "bluetooth", "windows", "wireless", "deadline", "meeting", "feature", "release", "cache"):
        print(f"  {w:12} -> {entries.get(w, '(yok)')}")
    for w in ("adam", "para", "ankara", "model", "anne", "kale", "bile", "normal", "final", "site"):
        print(f"  {w:12} -> {'HARİÇ (Türkçe)' if w not in entries else 'DİKKAT: ' + entries[w]}")


if __name__ == "__main__":
    main()
