"""Parity level 1 (ONNX vs PyTorch) and fixture generation for the C# gates (T014/T015).

For every model and every applicable case in `cases.py`:
- rows: token ids + marker positions exactly as the vendor runtime builds them (+ Von position ids);
- reference logits: the vendor module's own forward (Laya: the request batch as `Agent` collates it;
  Von: one row at a time, bool masks — the reference's path);
- ONNX logits: onnxruntime (CPU, basic graph optimisation) on the request batch Tau will send;
- reference answers: `Agent.system_one` / `OptionMarkerBackend.evaluate`, reduced to contract fields.

Tolerance (agreed 2026-09-27): max |dlogit| <= 2e-3, max |dprob| <= 1e-3, identical argmax.
Writes reports/r1/parity-model.json and tests/fixtures/parity/<model>/{sequences,logits,answers}.jsonl.

Usage: uv run python -m tau_sidecar.parity [--only laya-en ...] [--no-fixtures]
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import platform
import sys
import time
from pathlib import Path
from typing import Any, Dict, List

import numpy as np

from .cases import cases as all_cases
from .manifest import sha256
from .reference import ALL_IDS, LAYA_IDS, MODELS_DIR, REPO, VON_ID, LayaReference, VonReference

TOL_LOGIT, TOL_PROB = 2e-3, 1e-3
FIXTURES = REPO / "tests" / "fixtures" / "parity"
REPORTS = REPO / "reports" / "r1"
CONTRACT_FIELDS = {"choice": ("choice", "probabilities", "confidence"),
                   "score": ("score", "legend", "probabilities", "confidence"),
                   "noul": ("noul",)}


TIE_EPS = 1e-4  # see DECISIONS 2026-09-27 "argmax tie-break"


def argmax_tie(z: np.ndarray) -> int:
    """First index whose logit is within TIE_EPS of the max: the reference's first-index rule
    (torch.argmax / np.argmax), made robust to float noise on exact ties (e.g. duplicate options)."""
    return int(np.flatnonzero(z >= z.max() - TIE_EPS)[0])


def softmax(z: np.ndarray) -> np.ndarray:
    e = np.exp(z - z.max())
    return e / e.sum()


def contract_answer(qtype: str, ans: Dict[str, Any]) -> Dict[str, Any]:
    return {"type": qtype, **{k: ans[k] for k in CONTRACT_FIELDS[qtype]}}


def ort_session(model_id: str):
    import onnxruntime as ort

    so = ort.SessionOptions()
    so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_BASIC
    so.intra_op_num_threads = 8
    return ort.InferenceSession(str(MODELS_DIR / model_id / "model.onnx"), so, providers=["CPUExecutionProvider"])


def header(model_id: str) -> Dict[str, Any]:
    import onnxruntime
    import torch
    import transformers

    man = json.loads((MODELS_DIR / model_id / "tau-model.json").read_text(encoding="utf-8"))
    return {"header": True, "generator": "tau_sidecar.parity", "model": model_id,
            "date": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds"),
            "source": man["source"]["repo"], "revision": man["source"]["revision"],
            "onnx_sha256": man["onnx"]["sha256"], "onnx_data_sha256": man["onnx"].get("data_sha256"),
            "reference": man["reference"]["package"], "torch": torch.__version__,
            "transformers": transformers.__version__, "onnxruntime": onnxruntime.__version__,
            "python": platform.python_version(), "tolerance": {"logit": TOL_LOGIT, "prob": TOL_PROB}}


class Stats:
    def __init__(self):
        self.cases = self.rows = self.argmax_mismatch = 0
        self.max_dlogit = self.max_dprob = 0.0
        self.failures: List[str] = []
        self.errors_expected = 0

    def add_row(self, cid: str, ref: np.ndarray, onx: np.ndarray):
        k = len(ref)
        dl = float(np.abs(ref - onx[:k]).max())
        dp = float(np.abs(softmax(ref) - softmax(onx[:k])).max())
        self.rows += 1
        self.max_dlogit, self.max_dprob = max(self.max_dlogit, dl), max(self.max_dprob, dp)
        if argmax_tie(ref) != argmax_tie(onx[:k]):
            self.argmax_mismatch += 1
            self.failures.append(f"{cid}: argmax {argmax_tie(ref)} vs {argmax_tie(onx[:k])}")
        if dl > TOL_LOGIT or dp > TOL_PROB:
            self.failures.append(f"{cid}: dlogit {dl:.2e} dprob {dp:.2e}")

    def summary(self) -> Dict[str, Any]:
        return {"cases": self.cases, "rows": self.rows, "max_dlogit": self.max_dlogit,
                "max_dprob": self.max_dprob, "argmax_mismatches": self.argmax_mismatch,
                "expected_errors": self.errors_expected, "failures": self.failures,
                "pass": not self.failures}


def case_model(model_id: str) -> str:
    """Local fine-tunes of laya-en use laya-en's cases."""
    return "laya-en" if model_id.startswith("laya-en-ft-") else model_id


