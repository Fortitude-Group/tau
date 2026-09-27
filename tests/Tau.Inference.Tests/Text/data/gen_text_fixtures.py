"""Generate the PyJson / PyRepr parity fixtures from real CPython.

Run from the repo root with the reference interpreter (Python 3.12, the sidecar's pinned version):

    py -3.12 tests/Tau.Inference.Tests/Text/data/gen_text_fixtures.py

Writes, next to this script:
  pyjson.jsonl  - {"raw", "dumps", "dumps_state"}   json.dumps(json.loads(raw), ensure_ascii=False)
                                                     and Laya's serialize_state / render_criterion
  pyrepr.jsonl  - {"raw", "str", "von_state"}       str(json.loads(raw)) and Von's _format_state

The first line of each file is a header recording the interpreter and date. Every other line is one
case. `raw` is the JSON text both sides parse, so the C# side sees exactly the input Python saw.
The random parts use a fixed seed, so re-running produces the same files (apart from the date).
"""
import datetime
import json
import os
import platform
import random
import struct
import sys
import unicodedata

HERE = os.path.dirname(os.path.abspath(__file__))


def serialize_state(state):  # laya==0.3.20 laya.common.serialize_state
    if isinstance(state, str):
        return state
    return json.dumps(state, ensure_ascii=False)


def render_criterion(value):  # laya==0.3.20 laya.common.render_criterion
    if isinstance(value, str):
        return value
    return json.dumps(value, ensure_ascii=False, separators=(", ", ": "), default=str)


def format_state(state):  # von-sdk==1.2.3 von.backends.option_marker_backend._format_state
    if isinstance(state, str):
        return state
    if isinstance(state, dict):
        parts = []
        for k, v in state.items():
            parts.append(f"{k}: {v}")
        return "\n".join(parts)
    return str(state)


def j(value):
    """Raw JSON text for a Python value (ASCII-escaped, so the raw text itself exercises \\u escapes)."""
    return json.dumps(value)


def j_raw(value):
    """Raw JSON text with non-ASCII characters left literal."""
    return json.dumps(value, ensure_ascii=False)


rng = random.Random(20260927)
raws = []

# --- numbers written by hand, raw text chosen to hit int/float classification and repr rules
number_texts = [
    "0", "-0", "1", "-1", "7", "42", "-42", "1234567890", "9007199254740993", "-9223372036854775809",
    "18446744073709551616", str(2 ** 70), str(-(2 ** 70)), str(10 ** 40), "123456789012345678901234567890",
    "0.0", "-0.0", "1.0", "-1.0", "0.1", "0.2", "0.30000000000000004", "1.5", "2.5", "100.0", "1e2", "1E2",
    "1e+2", "1E-2", "1e0", "0e0", "-0e0", "0e10", "1.0e-7", "0.1e1", "10.0e-1", "1e-4", "1e-5", "0.0001",
    "0.00001", "0.00012345", "0.000012345", "1e15", "1e16", "1e17", "9999999999999998.0", "9999999999999999.0",
    "1234567890123456.0", "12345678901234567.0", "12345678901234567890.0", "1e22", "1e23", "1.7976931348623157e308",
    "1.7976931348623159e308", "1e308", "1e309", "-1e400", "1e400", "2.2250738585072014e-308", "2.2250738585072011e-308",
    "4.9406564584124654e-324", "5e-324", "2e-324", "3e-324", "1e-400", "-5e-324", "1.5e300", "1.5e-300", "3.14159",
    "2.718281828459045", "6.02214076e23", "6.62607015e-34", "123.456", "-123.456", "0.5", "0.25", "0.125",
    "1.1", "2.675", "1.005", "33.333333333333336", "0.7", "12.0", "1000000.0", "1e6", "1e-6", "123456.789e3",
    "5e-1", "0.000001", "0.0000001", "1.23456789012345678901234567890", "100", "1e1", "8.5", "-8.5e-3",
]
raws += number_texts

# --- random floats: random bit patterns (every finite double is reachable) and random decimal texts
for _ in range(1500):
    bits = rng.getrandbits(64)
    x = struct.unpack("<d", struct.pack("<Q", bits))[0]
    if x != x or x in (float("inf"), float("-inf")):
        continue
    raws.append(repr(x) if ("e" in repr(x) or "." in repr(x)) else repr(x) + ".0")
