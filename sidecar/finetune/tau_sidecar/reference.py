"""Reference runners: the maintained vendor runtimes, pinned, on CPU in FP32.

Laya's reference is `laya==0.3.20` (`laya.Agent`), Von's is `von-sdk==1.2.3` (`OptionMarkerBackend`).
Everything the parity gates compare against comes from here, so nothing in this module reimplements
model logic: tensors are built by the vendors' own functions and the forward pass is the vendors' own
module.

`laya` rewrites `tokenizer/tokenizer_config.json` in place on load (a transformers-compatibility fix),
which would break the sha256 lock on `models/src`. So each checkpoint is copied to `models/work/<id>`
and loaded from there; `models/src` stays byte-identical to the pinned upstream files.
"""
from __future__ import annotations

import json
import os
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List

import numpy as np
import torch

REPO = Path(__file__).resolve().parents[3]
MODELS_DIR = Path(os.environ.get("TAU_MODELS_DIR", REPO / "models"))

LAYA_IDS = ["laya-en", "laya-multilingual", "laya-typed-decisions"]
VON_ID = "von-1.2.0"
ALL_IDS = LAYA_IDS + [VON_ID]


def src_dir(model_id: str) -> Path:
    return MODELS_DIR / "src" / model_id


def work_dir(model_id: str) -> Path:
    """A private copy of the pinned checkpoint that vendor code may modify."""
    dst = MODELS_DIR / "work" / model_id
    src = src_dir(model_id)
    if not src.exists():
        raise FileNotFoundError(f"{src} missing: run scripts/fetch-models.ps1 first")
    for f in src.rglob("*"):
        if f.is_file():
            t = dst / f.relative_to(src)
            if not t.exists() or t.stat().st_size != f.stat().st_size and f.name != "tokenizer_config.json":
                t.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(f, t)
    return dst


def set_deterministic() -> None:
    torch.manual_seed(0)
    torch.use_deterministic_algorithms(True)
    torch.set_num_threads(min(8, os.cpu_count() or 1))


# --------------------------------------------------------------------------------------------- Laya
@dataclass
class LayaRows:
    """One request's rows exactly as `laya.Agent` builds them (before collation)."""
    ids: List[str]
    items: List[Dict[str, Any]]


class LayaReference:
    def __init__(self, model_id: str):
        from laya.agent import Agent

        set_deterministic()
        self.model_id = model_id
        self.agent = Agent(str(work_dir(model_id)), device="cpu")
        assert self.agent.device.type == "cpu" and not self.agent.amp_enabled, "reference must be CPU FP32"
        self.model = self.agent.model.eval()
        self.tok = self.agent.tok

    # The vendor's own validation + normalisation + tokenisation, per request.
    def rows(self, state: Any, questions: Dict[str, Any]) -> LayaRows:
        ids = list(questions)
        for qid in ids:
            self.agent._check_question(qid, questions[qid])
        internal = {qid: self.agent._to_internal(questions[qid]) for qid in ids}
        return LayaRows(ids, self.agent._encode_state(state, ids, internal))

    def collate(self, rows: LayaRows) -> Dict[str, torch.Tensor]:
        from laya.common import collate_items

        b = collate_items([rows.items], self.tok.pad_token_id)
        return {k: b[k] for k in ("input_ids", "attention_mask", "marker_pos", "marker_mask", "qtype")}

    @torch.no_grad()
    def logits(self, batch: Dict[str, torch.Tensor]) -> np.ndarray:
        logits, _act = self.model(batch["input_ids"], batch["attention_mask"], batch["marker_pos"],
                                  batch["marker_mask"], batch["qtype"])
        return logits.float().numpy()

    def answers(self, state: Any, questions: Dict[str, Any]) -> Dict[str, Any]:
        return self.agent.system_one(state, questions)

    def post_processing(self) -> Dict[str, Any]:
        cfg = self.agent.cfg
        return {
            "temperature": list(self.agent.temperature),
            "temperature_by_options": dict(self.agent.temperature_by_options),
            "temperature_raw": list(self.agent.temperature_raw),
            "temperature_by_options_raw": dict(self.agent.temperature_by_options_raw),
            "confidence": "entropy",
            "rounding": 4,
        } | {"max_len": cfg.get("max_len", 512), "head_max_len": cfg.get("head_max_len", 192)}


# ---------------------------------------------------------------------------------------------- Von
@dataclass
class VonRow:
    qid: str
    kind: str  # "choice" | "score" | "noul" | "noul-null"
    text: str
    input_ids: List[int]
    markers: List[int]


