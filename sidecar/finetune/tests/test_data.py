"""Tests for the dataset preparation scripts (`tau_sidecar.data_banking77`, `tau_sidecar.data_tickets`).

Covers: split determinism (re-running produces byte-identical files), disjointness across splits,
no vehicle/fleet keyword survives into any tickets split, manifest counts matching the files on disk,
and that every record's label is one the manifest actually recorded.

Both scripts cache their raw download (banking77 under `data/banking77/raw/`, tickets in the
`huggingface_hub` cache), so re-running `prepare()` here does not re-fetch over the network once the
first run (by a developer or `T013`) has populated that cache.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, List

import pytest

from tau_sidecar import data_banking77, data_tickets
from tau_sidecar.data_tickets import PRIORITY_LEVELS, has_vehicle_keyword
from tau_sidecar.dataset_common import sha256_file


def _require(manifest_path: Path, script: str) -> None:
    if not manifest_path.exists():
        pytest.fail(f"{manifest_path} missing: run `uv run --frozen python -m {script}` first")


def _read_jsonl(path: Path) -> List[Dict[str, Any]]:
    return [json.loads(line) for line in path.open(encoding="utf-8")]


# --------------------------------------------------------------------------------------- banking77

def test_banking77_manifest_present():
    _require(data_banking77.MANIFEST_PATH, "tau_sidecar.data_banking77")


def test_banking77_determinism():
    _require(data_banking77.MANIFEST_PATH, "tau_sidecar.data_banking77")
    first = data_banking77.prepare()
    second = data_banking77.prepare()
    assert first["files"] == second["files"], "re-running banking77 prep changed a split file's hash"


@pytest.mark.parametrize("split", ["calibration", "heldout", "finetune"])
def test_banking77_manifest_counts_match_files(split):
    _require(data_banking77.MANIFEST_PATH, "tau_sidecar.data_banking77")
    manifest = json.loads(data_banking77.MANIFEST_PATH.read_text(encoding="utf-8"))
    path = data_banking77.DATASET_DIR / f"{split}.jsonl"
    records = _read_jsonl(path)
    assert len(records) == manifest["files"][f"{split}.jsonl"]["rows"]
    assert len(records) == manifest["split"]["sizes"][split]
    assert sha256_file(path) == manifest["files"][f"{split}.jsonl"]["sha256"]


def test_banking77_splits_disjoint():
    _require(data_banking77.MANIFEST_PATH, "tau_sidecar.data_banking77")
    ids = {}
    for split in ["calibration", "heldout", "finetune"]:
        records = _read_jsonl(data_banking77.DATASET_DIR / f"{split}.jsonl")
        split_ids = [r["id"] for r in records]
        assert len(split_ids) == len(set(split_ids)), f"{split}: duplicate ids within the split"
        ids[split] = set(split_ids)
    assert not (ids["calibration"] & ids["finetune"]), "calibration/finetune overlap (both from train)"
    assert not (ids["calibration"] & ids["heldout"])
    assert not (ids["finetune"] & ids["heldout"])


def test_banking77_labels_valid():
    _require(data_banking77.MANIFEST_PATH, "tau_sidecar.data_banking77")
    manifest = json.loads(data_banking77.MANIFEST_PATH.read_text(encoding="utf-8"))
    for split in ["calibration", "heldout", "finetune"]:
        records = _read_jsonl(data_banking77.DATASET_DIR / f"{split}.jsonl")
        known = set(manifest["class_counts"][split])
        for r in records:
            assert isinstance(r["label"], str) and r["label"], r
            assert r["label"] in known


# ----------------------------------------------------------------------------------- support-tickets

def test_tickets_manifest_present():
    _require(data_tickets.MANIFEST_PATH, "tau_sidecar.data_tickets")


def test_tickets_determinism():
    _require(data_tickets.MANIFEST_PATH, "tau_sidecar.data_tickets")
    first = data_tickets.prepare()
    second = data_tickets.prepare()
    assert first["files"] == second["files"], "re-running tickets prep changed a split file's hash"
    assert first["filters"]["rows_removed"] == second["filters"]["rows_removed"]


@pytest.mark.parametrize("split", ["heldout", "calibration", "finetune"])
def test_tickets_manifest_counts_match_files(split):
    _require(data_tickets.MANIFEST_PATH, "tau_sidecar.data_tickets")
    manifest = json.loads(data_tickets.MANIFEST_PATH.read_text(encoding="utf-8"))
    path = data_tickets.DATASET_DIR / f"{split}.jsonl"
    records = _read_jsonl(path)
    assert len(records) == manifest["files"][f"{split}.jsonl"]["rows"]
    assert len(records) == manifest["split"]["sizes"][split]
    assert sha256_file(path) == manifest["files"][f"{split}.jsonl"]["sha256"]


def test_tickets_splits_disjoint():
    _require(data_tickets.MANIFEST_PATH, "tau_sidecar.data_tickets")
    ids = {}
    for split in ["heldout", "calibration", "finetune"]:
        records = _read_jsonl(data_tickets.DATASET_DIR / f"{split}.jsonl")
        split_ids = [r["id"] for r in records]
        assert len(split_ids) == len(set(split_ids)), f"{split}: duplicate ids within the split"
        ids[split] = set(split_ids)
    assert not (ids["heldout"] & ids["calibration"])
    assert not (ids["heldout"] & ids["finetune"])
    assert not (ids["calibration"] & ids["finetune"])


def test_tickets_no_vehicle_keyword_in_any_split():
    _require(data_tickets.MANIFEST_PATH, "tau_sidecar.data_tickets")
    for split in ["heldout", "calibration", "finetune"]:
        for r in _read_jsonl(data_tickets.DATASET_DIR / f"{split}.jsonl"):
            assert not has_vehicle_keyword(r["text"]), f"{split}/{r['id']}: vehicle keyword survived filtering"


def test_tickets_labels_valid():
    _require(data_tickets.MANIFEST_PATH, "tau_sidecar.data_tickets")
    valid = set(PRIORITY_LEVELS.values())
    for split in ["heldout", "calibration", "finetune"]:
        for r in _read_jsonl(data_tickets.DATASET_DIR / f"{split}.jsonl"):
            assert isinstance(r["label"], int) and r["label"] in valid, r


def test_tickets_filter_counts_are_consistent():
    """The rows removed by each filter, subtracted in order from rows_before, land on rows_after."""
    _require(data_tickets.MANIFEST_PATH, "tau_sidecar.data_tickets")
    manifest = json.loads(data_tickets.MANIFEST_PATH.read_text(encoding="utf-8"))
    filters = manifest["filters"]
    remaining = filters["rows_before"]
    for step in filters["order"]:
        remaining -= filters["rows_removed"][step]
    assert remaining == filters["rows_after"]


def test_tickets_manifest_has_no_ticket_text():
    """The manifest must never hold ticket subject/body text (licence CC-BY-NC-4.0, rows never committed)."""
    _require(data_tickets.MANIFEST_PATH, "tau_sidecar.data_tickets")
    raw = data_tickets.MANIFEST_PATH.read_text(encoding="utf-8")
    manifest = json.loads(raw)
    # every string value in the manifest must be short (an id/hash/keyword/path), never a ticket body
    def walk(node):
        if isinstance(node, dict):
            for v in node.values():
                yield from walk(v)
        elif isinstance(node, list):
            for v in node:
                yield from walk(v)
        elif isinstance(node, str):
            yield node
    for s in walk(manifest):
        assert len(s) < 200, f"manifest holds a suspiciously long string (possible ticket text): {s[:60]!r}..."
