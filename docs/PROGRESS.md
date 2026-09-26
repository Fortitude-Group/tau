# Tau — Progress

Updated after every completed task. On session restart, resume from here without asking.

**Current release:** R1 (Runtime core + ONNX parity), branch `001-runtime-onnx-parity`.
**Gate status:** R1 in progress. The R1 gate stops for Rob's "go".
**Task list:** `specs/001-runtime-onnx-parity/tasks.md` (ticked as tasks complete).

---

## Done

### Setup and planning
- **2026-09-26 · Grounding.** .NET 10.0.400, Python 3.12, uv 0.11.32, gh as `fortitude-omnis`,
  GPU RTX 3080 Ti 12 GB (driver 610.47), 68 GB RAM. Codenames resolved to real artefacts.
  *Evidence:* `docs/DECISIONS.md`.
- **2026-09-26 · Constitution v1.6.0** (Tau fork of the base). *Evidence:* `.specify/memory/constitution.md`.
- **2026-09-27 · R1 spec, clarify (2 questions answered by Rob), plan, tasks, analyze.** *Evidence:*
  `specs/001-runtime-onnx-parity/`. Analyze: 0 critical, all fixes applied.

### Implementation
- **T001–T003, T007 · Scaffolding.** Apache-2.0 LICENSE and NOTICE (encoder licences verified:
  ModernBERT Apache-2.0, mmBERT MIT), central pinned packages, `Tau.slnx` with 12 projects,
  `scripts/build-test.ps1`. *Evidence:* `dotnet build Tau.slnx` shows 0 warnings, 0 errors.
- **T004 · Models fetched.** `scripts/fetch-models.ps1` checks `models.lock.json` (sha256 from Hugging
  Face's own LFS metadata at the pinned revisions). 22/22 files verified, 5.5 GB. *Evidence:* command output.
- **T005 · Sidecar env.** `uv.lock`: torch 2.11.0+cu128 (CUDA available), transformers 5.17.0,
  laya 0.3.20, von-sdk 1.2.3, onnx 1.23.0, onnxruntime 1.30.0, Python 3.12.9.
- **T010–T012, T017 · ONNX export spike.** Approach A (TorchScript) exports, but it bakes the traced
  sequence length into a reshape, so it's wrong for any other length. Rejected. Approach B (dynamo)
  works for all four models. *Evidence:* DECISIONS "ONNX export spike".
- **T013–T015 · Parity corpus, level-1 parity, fixtures.** 51 hand-written cases (all question types,
  1–255 options, text/object/array/null/empty/number states, truncation, special tokens, 13 scripts).
  **All four models PASS: 293 rows, max |Δlogit| 5.5e-5, max |Δprob| 4.0e-6, 0 argmax mismatches**
  (tolerance 2e-3 / 1e-3). *Evidence:* `reports/r1/parity-model.json`, reproduced by
  `uv run python -m tau_sidecar.parity` (from `sidecar/finetune`). Fixtures are in `tests/fixtures/parity/`.
- **T016 · Sidecar tests.** 8/8 pass (manifest hashes, determinism, ONNX smoke parity).
  *Evidence:* `uv run pytest` in `sidecar/finetune`.
- **T018–T020 · Tau.Contract** (worktree agent, merged). 113 tests pass.
- **T021–T022 · Tau.Calibration** (worktree agent, merged). 95 tests pass.
- **T059 (research part) · Dataset licences.** Banking77 is CC-BY-4.0 (publishable). The best urgency set
  (`Tobi-Bueck/customer-support-tickets`) is CC-BY-NC-4.0. **Put to Rob at the R1 gate.**

## In flight

- T006/T023–T026 (serialisers, tokeniser, ORT natives and providers): worktree agent.
- T037 (script router port with Python-generated Unicode tables): worktree agent.

## Next

- T027 ModelPackage loader, then US1 builders (T028/T029) once the tokeniser/serialiser API lands.
- Then US1 C# parity (T030–T036), then US2.

## Gate items for Rob (collected so far)

1. The brainstorm's "encode the state once" is wrong about the mechanism. Batching per request
   was agreed in clarify, and the latency claim still holds.
2. Dataset licence: the best urgency set is CC-BY-NC-4.0. Decision needed (see DECISIONS "Datasets").
3. GPU is an RTX 3080 **Ti** (brief said 3080). Reports state the Ti.
