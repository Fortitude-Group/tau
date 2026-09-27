# Tau latency: in-process, CPU

These are measurements from the reference machine described below, at the precision stated. They say nothing about other hardware. The headline tables use repeat 1. Every figure is in milliseconds.

## Run

- **Measured (UTC)**: 2026-09-27T05:06:43Z
- **Reproduce with**: `./scripts/bench.ps1 -Http`
- **Bench command**: `Tau.Bench --provider cpu --iterations 30 --warmup 5 --out reports/r1 --repeat 2 --questions 1,4,10 --invoked-by "./scripts/bench.ps1 -Http"`
- **Git commit**: `c26162e40284fbbff86bc937e482885cbb961b64` (working tree had uncommitted changes)
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
- **Machine load just before timing (models already loaded)**: CPU 48.77 %, GPU 0.00 %, GPU memory in use 892 MiB
- **Machine load just after timing**: CPU 52.44 %, GPU 0.00 %, GPU memory in use 969 MiB
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
| laya-en | 810.20 / 862.57 / 885.75 | 3012.14 / 3162.97 / 3229.81 | 753.03 | 6839.91 / 7323.40 / 7364.07 | 683.99 |
| laya-multilingual | 252.43 / 294.89 / 304.60 | 983.54 / 1056.32 / 1076.32 | 245.89 | 2627.08 / 2766.25 / 2794.22 | 262.71 |
| laya-typed-decisions | 576.48 / 620.85 / 637.88 | 2614.58 / 2961.89 / 3315.95 | 653.65 | 7300.85 / 7463.95 / 7545.23 | 730.08 |
| von-1.2.0 | 510.79 / 534.37 / 541.49 | 2593.71 / 2675.45 / 2725.07 | 648.43 | 6820.37 / 7051.60 / 8139.27 | 682.04 |

This is the cost of the model itself, the figure closest to what a model card quotes. It's the floor: nothing in Tau's own code can make a request faster than this. If the engine figure below is well above it, the extra time is Tau's tokenising, row building and post-processing, and that's the place to optimise.

## Engine end to end (ms)

Stopwatch around OnnxDecisionEngine.DecideAsync on an already-parsed request: routing, tokenising, building the rows, the forward pass (including host-device copies) and the reference post-processing. No HTTP, no JSON.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 811.20 / 863.50 / 886.67 | 3013.33 / 3164.10 / 3231.05 | 753.33 | 6841.55 / 7325.41 / 7366.16 | 684.16 |
| laya-multilingual | 252.89 / 295.40 / 305.10 | 984.23 / 1057.32 / 1077.28 | 246.06 | 2628.01 / 2767.37 / 2795.33 | 262.80 |
| laya-typed-decisions | 577.00 / 621.66 / 638.62 | 2615.48 / 2962.93 / 3317.03 | 653.87 | 7302.40 / 7465.53 / 7547.25 | 730.24 |
| von-1.2.0 | 512.21 / 536.13 / 542.83 | 2597.83 / 2679.21 / 2729.61 | 649.46 | 6827.89 / 7058.61 / 8147.32 | 682.79 |

This is what a .NET program that embeds Tau.Inference pays per request, with no network hop. The per-question columns divide the batched p50 by the number of questions, which is the figure to set against batched per-question numbers published elsewhere, and only on comparable hardware.

## Model forward pass, varied inputs (ms)

As 'Model forward pass', but each of 10 requests per cell uses the workload's text cut to a different word count this process hasn't sent before (5 upwards, shuffled with a fixed seed), with no warm-up on those shapes. This is closer to real traffic than the fixed workload: a GPU runtime can't reuse shape-specific work.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 298.34 / 323.27 / 328.89 | 1189.14 / 1348.45 / 1410.49 | 297.29 | 3452.78 / 3871.24 / 4008.63 | 345.28 |
| laya-multilingual | 138.82 / 159.31 / 165.77 | 491.33 / 616.82 / 665.80 | 122.83 | 1382.34 / 1566.06 / 1621.66 | 138.23 |
| laya-typed-decisions | 334.01 / 374.05 / 383.66 | 1304.07 / 1426.48 / 1483.34 | 326.02 | 3468.36 / 3840.25 / 3936.89 | 346.84 |
| von-1.2.0 | 264.31 / 288.08 / 295.99 | 1208.46 / 1334.01 / 1387.94 | 302.12 | 3289.76 / 3677.26 / 3762.27 | 328.98 |



