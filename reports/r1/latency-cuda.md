# Tau latency: in-process, CUDA

These are measurements from the reference machine described below, at the precision stated. They say nothing about other hardware. The headline tables use repeat 1. Every figure is in milliseconds.

## Run

- **Measured (UTC)**: 2026-09-27T16:21:42Z
- **Reproduce with**: `./scripts/bench.ps1 -Http -Models laya-en,laya-multilingual,laya-typed-decisions,von-1.2.0`
- **Bench command**: `Tau.Bench --provider cuda --iterations 500 --warmup 50 --out reports/r1 --repeat 2 --questions 1,4,10 --invoked-by "./scripts/bench.ps1 -Http -Models laya-en,laya-multilingual,laya-typed-decisions,von-1.2.0" --models laya-en,laya-multilingual,laya-typed-decisions,von-1.2.0`
- **Git commit**: `c58502cca4793eef16676242aba2696956d8348b` (working tree had uncommitted changes)
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
  - nvidia-smi lists this process (pid 37936) as a compute client
- **Precision**: fp32 for every model
- **Session**: graph optimisation ORT_ENABLE_BASIC, sequential execution, intra-op threads 8, inter-op threads 1, deterministic compute on, CUDA: use_tf32=0, cudnn_conv_algo_search=DEFAULT, no Tau calibrators loaded (reference post-processing)
- **Sampling**: 50 warm-up iterations per cell, not timed. Then 500 timed iterations per cell, and the whole timed pass run 2 time(s) back to back. Timer: System.Diagnostics.Stopwatch (high resolution). Percentiles: linear interpolation between closest ranks (Hyndman-Fan type 7, the NumPy and Excel PERCENTILE.INC default).
- **Machine load just before timing (models already loaded)**: CPU 8.70 %, GPU 18.67 %, GPU memory in use 9147 MiB
- **Machine load just after timing**: CPU 1.85 %, GPU 33.33 %, GPU memory in use 11828 MiB
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
| laya-en | 18.26 / 19.30 / 21.00 | 50.98 / 52.44 / 53.28 | 12.75 | 113.32 / 115.75 / 120.50 | 11.33 |
| laya-multilingual | 12.09 / 13.55 / 15.52 | 23.96 / 25.02 / 25.84 | 5.99 | 52.45 / 54.29 / 55.29 | 5.24 |
| laya-typed-decisions | 19.11 / 20.28 / 21.09 | 51.78 / 53.64 / 56.90 | 12.94 | 127.16 / 155.18 / 181.85 | 12.72 |
| von-1.2.0 | 17.06 / 19.60 / 22.11 | 52.19 / 72.20 / 102.58 | 13.05 | 126.35 / 176.77 / 243.76 | 12.63 |

This is the cost of the model itself, the figure closest to what a model card quotes. It's the floor: nothing in Tau's own code can make a request faster than this. If the engine figure below is well above it, the extra time is Tau's tokenising, row building and post-processing, and that's the place to optimise.

## Engine end to end (ms)

Stopwatch around OnnxDecisionEngine.DecideAsync on an already-parsed request: routing, tokenising, building the rows, the forward pass (including host-device copies) and the reference post-processing. No HTTP, no JSON.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 18.59 / 19.77 / 21.78 | 51.47 / 53.06 / 53.80 | 12.87 | 114.11 / 116.58 / 121.54 | 11.41 |
| laya-multilingual | 12.34 / 13.86 / 15.86 | 24.39 / 25.48 / 26.52 | 6.10 | 53.09 / 55.04 / 55.92 | 5.31 |
| laya-typed-decisions | 19.45 / 20.66 / 21.47 | 52.27 / 54.31 / 57.62 | 13.07 | 127.97 / 155.99 / 182.60 | 12.80 |
| von-1.2.0 | 17.67 / 20.21 / 22.63 | 53.68 / 74.11 / 104.89 | 13.42 | 129.36 / 179.54 / 246.65 | 12.94 |

This is what a .NET program that embeds Tau.Inference pays per request, with no network hop. The per-question columns divide the batched p50 by the number of questions, which is the figure to set against batched per-question numbers published elsewhere, and only on comparable hardware.

## Model forward pass, varied inputs (ms)

