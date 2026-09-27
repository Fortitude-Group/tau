# Interfaces in R2

| Interface | Definition |
| --- | --- |
| `tau` CLI | `label`, `measure`, `calibrate`, `threshold`, `cascade`, `report`, `run`; each takes a `decision.yaml`; exit 0 success, 2 blocked (e.g. frontier answers pending), 1 error |
| `decision.yaml` | [data-model.md](../data-model.md) |
| Frontier batch/cache/override files | [data-model.md](../data-model.md) |
| Calibrator v1 (clarified) | R1 schema unchanged; input = log reference probabilities (research R-01) |
| `x-tau-raw` | unchanged from R1 |
| `report.json` | one object per run: metadata, per-model raw/calibrated metrics, threshold, cascade, baselines, label noise |
