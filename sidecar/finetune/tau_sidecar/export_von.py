"""Export Von 1.2.0 (option-marker, independent_options) to ONNX.

Graph interface (the host builds everything data-dependent, as Von's own OpenVINO path does):
inputs input_ids[B,S] i64, full_mask[B,1,S,S] f32 additive, sliding_mask[B,1,S,S] f32 additive,
position_ids[B,S] i64, marker_pos[B,K] i64, marker_mask[B,K] bool; output logits[B,K] f32 with
masked slots set to -1e4.

The encoder call mirrors `von.backends.option_marker_backend._IndependentOptionsEncoder`, which the Von
authors already trace to OpenVINO with exactly these tensors. Gather + scorer replace the reference's
per-row Python loop `last_hidden[b, pos_list]` with a batched gather; same maths.

Usage: uv run python -m tau_sidecar.export_von [--approach A|B]
"""
from __future__ import annotations

import argparse
import sys
import time

import torch

from .manifest import package_dir, write_manifest
from .reference import VON_ID, VonReference


class _Wrap(torch.nn.Module):
    def __init__(self, model):
        super().__init__()
        self.encoder = model.encoder
        self.scorer = model.scorer

    def forward(self, input_ids, full_mask, sliding_mask, position_ids, marker_pos, marker_mask):
        h = self.encoder(input_ids=input_ids,
                         attention_mask={"full_attention": full_mask, "sliding_attention": sliding_mask},
                         position_ids=position_ids).last_hidden_state
        idx = marker_pos.clamp(min=0)[:, :, None].expand(-1, -1, h.size(-1))
        reps = torch.gather(h, 1, idx)
        logits = self.scorer(reps).float()
        return logits.masked_fill(~marker_mask, -1e4)


def example_batch(ref: VonReference):
    long_state = {"message": " ".join(["My card was charged twice and support has not replied."] * 25),
                  "plan": "premium", "account_age_days": 412}
    q = {
        "queue": {"type": "choice", "instructions": "Which team owns this?",
                  "criteria": {"billing": "refunds and charges", "tech": "outages", "sales": "new orders"}},
        "urgency": {"type": "score", "instructions": "How urgent?", "criteria": ["low", "medium", "high"]},
        "churn": {"type": "noul", "instructions": "Is the customer threatening to leave?"},
    }
    rows = ref.rows(long_state, q) + ref.rows("short", {"queue": q["queue"]})
    b = ref.collate(rows)
    assert b["input_ids"].shape[1] > 300 and bool((b["attention_mask"] == 0).any()), "trace example must pad"
    return b


def export(approach: str):
    ref = VonReference()
    b = example_batch(ref)
    wrap = _Wrap(ref.model).eval()
    args = tuple(b[k] for k in ("input_ids", "full_mask", "sliding_mask", "position_ids", "marker_pos", "marker_mask"))
    out = package_dir(VON_ID) / "model.onnx"
    for stale in (out, out.with_name(out.name + ".data")):
        stale.unlink(missing_ok=True)
    names_in = ["input_ids", "full_mask", "sliding_mask", "position_ids", "marker_pos", "marker_mask"]
    t0 = time.time()
    if approach == "B":
        B, S, K = torch.export.Dim("B"), torch.export.Dim("S", max=8192), torch.export.Dim("K", min=2, max=255)
        torch.onnx.export(wrap, args, str(out), dynamo=True, opset_version=18,
                          input_names=names_in, output_names=["logits"],
                          dynamic_shapes={"input_ids": {0: B, 1: S}, "full_mask": {0: B, 2: S, 3: S},
                                          "sliding_mask": {0: B, 2: S, 3: S}, "position_ids": {0: B, 1: S},
                                          "marker_pos": {0: B, 1: K}, "marker_mask": {0: B, 1: K}},
                          external_data=True)
        opset, exporter = 18, "dynamo"
    else:
        torch.onnx.export(wrap, args, str(out), dynamo=False, opset_version=17,
                          input_names=names_in, output_names=["logits"],
                          dynamic_axes={"input_ids": {0: "B", 1: "S"}, "full_mask": {0: "B", 2: "S", 3: "S"},
                                        "sliding_mask": {0: "B", 2: "S", 3: "S"}, "position_ids": {0: "B", 1: "S"},
                                        "marker_pos": {0: "B", 1: "K"}, "marker_mask": {0: "B", 1: "K"},
                                        "logits": {0: "B", 1: "K"}})
        opset, exporter = 17, "torchscript"
    print(f"[{VON_ID}] approach {approach}: exported in {time.time() - t0:.0f}s -> {out}")
    post = ref.post_processing()
    post.pop("calibration_file")
    limits = {"max_position": ref.model.encoder.config.max_position_embeddings,
              "sliding_window": post["sliding_window"], "tau_max_tokens": 4096, "min_k": 2}
    write_manifest(VON_ID, "von", out, exporter, opset, "", limits, post)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--approach", choices=["A", "B"], default="B")
    export(ap.parse_args(argv).approach)
    return 0


if __name__ == "__main__":
    sys.exit(main())