As 'Model forward pass', but each of 150 requests per cell uses the workload's text cut to a different word count this process hasn't sent before (5 upwards, shuffled with a fixed seed), with no warm-up on those shapes. This is closer to real traffic than the fixed workload: a GPU runtime can't reuse shape-specific work.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 23.42 / 30.48 / 34.11 | 77.20 / 116.51 / 124.36 | 19.30 | 153.23 / 264.55 / 285.89 | 15.32 |
| laya-multilingual | 12.87 / 18.04 / 21.50 | 30.49 / 57.78 / 66.57 | 7.62 | 76.10 / 143.60 / 162.17 | 7.61 |
| laya-typed-decisions | 22.53 / 28.57 / 30.04 | 67.02 / 112.18 / 128.84 | 16.75 | 132.22 / 200.06 / 228.19 | 13.22 |
| von-1.2.0 | 19.38 / 22.50 / 24.04 | 59.73 / 93.01 / 97.67 | 14.93 | 131.36 / 5455.11 / 5861.54 | 13.14 |



## Engine end to end, varied inputs (ms)

As 'Engine end to end', with the varied inputs above. The input-tokens column is the mean over the requests.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 23.78 / 30.88 / 34.50 | 77.88 / 117.08 / 124.94 | 19.47 | 154.13 / 265.35 / 286.78 | 15.41 |
| laya-multilingual | 13.11 / 18.35 / 21.79 | 30.88 / 58.22 / 67.05 | 7.72 | 76.70 / 144.33 / 162.91 | 7.67 |
| laya-typed-decisions | 22.88 / 28.99 / 30.45 | 67.56 / 112.74 / 129.51 | 16.89 | 133.11 / 200.89 / 229.00 | 13.31 |
| von-1.2.0 | 19.85 / 23.75 / 25.27 | 60.98 / 96.70 / 100.40 | 15.24 | 134.32 / 5461.78 / 5869.32 | 13.43 |



## Run-to-run variation

| Measurement | Model | q | p50 repeat 1 | p50 repeat 2 | Change |
|---|---|---:|---:|---:|---:|
| Model forward pass | laya-en | 1 | 18.26 | 19.38 | +6.16 % |
| Model forward pass | laya-en | 4 | 50.98 | 56.95 | +11.70 % |
| Model forward pass | laya-en | 10 | 113.32 | 120.89 | +6.68 % |
| Model forward pass | laya-multilingual | 1 | 12.09 | 12.07 | -0.12 % |
| Model forward pass | laya-multilingual | 4 | 23.96 | 23.31 | -2.72 % |
| Model forward pass | laya-multilingual | 10 | 52.45 | 52.82 | +0.71 % |
| Model forward pass | laya-typed-decisions | 1 | 19.11 | 19.66 | +2.85 % |
| Model forward pass | laya-typed-decisions | 4 | 51.78 | 52.19 | +0.79 % |
| Model forward pass | laya-typed-decisions | 10 | 127.16 | 122.21 | -3.89 % |
| Model forward pass | von-1.2.0 | 1 | 17.06 | 17.58 | +3.07 % |
| Model forward pass | von-1.2.0 | 4 | 52.19 | 62.73 | +20.19 % |
| Model forward pass | von-1.2.0 | 10 | 126.35 | 153.95 | +21.85 % |
| Engine end to end | laya-en | 1 | 18.59 | 19.72 | +6.12 % |
| Engine end to end | laya-en | 4 | 51.47 | 57.46 | +11.64 % |
| Engine end to end | laya-en | 10 | 114.11 | 121.66 | +6.62 % |
| Engine end to end | laya-multilingual | 1 | 12.34 | 12.34 | +0.04 % |
| Engine end to end | laya-multilingual | 4 | 24.39 | 23.69 | -2.85 % |
| Engine end to end | laya-multilingual | 10 | 53.09 | 53.41 | +0.60 % |
| Engine end to end | laya-typed-decisions | 1 | 19.45 | 19.98 | +2.72 % |
| Engine end to end | laya-typed-decisions | 4 | 52.27 | 52.66 | +0.74 % |
| Engine end to end | laya-typed-decisions | 10 | 127.97 | 122.99 | -3.89 % |
| Engine end to end | von-1.2.0 | 1 | 17.67 | 18.17 | +2.83 % |
| Engine end to end | von-1.2.0 | 4 | 53.68 | 64.22 | +19.65 % |
| Engine end to end | von-1.2.0 | 10 | 129.36 | 156.92 | +21.31 % |