for _ in range(800):
    mant = rng.randint(1, 10 ** rng.randint(1, 20))
    exp = rng.randint(-340, 320)
    sign = rng.choice(["", "-"])
    raws.append(f"{sign}{mant}e{exp}")
for _ in range(300):
    whole = rng.randint(0, 10 ** rng.randint(0, 18))
    frac = rng.randint(0, 10 ** rng.randint(1, 18))
    raws.append(f"{rng.choice(['', '-'])}{whole}.{frac}")
for _ in range(200):
    raws.append(str(rng.randint(-10 ** rng.randint(1, 60), 10 ** rng.randint(1, 60))))

# --- scalars and strings
strings = [
    "", " ", "a", "hello world", "it's", 'say "hi"', "both ' and \"", "back\\slash", "\\\\", "\\n literal",
    "tab\there", "new\nline", "cr\rlf\r\n", "\b\f", "\x00", "\x01\x02\x1f", "\x7f", "\x80", "\x85", "\x9f", "\xa0",
    "\xad", "\xff", "caf\u00e9", "Z\u00fcrich", "na\u0131ve", "\u00e9 vs e\u0301 (combining)", "a\u0300\u0301\u0302",
    "\u200b zero width space", "\u200c\u200d joiners", "\u200e\u200f marks", "\u2028 line sep", "\u2029 para sep",
    "\u3000 ideographic space", "\ufeff bom", "\ue000 private use", "\U000f0000 plane-15 private", "\U0010ffff",
    "\u0378 unassigned", "\U0001f600 grin", "\U0001f468\u200d\U0001f469\u200d\U0001f467 family",
    "\U0001f1ec\U0001f1e7 flag", "\U0001d400 math bold A", "\U000e0001 tag", "\u4e2d\u6587\u6d4b\u8bd5",
    "\u65e5\u672c\u8a9e\u306e\u30c6\u30ad\u30b9\u30c8", "\ud55c\uad6d\uc5b4", "\u0627\u0644\u0639\u0631\u0628\u064a\u0629",
    "\u05e2\u05d1\u05e8\u05d9\u05ea", "\u0939\u093f\u0928\u094d\u0926\u0940", "\u0e44\u0e17\u0e22",
    "\u1781\u17d2\u1798\u17c2\u179a", "\u0440\u0443\u0441\u0441\u043a\u0438\u0439", "\u03b5\u03bb\u03bb\u03b7\u03bd\u03b9\u03ba\u03ac",
    "mixed \u05e2\u05d1 english \u0627\u0644\u0639 123", "\U0002ebf0 CJK ext I (Unicode 15.1)",
    "\U0001cc00 Unicode 16 symbol", "\u1b4e Unicode 16 Balinese", "\U00031350 CJK ext H", "\u2060 word joiner",
    "\u00a0\u2007\u202f nbsp family", "\u2066isolate\u2069", "\u061c arabic letter mark", "\u180e mongolian vowel sep",
    "'", '"', "'\"", "\"'", "''", '""', "\\'", '\\"', "{'a': 1}", "[1, 2]", "True", "None", "null", "1.0",
    "a" * 300, "line1\nline2\nline3", "key: value", "\t\t\t", "   leading and trailing   ",
]
raws += [j(s) for s in strings]
raws += [j_raw(s) for s in strings]
raws += ["true", "false", "null"]

# every ASCII code point, and a sweep across the whole code space (surrogates excluded)
raws.append(j("".join(chr(c) for c in range(0x80))))
raws.append(j(["".join(chr(c) for c in range(0x80, 0x100))]))
for _ in range(40):
    cps = []
    while len(cps) < 200:
        c = rng.randint(0x80, 0x10FFFF) if rng.random() < 0.5 else rng.randint(0x80, 0x2FFFF)
        if 0xD800 <= c <= 0xDFFF:
            continue
        cps.append(chr(c))
    raws.append(j(["".join(cps)]))
    raws.append(j({"k": "".join(cps)}))

