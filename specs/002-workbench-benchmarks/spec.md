# Feature Specification: Tau R2 — Workbench and benchmarks on two datasets

**Feature Branch**: `002-workbench-benchmarks`

**Created**: 2026-09-27

**Status**: Draft

**Input**: User description: "Tau R2: Workbench + benchmarks on two datasets" (full text in the `/speckit-specify` invocation). Sources: `docs/brainstorm.md` (Component B, headline demo, finish line, article-plan honesty rules) and `docs/DECISIONS.md` (binding, including the 2026-09-27 dataset correction).

## Clarifications

### Session 2026-09-27

- Q: Which Claude list price should the cascade's £ figures use? → A: The headline is Claude Opus 5.5 at standard list price ($4 / $20 per million input/output tokens, from platform.claude.com/docs/en/about-claude/pricing, checked 2026-09-27), since that model produced the frontier answers. Extra rows show the Batch API rate (50% off), and price-only "if escalated to Sonnet 5 ($2/$10) / Haiku 4.5 ($1/$5)" rows, labelled as unmeasured for accuracy.
- Q: How many held-out items per dataset get a second frontier answer with a different prompt wording? → A: 200 items per dataset.
- Q: What counts as "materially lower" calibration error? → A: At least a 50% relative reduction in held-out ECE for the out-of-the-box model on each dataset. A miss is reported with the reason.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find out whether a model's confidence can be trusted (Priority: P1)

A developer has a decision they want a System One model to make (for example "which of 77 banking intents is this?") and a labelled set of examples. They point the Workbench at any `/v1/systemone` endpoint and get one answer: how accurate the model is, and how far its stated confidence is from reality, shown as reliability diagrams and calibration error.

**Why this priority**: This is the Workbench's reason to exist. Laya ships with a calibration error of 0.466 by its own account, so an uncalibrated confidence score looks certain and means nothing. Measuring that is the first thing anyone needs.

**Independent Test**: With a decision spec and a labelled held-out set, one command measures a running endpoint and writes accuracy, calibration error, Brier score and log loss, plus a reliability diagram, to disk.

**Acceptance Scenarios**:

1. **Given** a decision spec (question, type, options, data source) and a held-out set with gold labels, **When** the measure stage runs against an endpoint, **Then** it records every answer with its full probability distribution and reports accuracy, expected calibration error, Brier score and log loss, per question type.
2. **Given** the same inputs, **When** measured twice against the same model, **Then** the numbers are identical.
3. **Given** an endpoint that is not Tau (any server speaking the contract), **When** measured, **Then** it works unchanged, apart from Tau-only diagnostics being absent.

---

### User Story 2 - Fix the confidence and prove it's fixed (Priority: P1)

The developer fits a calibration on a separate calibration set, loads it into the Runtime, and re-measures on the untouched held-out set. The report shows calibration error before and after, side by side.

**Why this priority**: Measuring a problem without fixing it is half a product. The R2 gate requires the after figure to be materially lower on both datasets.

**Independent Test**: One command fits temperature and isotonic calibrators per question type from the calibration set, writes calibrator files, and re-measures the held-out set through a Runtime that has loaded them.

**Acceptance Scenarios**:

1. **Given** measured answers on a calibration set that doesn't overlap the held-out set, **When** calibration runs, **Then** temperature and isotonic calibrators are fitted per question type (and option-count bucket where the data supports it) and written as calibrator files with their provenance.
2. **Given** those calibrators loaded by the Runtime, **When** the held-out set is re-measured, **Then** the report shows calibration error before and after, for each method, with the number of calibration examples each fit used.
3. **Given** too few calibration examples for isotonic regression, **When** calibration runs, **Then** it falls back to temperature scaling and says so in the report.

---

### User Story 3 - Decide how much work can stay local, and what that saves (Priority: P1)

The developer sets a target error rate. The Workbench picks the confidence threshold τ that meets it, shows the trade-off curve between how many decisions are accepted locally and how accurate they are, and simulates a cascade: accept the local answer above τ, send the rest to a frontier model. It reports what share stays local, the blended accuracy against the frontier model alone, and the cost in pounds per 1,000 and per million decisions.

**Why this priority**: This is the headline the brainstorm promises ("X% of decisions stay local, accuracy within Y points of Claude-only, £Z saved per million"), and it's what the articles are built on.

