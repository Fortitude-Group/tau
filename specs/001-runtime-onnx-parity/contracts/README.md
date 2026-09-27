# Interfaces exposed in R1

| Interface | Definition | Consumers |
| --- | --- | --- |
| `POST /v1/systemone` | [`contracts/systemone/2026-09-27/`](../../../contracts/systemone/2026-09-27/) (request + strict response schema, human-readable reference) | any contract client, Tau.Client, conformance tool |
| 422 error body | `ValidationProblem` in [data-model.md](../data-model.md) | clients, Tau.Client typed error |
| `GET /v1/models` (Tau-only, read-only discovery) | `{ "models": [ { "id", "family", "revision", "onnx_sha256", "loaded" } ], "aliases": { ... } }` | operators, conformance tool, bench |
| `GET /metrics` | Prometheus text exposition | scrapers |
| `GET /healthz` | 200 once the configured models are loaded, 503 before | Docker healthcheck |
| `x-tau-raw` request header (`true`) | skip Tau calibrators; return reference post-processing | Workbench (R2) |
| `x-tau-*` response headers | `x-tau-model-hash`, `x-tau-calibrators`, `x-tau-truncated`, `x-tau-route-reason`, `x-tau-model-ms` | diagnostics, bench |
| Calibrator file | [calibrator.schema.json](calibrator.schema.json) | Runtime (load), Workbench R2 (write) |
| Model package manifest | [model-package.schema.json](model-package.schema.json) | sidecar export (write), Runtime (load) |

`/v1/models`, `/metrics` and `/healthz` are separate routes, so they don't extend the `/v1/systemone`
contract (Principle XIV).