## Engine end to end, varied inputs (ms)

As 'Engine end to end', with the varied inputs above. The input-tokens column is the mean over the requests.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 298.75 / 323.66 / 329.27 | 1189.86 / 1349.30 / 1411.42 | 297.47 | 3454.54 / 3873.01 / 4010.56 | 345.45 |
| laya-multilingual | 139.20 / 159.70 / 166.15 | 492.03 / 617.84 / 666.88 | 123.01 | 1383.69 / 1567.54 / 1623.01 | 138.37 |
| laya-typed-decisions | 334.43 / 374.52 / 384.15 | 1305.02 / 1427.46 / 1484.39 | 326.25 | 3470.00 / 3842.11 / 3938.56 | 347.00 |
| von-1.2.0 | 264.88 / 288.77 / 296.71 | 1209.82 / 1335.53 / 1389.44 | 302.46 | 3292.43 / 3680.54 / 3765.25 | 329.24 |



## Run-to-run variation

| Measurement | Model | q | p50 repeat 1 | p50 repeat 2 | Change |
|---|---|---:|---:|---:|---:|
| Model forward pass | laya-en | 1 | 810.20 | 667.72 | -17.59 % |
| Model forward pass | laya-en | 4 | 3012.14 | 2753.31 | -8.59 % |
| Model forward pass | laya-en | 10 | 6839.91 | 7195.90 | +5.20 % |
| Model forward pass | laya-multilingual | 1 | 252.43 | 276.60 | +9.58 % |
| Model forward pass | laya-multilingual | 4 | 983.54 | 1121.94 | +14.07 % |
| Model forward pass | laya-multilingual | 10 | 2627.08 | 2984.14 | +13.59 % |
| Model forward pass | laya-typed-decisions | 1 | 576.48 | 673.49 | +16.83 % |
| Model forward pass | laya-typed-decisions | 4 | 2614.58 | 2685.99 | +2.73 % |
| Model forward pass | laya-typed-decisions | 10 | 7300.85 | 6898.94 | -5.51 % |
| Model forward pass | von-1.2.0 | 1 | 510.79 | 481.62 | -5.71 % |
| Model forward pass | von-1.2.0 | 4 | 2593.71 | 2496.28 | -3.76 % |
| Model forward pass | von-1.2.0 | 10 | 6820.37 | 6616.34 | -2.99 % |
| Engine end to end | laya-en | 1 | 811.20 | 668.47 | -17.59 % |
| Engine end to end | laya-en | 4 | 3013.33 | 2754.17 | -8.60 % |
| Engine end to end | laya-en | 10 | 6841.55 | 7197.45 | +5.20 % |
| Engine end to end | laya-multilingual | 1 | 252.89 | 277.02 | +9.54 % |
| Engine end to end | laya-multilingual | 4 | 984.23 | 1122.81 | +14.08 % |
| Engine end to end | laya-multilingual | 10 | 2628.01 | 2985.14 | +13.59 % |
| Engine end to end | laya-typed-decisions | 1 | 577.00 | 674.14 | +16.84 % |
| Engine end to end | laya-typed-decisions | 4 | 2615.48 | 2686.95 | +2.73 % |
| Engine end to end | laya-typed-decisions | 10 | 7302.40 | 6900.71 | -5.50 % |
| Engine end to end | von-1.2.0 | 1 | 512.21 | 482.70 | -5.76 % |
| Engine end to end | von-1.2.0 | 4 | 2597.83 | 2499.38 | -3.79 % |
| Engine end to end | von-1.2.0 | 10 | 6827.89 | 6624.63 | -2.98 % |

The two timed passes ran back to back on the same process and models. The largest p50 change between them is 17.59 %. SC-007 asks that a rerun of the command reproduces the headline figures within the variation a report records, and this is that variation. A change of more than a few percent means the machine wasn't quiet, and the figures should be re-measured before they're quoted.

