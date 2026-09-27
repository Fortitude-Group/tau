# Research: Tau R1 — Runtime core and model parity

Phase 0 output. Every decision below was settled from the real artefacts (pinned checkpoint repos,
the maintained vendor runtimes, package registries, this machine) on 2026-09-27, not from memory.
Where something can only be proven by running it, the decision names the task that proves it.

## R-01 · What counts as "the reference implementation"

- **Decision**: Laya's reference is the maintained PyPI runtime **`laya==0.3.20`** (`laya.agent.Agent`,
  `laya.common`, `laya.router`, `laya.lang`). The older `rl_agent_api.py` in the checkpoint repo isn't
  the reference. Von's reference is **`von-sdk==1.2.3`** (`OptionMarkerBackend`). Both run on CPU
  in FP32 for the parity gate.
- **Rationale**: the checkpoint repo's script differs from what users run. The maintained runtime clamps
  temperatures to [0.5, 5.0], so the shipped `choice:11+` = 0.1006 becomes 0.5. It also left-truncates list
  states, renders structured criteria as JSON, normalises noul criteria keys and validates questions.
  "Drop-in" means matching what users actually get.
- **Alternatives rejected**: `rl_agent_api.py`, which is stale and misreports confidence for 11+ option choices.
  Reimplementing from the model cards would be guesswork.

## R-02 · Export route (two genuinely different approaches, per the brief's fallback rule)

- **Decision**: approach **A** is `torch.onnx.export` on the TorchScript-based exporter (opset 17, eager
  attention, dynamic batch and sequence axes). Approach **B** is the `torch.export`/dynamo exporter
  (`torch.onnx.export(..., dynamo=True)`). If both fail for a model, use the fallback: a thin Python
  inference process behind the Runtime (FR-006), recorded and reported.
- **Graph interfaces** (the host builds everything data-dependent, so the graph stays static and traceable):
  - **Laya** (all three checkpoints): inputs `input_ids[B,S] i64`, `attention_mask[B,S] i64`,
    `marker_pos[B,K] i64`, `marker_mask[B,K] bool`, `qtype[B] i64`. Outputs `logits[B,K] f32`,
    `act_logits[B,2] f32`. These names and dtypes match `laya.onnx_agent.ONNXAgent`, so Laya's own
    ONNX runtime can load our export as a third cross-check.
  - **Von**: inputs `input_ids[B,S] i64`, `full_mask[B,1,S,S] f32` (additive), `sliding_mask[B,1,S,S] f32`
    (additive), `position_ids[B,S] i64`, `marker_pos[B,K] i64`, `marker_mask[B,K] bool`. Output
    `logits[B,K] f32` (masked slots = −1e4). This mirrors `_IndependentOptionsEncoder`, which the Von
    authors already trace to OpenVINO with exactly these tensors. That's strong evidence the encoder traces.
- **Fallback trigger if Laya's in-graph 2D→4D mask construction won't trace** (transformers 5 builds
  sliding masks with data-dependent ops): Laya switches to Von's interface (host-built additive masks).
  That's a different graph input, not a third export method.
- **Proven by**: the export and parity tasks (tasks.md Phase 2).

## R-03 · Parity levels and tolerance

- **Decision**: three automated parity gates, plus one cross-check.
  1. **Tokeniser**: C# token IDs == HF `tokenizers` IDs, exact, on the fixture corpus.
  2. **Model**: ONNX logits against PyTorch logits for identical token tensors. FP32 on CPU, max |Δlogit|
     ≤ 2×10⁻³, max |Δprob| ≤ 1×10⁻³, identical argmax (agreed in clarify).
  3. **End to end**: C# Runtime `/v1/systemone` answers against reference-runtime answers for the same
     requests. Same `choice`, probabilities within 1×10⁻³, `score` within 1×10⁻³ × (levels−1).
  - Cross-check: Laya's own `ONNXAgent` loads our Laya export and agrees with `Agent`.
- **Fixtures**: Python generates committed JSONL fixtures (inputs, reference token IDs, reference logits,
  reference answers) with the checkpoint hashes and package versions in a header line. C# tests consume
  them, so the C# gates don't need Python at test time.
- **Rationale**: a one-token drift silently changes every answer after it, so the tokeniser needs its own
  gate. Level 3 is the only one that tests our ported post-processing.

## R-04 · Python sidecar environment

- **Decision**: `sidecar/finetune/` is a `uv` project pinned to **Python 3.12** with a committed `uv.lock`.
  Deps: `torch` (CUDA 12.x wheel), `transformers` 5.x (von-sdk needs ≥5.0), `laya==0.3.20`,
  `von-sdk==1.2.3`, `onnx`, `onnxruntime` (CPU, parity), `safetensors`, `huggingface_hub`, `pytest`.
