"""Compact answer sheets for the frontier labels, and ingest of the answers into frontier/cache.jsonl.

The Workbench exports one fixed prompt per item (`frontier/pending/<version>/batch-NNN.jsonl`). The cost
model counts those per-item prompts, because that is what an API caller would pay for. The answers
themselves come from this Claude Code session (never the API), and sending 1,000 copies of Banking77's
77-option list through a session is waste. So a sheet shows the prompt's fixed part once, taken verbatim
from the batch's own prompts, then every item's text. The answerer is told to judge each item on its own.
The difference from one-call-per-item is recorded in every cache line's `produced_by`.

Usage:
  uv run python -m tau_sidecar.frontier_sheets sheets --example banking77
  uv run python -m tau_sidecar.frontier_sheets ingest --example banking77 --answers <tsv> --model claude-opus-5-5

Sheets go to examples/<example>/frontier/sheets/ (gitignored: they hold item text). An answers file is
TSV, one `item_id<TAB>answer` per line, for one prompt version (named by the sheet it answers).
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import sys
from pathlib import Path
from typing import Dict, List, Tuple

from .reference import REPO

PRODUCED_BY = ("claude-code-session: subagent answer sheet; the prompt's fixed part shown once per "
               "batch of up to 200 items, each item judged on its own; no API")


def _markers(version: str) -> Tuple[str, str]:
    """The delimiters around the item text in each template (see PromptTemplates.cs)."""
    if version == "v1":
        return "Item:\n<<<\n", "\n>>>"
    if version == "v1-alt":
        return 'Read the following text carefully.\n\n"""\n', '\n"""'
    raise SystemExit(f"unknown prompt version {version}")


def split_prompt(version: str, prompt: str) -> Tuple[str, str, str]:
    """(before the item, the item text, after the item), exactly as the template rendered them."""
    start, end = _markers(version)
    i = prompt.index(start) + len(start)
    j = prompt.index(end, i)
    return prompt[:i - len(start)], prompt[i:j], prompt[j + len(end):]


def load_batch(path: Path) -> List[dict]:
    return [json.loads(l) for l in path.read_text(encoding="utf-8").splitlines() if l.strip()]


def write_sheet(example: str, batch: Path) -> Path:
    version = batch.parent.name
    rows = load_batch(batch)
    heads = {split_prompt(version, r["prompt"])[0] for r in rows}
    tails = {split_prompt(version, r["prompt"])[2] for r in rows}
    if len(heads) != 1 or len(tails) != 1:
        raise SystemExit(f"{batch}: the fixed part of the prompt varies between items; a sheet can't show it once")
    head, tail = heads.pop().strip(), tails.pop().strip()
    lines = [f"# Answer sheet: {example}, prompt {version}, {batch.name} ({len(rows)} items)", "",
             "Judge every item on its own, as if it were the only one. Don't let earlier items or the",
             "distribution of your earlier answers influence the next.", "",
             "## The fixed prompt (the item goes where it says ITEM)", ""]
    if version == "v1":
        lines += [head, "", "Item:", "<<<", "ITEM", ">>>", "", tail]
    else:
        lines += ["Read the following text carefully.", "", '"""', "ITEM", '"""', "", tail]
    lines += ["", "## Output", "",
              "Write a TSV file, one line per item in this order: `item_id<TAB>answer`, where answer is exactly",
              f"one allowed answer ({', '.join(rows[0]['allowed'][:6])}{', ...' if len(rows[0]['allowed']) > 6 else ''}).",
              "", "## Items", ""]
    for r in rows:
        lines += [f"### {r['item_id']}", split_prompt(version, r["prompt"])[1], ""]
    out = REPO / "examples" / example / "frontier" / "sheets" / f"{version}-{batch.stem}.md"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(lines), encoding="utf-8", newline="\n")
    return out


def sheets(example: str) -> int:
    pending = REPO / "examples" / example / "frontier" / "pending"
    batches = sorted(pending.glob("*/batch-*.jsonl"))
    if not batches:
        print(f"no pending batches under {pending}: run 'tau label' first")
        return 1
    for b in batches:
        out = write_sheet(example, b)
        print(f"{out.relative_to(REPO)}  {out.stat().st_size:,} chars")
    return 0


def ingest(example: str, answers: Path, model: str, version: str | None) -> int:
    pending = REPO / "examples" / example / "frontier" / "pending"
    by_id: Dict[Tuple[str, str], dict] = {}
    for b in sorted(pending.glob("*/batch-*.jsonl")):
        for r in load_batch(b):
            by_id[(r["prompt_version"], r["item_id"])] = r
    version = version or ("v1-alt" if answers.name.startswith("v1-alt") else "v1")
    today = dt.date.today().isoformat()
    good, bad = [], []
    for n, line in enumerate(answers.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        parts = line.split("\t")
        if len(parts) != 2:
            bad.append(f"line {n}: expected item_id<TAB>answer")
            continue
        item_id, answer = parts[0].strip(), parts[1].strip()
        r = by_id.get((version, item_id))
        if r is None:
            bad.append(f"line {n}: {item_id} is not pending for {version}")
        elif answer not in r["allowed"]:
            bad.append(f"line {n}: {item_id} answer {answer!r} is not allowed")
        else:
            good.append({"key": r["key"], "item_id": item_id, "prompt_version": version, "answer": answer,
                         "model": model, "produced_by": PRODUCED_BY, "date": today,
                         "input_chars": len(r["prompt"]), "output_chars": len(answer)})
    for b in bad[:20]:
        print("  rejected", b)
    if bad:
        print(f"{len(bad)} line(s) rejected; nothing written. Fix the answers file and ingest again.")
        return 1
    cache = REPO / "examples" / example / "frontier" / "cache.jsonl"
    cache.parent.mkdir(parents=True, exist_ok=True)
    with open(cache, "a", encoding="utf-8", newline="\n") as f:
        for g in good:
            f.write(json.dumps(g, ensure_ascii=False) + "\n")
    print(f"{len(good)} answer(s) appended to {cache.relative_to(REPO)}. Run 'tau label' to validate.")
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("sheets")
    s.add_argument("--example", required=True)
    i = sub.add_parser("ingest")
    i.add_argument("--example", required=True)
    i.add_argument("--answers", required=True, type=Path)
    i.add_argument("--model", required=True)
    i.add_argument("--version", choices=["v1", "v1-alt"])
    a = ap.parse_args(argv)
    return sheets(a.example) if a.cmd == "sheets" else ingest(a.example, a.answers, a.model, a.version)


if __name__ == "__main__":
    sys.exit(main())
