# Tau latency: in-process, CPU

These are measurements from the reference machine described below, at the precision stated. They say nothing about other hardware. The headline tables use repeat 1. Every figure is in milliseconds.

## Run

- **Measured (UTC)**: 2026-09-27T02:53:14Z
- **Reproduce with**: `./scripts/bench.ps1 -Http`
- **Bench command**: `Tau.Bench --provider cpu --iterations 30 --warmup 5 --out reports/r1 --repeat 2 --questions 1,4,10 --invoked-by "./scripts/bench.ps1 -Http"`
- **Git commit**: `77a47a7e0eb6da7c0049e4506c3fe1461fd20805` (working tree had uncommitted changes)
- **Contract**: `systemone/2026-09-27`
- **GPU**: NVIDIA GeForce RTX 3080 Ti, 12288 MiB, driver 610.47
- **CPU**: 11th Gen Intel(R) Core(TM) i9-11900K @ 3.50GHz, 8 physical cores, 16 logical processors
- **RAM**: 63.8 GiB
- **OS**: Microsoft Windows 10 Pro 10.0.19045
- **Software**: .NET 10.0.11 (x64), ONNX Runtime 1.24.4 managed / 1.24.4 native, Tau 0.1.0
- **Provider**: requested CPU, ran on CPU
  - engine built with AllowCpuFallback=false, so a provider that can't start throws instead of falling back
  - every session reports provider CPUExecutionProvider with no fallback reason (laya-en, laya-multilingual, laya-typed-decisions, von-1.2.0)
  - engine.ActualProvider = Cpu
- **Precision**: fp32 for every model
- **Session**: graph optimisation ORT_ENABLE_BASIC, sequential execution, intra-op threads 8, inter-op threads 1, deterministic compute on, no Tau calibrators loaded (reference post-processing)
- **Sampling**: 5 warm-up iterations per cell, not timed. Then 30 timed iterations per cell, and the whole timed pass run 2 time(s) back to back. Timer: System.Diagnostics.Stopwatch (high resolution). Percentiles: linear interpolation between closest ranks (Hyndman-Fan type 7, the NumPy and Excel PERCENTILE.INC default).
- **Machine load just before timing (models already loaded)**: CPU 39.89 %, GPU 0.00 %, GPU memory in use 815 MiB
- **Machine load just after timing**: CPU 46.41 %, GPU 0.00 %, GPU memory in use 1037 MiB
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
| laya-en | 916.17 / 1041.28 / 1087.29 | 3253.72 / 3440.53 / 3472.63 | 813.43 | 7317.86 / 7742.84 / 8826.61 | 731.79 |
| laya-multilingual | 257.55 / 314.77 / 327.25 | 1045.74 / 1170.46 / 1223.51 | 261.44 | 2703.14 / 2873.94 / 2968.67 | 270.31 |
| laya-typed-decisions | 594.25 / 643.74 / 659.08 | 2491.82 / 2643.47 / 2681.94 | 622.95 | 7383.67 / 7661.93 / 8948.34 | 738.37 |
| von-1.2.0 | 522.60 / 565.25 / 580.97 | 2612.10 / 2775.06 / 2796.17 | 653.02 | 6954.07 / 7187.93 / 7250.33 | 695.41 |

This is the cost of the model itself, the figure closest to what a model card quotes. It's the floor: nothing in Tau's own code can make a request faster than this. If the engine figure below is well above it, the extra time is Tau's tokenising, row building and post-processing, and that's the place to optimise.

## Engine end to end (ms)

Stopwatch around OnnxDecisionEngine.DecideAsync on an already-parsed request: routing, tokenising, building the rows, the forward pass (including host-device copies) and the reference post-processing. No HTTP, no JSON.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 917.29 / 1042.68 / 1088.44 | 3254.86 / 3441.98 / 3473.95 | 813.71 | 7319.57 / 7744.28 / 8828.30 | 731.96 |
| laya-multilingual | 258.01 / 315.29 / 327.75 | 1046.50 / 1171.15 / 1224.25 | 261.63 | 2704.49 / 2875.08 / 2969.75 | 270.45 |
| laya-typed-decisions | 594.85 / 644.37 / 659.68 | 2492.69 / 2644.30 / 2682.90 | 623.17 | 7385.54 / 7663.39 / 8950.55 | 738.55 |
| von-1.2.0 | 523.95 / 566.73 / 582.83 | 2615.89 / 2778.92 / 2800.63 | 653.97 | 6962.20 / 7195.72 / 7259.65 | 696.22 |

