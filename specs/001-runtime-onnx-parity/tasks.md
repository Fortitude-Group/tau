# Tasks: Tau R1 — Runtime core and model parity

**Input**: design documents in `specs/001-runtime-onnx-parity/` (plan, spec, research, data-model, contracts, quickstart)

**Test traits**: fixture parity tests are `[Trait("Category","Parity")]`, tests needing fetched models are also `Category=Models`.

**Tests**: required. The spec makes parity and conformance gates (FR-003/005/024, SC-001..005), and constitution
Principle III requires comprehensive tests on every public contract.

**Format**: `- [ ] Txxx [P?] [USn?] description (path)`. `[P]` = no dependency on another incomplete task and no
shared file, so it can fan out as its own agent or worktree.

**Standing rule (brief + Principle VII)**: after **every** completed task, tick it here and append a line
to `docs/PROGRESS.md` (what was done, evidence, what's next) in the same commit.

## Parallel lanes (fan-out plan)

Principle: decompose, then fan out. Once Phase 1 is done, three lanes run concurrently.

| Lane | Tasks | Why independent |
| --- | --- | --- |
| **A: Python spike** | T010–T017 | Sidecar only. Needs models (T004) and the sidecar env (T003). Owns `sidecar/`, `tests/fixtures/`. |
| **B: C# foundations** | T018–T025 | Pure C#, no model output needed. Owns `src/Tau.Contract`, `src/Tau.Calibration`, parts of `src/Tau.Inference`. |
| **C: Research checks** | T026, T059 | Provider spike and dataset licences. No shared files with A or B. |

Lanes A and B join at US1 C# parity (T027+), which consumes A's fixtures and B's building blocks. After US2,
the US3/US4/US5/US6 phases are mutually independent and fan out again. **Serial by necessity:** the
export → fixtures → C# parity chain for one model (a real data dependency), and the final gate (T064–T066).

---

## Phase 1: Setup (shared infrastructure)

- [X] T001 Create repo scaffolding: `LICENSE` (Apache-2.0 full text), `NOTICE` (third-party: Laya, Von, ModernBERT, mmBERT, ONNX Runtime, Tokenizers.DotNet), `.gitattributes` (`* text=auto eol=lf`, `*.onnx binary`), `global.json` (SDK 10.0.400, rollForward latestFeature)
- [X] T002 Create `Directory.Build.props` (net10.0, nullable, `TreatWarningsAsErrors`, deterministic, `LangVersion latest`) and `Directory.Packages.props` (central package versions: ONNX Runtime 1.24.4 family, Tokenizers.DotNet 1.4.1 + win-x64/linux-x64 runtimes, OpenTelemetry, JsonSchema.Net, xunit.v3, Microsoft.AspNetCore.Mvc.Testing, all exact versions)
- [X] T003 Create `Tau.slnx` with projects `src/Tau.Contract`, `src/Tau.Calibration`, `src/Tau.Inference`, `src/Tau.Runtime`, `src/Tau.Client`, `tools/Tau.Bench`, `tools/Tau.Conformance`, `tests/Tau.Contract.Tests`, `tests/Tau.Calibration.Tests`, `tests/Tau.Inference.Tests`, `tests/Tau.Runtime.Tests`, `tests/Tau.Client.Tests`; `dotnet build Tau.slnx` shows 0 errors
- [X] T004 [P] Write `scripts/fetch-models.ps1` + `models.lock.json` (pinned repos/revisions from DECISIONS: `convaiinnovations/laya@55cf4c4e…` root + `multilingual/` + `typed-decisions/`, `wfzyx/von@5df8185a…`): download via HF `resolve/<sha>` URLs into `models/src/…`, verify sha256 against the lock (the first run records the hashes and fails if they're absent)
- [X] T005 [P] Create sidecar uv project `sidecar/finetune/pyproject.toml` (Python 3.12, torch CUDA 12 index, transformers 5.x, `laya==0.3.20`, `von-sdk==1.2.3`, onnx, onnxruntime, safetensors, huggingface_hub, pytest) and commit `uv.lock`; `uv run python -c "import laya, von, torch, onnxruntime"` succeeds
- [X] T006 [P] Write `scripts/fetch-cuda.ps1`: install NVIDIA CUDA 12 runtime, cuBLAS, cuDNN 9, cuFFT and cuRAND pip wheels (free) into `native/cuda-deps/` via `uv pip install --target`, and list the DLL directory
- [X] T007 [P] Write `scripts/build-test.ps1`: `dotnet build Tau.slnx -c Release` (gate: 0 errors), `dotnet test Tau.slnx`, `uv run pytest` in `sidecar/finetune`; non-zero exit on any failure
- [X] T008 Update `docs/PROGRESS.md` (Phase 1 done, evidence: build output and fetched hashes) and commit

---

## Phase 2: Foundational (blocking prerequisites)

**Lane A: Python export spike** (sequential inside the lane; the brief says the spike starts R1)

- [X] T010 Write the reference runners in `sidecar/finetune/tau_sidecar/reference.py`: load `laya.Agent` per checkpoint and `von.backends.OptionMarkerBackend` from local `models/src` paths, CPU, FP32, `torch.use_deterministic_algorithms(True)`, eval mode. Expose `logits(batch)` (pre-temperature raw logits from the exact tensors the reference builds) and `answers(state, questions)`
- [X] T011 Write `sidecar/finetune/tau_sidecar/export_laya.py`: approach A = `torch.onnx.export` (TorchScript, opset 17, `attn_implementation="eager"`, dynamic axes B/S/K), inputs `input_ids, attention_mask, marker_pos, marker_mask, qtype`, outputs `logits, act_logits`. On failure, approach B = `dynamo=True`. If Laya's in-graph mask won't trace, switch to host-built additive masks (research R-02). Writes `models/<id>/model.onnx` + `tau-model.json` (schema: `specs/.../contracts/model-package.schema.json`) with source/ONNX/tokeniser hashes and clamped post-processing params, for `laya-en`, `laya-multilingual`, `laya-typed-decisions`
- [X] T012 Write `sidecar/finetune/tau_sidecar/export_von.py`: wrap encoder + gather + `OptionMarkerScorer` with inputs `input_ids, full_mask, sliding_mask, position_ids, marker_pos, marker_mask`, output `logits`. Approach A, then B. Writes `models/von-1.2.0/model.onnx` + `tau-model.json` (temperature, calibration_map, noul_prior, digit_split, independent_options)
- [X] T013 Write the hand-written parity case corpus `sidecar/finetune/cases/*.json`: choice/score/noul; 1, 2, 5, 10, 30, 77 and max-fitting options; text/object/array/nested/null/empty states; long (truncating) states; list-state conversations (left truncation); criteria with structured values; `[MASK]`/`[SEP]` inside text; digits; ≥ 5 non-Latin scripts plus Latin non-English for multilingual and routing; request-sized batches (1, 4, 10 questions). No fleet or vehicle content.
- [X] T014 Write `sidecar/finetune/tau_sidecar/parity.py` level 1: for every case and model, compare onnxruntime-python logits to PyTorch logits for identical tensors. Tolerance |Δlogit| ≤ 2e-3, |Δprob| ≤ 1e-3, identical argmax. Also run Laya's own `laya.onnx_agent.ONNXAgent` on our Laya export against `Agent` answers (cross-check). Emit `reports/r1/parity-model.json`
- [X] T015 Write `sidecar/finetune/tau_sidecar/fixtures.py`: generate `tests/fixtures/parity/<model>/{tokens,sequences,logits,answers}.jsonl`, `tests/fixtures/serialise/{pyjson,pyrepr}.jsonl` (fuzz corpus: floats, big ints, unicode, escapes, nesting, bool/None) and `tests/fixtures/routing/routes.jsonl` (≥ 200 states → `laya.router.Router._route` decision, never loading models). Each file starts with a header line (versions, hashes, date)
- [X] T016 Write `sidecar/finetune/tests/test_parity.py` (pytest wrappers for T014, plus a determinism check: two reference runs are identical)
- [X] T017 Record the spike outcome in `docs/DECISIONS.md` (the export approach that worked per model, or the fallback with the evidence of both attempts) and `docs/PROGRESS.md`. **If both approaches fail for a model: stop and tell Rob (brief rule).**

**Lane B: C# foundations** (all [P]: separate projects/files)

- [X] T018 [P] Implement contract types in `src/Tau.Contract/` (`DecisionRequest`, `Question` + `ChoiceQuestion`/`ScoreQuestion`/`NoulQuestion`, `Answer` variants, `DecisionResponse`, `Usage`, `ValidationProblem`), with `JsonNode` for state/instructions/criteria values and order-preserving criteria maps
- [X] T019 [P] Implement `src/Tau.Contract/Validation/RequestValidator.cs`: every rule in data-model (choice 1–255, score 2–10, noul keys, instruction types, 1–50 questions, unknown non-`x-tau-` fields, required keys), collecting all problems with JSON paths. Embed `contracts/systemone/2026-09-27/*.schema.json` as resources
- [X] T020 [P] Tests `tests/Tau.Contract.Tests/`: round-trip the contract's documented examples; one test per validation rule plus boundaries (0/1/255/256 options, 1/2/10/11 levels, noul key casing, null/empty state, 51 questions, unknown fields); the schema validates valid and invalid samples consistently with `RequestValidator`
- [X] T021 [P] Implement `src/Tau.Calibration/`: `TemperatureScaling` (fit by NLL minimisation via golden-section/Brent on log T, apply), `IsotonicRegression` (PAV fit, piecewise-linear clamped apply), `Metrics` (ECE with 15 bins, first bin inclusive (matching `laya.common.ece_score`), Brier, log loss), `CalibratorFile` v1 read/validate/write (schema in `specs/.../contracts/calibrator.schema.json`), `CalibratorSet` selection (model+type+bucket → model+type)
- [X] T022 [P] Tests `tests/Tau.Calibration.Tests/`: metrics against hand-computed and numpy-generated reference values (committed constants), isotonic monotonicity and PAV correctness on known sequences, temperature recovery on synthetic data with known T, file-format rejection cases (wrong format/version/hash, non-monotone y, missing fields)
- [X] T023 [P] Implement `src/Tau.Inference/Text/PyJson.cs` (`json.dumps(ensure_ascii=False)` + render_criterion variant) and `PyRepr.cs` (`str()` of dict/list/scalars), with unit tests in `tests/Tau.Inference.Tests/Text/` for known cases (fixture parity comes in T030)
- [X] T024 [P] Implement `src/Tau.Inference/Tokenization/HfTokenizer.cs` over Tokenizers.DotNet: load `tokenizer.json`, `Encode(text, addSpecialTokens)`, `EncodeTruncated(text, maxTokens)`, special token ids (`cls/sep/mask/pad`) read from tokenizer config, thread-safe
- [X] T025 [P] Implement `src/Tau.Inference/Onnx/OrtNativeResolver.cs` + `OrtSessionFactory.cs`: provider enum (cpu|cuda|directml), `NativeLibrary.SetDllImportResolver` on the ORT managed assembly loading `native/<flavour>/onnxruntime.dll|.so`, CUDA deps dir prepended to the DLL search path, deterministic session options (`ORT_ENABLE_BASIC` graph opt, fixed thread counts on CPU), startup failure with a clear message when the provider is unavailable unless `AllowCpuFallback`

**Lane C: research checks**

- [X] T026 [P] Provider spike `tests/Tau.Inference.Tests/Onnx/ProviderSmokeTests.cs` + `scripts/fetch-natives.ps1` (restores the three native flavours into `native/`): in separate test processes, load a tiny committed ONNX (Add op) under CPU, CUDA and DirectML via the resolver and run it; record the outcome in DECISIONS

**Checkpoint**: sidecar exports + fixtures exist (Lane A), foundations are green (Lane B), providers are proven (Lane C).

---

## Phase 3: User Story 1: exported models answer exactly like the originals (P1) 🎯 MVP

**Goal**: C# reproduces the reference token sequences, logits and answers for all four models.
**Independent test**: `./scripts/parity.ps1` → `reports/r1/parity.md` shows 100% on every gate.

- [X] T027 [US1] Implement `src/Tau.Inference/Models/ModelPackage.cs`: load `tau-model.json`, verify the ONNX/tokeniser sha256 against the manifest (FR-002), expose limits and post-processing params; test in `tests/Tau.Inference.Tests/Models/ModelPackageTests.cs` that a tampered ONNX or manifest hash refuses to load with the file named
- [X] T028 [P] [US1] Implement `src/Tau.Inference/Laya/LayaSequenceBuilder.cs`: port of `serialize_state`, `render_criterion`, `render_options` (noul labels), `build_sequence` (48-token option cap, budget shrink rule, head truncation, left truncation for list states, `[MASK]` neutralisation, shared state ids), and `LayaQuestionNormaliser` (`_to_internal`: instruction JSON, noul key lowering)
- [X] T029 [P] [US1] Implement `src/Tau.Inference/Von/VonSequenceBuilder.cs`: port of `_format_state` (PyRepr), `pack_sequence`, `split_digits`, marker position discovery, `build_option_invariant_position_ids`, `build_independent_option_masks` (additive f32, sliding window from position ids, eye term), noul descriptions + null-state second row, score legend (dict items `what`/`examples`); Von sequence cap `Tau:Von:MaxTokens` (default 4096): a longer packed sequence → validation error naming the question (documented deviation: the reference allows 8192 but the S×S masks cost about 256 MB per row there)
- [X] T030 [US1] Tests `tests/Tau.Inference.Tests/Parity/SerialiseAndTokenParityTests.cs`: PyJson/PyRepr against `tests/fixtures/serialise/*.jsonl` (byte-exact); tokeniser against `tokens.jsonl` for all 4 models (exact ids)
- [X] T031 [US1] Tests `tests/Tau.Inference.Tests/Parity/SequenceParityTests.cs`: Laya and Von builders against `sequences.jsonl` (ids, marker positions, masks, position ids exact)
- [X] T032 [US1] Implement `src/Tau.Inference/Laya/LayaEngine.cs` (collate a request's rows with pad id → ORT run → logits) and `LayaPostProcessor.cs` (clamped temperatures [0.5, 5], bucket lookup, softmax, entropy confidence, 4-dp rounding, choice/score/noul contract answers; drop non-contract fields)
- [X] T033 [US1] Implement `src/Tau.Inference/Von/VonEngine.cs` (batch all questions + null-prior rows into one pass) and `VonPostProcessor.cs` (input-conditioned temperature map with real token count of the state, `max(eff_temp,1e-4)`, margin confidence rounded to 3 dp, noul prior correction, noul = p[0] rounded to 4 dp, score rounded to 2 dp, argmax on unscaled logits)
- [X] T034 [US1] Tests `tests/Tau.Inference.Tests/Parity/LogitAndAnswerParityTests.cs` (`Category=Models`, fail loudly if models are missing): C# ORT logits vs `logits.jsonl` (|Δ| ≤ 2e-3, argmax identical); full answers vs `answers.jsonl` (same choice, probs ≤ 1e-3, score ≤ 1e-3×(k−1), noul ≤ 1e-3)
- [X] T035 [US1] Write `scripts/export.ps1` (fetch check → `uv run python -m tau_sidecar.export_laya/export_von`) and `scripts/parity.ps1` (sidecar parity + fixtures → `dotnet test --filter Category=Parity|Category=Models` → merge into `reports/r1/parity.{md,json}` with the standard report header: hardware, hashes, versions, date, command)
- [X] T036 [US1] Run `scripts/parity.ps1`, commit `reports/r1/parity.*` and fixtures, and update PROGRESS with the evidence

**Checkpoint**: parity gate green. US2 can start.

---

## Phase 4: User Story 2: swap the base URL (P1)

**Goal**: a contract client gets answers from a local Runtime with only the base URL changed.
**Independent test**: HTTP integration tests + quickstart step 4.

- [X] T037 [P] [US2] Implement `src/Tau.Inference/Routing/ScriptAnalyser.cs` (port of `laya.lang.analyse`: script shares, Latin/non-Latin, diacritic rate, stopword language id, undecided) and `ModelRouter.cs` (explicit id → aliases `auto|tau-auto|jev-latest|jev-*` → analyse → laya-en/laya-multilingual; typed-decisions never auto), plus `tests/Tau.Inference.Tests/Parity/RoutingParityTests.cs` against `routes.jsonl` (100% match)
- [X] T038 [P] [US2] Implement `src/Tau.Inference/Models/ModelRegistry.cs`: installed models from config, lazy load (preload option), thread-safe, **[deviation: LRU cap not built; the dead `MaxLoaded` option was removed and VRAM is limited via `Tau:Models`; see DECISIONS "Performance findings"]**, discovery for `/v1/models`, and `DecisionService.cs` (validate → route → engine → calibrate hook → response, per-request telemetry record)
- [X] T039 [US2] Implement the `src/Tau.Runtime/` host: `Program.cs` minimal API, `TauOptions` (provider, models dir, preload, AllowCpuFallback, aliases, calibrators dir, port 8080), `POST /v1/systemone`, `GET /v1/models`, `GET /healthz`; 422 body per data-model; `Authorization` accepted and ignored; `x-tau-*` response headers; malformed JSON → 422
- [X] T040 [US2] Implement telemetry in `src/Tau.Runtime/Telemetry/`: OpenTelemetry traces and metrics (request latency, model time, input tokens, batch rows, model id/version, calibrator versions, truncation), OTLP exporter off by default, Prometheus `/metrics` (research R-11 fallback if the prerelease exporter fails); test `tests/Tau.Runtime.Tests/Http/MetricsTests.cs` that after one request `/metrics` exposes the latency, token, batch-row and model-id series
- [X] T041 [US2] Tests `tests/Tau.Runtime.Tests/Http/ContractTests.cs` (WebApplicationFactory, `Category=Models`): every response validates against `response.schema.json`; text/object/array/nested/null/empty states answered; every invalid-request class → 422 naming the field; unknown model → 422 listing ids; `jev-latest` routes; `Authorization` ignored; no non-contract fields
- [X] T042 [US2] Tests `tests/Tau.Runtime.Tests/Http/DeterminismTests.cs` (SC-005): the same request twice gives identical bytes; each question alone vs together gives identical answers; 100 concurrent requests vs sequential give identical answers
- [X] T043 [US2] Tests `tests/Tau.Runtime.Tests/Http/EdgeCaseTests.cs`: options exceeding the head budget → 422 naming the question; truncated state sets `x-tau-truncated`; `[MASK]` in option keys; duplicate descriptions; 50 questions; unavailable configured provider fails at startup; CPU fallback when allowed
- [X] T044 [US2] Update PROGRESS and commit

---

## Phase 5: User Story 3: conformance against Kev (P2)

**Goal**: a committed conformance report, Tau vs contract vs Kev.
**Independent test**: `./scripts/conformance.ps1` → zero structural failures for Tau.

- [X] T045 [P] [US3] Write the committed request set `tests/conformance/requests/*.json` (≥ 40 requests: every question type, option-count boundaries, structured states, invalid requests with the expected status)
- [X] T046 [US3] Implement `tools/Tau.Conformance/` (`--tau <url> --peer <url> --out reports/r1`): send each request to both, validate Tau strictly and the peer leniently (extra fields = "peer extension"), classify differences (structural = status/shape mismatch vs contract → fail; model disagreement = different answer values → recorded), write `conformance.{md,json}` with the standard header
- [X] T047 [US3] Write `scripts/conformance.ps1`: start Tau (built exe, CUDA), start Kev-0.8B via `uv` (clone `jaredpalmer/kev` at a pinned commit into `.cache/kev`, `uv sync --extra serve`, `python -m kev.serve --run jaredpalmer/kev-0.8b`), fallback WSL Ubuntu, fallback `von serve`; record which peer actually ran and its revision in the report; tear both down
- [X] T048 [US3] Run the conformance suite, commit `reports/r1/conformance.*`, and record the peer outcome in DECISIONS and PROGRESS

---

## Phase 6: User Story 4: latency on real hardware (P2)

**Goal**: committed latency report on the RTX 3080 Ti and CPU.
**Independent test**: `./scripts/bench.ps1` regenerates `reports/r1/latency.*`.

- [X] T049 [US4] Implement `tools/Tau.Bench/`: hardware fingerprint (GPU name/VRAM/driver via `nvidia-smi`, CPU, RAM, OS), in-process model-time runs and end-to-end HTTP runs against a started Runtime, per model × provider (cuda, cpu) × questions (1, 4, 10); warm-up 50; measured 500 (GPU) / 100 (CPU); p50/p95/p99/mean/sd; two back-to-back repeats with variance; precision stated; writes `latency.{md,json}` with the standard header
- [X] T050 [US4] Write `scripts/bench.ps1` (build Release, run bench for both providers), run it, commit `reports/r1/latency.*`, update PROGRESS

---

## Phase 7: User Story 5: typed .NET client (P3)

- [x] T051 [P] [US5] Implement `src/Tau.Client/`: `SystemOneClient(HttpClient, baseUrl)` with `SystemOneAsync(DecisionRequest)`, typed `DecideAsync<TEnum>(state, instructions, descriptions?)` → `Decision<TEnum>` (value, probabilities per member, confidence), `ScoreAsync`, `NoulAsync`, `SystemOneValidationException` carrying the problem details; NuGet metadata (Apache-2.0, 0.1.0, README), packed locally with `dotnet pack` only
- [X] T052 [US5] Tests `tests/Tau.Client.Tests/`: against a stub handler (shape, errors) — **done, 41 tests green** — and, with `Category=Models`, against the in-process Runtime (enum answer round trip) — **blocked**: `Tau.Runtime` is still the empty web template, so this half is deferred until the Runtime host exists

---

## Phase 8: User Story 6: calibrator hook and raw scores (P3)

- [X] T053 [US6] Wire `Tau.Calibration.CalibratorSet` into `DecisionService`: load `*.calibrator.json` from the configured dir at startup (refuse to start on an invalid/incompatible file, naming it), apply temperature to raw logits or isotonic to probabilities then renormalise, recompute confidence with the model's formula, `x-tau-calibrators` header, telemetry versions; `x-tau-raw: true` bypasses
- [X] T054 [US6] Tests `tests/Tau.Runtime.Tests/Http/CalibrationHookTests.cs`: a hand-made temperature calibrator changes probabilities by the expected amount (checked against `Tau.Calibration` directly); isotonic applied and renormalised; raw flag returns the reference answer; bad file → startup failure with the file named; response still schema-valid

---

## Phase 9: Polish and gate

- [X] T055 [P] Single-file publish profile for `src/Tau.Runtime` (win-x64, linux-x64; self-contained exe + `native/` folder) and `src/Tau.Runtime/Dockerfile` (multi-stage, CPU default, CUDA build arg); build the image locally (`tau-runtime:r1-local`), never push; smoke-run the container `/healthz` on CPU
- [X] T056 [P] Add XML docs on every public type in `Tau.Contract`, `Tau.Calibration`, `Tau.Client`; enable `GenerateDocumentationFile` (warnings as errors)
- [X] T057 [P] Write `src/Tau.Runtime/appsettings.json` defaults + `docs/runtime-config.md` (every option, provider setup, CUDA deps), no secrets
- [X] T058 Security pass: path handling for models and calibrators dirs (no traversal), request size limits (body ≤ 1 MB default, configurable), and a `npx @claude-flow/cli@latest security scan` if available (record if unavailable)
- [X] T059 [P] Dataset licence check (FR-027): Banking77 and ≥ 2 open support-ticket datasets with urgency/priority labels; record licence, revision and publishability in `docs/DECISIONS.md`; drop anything unpublishable and flag it to Rob at the gate
- [X] T060 Run `scripts/build-test.ps1` from a clean `git clean -xdf` + fetch (full reproduction); fix anything found
- [X] T061 Re-run parity, conformance and bench from their single commands; confirm the reports regenerate within their stated variance (SC-007); commit
- [X] T062 Cross-check every FR/SC in spec.md against evidence and write the checklist into `docs/PROGRESS.md` (R1 gate section)
- [X] T063 Merge `001-runtime-onnx-parity` into `master` (no PR; local only, no remote push until the repo exists and Rob says so)
- [X] T064 **R1 GATE: stop.** Report to Rob: what's done, evidence links, misses, and a rough estimate for R2. Wait for "go".

---

## Dependencies

- Phase 1 → everything. T004 (models) and T005 (env) gate Lane A. T002/T003 gate Lane B.
- Lane A: T010 → T011, T012 (parallel with each other) → T013 is independent (can be written first) → T014 → T015 → T016 → T017.
- US1 (T027–T036) needs Lane A fixtures and Lane B T023–T025. US2 needs US1 engines. US3, US4, US5 and US6 need US2's host and are independent of each other.
- Phase 9 needs all stories. T064 is last.

## Parallel examples

```text
# After Phase 1, launch together:
Lane A agent: T010→T017 (sidecar spike)
Lane B agents: T018+T019+T020 (contract) | T021+T022 (calibration) | T023 (serialisers) | T024 (tokeniser) | T025 (ORT)
Lane C agent: T026 (provider spike) | T059 (licences)

# After US2:
US3 agent (T045–T048) | US4 agent (T049–T050) | US5 agent (T051–T052) | US6 agent (T053–T054)
```

## Implementation strategy

MVP = Phase 1 + 2 + US1: parity proven, which retires the brainstorm's biggest technical risk. Then US2 (the
product), then the four P2/P3 stories in parallel, then polish and the gate. Every report comes from a
committed single command.