**Independent Test**: With calibrated held-out answers and cached frontier answers for the same items, one command produces the threshold, the trade-off curve and the cascade table.

**Acceptance Scenarios**:

1. **Given** a target error rate, **When** the threshold stage runs, **Then** it reports τ, the accept rate and the accuracy on accepted items at that τ, and the whole accept-rate/accuracy curve.
2. **Given** τ and the frontier model's cached answers, **When** the cascade stage runs, **Then** it reports the share kept local, blended accuracy, frontier-only accuracy, share escalated, the latency mix and cost per 1,000 and per million decisions for the cascade and for frontier-only, with the price basis and date stated and every cost labelled an estimate.
3. **Given** no τ meets the target, **When** threshold runs, **Then** it says so plainly and reports the best achievable error rate instead of inventing a threshold.

---

### User Story 4 - Get frontier answers without an API or spending money (Priority: P1)

The developer (here, Rob's Claude Code session) needs frontier-model answers for the held-out items: as the cascade's escalation target, as the frontier-only baseline, and as a label-noise check against gold labels. The Workbench writes the items as batch files. The frontier answers them in the interactive session. The Workbench reads the answers from an on-disk cache and never asks for an item twice.

**Why this priority**: The brief rules out paid APIs and caps frontier work at 1,000 held-out items per dataset. Without this route there's no cascade.

**Independent Test**: The label stage exports a batch. Answers written to the cache are ingested with provenance. Re-running the stage finds everything cached and exports nothing new.

**Acceptance Scenarios**:

1. **Given** held-out items with no cached answer, **When** the label stage runs, **Then** it writes batch files of pending items, each with a fixed per-item prompt, and stops without guessing.
2. **Given** answers in the cache, **When** the label stage runs again, **Then** it ingests them, validates each against the question's allowed options, records provenance (model, date, prompt version, "interactive session, not an API") and exports only what's still missing.
3. **Given** more items than the cap, **When** export runs, **Then** it never exports more than 1,000 held-out items per dataset.
4. **Given** gold labels exist, **When** labelling completes, **Then** the report states the frontier model's disagreement with gold (label noise), and the agreement between two prompt wordings on a 200-item subset.

---

### User Story 5 - Fine-tune the local model and see whether it's worth it (Priority: P2)

The developer fine-tunes Laya on the training split with the Python sidecar, exports it to the Runtime through the R1 export and parity path, and measures it like any other model. A small classic encoder, fine-tuned on the same data, runs alongside it as an honesty baseline.

**Why this priority**: Laya's own card says it needs fine-tuning (0.425 on Banking77 out of the box). The brainstorm also says static tasks favour classic fine-tuning, and that claim should be tested here, not cited.

**Independent Test**: One command fine-tunes, exports and parity-checks a Laya checkpoint. A second trains the classic baseline. Both are measured by the same measure stage, or the same metric code.

**Acceptance Scenarios**:

1. **Given** the training split, **When** fine-tuning runs on the reference GPU, **Then** it produces a checkpoint whose ONNX export passes the R1 parity tolerance against its PyTorch weights, with a manifest recording the training data revision, seed and settings.
2. **Given** the fine-tuned model is installed in the Runtime, **When** measured, **Then** its accuracy and calibration are reported next to the out-of-the-box model on the same held-out set.
3. **Given** the classic baseline is trained, **When** reported, **Then** its accuracy (and its calibration, where it produces probabilities) appears in the same table, including where it beats Tau's models.

---

### User Story 6 - One file to read, screenshot and publish from (Priority: P2)

Each run produces a single self-contained HTML report: reliability diagrams before and after, the trade-off curve, the cost table, confusion matrices, label noise, and run metadata. It opens offline and reads cleanly in a screenshot.

**Why this priority**: The report is the visual for the articles in R3, and the single place every number is traced back to.

**Independent Test**: Open the HTML file with no network. Every chart and table renders, and the metadata block names hardware, model hashes, dataset revision, date and the command.

**Acceptance Scenarios**:

1. **Given** a completed run, **When** the report stage runs, **Then** one HTML file is written that needs nothing beyond itself to render.
2. **Given** the report, **When** a figure is shown, **Then** the page states what it is, why it matters and what follows from it, next to it.

---

### User Story 7 - Run the whole thing with one command (Priority: P2)

`tau run` takes a decision spec and runs every stage end to end, reusing artefacts already on disk, and produces the report. The two worked examples (Banking77 and the synthetic support tickets) are committed with their reports and reproduce from one command each.

**Independent Test**: From a clean checkout with models fetched, one command per dataset regenerates its committed report, and the numbers match within the report's stated variation.

**Acceptance Scenarios**:

1. **Given** a worked example directory, **When** `tau run` executes, **Then** every stage runs or is skipped because its artefact is current, and the report is regenerated.
2. **Given** cached frontier answers are committed, **When** the example is re-run on another checkout, **Then** no new frontier work is needed.

---

### Edge Cases

- A held-out item whose text exceeds the model's context: measured as the Runtime truncates it, and counted in a "truncated" figure in the report.
- A frontier answer that isn't one of the allowed options (or is malformed): rejected at ingest, reported, and the item left pending. Never coerced into a label silently.
- An endpoint that returns an error for some items: those items are reported as failures with their error, and excluded from metrics with the exclusion stated (constitution XII).
- A class with no examples in the calibration set: its calibration falls back to the question-type calibrator, and the report says which classes did.
- Duplicate texts across splits: detected and removed from the held-out set so the model isn't evaluated on training data.
- Vehicle or transport content in the ticket data: excluded before any split is made (constitution XVII), and the exclusion counted in the dataset manifest.
- The Runtime and the Workbench disagree about a calibrated score: impossible by construction, because both use the shared calibration library. Tested by applying one calibrator in both and comparing.

## Requirements *(mandatory)*

### Functional Requirements

**Decision spec and data**

- **FR-001**: The Workbench MUST read a YAML decision spec: question name, instructions, type (choice, score or noul), options (choice keys with descriptions, or ordered score levels), the model or endpoint to measure, and the data source (CSV, JSONL or a folder of text files) with the field holding the state and the field holding the gold label.
- **FR-002**: The Workbench MUST split the data deterministically (fixed seed) into non-overlapping training, calibration and held-out sets. It MUST cap held-out at 1,000 items and record split sizes, seed and the dataset revision in a dataset manifest.
- **FR-003**: Dataset preparation scripts MUST fetch Banking77 and the synthetic support tickets at pinned revisions, verify them, keep English ticket rows only, exclude vehicle and transport rows, and never commit the ticket rows themselves.

**Frontier labelling**

- **FR-004**: The label stage MUST export pending items as batch files with a fixed, versioned per-item prompt, and ingest answers from an on-disk cache keyed by (dataset revision, item id, prompt version).
- **FR-005**: It MUST never export an item whose answer is cached, never exceed 1,000 held-out items per dataset, and validate every ingested answer against the allowed options.
- **FR-006**: It MUST record provenance for every answer: model name, date, prompt version, and the fact that it was produced in an interactive session rather than an API call.
- **FR-007**: It MUST support a human override of any label, recorded with its own provenance, and report how many labels were overridden.
- **FR-008**: It MUST report label noise (frontier against gold) and an agreement rate between two prompt wordings on a 200-item subset per dataset.

**Measure, calibrate, threshold, cascade**

- **FR-009**: The measure stage MUST send every item through any `/v1/systemone` endpoint with raw (uncalibrated) outputs requested, store the full probability distribution per item, and compute accuracy, expected calibration error on max(p) (15 bins), Brier score and log loss per question type. Reports MUST say that ECE is on max(p) and why.
- **FR-010**: The calibrate stage MUST fit temperature scaling and isotonic regression per question type (and option-count bucket where supported) on the calibration set only, write calibrator files the Runtime loads, and re-measure the held-out set with them applied.
- **FR-011**: The threshold stage MUST, for a target error rate, choose τ on the calibration set and report the held-out accept rate and accepted-item accuracy at that τ, plus the full trade-off curve.
- **FR-012**: The cascade stage MUST combine local answers above τ with frontier answers below it, and report share kept local, share escalated, blended accuracy, frontier-only accuracy, local-only accuracy, and cost per 1,000 and per million decisions for the cascade and for frontier-only.
- **FR-013**: Cost MUST use published API list prices for the frontier model (headline: Claude Opus 5.5 standard; extra rows: Batch API, and price-only Sonnet 5 / Haiku 4.5 what-ifs), with the price basis, currency conversion and date stated, and every £ figure labelled an estimate. Local inference cost MUST be stated separately and its assumptions shown.

**Fine-tuning and baselines**

- **FR-014**: The fine-tune sidecar MUST fine-tune Laya on the training split on the reference GPU and export the result as a Runtime model package through the R1 export path, and the R1 parity tolerance MUST hold for it.
- **FR-015**: A classic small encoder baseline MUST be fine-tuned on the same training split, and its held-out accuracy (and calibration, where it gives probabilities) MUST be reported alongside Tau's models.

**Reports and orchestration**

- **FR-016**: The report stage MUST write one self-contained HTML file per run with reliability diagrams before and after, the trade-off curve, the cost table, confusion matrices (or a top-confusions table where there are too many classes to draw), label noise, and a metadata block: hardware, model hashes, dataset revision, split sizes, prompt version, date, command.
- **FR-017**: `tau run` MUST run every stage for a decision spec, skip stages whose artefacts are current, and regenerate the report.
- **FR-018**: Two worked examples (Banking77, synthetic support tickets) MUST be committed with their decision specs, dataset manifests (not the ticket rows), calibrators, cached frontier answers and reports, each reproducible by one command.
- **FR-019**: The Workbench MUST use the shared calibration library for every calibration computation and MUST NOT reimplement any of it.
- **FR-020**: The R1 CPU latency benchmark MUST be re-run on a quiet machine and its committed reports replaced.

### Key Entities

- **Decision spec**: the question, its type and options, the data source and fields, the endpoint or model to measure.
- **Dataset manifest**: source, pinned revision, filters applied (and rows excluded by each), split seed and sizes.
- **Frontier cache**: one record per (dataset revision, item id, prompt version) with the answer and its provenance.
- **Measurement**: per item, the endpoint's full answer and probability distribution. Per run, the metrics and the endpoint's identity.
- **Calibrator**: the R1 calibrator file format, fitted here.
- **Threshold result**: target, τ, accept rate, accepted accuracy, curve.
- **Cascade result**: shares, accuracies, latency mix, costs, price basis.
- **Run report**: the HTML file plus a JSON twin holding every number.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Both worked examples run end to end, and each has a committed report reproducible by one command.
- **SC-002**: On both datasets, held-out calibration error after calibration is materially lower than before: at least a 50% relative reduction for the out-of-the-box model, or the report explains why not.
- **SC-003**: Each cascade report states share kept local, blended accuracy against frontier-only, and cost per million decisions against frontier-only, with the price basis stated.
- **SC-004**: No frontier item is requested twice, no dataset exceeds 1,000 frontier-answered held-out items, and zero paid API calls are made.
- **SC-005**: Every figure in every report can be traced to the command, hardware, model hashes, dataset revision and date in that report.
- **SC-006**: The misses are in the reports: label noise, tasks where calibration helped least, and every case where the classic baseline beats Tau.
- **SC-007**: All automated tests pass, including a test that one calibrator gives the same probabilities in the Workbench and in the Runtime.

## Assumptions

- **Frontier model**: answers come from the Claude model running this Claude Code session (Claude Opus 5.5), prompted per item with a fixed template, in batches. It isn't an API call, and reports say so. The cost figures price what the same work would cost through the API at list price.
- **Gold labels**: Banking77 intents and the ticket dataset's `priority` field are the gold labels. Frontier labels are measured against them (label noise) and used as the cascade's escalation answers.
- **Ticket urgency** is a score question over the dataset's five priority levels (very_low → critical). Ticket data is synthetic, and every report says "synthetic support tickets".
- **Splits**: Banking77 held-out is drawn from its official test split, and calibration from its train split. Ticket splits are drawn from the filtered English rows. All use seed 42.
- **Models measured**: laya-en, von-1.2.0 and a fine-tuned Laya on both datasets. laya-typed-decisions is also measured on the tickets, because it was trained on a customer-service urgency workflow.
- **Classic baseline**: a small open sentence encoder of about 22M parameters, fine-tuned for classification, trained locally.
- **Local inference cost**: estimated from measured latency and the reference GPU's power draw at a stated electricity price, and labelled an estimate.
- R1's contract, Runtime, calibrator format and parity tooling are reused unchanged unless a defect is found. Any change is recorded.
