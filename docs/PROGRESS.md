# Tau — Progress

Updated after every completed task. On session restart, resume from here without asking.

**Current release:** R2 (Workbench + benchmarks), branch `002-workbench-benchmarks`. R1 is merged to `master`.
**Gate status:** **R2 GATE REACHED (2026-09-27). Waiting for Rob's "go" before R3.**
**Task list:** `specs/002-workbench-benchmarks/tasks.md`.

## R3 log

- T020: `README.md` written. Quickstart on CPU serving laya-en: fetch-models, export, fetch-natives, `dotnet run`, one curl with `examples/quickstart/request.json`. Also a C# `Tau.Client` snippet, GPU, Docker, Workbench, layout, reproduction and licence sections.
- T021: README walkthrough on a fresh clone of `003-launch-articles` (cloned from the local repo, since the GitHub repo isn't public). First walk failed at step 3: `export.ps1 -Only laya-en` passed the id as the letter `l` (a single filtered id splatted as a string). Fixed in the script (`9109daa`). Second walk, from a new clone, reached a first decision: **121 s in total, 117 s excluding the model and native downloads.** Steps: clone 0.6 s, fetch-models 1.0 s, export 103 s (uv installed 76 packages from its local cache in 14 s), fetch-natives 3.2 s (a real 125 MB download), Runtime build and start 8.2 s, curl 0.4 s. The README's C# snippet then answered too (`Billing`). The laya-en checkpoint was linked into the clone with a directory junction to `models/src/laya-en`, so the real fetch wasn't timed: a 200 MB sample from Hugging Face ran at 65 MB/s, which puts the laya-en fetch at about 13 s and all four models at about 85 s. A cold uv cache also downloads about 3 GB of PyTorch wheels. The re-exported `model.onnx` hash differs from the main tree's (`cf8bc443…` against `866a05b2…`) but the answers match to 4 dp. README disk estimate raised from 10 to 12 GB after measuring the 4.5 GB venv.
- T022: `dotnet pack` of Tau.Client and Tau.Workbench into `artifacts/packages`. Added the project and repository URLs and a README to the packages. Tau.Client depended on an unpacked Tau.Contract, so a consumer couldn't restore it: Tau.Contract now packs too, and the READMEs say to pack both. Nuspec: authors `Rob Hill,Fortitude Omnis`, licence expression Apache-2.0, repository `https://github.com/Fortitude-Group/tau`, README included. `dotnet tool install Tau.Workbench --tool-path <temp>` from the local folder, then `tau --help` and `tau --version` (0.1.0) worked. Nothing pushed.
- T023: `docker build` of `tau-runtime:local` (CPU), 390 MB, 33 s with a warm layer cache. Ran it on host port 18120 with `models/` mounted read-only and `Tau__Models__0=laya-en`: healthy after 69 s, and the quickstart request answered `billing` 0.9501, identical to the host run. Stopped and removed only that container. Nothing pushed.
- T024: `THIRD-PARTY-NOTICES.md` written and `NOTICE` extended with the datasets, OpenTelemetry and YamlDotNet. No licence conflicts with Apache-2.0 for anything the repo holds or ships. Flags for Rob: JsonSchema.Net 9.x (tests and tools only) ships its binaries under an Open Source Maintenance Fee EULA, a fee for revenue-generating users over US$10k a year. DirectML and the NVIDIA CUDA libraries are proprietary and fetched, not committed. The tickets data is CC-BY-NC-4.0: none of 2,000 sampled ticket texts appears in any tracked file or in the history.
- T025: secret scan with gitleaks 8.28.0 over all 65 commits (`--log-opts=--all`) and over the working tree, plus a regex sweep of `git log -p --all` and every tracked file for private keys, AWS, Azure, GitHub, Slack, Google, Anthropic and OpenAI key shapes, connection-string passwords and committed `.env`, `.pem`, `.pfx` or `.key` files. **Nothing found.** gitleaks flagged 3 items, all the same false positive: `tokenizer_json_sha256` in the tokenizer fixtures, which is the SHA-256 of the public laya tokenizer file. The tree scan's other hits were all in gitignored folders (`.venv`, `.cache`, `bin`, agent worktrees).

## R2 log

- Spec, clarify (3 answers), plan, research R-01..R-09, tasks T001–T066 in lanes A–E.
- Lane D code: `finetune_laya.py`, the manifest/parity changes for local fine-tunes, and `baseline_minilm.py` are committed.
- Lane C (T040–T042) is merged. Calibrators now act on the log reference probabilities (DECISIONS). The client has per-request headers. Tests: Calibration 107, Client 54, Runtime 43, Contract 113, Inference 1,507 (+3 skipped), model parity 12/12. After the merge, the full non-model suite passes on the branch.
- Lane A (T002, T010–T013) is merged. Banking77: calibration 1,000, held-out 1,000, fine-tune 9,003. Tickets: after the filters (non-English 33,504, vehicle keywords 393, duplicates 4,467), held-out 1,000, calibration 1,000, fine-tune 8,000. Byte-identical on re-run. 17 data tests pass. Split hashes are in `examples/*/dataset.manifest.json`.
  - **Finding:** English tickets carry only low/medium/high. `very_low` and `critical` exist only in the German file, so reports must say "no English examples", not show them as a 0% class.
  - **Finding:** every one of the 393 vehicle-keyword hits was a false positive (software drivers, "driving growth", "Smart Garage"). The rows stay dropped. It costs nothing, since the pool is 23,401 and the splits need 10,000.
- Lane B (T001, T020–T030) is merged: `src/Tau.Workbench` (spec, data, frontier, measure, calibrate, threshold, cascade, cost, baselines, report) and the `tau` global tool (`src/Tau.Workbench.Cli`, packs as `Tau.Workbench`). 143 tests pass and the solution builds with 0 warnings and 0 errors. Isotonic calibrators are fitted one-vs-rest on every option's probability, because the shared calibrator applies them per option (a max(p) fit gave log loss about 10 on test data). T031 (the Runtime equality test) is still open.
- Lane D (T014–T017) done. Both Laya fine-tunes are trained, exported and pass parity: Banking77 max |Δlogit| 2.1e-5, tickets 1.1e-3, tolerance 2e-3. The MiniLM baselines, with early stopping on the calibration split, score 91.5% on Banking77 and 55.6% on the tickets (held-out, sidecar quick check). The recipe change after a strawman first run is in DECISIONS. The sidecar tests pass (24).
- T050: frontier batches exported (1,200 pending per dataset) and turned into 12 compact answer sheets. Rob said yes to labelling.
- T051–T053 done: 1,200 frontier answers per dataset are cached and committed (no API). They used about 0.9M session tokens, over the 0.45–0.6M estimate. Banking77: the frontier disagrees with gold on 5.8%, and the two wordings agree on 97.5%. **Tickets: it disagrees with gold on 76.2%, and the wordings agree on 76.5%. The synthetic priority labels look close to arbitrary (DECISIONS). Asked Rob how the tickets example should treat them.**
- Reference mode done: `data.reference: frontier` for the tickets (Banking77 stays gold). Every stage scores through `ReferenceLabels`, and the report says "agreement with the frontier model" and keeps a secondary table against the dataset's labels. `tau label` on the tickets exits 2 with 1,000 calibration items pending in 5 batches. Workbench tests 162 pass, and the solution builds with 0 warnings and 0 errors.
- T031 done: `CalibratorEquivalenceTests` (Inference tests, `Category=Models`) runs laya-en choice and noul and von-1.2.0 choice through the engine with a temperature and an isotonic calibrator loaded from a directory. The Workbench's offline path gives the engine's unrounded output exactly (max |Δ| 0) from the same unrounded vector, and agrees with the calibrated answer within 2.4e-4 from the 4-dp raw answer (limit 1e-3). The engine gained an internal test hook that exposes each calibrator's input and output.
- T060 done: `scripts/examples.ps1 -Example <name>` checks data and model packages (printing the prepare, export or fine-tune command when something is missing), starts the Runtime on CUDA on the spec's port, runs `tau run`, restarts with the calibrators for the calibrated phase and the report, and stops only the process it started. `-CheckOnly` passes for both examples. The full run is T061/T062.
- Tickets calibration split labelled (Rob approved): 1,000 answers from 5 subagents, about 0.43M session tokens, no API. `tau label` reports nothing pending and 0 rejected.
- T061 done: Banking77 end to end (`examples/banking77/report.html`).
- T062 done: support tickets end to end against the frontier reference (`examples/support-tickets/report.html`). The report now explains a missed ECE goal when the selection rule picked the method with the clearly worse calibration-split ECE (tested).

## R2 gate: evidence against every requirement (T064)

Reference machine: RTX 3080 Ti 12 GB, i9-11900K, CUDA FP32. Every number below is in a committed report produced by one command (`scripts/examples.ps1 -Example <name>`).

| Requirement | Evidence | Status |
| --- | --- | --- |
| SC-001 both examples end to end, one command | `examples/banking77/report.html`, `examples/support-tickets/report.html` via `scripts/examples.ps1` | Met |
| SC-002 ≥50% ECE cut for the out-of-the-box model, or explained | Banking77 laya-en 0.502 → 0.065 (**87%**), von 79%. Tickets laya-typed-decisions 0.269 → 0.067 (75%), laya-en 0.273 → 0.148 (**46%, miss**, explained in the report: the log-loss rule picked isotonic over a temperature fit with calibration-split ECE 0.016) | Met with one explained miss |
| SC-003 cascade share, blended vs frontier-only, £ per million | Both reports' cascade and cost tables (Opus 5.5 headline, Batch, Sonnet 5 and Haiku 4.5 what-ifs, basis stated, labelled estimates) | Met |
| SC-004 no item asked twice, ≤1,000 held-out, zero paid API calls | Cache duplicate lines 0; held-out 1,000 per dataset (plus the tickets calibration split Rob approved); all answers from session subagents, `produced_by` on every line | Met |
| SC-005 every figure traceable | Report metadata: hardware, endpoint `/v1/models` hashes, manifest hash, prompt versions, UTC date, command, git commit | Met |
| SC-006 misses published | Label noise (5.8% and 76.2%), least-helped calibration (laya-en-ft-banking77 14%, von on tickets 12%), MiniLM beating Tau on both datasets, unreachable thresholds | Met |
| SC-007 tests green, incl. Workbench = Runtime | `CalibratorEquivalenceTests` exact (max |Δ| 0) and endpoint 2.4e-4; clean clone of `7776db1` (fetched models, natives and data linked in, as in R1): build 0 warnings / 0 errors, 2,013 .NET tests (2,010 pass, 3 designed GPU-provider skips), sidecar 32/32 | Met |
| FR-014 fine-tune + parity | `examples/*/finetune-parity.json`: max |Δlogit| 2.1e-5 and 1.1e-3 (tolerance 2e-3) | Met |
| FR-015 classic baseline | MiniLM-L6: Banking77 91.5%, tickets 55.6% (against gold), in both reports | Met |
| FR-020 R1 CPU latency re-run on a quiet machine | `reports/r1/latency*.md` (2026-09-27, R1's four models, CPU load under 10%). CPU p50 laya-en q=1 811 → 529 ms. First attempt with six models resident failed (DECISIONS) | Met |

### Headline numbers (held-out, 1,000 items each)

| Dataset | Model | Raw acc. | ECE raw → calibrated |
| --- | --- | --- | --- |
| Banking77 (gold) | laya-en | 37.2% | 0.502 → 0.065 |
| Banking77 (gold) | von-1.2.0 | 77.1% | 0.185 → 0.039 |
| Banking77 (gold) | laya-en-ft-banking77 | 87.3% | 0.071 → 0.061 |
| Banking77 (gold) | MiniLM-L6 baseline | 91.5% | calibrated 0.024 |
| Tickets (vs frontier) | laya-en | 23.2% | 0.273 → 0.148 |
| Tickets (vs frontier) | laya-typed-decisions | 18.5% | 0.269 → 0.067 |
| Tickets (vs frontier) | von-1.2.0 | 43.9% | 0.045 → 0.040 |
| Tickets (vs frontier) | laya-en-ft-tickets | 27.7% | 0.255 → 0.074 |
| Tickets (vs frontier) | MiniLM-L6 baseline | 27.4% | calibrated 0.015 |

Cascade (τ picked on the calibration split, judged on held-out, £ = list-price estimates):

| Dataset | Model | Kept local | Blended | Frontier only | £ per million (cascade vs frontier) |
| --- | --- | --- | --- | --- | --- |
| Banking77, 5% target error | laya-en-ft-banking77 | **73.6%** | 93.2% | 94.2% | **£1,031** vs £3,898 |
| Banking77 | von-1.2.0 | 4.5% | 94.2% | 94.2% | £3,723 vs £3,898 |
| Banking77 | laya-en | no τ reaches 5% | | | |
| Banking77 | MiniLM-L6 baseline (not served via Tau, no energy) | **94.7%** | 94.2% | 94.2% | £207 vs £3,898 |
| Tickets, 20% target disagreement | every Tau model | 0.1% | 100% agreement (by construction) | | £996 vs £997 |
| Tickets | MiniLM-L6 baseline | 0.4% | 99.7% | | £993 vs £997 |

The headline: a fine-tuned Laya keeps about three-quarters of Banking77 decisions local at one point below the frontier's accuracy, which cuts the estimated bill by about 74%. A fine-tuned 22.7M MiniLM does better still. On urgency, a judgement call, nothing local stands in for the frontier. An earlier version of this paragraph said "only Von keeps any decisions local". It was copied from a report summary that quoted the first model's cascade rather than the best one. The summary is fixed (DECISIONS).

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

- Lane A (sidecar data, T002/T010–T013) done. Remaining R2 lanes: B (Workbench core, T020–T031), C (R1
  adjustments, T040–T042), D (fine-tune + baseline, T014–T017, needs Lane A's splits), E (frontier
  labelling, T050–T053, needs Lane A's held-out ids + Lane B's batch export), then Phase 7 end-to-end.
