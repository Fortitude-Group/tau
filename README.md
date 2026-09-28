# Tau

Tau is a self-hosted server for the `/v1/systemone` decision API. It runs the open Laya and Von classification models on your own GPU or CPU through ONNX Runtime, and it ships with `tau`, a command-line Workbench that measures, calibrates and prices a local-first cascade that only sends the hard cases to a frontier model.

## How do I run a self-hosted /v1/systemone server?

This gets you from a fresh clone to one answered request on CPU. It serves one model, `laya-en`.

You need:

- Windows or Linux on x64
- the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) (`pwsh`), which runs the scripts
- `curl` and `git`
- about 3 GB of free disk

Run every command from the repository root, in `pwsh`.

**1. Clone the repository.**

```powershell
git clone https://github.com/Fortitude-Group/tau.git
cd tau
```

**2. Fetch the exported model.** This downloads the laya-en ONNX package (about 1.3 GB) from the repository's `models-v1` release, checks its SHA-256 against the value pinned in the script, and unpacks it to `models/laya-en/`. These are the exact files the committed reports were measured with.

```powershell
./scripts/fetch-onnx.ps1 -Only laya-en
```

**3. Fetch the ONNX Runtime native library** for your platform. Use `-Rid linux-x64` on Linux.

```powershell
./scripts/fetch-natives.ps1 -Flavour cpu -Rid win-x64
```

**4. Start the Runtime.** Leave it running in this terminal. It's ready when it prints `Tau engine ready: provider Cpu, models laya-en`.

```powershell
dotnet run --project src/Tau.Runtime -c Release -- --urls http://localhost:8088
```

**5. Ask it something.** In a second terminal, from the repository root, send the example request in [`examples/quickstart/request.json`](examples/quickstart/request.json). It asks one choice question and one yes/no question about a short support message.

```powershell
curl -s http://localhost:8088/v1/systemone -H "Content-Type: application/json" -d "@examples/quickstart/request.json"
```

You get back something like this (formatted here for reading):

```json
{
  "model": "laya-en",
  "answers": {
    "category": {
      "type": "choice",
      "choice": "billing",
      "probabilities": { "billing": 0.9501, "technical": 0.0265, "account": 0.0233 },
      "confidence": 0.7883
    },
    "urgent": { "type": "noul", "noul": 0.8481 }
  },
  "usage": { "input_tokens": 102, "output_tokens": 0 }
}
```

`jev-latest` in the request is an alias. It asks the Runtime to pick a model, and English text goes to `laya-en`. You can also name a model directly. `GET /v1/models` lists what's loaded, and `GET /healthz` says whether the server is up. Stop the Runtime with Ctrl+C.

### How do I run it on a GPU?

