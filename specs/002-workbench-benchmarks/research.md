# Research: Tau R2 — Workbench and benchmarks

Phase 0 output. Facts were checked against primary sources on 2026-09-27 (dataset server, dataset cards, the
Laya GitHub repository, Anthropic's pricing page). Decisions follow each finding.

## R-01 · What a calibrator acts on (changes R1's Runtime hook)

- **Finding:** the contract returns probabilities, not logits. An endpoint-agnostic Workbench can only fit on
  the probabilities an endpoint reports. R1's Runtime applies calibrators to raw model logits, *before* the
  reference temperature. The two would disagree, which Principle XV forbids.
- **Decision:** a v1 calibrator's input is **the log of the model's reference probabilities** (what
  `x-tau-raw: true` returns). Temperature: `p' = softmax(log(p_ref) / T)`. Isotonic: as R1 (map each p_ref,
  renormalise). The Runtime computes p_ref exactly as for a raw answer, then applies the calibrator, so the
  Workbench and Runtime compute the same function. Noul uses `[1 − p, p]`. Probabilities are clamped to
  1e-6 before the log. The Workbench fits on the 4-dp-rounded probabilities an endpoint returns, and the
  Runtime applies to unrounded ones. The difference is at most about 5e-5 in p, and a test pins it
  (SC-007).
- **Alternative rejected:** a Tau-only logits extension. That would break the "any endpoint" promise
  and add a contract extension (Principle XIV).

## R-02 · Datasets (verified)

- **Banking77:** CC-BY-4.0. The data lives at `PolyAI-LDN/task-specific-datasets` @ `9d081458…`
  (`banking_data/train.csv` 10,003 rows, `test.csv` 3,080 rows, fields `text`, `category`). 77 intents.
- **Tickets:** `Tobi-Bueck/customer-support-tickets` @ `ddf1c81a…`, CC-BY-NC-4.0, **synthetic**. 61,765 rows,
  28,261 English. `priority` is five levels (very_low, low, medium, high, critical, heavily imbalanced:
  critical 3%). Filters: English only. Drop rows whose `queue` starts `Autos & Vehicles` or
  `Travel & Transportation`. Drop rows whose subject or body matches a vehicle/fleet keyword list. Drop
  exact-duplicate texts. Rows are never committed, only the manifest (counts per filter) and item ids.
- **Splits (seed 42, stratified):** Banking77 calibration = 1,000 from train, held-out = 1,000 from test,
  fine-tune = the rest of train. Tickets: held-out 1,000, calibration 1,000, fine-tune up to 8,000 from the
  rest (capped so fine-tuning stays under about 2 h on the 3080 Ti). The cap is recorded.

## R-03 · Fine-tuning recipe: adapt Laya's own

- **Finding:** `laya` 0.3.20 ships inference only. The authors' fine-tune is
  `notebooks/laya_finetune_typed_decisions_2xT4_kaggle.ipynb` (`train_ddp.py`) in `NandhaKishorM/laya` @
  `4066d5d5…` (Apache-2.0). It uses a policy gradient on Laya's strictly proper reward (log + spherical +
  ranked-probability score) plus soft cross-entropy, AdamW with separate encoder/head learning rates,
  cosine schedule, fp16 autocast, gradient checkpointing, a held-back calibration slice, and post-hoc
  per-type temperature.
- **Decision:** `sidecar/finetune/tau_sidecar/finetune_laya.py` adapts that script to one GPU (DDP removed,
  otherwise the same objective and settings), with attribution. The output uses Laya's checkpoint layout
  in `models/src/<id>/`, so R1's export and parity run unchanged. Model ids: `laya-en-ft-banking77`,
  `laya-en-ft-tickets`. Packages record `source.repo = "local-finetune"` and the training run's config
  hash, not an upstream commit.

## R-04 · Classic baseline

