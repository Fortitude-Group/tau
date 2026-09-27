"""Fine-tune a Laya checkpoint on a Tau worked example, on one GPU.

Adapted from Laya's own fine-tuning script, `train_ddp.py`, in the notebook
`notebooks/laya_finetune_typed_decisions_2xT4_kaggle.ipynb` of github.com/NandhaKishorM/laya at commit
4066d5d5fbf08b66c6757ddeedbd797bd7655bc0 (Apache License 2.0). Same objective and optimiser: a policy
gradient on Laya's strictly proper reward (log + spherical + ranked probability score) plus soft
cross-entropy, AdamW with separate encoder/head learning rates, a cosine schedule, fp16 autocast,
gradient checkpointing, a held-back slice for post-hoc per-type temperature. Changes, all recorded in
`training.json`:
- one GPU instead of DDP; micro-batch 4 x accumulation 16 keeps the original effective batch of 64;
- training rows are built from the example's decision.yaml, so the model is trained on exactly the
  question it will be measured with;
- `max_len`/`head_max_len` come from the example (Banking77 needs room for 77 options);
- the held-back temperature slice comes from the fine-tune split only; Tau's calibration and held-out
  splits are never seen here.

Usage: uv run python -m tau_sidecar.finetune_laya --example banking77 [--epochs 2]
Writes models/src/<id>/ in Laya's checkpoint layout, so the R1 export and parity run unchanged.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import random
import shutil
import sys
import time
from pathlib import Path

import torch
import yaml
from safetensors.torch import load_file, save_file

from .reference import MODELS_DIR, REPO, set_deterministic, work_dir

SETTINGS = {
    # example: (base checkpoint, output id, max_len, head_max_len)
    "banking77": ("laya-en", "laya-en-ft-banking77", 1024, 896),
    "support-tickets": ("laya-en", "laya-en-ft-tickets", 512, 192),
}
EPOCHS, MICRO_BATCH, GRAD_ACCUM, GROUP_SIZE = 2, 4, 16, 4
LR_ENCODER, LR_HEAD, SIGMA_START, SIGMA_END = 2.5e-5, 1.0e-4, 0.4, 0.1
CALIB_MAX, SEED = 400, 42


def load_example(name: str):
    spec = yaml.safe_load((REPO / "examples" / name / "decision.yaml").read_text(encoding="utf-8"))
    q = spec["question"]
    t, opts = q["type"], q["options"]
    crit = dict(opts) if t == "choice" else list(opts)
    keys = list(crit.keys()) if t == "choice" else [str(i) for i in range(len(crit))]
    internal = {"t": t, "ins": q["instructions"], "crit": crit}
    rows = [json.loads(l) for l in (REPO / "data" / spec["data"]["dataset"] / "finetune.jsonl").read_text(encoding="utf-8").splitlines() if l]
    return spec, internal, keys, rows


def build_items(tok, internal, keys, rows, max_len, head_max_len):
    from laya.common import QTYPES, build_sequence, render_options

    k = len(render_options(internal))
    items, skipped = [], 0
    for r in rows:
        label = keys.index(str(r["label"]))
        seq, markers = build_sequence(tok, r["text"], internal, max_len, head_max_len)
        if len(markers) != k:
            skipped += 1
            continue
        target = [0.0] * k
        target[label] = 1.0
        items.append({"ids": seq, "markers": markers, "qtype": QTYPES[internal["t"]], "target": target, "label": label})
    return items, skipped


def collate(items, pad_id):
    n, L = len(items), max(len(it["ids"]) for it in items)
    kmax = max(2, max(len(it["markers"]) for it in items))
    ids = torch.full((n, L), pad_id, dtype=torch.long)
    att = torch.zeros((n, L), dtype=torch.long)
    mpos = torch.zeros((n, kmax), dtype=torch.long)
    mmask = torch.zeros((n, kmax), dtype=torch.bool)
    target = torch.zeros((n, kmax), dtype=torch.float32)
    for i, it in enumerate(items):
        ids[i, : len(it["ids"])] = torch.tensor(it["ids"])
        att[i, : len(it["ids"])] = 1
        kk = len(it["markers"])
        mpos[i, :kk] = torch.tensor(it["markers"])
        mmask[i, :kk] = True
        target[i, :kk] = torch.tensor(it["target"])
    return {"input_ids": ids, "attention_mask": att, "marker_pos": mpos, "marker_mask": mmask, "target": target,
            "qtype": torch.tensor([it["qtype"] for it in items])}


def fit_one_temp(sel):
    """As the original: LBFGS on log T, clamped to [0.1, 10]."""
    if len(sel) < 10:
        return 1.0
    kmax = max(len(z) for z, _ in sel)
    Z = torch.full((len(sel), kmax), -1e4)
    T = torch.zeros((len(sel), kmax))
    for i, (z, t) in enumerate(sel):
        Z[i, : len(z)] = torch.tensor(z)
        T[i, : len(t)] = torch.tensor(t, dtype=torch.float32)
    log_t = torch.zeros(1, requires_grad=True)
    opt = torch.optim.LBFGS([log_t], lr=0.1, max_iter=100)

    def closure():
        opt.zero_grad()
        loss = -(T * torch.log_softmax(Z / log_t.exp(), -1)).sum(-1).mean()
        loss.backward()
        return loss

    opt.step(closure)
    return float(torch.clamp(log_t.exp(), 0.1, 10.0).item())


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--example", required=True, choices=sorted(SETTINGS))
    ap.add_argument("--epochs", type=int, default=EPOCHS)
    ap.add_argument("--limit", type=int, default=0, help="train on the first N rows only (smoke tests)")
    a = ap.parse_args(argv)
    base_id, out_id, max_len, head_max_len = SETTINGS[a.example]

    from laya.agent import Agent
    from laya.common import build_model, proper_reward

    set_deterministic()
    torch.use_deterministic_algorithms(False)  # training kernels on CUDA aren't all deterministic; seeds are fixed
    random.seed(SEED)
    device = torch.device("cuda")
    base_dir = work_dir(base_id)
    spec, internal, keys, rows = load_example(a.example)
    if a.limit:
        rows = rows[: a.limit]

    agent = Agent(str(base_dir), device="cpu")  # the vendor loader: config fixes, tokenizer, weights
    tok, cfg = agent.tok, dict(agent.cfg)
    cfg.update({"max_len": max_len, "head_max_len": head_max_len})
    model = agent.model
    del agent
    model.encoder.gradient_checkpointing_enable(gradient_checkpointing_kwargs={"use_reentrant": False})
    model.head_checkpointing = True
    model.to(device).train()

    items, skipped = build_items(tok, internal, keys, rows, max_len, head_max_len)
    order = list(range(len(items)))
    random.Random(20260922).shuffle(order)  # the original's seed for the held-back slice
    n_calib = min(CALIB_MAX, len(items) // 10)
    calib_items = [items[i] for i in sorted(order[:n_calib])]
    train_items = [items[i] for i in sorted(order[n_calib:])]
    print(f"[{out_id}] {len(train_items)} train items, {len(calib_items)} held back for temperature, "
          f"{skipped} skipped (options didn't fit), {a.epochs} epochs", flush=True)

    enc = [p for n, p in model.named_parameters() if n.startswith("encoder.")]
    head = [p for n, p in model.named_parameters() if not n.startswith("encoder.")]
    optimizer = torch.optim.AdamW([{"params": enc, "lr": LR_ENCODER}, {"params": head, "lr": LR_HEAD}], weight_decay=0.01)
    total_updates = max(1, (len(train_items) // (MICRO_BATCH * GRAD_ACCUM)) * a.epochs)
    scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(optimizer, T_max=total_updates, eta_min=1e-6)
    scaler = torch.amp.GradScaler("cuda", enabled=True)
    t0, log = time.time(), []

    for epoch in range(a.epochs):
        random.seed(SEED + epoch)
        random.shuffle(train_items)
        sigma = SIGMA_START + (SIGMA_END - SIGMA_START) * (epoch / max(1, a.epochs - 1))
        optimizer.zero_grad(set_to_none=True)
        epoch_loss, n_batches = 0.0, 0
        for b_idx in range(0, len(train_items), MICRO_BATCH):
            batch = collate(train_items[b_idx: b_idx + MICRO_BATCH], tok.pad_token_id)
            with torch.autocast("cuda", dtype=torch.float16):
                logits, act = model(batch["input_ids"].to(device), batch["attention_mask"].to(device),
                                    batch["marker_pos"].to(device), batch["marker_mask"].to(device), batch["qtype"].to(device))
            logits = logits.float()
            mask = batch["marker_mask"].to(device)
            k = mask.sum(-1, keepdim=True).float()
            target = batch["target"].to(device)
            eps = torch.randn((GROUP_SIZE,) + logits.shape, device=device) * sigma * mask
            eps = (eps - eps.sum(-1, keepdim=True) / k) * mask
            z = logits.detach().unsqueeze(0) + eps
            q = torch.softmax(z.masked_fill(~mask, -1e4), -1)
            with torch.no_grad():
                r = proper_reward(q, target.unsqueeze(0), batch["qtype"].to(device), mask, w_sph=0.75, w_rps=1.0)
                adv = (r - r.mean(0, keepdim=True)) / (r.std() + 1e-6)
            logp = -(((z - logits.unsqueeze(0)) ** 2) * mask).sum(-1) / (2 * sigma ** 2)
            loss_rl = -(adv * logp).mean()
            loss_ce = -(target * torch.log_softmax(logits.masked_fill(~mask, -1e4), -1)).sum(-1).mean()
            loss = (loss_rl + loss_ce) / GRAD_ACCUM + 0.0 * act.sum()
            scaler.scale(loss).backward()
            n_batches += 1
            if n_batches % GRAD_ACCUM == 0 or b_idx + MICRO_BATCH >= len(train_items):
                scaler.unscale_(optimizer)
                torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
                scaler.step(optimizer)
                scaler.update()
                scheduler.step()
                optimizer.zero_grad(set_to_none=True)
            epoch_loss += loss.item() * GRAD_ACCUM
            if n_batches % 100 == 0:
                print(f"  epoch {epoch + 1}/{a.epochs} step {n_batches} loss {loss.item() * GRAD_ACCUM:.4f} "
                      f"reward {r.mean().item():.3f} {time.time() - t0:.0f}s", flush=True)
        log.append({"epoch": epoch + 1, "avg_loss": epoch_loss / max(1, n_batches), "elapsed_s": round(time.time() - t0)})
        print(f"=== epoch {epoch + 1} done, avg loss {log[-1]['avg_loss']:.4f}, {log[-1]['elapsed_s']}s", flush=True)

    # Post-hoc per-type temperature on the held-back slice, as the original.
    model.eval()
    preds = []
    with torch.no_grad():
        for c in range(0, len(calib_items), 16):
            chunk = calib_items[c: c + 16]
            cb = collate(chunk, tok.pad_token_id)
            with torch.autocast("cuda", dtype=torch.float16):
                lg, _ = model(cb["input_ids"].to(device), cb["attention_mask"].to(device), cb["marker_pos"].to(device),
                              cb["marker_mask"].to(device), cb["qtype"].to(device))
            lg = lg.float().cpu().numpy()
            for r_i, it in enumerate(chunk):
                preds.append((it["qtype"], lg[r_i, : len(it["markers"])].tolist(), it["target"]))
    temps = list(cfg.get("temperature", [1.0, 1.0, 1.0]))
    for qt in range(3):
        sel = [(z, t) for q_t, z, t in preds if q_t == qt]
        if sel:
            temps[qt] = fit_one_temp(sel)

    out = MODELS_DIR / "src" / out_id
    if out.exists():
        shutil.rmtree(out)
    (out / "encoder").mkdir(parents=True)
    save_file({k: v.half().contiguous().cpu() for k, v in model.state_dict().items()}, str(out / "model.safetensors"))
    model.encoder.config.save_pretrained(str(out / "encoder"))
    shutil.copytree(MODELS_DIR / "src" / base_id / "tokenizer", out / "tokenizer")  # the base model's pinned files
    cfg.update({"fine_tuned": True, "model_name": out_id, "temperature": temps})
    cfg.pop("temperature_by_options", None)  # fitted per type; inherited buckets would hide the new values
    (out / "rl_agent_config.json").write_text(json.dumps(cfg, indent=2), encoding="utf-8")
    manifest = json.loads((REPO / "examples" / a.example / "dataset.manifest.json").read_text(encoding="utf-8"))
    training = {
        "id": out_id, "base": base_id, "example": a.example, "recipe": "NandhaKishorM/laya train_ddp.py @ 4066d5d5, single-GPU",
        "dataset_manifest_sha256": hashlib.sha256(json.dumps(manifest, sort_keys=True).encode()).hexdigest(),
        "train_items": len(train_items), "temperature_items": len(calib_items), "skipped_items": skipped,
        "epochs": a.epochs, "micro_batch": MICRO_BATCH, "grad_accum": GRAD_ACCUM, "group_size": GROUP_SIZE,
        "lr_encoder": LR_ENCODER, "lr_head": LR_HEAD, "sigma": [SIGMA_START, SIGMA_END], "seed": SEED,
        "max_len": max_len, "head_max_len": head_max_len, "fitted_temperature": temps, "epochs_log": log,
        "wall_time_s": round(time.time() - t0), "gpu": torch.cuda.get_device_name(0), "torch": torch.__version__,
        "python": platform.python_version(), "limit": a.limit,
    }
    (out / "training.json").write_text(json.dumps(training, indent=2), encoding="utf-8")
    print(f"[{out_id}] saved to {out} in {training['wall_time_s']}s; temperatures {temps}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