def run_laya(model_id: str, fx: bool) -> Dict[str, Any]:
    ref, sess, st = LayaReference(model_id), ort_session(model_id), Stats()
    seq_out, log_out, ans_out = [header(model_id)], [header(model_id)], [header(model_id)]
    derived = case_model(model_id) != model_id
    for case in [c for c in all_cases() if case_model(model_id) in c["models"]]:
        cid, state, qs = case["id"], case["state"], case["questions"]
        st.cases += 1
        try:
            rows = ref.rows(state, qs)
        except ValueError as e:
            # A fine-tune can have a different token budget: its own reference decides what it rejects.
            if not case.get("expect_error") and not derived:
                st.failures.append(f"{cid}: unexpected reference error {e}")
            st.errors_expected += 1
            ans_out.append({"id": cid, "request": {"state": state, "questions": qs}, "error": str(e)})
            continue
        if case.get("expect_error") and not derived:
            st.failures.append(f"{cid}: expected an error, reference answered")
        b = ref.collate(rows)
        # Tau pads K to >= 2 (the head's topk(2)); do the same for ONNX. The reference path itself
        # handles K == 1 in Python, so the torch logits come from the unpadded batch.
        tl = ref.logits(b)
        feeds = {k: b[k].numpy() for k in ("input_ids", "attention_mask", "marker_pos", "marker_mask", "qtype")}
        if feeds["marker_pos"].shape[1] < 2:
            pad = 2 - feeds["marker_pos"].shape[1]
            feeds["marker_pos"] = np.pad(feeds["marker_pos"], ((0, 0), (0, pad)))
            feeds["marker_mask"] = np.pad(feeds["marker_mask"], ((0, 0), (0, pad)))
        feeds["marker_mask"] = feeds["marker_mask"].astype(bool)
        ol = sess.run(["logits"], feeds)[0]
        row_logits = []
        for r, qid in enumerate(rows.ids):
            k = len(rows.items[r]["markers"])
            st.add_row(f"{cid}/{qid}", tl[r, :k], ol[r, :k])
            row_logits.append([float(x) for x in tl[r, :k]])
        answers = ref.answers(state, qs)
        seq_out.append({"id": cid, "rows": [{"qid": qid, "input_ids": it["ids"], "markers": it["markers"],
                                             "qtype": it["qtype"]} for qid, it in zip(rows.ids, rows.items)]})
        log_out.append({"id": cid, "logits": row_logits})
        ans_out.append({"id": cid, "request": {"state": state, "questions": qs},
                        "expected": {qid: contract_answer(qs[qid]["type"], a) for qid, a in answers["answers"].items()},
                        "usage": answers["usage"]})
    if fx:
        write_fixtures(model_id, seq_out, log_out, ans_out)
    return st.summary()


