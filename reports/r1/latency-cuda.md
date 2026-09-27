# Tau latency: in-process, CUDA

These are measurements from the reference machine described below, at the precision stated. They say nothing about other hardware. The headline tables use repeat 1. Every figure is in milliseconds.

## Run

- **Measured (UTC)**: 2026-09-27T04:22:38Z
- **Reproduce with**: `./scripts/bench.ps1 -Http`
- **Bench command**: `Tau.Bench --provider cuda --iterations 500 --warmup 50 --out reports/r1 --repeat 2 --questions 1,4,10 --invoked-by "./scripts/bench.ps1 -Http"`
- **Git commit**: `c26162e40284fbbff86bc937e482885cbb961b64` (working tree had uncommitted changes)
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
  - nvidia-smi lists this process (pid 35380) as a compute client
- **Precision**: fp32 for every model
- **Session**: graph optimisation ORT_ENABLE_BASIC, sequential execution, intra-op threads 8, inter-op threads 1, deterministic compute on, CUDA: use_tf32=0, cudnn_conv_algo_search=DEFAULT, no Tau calibrators loaded (reference post-processing)
- **Sampling**: 50 warm-up iterations per cell, not timed. Then 500 timed iterations per cell, and the whole timed pass run 2 time(s) back to back. Timer: System.Diagnostics.Stopwatch (high resolution). Percentiles: linear interpolation between closest ranks (Hyndman-Fan type 7, the NumPy and Excel PERCENTILE.INC default).
- **Machine load just before timing (models already loaded)**: CPU 49.94 %, GPU 0.00 %, GPU memory in use 8339 MiB
- **Machine load just after timing**: CPU 50.60 %, GPU 33.33 %, GPU memory in use 11967 MiB
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
| laya-en | 21.09 / 27.16 / 29.59 | 51.23 / 52.10 / 54.26 | 12.81 | 111.12 / 112.18 / 112.76 | 11.11 |
| laya-multilingual | 18.33 / 25.31 / 27.73 | 23.96 / 26.99 / 28.72 | 5.99 | 52.24 / 53.05 / 53.63 | 5.22 |
| laya-typed-decisions | 25.37 / 36.08 / 39.46 | 52.24 / 53.11 / 53.47 | 13.06 | 111.83 / 112.79 / 113.21 | 11.18 |
| von-1.2.0 | 18.84 / 28.82 / 32.92 | 50.38 / 51.02 / 51.53 | 12.59 | 116.94 / 118.42 / 118.92 | 11.69 |

This is the cost of the model itself, the figure closest to what a model card quotes. It's the floor: nothing in Tau's own code can make a request faster than this. If the engine figure below is well above it, the extra time is Tau's tokenising, row building and post-processing, and that's the place to optimise.

## Engine end to end (ms)

Stopwatch around OnnxDecisionEngine.DecideAsync on an already-parsed request: routing, tokenising, building the rows, the forward pass (including host-device copies) and the reference post-processing. No HTTP, no JSON.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 21.57 / 27.91 / 30.53 | 51.99 / 53.22 / 55.01 | 13.00 | 112.44 / 114.09 / 115.16 | 11.24 |
| laya-multilingual | 18.73 / 25.92 / 28.31 | 24.62 / 27.88 / 29.55 | 6.16 | 53.28 / 54.51 / 55.36 | 5.33 |
| laya-typed-decisions | 25.95 / 36.72 / 40.10 | 53.08 / 54.25 / 54.69 | 13.27 | 113.15 / 114.51 / 115.17 | 11.31 |
| von-1.2.0 | 19.75 / 29.90 / 34.20 | 52.87 / 54.74 / 55.71 | 13.22 | 122.97 / 127.01 / 128.27 | 12.30 |

This is what a .NET program that embeds Tau.Inference pays per request, with no network hop. The per-question columns divide the batched p50 by the number of questions, which is the figure to set against batched per-question numbers published elsewhere, and only on comparable hardware.

## Model forward pass, varied inputs (ms)