This is what a .NET program that embeds Tau.Inference pays per request, with no network hop. The per-question columns divide the batched p50 by the number of questions, which is the figure to set against batched per-question numbers published elsewhere, and only on comparable hardware.

## Model forward pass, varied inputs (ms)

As 'Model forward pass', but each of 10 requests per cell uses the workload's text cut to a different word count this process hasn't sent before (5 upwards, shuffled with a fixed seed), with no warm-up on those shapes. This is closer to real traffic than the fixed workload: a GPU runtime can't reuse shape-specific work.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 295.45 / 323.04 / 330.55 | 1194.83 / 1374.83 / 1399.78 | 298.71 | 3205.98 / 3558.39 / 3660.99 | 320.60 |
| laya-multilingual | 115.14 / 130.71 / 136.76 | 436.91 / 503.90 / 532.14 | 109.23 | 1207.02 / 1433.96 / 1491.22 | 120.70 |
| laya-typed-decisions | 293.48 / 322.26 / 325.97 | 1158.41 / 1302.15 / 1321.31 | 289.60 | 3266.71 / 4631.73 / 5468.66 | 326.67 |
| von-1.2.0 | 277.04 / 332.99 / 338.62 | 1153.77 / 1329.16 / 1332.58 | 288.44 | 3112.76 / 3478.19 / 3513.98 | 311.28 |



## Engine end to end, varied inputs (ms)

As 'Engine end to end', with the varied inputs above. The input-tokens column is the mean over the requests.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 295.87 / 323.46 / 330.93 | 1195.76 / 1375.65 / 1400.65 | 298.94 | 3207.40 / 3559.88 / 3662.72 | 320.74 |
| laya-multilingual | 115.47 / 131.03 / 137.10 | 437.55 / 504.54 / 532.74 | 109.39 | 1208.28 / 1435.11 / 1492.40 | 120.83 |
| laya-typed-decisions | 293.90 / 322.61 / 326.29 | 1159.12 / 1302.98 / 1322.21 | 289.78 | 3268.11 / 4633.27 / 5470.30 | 326.81 |
| von-1.2.0 | 277.64 / 333.60 / 339.25 | 1155.26 / 1330.57 / 1333.80 | 288.81 | 3116.12 / 3481.49 / 3517.55 | 311.61 |



## Run-to-run variation

| Measurement | Model | q | p50 repeat 1 | p50 repeat 2 | Change |
|---|---|---:|---:|---:|---:|
| Model forward pass | laya-en | 1 | 916.17 | 690.34 | -24.65 % |
| Model forward pass | laya-en | 4 | 3253.72 | 2784.21 | -14.43 % |
| Model forward pass | laya-en | 10 | 7317.86 | 7331.88 | +0.19 % |
| Model forward pass | laya-multilingual | 1 | 257.55 | 269.25 | +4.54 % |
| Model forward pass | laya-multilingual | 4 | 1045.74 | 1139.41 | +8.96 % |
| Model forward pass | laya-multilingual | 10 | 2703.14 | 2975.80 | +10.09 % |
| Model forward pass | laya-typed-decisions | 1 | 594.25 | 662.32 | +11.45 % |
| Model forward pass | laya-typed-decisions | 4 | 2491.82 | 2700.89 | +8.39 % |
| Model forward pass | laya-typed-decisions | 10 | 7383.67 | 6992.14 | -5.30 % |
| Model forward pass | von-1.2.0 | 1 | 522.60 | 488.81 | -6.47 % |
| Model forward pass | von-1.2.0 | 4 | 2612.10 | 2447.52 | -6.30 % |
| Model forward pass | von-1.2.0 | 10 | 6954.07 | 6545.55 | -5.87 % |
| Engine end to end | laya-en | 1 | 917.29 | 691.00 | -24.67 % |
| Engine end to end | laya-en | 4 | 3254.86 | 2785.20 | -14.43 % |
| Engine end to end | laya-en | 10 | 7319.57 | 7333.51 | +0.19 % |
| Engine end to end | laya-multilingual | 1 | 258.01 | 269.76 | +4.55 % |
| Engine end to end | laya-multilingual | 4 | 1046.50 | 1140.23 | +8.96 % |
| Engine end to end | laya-multilingual | 10 | 2704.49 | 2976.95 | +10.07 % |
| Engine end to end | laya-typed-decisions | 1 | 594.85 | 662.98 | +11.45 % |
| Engine end to end | laya-typed-decisions | 4 | 2492.69 | 2701.94 | +8.39 % |
| Engine end to end | laya-typed-decisions | 10 | 7385.54 | 6993.79 | -5.30 % |
| Engine end to end | von-1.2.0 | 1 | 523.95 | 489.81 | -6.52 % |
| Engine end to end | von-1.2.0 | 4 | 2615.89 | 2450.94 | -6.31 % |
| Engine end to end | von-1.2.0 | 10 | 6962.20 | 6554.58 | -5.85 % |