The two timed passes ran back to back on the same process and models. The largest p50 change between them is 21.85 %. SC-007 asks that a rerun of the command reproduces the headline figures within the variation a report records, and this is that variation. A change of more than a few percent means the machine wasn't quiet, and the figures should be re-measured before they're quoted.

## Full distributions (repeat 1)

Standard deviation is the sample standard deviation. Later repeats are in the JSON file next to this one.

### Model forward pass

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 500 | 18.37 | 0.61 | 17.60 | 18.26 | 19.30 | 21.00 | 22.58 |
| laya-en | 4 | 500 | 51.12 | 0.77 | 50.03 | 50.98 | 52.44 | 53.28 | 59.22 |
| laya-en | 10 | 500 | 113.65 | 1.98 | 111.08 | 113.32 | 115.75 | 120.50 | 136.49 |
| laya-multilingual | 1 | 500 | 12.27 | 0.79 | 11.60 | 12.09 | 13.55 | 15.52 | 20.00 |
| laya-multilingual | 4 | 500 | 24.04 | 0.58 | 23.08 | 23.96 | 25.02 | 25.84 | 27.63 |
| laya-multilingual | 10 | 500 | 52.59 | 0.86 | 51.44 | 52.45 | 54.29 | 55.29 | 56.03 |
| laya-typed-decisions | 1 | 500 | 19.22 | 0.75 | 18.05 | 19.11 | 20.28 | 21.09 | 29.12 |
| laya-typed-decisions | 4 | 500 | 51.96 | 1.14 | 50.42 | 51.78 | 53.64 | 56.90 | 60.21 |
| laya-typed-decisions | 10 | 500 | 129.39 | 17.56 | 110.31 | 127.16 | 155.18 | 181.85 | 325.66 |
| von-1.2.0 | 1 | 500 | 17.33 | 1.26 | 15.91 | 17.06 | 19.60 | 22.11 | 28.90 |
| von-1.2.0 | 4 | 500 | 56.29 | 13.42 | 48.20 | 52.19 | 72.20 | 102.58 | 236.88 |
| von-1.2.0 | 10 | 500 | 134.98 | 29.95 | 116.20 | 126.35 | 176.77 | 243.76 | 478.93 |

### Engine end to end

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 500 | 18.70 | 0.67 | 17.87 | 18.59 | 19.77 | 21.78 | 23.08 |
| laya-en | 4 | 500 | 51.61 | 0.82 | 50.49 | 51.47 | 53.06 | 53.80 | 60.90 |
| laya-en | 10 | 500 | 114.44 | 2.00 | 111.80 | 114.11 | 116.58 | 121.54 | 137.26 |
| laya-multilingual | 1 | 500 | 12.53 | 0.82 | 11.84 | 12.34 | 13.86 | 15.86 | 20.46 |
| laya-multilingual | 4 | 500 | 24.46 | 0.63 | 23.44 | 24.39 | 25.48 | 26.52 | 28.43 |
| laya-multilingual | 10 | 500 | 53.23 | 0.89 | 52.03 | 53.09 | 55.04 | 55.92 | 56.67 |
| laya-typed-decisions | 1 | 500 | 19.56 | 0.78 | 18.35 | 19.45 | 20.66 | 21.47 | 29.57 |
| laya-typed-decisions | 4 | 500 | 52.48 | 1.21 | 50.88 | 52.27 | 54.31 | 57.62 | 61.56 |
| laya-typed-decisions | 10 | 500 | 130.21 | 17.59 | 111.03 | 127.97 | 155.99 | 182.60 | 326.67 |
| von-1.2.0 | 1 | 500 | 17.91 | 1.34 | 16.32 | 17.67 | 20.21 | 22.63 | 29.73 |
| von-1.2.0 | 4 | 500 | 57.84 | 13.45 | 49.86 | 53.68 | 74.11 | 104.89 | 238.23 |
| von-1.2.0 | 10 | 500 | 138.16 | 30.01 | 118.75 | 129.36 | 179.54 | 246.65 | 482.63 |

### Model forward pass, varied inputs

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|

### Engine end to end, varied inputs

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|

