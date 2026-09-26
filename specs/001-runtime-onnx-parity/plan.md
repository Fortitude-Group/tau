# Implementation Plan: Tau R1 — Runtime core and model parity

**Branch**: `001-runtime-onnx-parity` | **Date**: 2026-09-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-runtime-onnx-parity/spec.md`

## Summary

Serve Laya (three checkpoints) and Von 1.2.0 behind the published `/v1/systemone` contract from a .NET 10
server, with ONNX Runtime doing inference. The work starts with an export spike. A Python 3.12 sidecar
exports each checkpoint to ONNX and generates committed parity fixtures from the maintained reference
runtimes (`laya==0.3.20`, `von-sdk==1.2.3`). Four gates must be green before anything else counts: C#
tokenisation, ONNX logits, C# end-to-end answers and routing, each matched against those fixtures. On top
sit the contract types and validation, a shared calibration library, the ASP.NET Core host (batched pass
per request, per-model reference post-processing, script routing, calibrator hook, CPU/CUDA/DirectML via
one managed binary), a typed client, a conformance suite against a local Kev, and a latency benchmark on
the RTX 3080 Ti. Details are in [research.md](research.md).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.400) for everything shipped. Python 3.12 (uv-locked) for
the export and parity sidecar only.

**Primary Dependencies**: Microsoft.ML.OnnxRuntime 1.24.4 (Managed plus CPU/Gpu/DirectML natives),
Tokenizers.DotNet 1.4.1, ASP.NET Core 10, OpenTelemetry .NET, JsonSchema.Net. Sidecar: torch (CUDA 12
wheel), transformers 5.x, laya 0.3.20, von-sdk 1.2.3, onnx, onnxruntime.

**Storage**: files only. Models are fetched to `models/` (gitignored, hash-pinned). Committed fixtures go in
`tests/fixtures/`, committed reports in `reports/r1/`, calibrator JSON files come from config.

**Testing**: xUnit v3 plus Microsoft.AspNetCore.Mvc.Testing plus JsonSchema.Net. pytest for the sidecar.
Model-dependent tests are tagged `Category=Models` and fail loudly without models.

**Target Platform**: Windows 10/11 x64 (reference machine) and Linux x64 (container). NVIDIA CUDA 12 GPU,
DirectX 12 GPU (Windows), or CPU.

**Project Type**: web service + libraries + CLI tools (server, client library, bench and conformance tools).

**Performance Goals**: no gating target in R1. Measure and publish. Context: Laya reports about 33 ms for one
question and 7.2 ms/question batched on a T4, and hosted Jev is reported at p50 236 to 380 ms (third party).

**Constraints**: parity FP32/CPU max |Δlogit| ≤ 2e-3, |Δprob| ≤ 1e-3, identical argmax. Deterministic
responses. Zero spend. No contract extensions (only `x-tau-*`). 12 GB VRAM. About 96 GB free disk (models
about 4 GB, torch about 5 GB, Kev about 2 GB).

**Scale/Scope**: single-tenant self-hosted server. Up to 50 questions per request, 255 options per choice.
Four models are resident if VRAM allows (about 1.7 GB FP32 each), otherwise they load on demand with an LRU
cap (default 2, as in the reference).

## Constitution Check

*GATE: must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | How the plan complies |
| --- | --- | --- |
| I Modular & Composable | ✅ | Separate projects for contract, calibration, inference and host. The host is a thin consumer. |
| II Contract Stability & SemVer | ✅ | Contract pinned as a dated snapshot. Every package pinned to an exact version. Client and Runtime start at 0.1.0. |
| III Comprehensive Tests | ✅ | Unit tests for contract, calibration, serialisers and routing. Parity gates. HTTP integration tests. Edge cases from the spec. |
| IV Deterministic & Observable | ✅ | Determinism test (SC-005). Per-request telemetry, OTel and Prometheus. |
| V Simplicity | ✅ | See Complexity Tracking for the two justified additions (native resolver, Python sidecar). |
| VI Complete the Scope | ✅ | Every FR maps to tasks. Nothing deferred inside R1. |
| VII Records in sync | ✅ | PROGRESS and DECISIONS updated per task. Commits reference R1 and the task id. |
| VIII Fresh base | ✅ | Solo repo with no remote yet. Branch from `master`, merge back at the gate. |
| IX Ask, then wait | ✅ | Clarify questions answered. The R1 gate stops for "go". |
| X Production waits | ✅ | Nothing published or pushed in R1. Docker image and NuGet stay local. |
| XI Mechanism first | ✅ | Reference = code actually read, not model cards. Every risky claim has a proving task. |
| XII Explain every number | ✅ | Reports carry what, why and provenance. The bench report states precision and variance. |
| XIII Measured, not claimed | ✅ | One command per report, with hardware, hashes, dataset/contract version and date embedded. |
| XIV Contract-first | ✅ | Strict schema. Non-contract reference fields dropped. Extras only as `x-tau-*`. |
| XV One calibration library | ✅ | `Tau.Calibration` is the only home of the maths and the file format. The Runtime consumes it. |
| XVI Zero extra cost | ✅ | Local models, free CUDA wheels, no API calls, no key. No frontier calls in R1. |
| XVII Named data exclusions | ✅ | No fleet or vehicle data. The fixture corpus is written by hand or drawn from the checkpoints' own examples. Licences checked. |
| Gate 8 Reproducibility & pinning | ✅ | Parity and conformance are merge gates. Hashes are checked at load. No secrets. |

Result: **PASS** (pre-design and post-design; the design introduced no new violations).

## Project Structure

### Documentation (this feature)

```text
specs/001-runtime-onnx-parity/
├── plan.md              # this file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/           # Phase 1: HTTP contract + calibrator schema + model package manifest
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
contracts/systemone/2026-09-27/     # pinned contract: request/response JSON Schema + human-readable reference
src/
├── Tau.Contract/                   # request/response types, validation (422 body), schema (embedded)
├── Tau.Calibration/                # temperature, isotonic, ECE/Brier/log loss, calibrator file v1
├── Tau.Inference/                  # tokeniser, PyJson/PyRepr, sequence builders, ORT sessions,
│   ├── Laya/                       #   native resolver, Laya + Von post-processing, script router
│   ├── Von/
│   ├── Routing/
│   └── Onnx/
├── Tau.Runtime/                    # ASP.NET Core host: endpoint, config, telemetry, /metrics, Dockerfile
└── Tau.Client/                     # typed client (NuGet, local only)
tools/
├── Tau.Bench/                      # latency benchmark → reports/r1/latency-*.{md,json}
└── Tau.Conformance/                # conformance runner → reports/r1/conformance-*.{md,json}
sidecar/finetune/                   # uv project (Python 3.12): export, reference runners, fixture generators, parity
│   ├── tau_sidecar/
│   └── tests/
tests/
├── Tau.Contract.Tests/
├── Tau.Calibration.Tests/
├── Tau.Inference.Tests/            # tokeniser / serialiser / routing / logits / end-to-end parity (fixtures)
├── Tau.Runtime.Tests/              # HTTP integration, determinism, validation, calibrator hook
├── Tau.Client.Tests/
├── conformance/requests/           # committed conformance request set
└── fixtures/                       # committed parity fixtures (generated by the sidecar)
scripts/                            # fetch-models.ps1, fetch-cuda.ps1, export.ps1, parity.ps1,
                                    # conformance.ps1, bench.ps1, build-test.ps1
reports/r1/                         # committed reports
models/  native/  (gitignored)      # fetched checkpoints, exported ONNX, CUDA wheels
Tau.slnx
```

**Structure Decision**: the brainstorm's monorepo layout, extended with `Tau.Inference`. The engine is used
by the host, the benchmark and the parity tests, so under Principle I it can't live inside the host. It also
adds `tools/`, `contracts/` and `reports/`. `tests/conformance` holds the request set, and the runner is a
tool so it can target any base URL.

## Complexity Tracking

| Violation | Why needed | Simpler alternative rejected because |
| --- | --- | --- |
| Native-library resolver choosing the ORT flavour at startup | Brief: CPU/CUDA/DirectML from the same binary, selected by config | Per-flavour builds are simpler but break "same binary". A single native package can't hold DirectML and CUDA together. |
| Python sidecar in R1 (export and fixtures) | The export and the reference outputs can only come from PyTorch and the vendor runtimes | Hand-written C# reference answers would be circular. The brainstorm already puts the sidecar in scope. |
| `Tau.Inference` as a separate project | Shared by the host, bench and parity tests | Folding it into the host would make the bench and tests depend on ASP.NET (violates Principle I). |
