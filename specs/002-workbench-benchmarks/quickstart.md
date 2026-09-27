# Quickstart: validate R2

Run on the reference machine after R1's `fetch-models`, `export`, `fetch-natives` and `fetch-cuda`.

```powershell
./scripts/examples.ps1 -Example banking77         # prepare data → fine-tune + export + parity → tau run
./scripts/examples.ps1 -Example support-tickets
```

Each writes `examples/<name>/report.html` and `report.json`. Committed frontier answers mean no new frontier
work is needed. If the cache is incomplete, `tau label` writes pending batches and stops, naming the files.

Stage by stage:

```powershell
tau label     examples/banking77/decision.yaml     # export pending frontier batches / ingest the cache
tau measure   examples/banking77/decision.yaml     # raw measurements per model (x-tau-raw)
tau calibrate examples/banking77/decision.yaml     # fit on calibration split, write calibrators, re-measure held-out
tau threshold examples/banking77/decision.yaml
tau cascade   examples/banking77/decision.yaml
tau report    examples/banking77/decision.yaml
tau run       examples/banking77/decision.yaml     # all of the above, skipping current artefacts
```

Expected: the report shows ECE before and after for each model (after ≤ half of before for the
out-of-the-box model, or an explanation), the τ trade-off curve, the cascade table with £ estimates and
their basis, label noise, the classic baseline, and a metadata block.
