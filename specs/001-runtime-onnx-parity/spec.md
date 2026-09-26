# Feature Specification: Tau R1 — Runtime core and model parity

**Feature Branch**: `001-runtime-onnx-parity`

**Created**: 2026-09-27

**Status**: Draft

**Input**: User description: "Tau R1: Runtime core + ONNX parity" (full text in the `/speckit-specify` invocation). Sources: `docs/brainstorm.md` (Component A, finish line, risks) and `docs/DECISIONS.md` (all 2026-09-26/27 entries are binding).

## Clarifications

### Session 2026-09-27

- Q: What tolerance should the parity gate enforce (full-precision export against the full-precision reference, on CPU)? → A: max |Δ option score| ≤ 2×10⁻³, max |Δ probability| ≤ 1×10⁻³, identical top answer on every case. Reduced-precision GPU drift is reported, not gated.
- Q: How should R1 handle the brainstorm's "encode the state once" when both models put each question in the same sequence as the state? → A: batch all of a request's questions into one forward pass (one sequence per question). Articles describe it that way and never claim the state is encoded once.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prove the exported models answer exactly like the originals (Priority: P1)

A maintainer exports Laya and Von from their published checkpoints into the portable model format the Runtime loads, then runs one command that feeds the same inputs through the original reference model and the exported one and shows that the outputs agree on every test case.

**Why this priority**: Nothing else in Tau can be trusted if the exported model disagrees with the original. The brainstorm names export as the biggest technical risk, and the parity test gates everything after it.

**Independent Test**: Run the parity command on a clean checkout with the pinned checkpoints downloaded. It passes or fails on its own. No server or client is involved.

**Acceptance Scenarios**:

1. **Given** the pinned Laya checkpoints (English, multilingual, typed-decisions) and the pinned Von 1.2.0 checkpoint, **When** the export command runs, **Then** one exported model per checkpoint is produced, each tagged with the source checkpoint's content hash.
2. **Given** the exported models and the parity case set (choice, score and noul questions, 2 to 255 options, short and long states, multiple languages, batches of 1 and many), **When** the parity command runs, **Then** every case is within tolerance, every case's top answer matches the reference, and a committed parity report records the maximum deviations.
3. **Given** the parity case set, **When** the Runtime's own text-to-token conversion runs over it, **Then** it produces exactly the same token sequence as the reference tokeniser for every case, including special markers inside text, non-Latin scripts, digits and inputs long enough to be truncated.

---

### User Story 2 - Swap the base URL and get the same kind of decisions locally (Priority: P1)

A developer who already calls the hosted `/v1/systemone` endpoint (with the official SDK, a Kev client or LiteLLM) points their base URL at a local Tau Runtime and gets typed answers back with no other code change.

**Why this priority**: This is the product. "Swap the base URL, nothing else changes" is the Runtime's whole promise.

**Independent Test**: Start the Runtime and send the contract's own example requests from an unmodified client. Answers come back in the contract shape, and bad requests are rejected the way the contract says.

**Acceptance Scenarios**:

1. **Given** a running Runtime, **When** a client sends a request with a text state and choice, score and noul questions, **Then** it receives one answer per question keyed by the question's name: choice with the selected option, a probability for every option summing to 1, and confidence; score with the probability-weighted value, the level legend, probabilities and confidence; noul with a probability between 0 and 1. Usage token counts are included.
2. **Given** a running Runtime, **When** the state is structured data (an object, an array, nested data or null) instead of text, **Then** the request is accepted and answered.
3. **Given** a request that breaks the contract (a missing required field, an unknown question type, a choice with more than 255 options, a score with fewer than 2 or more than 10 levels, or a malformed body), **When** it's sent, **Then** the Runtime rejects it with the contract's validation-error status and a message naming the offending field.
4. **Given** a request that pins a model, **When** it's answered, **Then** that model is used. **Given** no pinned model, **When** the state is in a non-Latin script, **Then** the multilingual model is chosen automatically, and English text goes to the English model.
5. **Given** a request with several questions, **When** it's answered, **Then** each question's answer is identical to the answer it would get if sent alone. Questions never influence each other.
6. **Given** the same request sent twice, **When** both are answered, **Then** the answers are identical.

---

### User Story 3 - Show the Runtime behaves like other contract servers (Priority: P2)

A maintainer runs one command that sends a fixed set of requests to the Tau Runtime and to an independent open implementation of the same contract (Kev), checks every response against the pinned contract definition, and reports the differences.

