# Tau — Progress

Updated after every completed task. On session restart, resume from here without asking.

**Current release:** R2 (Workbench + benchmarks), branch `002-workbench-benchmarks`. R1 is merged to `master`.
**Gate status:** R1 passed (Rob's "go", 2026-09-27). R2 in progress.
**Task list:** `specs/002-workbench-benchmarks/tasks.md`.

## R2 log

- Spec, clarify (3 answers), plan, research R-01..R-09, tasks T001–T066 in lanes A–E.
- Lane D code: `finetune_laya.py`, the manifest/parity changes for local fine-tunes, and `baseline_minilm.py` are committed. They haven't been run yet because they need lane A's splits.
- Lane C (T040–T042) is merged. Calibrators now act on the log reference probabilities (DECISIONS). The client has per-request headers. Tests: Calibration 107, Client 54, Runtime 43, Contract 113, Inference 1,507 (+3 skipped), model parity 12/12, all green in the agent's worktree. After the merge, the full non-model suite passes on the branch.
- Lane B (T001, T020–T030) done in its worktree: `src/Tau.Workbench` (spec, data, frontier, measure, calibrate, threshold, cascade, cost, baselines, report), the `tau` global tool (`src/Tau.Workbench.Cli`, packs as `Tau.Workbench`), 143 tests green, solution builds 0/0. Isotonic calibrators are fitted one-vs-rest on every option's probability, because the shared calibrator applies them per option (a max(p) fit gave log loss about 10 on test data). T031 (the Runtime equality test) is still open.
- Still running: lane A (data).

---

## R1 gate: evidence against every requirement (T062)

| Requirement | Evidence | Status |
| --- | --- | --- |
| FR-001 export all four checkpoints | `scripts/export.ps1`. DECISIONS "ONNX export spike" (TorchScript rejected, dynamo passes) | met |
| FR-002 refuse mismatched hashes | `ModelPackageTests` (tampered ONNX, weights, tokeniser, manifest) | met |
| FR-003/004 parity command and case set | `scripts/parity.ps1` → `reports/r1/parity.md`. 51 cases in `tau_sidecar/cases.py` | met |
| FR-005 tokeniser parity | about 740 texts across 3 tokenisers, exact ids (parity report "Supporting gates") | met |
| FR-006 fallback if export fails | not needed: export passed. Recorded | n/a |
| FR-007–012 contract | 113 contract tests, strict response schema, pinned snapshot `contracts/systemone/2026-09-27/` | met |
| FR-013 one batched pass per request | `OnnxModel.RunLaya/RunVon` (Von chunks only if masks exceed 512 MB). Independence test | met |
| FR-014 reference post-processing | C# answer gate: 263 answers, 0 mismatches (`reports/r1/parity.md`) | met |
| FR-015 routing | 684/684 routing fixtures incl. reason strings. `jev-latest` routes | met |
| FR-016 determinism | identical bytes over 100 concurrent requests (`DeterminismTests`) | met |
| FR-017 CPU / CUDA / DirectML | `scripts/provider-smoke.ps1`: all three pass. Refuses to start without fallback | met |
| FR-018 shared calibration library | `Tau.Calibration`, 95 tests. Runtime consumes it, nothing reimplemented | met |
| FR-019/020 calibrator hook, raw flag | `CalibrationHookTests` (temperature, isotonic, raw bypass, bad or stale file stops start-up) | met |
| FR-021 observability | OTel + `/metrics` series test (`HostTests`) | met |
| FR-022 single file + container | `scripts/publish.ps1` (104 MB exe + `native/`). Local image `tau-runtime:r1-local` answers | met |
| FR-023 typed client | `Tau.Client` 44 tests incl. real-model round trip. `Tau.Client.0.1.0.nupkg` local only | met |
| FR-024 conformance vs Kev | `reports/r1/conformance.md`: Tau 45/45. Kev-0.8B 40/45 (accepts 5 invalid requests) | met |
| FR-025 latency | `reports/r1/latency*.md` (CUDA, CPU, HTTP, varied inputs) | met, see caveats |
| FR-026 provenance in reports | hardware, hashes, versions, date, command in every report | met, see caveats |
| FR-027 dataset licences | DECISIONS "Datasets" | met, **decision needed** |
| SC-001 parity 100% | `reports/r1/parity.md` | met |
| SC-002 tokeniser 100% | same | met |
| SC-003 unmodified client works | SDK-default `jev-latest` routes. Conformance 45/45 | met |
| SC-004 invalid → 422 | 12/12 invalid conformance requests, and the contract tests | met |
| SC-005 order/concurrency independence | same choice, values within 1e-4 (one rounding step). Identical bytes concurrently | met (recorded tolerance) |
| SC-006 latency per model/provider | all four models × CUDA/CPU × q=1/4/10, plus HTTP and varied inputs | met |
| SC-007 reports reproduce | two full benchmark runs agree within a few % on CUDA. Parity and conformance re-run on the final commit | met, see caveats |
| SC-008 all tests pass | clean clone of `c26162e`: build 0 warnings / 0 errors, 1,815 tests (1,812 pass, 3 designed GPU skips), sidecar 8/8 | met |

## Headline numbers (reference machine: RTX 3080 Ti 12 GB, driver 610.47, i9-11900K, FP32)

- **Parity:** every gate passes on all four models. Max |Δlogit| about 6e-5 against a 2e-3 tolerance. Sequences
  token-exact, answers identical in choice. → `reports/r1/parity.md`
- **CUDA, engine end to end, p50:** 1 question about 19–26 ms. 10 questions about 53 ms (multilingual) to
  123 ms (Von), i.e. about 5–12 ms per question batched. HTTP adds under 1 ms on the same machine. →
  `reports/r1/latency.md`
- **Varied inputs (every request a new length), CUDA:** p50 close to the fixed workload. p95 up to about
  1.6x. **Von q=10: p95 3.7 s**, reproduced in two runs, caused by VRAM pressure with all four FP32 models
  resident (215 ms with Von alone).
- **CPU (FP32, 8 threads):** 0.26–0.92 s for one question, 2.7–7.4 s for ten. Measured under about 50%
  background CPU load (see caveats).
- **Conformance:** Tau 45/45 against the pinned contract. Kev-0.8B 40/45.

## Caveats that belong with those numbers

1. **CPU figures are inflated by an unknown amount.** About 35% of the CPU during the benchmark was taken by
   processes that aren't Tau's: three orphaned `ccstatusline` shells (Claude Code's status-line helper,
   spinning since 02:11) and an orphaned `find / -iname *.nuget` (since 01:07, probably left by a subagent,
   parent gone). I haven't stopped them, because I can't prove they're mine. Re-run
   `./scripts/bench.ps1 -Http` after clearing them, before any CPU figure is quoted.
2. **The latency reports say "(dirty)" wrongly.** The benchmark ran after other reports had been written and
   counted them as uncommitted changes. The tree was clean at `c26162e` (chain log). Fixed in `3f0066e`,
   and the next benchmark run will label it correctly.
3. **FP32 only.** Laya's published 7.2 ms/question (T4) is reduced precision. The FP16 export, the real
   speed lever, is a known gap and not in R1.
4. **Single-question CUDA cells are noisier** (up to about 20% between repeats). 4- and 10-question cells
   agree within about 2%.

## Done (by area)

- Grounding, constitution v1.6.0, R1 spec/clarify/plan/tasks/analyze.
- Models fetched and hash-verified. Export spike. Level-1 and C# parity. Tokeniser, serialiser and routing
  parity. Contract, calibration, engine, host, telemetry, calibrator hook. Client, conformance tool and Kev
  peer. Benchmark with a varied-input pass. Single-file publish and a local container. Clean-clone
  reproduction.

## Gate items for Rob

1. **Incident:** a build agent killed Docker Desktop's backend while clearing port 8080. Containers came
   back within minutes (verified). `deploy-caddy-1` (80/443) had a short outage. Tau now uses 8088.
2. **Orphaned processes** holding about 35% CPU (caveat 1). Please clear them, then I'll re-run the
   benchmark.
3. **Dataset licence:** the best urgency set (`Tobi-Bueck/customer-support-tickets`, 61,765 real tickets,
   5-level priority) is **CC-BY-NC-4.0**. Recommendation below.
4. The brainstorm's "encode the state once" is wrong about the mechanism (agreed in clarify).
5. The GPU is an RTX 3080 **Ti**.
6. Laya's own misses for the articles: Banking77 0.425 vs Jev 0.870 (its model card), English checkpoint
   collapses off-English, and its choice/score `confidence` isn't the calibrated quantity.

## Next

- On Rob's "go": R2 (Workbench, fine-tune sidecar, two datasets end to end, calibration and cascade £).
