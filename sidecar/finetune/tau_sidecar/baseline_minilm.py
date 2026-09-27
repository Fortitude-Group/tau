"""Classic small-encoder baseline: fine-tune all-MiniLM-L6-v2 (22.7M parameters) with a linear head.

The brainstorm cites "a fine-tuned 22M encoder scores 93.2% on Banking77" as third-party data. This
measures the same kind of model on our splits and hardware, so the claim is tested rather than repeated
(constitution XIII). Standard recipe: AutoModelForSequenceClassification, AdamW 5e-5, linear warm-up and
decay, 5 epochs, batch 32, max length 128, seed 42, fp16 on the GPU. It is trained on the example's
fine-tune split only and writes softmax probabilities for the calibration and held-out splits; the
Workbench computes every metric from those with the same code it uses for Tau's models.

Usage: uv run python -m tau_sidecar.baseline_minilm --example banking77
Writes examples/<example>/baselines/minilm-l6-<short>.jsonl and a .training.json beside it.
"""
from __future__ import annotations

import argparse
import json
import platform
import random
import sys
import time

import numpy as np
import torch
import yaml

from .reference import REPO

MODEL = "sentence-transformers/all-MiniLM-L6-v2"
REVISION = "1110a243fdf4706b3f48f1d95db1a4f5529b4d41"  # Apache-2.0
SHORT = {"banking77": "banking77", "support-tickets": "tickets"}
EPOCHS, BATCH, LR, MAX_LEN, SEED = 5, 32, 5e-5, 128, 42


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--example", required=True, choices=sorted(SHORT))
    ap.add_argument("--epochs", type=int, default=EPOCHS)
    ap.add_argument("--limit", type=int, default=0, help="first N training rows only (smoke tests)")
    ap.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    a = ap.parse_args(argv)

    from transformers import AutoModelForSequenceClassification, AutoTokenizer, get_linear_schedule_with_warmup

    random.seed(SEED)
    np.random.seed(SEED)
    torch.manual_seed(SEED)
    spec = yaml.safe_load((REPO / "examples" / a.example / "decision.yaml").read_text(encoding="utf-8"))
    q = spec["question"]
    keys = list(q["options"].keys()) if q["type"] == "choice" else [str(i) for i in range(len(q["options"]))]
    data_dir = REPO / "data" / spec["data"]["dataset"]

    def load(split):
        return [json.loads(l) for l in (data_dir / f"{split}.jsonl").read_text(encoding="utf-8").splitlines() if l]

    train, splits = load("finetune"), {s: load(s) for s in ("calibration", "heldout")}
    if a.limit:
        train = train[: a.limit]
    tok = AutoTokenizer.from_pretrained(MODEL, revision=REVISION)
    model = AutoModelForSequenceClassification.from_pretrained(MODEL, revision=REVISION, num_labels=len(keys)).to(a.device)
    label_of = {k: i for i, k in enumerate(keys)}

    def batches(rows, shuffle):
        idx = list(range(len(rows)))
        if shuffle:
            random.shuffle(idx)
        for s in range(0, len(idx), BATCH):
            chunk = [rows[i] for i in idx[s: s + BATCH]]
            enc = tok([r["text"] for r in chunk], truncation=True, max_length=MAX_LEN, padding=True, return_tensors="pt")
            yield chunk, {k: v.to(a.device) for k, v in enc.items()}

    opt = torch.optim.AdamW(model.parameters(), lr=LR, weight_decay=0.01)
    steps = a.epochs * ((len(train) + BATCH - 1) // BATCH)
    sched = get_linear_schedule_with_warmup(opt, int(0.1 * steps), steps)
    scaler = torch.amp.GradScaler("cuda", enabled=a.device == "cuda")
    t0, log = time.time(), []
    for epoch in range(a.epochs):
        model.train()
        total = 0.0
        for chunk, enc in batches(train, shuffle=True):
            labels = torch.tensor([label_of[str(r["label"])] for r in chunk], device=a.device)
            with torch.autocast(a.device, dtype=torch.float16, enabled=a.device == "cuda"):
                loss = model(**enc, labels=labels).loss
            scaler.scale(loss).backward()
            scaler.unscale_(opt)
            torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            scaler.step(opt)
            scaler.update()
            opt.zero_grad(set_to_none=True)
            sched.step()
            total += loss.item() * len(chunk)
        log.append({"epoch": epoch + 1, "avg_loss": total / max(1, len(train)), "elapsed_s": round(time.time() - t0)})
        print(f"epoch {epoch + 1}/{a.epochs} loss {log[-1]['avg_loss']:.4f} {log[-1]['elapsed_s']}s", flush=True)

    model.eval()
    name = f"minilm-l6-{SHORT[a.example]}"
    out_dir = REPO / "examples" / a.example / "baselines"
    out_dir.mkdir(parents=True, exist_ok=True)
    correct = n = 0
    with open(out_dir / f"{name}.jsonl", "w", encoding="utf-8", newline="\n") as f, torch.no_grad():
        for split, rows in splits.items():
            for chunk, enc in batches(rows, shuffle=False):
                probs = torch.softmax(model(**enc).logits.float(), -1).cpu().numpy()
                for r, p in zip(chunk, probs):
                    f.write(json.dumps({"id": r["id"], "split": split, "label": str(r["label"]),
                                        "probabilities": {k: round(float(v), 6) for k, v in zip(keys, p)}}) + "\n")
                    if split == "heldout":
                        n += 1
                        correct += int(keys[int(p.argmax())] == str(r["label"]))
    training = {"model": MODEL, "revision": REVISION, "licence": "Apache-2.0", "params": sum(p.numel() for p in model.parameters()),
                "example": a.example, "train_rows": len(train), "epochs": a.epochs, "batch": BATCH, "lr": LR,
                "max_len": MAX_LEN, "seed": SEED, "device": a.device,
                "gpu": torch.cuda.get_device_name(0) if a.device == "cuda" else None, "epochs_log": log,
                "wall_time_s": round(time.time() - t0), "heldout_accuracy_quick_check": correct / max(1, n),
                "torch": torch.__version__, "python": platform.python_version(), "limit": a.limit}
    (out_dir / f"{name}.training.json").write_text(json.dumps(training, indent=2), encoding="utf-8")
    print(f"[{name}] {training['params']:,} params, held-out accuracy (quick check) {training['heldout_accuracy_quick_check']:.4f}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
