"""T017: fine-tune collation, the baseline output format and the frontier answer sheets.

No GPU and no training run: collation is checked on hand-made items, the baseline check reads the committed
probability files, and the sheet check uses a synthetic batch.
"""
from __future__ import annotations

import json
import math

import pytest
import yaml

from tau_sidecar import frontier_sheets
from tau_sidecar.finetune_laya import collate
from tau_sidecar.reference import REPO

BASELINES = {"banking77": "minilm-l6-banking77", "support-tickets": "minilm-l6-tickets"}


def test_collate_pads_to_the_longest_row_and_at_least_two_markers():
    items = [{"ids": [5, 6, 7], "markers": [1], "qtype": 0, "target": [1.0], "label": 0},
             {"ids": [5, 6, 7, 8, 9], "markers": [2], "qtype": 1, "target": [1.0], "label": 0}]
    b = collate(items, pad_id=0)
    assert b["input_ids"].shape == (2, 5)
    assert b["input_ids"][0].tolist() == [5, 6, 7, 0, 0]
    assert b["attention_mask"][0].tolist() == [1, 1, 1, 0, 0]
    assert b["marker_pos"].shape == (2, 2)  # the head's topk(2) needs K >= 2
    assert b["marker_mask"].tolist() == [[True, False], [True, False]]
    assert b["qtype"].tolist() == [0, 1]


def test_collate_keeps_each_rows_target_under_its_own_markers():
    items = [{"ids": [1, 2, 3, 4], "markers": [1, 2, 3], "qtype": 0, "target": [0.0, 1.0, 0.0], "label": 1},
             {"ids": [1, 2], "markers": [0, 1], "qtype": 0, "target": [1.0, 0.0], "label": 0}]
    b = collate(items, pad_id=9)
    assert b["target"].tolist() == [[0.0, 1.0, 0.0], [1.0, 0.0, 0.0]]
    assert b["marker_mask"][1].tolist() == [True, True, False]
    assert b["input_ids"][1].tolist() == [1, 2, 9, 9]


@pytest.mark.parametrize("example", sorted(BASELINES))
def test_baseline_probability_files_have_the_workbench_shape(example):
    path = REPO / "examples" / example / "baselines" / f"{BASELINES[example]}.jsonl"
    if not path.exists():
        pytest.skip(f"{path.name} not generated yet")
    spec = yaml.safe_load((REPO / "examples" / example / "decision.yaml").read_text(encoding="utf-8"))
    q = spec["question"]
    keys = list(q["options"]) if q["type"] == "choice" else [str(i) for i in range(len(q["options"]))]
    splits = {"calibration": 0, "heldout": 0}
    for line in path.read_text(encoding="utf-8").splitlines():
        r = json.loads(line)
        assert set(r) == {"id", "split", "label", "probabilities"}
        assert r["label"] in keys
        assert list(r["probabilities"]) == keys
        assert math.isclose(sum(r["probabilities"].values()), 1.0, abs_tol=1e-4)
        splits[r["split"]] += 1
    assert splits == {"calibration": 1000, "heldout": 1000}
    training = json.loads(path.with_suffix(".training.json").read_text(encoding="utf-8"))
    assert 1 <= training["best_epoch"] <= training["max_epochs"]


@pytest.mark.parametrize("version,prompt,item", [
    ("v1", "Task: X\n\nItem:\n<<<\nhello\nworld\n>>>\n\nReply now.", "hello\nworld"),
    ("v1-alt", 'Read the following text carefully.\n\n"""\nsome text\n"""\n\nQuestion: X', "some text"),
])
def test_split_prompt_recovers_the_item_text(version, prompt, item):
    head, text, tail = frontier_sheets.split_prompt(version, prompt)
    assert text == item
    start, end = frontier_sheets._markers(version)
    assert head + start + text + end + tail == prompt


def test_ingest_rejects_everything_if_one_answer_is_invalid(tmp_path, monkeypatch):
    ex = tmp_path / "examples" / "demo" / "frontier" / "pending" / "v1"
    ex.mkdir(parents=True)
    rows = [{"key": f"rev:i{n}:v1", "item_id": f"i{n}", "prompt_version": "v1",
             "prompt": f"Item:\n<<<\nt{n}\n>>>\nReply.", "allowed": ["a", "b"]} for n in range(2)]
    (ex / "batch-001.jsonl").write_text("\n".join(json.dumps(r) for r in rows), encoding="utf-8")
    monkeypatch.setattr(frontier_sheets, "REPO", tmp_path)
    answers = tmp_path / "v1-batch-001.tsv"
    answers.write_text("i0\ta\ni1\tzz\n", encoding="utf-8")
    assert frontier_sheets.ingest("demo", answers, "m", None) == 1
    assert not (tmp_path / "examples" / "demo" / "frontier" / "cache.jsonl").exists()
    answers.write_text("i0\ta\ni1\tb\n", encoding="utf-8")
    assert frontier_sheets.ingest("demo", answers, "m", None) == 0
    cached = [json.loads(l) for l in (tmp_path / "examples" / "demo" / "frontier" / "cache.jsonl").read_text(encoding="utf-8").splitlines()]
    assert [c["answer"] for c in cached] == ["a", "b"]
    assert all(c["input_chars"] == len(rows[0]["prompt"]) for c in cached)
    assert "no API" in cached[0]["produced_by"]
