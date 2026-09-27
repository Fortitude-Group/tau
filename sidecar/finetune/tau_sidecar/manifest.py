"""Write and verify `tau-model.json` package manifests (schema: specs/001-runtime-onnx-parity/contracts/model-package.schema.json)."""
from __future__ import annotations

import hashlib
import json
import shutil
from pathlib import Path
from typing import Any, Dict

from .reference import MODELS_DIR, REPO, src_dir


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def lock_entry(model_id: str) -> Dict[str, Any]:
    lock = json.loads((REPO / "models.lock.json").read_text())
    for m in lock["models"]:
        if m["id"] == model_id:
            return m
    raise KeyError(model_id)


def package_dir(model_id: str) -> Path:
    d = MODELS_DIR / model_id
    d.mkdir(parents=True, exist_ok=True)
    return d


def write_manifest(model_id: str, family: str, onnx_path: Path, exporter: str, opset: int,
                   tokenizer_rel: str, limits: Dict[str, Any], post: Dict[str, Any]) -> Path:
    """Copy the tokeniser files into the package and write tau-model.json with every hash."""
    import torch
    import transformers

    lock = lock_entry(model_id)
    pkg = package_dir(model_id)
    tok_src = src_dir(model_id) / tokenizer_rel
    shutil.copy2(tok_src / "tokenizer.json", pkg / "tokenizer.json")
    shutil.copy2(tok_src / "tokenizer_config.json", pkg / "tokenizer_config.json")
    ref_pkg = "laya==0.3.20" if family == "laya" else "von-sdk==1.2.3"
    manifest = {
        "format": "tau.model",
        "version": 1,
        "id": model_id,
        "family": family,
        "source": {
            "repo": lock["repo"],
            "revision": lock["revision"],
            "subfolder": lock["subfolder"],
            "files": {k: v["sha256"] for k, v in lock["files"].items()},
        },
        "onnx": {"file": onnx_path.name, "sha256": sha256(onnx_path), "opset": opset,
                 "exporter": exporter, "precision": "fp32",
                 **({"data_file": onnx_path.name + ".data",
                     "data_sha256": sha256(onnx_path.with_name(onnx_path.name + ".data"))}
                    if onnx_path.with_name(onnx_path.name + ".data").exists() else {})},
        "tokenizer": {"file": "tokenizer.json", "sha256": sha256(pkg / "tokenizer.json"),
                      "config": "tokenizer_config.json", "config_sha256": sha256(pkg / "tokenizer_config.json")},
        "limits": limits,
        "postProcessing": post,
        "reference": {"package": ref_pkg, "transformers": transformers.__version__,
                      "torch": torch.__version__.split("+")[0]},
    }
    out = pkg / "tau-model.json"
    out.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return out
