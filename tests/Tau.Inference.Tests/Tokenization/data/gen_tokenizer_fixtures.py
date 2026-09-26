"""Generate tokeniser parity fixtures with the real Hugging Face stack.

Run from the repo root (a throwaway environment; the sidecar project is not touched):

    uv run --no-project --python 3.12 --with transformers --with tokenizers \
        python tests/Tau.Inference.Tests/Tokenization/data/gen_tokenizer_fixtures.py [models_dir]

models_dir defaults to $TAU_MODELS_DIR or <repo>/models; the multilingual tokenizer (34 MB) is read from
<models_dir>/src/laya-multilingual/tokenizer/, the other two from the committed copies next to this script.

Expected ids are exactly what the reference runtimes compute:
    AutoTokenizer.from_pretrained(dir)(text, add_special_tokens=False)["input_ids"]
Each file's first line is a header with the library versions, the tokenizer.json sha256 and the date.
"""
import datetime
import hashlib
import json
import os
import random
import sys

import tokenizers
import transformers
from transformers import AutoTokenizer

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", "..", "..", ".."))
MODELS = sys.argv[1] if len(sys.argv) > 1 else os.environ.get("TAU_MODELS_DIR") or os.path.join(REPO, "models")

TARGETS = {
    "laya-en": (os.path.join(HERE, "laya-en"), ["[CLS]", "[SEP]", "[MASK]", "[PAD]", "[UNK]", "<|padding|>", "[unused0]", "|||IP_ADDRESS|||"]),
    "von-1.2.0": (os.path.join(HERE, "von-1.2.0"), ["[CLS]", "[SEP]", "[MASK]", "[PAD]", "[UNK]", "<|padding|>", "[unused5]"]),
    "laya-multilingual": (os.path.join(MODELS, "src", "laya-multilingual", "tokenizer"),
                          ["<bos>", "<eos>", "<mask>", "<pad>", "<unk>", "<start_of_turn>", "<end_of_turn>", "<unused0>", "[@BOS@]", "<2mass>"]),
}

rng = random.Random(20260927)

BASE = [
    "", " ", "  ", "a", "A", "hello", "Hello world", "hello world!", "Hello, World! How are you?",
    "The quick brown fox jumps over the lazy dog.", "don't won't can't I'm you're", "e-mail user@example.com",
    "https://example.com/path?q=1&r=two#frag", "C:\\Users\\name\\file.txt", "snake_case camelCase PascalCase kebab-case",
    "0", "7", "42", "123", "1234", "12345", "123456789", "3.14159", "-0.5", "1e-05", "1,000,000", "$1,234.56",
    "2026-09-27T12:34:56Z", "+44 20 7946 0958", "v1.2.3", "0x1F600", "100%", "#hashtag @mention",
    "tabs\there\tand\tthere", "new\nlines\n\nand\r\nwindows", "trailing space ", " leading space",
    "multiple    internal     spaces", "                                   (35 spaces)", "\t\t\t\t\t\t\t\t",
    "\n\n\n\n", "mixed \t \n \r whitespace", "\u00a0non-breaking\u00a0space", "\u3000ideographic space",
    "caf\u00e9 na\u00efve r\u00e9sum\u00e9", "cafe\u0301 (combining acute)", "\u00c5ngstr\u00f6m vs A\u030angstro\u0308m",
    "Stra\u00dfe", "\u0130stanbul \u0131", "\ufb01ligature", "\u2126 ohm sign", "\u212b angstrom sign",
    "\u4e2d\u6587\u6d4b\u8bd5\uff0c\u8fd9\u662f\u4e00\u4e2a\u53e5\u5b50\u3002", "\u65e5\u672c\u8a9e\u306e\u30c6\u30ad\u30b9\u30c8\u3067\u3059\u3002",
    "\ud55c\uad6d\uc5b4 \ubb38\uc7a5\uc785\ub2c8\ub2e4.", "\u0627\u0644\u0644\u063a\u0629 \u0627\u0644\u0639\u0631\u0628\u064a\u0629 \u062c\u0645\u064a\u0644\u0629",
    "\u05e2\u05d1\u05e8\u05d9\u05ea \u05d6\u05d5 \u05e9\u05e4\u05d4", "\u0939\u093f\u0928\u094d\u0926\u0940 \u092d\u093e\u0937\u093e",
    "\u0e20\u0e32\u0e29\u0e32\u0e44\u0e17\u0e22", "\u1797\u17b6\u179f\u17b6\u1781\u17d2\u1798\u17c2\u179a",
    "\u0420\u0443\u0441\u0441\u043a\u0438\u0439 \u044f\u0437\u044b\u043a", "\u0395\u03bb\u03bb\u03b7\u03bd\u03b9\u03ba\u03ac",
    "\u10e5\u10d0\u10e0\u10d7\u10e3\u10da\u10d8", "\u0540\u0561\u0575\u0565\u0580\u0565\u0576", "Ti\u1ebfng Vi\u1ec7t c\u00f3 d\u1ea5u",
    "mixed English \u05e2\u05d1\u05e8\u05d9\u05ea and \u0627\u0644\u0639\u0631\u0628\u064a\u0629 123",
    "\U0001f600\U0001f603\U0001f604", "\U0001f468\u200d\U0001f469\u200d\U0001f467 family", "\U0001f1ec\U0001f1e7 flag",
    "\u2764\ufe0f heart", "\U0001d400\U0001d401 math", "zero\u200bwidth\u200cjoin\u200dners", "\ufeffBOM at start",
    "\u202eRLO override", "control \x00 \x01 \x1f \x7f chars", "\x85 next line", "\u2028line sep\u2029para sep",
    "\ue000 private use", "\U0010fffd", "{\"a\": 1, \"b\": [true, false, null]}", "{'a': 1, 'b': True, 'c': None}",
    "name: Ana\ntier: gold\nopen_tickets: 3", "choice question: Which team should handle this ticket?",
    "score question: How urgent is this ticket?", "noul question: Is the customer asking for a refund?",
    "!!!???...,,,;;;:::", "((([[[{{{}}}]]])))", "<html><body>Hi</body></html>", "SELECT * FROM t WHERE x = 'y';",
    "def f(x):\n    return x ** 2\n", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    "ab" * 40, "The\u2014em dash\u2013en dash\u2026ellipsis \u201cquotes\u201d \u2018single\u2019",
    "\u00bd \u00bc \u2153 \u00b2 \u00b3 \u2082", "\u2160 \u2161 \u2162 roman", "\uff21\uff22\uff23 fullwidth",
]