## Full distributions (repeat 1)

Standard deviation is the sample standard deviation. Later repeats are in the JSON file next to this one.

### Model forward pass

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 30 | 806.71 | 40.85 | 718.14 | 810.20 | 862.57 | 885.75 | 894.63 |
| laya-en | 4 | 30 | 3010.47 | 113.71 | 2772.90 | 3012.14 | 3162.97 | 3229.81 | 3250.47 |
| laya-en | 10 | 30 | 6858.50 | 274.82 | 6381.29 | 6839.91 | 7323.40 | 7364.07 | 7369.59 |
| laya-multilingual | 1 | 30 | 252.95 | 21.76 | 215.13 | 252.43 | 294.89 | 304.60 | 305.02 |
| laya-multilingual | 4 | 30 | 990.34 | 43.31 | 923.20 | 983.54 | 1056.32 | 1076.32 | 1081.75 |
| laya-multilingual | 10 | 30 | 2633.92 | 76.51 | 2505.97 | 2627.08 | 2766.25 | 2794.22 | 2803.78 |
| laya-typed-decisions | 1 | 30 | 575.12 | 28.31 | 527.10 | 576.48 | 620.85 | 637.88 | 641.75 |
| laya-typed-decisions | 4 | 30 | 2641.97 | 201.07 | 2343.48 | 2614.58 | 2961.89 | 3315.95 | 3391.61 |
| laya-typed-decisions | 10 | 30 | 7300.17 | 107.51 | 7039.23 | 7300.85 | 7463.95 | 7545.23 | 7576.58 |
| von-1.2.0 | 1 | 30 | 508.06 | 19.73 | 463.56 | 510.79 | 534.37 | 541.49 | 542.97 |
| von-1.2.0 | 4 | 30 | 2592.58 | 68.14 | 2444.84 | 2593.71 | 2675.45 | 2725.07 | 2738.46 |
| von-1.2.0 | 10 | 30 | 6893.06 | 332.86 | 6644.58 | 6820.37 | 7051.60 | 8139.27 | 8573.01 |

### Engine end to end

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 30 | 807.67 | 40.83 | 719.27 | 811.20 | 863.50 | 886.67 | 895.52 |
| laya-en | 4 | 30 | 3011.76 | 113.61 | 2774.23 | 3013.33 | 3164.10 | 3231.05 | 3251.87 |
| laya-en | 10 | 30 | 6860.16 | 274.85 | 6383.30 | 6841.55 | 7325.41 | 7366.16 | 7371.69 |
| laya-multilingual | 1 | 30 | 253.44 | 21.81 | 215.62 | 252.89 | 295.40 | 305.10 | 305.50 |
| laya-multilingual | 4 | 30 | 991.05 | 43.34 | 923.76 | 984.23 | 1057.32 | 1077.28 | 1082.56 |
| laya-multilingual | 10 | 30 | 2635.01 | 76.52 | 2507.16 | 2628.01 | 2767.37 | 2795.33 | 2804.91 |
| laya-typed-decisions | 1 | 30 | 575.72 | 28.36 | 527.63 | 577.00 | 621.66 | 638.62 | 642.37 |
| laya-typed-decisions | 4 | 30 | 2642.98 | 201.15 | 2344.12 | 2615.48 | 2962.93 | 3317.03 | 3392.69 |
| laya-typed-decisions | 10 | 30 | 7301.81 | 107.52 | 7041.11 | 7302.40 | 7465.53 | 7547.25 | 7578.81 |
| von-1.2.0 | 1 | 30 | 509.67 | 19.49 | 469.44 | 512.21 | 536.13 | 542.83 | 544.29 |
| von-1.2.0 | 4 | 30 | 2596.44 | 68.30 | 2447.24 | 2597.83 | 2679.21 | 2729.61 | 2743.45 |
| von-1.2.0 | 10 | 30 | 6900.75 | 333.13 | 6652.95 | 6827.89 | 7058.61 | 8147.32 | 8582.20 |

### Model forward pass, varied inputs

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|

### Engine end to end, varied inputs

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|