As 'Model forward pass', but each of 150 requests per cell uses the workload's text cut to a different word count this process hasn't sent before (5 upwards, shuffled with a fixed seed), with no warm-up on those shapes. This is closer to real traffic than the fixed workload: a GPU runtime can't reuse shape-specific work.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 25.04 / 35.88 / 41.24 | 56.58 / 81.95 / 82.93 | 14.15 | 124.49 / 195.02 / 197.25 | 12.45 |
| laya-multilingual | 17.54 / 25.85 / 28.04 | 28.08 / 41.80 / 42.31 | 7.02 | 59.23 / 96.55 / 97.94 | 5.92 |
| laya-typed-decisions | 25.15 / 33.21 / 38.19 | 55.88 / 81.34 / 83.27 | 13.97 | 125.56 / 195.59 / 200.31 | 12.56 |
| von-1.2.0 | 21.82 / 28.34 / 33.59 | 58.12 / 90.23 / 91.37 | 14.53 | 123.57 / 3722.01 / 4774.57 | 12.36 |



## Engine end to end, varied inputs (ms)

As 'Engine end to end', with the varied inputs above. The input-tokens column is the mean over the requests.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 25.57 / 36.52 / 42.22 | 57.37 / 82.79 / 84.32 | 14.34 | 126.16 / 196.18 / 199.01 | 12.62 |
| laya-multilingual | 17.83 / 26.31 / 28.53 | 28.55 / 42.34 / 43.03 | 7.14 | 60.34 / 97.61 / 99.16 | 6.03 |
| laya-typed-decisions | 25.79 / 34.18 / 39.33 | 56.51 / 82.16 / 84.58 | 14.13 | 126.85 / 196.93 / 201.70 | 12.69 |
| von-1.2.0 | 23.25 / 29.97 / 35.24 | 60.67 / 95.27 / 98.94 | 15.17 | 130.23 / 3732.96 / 4791.71 | 13.02 |



## Run-to-run variation

| Measurement | Model | q | p50 repeat 1 | p50 repeat 2 | Change |
|---|---|---:|---:|---:|---:|
| Model forward pass | laya-en | 1 | 21.09 | 21.79 | +3.33 % |
| Model forward pass | laya-en | 4 | 51.23 | 52.23 | +1.95 % |
| Model forward pass | laya-en | 10 | 111.12 | 111.89 | +0.69 % |
| Model forward pass | laya-multilingual | 1 | 18.33 | 14.88 | -18.82 % |
| Model forward pass | laya-multilingual | 4 | 23.96 | 24.14 | +0.75 % |
| Model forward pass | laya-multilingual | 10 | 52.24 | 52.09 | -0.28 % |
| Model forward pass | laya-typed-decisions | 1 | 25.37 | 20.35 | -19.79 % |
| Model forward pass | laya-typed-decisions | 4 | 52.24 | 52.21 | -0.06 % |
| Model forward pass | laya-typed-decisions | 10 | 111.83 | 112.90 | +0.95 % |
| Model forward pass | von-1.2.0 | 1 | 18.84 | 18.01 | -4.42 % |
| Model forward pass | von-1.2.0 | 4 | 50.38 | 50.62 | +0.49 % |
| Model forward pass | von-1.2.0 | 10 | 116.94 | 118.37 | +1.23 % |
| Engine end to end | laya-en | 1 | 21.57 | 22.28 | +3.26 % |
| Engine end to end | laya-en | 4 | 51.99 | 53.06 | +2.06 % |
| Engine end to end | laya-en | 10 | 112.44 | 113.06 | +0.55 % |
| Engine end to end | laya-multilingual | 1 | 18.73 | 15.22 | -18.78 % |
| Engine end to end | laya-multilingual | 4 | 24.62 | 24.71 | +0.37 % |
| Engine end to end | laya-multilingual | 10 | 53.28 | 52.97 | -0.58 % |
| Engine end to end | laya-typed-decisions | 1 | 25.95 | 20.77 | -19.94 % |
| Engine end to end | laya-typed-decisions | 4 | 53.08 | 52.91 | -0.32 % |
| Engine end to end | laya-typed-decisions | 10 | 113.15 | 114.11 | +0.85 % |
| Engine end to end | von-1.2.0 | 1 | 19.75 | 18.86 | -4.52 % |
| Engine end to end | von-1.2.0 | 4 | 52.87 | 52.93 | +0.11 % |
| Engine end to end | von-1.2.0 | 10 | 122.97 | 123.21 | +0.19 % |