The two timed passes ran back to back on the same process and models. The largest p50 change between them is 24.67 %. SC-007 asks that a rerun of the command reproduces the headline figures within the variation a report records, and this is that variation. A change of more than a few percent means the machine wasn't quiet, and the figures should be re-measured before they're quoted.

## Full distributions (repeat 1)

Standard deviation is the sample standard deviation. Later repeats are in the JSON file next to this one.

### Model forward pass

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 30 | 931.85 | 68.91 | 818.82 | 916.17 | 1041.28 | 1087.29 | 1105.72 |
| laya-en | 4 | 30 | 3245.11 | 135.63 | 2979.22 | 3253.72 | 3440.53 | 3472.63 | 3485.43 |
| laya-en | 10 | 30 | 7345.88 | 446.23 | 6849.55 | 7317.86 | 7742.84 | 8826.61 | 9266.01 |
| laya-multilingual | 1 | 30 | 261.66 | 22.70 | 233.71 | 257.55 | 314.77 | 327.25 | 329.50 |
| laya-multilingual | 4 | 30 | 1063.40 | 59.77 | 981.04 | 1045.74 | 1170.46 | 1223.51 | 1234.54 |
| laya-multilingual | 10 | 30 | 2718.81 | 96.07 | 2582.35 | 2703.14 | 2873.94 | 2968.67 | 3003.39 |
| laya-typed-decisions | 1 | 30 | 595.61 | 29.07 | 547.64 | 594.25 | 643.74 | 659.08 | 663.77 |
| laya-typed-decisions | 4 | 30 | 2490.20 | 91.54 | 2353.81 | 2491.82 | 2643.47 | 2681.94 | 2690.30 |
| laya-typed-decisions | 10 | 30 | 7418.85 | 416.67 | 6918.82 | 7383.67 | 7661.93 | 8948.34 | 9447.90 |
| von-1.2.0 | 1 | 30 | 523.03 | 23.27 | 481.96 | 522.60 | 565.25 | 580.97 | 582.60 |
| von-1.2.0 | 4 | 30 | 2636.51 | 80.91 | 2504.60 | 2612.10 | 2775.06 | 2796.17 | 2798.58 |
| von-1.2.0 | 10 | 30 | 6968.86 | 132.76 | 6708.65 | 6954.07 | 7187.93 | 7250.33 | 7275.65 |

### Engine end to end

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 30 | 933.03 | 69.08 | 819.89 | 917.29 | 1042.68 | 1088.44 | 1106.78 |
| laya-en | 4 | 30 | 3246.48 | 135.59 | 2980.32 | 3254.86 | 3441.98 | 3473.95 | 3486.66 |
| laya-en | 10 | 30 | 7347.60 | 446.16 | 6851.22 | 7319.57 | 7744.28 | 8828.30 | 9267.73 |
| laya-multilingual | 1 | 30 | 262.13 | 22.72 | 234.08 | 258.01 | 315.29 | 327.75 | 329.98 |
| laya-multilingual | 4 | 30 | 1064.18 | 59.77 | 981.64 | 1046.50 | 1171.15 | 1224.25 | 1235.31 |
| laya-multilingual | 10 | 30 | 2719.93 | 96.09 | 2583.51 | 2704.49 | 2875.08 | 2969.75 | 3004.46 |
| laya-typed-decisions | 1 | 30 | 596.22 | 29.10 | 548.08 | 594.85 | 644.37 | 659.68 | 664.37 |
| laya-typed-decisions | 4 | 30 | 2491.11 | 91.53 | 2354.69 | 2492.69 | 2644.30 | 2682.90 | 2691.28 |
| laya-typed-decisions | 10 | 30 | 7420.69 | 416.77 | 6919.98 | 7385.54 | 7663.39 | 8950.55 | 9450.53 |
| von-1.2.0 | 1 | 30 | 524.70 | 23.29 | 483.28 | 523.95 | 566.73 | 582.83 | 584.60 |
| von-1.2.0 | 4 | 30 | 2640.33 | 80.96 | 2507.53 | 2615.89 | 2778.92 | 2800.63 | 2803.14 |
| von-1.2.0 | 10 | 30 | 6976.87 | 132.71 | 6717.29 | 6962.20 | 7195.72 | 7259.65 | 7285.08 |

### Model forward pass, varied inputs

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|

### Engine end to end, varied inputs

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|