- **Rationale**: uv 0.11.32 is installed. A lockfile gives a reproducible environment, which is a
  constitution requirement for every report. Python 3.14 is avoided (wheels).

## R-05 · C# tokeniser

- **Decision**: **Tokenizers.DotNet 1.4.1** (MIT), a .NET wrapper over the Hugging Face `tokenizers` Rust
  library, with its per-RID native runtime packages (win-x64, linux-x64). It loads the checkpoint's own
  `tokenizer.json`.
- **Rationale**: it's the same Rust code the reference uses, so exact parity is by construction rather than
  by reimplementation. That covers ModernBERT's byte-level BPE, mmBERT's 256k Gemma-style vocab, and added
  special tokens such as `[SEP]` and `[MASK]` matched inside text, which Von relies on deliberately.
- **Alternative / fallback**: Microsoft.ML.Tokenizers 2.0.0 (byte-level BPE from vocab/merges extracted
  from `tokenizer.json`). It's used only if the tokeniser parity gate fails with Tokenizers.DotNet.

## R-06 · ONNX Runtime and execution providers ("same binary, config-selected")

- **Decision**: pin the whole ORT stack at **1.24.4**: `Microsoft.ML.OnnxRuntime.Managed` 1.24.4, with
  native libraries from `Microsoft.ML.OnnxRuntime` (CPU), `Microsoft.ML.OnnxRuntime.Gpu.Windows`/`.Gpu.Linux`
  (CUDA) and `Microsoft.ML.OnnxRuntime.DirectML` (DirectX 12). Each native flavour goes in its own
  `native/<cpu|cuda|directml>/` folder. At startup the Runtime registers a
  `NativeLibrary.SetDllImportResolver` for the managed ORT assembly that loads `onnxruntime` from the
  configured flavour's folder, before the first ORT call.
- **Rationale**: the DirectML native package stops at 1.24.4 (checked on nuget.org), and the CUDA package
  is at 1.30. One managed assembly can only drive natives of its own version, so the common version is
  1.24.4. The resolver gives one managed binary with the provider selected by config, which is what the
  brief asks for.
- **Consequence**: "single-file publish" means a single-file executable plus a sibling `native/` folder.
  Provider natives can't be bundled into the single file and then chosen at runtime. Recorded in DECISIONS.
- **CUDA runtime libraries**: ORT 1.24 CUDA needs CUDA 12.x and cuDNN 9 DLLs. None are installed. The free
  source is NVIDIA's pip wheels (`nvidia-cuda-runtime-cu12`, `nvidia-cublas-cu12`, `nvidia-cudnn-cu12`,
  `nvidia-cufft-cu12`, `nvidia-curand-cu12`), installed into a Tau-owned folder by `scripts/fetch-cuda.ps1`
  and put on the native search path by the Runtime. No toolkit install, no spend.
- **Proven by**: the provider spike task (load all three flavours in one process run, one per test).

## R-07 · Reproducing Python's text serialisation in C#

- **Decision**: two small, fixture-tested serialisers in `Tau.Inference`:
  - `PyJson.Dumps` reproduces `json.dumps(x, ensure_ascii=False)`, and the variant
    `separators=(", ", ": "), default=str` for Laya's `render_criterion`. That covers Python float repr
    (`1.0`, `1e-05`, `100.0` for JSON `1e2`), ints of any size, non-ASCII passed through, and the escape set.
  - `PyRepr.Str` reproduces `str()` of dicts and lists for Von's `_format_state`
    (`{'a': 1, 'b': True, 'c': None}`, repr quoting rules).
- **Rationale**: state and criteria text is fed straight to the tokeniser. One differing character is a
  different token sequence, so these serialisers are on the parity path.
- **Proven by**: a Python-generated fixture corpus (edge values, unicode, nesting, big ints, floats) that the
  C# serialisers must match byte for byte.

## R-08 · Routing (script detection)

- **Decision**: port `laya.router._route` precedence and `laya.lang.analyse` to C#. Tau's order: explicit Tau
  model id → auto. Auto routes with `analyse`: unknown → default (English), non-Latin → multilingual,
  Latin non-English → multilingual, undecided → default, English → English. `typed-decisions` is never
  auto-selected, same as the reference.