The two timed passes ran back to back on the same process and models. The largest p50 change between them is 19.94 %. SC-007 asks that a rerun of the command reproduces the headline figures within the variation a report records, and this is that variation. A change of more than a few percent means the machine wasn't quiet, and the figures should be re-measured before they're quoted.

## Full distributions (repeat 1)

Standard deviation is the sample standard deviation. Later repeats are in the JSON file next to this one.

### Model forward pass

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 500 | 21.80 | 2.82 | 18.26 | 21.09 | 27.16 | 29.59 | 32.91 |
| laya-en | 4 | 500 | 51.32 | 0.79 | 50.06 | 51.23 | 52.10 | 54.26 | 60.26 |
| laya-en | 10 | 500 | 111.25 | 0.91 | 110.12 | 111.12 | 112.18 | 112.76 | 126.39 |
| laya-multilingual | 1 | 500 | 18.76 | 3.73 | 12.09 | 18.33 | 25.31 | 27.73 | 31.62 |
| laya-multilingual | 4 | 500 | 24.31 | 1.11 | 23.04 | 23.96 | 26.99 | 28.72 | 29.36 |
| laya-multilingual | 10 | 500 | 52.31 | 0.68 | 51.29 | 52.24 | 53.05 | 53.63 | 58.91 |
| laya-typed-decisions | 1 | 500 | 26.13 | 5.14 | 19.04 | 25.37 | 36.08 | 39.46 | 47.56 |
| laya-typed-decisions | 4 | 500 | 52.29 | 0.47 | 51.28 | 52.24 | 53.11 | 53.47 | 53.77 |
| laya-typed-decisions | 10 | 500 | 111.93 | 0.45 | 111.01 | 111.83 | 112.79 | 113.21 | 115.29 |
| von-1.2.0 | 1 | 500 | 20.44 | 3.81 | 16.66 | 18.84 | 28.82 | 32.92 | 37.70 |
| von-1.2.0 | 4 | 500 | 50.40 | 0.35 | 49.54 | 50.38 | 51.02 | 51.53 | 52.19 |
| von-1.2.0 | 10 | 500 | 117.16 | 1.01 | 116.04 | 116.94 | 118.42 | 118.92 | 134.80 |

### Engine end to end

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 500 | 22.33 | 2.93 | 18.64 | 21.57 | 27.91 | 30.53 | 33.60 |
| laya-en | 4 | 500 | 52.12 | 0.90 | 50.62 | 51.99 | 53.22 | 55.01 | 61.31 |
| laya-en | 10 | 500 | 112.65 | 1.11 | 111.03 | 112.44 | 114.09 | 115.16 | 128.21 |
| laya-multilingual | 1 | 500 | 19.17 | 3.80 | 12.36 | 18.73 | 25.92 | 28.31 | 32.04 |
| laya-multilingual | 4 | 500 | 24.98 | 1.21 | 23.49 | 24.62 | 27.88 | 29.55 | 30.44 |
| laya-multilingual | 10 | 500 | 53.42 | 0.84 | 52.09 | 53.28 | 54.51 | 55.36 | 61.09 |
| laya-typed-decisions | 1 | 500 | 26.70 | 5.23 | 19.50 | 25.95 | 36.72 | 40.10 | 48.18 |
| laya-typed-decisions | 4 | 500 | 53.13 | 0.62 | 51.84 | 53.08 | 54.25 | 54.69 | 54.97 |
| laya-typed-decisions | 10 | 500 | 113.27 | 0.67 | 112.00 | 113.15 | 114.51 | 115.17 | 116.65 |
| von-1.2.0 | 1 | 500 | 21.36 | 4.00 | 17.33 | 19.75 | 29.90 | 34.20 | 38.93 |
| von-1.2.0 | 4 | 500 | 53.05 | 0.94 | 51.22 | 52.87 | 54.74 | 55.71 | 56.85 |
| von-1.2.0 | 10 | 500 | 123.30 | 2.27 | 119.57 | 122.97 | 127.01 | 128.27 | 139.51 |

### Model forward pass, varied inputs

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|

### Engine end to end, varied inputs

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|

