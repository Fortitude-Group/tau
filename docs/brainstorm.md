# Tau — System One Runtime & Calibration Workbench

Sep 26, 2026 · @Rob Hill

## Summary

Tau makes open System One models trustworthy and easy to run. It has two open-source parts: a .NET runtime that serves Laya and Von behind TypeSafe's `/v1/systemone` contract, and a workbench that measures, fixes and proves their calibration, then prices a Claude-backed cascade in £.

- **Tau Runtime** — a local, drop-in replacement for the Jev endpoint. C#, ONNX Runtime, shared-state batching. Anything that already speaks the contract (the official SDK, LiteLLM, Kev clients) works unchanged.
- **Tau Workbench** — points at any `/v1/systemone` endpoint (Jev, Kev, Tau Runtime). Draws reliability diagrams, refits calibration per question type, picks the confidence threshold τ for a target error rate, and simulates the accept-when-confident, escalate-when-unsure cascade with real costs.

Why now: Jev shipped on 15 Sep 2026 and about 1,865 repos appeared in its first week. Most run a model. Almost none make its confidence scores mean anything. Laya's out-of-the-box Expected Calibration Error (ECE) is 0.466, which makes it unsafe to gate on.

"Tau" is a working name, after τ, the cascade threshold.

## Evidence base

The gap is calibration, not speed: open models are already fast, but their confidence scores can't yet be trusted to gate decisions. Figures below are from the ArXivIQ review (26 Sep 2026) and its sources. Every one gets re-measured before it goes in an article.

| Claim | Figure | Why it matters to Tau |
| --- | --- | --- |
| Laya out-of-box calibration | ECE 0.466 | Confidence is near-meaningless until refitted. The Workbench's core job. |
| Laya zero-shot accuracy | 0.362 vs 0.318 random | Needs fine-tuning or labelling. Workbench's distil stage. |
| Von zero-shot (49-task benchmark) | 0.704 vs Laya 0.583 | Von is the better default for zero-shot demos. Runtime must serve both. |
| Laya latency, T4 GPU | 32.8–39.5 ms; 7.2 ms per question batched on one state | Shared-state batching is the Runtime's headline trick. |
| Jev latency, hosted | p50 380 ms (other sources: 236–276 ms) | Local Runtime should beat hosted by an order of magnitude. Measure it. |
| Cascade on RewardBench, τ = 0.9 | 53.7% accepted; 92.5% vs 93.1% accuracy; \~40% cost cut | The £ story the Workbench reproduces with open models. |
| Fine-tuned 22M encoder, Banking77 | 93.2% vs Jev 80.1% | Honest framing: static tasks favour classic fine-tuning. Say so. |
| Kev wire compatibility | Full `/v1/systemone` contract | The contract is the de facto standard. Implement it, don't invent one. |

One claim to rebut, not repeat: "mathematically immune to prompt injection". The format can't break; the decision can still be steered by attacker-controlled state. Out of scope here, but the Workbench's test harness should make that attack easy to run later.

## Component A: Tau Runtime

A self-hosted C# server that answers `POST /v1/systemone` exactly as TypeSafe does, running Laya or Von locally through ONNX Runtime. Swap the base URL; nothing else changes.

**Core capabilities**

- Wire-compatible `/v1/systemone`: state (text, arrays, nested JSON, null) plus a map of named questions. Returns a typed map of answers with full probability distributions.
- All three primitives: **Choice** (pick one, full distribution), **Score** (ordered levels, interpolated float), **Noul** (yes/no probability).
- Shared-state batching: encode the state once, answer every question against it. Question independence preserved — no question sees another.
- Model routing: Laya English, Laya multilingual (mmBERT), Von. Script detection picks the checkpoint unless the request pins one.
- Calibration hook: loads per-question-type calibrators exported by the Workbench and applies them to every response. Raw scores stay available behind a flag.
- Execution providers: CPU, CUDA, DirectML. Same binary, config-selected.
- Observability: per-request latency, tokens, batch size, model and calibrator versions. OpenTelemetry out, Prometheus endpoint.

**Deliverables**

- `Tau.Runtime` server (ASP.NET Core, single-file publish, Docker image).
- `Tau.Client` NuGet package with a typed C# API, e.g. `Decide<Priority>(state)` returning the answer plus probabilities.
- Model export scripts: PyTorch checkpoint to ONNX, with a parity test proving outputs match the reference implementation within tolerance.
- Conformance suite: the same request set run against Jev (if a key is available), Kev and Tau, diffed.

**Stretch (R3)**

- WebGPU build running the same ONNX model in the browser. Demo page: "Jev-class decisions, zero server".
- NumericJev-style interval decoding as a fourth primitive for bounded numeric extraction.

## Component B: Tau Workbench

A CLI plus a self-contained HTML report that answers one question: "can I trust this model's confidence enough to gate on it, and what does that save me?" It works against any `/v1/systemone` endpoint.

**Pipeline** (each stage runnable on its own, artefacts on disk between stages)

