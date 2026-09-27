"""Shared helpers for dataset preparation scripts (`data_banking77.py`, `data_tickets.py`).

Kept free of `torch`/`transformers` imports, so preparing a dataset never needs the ML stack -
these scripts only need `pandas`/`datasets`/`huggingface_hub`.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any, Dict, Iterable, List, Sequence, Tuple

import pandas as pd

REPO = Path(__file__).resolve().parents[3]
DATA_DIR = REPO / "data"
EXAMPLES_DIR = REPO / "examples"


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def stratified_take(df: pd.DataFrame, category_col: str, n: int, seed: int) -> Tuple[pd.DataFrame, pd.DataFrame]:
    """Split `df` into `(taken, rest)` with `taken` holding exactly `n` rows.

    Rows are drawn proportional to each category's share of `df` (largest-remainder rounding to
    hit `n` exactly, ties broken by category name), sampled within each category with
    `random_state=seed`. Both frames come back sorted by their original index, so the row order in
    each split file reflects the source CSV's order.
    """
    if n > len(df):
        raise ValueError(f"cannot take {n} rows from {len(df)}")
    counts = df[category_col].value_counts()
    exact = counts * (n / len(df))
    base = exact.apply(lambda x: int(x))
    remainder = n - int(base.sum())
    frac = exact - base
    order = sorted(frac.index, key=lambda c: (-frac[c], str(c)))
    take_counts = base.copy()
    for c in order[:remainder]:
        take_counts[c] += 1
    taken_parts: List[pd.DataFrame] = []
    rest_parts: List[pd.DataFrame] = []
    for cat, k in take_counts.items():
        cat_df = df[df[category_col] == cat]
        sampled = cat_df.sample(n=int(k), random_state=seed) if k > 0 else cat_df.iloc[0:0]
        taken_parts.append(sampled)
        rest_parts.append(cat_df.drop(sampled.index))
    taken = pd.concat(taken_parts).sort_index() if taken_parts else df.iloc[0:0]
    rest = pd.concat(rest_parts).sort_index() if rest_parts else df.iloc[0:0]
    return taken, rest


def write_jsonl(path: Path, records: Iterable[Dict[str, Any]]) -> str:
    """Write one JSON object per line (LF, UTF-8) and return its sha256."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for rec in records:
            f.write(json.dumps(rec, ensure_ascii=False) + "\n")
    return sha256_file(path)


def class_counts(records: Sequence[Dict[str, Any]], label_field: str = "label") -> Dict[str, int]:
    out: Dict[str, int] = {}
    for r in records:
        key = str(r[label_field])
        out[key] = out.get(key, 0) + 1
    return dict(sorted(out.items()))


def write_manifest(path: Path, manifest: Dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
