# Tasks: Tau R2 — Workbench and benchmarks on two datasets

**Input**: `specs/002-workbench-benchmarks/` (spec, plan, research, data-model, quickstart).
**Tests**: required (Principle III; SC-007 equality test).
**Standing rule**: after every completed task, tick it here and add a line to `docs/PROGRESS.md` in the same commit.
**Money rule**: no paid API calls anywhere. Frontier answers come from this session. Give Rob a usage estimate before bulk frontier labelling.
**Test traits**: `Category=Models` for tests needing fetched models or a running Runtime.

## Parallel lanes

| Lane | Tasks | Independent because |
| --- | --- | --- |
| **A: sidecar data** | T010–T013 | Python only, owns `sidecar/`, `data/`, `examples/*/dataset.manifest.json` |
| **B: Workbench core** | T020–T031 | C# only, owns `src/Tau.Workbench*`, `tests/Tau.Workbench.Tests` |
| **C: R1 adjustments** | T040–T042 | Owns `src/Tau.Inference/Engine`, `src/Tau.Client`, their tests |
| **D: fine-tune + baseline** | T014–T017 | Needs A's splits. GPU-heavy, so serial with other GPU work |
| **E: frontier labelling** | T050–T053 | Needs A's held-out ids + B's batch export. Session tokens |

Serial by necessity: the end-to-end runs (T060+), because they need all lanes and the GPU.

---

## Phase 1: Setup

- [X] T001 Add projects `src/Tau.Workbench` (library), `src/Tau.Workbench.Cli` (global tool: `PackAsTool`, `ToolCommandName=tau`), `tests/Tau.Workbench.Tests`. Add YamlDotNet (pinned, MIT) to `Directory.Packages.props`. Add all three to `Tau.slnx`. Build clean.
- [x] T002 Add sidecar deps (`datasets`, `pandas`) to `sidecar/finetune/pyproject.toml`, re-lock (`uv lock`), sync.

## Phase 2: Lane A (data)

- [x] T010 [P] `tau_sidecar/data_banking77.py`: download `train.csv` and `test.csv` from `PolyAI-LDN/task-specific-datasets` @ `9d081458…` into `data/banking77/raw/` and verify sha256 (recorded in the script). Stratified seed-42 splits: calibration 1,000 from train, held-out 1,000 from test, finetune = rest of train. Write `data/banking77/{calibration,heldout,finetune}.jsonl` (`id`, `text`, `label`) and `examples/banking77/dataset.manifest.json` (licence, `synthetic: false`, counts, per-class counts, split sha256s).
- [x] T011 [P] `tau_sidecar/data_tickets.py`: download the pinned revision of `Tobi-Bueck/customer-support-tickets` (hf_hub, pinned `ddf1c81a…`). Filters: English. Drop `queue` starting `Autos & Vehicles` or `Travel & Transportation`. Drop rows whose subject or body matches the vehicle/fleet keyword list (word-boundary, case-insensitive, list in the script). Drop exact-duplicate text (subject + body). Text = subject + "\n\n" + body. Label = priority level index (very_low=0 … critical=4). Stratified seed-42 splits: held-out 1,000, calibration 1,000, finetune ≤ 8,000. Write the JSONL to `data/tickets/` (gitignored) and `examples/support-tickets/dataset.manifest.json` (licence CC-BY-NC-4.0, `synthetic: true`, rows removed per filter, counts).
- [x] T012 [P] `sidecar/finetune/tests/test_data.py`: split determinism, disjointness, no vehicle keywords in the output, and manifest counts match the files.
- [x] T013 Run T010/T011, commit manifests, record facts in DECISIONS.

## Phase 3: Lane C (R1 adjustments)

- [X] T040 [P] Engine: apply calibrators to **log reference probabilities** (research R-01). Laya: the reference probability vector. Von: reference probabilities. Noul: `[1−p, p]` (Laya) or `[p_true, p_false]` (Von), mapped back. Clamp at 1e-6. Update `CalibrationHookTests` expectations if the mechanism changes a number.
- [X] T041 [P] `Tau.Calibration`: a `Calibrator.ApplyToProbabilities(double[] p)` helper (log, clamp, then the existing apply), used by both the Engine and the Workbench. Unit tests.
- [X] T042 [P] `Tau.Client`: per-request headers (`SystemOneAsync(request, headers, ct)`) for `x-tau-raw`. Tests.

## Phase 4: Lane B (Workbench core)