- **Rationale**: the reference router exists because the English checkpoint collapses off-English
  (Khmer 0.000 accuracy at 0.952 confidence, per Laya's README). Tau has to make the same call.
- **Proven by**: a routing parity fixture (≥ 200 states across scripts and languages) comparing the C#
  decision with `Router._route`.

## R-09 · Model ids and "swap the base URL, nothing else changes"

- **Decision**: installed ids are `laya-en`, `laya-multilingual`, `laya-typed-decisions`, `von-1.2.0`.
  Auto-route aliases are `auto`, `tau-auto`, `jev-latest` and any `jev-*`, which route with R-08 between
  `laya-en` and `laya-multilingual`. Aliases are configurable. The response `model` is the resolved Tau id.
  An unknown non-alias id returns 422 naming the installed ids.
- **Rationale**: the official SDK defaults to `model: "jev-latest"`. Rejecting it would break the
  unchanged-client promise.

## R-10 · Contract strictness

- **Decision**: validate against the pinned snapshot exactly. Choice `criteria` must be a map (1 to 255
  entries). Score must have 2 to 10 levels. Noul criteria keys are only `true`/`false`. `instructions` is a
  string, object or array. `state` can be any JSON including null. Violations return 422 with a body that
  lists each offending JSON path. Laya's laxer inputs (a list-form choice, a one-level score) are rejected,
  because Principle XIV puts the contract first. Extra request fields: unknown top-level `x-tau-*`
  fields are allowed and anything else returns 422.
- **Response**: exactly the contract's fields. Laya's `answer_confidence`, `action` and noul `confidence`
  are dropped. Diagnostics go in `x-tau-*` response headers.

## R-11 · Web host, observability, JSON

- **Decision**: ASP.NET Core minimal API on .NET 10, System.Text.Json with `JsonNode` for free-form JSON
  (which keeps object key order, needed because option order is positional). OpenTelemetry .NET SDK for
  traces and metrics (OTLP exporter, off unless configured). The Prometheus text endpoint `/metrics` uses
  `OpenTelemetry.Exporter.Prometheus.AspNetCore`, a pinned prerelease, which is acceptable because it's
  pinned (Principle II). If it's unusable, the fallback is `prometheus-net.AspNetCore` fed by the same
  counters.
- **Container**: multi-stage Dockerfile on the official .NET 10 runtime image (CPU and CUDA variants),
  built locally with Docker Desktop and never pushed.

## R-12 · Tests

- **Decision**: xUnit v3 for .NET (plain `Assert`, no FluentAssertions because of its commercial licence
  change), `Microsoft.AspNetCore.Mvc.Testing` for in-process HTTP tests, `JsonSchema.Net` (MIT) for contract
  schema validation. pytest for the sidecar. Tests that need the fetched models are tagged
  `Category=Models` and **fail with a fetch instruction** if the models are missing. They never skip
  silently, because the parity gate has to be a gate.

## R-13 · Conformance peer

- **Decision**: Kev-0.8B (`jaredpalmer/kev`, Apache-2.0, about 4 GB VRAM) via `uv sync --extra serve`. Kev
  documents Linux and macOS only, so the order is: native Windows → WSL Ubuntu (installed, stopped) →
  `von serve` (von-sdk's own contract server). The conformance tool validates both servers' responses against
  the snapshot schema. Tau must be strict, and extra fields from the peer count as "peer extensions"
  (recorded, not failed). Differences are classified structural (fails) or model disagreement (recorded).

## R-14 · Latency benchmark

- **Decision**: `tools/Tau.Bench` console, one command (`scripts/bench.ps1`). For each model and each
  provider (CUDA on the 3080 Ti, CPU), run one state with 1, 4 and 10 questions. Measure both in-process
  model time and end-to-end HTTP time against a locally started Runtime. 50 warm-up iterations are
  excluded, then 500 measured for GPU and 100 for CPU. Report p50/p95/p99, mean and stddev as Markdown
  and JSON in `reports/r1/`, with the hardware fingerprint (GPU name, VRAM, driver, CPU model, RAM,
  OS), checkpoint hashes, ORT version and provider, contract version, date and command line.
  The run-to-run variance of two back-to-back runs is recorded in the report so SC-007 can be judged.
- **Precision**: GPU numbers are FP32 unless an FP16 export passes parity-within-tolerance on its own. R1
  doesn't chase FP16, and the report states the precision.

## R-15 · Dataset licences (for R2, checked in R1)

- **Decision**: check Banking77 (PolyAI, reported CC-BY-4.0) and candidate support-ticket sets with urgency
  or priority labels from their dataset cards at a pinned revision. Record each in `docs/DECISIONS.md`.
  Anything non-commercial-only or without a licence is dropped if a publishable alternative exists.
- **Proven by**: the licence task (reads the dataset cards and records the licence and revision).

## R-16 · Where the brainstorm and the reference disagree (recorded, owner informed)

- State isn't "encoded once". Each question gets its own sequence, and they're batched per request.
  Decided in clarify.
- Laya's contract `confidence` for choice and score (1 − normalised entropy) isn't what temperature
  scaling calibrates. Laya reports `answer_confidence = max(p)` separately for that reason. **Carried to R2:**
  the Workbench's ECE uses `max(p)` and says so. The contract field is left as the reference defines it.