class VonReference:
    def __init__(self):
        from von.backends.option_marker_backend import OptionMarkerBackend

        set_deterministic()
        self.backend = OptionMarkerBackend(checkpoint_dir=str(work_dir(VON_ID)), device="cpu")
        self.model = self.backend._get_model().eval()
        assert self.backend._independent_options, "von-1.2.0 is an independent_options checkpoint"
        self.tok = self.model.tokenizer

    def _row(self, qid: str, kind: str, state_text: str, instructions: str, descriptions: List[str]) -> VonRow:
        text = self.model.pack_sequence(state_text, instructions, descriptions)
        enc = self.tok(text)
        ids = list(enc["input_ids"])
        markers = [i for i, t in enumerate(ids) if t == self.model.mask_token_id]
        return VonRow(qid, kind, text, ids, markers)

    def rows(self, state: Any, questions: Dict[str, Any]) -> List[VonRow]:
        """Rows in the order Von's evaluate() would run them, one forward each in the reference."""
        from von.backends.option_marker_backend import _format_state
        from von.types import Choice, Noul, Score

        state_text = _format_state(state)
        rows: List[VonRow] = []
        for qid, qd in questions.items():
            t = qd.get("type", "choice")
            if t == "choice":
                q = Choice(**qd)
                opts = list(q.criteria.keys())
                descs = [(q.criteria.get(o) or o).strip() if q.criteria.get(o) else o.strip() for o in opts]
                rows.append(self._row(qid, "choice", state_text, q.instructions, descs))
            elif t == "score":
                q = Score(**qd)
                descs = []
                for item in q.criteria:
                    if isinstance(item, dict):
                        ex = item.get("examples", [])
                        descs.append((f"{item.get('what', '')}" + (f" Examples: {', '.join(ex)}" if ex else "")).strip())
                    else:
                        descs.append(str(item).strip())
                rows.append(self._row(qid, "score", state_text, q.instructions, descs))
            else:
                q = Noul(**qd)
                crit = q.criteria or {}
                pos, neg = crit.get("true"), crit.get("false")
                explicit = bool(pos or neg)
                descs = [pos or "Yes, condition holds true.", neg or "No, condition is false."]
                rows.append(self._row(qid, "noul", state_text, q.instructions, descs))
                if not explicit:
                    rows.append(self._row(qid, "noul-null", "", q.instructions, descs))
        return rows

    def collate(self, rows: List[VonRow]) -> Dict[str, torch.Tensor]:
        """Pad rows into one batch and build Von's own order-invariant masks and position ids."""
        from von.backends.option_marker_backend import _bool_to_additive
        from von.models.option_marker import build_independent_option_masks, build_option_invariant_position_ids

        B, S = len(rows), max(len(r.input_ids) for r in rows)
        K = max(2, max(len(r.markers) for r in rows))
        pad = self.tok.pad_token_id
        input_ids = torch.full((B, S), pad, dtype=torch.long)
        att = torch.zeros((B, S), dtype=torch.long)
        mpos = torch.zeros((B, K), dtype=torch.long)
        mmask = torch.zeros((B, K), dtype=torch.bool)
        for i, r in enumerate(rows):
            input_ids[i, : len(r.input_ids)] = torch.tensor(r.input_ids)
            att[i, : len(r.input_ids)] = 1
            mpos[i, : len(r.markers)] = torch.tensor(r.markers)
            mmask[i, : len(r.markers)] = True
        positions = [r.markers for r in rows]
        pos_ids = build_option_invariant_position_ids(input_ids, att, positions)
        window = getattr(self.model.encoder.config, "sliding_window", None)
        masks = build_independent_option_masks(input_ids, att, positions, pos_ids, window)
        return {
            "input_ids": input_ids,
            "full_mask": _bool_to_additive(masks["full_attention"]),
            "sliding_mask": _bool_to_additive(masks["sliding_attention"]),
            "position_ids": pos_ids,
            "marker_pos": mpos,
            "marker_mask": mmask,
            "attention_mask": att,
        }

    @torch.no_grad()
    def logits_single(self, row: VonRow) -> np.ndarray:
        """The reference path exactly: one row, bool masks, the vendor module's forward."""
        enc = torch.tensor([row.input_ids])
        att = torch.ones_like(enc)
        out = self.model(input_ids=enc, attention_mask=att, mask_positions=[row.markers],
                         independent_options=True)
        return out[0].float().numpy()

    def answers(self, state: Any, questions: Dict[str, Any]) -> Dict[str, Any]:
        resp = self.backend.evaluate(state, questions)
        return json.loads(resp.model_dump_json()) if hasattr(resp, "model_dump_json") else resp

    def post_processing(self) -> Dict[str, Any]:
        cal = json.loads((src_dir(VON_ID) / "marker_calibration.json").read_text())
        return {
            "temperature": self.backend._default_temp,
            "calibration_map": self.backend._calib_map,
            "noul_prior": self.backend._noul_prior,
            "independent_options": self.backend._independent_options,
            "digit_split": bool(self.model.digit_split),
            "sliding_window": getattr(self.model.encoder.config, "sliding_window", None),
            "confidence": "margin",
            "calibration_file": cal,
        }
