# Quickstart: validate R1 end to end

Run from the repo root in PowerShell on the reference machine (Windows 10/11, RTX 3080 Ti). Each step is
one command, and each report step writes a committed file under `reports/r1/`.

## Prerequisites

- .NET SDK 10.0.4xx, `uv` ≥ 0.11, Python 3.12 (uv will fetch it if missing), Docker Desktop (image step only).
- About 15 GB free disk for checkpoints, torch and the CUDA wheels.

## 1. Fetch and export (once)

```powershell
./scripts/fetch-models.ps1     # downloads the pinned revisions to models/, verifies sha256
./scripts/fetch-cuda.ps1       # free NVIDIA CUDA 12 + cuDNN 9 runtime wheels → native/cuda-deps/
./scripts/export.ps1           # sidecar: ONNX export for all four models + tau-model.json manifests
```

Expected: `models/<id>/model.onnx` and `tau-model.json` for `laya-en`, `laya-multilingual`,
`laya-typed-decisions` and `von-1.2.0`, with hashes that match the manifest.

## 2. Parity gates

```powershell
./scripts/parity.ps1           # sidecar model parity + fixture generation, then the C# parity tests
```

Expected: `reports/r1/parity.md` shows every model at 100% of cases within tolerance (|Δlogit| ≤ 2e-3,
|Δprob| ≤ 1e-3, identical argmax), and tokeniser, serialiser, routing and end-to-end parity at 100%.
The script exits non-zero on any failure.

## 3. Build and test everything

```powershell
./scripts/build-test.ps1       # dotnet build (0 errors) + dotnet test Tau.slnx + sidecar pytest
```

## 4. Run the Runtime and make a first decision

```powershell
dotnet run --project src/Tau.Runtime -- --Tau:Provider=cuda
```

```powershell
$body = @{ model = "jev-latest"; state = "I was charged twice for my order and I want a refund today."
  questions = @{
    queue   = @{ type = "choice"; instructions = "Which team owns this?"; criteria = @{ billing = "refunds, charges"; tech = "outages, bugs" } }
    urgency = @{ type = "score"; instructions = "How urgent?"; criteria = @("low", "medium", "high", "critical") }
    churn   = @{ type = "noul"; instructions = "Is the customer threatening to leave?" } } } | ConvertTo-Json -Depth 6
Invoke-RestMethod -Method Post -Uri http://localhost:8088/v1/systemone -Body $body -ContentType application/json
```

Expected: `model = "laya-en"`, the three typed answers in contract shape, and HTTP 422 if you change
`urgency.criteria` to one level.

## 5. Conformance against Kev

```powershell
./scripts/conformance.ps1      # starts Kev-0.8B (native → WSL → von serve fallback) and Tau, runs the request set
```

Expected: `reports/r1/conformance.md` has zero structural failures for Tau. Model disagreements are listed,
not failed.

## 6. Latency

```powershell
./scripts/bench.ps1            # CUDA and CPU, every model, 1/4/10 questions
```

Expected: `reports/r1/latency.md` with p50/p95/p99 per model × provider × question count, and the hardware,
hashes, precision and repeat-run variance stated at the top.

## 7. Container (local only, never pushed)

```powershell
docker build -f src/Tau.Runtime/Dockerfile -t tau-runtime:r1-local .
```
