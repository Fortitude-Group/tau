# Data Model: Tau R1

Entities in the order a request flows through them. Field names on the wire are the contract's
(`contracts/systemone/2026-09-27/`). C# names follow .NET casing and serialise to the wire names.

## DecisionRequest (wire: request body)

| Field | Type | Rules |
| --- | --- | --- |
| `model` | string | Required. Tau id (`laya-en`, `laya-multilingual`, `laya-typed-decisions`, `von-1.2.0`) or auto alias (`auto`, `tau-auto`, `jev-latest`, `jev-*`). Unknown non-alias → 422 listing installed ids. |
| `state` | any JSON (`JsonNode?`) | Required key. Text, object, array, number, bool or null. Object key order preserved. |
| `questions` | ordered map string → Question | Required, 1 to 50 entries. Keys are answer keys, returned exactly. Order preserved (it sets batch order, not results). |
| `x-tau-*` | any | Allowed and ignored unless recognised. Any other unknown top-level field → 422. |

## Question (discriminated by `type`)

| Type | `instructions` | `criteria` | Rules |
| --- | --- | --- | --- |
| `choice` | string, object or array | map option key → string, object, array or null | 1 to 255 entries. Option order = rendering order. |
| `score` | string, object or array | array of string, object or array | 2 to 10 levels, index 0 first. |
| `noul` | string, object or array | optional map with keys only `true` / `false` | Keys case-insensitive (normalised to lower case, as the reference does). |

Validation happens before any tokenisation. Every violation is collected (not just the first) into a
`ValidationProblem` list.

## ValidationProblem (wire: 422 body)

```json
{ "error": { "type": "validation_error", "message": "...",
             "details": [ { "path": "questions.urgency.criteria", "problem": "score needs 2-10 levels, got 1" } ] } }
```

## Answer (wire: `answers[<question key>]`)

| Type | Fields (exactly these) |
| --- | --- |
| choice | `type`, `choice` (argmax key), `probabilities` (key → float, sums to 1 ± 1e-3 after rounding), `confidence` |
| score | `type`, `score` (Σ i·pᵢ), `legend` ("0".."k-1" → level text as rendered), `probabilities` ("0".. → float), `confidence` |
| noul | `type`, `noul` (P(true)) |

Rounding and confidence follow each model's reference (see ModelPackage.postProcessing).

## DecisionResponse

`model` (resolved Tau id), `answers`, `usage` { `input_tokens` = tokens actually processed (sum of
attention masks over the request's rows, including Von's null-prior rows), `output_tokens` = 0 }.
Diagnostic response headers: `x-tau-model-hash`, `x-tau-calibrators`, `x-tau-truncated`,
`x-tau-route-reason`, `x-tau-model-ms`.

## ModelPackage (on disk: `models/<id>/tau-model.json` + files)

| Field | Meaning |
| --- | --- |
| `id` | Tau model id |
| `family` | `laya` \| `von` |
| `source` | { `repo`, `revision` (commit sha), `subfolder`, `files`: { name → sha256 } } |
| `onnx` | { `file`, `sha256`, `opset`, `exporter` (A/B), `precision` } |
| `tokenizer` | `tokenizer.json` path + sha256 |
| `limits` | Laya: `max_len`, `head_max_len`. Von: `max_position`, `sliding_window` |
| `postProcessing` | Laya: clamped `temperature[3]`, `temperature_by_options`, confidence = `entropy`. Von: `temperature`, `calibration_map`, `noul_prior`, confidence = `margin`, `digit_split`, `independent_options` |
| `reference` | { `package`: `laya==0.3.20` \| `von-sdk==1.2.3`, `transformers`, `torch` } |

The Runtime refuses a package whose recorded hashes don't match the files (FR-002).

## Calibrator (file format `tau.calibrator` v1, shared library)

| Field | Meaning |
| --- | --- |
| `format` / `version` | `"tau.calibrator"` / `1`. Any other value refuses to load. |
| `model`, `modelHash` | Model id and ONNX sha256 it was fitted on. A mismatch refuses to load. |
| `questionType` | `choice` \| `score` \| `noul` |
| `bucket` | optional option-count bucket (`2`, `3-5`, `6-10`, `11+`), same buckets as the reference |
| `method` | `temperature` (applied to raw logits, replacing the reference temperature) \| `isotonic` (applied to each option probability, then renormalised) |
| `temperature` | float > 0 (method temperature) |
| `isotonic` | { `x`: ascending floats in [0,1], `y`: non-decreasing floats in [0,1] }, piecewise-linear, clamped |
| `fitted` | provenance: `n`, `dataset`, `datasetRevision`, `date`, `eceBefore`, `eceAfter`, `tool`, `toolVersion` |

Selection per answer: most specific match (model + type + bucket), then model + type. With `x-tau-raw: true`
no calibrator is applied and answers are the reference post-processing.

## Report (committed: `reports/r1/*.md` + `*.json`)

Common header: `report`, `command`, `date` (UTC ISO), `git_commit`, `hardware` { gpu, vram, driver, cpu,
ram, os }, `software` { dotnet, ort + provider, python/torch/transformers where used }, `models`
[{ id, repo, revision, onnx_sha256 }], `contract` (`systemone/2026-09-27`), then report-specific bodies:
parity (per-model max deviations, case counts, failures), conformance (per-request validation plus diff
classification), latency (per model × provider × question count: p50/p95/p99/mean/sd, n, warm-up, precision,
repeat-run variance).

## ParityFixture (committed: `tests/fixtures/parity/<model>/*.jsonl`)

First line is a header (generator, versions, model hashes, date). Then one record per case:
`{ id, kind: tokens|sequence|logits|answer|route|serialise, input, expected }`.