def special_cases(specials):
    out = []
    for s in specials:
        out += [s, f"{s}", f"a{s}b", f"a {s} b", f" {s} ", f"{s}{s}", f"{s} {s}", f"text before {s}", f"{s} text after",
                f"x{s}\n{s}y", s.lower(), s.upper()]
    out.append(" ".join(specials))
    out.append("".join(specials))
    return out


def random_texts(n):
    pools = [
        [chr(c) for c in range(0x20, 0x7F)],
        [chr(c) for c in range(0xA0, 0x250)],
        [chr(c) for c in range(0x400, 0x500)],
        [chr(c) for c in range(0x4E00, 0x4F00)],
        [chr(c) for c in range(0x600, 0x6FF)],
        [" ", " ", "  ", "\n", "\t"],
        [chr(c) for c in range(0x1F300, 0x1F650)],
    ]
    out = []
    for _ in range(n):
        length = rng.randint(1, 120)
        out.append("".join(rng.choice(rng.choice(pools)) for _ in range(length)))
    return out


def long_texts():
    words = "the ticket was escalated because the customer reported a billing error twice and asked for a refund".split()
    long_en = " ".join(rng.choice(words) for _ in range(12000))  # well past 8192 tokens
    long_mixed = ("Mixed \u4e2d\u6587 and \u0627\u0644\u0639\u0631\u0628\u064a\u0629 text 12345. " * 900)
    return [long_en, long_mixed, "x" * 20000, json.dumps({"k%d" % i: i * 1.5 for i in range(1500)})]


for name, (tok_dir, specials) in TARGETS.items():
    tj = os.path.join(tok_dir, "tokenizer.json")
    if not os.path.exists(tj):
        sys.exit(f"{tj} missing: run scripts/fetch-models.ps1 or pass the models dir")
    tok = AutoTokenizer.from_pretrained(tok_dir)
    raw = tokenizers.Tokenizer.from_file(tj)
    raw.no_truncation()
    raw.no_padding()

    texts = []
    for t in BASE + special_cases(specials) + random_texts(60) + long_texts():
        if t not in texts:
            texts.append(t)

    records, raw_diffs = [], 0
    for t in texts:
        ids = tok(t, add_special_tokens=False)["input_ids"]
        if raw.encode(t, add_special_tokens=False).ids != ids:
            raw_diffs += 1
        records.append({"text": t, "ids": ids})

    special = {k: getattr(tok, k) for k in ("cls_token", "sep_token", "mask_token", "pad_token")}
    special_ids = {k.replace("_token", "_id"): tok.convert_tokens_to_ids(v) for k, v in special.items()}
    header = {
        "header": True,
        "generator": "tests/Tau.Inference.Tests/Tokenization/data/gen_tokenizer_fixtures.py",
        "model": name,
        "python": sys.version.split()[0],
        "transformers": transformers.__version__,
        "tokenizers": tokenizers.__version__,
        "tokenizer_json_sha256": hashlib.sha256(open(tj, "rb").read()).hexdigest(),
        "tokenizer_class": type(tok).__name__,
        "special_tokens": special,
        "special_ids": special_ids,
        "cases_differing_from_raw_tokenizers": raw_diffs,
        "date": datetime.date.today().isoformat(),
    }
    with open(os.path.join(HERE, f"{name}.expected.jsonl"), "w", encoding="ascii", newline="\n") as f:
        f.write(json.dumps(header) + "\n")
        for r in records:
            f.write(json.dumps(r) + "\n")
    print(f"{name}: {len(records)} texts, {raw_diffs} differ between transformers and raw tokenizers; {special_ids}")
