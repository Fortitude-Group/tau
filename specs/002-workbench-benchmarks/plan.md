# Implementation Plan: Tau R2 — Workbench and benchmarks

**Branch**: `002-workbench-benchmarks` | **Date**: 2026-09-27 | **Spec**: [spec.md](spec.md)

## Summary

Build `tau`, a .NET global tool whose stages (define, label, fine-tune, measure, calibrate, threshold,
cascade, report, run) work against any `/v1/systemone` endpoint, with artefacts on disk between stages. Run
it end to end on Banking77 (real) and a synthetic support-ticket set, measuring laya-en, Von, fine-tuned
Laya and (on tickets) laya-typed-decisions, with a fine-tuned 22M classic encoder as a baseline. Frontier
answers come from this Claude Code session's subagents, cached and committed. One design change to R1:
calibrators act on log reference probabilities (research R-01), so the Workbench and the Runtime compute
the same function. Details: [research.md](research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (Workbench, Runtime change). Python 3.12 sidecar (data prep, fine-tune,
classic baseline, export).

**Primary Dependencies**: R1 projects. YamlDotNet (new, MIT). Sidecar adds `datasets`/`pandas` (data prep),
and `sentence-transformers` weights via `transformers` (baseline).

**Storage**: files under `examples/<name>/`. Datasets are downloaded to `data/` (gitignored). Fine-tuned
checkpoints go to `models/src/<ft-id>/` and packages to `models/<ft-id>/` (gitignored, hash-pinned in
manifests).

**Testing**: xUnit v3 for the Workbench (unit tests on metrics glue, splits, cache, threshold, cascade,
cost, report rendering, and the Workbench-vs-Runtime calibrator equality test). pytest for the new
sidecar modules. End-to-end: the two worked examples.

**Target Platform**: Windows reference machine (RTX 3080 Ti) for the committed runs. The Workbench itself
is cross-platform.

**Project Type**: CLI tool + libraries + Python sidecar.

**Performance Goals**: none gating. Fine-tuning is kept under about 2 h per dataset (research R-02).

**Constraints**: zero spend. At most 1,000 frontier-answered held-out items per dataset, plus 200 second-prompt
answers. No ticket rows committed. No vehicle/fleet content. Every number comes from one command.

**Scale/Scope**: 2 datasets × up to 5 models × 1,000 held-out + 1,000 calibration items.

## Constitution Check

| Principle | Status | How |
| --- | --- | --- |
| I Modular | ✅ | Stages in `Tau.Workbench` (library). CLI is a thin shell. The sidecar is isolated. |
| II Contract/SemVer | ✅ | `/v1/systemone` unchanged. Calibrator v1 semantics clarified (R-01) and documented. Packages stay 0.x. |
| III Tests | ✅ | Unit tests per stage, the calibrator-equality test, sidecar tests, two end-to-end examples. |
| IV Deterministic/observable | ✅ | Fixed seeds, sorted outputs, runs record everything. |
| V Simplicity | ✅ | Inline SVG over a charting framework. Reuses R1 export/parity. |
| VI Complete scope | ✅ | FR-020 (CPU re-bench) included. |
| IX Ask, then wait | ✅ | Clarify answered. The R2 gate stops for "go". |
| X Production | ✅ | Nothing published. The repo stays private. |
| XI Mechanism first | ✅ | Datasets, recipe and prices verified from primary sources. |
| XII Explain every number | ✅ | Report text states what, why and what follows for each figure. |
| XIII Measured, not claimed | ✅ | Committed reports, one command each, synthetic data labelled, misses published. |
| XIV Contract-first | ✅ | The Workbench uses only the contract plus `x-tau-raw`. |
| XV One calibration library | ✅ | All fitting and metrics via `Tau.Calibration`. The equality test enforces it. |
| XVI Zero cost | ✅ | Frontier via the session. Cache, caps and prices only for estimates. |
| XVII Exclusions | ✅ | Vehicle/transport filters, counted in the manifest. Ticket rows never committed. |

Result: **PASS**.

## Project Structure

```text
src/
├── Tau.Workbench/            # stages: Spec, Data, Frontier, Measure, Calibrate, Threshold, Cascade, Cost, Report
├── Tau.Workbench.Cli/        # `tau` global tool (label, measure, calibrate, threshold, cascade, report, run)
├── Tau.Client/               # + per-request headers (x-tau-raw)
├── Tau.Inference/            # Engine: calibrators applied to log reference probabilities (R-01)
sidecar/finetune/tau_sidecar/
├── data_banking77.py         # fetch @ pinned commit, stratified splits → data/banking77/*.jsonl + manifest
├── data_tickets.py           # fetch @ pinned rev, English, vehicle/transport filters, dedup, splits + manifest
├── finetune_laya.py          # adapted from Laya's train_ddp.py (single GPU)
├── baseline_minilm.py        # classic 22M encoder fine-tune → held-out probabilities JSONL
examples/
├── banking77/                # decision.yaml, dataset.manifest.json, frontier/, runs/, calibrators/, report.*
└── support-tickets/          # same (no rows)
tests/Tau.Workbench.Tests/
scripts/examples.ps1          # one command per example: prepare → (fine-tune) → tau run
```

## Complexity Tracking

| Addition | Why needed | Simpler alternative rejected because |
| --- | --- | --- |
| Changing R1's calibrator input to log reference probabilities | Workbench and Runtime must compute the same function (XV) | Leaving it would ship calibrators that mean different things in the two places |
| A second CLI project | `PackAsTool` wants an exe, and the stages must be testable as a library | One project would make the CLI the unit of reuse (violates I) |