**Why this priority**: Being compatible with other servers is a claim about them as well as us. The finish line requires it, and it's what catches drift if the contract or checkpoints change.

**Independent Test**: With both servers running, one command produces a conformance report. It passes when every Tau response matches the contract definition and every structural difference from Kev is either absent or explained.

**Acceptance Scenarios**:

1. **Given** Tau and a local Kev server, **When** the conformance command runs, **Then** every request's response from both servers is checked against the pinned contract definition and the results are committed as a report.
2. **Given** the conformance report, **When** a reader looks at it, **Then** each difference between Tau and Kev is classified as structural (a contract problem, which fails) or a model disagreement (a different answer, which is expected and recorded, not failed).

---

### User Story 4 - Know how fast it is on real hardware (Priority: P2)

A reader of the articles wants latency they can believe: single question and batched questions, per model, on the stated GPU and on CPU, reproducible with one command.

**Why this priority**: The brainstorm's speed claims (local beats hosted by an order of magnitude) are only credible with a reproducible measurement tied to named hardware.

**Independent Test**: Run the benchmark command. It produces a committed report with hardware, checkpoint hashes, contract version and date, and per-model latency distributions.

**Acceptance Scenarios**:

1. **Given** the Runtime on the reference GPU, **When** the benchmark runs, **Then** it records p50/p95/p99 latency for 1 question and for batches of several questions against one state, for every model, with warm-up excluded.
2. **Given** the same benchmark on CPU only, **When** it runs, **Then** the same figures are recorded and labelled CPU.

---

### User Story 5 - Use Tau from C# with types (Priority: P3)

A .NET developer calls the Runtime (or any contract server) through a typed client: ask a question whose options are an enum and get back the enum value plus the probabilities, or send a raw contract request.

**Why this priority**: The typed client is a deliverable and the easiest route for the .NET audience, but the Runtime's HTTP surface is usable without it.

**Independent Test**: A sample program uses the client against a running Runtime and gets a typed answer.

**Acceptance Scenarios**:

1. **Given** an enum of options, **When** the developer asks for a decision on a state, **Then** they receive the chosen enum value, the probability for every enum member, and the confidence.
2. **Given** a raw contract request, **When** it's sent through the client, **Then** the response is returned as typed contract objects, and contract validation errors surface as a typed error with the offending field.

---

### User Story 6 - Load calibrators and see raw scores on demand (Priority: P3)

A maintainer drops calibrator files (produced later by the Workbench) into the Runtime's configuration, and every answer uses them. The raw, uncalibrated scores can still be requested for measurement.

**Why this priority**: R2's calibration work needs the Runtime to apply calibrators and expose raw scores. Building the hook now means R2 doesn't have to reopen the Runtime.

**Independent Test**: With a hand-made calibrator file configured, an answer's probabilities change as the calibrator dictates. With the raw-scores flag set, the reference model's own outputs are returned instead.

**Acceptance Scenarios**:

1. **Given** a calibrator for a question type, **When** such a question is answered, **Then** the calibrated probabilities are returned and the calibrator's version is recorded in telemetry.
2. **Given** the raw-scores flag on a request, **When** it's answered, **Then** the model's own post-processed outputs are returned, the response shape stays within the contract, and any extra information appears only in fields or headers prefixed `x-tau-`.
3. **Given** a malformed or wrong-version calibrator file, **When** the Runtime starts, **Then** it refuses to start and names the file and the problem. It never silently runs uncalibrated.

---

### Edge Cases

- A choice with so many or such long options that they can't all fit the model's option budget: the request is rejected with a validation error naming the question. A truncated answer space is never silently returned (this matches the reference behaviour).
- A state longer than the model's context: it's truncated the way the reference implementation truncates, and the fact is recorded in telemetry.
- Empty state (`""` or `null`): accepted and answered.
- Question names with unusual characters, and 1 to 50 questions in one request: all answered and keyed back exactly.
- Option keys or text containing the model's special marker tokens: neutralised exactly as the reference implementation does.
- Duplicate option descriptions: each option key still gets its own probability.
- The GPU is missing or unavailable when configured: the Runtime fails at startup with a clear message rather than quietly running on CPU (unless configured to fall back).
- A pinned model id that isn't installed: a validation error naming the model and listing the installed models.
- Concurrent requests: answers are identical to the same requests sent one at a time.