1. **Define** — a decision spec in YAML: the question, its type (Choice / Score / Noul), options, and where the states come from (CSV, JSONL, folder of files).
2. **Label** — a frontier model (Claude by default) labels states, with an agreement check across two prompts or two models. Humans can override any label. Output: a gold set with provenance.
3. **Fine-tune** (optional) — Python sidecar fine-tunes Laya or Von on the gold set, exports to ONNX for the Runtime. The only non-.NET stage.
4. **Measure** — run the held-out set through the endpoint. Accuracy, ECE, Brier score, log loss, per question type.
5. **Calibrate** — fit isotonic regression (and temperature scaling as a baseline) per question type. Export calibrators the Runtime loads. Re-measure.
6. **Threshold** — given a target error rate, pick τ. Show the accept-rate / accuracy trade-off curve.
7. **Simulate cascade** — accept above τ, escalate the rest to the frontier model. Report blended accuracy, share escalated, latency, and £ per 1,000 and per million decisions against frontier-only.
8. **Report** — one HTML file: reliability diagrams before and after, trade-off curve, cost table, confusion matrices, run metadata. Built to be screenshotted into articles.

**Headline demo** — a public dataset (e.g. Banking77 or a support-ticket set) run end to end: raw Laya (badly calibrated) → calibrated Laya → cascade with Claude → "X% of decisions stay local, accuracy within Y points of Claude-only, £Z saved per million". Numbers measured, never claimed.

**Deliverables**

- `tau` CLI (.NET global tool): `tau label`, `tau measure`, `tau calibrate`, `tau threshold`, `tau cascade`, `tau report`, and `tau run` for the lot.
- Calibrator export format (versioned JSON) shared with the Runtime.
- Python fine-tune sidecar with a pinned environment.
- Two worked examples in the repo with committed reports.

## Architecture and repo layout

&#91;embedded content: Tau architecture · 2 open-source components, 4 neighbours\]

The `/v1/systemone` contract is the only interface between parts, so the Workbench can measure Tau, Jev or Kev with the same code. Calibration maths lives in one shared library, so the Workbench and the Runtime can never disagree about what a calibrated score is.

One monorepo under the Fortitude GitHub organisation, .NET 10, Apache 2.0 to match Laya:

```
tau/
  src/Tau.Contract/       request/response types for /v1/systemone
  src/Tau.Calibration/    isotonic, temperature, ECE/Brier/log loss (shared)
  src/Tau.Runtime/        ASP.NET Core server, ONNX Runtime
  src/Tau.Client/         typed NuGet client
  src/Tau.Workbench/      CLI global tool + HTML report
  sidecar/finetune/       Python fine-tune + ONNX export + parity test
  tests/conformance/      contract diff: Tau vs Kev vs Jev
  examples/               two worked datasets with committed reports
  docs/articles/          article drafts
```

## Release plan

&#91;embedded content: Release plan · 3 releases, 3 gates\]

No release starts until the previous gate is green. Stretch items (WebGPU build, numeric interval decoding, the prompt-injection red-team harness) come after the finish line, not before it.

## Non-goals

Tau wraps and proves existing open models; it doesn't compete with them.

- No new base model and no pretraining. Fine-tuning existing checkpoints only.
- No reimplementation of RLCD training. Calibration is fixed after training, not during it.
- No hosted service, accounts or billing. Self-hosted only; this is an R&D showpiece, not a product.
- No GUI app. CLI plus static HTML report; the report is the visual.
- No extensions to the `/v1/systemone` contract in R1–R3. Anything extra goes behind an `x-tau-` prefix or waits.
- No fleet or vehicle datasets or examples, anywhere.

## Finish line and success criteria

Done means both components work end to end on two public datasets, the numbers are measured and reproducible, and four articles sit ready for Rob to publish.

- [x] Runtime answers the full `/v1/systemone` contract; conformance suite passes against Kev (and Jev if a key is available). *Evidence: [reports/r1/conformance.md](../reports/r1/conformance.md), Tau 45 of 45. No Jev key at first, by decision. A key was bought on 28 September and Jev was measured head to head in both worked examples (6,000 calls, about $0.32 estimated), but the 45-request conformance suite hasn't been run against it yet. See [FINISH.md](FINISH.md).*
- [x] ONNX outputs match the PyTorch reference within an agreed tolerance on every test case. *Evidence: [reports/r1/parity.md](../reports/r1/parity.md) and both `examples/*/finetune-parity.json`.*
- [x] Runtime latency measured on Rob's hardware for single and batched questions, published with hardware specs. *Evidence: [reports/r1/latency.md](../reports/r1/latency.md), re-run on a quiet machine in R2.*
- [x] Workbench shows ECE before and after calibration on both datasets; the after figure is materially lower. *Evidence: both `examples/*/report.html`. One explained miss (laya-en on tickets, 46%).*
- [x] Cascade simulation reports share kept local, blended accuracy vs Claude-only, and £ per million decisions. *Evidence: the cascade and cost tables in both reports.*
- [x] Every number in every article traces to a committed report file. One command reproduces each report. *Evidence: `scripts/check-articles.ps1` (0 problems over every draft) and `scripts/examples.ps1 -Example <name>`.*
- [x] Repo, NuGet package and Docker image ready to go public; README gets a newcomer to a first decision in under 10 minutes. *Evidence: README walkthrough 117 s excluding downloads, packages and image built and tested locally, secret scan clean. Caveats in [FINISH.md](FINISH.md).*
- [x] Articles drafted for LinkedIn, DEV.to, Reddit and HN (Hacker News), each fitted to its channel. Rob approves before anything publishes. *Evidence: `docs/articles/`. Drafted, and awaiting Rob's approval.*

