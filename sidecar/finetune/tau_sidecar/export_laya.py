"""Export the Laya checkpoints (encoder + typed decision head) to ONNX.

Approach A: TorchScript-based `torch.onnx.export` (opset 17). Approach B: the dynamo exporter.
Graph interface matches `laya.onnx_agent.ONNXAgent`: inputs input_ids, attention_mask, marker_pos,
marker_mask, qtype; outputs logits, act_logits.

Tracing hazards handled here (each was read out of the reference code, see DECISIONS/research):
- HF mask helpers skip building a mask when a batch has no padding or fits inside the local
  window, so the trace example has padding AND sequences longer than the 128-token local window.
- `nn.TransformerEncoderLayer` switches to a fused kernel (`_transformer_encoder_layer_fwd`, no ONNX
  symbolic) in no-grad eval mode, so the export runs with grad enabled.
- The head's `topk(2)` needs K >= 2: the trace uses K >= 2 and the host pads K to >= 2 with a
  masked slot (its logit is -1e4 and never reaches an answer).

Usage: uv run python -m tau_sidecar.export_laya [--only laya-en] [--approach A|B]
"""
from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

import torch

from .manifest import package_dir, write_manifest
from .reference import LAYA_IDS, LayaReference

OPSET = 17


class _Wrap(torch.nn.Module):
    def __init__(self, model):
        super().__init__()
        self.m = model

    def forward(self, input_ids, attention_mask, marker_pos, marker_mask, qtype):
        return self.m(input_ids, attention_mask, marker_pos, marker_mask, qtype)


def example_batch(ref: LayaReference):
    """Padding present, lengths straddling the 128-token local window, K >= 2, all three types."""
    long_state = " ".join(["The customer wrote again about the duplicate charge on the March invoice."] * 30)
    short_state = "Refund please."
    q = {
        "queue": {"type": "choice", "instructions": "Which team owns this?",
                  "criteria": {"billing": "refunds and charges", "tech": "outages", "sales": "new orders"}},
        "urgency": {"type": "score", "instructions": "How urgent?", "criteria": ["low", "medium", "high"]},
        "churn": {"type": "noul", "instructions": "Is the customer threatening to leave?"},
    }
    rows_long = ref.rows(long_state, q)
    rows_short = ref.rows(short_state, {"queue": q["queue"]})
    rows_long.items.extend(rows_short.items)
    b = ref.collate(rows_long)
    assert b["attention_mask"].shape[1] > 300 and bool((b["attention_mask"] == 0).any()), "trace example must pad"
    return b


def export(model_id: str, approach: str) -> Path:
    ref = LayaReference(model_id)
    b = example_batch(ref)
    wrap = _Wrap(ref.model).eval()
    for p in wrap.parameters():
        p.requires_grad_(True)  # disables the fused encoder-layer fast path during tracing
    args = (b["input_ids"], b["attention_mask"], b["marker_pos"], b["marker_mask"], b["qtype"])
    out = package_dir(model_id) / "model.onnx"
    for stale in (out, out.with_name(out.name + ".data")):
        stale.unlink(missing_ok=True)
    names_in = ["input_ids", "attention_mask", "marker_pos", "marker_mask", "qtype"]
    names_out = ["logits", "act_logits"]
    t0 = time.time()
    with torch.enable_grad():
        if approach == "A":
            torch.onnx.export(
                wrap, args, str(out), dynamo=False, opset_version=OPSET,
                input_names=names_in, output_names=names_out,
                dynamic_axes={"input_ids": {0: "B", 1: "S"}, "attention_mask": {0: "B", 1: "S"},
                              "marker_pos": {0: "B", 1: "K"}, "marker_mask": {0: "B", 1: "K"},
                              "qtype": {0: "B"}, "logits": {0: "B", 1: "K"}, "act_logits": {0: "B"}},
                do_constant_folding=True)
        else:
            B, S, K = torch.export.Dim("B"), torch.export.Dim("S", max=8192), torch.export.Dim("K", min=2, max=255)
            torch.onnx.export(
                wrap, args, str(out), dynamo=True, opset_version=18,
                input_names=names_in, output_names=names_out,
                dynamic_shapes={"input_ids": {0: B, 1: S}, "attention_mask": {0: B, 1: S},
                                "marker_pos": {0: B, 1: K}, "marker_mask": {0: B, 1: K}, "qtype": {0: B}},
                external_data=True)
    print(f"[{model_id}] approach {approach}: exported in {time.time() - t0:.0f}s -> {out} "
          f"({out.stat().st_size / 1e9:.2f} GB)")
    post = ref.post_processing()
    limits = {"max_len": post.pop("max_len"), "head_max_len": post.pop("head_max_len"), "min_k": 2}
    tok_rel = "tokenizer"
    write_manifest(model_id, "laya", out, "torchscript" if approach == "A" else "dynamo",
                   OPSET if approach == "A" else 18, tok_rel, limits, post)
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=LAYA_IDS)
    # A (TorchScript) exports but bakes the traced sequence length into the head's attention
    # reshape, so it fails on any other length: see DECISIONS 2026-09-27 "ONNX export spike".
    ap.add_argument("--approach", choices=["A", "B"], default="B")
    a = ap.parse_args(argv)
    for mid in a.only:
        export(mid, a.approach)
    return 0


if __name__ == "__main__":
    sys.exit(main())
