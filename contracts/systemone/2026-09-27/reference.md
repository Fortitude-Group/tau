# `/v1/systemone` contract snapshot — 2026-09-27

A human-readable copy of TypeSafe's published API reference (https://docs.typesafe.ai/api), taken on
2026-09-27. The reference has no version identifier, so this dated snapshot is the version Tau
pins. `request.schema.json` and `response.schema.json` beside this file are the machine-checkable
form. If the published reference changes, take a new dated snapshot and diff it against this one.
Don't edit this one.

## Endpoint

`POST /v1/systemone`, `Content-Type: application/json`, `Authorization: Bearer <key>` (the hosted
service requires it; Tau accepts and ignores it).

## Request

| Field | Type | Notes |
| --- | --- | --- |
| `model` | string | e.g. `"jev-latest"` |
| `state` | string \| object \| array | "The content to evaluate. A plain string for text, or structured data." Tau also accepts `null`. |
| `questions` | map<string, Question> | Keys are chosen by the caller and returned as answer keys. |

### Question types

- **noul**: `type: "noul"`, `instructions` (string \| object \| array), optional `criteria` { `true`: description, `false`: description }.
- **choice**: `type: "choice"`, `instructions`, `criteria`: map<string, string \| object \| array \| null>, "maximum of 255 options".
- **score**: `type: "score"`, `instructions`, `criteria`: array<string \| object \| array>, "at least two levels; the API accepts up to 10".

## Response

| Field | Type | Notes |
| --- | --- | --- |
| `model` | string | e.g. `"jev-1.13.0"` |
| `answers` | map<string, Answer> | One per question, keyed by question id. |
| `usage` | { `input_tokens`: int, `output_tokens`: int } | |

### Answer types

- **noul**: `type: "noul"`, `noul`: number, "scale from 0 (no) to 1 (yes)".
- **choice**: `type: "choice"`, `choice`: string (highest-probability option), `probabilities`: map<string, number> "floats that sum to 1", `confidence`: number "derived from probabilities".
- **score**: `type: "score"`, `score`: number "probability-weighted answer across the levels; can land between levels", `legend`: map<string, string> (level index → description), `probabilities`: map<string, number>, `confidence`: number.

## Errors

| Status | Meaning |
| --- | --- |
| 401 | Missing or invalid API key (hosted only; Tau never returns it) |
| 422 | Request validation failed |
| 429 | Rate limit exceeded (hosted only) |
| 529 | Service overloaded (hosted only) |

The published reference gives no error body shape. Tau's 422 body is documented in
`specs/001-runtime-onnx-parity/data-model.md` (ValidationProblem).
