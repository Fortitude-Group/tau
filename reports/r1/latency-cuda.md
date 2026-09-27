# Tau latency: in-process, CUDA

These are measurements from the reference machine described below, at the precision stated. They say nothing about other hardware. The headline tables use repeat 1. Every figure is in milliseconds.

## Run

- **Measured (UTC)**: 2026-09-27T02:08:27Z
- **Reproduce with**: `./scripts/bench.ps1 -Http`
- **Bench command**: `Tau.Bench --provider cuda --iterations 500 --warmup 50 --out reports/r1 --repeat 2 --questions 1,4,10 --invoked-by "./scripts/bench.ps1 -Http"`
- **Git commit**: `77a47a7e0eb6da7c0049e4506c3fe1461fd20805` (working tree had uncommitted changes)
- **Contract**: `systemone/2026-09-27`
- **GPU**: NVIDIA GeForce RTX 3080 Ti, 12288 MiB, driver 610.47
- **CPU**: 11th Gen Intel(R) Core(TM) i9-11900K @ 3.50GHz, 8 physical cores, 16 logical processors
- **RAM**: 63.8 GiB
- **OS**: Microsoft Windows 10 Pro 10.0.19045
- **Software**: .NET 10.0.11 (x64), ONNX Runtime 1.24.4 managed / 1.24.4 native, Tau 0.1.0
- **Provider**: requested CUDA, ran on CUDA
  - engine built with AllowCpuFallback=false, so a provider that can't start throws instead of falling back
  - every session reports provider CUDAExecutionProvider with no fallback reason (laya-en, laya-multilingual, laya-typed-decisions, von-1.2.0)
  - engine.ActualProvider = Cuda
  - nvidia-smi lists this process (pid 32760) as a compute client
- **Precision**: fp32 for every model
- **Session**: graph optimisation ORT_ENABLE_BASIC, sequential execution, intra-op threads 8, inter-op threads 1, deterministic compute on, CUDA: use_tf32=0, cudnn_conv_algo_search=DEFAULT, no Tau calibrators loaded (reference post-processing)
- **Sampling**: 50 warm-up iterations per cell, not timed. Then 500 timed iterations per cell, and the whole timed pass run 2 time(s) back to back. Timer: System.Diagnostics.Stopwatch (high resolution). Percentiles: linear interpolation between closest ranks (Hyndman-Fan type 7, the NumPy and Excel PERCENTILE.INC default).
- **Machine load just before timing (models already loaded)**: CPU 46.19 %, GPU 0.00 %, GPU memory in use 9215 MiB
- **Machine load just after timing**: CPU 50.30 %, GPU 33.33 %, GPU memory in use 11955 MiB
  - CPU: GetSystemTimes busy share over the sampling window; GPU: mean of 3 nvidia-smi utilization.gpu/memory.used readings 0.5 s apart. A snapshot, so it shows whether the machine was busy, not what happened during every iteration.
- **Note**: The working tree had uncommitted changes when this ran, so the commit above doesn't fully describe the code measured.

## Models

| Model | Family | Upstream revision | ONNX sha256 | Precision | Reference |
|---|---|---|---|---|---|
| laya-en | laya | `55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851` | `866a05b244e47e96820660d18ee050c518c2de0f60c39e2e9a89dfeb056e1eec` | fp32 | laya==0.3.20 |
| laya-multilingual | laya | `55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851` | `62be63b71dd97ed1d2b6582965702d06d9fb387a1c9be103fe365d6d9c82ed0d` | fp32 | laya==0.3.20 |
| laya-typed-decisions | laya | `55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851` | `2b7a961ac37157d1cfa8105e3283106baf1ba2a5cc30fb3a673253f06aa1e1c5` | fp32 | laya==0.3.20 |
| von-1.2.0 | von | `5df8185a4f2327ad0a7cd117cc4f701ac557b9ae` | `0777bb988636663b770775ae0b4eb961d6fbee176c9bea1d1da823723b1eb3f3` | fp32 | von-sdk==1.2.3 |

## Workload