## Requirements *(mandatory)*

### Functional Requirements

**Model export and parity**

- **FR-001**: The system MUST export each pinned checkpoint (Laya English, Laya multilingual, Laya typed-decisions, Von 1.2.0) into the Runtime's portable model format with one command, including the whole decision computation that the reference implementation runs on the accelerator.
- **FR-002**: Each exported model MUST carry the source checkpoint's repository, commit and content hash, and the Runtime MUST refuse to load an exported model whose recorded hash doesn't match its configuration.
- **FR-003**: The system MUST provide a parity command that runs a fixed, committed case set through the reference and exported models and fails if any case's output deviates beyond tolerance or any top answer differs.
- **FR-004**: The parity case set MUST cover all three question types, option counts from 2 up to the largest that fits each model, short, long and truncated states, structured and text states, at least five non-Latin scripts for the multilingual model, and batch sizes of 1 and many.
- **FR-005**: The Runtime's text-to-token conversion MUST produce exactly the reference tokeniser's output on every case in the parity case set, verified by an automated test.
- **FR-006**: If export fails after two genuinely different approaches, the system MUST fall back to a separate reference-inference process behind the Runtime with the same external behaviour, and the fallback MUST be recorded in `docs/DECISIONS.md` and reported to the owner.

**Contract**