# --- containers
containers = [
    {}, [], [[]], [{}], {"a": {}}, {"a": []}, {"a": 1}, {"a": 1, "b": True, "c": None},
    {"a": 1.0, "b": False, "c": [1, 2.5, "x"]}, [1, "two", 3.0, None, True, False],
    {"nested": {"deeper": {"deepest": [1, [2, [3, [4]]]]}}}, {"it's": "value's"}, {'say "hi"': 'x "y"'},
    {"both": "' and \""}, {"": ""}, {" ": " "}, {"k\n": "v\t"}, {"\u4e2d": "\u6587"}, {"emoji": "\U0001f600"},
    {"customer": "Ana", "tier": "gold", "open_tickets": 3, "spend": 1234.5},
    {"ticket": {"id": 42, "subject": "Refund request", "body": "I want my money back!\nNow."}, "history": []},
    [{"role": "user", "content": "Hi"}, {"role": "assistant", "content": "Hello \u2014 how can I help?"}],
    {"big": 2 ** 70, "neg": -(2 ** 70), "tiny": 5e-324, "huge": 1.5e300, "exp": 1e-5, "sci": 1e16},
    {"order": ["z", "a", "m"], "z": 1, "a": 2, "m": 3}, {"b": 1, "a": 2}, {"1": 1, "10": 10, "2": 2},
    {"nbsp\u00a0key": "\u00a0", "ctrl": "\x07", "del": "\x7f", "zw": "\u200b"}, [["a", "b"], ["c", ["d"]]],
    {"list_of_dicts": [{"a": 1}, {"b": [2, {"c": 3}]}]}, {"q": "What's the \"best\" option?"},
    {"backslash": "C:\\path\\to\\file", "unc": "\\\\server\\share"}, [0.1, 0.2, 0.30000000000000004],
    {"x": [], "y": {}, "z": [[], {}]}, ["\u05e2\u05d1\u05e8\u05d9\u05ea", "\u0627\u0644\u0639\u0631\u0628\u064a\u0629"],
    {"state": "open", "priority": None, "tags": ["billing", "urgent"], "sla_breached": False},
]
raws += [j(c) for c in containers]
raws += [j_raw(c) for c in containers]
# raw texts whose number spelling matters inside containers
raws += [
    '{"a": 1e2, "b": 1E-7, "c": -0.0, "d": -0, "e": 100, "f": 1.0}',
    '[1e400, -1e400, 1e-400, 0.1e1]',
    '{"n":{"m":[1,2,{"o":3.50}]}}',
    '{ "spaced" : [ 1 , 2 ] , "x" : "y" }',
    '{"u": "\\u00e9\\u0301\\ud83d\\ude00"}',
    '"\\u0000\\u001f\\u007f\\u0080"',
    '{"deep": [[[[[[[[[[["bottom"]]]]]]]]]]]}',
]

# de-duplicate while keeping order
seen = set()
unique = []
for r in raws:
    if r not in seen:
        seen.add(r)
        unique.append(r)

header = {
    "header": True,
    "generator": "tests/Tau.Inference.Tests/Text/data/gen_text_fixtures.py",
    "python": sys.version.split()[0],
    "implementation": platform.python_implementation(),
    "unicodedata": unicodedata.unidata_version,
    "date": datetime.date.today().isoformat(),
}

with open(os.path.join(HERE, "pyjson.jsonl"), "w", encoding="ascii", newline="\n") as f:
    f.write(json.dumps(header) + "\n")
    for r in unique:
        v = json.loads(r)
        rec = {"raw": r, "dumps": json.dumps(v, ensure_ascii=False), "dumps_state": serialize_state(v),
               "render_criterion": render_criterion(v)}
        f.write(json.dumps(rec) + "\n")

with open(os.path.join(HERE, "pyrepr.jsonl"), "w", encoding="ascii", newline="\n") as f:
    f.write(json.dumps(header) + "\n")
    for r in unique:
        v = json.loads(r)
        f.write(json.dumps({"raw": r, "str": str(v), "von_state": format_state(v)}) + "\n")

print(f"{len(unique)} cases written with Python {header['python']} (unicodedata {header['unicodedata']})")