- **Decision:** `sentence-transformers/all-MiniLM-L6-v2` (22.7M params, Apache-2.0), fine-tuned with a linear
  classification head (`AutoModelForSequenceClassification`) on the same fine-tune split. It writes held-out
  probabilities to JSONL. The Workbench computes its metrics with the same C# code as every other model
  (FR-015). This tests the brainstorm's cited "22M encoder: 93.2% on Banking77" on our hardware. That
  figure is third-party.

## R-05 · Frontier answers without an API

- **Decision:** `tau label` writes `frontier/pending/<prompt-version>/batch-NNN.jsonl` (items plus a fixed
  prompt template and allowed answers). This session answers them through Agent-tool subagents (the
  session's own model, Claude Opus 5.5), about 200 items per batch to keep per-agent overhead low. The
  subagents write `frontier/cache.jsonl`. `tau label` ingests and validates, and never re-exports cached
  keys. Prompts: `v1` (primary, all held-out) and `v1-alt` (a different wording, a 200-item subset). The
  cache is committed. Provenance on every record: model id, date, prompt version, "Claude Code session
  (subagent), not the API". Token cost for the £ estimate: input and output characters over 4 (Anthropic's
  published rule of thumb), times 1.3 for the Claude 4.7+ tokenizer (pricing page: "approximately 30% more
  tokens"). Labelled an estimate.

## R-06 · Prices, currency and local cost

- **Frontier prices** (platform.claude.com/docs/en/about-claude/pricing, 2026-09-27, USD per million tokens):
  Opus 5.5 $4 in / $20 out (Batch $2/$10). Sonnet 5 $2/$10. Haiku 4.5 $1/$5.
- **USD→GBP:** the ECB euro reference rates (USD and GBP per EUR) on a stated date, from ECB's public data
  API. The rate and date are stored in the example's spec and in the report.
- **Local cost:** GPU power (mean `nvidia-smi power.draw` during the measure run) × measured seconds per
  decision → kWh, × a UK electricity unit price (Ofgem price cap, date stated). Hardware amortisation is
  excluded and says so. Labelled an estimate.
- **Frontier latency:** not measured (no API calls). The cascade reports local latency and escalation share,
  and says frontier latency is unmeasured.

## R-07 · Metrics definitions

- ECE on max(p), 15 bins, first bin inclusive (`Tau.Calibration.Metrics`, identical to `laya.common.ece_score`).
  "Correct": argmax equals gold. For score questions, the exact level.
- Brier: multi-class. Log loss: −ln p(gold), clamped at 1e-12.
- The threshold is chosen on the calibration set's calibrated confidences (max p). The accept rate and accepted
  accuracy are reported on held-out, so τ is never tuned on the numbers it's judged by.
- Score questions also report mean absolute error in levels, because ordinal misses are not all equal.

## R-08 · Workbench shape

- `src/Tau.Workbench` is a library with the stages and the HTML report. `src/Tau.Workbench.Cli` is the
  `tau` global tool (`PackAsTool`, `ToolCommandName=tau`), a thin shell (Principle I). Tests in
  `tests/Tau.Workbench.Tests`.
- YAML via YamlDotNet (MIT). HTTP via `Tau.Client`, extended with per-request headers so it can send
  `x-tau-raw`.
- Report: C# renders inline SVG (reliability diagrams, trade-off curve) and HTML tables into one file with
  inline CSS and no scripts. It opens offline and screenshots cleanly. The `dataviz` skill is loaded before
  the chart code is written.
- Artefacts per example: `examples/<name>/{decision.yaml, dataset.manifest.json, frontier/, runs/<model>/
  measure.jsonl, calibrators/, report.html, report.json}`.

## R-09 · Runtime for measurement

- Measurements go through a Tau Runtime on CUDA with the fine-tuned packages installed (`Tau:Models` lists
  what each example needs). `x-tau-raw: true` gives reference probabilities. Calibrated re-measurement
  restarts the Runtime with `Tau:CalibratorsDirectory` set to the example's calibrators. That exercises the
  real hook end to end.