For an NVIDIA card, fetch the CUDA build of ONNX Runtime and the CUDA 12 and cuDNN 9 libraries it needs (about 2 GB, from NVIDIA's pip wheels, so no CUDA toolkit install). Then start the Runtime with the CUDA provider:

```powershell
./scripts/fetch-natives.ps1 -Flavour cuda -Rid win-x64
./scripts/fetch-cuda.ps1
dotnet run --project src/Tau.Runtime -c Release -- --urls http://localhost:8088 --Tau:Provider=cuda
```

On Windows, `-Flavour directml` and `--Tau:Provider=directml` run on any DirectX 12 GPU. The Runtime refuses to start if the provider you asked for can't load, unless you also pass `--Tau:AllowCpuFallback=true`. Every setting is described in [`docs/runtime-config.md`](docs/runtime-config.md).

### How do I serve the other models?

Drop `-Only laya-en` from step 2 to fetch all five packages: `laya-en`, `laya-multilingual`, `laya-typed-decisions`, `von-1.2.0` and the fine-tuned `laya-en-ft-banking77`. That's about 6.5 GB of downloads. The Runtime serves every package it finds in `models/`, or only the ones you list with `--Tau:Models:0=laya-en --Tau:Models:1=von-1.2.0`.

### How do I build the models from source instead?

Fetch the checkpoints from Hugging Face at their pinned revisions, then export them to ONNX yourself. The export needs [uv](https://docs.astral.sh/uv/), which fetches its own Python 3.12 and about 3 GB of PyTorch, so allow about 12 GB of disk.

```powershell
./scripts/fetch-models.ps1 -Only laya-en
./scripts/export.ps1 -Only laya-en
```

A fresh export gives the same answers to 4 decimal places but isn't byte-identical to the release files. The committed calibrators are bound to the release hashes, so use `fetch-onnx.ps1` if you want the reports to reproduce exactly.

### How do I run it in Docker?

The image carries the Runtime and the ONNX Runtime natives but no models. Fetch them on the host first (step 2), then mount the folder read-only:

```powershell
docker build -f src/Tau.Runtime/Dockerfile -t tau-runtime:local .
docker run --rm -p 8088:8080 -v "${PWD}/models:/models:ro" -e Tau__Models__0=laya-en tau-runtime:local
```

## How do I call it from C#?

`Tau.Client` is a typed client for any `/v1/systemone` server. It isn't on nuget.org yet, so pack it and the contract types it depends on, then add it to your project from that folder:

```powershell
dotnet pack src/Tau.Contract -c Release -o artifacts/packages
dotnet pack src/Tau.Client -c Release -o artifacts/packages
dotnet add <your-project> package Tau.Client --source <path-to-tau>/artifacts/packages
```

Then ask for an enum:

```csharp
using Tau.Client;

using var client = new SystemOneClient(new Uri("http://localhost:8088/"));

var decision = await client.DecideAsync<Category>(
    state: "My card payment failed three times this morning and I need it sorted today.",
    instructions: "Which support category fits this message?");

Console.WriteLine($"{decision.Value} ({decision.Confidence:F2})");

enum Category { Billing, Technical, Account }
```

`ScoreAsync` and `NoulAsync` do the same for an ordered scale and a yes/no question. See [`src/Tau.Client/README.md`](src/Tau.Client/README.md) for option descriptions, errors and retries.

## How do I know if a local model is good enough to replace frontier calls?

That's the Workbench's job. You describe one decision in a `decision.yaml`: the question, the labelled data, the local models and the frontier model. Then `tau run` measures every model on a calibration split and a held-out split, fits temperature and isotonic calibrators, picks the confidence threshold that meets your target error, simulates the cascade (local answer above the threshold, frontier below it) and prices it. It writes a self-contained `report.html` with the misses left in. Frontier answers arrive as files in `frontier/cache.jsonl`, so the frontier model is never called.

A spec can also list hosted `/v1/systemone` endpoints under `external:`, and the Workbench measures them over the network alongside the local models. The key is read at run time from the environment variable the spec names in `api_key_env` and is never written to disk. A budget in the spec (`budget_usd`) stops the run before the estimated spend passes it. Both worked examples measure TypeSafe's hosted Jev this way. `scripts/examples.ps1` runs the whole thing for the two worked examples, and their committed reports are [`examples/banking77/report.html`](examples/banking77/report.html) and [`examples/support-tickets/report.html`](examples/support-tickets/report.html). Run `tau --help` for the stages.

## What's in the repository?

- `contracts/` holds the dated `/v1/systemone` contract snapshot and its JSON schemas.
- `docs/` holds the decisions log, progress notes, Runtime configuration and articles.
- `examples/` holds the two worked Workbench examples with their reports, and the quickstart request.
- `reports/r1/` holds the Runtime's parity, conformance and latency reports.
- `scripts/` holds the PowerShell entry points: fetch, export, run examples, benchmark, publish.
- `sidecar/finetune/` is the Python sidecar for ONNX export, reference parity, fine-tuning and data preparation.
- `specs/` holds the specification, plan and task list for each release.
- `src/` holds the .NET projects: Runtime, inference engine, contract, calibration, client and Workbench.
- `tests/` holds the .NET test projects, conformance requests and shared fixtures.
- `tools/` holds the benchmark and conformance runners.

## How do I reproduce the numbers?

Each report comes from one command. The script checks everything first and prints the exact command for anything missing: the data preparation, a model export or a local fine-tune. `-CheckOnly` stops after those checks. Both examples run on CUDA, so fetch the CUDA natives first (see above).

```powershell
./scripts/examples.ps1 -Example banking77
./scripts/examples.ps1 -Example support-tickets
```

The fine-tunes need an NVIDIA GPU and take tens of minutes each. The frontier answers are committed, so a re-run never calls Claude. Re-measuring Jev needs your own key in `TYPESAFE_API_KEY` and costs a few pence (the last run's estimate was $0.14 for Banking77 and $0.04 for the tickets). Without the key the Workbench sends Jev nothing and says so.

The latency figures are in [`reports/r1/latency.md`](reports/r1/latency.md). Regenerate them with `./scripts/bench.ps1 -Http` on a quiet machine. Each report records the hardware it ran on, and the CPU and GPU load either side of the timed passes.

To build and run every test: `./scripts/build-test.ps1`, or `./scripts/build-test.ps1 -NoModels` on a machine without the exported models.

## What's the licence?

Tau is licensed under the [Apache License 2.0](LICENSE). See [`NOTICE`](NOTICE) for attributions, and [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for the licences of the packages, models and datasets it uses. Model weights and dataset rows aren't in this repository. The scripts download them from their original sources.