- [X] T020 [P] `DecisionSpec` (YamlDotNet): load, validate (type-specific options, models, pricing), and resolve paths relative to the spec file. Tests.
- [X] T021 [P] `Dataset`: read prepared JSONL splits and the manifest, and verify split sha256 against the manifest. Tests.
- [X] T022 [P] `Frontier`: batch export (versioned prompt templates `v1`, `v1-alt`; ≤1,000 held-out per dataset; alt subset 200 chosen with seed 42; skip cached keys), cache ingest with validation, overrides, provenance, and a character tally for cost. `tau label` exits 2 when answers are pending. Tests (never re-export cached keys, cap enforced, invalid answers rejected).
- [X] T023 [P] `Measure`: POST each item via `Tau.Client` with `x-tau-raw: true` (raw phase) or without (calibrated phase), with bounded concurrency (4), deterministic output order, per-item record, and failures captured. Summary metrics via `Tau.Calibration` (accuracy, ECE max-p 15 bins, Brier, log loss, MAE for score). Endpoint identity from `/v1/models`. GPU power sampled via nvidia-smi when local. Tests against a stub endpoint.
- [X] T024 [P] `Calibrate`: per question type (and option-count bucket where n ≥ 200), fit temperature on the calibration split's log reference probabilities, and isotonic on max-p confidence (with fallback to temperature when n < 200). Write R1 calibrator files, choosing the method with lower calibration-split log loss and recording both. Tests on synthetic data (recovers T, isotonic monotone).
- [X] T025 [P] `Threshold`: τ from calibration-split calibrated confidences for the target error. Held-out accept rate and accuracy. Curve (τ grid of 0.00–1.00 by 0.01). "Unreachable" result when no τ meets the target. Tests.
- [X] T026 [P] `Cascade`: blend local (≥ τ) with frontier answers from the cache. Shares, accuracies (local-only, frontier-only, blended), local latency p50. Tests with hand-made inputs.
- [X] T027 [P] `Cost`: frontier tokens from the character tally (chars/4 × tokenizer factor), prices per row, USD→GBP, local kWh from power × time, per 1,000 and per million. Every figure labelled an estimate. Tests with hand-computed values.
- [X] T028 `Report`: `report.json` plus a self-contained `report.html` (inline SVG reliability diagrams before/after per model, trade-off curve, cost table, confusion matrix or top-confusions table, label noise, baseline table, metadata block, and a what/why/what-follows sentence per figure). Load the `dataviz` skill before writing chart code. No scripts, no external assets. Snapshot test on a fixed input.
- [X] T029 `Baselines`: read sidecar probability files and compute the same metrics via `Tau.Calibration`.
- [X] T030 `tau` CLI: `label`, `measure`, `calibrate`, `threshold`, `cascade`, `report`, `run` (skips stages whose artefacts are newer than their inputs). Exit codes per contracts. `dotnet pack` works locally, not pushed.
- [ ] T031 SC-007 test: the same calibrator file gives identical probabilities through the Workbench path and through the Runtime (in-process, `Category=Models`).

## Phase 5: Lane D (fine-tune + baseline, GPU)

- [X] T014 `tau_sidecar/finetune_laya.py`: adapt `train_ddp.py` from `NandhaKishorM/laya` @ `4066d5d5…` to one GPU (attribution header; same objective: proper-reward policy gradient + soft CE, AdamW encoder/head learning rates, cosine, fp16 autocast, gradient checkpointing). Train on the finetune split, seed 42. Write the checkpoint in Laya layout to `models/src/<ft-id>/` with `training.json` (data manifest hash, settings, epochs, wall time, GPU).
- [X] T015 Extend `export_laya`/`manifest`/`parity` for local fine-tuned ids (source `local-finetune`, no lock entry, cases applied as for laya-en). Export and run parity for `laya-en-ft-banking77` and `laya-en-ft-tickets`. Reports go to `examples/<name>/finetune-parity.json`.
- [X] T016 `tau_sidecar/baseline_minilm.py`: fine-tune `sentence-transformers/all-MiniLM-L6-v2` (pinned revision, Apache-2.0) with a classification head on the finetune split. Write held-out and calibration probability JSONL to `examples/<name>/baselines/minilm-l6-<name>.jsonl` with `training.json`.
- [X] T017 [P] pytest for the fine-tune data collation and the baseline output format (tiny smoke run on CPU with a few items).

## Phase 6: Lane E (frontier labelling: session tokens)

- [X] T050 Export batches for both examples (`tau label`). Report the pending item and character counts. **Give Rob a usage estimate before T051.**
- [X] T051 Answer the `v1` batches via Agent-tool subagents (the session's model), about 200 items per batch, writing `frontier/cache.jsonl`. Validate by re-running `tau label` (0 pending).
- [X] T052 Answer the `v1-alt` 200-item subsets likewise.
- [X] T053 Commit the caches. Record counts, dates and the session model in DECISIONS.

## Phase 7: End to end + gate

- [ ] T060 `scripts/examples.ps1 -Example <name>`: prepare (if needed) → fine-tune/export/parity (if packages are missing) → start the Runtime (CUDA, needed models, free port) → `tau run` → restart with calibrators → calibrated phase → report. It stops only the processes it started.
- [ ] T061 Run banking77 end to end. Commit `examples/banking77/**` (no raw data).
- [ ] T062 Run support-tickets end to end. Commit (no ticket rows).
- [ ] T063 Re-run the R1 benchmark on the quiet machine (`scripts/bench.ps1 -Http`). Commit `reports/r1/latency*`.
- [ ] T064 Check SC-002 (≥50% relative ECE drop for the out-of-the-box model on each dataset, or the reason recorded). Cross-check every FR/SC against evidence in PROGRESS.
- [ ] T065 Clean-clone `build-test`. Merge to `master`, push to the private repo.
- [ ] T066 **R2 GATE: stop.** Report to Rob with evidence, misses and an R3 estimate.

## Dependencies

Phase 1 → all. A → D and E (splits and ids). B's T022 → E. C → B's T031 and the calibrated phase. D + E + B + C → Phase 7.