`tools/Tau.Bench/workload.json` (bank-support-duplicate-payment, canonical sha256 `33ba095cb095767fd139c6173ea1c0410256d2db35e9e7b7d3b9b2b84827910b`). One retail-banking support ticket (a duplicated debit card payment that caused an overdraft fee) and ten questions in a fixed order that cycles choice (4 options), score (4 levels), noul. A run with q questions sends the first q. The state is about 83 words. Each request pins `model` to the model id, so no auto-routing is involved.

| Questions | Types, in order |
|---|---|
| 1 | choice |
| 4 | choice, score, noul, choice |
| 10 | choice, score, noul, choice, score, noul, choice, score, noul, choice |

Input size per request (batch rows sent to the model / input tokens processed):

| Model | q=1 | q=4 | q=10 |
|---|---|---|---|
| laya-en | 1 / 188 | 4 / 693 | 10 / 1731 |
| laya-multilingual | 1 / 196 | 4 / 728 | 10 / 1820 |
| laya-typed-decisions | 1 / 188 | 4 / 693 | 10 / 1731 |
| von-1.2.0 | 1 / 163 | 5 / 618 | 13 / 1561 |

Both families build one row per question with the whole state in it, so the tokens grow with the question count. Von also adds a state-free row for each noul question (its zero-shot prior correction), so a Von request sends more rows than it has questions. The models use different tokenisers, so their token counts differ for the same text.

## Model forward pass (ms)

Time inside ONNX Runtime's Run call for the request's single batched forward pass (DecisionDiagnostics.ModelMilliseconds). On a GPU it includes copying the inputs to the device and the logits back. Excludes tokenising, building the rows and post-processing.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 19.68 / 26.42 / 29.56 | 51.23 / 52.05 / 52.51 | 12.81 | 111.68 / 112.44 / 112.88 | 11.17 |
| laya-multilingual | 15.93 / 23.50 / 26.63 | 24.20 / 25.42 / 27.47 | 6.05 | 52.14 / 52.96 / 53.43 | 5.21 |
| laya-typed-decisions | 20.55 / 28.89 / 33.51 | 52.18 / 53.08 / 53.55 | 13.04 | 113.28 / 114.48 / 116.77 | 11.33 |
| von-1.2.0 | 18.35 / 27.02 / 31.79 | 50.85 / 51.75 / 52.26 | 12.71 | 118.85 / 120.55 / 122.70 | 11.89 |

This is the cost of the model itself, the figure closest to what a model card quotes. It's the floor: nothing in Tau's own code can make a request faster than this. If the engine figure below is well above it, the extra time is Tau's tokenising, row building and post-processing, and that's the place to optimise.

## Engine end to end (ms)

Stopwatch around OnnxDecisionEngine.DecideAsync on an already-parsed request: routing, tokenising, building the rows, the forward pass (including host-device copies) and the reference post-processing. No HTTP, no JSON.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 20.26 / 26.96 / 30.19 | 51.95 / 52.98 / 53.47 | 12.99 | 112.84 / 113.96 / 114.72 | 11.28 |
| laya-multilingual | 16.27 / 23.89 / 27.03 | 24.80 / 26.30 / 28.16 | 6.20 | 53.07 / 54.30 / 54.74 | 5.31 |
| laya-typed-decisions | 21.04 / 29.58 / 34.32 | 52.90 / 54.14 / 54.83 | 13.23 | 114.45 / 116.13 / 118.45 | 11.45 |
| von-1.2.0 | 19.24 / 28.18 / 32.64 | 53.31 / 55.71 / 56.76 | 13.33 | 124.21 / 128.57 / 130.86 | 12.42 |

This is what a .NET program that embeds Tau.Inference pays per request, with no network hop. The per-question columns divide the batched p50 by the number of questions, which is the figure to set against batched per-question numbers published elsewhere, and only on comparable hardware.

## Model forward pass, varied inputs (ms)

