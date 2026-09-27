# Configuring the Tau Runtime

The Runtime reads its settings from the `Tau` section of `appsettings.json`. You can override any of them
with an environment variable (`Tau__Provider=cuda`) or a command-line switch (`--Tau:Provider=cuda`).
Nothing here needs a secret. The Runtime accepts an `Authorization` header so existing clients keep working,
and then ignores it.

## Settings

| Setting | Default | What it does |
| --- | --- | --- |
| `Provider` | `cpu` | Where inference runs: `cpu`, `cuda` (NVIDIA) or `directml` (any DirectX 12 GPU on Windows). |
| `AllowCpuFallback` | `false` | If the GPU provider can't start, run on CPU instead of refusing to start. Leave it off when you're measuring anything: a silent fallback makes GPU numbers wrong. |
| `ModelsDirectory` | `models` beside the executable, or the repo's `models/` | Where the model packages live. Each package is a folder with `tau-model.json`, `model.onnx`, `model.onnx.data` and the tokeniser files. |
| `NativeDirectory` | `native` beside the executable, or the repo's `native/` | Where the ONNX Runtime native libraries live, one folder per provider (`native/cpu`, `native/cuda`, `native/directml`). |
| `CudaDepsDirectory` | `native/cuda-deps` | The CUDA 12.8 and cuDNN 9 libraries the CUDA provider needs. `scripts/fetch-cuda.ps1` puts them there from NVIDIA's free pip wheels, so you don't need the CUDA toolkit installed. |
| `Models` | every package found | The model ids to serve, for example `["laya-en", "laya-multilingual"]`. |
| `Preload` | `true` | Load every model at start-up. The first request then doesn't pay a load of several seconds. |
| `CalibratorsDirectory` | none | A folder of `*.calibrator.json` files (made by the Workbench in R2). The Runtime applies them to every answer. A malformed calibrator, or one fitted on a different model export, stops the Runtime at start-up. |
| `MaxRequestBytes` | `1048576` | The largest request body accepted. Anything bigger gets HTTP 413. |
| `VonMaxTokens` | `4096` | The longest packed sequence Tau sends to Von. Von's reference allows 8,192, but its attention masks grow with the square of the length. Longer requests get a 422 that names the question. |
| `OtlpEndpoint` | none | Send traces and metrics to an OpenTelemetry collector. |

The listening address comes from ASP.NET Core's own `Urls` setting. The default is `http://localhost:8088`.

## Getting a GPU provider working

```powershell
./scripts/fetch-natives.ps1   # ONNX Runtime 1.24.4 for cpu, cuda and directml
./scripts/fetch-cuda.ps1      # CUDA 12.8 + cuDNN 9 runtime libraries (CUDA only)
./scripts/provider-smoke.ps1  # proves each provider actually runs on this machine
```

Why 1.24.4 across the board: the DirectML build of ONNX Runtime stops at 1.24.4, and one managed ONNX
Runtime assembly can only drive native libraries of its own version. The Runtime loads the native library
for the configured provider at start-up, so the same binary serves all three.

## Model routing

Send `"model": "laya-en"` (or `laya-multilingual`, `laya-typed-decisions`, `von-1.2.0`) to pin a model.
Send `auto`, `tau-auto`, `jev-latest` or anything starting `jev-` and the Runtime picks between `laya-en`
and `laya-multilingual` by looking at the state's script and language, the same way Laya's own router
does. The English checkpoint falls apart on other languages while still sounding confident, which is why
this exists. `laya-typed-decisions` is never picked automatically. The response's `model` field and the
`x-tau-route-reason` header tell you what was chosen and why.

## Headers

Tau adds diagnostics as `x-tau-*` response headers so the response body stays exactly the published
contract:

- `x-tau-model-hash`: sha256 of the ONNX model that answered.
- `x-tau-route-reason`: why that model was chosen.
- `x-tau-calibrators`: the calibrators applied, or `none`.
- `x-tau-truncated`: `true` if the state was cut to fit the model.
- `x-tau-model-ms`: time spent in the model's forward pass.
- `x-tau-precision`: `full` when the request asked for unrounded answers (see below); absent otherwise.

Send `x-tau-raw: true` to skip Tau's calibrators and get the model's own post-processing. The Workbench
uses this to measure the uncalibrated model.

Send `x-tau-precision: full` to get every value in the answers unrounded: the probabilities, and the noul,
score and confidence values, exactly as the engine computed them before the reference's rounding (4 dp for
probabilities). The Runtime answers with `x-tau-precision: full` when it honoured the request. Without the
header the response is rounded exactly as the reference runtimes round it, byte for byte. The Workbench sends
it on every request, raw and calibrated, so calibrators are fitted on the same values the Runtime applies them
to. Endpoints that don't know the header (Jev, Kev) ignore it and still round.

## Other endpoints

- `GET /v1/models` lists the installed models with their pinned revisions and hashes.
- `GET /healthz` returns 200 once the models are loaded.
- `GET /metrics` serves Prometheus metrics: request and model latency, tokens, batch rows and
  truncations, each tagged with the model id and the first 12 characters of its hash.
