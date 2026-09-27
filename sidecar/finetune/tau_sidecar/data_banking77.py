"""Prepare Banking77 splits for the Workbench (research R-02, docs/DECISIONS.md 2026-09-27).

Downloads `banking_data/train.csv` and `banking_data/test.csv` from `PolyAI-LDN/task-specific-datasets`
at a pinned commit, verifies each against a sha256 recorded below (computed and hard-coded after the
first download), and writes deterministic, non-overlapping, stratified (seed 42) splits:

  - calibration: 1,000 rows from train, stratified by category
  - held-out:    1,000 rows from test, stratified by category
  - finetune:    the remainder of train (the calibration rows removed)

Fields are `text` and `category`; 77 intents. Licence: CC-BY-4.0. Not synthetic.

Item ids are stable across runs: `train-<row index>` / `test-<row index>`, `<row index>` being the
0-based position in the original CSV (header excluded).

Run: `uv run --frozen python -m tau_sidecar.data_banking77`
"""
from __future__ import annotations

import datetime
import urllib.request
from pathlib import Path
from typing import Any, Dict, List

import pandas as pd

from .dataset_common import (
    DATA_DIR,
    EXAMPLES_DIR,
    class_counts,
    sha256_file,
    stratified_take,
    write_jsonl,
    write_manifest,
)

SOURCE_REPO = "PolyAI-LDN/task-specific-datasets"
SOURCE_COMMIT = "9d081458ff52e53cf7e848f414e6e9344e4e6696"
BASE_URL = f"https://raw.githubusercontent.com/{SOURCE_REPO}/{SOURCE_COMMIT}/banking_data"
LICENCE = "CC-BY-4.0"

# Computed and hard-coded after the first download (verified against the pinned commit above).
RAW_SHA256 = {
    "train.csv": "b06e26ac675513959a63135f11b94ea7786ed02da65db93a5650d8838cbc664b",
    "test.csv": "d12d6e3bc4c3103966ae786dc435913c0c563dfa328f5a3646d0e62cfeeb474d",
}

SEED = 42
CALIBRATION_SIZE = 1000
HELDOUT_SIZE = 1000

DATASET_DIR = DATA_DIR / "banking77"
RAW_DIR = DATASET_DIR / "raw"
MANIFEST_PATH = EXAMPLES_DIR / "banking77" / "dataset.manifest.json"
COMMAND = "uv run --frozen python -m tau_sidecar.data_banking77"


def _download(name: str) -> Path:
    RAW_DIR.mkdir(parents=True, exist_ok=True)
    dest = RAW_DIR / name
    if not dest.exists():
        urllib.request.urlretrieve(f"{BASE_URL}/{name}", dest)
    digest = sha256_file(dest)
    expected = RAW_SHA256[name]
    if digest != expected:
        raise ValueError(
            f"{name}: sha256 mismatch (got {digest}, expected {expected}) - refusing to use an "
            "unverified file. Delete it and re-run, or update RAW_SHA256 if the pin genuinely changed."
        )
    return dest


def _records(df: pd.DataFrame, prefix: str) -> List[Dict[str, Any]]:
    return [
        {"id": f"{prefix}-{idx}", "text": row["text"], "label": row["category"]}
        for idx, row in df.iterrows()
    ]


def prepare() -> Dict[str, Any]:
    train_path = _download("train.csv")
    test_path = _download("test.csv")

    train = pd.read_csv(train_path)
    test = pd.read_csv(test_path)
    assert list(train.columns) == ["text", "category"], train.columns
    assert list(test.columns) == ["text", "category"], test.columns
    assert train["text"].notna().all() and test["text"].notna().all()

    calibration_df, finetune_df = stratified_take(train, "category", CALIBRATION_SIZE, SEED)
    heldout_df, _unused_test_rest = stratified_take(test, "category", HELDOUT_SIZE, SEED)

    calibration = _records(calibration_df, "train")
    finetune = _records(finetune_df, "train")
    heldout = _records(heldout_df, "test")

    # Disjointness: calibration/finetune come from disjoint row sets of train (stratified_take
    # partitions its input); held-out comes from test entirely, which never overlaps train.
    assert not (set(r["id"] for r in calibration) & set(r["id"] for r in finetune))

    calibration_sha = write_jsonl(DATASET_DIR / "calibration.jsonl", calibration)
    heldout_sha = write_jsonl(DATASET_DIR / "heldout.jsonl", heldout)
    finetune_sha = write_jsonl(DATASET_DIR / "finetune.jsonl", finetune)

    manifest = {
        "dataset": "banking77",
        "source": {
            "repo": SOURCE_REPO,
            "commit": SOURCE_COMMIT,
            "files": ["banking_data/train.csv", "banking_data/test.csv"],
            "url": BASE_URL,
        },
        "licence": LICENCE,
        "synthetic": False,
        "raw": {
            "train.csv": {"sha256": RAW_SHA256["train.csv"], "rows": len(train)},
            "test.csv": {"sha256": RAW_SHA256["test.csv"], "rows": len(test)},
        },
        "fields": {"text_field": "text", "label_field": "category"},
        "split": {
            "seed": SEED,
            "method": "stratified by category, largest-remainder rounding; calibration and "
                      "held-out drawn proportional to each category's share of their source split",
            "sizes": {
                "calibration": len(calibration),
                "heldout": len(heldout),
                "finetune": len(finetune),
            },
        },
        "class_counts": {
            "calibration": class_counts(calibration),
            "heldout": class_counts(heldout),
            "finetune": class_counts(finetune),
        },
        "files": {
            "calibration.jsonl": {"sha256": calibration_sha, "rows": len(calibration)},
            "heldout.jsonl": {"sha256": heldout_sha, "rows": len(heldout)},
            "finetune.jsonl": {"sha256": finetune_sha, "rows": len(finetune)},
        },
        "command": COMMAND,
        "date": datetime.date.today().isoformat(),
    }
    write_manifest(MANIFEST_PATH, manifest)
    return manifest


def main() -> None:
    manifest = prepare()
    sizes = manifest["split"]["sizes"]
    print(
        f"banking77: calibration={sizes['calibration']} heldout={sizes['heldout']} "
        f"finetune={sizes['finetune']} -> {MANIFEST_PATH}"
    )


if __name__ == "__main__":
    main()