- **FR-007**: The Runtime MUST accept `POST /v1/systemone` with `model`, `state` (text, object, array or null) and `questions` (a map of named questions) exactly as defined in the pinned contract snapshot (2026-09-27).
- **FR-008**: The Runtime MUST support choice questions (option map, 1 to 255 options, optional descriptions), score questions (ordered levels, 2 to 10) and noul questions (optional true/false descriptions), with `instructions` given as text or structured data.
- **FR-009**: The Runtime MUST return `model`, `answers` (one per question, keyed by the question's name) and `usage` (`input_tokens`, `output_tokens`). Answer shapes: choice has `type`, `choice`, `probabilities` and `confidence`. Score has `type`, `score`, `legend`, `probabilities` and `confidence`. Noul has `type` and `noul`. Probabilities sum to 1 within rounding.
- **FR-010**: The Runtime MUST reject invalid requests with the contract's validation status (422) and a machine-readable body naming each offending field. It MUST NOT answer a request it can't answer faithfully.
- **FR-011**: The Runtime MUST NOT add response fields outside the contract. Any Tau-specific information MUST use an `x-tau-` prefix.
- **FR-012**: The pinned contract MUST be committed as a machine-checkable definition plus a human-readable copy, dated and versioned, and the conformance and unit tests MUST validate against it.

**Inference behaviour**

- **FR-013**: For each request, the Runtime MUST answer all questions for a given model in one batched pass (one sequence per question, since the state can't be encoded once and reused with these models), and each question's answer MUST be independent of the other questions in the request.
- **FR-014**: The Runtime MUST reproduce each model's reference answer post-processing exactly: state serialisation, option rendering, temperature rules, confidence formula, noul orientation and any prior correction. Answers MUST match the reference implementation within parity tolerance.
- **FR-015**: The Runtime MUST route unpinned requests between Laya English and Laya multilingual by detecting the state's script, and MUST honour a pinned model id. Installed model ids MUST be discoverable.
- **FR-016**: The Runtime MUST be deterministic: identical requests MUST yield identical responses, whatever the concurrency.
- **FR-017**: The Runtime MUST run on CPU, on an NVIDIA GPU and on a DirectX 12 GPU from one codebase, with the execution target selected by configuration. It MUST fail at startup, with a clear message, if the configured target is unavailable, unless CPU fallback is explicitly configured.

**Calibration hook**

- **FR-018**: A shared calibration library MUST provide temperature scaling, isotonic regression, expected calibration error, Brier score and log loss, and MUST read and write a versioned calibrator file format. The Runtime and (from R2) the Workbench MUST both use it and MUST NOT reimplement it.
- **FR-019**: The Runtime MUST load calibrators per model and question type from configuration and apply them to every answer. It MUST refuse to start on an invalid or incompatible calibrator file.
- **FR-020**: A request MUST be able to ask for raw (reference post-processed, not Tau-calibrated) outputs through an `x-tau-` flag.

**Observability and packaging**

- **FR-021**: The Runtime MUST record, per request: latency (total and model time), token counts, batch size, model id and version, calibrator versions, and whether truncation happened. It MUST export these as OpenTelemetry signals and on a Prometheus-format metrics endpoint.
- **FR-022**: The Runtime MUST build as a self-contained single-file executable and as a container image built locally. Neither is pushed anywhere in R1.
- **FR-023**: A typed .NET client package MUST provide a typed decision call (enum options in, enum answer plus probabilities and confidence out) and a raw contract call. It's built locally and not published in R1.

**Conformance, benchmarks and licences**

- **FR-024**: A conformance command MUST run a committed request set against Tau and a local Kev server, validate every response against the pinned contract, classify each difference as structural or model disagreement, and write a committed report. Structural failures fail the suite.
- **FR-025**: A benchmark command MUST measure per-model latency (p50/p95/p99, warm-up excluded) for 1 question and for batched questions against one state, on the reference GPU and on CPU, and write a committed report.
- **FR-026**: Every report (parity, conformance, benchmark) MUST record the hardware, the checkpoint hashes, the contract version, the date, the command that produced it and the software versions, and MUST be regenerable by that one command.
- **FR-027**: The licences of the R2 datasets (Banking77 and an open support-ticket set with urgency labels) MUST be checked and recorded before R1 closes. Anything that can't be published MUST be dropped and reported to the owner.

### Key Entities

- **Decision request**: model id (optional pin), state (text or structured data), named questions.
- **Question**: type (choice, score or noul), instructions, criteria (option map, ordered levels, or true/false descriptions).
- **Answer**: typed result per question with probabilities and confidence, keyed by question name.
- **Model package**: exported model plus tokeniser, reference post-processing parameters, and source checkpoint identity (repo, commit, content hash).
- **Calibrator**: versioned mapping from a model's raw probabilities to calibrated ones for one question type (and option-count bucket), with its fitting metadata.
- **Report**: a committed record of a measurement (parity, conformance or latency) with provenance: hardware, hashes, contract version, date and command.
- **Contract snapshot**: the dated, pinned definition of `/v1/systemone` that every test validates against.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of parity cases pass for all four exported models: the top answer is identical and every output is within tolerance (see Assumptions).
- **SC-002**: 100% of tokeniser parity cases produce identical token sequences.
- **SC-003**: An unmodified contract client gets valid answers by changing only its base URL. 100% of conformance requests return contract-valid responses from Tau, with zero structural differences from the contract.
- **SC-004**: 100% of requests invalid under the contract are rejected with the validation status and a named field. 0% are answered.
- **SC-005**: A request's answers are identical whether its questions are sent together or one at a time, and whether requests arrive one at a time or concurrently (verified over at least 100 requests).
- **SC-006**: Latency figures exist for every model, on GPU and CPU, for single and batched questions, each traceable to a committed report stating the hardware and date.
- **SC-007**: Every report can be regenerated by one documented command on the reference machine, and its headline figures reproduce within the variation the report itself records.
- **SC-008**: All automated tests pass, and every dataset planned for R2 has a recorded, publishable licence (or has been dropped and reported).

## Assumptions

- **Parity tolerance** (agreed with the owner 2026-09-27): comparing full-precision exported models to the full-precision reference on CPU, the maximum absolute difference is ≤ 2×10⁻³ per option score and ≤ 1×10⁻³ per output probability, and the top answer is identical on every case. Reduced-precision GPU runs are reported against the same reference with their measured deviation. They're a benchmark figure, not the parity gate.
- The pinned contract snapshot (2026-09-27) is the authority. The hosted Jev endpoint isn't called (no key, no spend). Any Jev figure is third-party and labelled so.
- Reference behaviour means the published inference code in each checkpoint's repository at the pinned commit (Laya's `rl_agent_api.py`/`rl_common.py`, Von's `von-sdk` 1.2.3 option-marker backend).
- Kev-0.8B is the conformance peer. If it can't run on this machine, the fallback is a Linux environment on the same machine, then Von's own contract server. Whichever is used gets recorded.
- The reference hardware is an NVIDIA RTX 3080 Ti (12 GB, driver 610.47). The GPU runtime libraries it needs are obtained free.
- The Runtime is single-tenant and self-hosted. Authentication is out of scope: an `Authorization` header is accepted and ignored, so contract clients that always send one still work.
- Usage token counts report the tokens actually processed by the model. `output_tokens` is 0 because nothing is generated, which matches the reference Laya implementation.
- Everything built in R1 stays local. Nothing is published, pushed to a registry or made public.