As 'Model forward pass', but each of 150 requests per cell uses the workload's text cut to a different word count this process hasn't sent before (5 upwards, shuffled with a fixed seed), with no warm-up on those shapes. This is closer to real traffic than the fixed workload: a GPU runtime can't reuse shape-specific work.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 29.92 / 41.77 / 45.22 | 57.27 / 83.08 / 85.47 | 14.32 | 127.03 / 197.44 / 201.14 | 12.70 |
| laya-multilingual | 24.52 / 33.48 / 36.05 | 29.05 / 42.59 / 43.04 | 7.26 | 59.60 / 97.05 / 99.12 | 5.96 |
| laya-typed-decisions | 31.85 / 46.38 / 47.48 | 57.52 / 83.05 / 96.74 | 14.38 | 125.92 / 196.62 / 200.03 | 12.59 |
| von-1.2.0 | 24.86 / 33.72 / 36.67 | 59.02 / 90.99 / 92.16 | 14.76 | 124.32 / 3114.48 / 3273.75 | 12.43 |



## Engine end to end, varied inputs (ms)

As 'Engine end to end', with the varied inputs above. The input-tokens column is the mean over the requests.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 30.72 / 42.82 / 46.39 | 58.39 / 84.06 / 87.11 | 14.60 | 128.67 / 198.89 / 203.06 | 12.87 |
| laya-multilingual | 25.04 / 34.12 / 36.61 | 29.78 / 43.39 / 44.13 | 7.44 | 61.09 / 98.38 / 100.40 | 6.11 |
| laya-typed-decisions | 32.56 / 47.26 / 48.42 | 58.33 / 84.43 / 98.09 | 14.58 | 127.62 / 198.67 / 202.09 | 12.76 |
| von-1.2.0 | 26.27 / 35.35 / 38.30 | 62.25 / 96.59 / 101.67 | 15.56 | 131.63 / 3127.69 / 3290.71 | 13.16 |



## Run-to-run variation

| Measurement | Model | q | p50 repeat 1 | p50 repeat 2 | Change |
|---|---|---:|---:|---:|---:|
| Model forward pass | laya-en | 1 | 19.68 | 25.11 | +27.58 % |
| Model forward pass | laya-en | 4 | 51.23 | 53.00 | +3.45 % |
| Model forward pass | laya-en | 10 | 111.68 | 114.10 | +2.17 % |
| Model forward pass | laya-multilingual | 1 | 15.93 | 18.98 | +19.09 % |
| Model forward pass | laya-multilingual | 4 | 24.20 | 24.35 | +0.63 % |
| Model forward pass | laya-multilingual | 10 | 52.14 | 52.94 | +1.53 % |
| Model forward pass | laya-typed-decisions | 1 | 20.55 | 25.38 | +23.53 % |
| Model forward pass | laya-typed-decisions | 4 | 52.18 | 52.94 | +1.46 % |
| Model forward pass | laya-typed-decisions | 10 | 113.28 | 114.48 | +1.06 % |
| Model forward pass | von-1.2.0 | 1 | 18.35 | 30.63 | +66.89 % |
| Model forward pass | von-1.2.0 | 4 | 50.85 | 51.72 | +1.71 % |
| Model forward pass | von-1.2.0 | 10 | 118.85 | 119.74 | +0.75 % |
| Engine end to end | laya-en | 1 | 20.26 | 25.71 | +26.91 % |
| Engine end to end | laya-en | 4 | 51.95 | 53.89 | +3.75 % |
| Engine end to end | laya-en | 10 | 112.84 | 115.51 | +2.37 % |
| Engine end to end | laya-multilingual | 1 | 16.27 | 19.37 | +19.04 % |
| Engine end to end | laya-multilingual | 4 | 24.80 | 24.96 | +0.63 % |
| Engine end to end | laya-multilingual | 10 | 53.07 | 54.04 | +1.84 % |
| Engine end to end | laya-typed-decisions | 1 | 21.04 | 25.91 | +23.16 % |
| Engine end to end | laya-typed-decisions | 4 | 52.90 | 53.82 | +1.73 % |
| Engine end to end | laya-typed-decisions | 10 | 114.45 | 116.04 | +1.38 % |
| Engine end to end | von-1.2.0 | 1 | 19.24 | 32.06 | +66.64 % |
| Engine end to end | von-1.2.0 | 4 | 53.31 | 55.63 | +4.36 % |
| Engine end to end | von-1.2.0 | 10 | 124.21 | 126.78 | +2.07 % |