## Risks and mitigations

The biggest technical risk is the ONNX export; the biggest credibility risk is a number that doesn't reproduce.

| Risk | Mitigation |
| --- | --- |
| ModernBERT or Laya's custom decision head won't export cleanly to ONNX | Spike it first in R1. Export with eager attention; parity test gates everything. Fallback: a thin Python inference process behind the Runtime until export works. |
| Per-type isotonic calibration needs more labels than expected | Report ECE against label count. Temperature scaling as the low-data fallback. |
| Claude labels are wrong often enough to poison the gold set | Two-prompt agreement check, human spot-check sample, label noise reported in every report. |
| Contract or checkpoints change mid-build (the space is weeks old) | Pin contract version and model hashes. Conformance suite flags drift. |
| Licence or terms problems: Von's licence, dataset licences, TypeSafe's terms on publishing benchmarks | Check before R1 closes. Drop anything that can't be published. |
| Benchmarks look cherry-picked | Publish failures too, including where fine-tuned classic encoders win. Honest framing is the brand. |

## Open questions for Rob

These are the decisions `/speckit.clarify` should put to you; everything else can default.

- [x] Name: keep "Tau", or something else? *Answered: Tau.*
- [x] Which hardware is the benchmark reference? GPU model matters for every latency figure. *Answered: Rob's RTX 3080 Ti (12 GB) with an i9-11900K.*
- [x] Do you have, or want to buy, a Jev API key for the conformance and head-to-head runs? *Answered: not at first. A key with $5 of credit arrived on 28 September and the head-to-head ran (conformance against Jev is still open).*
- [x] Which two datasets? Default proposal: Banking77 (intent, 77 classes) plus one open support-ticket set with urgency scoring. *Answered: Banking77 and Tobi-Bueck/customer-support-tickets, which turned out to be synthetic. It's kept, labelled as such, and scored against Claude's answers.*
- [x] Claude spend cap for labelling and cascade runs? *Answered: no API spend at all. Frontier answers came from the Claude Code session, with a usage estimate before each batch.*
- [x] Fine-tuning compute: local GPU, Kaggle, or a rented instance? *Answered: the local GPU.*
- [x] Workbench in C# as proposed, or Python for reach with the ML crowd? (C# is less crowded; Python gets more stars.) *Answered: C# on .NET 10, with a Python sidecar only for fine-tuning and export.*
- [x] Repo home: the existing Fortitude-Group organisation, or a separate R&D organisation? *Answered: Fortitude-Group, now public.*
- [x] Articles: under your name, the Fortitude Omnis brand, or both? *Answered: both, Rob Hill and Fortitude Omnis.*

## Article plan

One long-form canonical piece on the Fortitude R&D page, then four channel-fitted versions. Every number links to a committed report. The named failure shape throughout: **a confidence score that looks certain and means nothing**.

| Channel | Angle | Form | Playbook tactic |
| --- | --- | --- | --- |
| HN (Hacker News) | "Laya's confidence scores are lying to you. We measured it and fixed it." | Show HN post linking the repo, plus the canonical write-up. Rob answers comments. No marketing tone. | Measured, not claimed; confession (where classic fine-tuned encoders beat us) |
| DEV.to | "From ECE 0.466 to X: calibrating an open System One model in C#" | Hands-on tutorial with code, reliability diagrams, one command to reproduce. Canonical URL points at the R&D page. | Docs are marketing; answer-shaped headings |
| Reddit | r/LocalLLaMA: local Laya/Von latency and calibration numbers. r/dotnet: the Runtime. r/MachineLearning only as a \[P\] post with method first. | Findings first, repo link last. Obey each subreddit's self-promotion rules. | Measured, not claimed; community proof |
| LinkedIn | "How many AI decisions actually need a frontier model?" — the cascade £ story. | Short post, one chart, link out. Credibility, not lead generation; LinkedIn hasn't converted for OSPulse. | Underdog: one engineer, N agents |

Rules for every piece:

- Caveat our own headline number unprompted (hardware, dataset, date measured).
- Publish the misses: label noise rate, tasks where calibration barely helped, where fine-tuned classic encoders win.
- LLM-targeted questions to answer in docs and headings: "is Laya calibrated", "how do I calibrate Laya", "self-hosted /v1/systemone server", "open-source Jev alternative for .NET", "when do I need a frontier model instead of a decision model".
- Nothing personal beyond the byline. If the byline is personal, post LinkedIn from the Fortitude Omnis page unless Rob decides otherwise.
