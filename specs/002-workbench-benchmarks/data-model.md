# Data Model: Tau R2

## decision.yaml (per example)

```yaml
name: banking77
question:
  key: intent
  type: choice              # choice | score | noul
  instructions: "Which banking intent does this customer message express?"
  options:                  # choice: key → description ; score: ordered level list ; noul: true/false descriptions
    activate_my_card: "..."
data:
  dataset: banking77        # prepared by the sidecar into data/<dataset>/{calibration,heldout,finetune}.jsonl
  text_field: text
  label_field: label
endpoint: http://localhost:18093
models: [laya-en, von-1.2.0, laya-en-ft-banking77]
baselines: [minilm-l6-banking77]           # probability files from the sidecar
threshold:
  target_error: 0.05
frontier:
  model: claude-opus-5-5
  prompt_version: v1
  alt_prompt_version: v1-alt
  alt_subset: 200
pricing:
  basis_date: 2026-09-27
  usd_per_mtok: { claude-opus-5-5: [4, 20], claude-opus-5-5-batch: [2, 10], claude-sonnet-5: [2, 10], claude-haiku-4-5: [1, 5] }
  gbp_per_usd: 0.0            # filled from the ECB reference rate on basis_date (research R-06)
  electricity_gbp_per_kwh: 0.0
  tokenizer_factor: 1.3
```

## Prepared data (`data/<dataset>/<split>.jsonl`, gitignored)

`{ "id": "<stable id>", "text": "...", "label": "<gold option key or level index>" }`

## dataset.manifest.json (committed)

Source, pinned revision, licence, `synthetic: true|false`, filters with rows removed by each, split seed,
split sizes, per-class counts per split, and the sha256 of each prepared split file.

## Frontier batch / cache

- Batch line: `{ "key": "<dataset-rev>:<item-id>:<prompt-version>", "item_id", "prompt_version", "prompt", "allowed": [...] }`
- Cache line: `{ "key", "item_id", "prompt_version", "answer", "model": "claude-opus-5-5",
  "produced_by": "Claude Code session (subagent), not the API", "date",
  "input_chars", "output_chars" }`
- Override line (`frontier/overrides.jsonl`): `{ "item_id", "answer", "by", "date", "reason" }`

## Measurement (`runs/<model>/<phase>.jsonl`, phase = raw | calibrated)

Per item: `{ "item_id", "gold", "answer", "probabilities": {..}, "confidence_maxp", "latency_ms",
"model_ms", "truncated" }`. Per run (`runs/<model>/<phase>.summary.json`): accuracy, ECE (max p), Brier,
log loss, MAE (score), n, failures, endpoint identity (`/v1/models` hash), GPU power mean.

## Calibrators (`calibrators/*.calibrator.json`)

R1 format v1. `fitted.dataset` = example name, `datasetRevision` = manifest hash. Input = log reference
probabilities (research R-01).

## Threshold / cascade results (inside report.json)

Threshold: target, τ, calibration-set accept rate, held-out accept rate, held-out accepted accuracy, curve
points. Cascade: shares, accuracies (local-only, frontier-only, blended), local latency p50, costs per
1,000 / per million by price row, and the price basis.

## report.json / report.html

Every number in the HTML comes from `report.json`. The metadata block covers hardware, the Runtime's
`/v1/models`, model hashes, dataset manifest hash, prompt versions, cache counts, date, command and git commit.
