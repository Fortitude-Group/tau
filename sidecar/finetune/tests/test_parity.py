"""Fast sidecar checks (the full-corpus gate is `scripts/parity.ps1` -> `tau_sidecar.parity`).

- every exported package's manifest hashes match the files on disk;
- the reference is deterministic (two runs, identical logits);
- ONNX matches the reference within tolerance on a smoke subset of the case corpus.
"""
import json

import numpy as np
import pytest

from tau_sidecar.cases import cases
from tau_sidecar.manifest import sha256
from tau_sidecar.parity import TOL_LOGIT, TOL_PROB, argmax_tie, ort_session, softmax
from tau_sidecar.reference import ALL_IDS, LAYA_IDS, MODELS_DIR, VON_ID, LayaReference, VonReference

SMOKE = {"ticket-all3", "choice-10", "state-object", "state-array-conversation"}


def _require(model_id):
    if not (MODELS_DIR / model_id / "tau-model.json").exists():
        pytest.fail(f"{model_id} not exported: run scripts/fetch-models.ps1 then scripts/export.ps1")


@pytest.mark.parametrize("model_id", ALL_IDS)
def test_manifest_hashes_match_files(model_id):
    _require(model_id)
    d = MODELS_DIR / model_id
    man = json.loads((d / "tau-model.json").read_text(encoding="utf-8"))
    assert sha256(d / man["onnx"]["file"]) == man["onnx"]["sha256"]
    if "data_file" in man["onnx"]:
        assert sha256(d / man["onnx"]["data_file"]) == man["onnx"]["data_sha256"]
    assert sha256(d / man["tokenizer"]["file"]) == man["tokenizer"]["sha256"]
    lock = {m["id"]: m for m in json.loads((MODELS_DIR.parent / "models.lock.json").read_text())["models"]}
    assert man["source"]["revision"] == lock[model_id]["revision"]


def _close(ref, onx):
    k = len(ref)
    assert np.abs(ref - onx[:k]).max() <= TOL_LOGIT
    assert np.abs(softmax(ref) - softmax(onx[:k])).max() <= TOL_PROB
    assert argmax_tie(ref) == argmax_tie(onx[:k])


@pytest.mark.parametrize("model_id", LAYA_IDS)
def test_laya_deterministic_and_onnx_close(model_id):
    _require(model_id)
    ref, sess = LayaReference(model_id), ort_session(model_id)
    for c in [c for c in cases() if c["id"] in SMOKE and model_id in c["models"]]:
        rows = ref.rows(c["state"], c["questions"])
        b = ref.collate(rows)
        a, b2 = ref.logits(b), ref.logits(ref.collate(ref.rows(c["state"], c["questions"])))
        assert np.array_equal(a, b2), f"{c['id']}: reference not deterministic"
        feeds = {k: b[k].numpy() for k in ("input_ids", "attention_mask", "marker_pos", "marker_mask", "qtype")}
        feeds["marker_mask"] = feeds["marker_mask"].astype(bool)
        o = sess.run(["logits"], feeds)[0]
        for r, it in enumerate(rows.items):
            k = len(it["markers"])
            _close(a[r, :k], o[r, :k])


def test_von_deterministic_and_onnx_close():
    _require(VON_ID)
    ref, sess = VonReference(), ort_session(VON_ID)
    for c in [c for c in cases() if c["id"] in SMOKE and VON_ID in c["models"]]:
        rows = ref.rows(c["state"], c["questions"])
        b = ref.collate(rows)
        feeds = {k: b[k].numpy() for k in ("input_ids", "full_mask", "sliding_mask", "position_ids", "marker_pos", "marker_mask")}
        feeds["marker_mask"] = feeds["marker_mask"].astype(bool)
        o = sess.run(["logits"], feeds)[0]
        for r, row in enumerate(rows):
            t1, t2 = ref.logits_single(row), ref.logits_single(row)
            assert np.array_equal(t1, t2), f"{c['id']}: reference not deterministic"
            _close(t1, o[r])