The two timed passes ran back to back on the same process and models. The largest p50 change between them is 66.89 %. SC-007 asks that a rerun of the command reproduces the headline figures within the variation a report records, and this is that variation. A change of more than a few percent means the machine wasn't quiet, and the figures should be re-measured before they're quoted.

## Full distributions (repeat 1)

Standard deviation is the sample standard deviation. Later repeats are in the JSON file next to this one.

### Model forward pass

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 500 | 20.82 | 2.70 | 17.87 | 19.68 | 26.42 | 29.56 | 34.22 |
| laya-en | 4 | 500 | 51.19 | 0.58 | 49.49 | 51.23 | 52.05 | 52.51 | 52.88 |
| laya-en | 10 | 500 | 111.74 | 0.97 | 110.73 | 111.68 | 112.44 | 112.88 | 131.48 |
| laya-multilingual | 1 | 500 | 16.65 | 3.42 | 11.71 | 15.93 | 23.50 | 26.63 | 31.88 |
| laya-multilingual | 4 | 500 | 24.31 | 0.67 | 23.45 | 24.20 | 25.42 | 27.47 | 29.31 |
| laya-multilingual | 10 | 500 | 52.19 | 0.44 | 51.34 | 52.14 | 52.96 | 53.43 | 54.19 |
| laya-typed-decisions | 1 | 500 | 22.19 | 3.36 | 18.90 | 20.55 | 28.89 | 33.51 | 39.46 |
| laya-typed-decisions | 4 | 500 | 52.20 | 0.53 | 50.87 | 52.18 | 53.08 | 53.55 | 54.98 |
| laya-typed-decisions | 10 | 500 | 113.68 | 5.65 | 111.28 | 113.28 | 114.48 | 116.77 | 227.10 |
| von-1.2.0 | 1 | 500 | 19.90 | 3.32 | 16.77 | 18.35 | 27.02 | 31.79 | 34.38 |
| von-1.2.0 | 4 | 500 | 51.17 | 4.48 | 49.88 | 50.85 | 51.75 | 52.26 | 143.64 |
| von-1.2.0 | 10 | 500 | 119.28 | 6.92 | 116.79 | 118.85 | 120.55 | 122.70 | 272.25 |

### Engine end to end

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 500 | 21.31 | 2.76 | 18.22 | 20.26 | 26.96 | 30.19 | 34.64 |
| laya-en | 4 | 500 | 51.91 | 0.71 | 49.94 | 51.95 | 52.98 | 53.47 | 54.08 |
| laya-en | 10 | 500 | 112.95 | 1.09 | 111.67 | 112.84 | 113.96 | 114.72 | 133.34 |
| laya-multilingual | 1 | 500 | 17.01 | 3.47 | 11.96 | 16.27 | 23.89 | 27.03 | 32.28 |
| laya-multilingual | 4 | 500 | 24.94 | 0.76 | 23.86 | 24.80 | 26.30 | 28.16 | 29.84 |
| laya-multilingual | 10 | 500 | 53.15 | 0.61 | 52.04 | 53.07 | 54.30 | 54.74 | 55.79 |
| laya-typed-decisions | 1 | 500 | 22.66 | 3.43 | 19.33 | 21.04 | 29.58 | 34.32 | 40.10 |
| laya-typed-decisions | 4 | 500 | 52.95 | 0.68 | 51.37 | 52.90 | 54.14 | 54.83 | 56.00 |
| laya-typed-decisions | 10 | 500 | 114.93 | 5.68 | 112.46 | 114.45 | 116.13 | 118.45 | 228.22 |
| von-1.2.0 | 1 | 500 | 20.76 | 3.46 | 17.24 | 19.24 | 28.18 | 32.64 | 35.72 |
| von-1.2.0 | 4 | 500 | 53.81 | 4.59 | 51.31 | 53.31 | 55.71 | 56.76 | 146.11 |
| von-1.2.0 | 10 | 500 | 124.98 | 7.18 | 120.13 | 124.21 | 128.57 | 130.86 | 278.42 |

### Model forward pass, varied inputs

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|

### Engine end to end, varied inputs

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|