def run_von(fx: bool) -> Dict[str, Any]:
    ref, sess, st = VonReference(), ort_session(VON_ID), Stats()
    seq_out, log_out, ans_out = [header(VON_ID)], [header(VON_ID)], [header(VON_ID)]
    from von.models.option_marker import build_option_invariant_position_ids
    import torch

    for case in [c for c in all_cases() if VON_ID in c["models"]]:
        cid, state, qs = case["id"], case["state"], case["questions"]
        st.cases += 1
        rows = ref.rows(state, qs)
        b = ref.collate(rows)
        feeds = {k: b[k].numpy() for k in ("input_ids", "full_mask", "sliding_mask", "position_ids", "marker_pos", "marker_mask")}
        feeds["marker_mask"] = feeds["marker_mask"].astype(bool)
        ol = sess.run(["logits"], feeds)[0]
        row_logits, seq_rows = [], []
        for r, row in enumerate(rows):
            tl = ref.logits_single(row)  # the reference's own unbatched path
            st.add_row(f"{cid}/{row.qid}/{row.kind}", tl, ol[r])
            row_logits.append([float(x) for x in tl])
            ids = torch.tensor([row.input_ids])
            pos = build_option_invariant_position_ids(ids, torch.ones_like(ids), [row.markers])[0].tolist()
            seq_rows.append({"qid": row.qid, "kind": row.kind, "text": row.text, "input_ids": row.input_ids,
                             "markers": row.markers, "position_ids": pos})
        answers = ref.answers(state, qs)
        seq_out.append({"id": cid, "rows": seq_rows})
        log_out.append({"id": cid, "logits": row_logits})
        ans_out.append({"id": cid, "request": {"state": state, "questions": qs},
                        "expected": {qid: contract_answer(qs[qid]["type"], a) for qid, a in answers["answers"].items()}})
    if fx:
        write_fixtures(VON_ID, seq_out, log_out, ans_out)
    return st.summary()


def write_fixtures(model_id: str, *streams):
    d = FIXTURES / model_id
    d.mkdir(parents=True, exist_ok=True)
    for name, recs in zip(("sequences", "logits", "answers"), streams):
        with open(d / f"{name}.jsonl", "w", encoding="utf-8", newline="\n") as f:
            for r in recs:
                f.write(json.dumps(r, ensure_ascii=False) + "\n")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=ALL_IDS)
    ap.add_argument("--no-fixtures", action="store_true")
    ap.add_argument("--report", default=None, help="report path (default reports/r1/parity-model.json)")
    a = ap.parse_args(argv)
    t0, results = time.time(), {}
    for mid in a.only:
        t = time.time()
        results[mid] = run_von(not a.no_fixtures) if mid == VON_ID else run_laya(mid, not a.no_fixtures)
        s = results[mid]
        print(f"[{mid}] cases={s['cases']} rows={s['rows']} max|dlogit|={s['max_dlogit']:.2e} "
              f"max|dprob|={s['max_dprob']:.2e} argmax_mismatch={s['argmax_mismatches']} "
              f"expected_errors={s['expected_errors']} -> {'PASS' if s['pass'] else 'FAIL'} ({time.time() - t:.0f}s)")
        for f in s["failures"][:10]:
            print("   ", f)
    REPORTS.mkdir(parents=True, exist_ok=True)
    report = {"report": "parity-model", "level": 1, "command": "uv run python -m tau_sidecar.parity " + " ".join(argv or sys.argv[1:]),
              "date": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds"),
              "tolerance": {"logit": TOL_LOGIT, "prob": TOL_PROB}, "results": results,
              "models": {m: {"onnx_sha256": sha256(MODELS_DIR / m / "model.onnx")} for m in results},
              "elapsed_s": round(time.time() - t0)}
    out = Path(a.report) if a.report else REPORTS / "parity-model.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 0 if all(r["pass"] for r in results.values()) else 1


if __name__ == "__main__":
    sys.exit(main())
