# Tau latency: in-process, CPU

These are measurements from the reference machine described below, at the precision stated. They say nothing about other hardware. The headline tables use repeat 1. Every figure is in milliseconds.

## Run

- **Measured (UTC)**: 2026-09-27T16:58:00Z
- **Reproduce with**: `./scripts/bench.ps1 -Http -Models laya-en,laya-multilingual,laya-typed-decisions,von-1.2.0`
- **Bench command**: `Tau.Bench --provider cpu --iterations 30 --warmup 5 --out reports/r1 --repeat 2 --questions 1,4,10 --invoked-by "./scripts/bench.ps1 -Http -Models laya-en,laya-multilingual,laya-typed-decisions,von-1.2.0" --models laya-en,laya-multilingual,laya-typed-decisions,von-1.2.0`
- **Git commit**: `c58502cca4793eef16676242aba2696956d8348b` (working tree had uncommitted changes)
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
- **Machine load just before timing (models already loaded)**: CPU 9.40 %, GPU 0.33 %, GPU memory in use 1150 MiB
- **Machine load just after timing**: CPU 7.87 %, GPU 15.67 %, GPU memory in use 1678 MiB
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
| laya-en | 528.44 / 606.31 / 621.96 | 2164.74 / 2377.76 / 2446.10 | 541.19 | 5615.65 / 5911.83 / 5950.76 | 561.56 |
| laya-multilingual | 233.48 / 262.26 / 279.59 | 905.25 / 1034.00 / 1051.87 | 226.31 | 2526.84 / 2640.63 / 2792.36 | 252.68 |
| laya-typed-decisions | 575.86 / 623.23 / 637.22 | 2199.33 / 2390.74 / 2498.42 | 549.83 | 5826.61 / 6170.56 / 6226.70 | 582.66 |
| von-1.2.0 | 398.85 / 453.15 / 464.15 | 2072.44 / 2228.89 / 2261.94 | 518.11 | 5571.17 / 5900.62 / 5990.65 | 557.12 |

This is the cost of the model itself, the figure closest to what a model card quotes. It's the floor: nothing in Tau's own code can make a request faster than this. If the engine figure below is well above it, the extra time is Tau's tokenising, row building and post-processing, and that's the place to optimise.

## Engine end to end (ms)

Stopwatch around OnnxDecisionEngine.DecideAsync on an already-parsed request: routing, tokenising, building the rows, the forward pass (including host-device copies) and the reference post-processing. No HTTP, no JSON.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 529.21 / 607.11 / 622.72 | 2165.70 / 2378.86 / 2447.05 | 541.43 | 5616.91 / 5913.20 / 5952.17 | 561.69 |
| laya-multilingual | 233.97 / 262.69 / 280.01 | 905.84 / 1034.63 / 1052.58 | 226.46 | 2527.83 / 2641.65 / 2793.41 | 252.78 |
| laya-typed-decisions | 576.39 / 623.85 / 637.83 | 2200.07 / 2391.55 / 2499.13 | 550.02 | 5827.77 / 6171.72 / 6227.74 | 582.78 |
| von-1.2.0 | 399.72 / 456.32 / 465.17 | 2074.73 / 2231.95 / 2264.84 | 518.68 | 5576.43 / 5905.89 / 5998.70 | 557.64 |

This is what a .NET program that embeds Tau.Inference pays per request, with no network hop. The per-question columns divide the batched p50 by the number of questions, which is the figure to set against batched per-question numbers published elsewhere, and only on comparable hardware.

## Model forward pass, varied inputs (ms)

As 'Model forward pass', but each of 10 requests per cell uses the workload's text cut to a different word count this process hasn't sent before (5 upwards, shuffled with a fixed seed), with no warm-up on those shapes. This is closer to real traffic than the fixed workload: a GPU runtime can't reuse shape-specific work.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 245.32 / 272.35 / 275.14 | 1031.56 / 1166.59 / 1186.95 | 257.89 | 2864.68 / 3160.31 / 3177.68 | 286.47 |
| laya-multilingual | 96.42 / 110.31 / 116.33 | 393.52 / 451.09 / 455.31 | 98.38 | 1044.60 / 1316.47 / 1375.60 | 104.46 |
| laya-typed-decisions | 280.88 / 313.00 / 326.40 | 1052.61 / 1255.19 / 1345.92 | 263.15 | 2809.01 / 3064.87 / 3112.40 | 280.90 |
| von-1.2.0 | 206.48 / 246.08 / 249.87 | 1031.32 / 1200.68 / 1250.21 | 257.83 | 2814.18 / 3074.48 / 3147.93 | 281.42 |



## Engine end to end, varied inputs (ms)

As 'Engine end to end', with the varied inputs above. The input-tokens column is the mean over the requests.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 245.62 / 272.71 / 275.51 | 1032.17 / 1167.40 / 1187.71 | 258.04 | 2865.89 / 3161.68 / 3179.05 | 286.59 |
| laya-multilingual | 96.76 / 110.56 / 116.57 | 394.06 / 451.77 / 456.05 | 98.52 | 1045.53 / 1317.64 / 1376.88 | 104.55 |
| laya-typed-decisions | 281.34 / 313.40 / 326.81 | 1053.40 / 1255.97 / 1346.65 | 263.35 | 2810.48 / 3066.19 / 3113.86 | 281.05 |
| von-1.2.0 | 207.02 / 246.57 / 250.40 | 1033.60 / 1202.06 / 1251.58 | 258.40 | 2817.11 / 3076.82 / 3150.71 | 281.71 |



## Run-to-run variation

| Measurement | Model | q | p50 repeat 1 | p50 repeat 2 | Change |
|---|---|---:|---:|---:|---:|
| Model forward pass | laya-en | 1 | 528.44 | 525.44 | -0.57 % |
| Model forward pass | laya-en | 4 | 2164.74 | 2270.48 | +4.88 % |
| Model forward pass | laya-en | 10 | 5615.65 | 6262.47 | +11.52 % |
| Model forward pass | laya-multilingual | 1 | 233.48 | 208.28 | -10.79 % |
| Model forward pass | laya-multilingual | 4 | 905.25 | 883.38 | -2.42 % |
| Model forward pass | laya-multilingual | 10 | 2526.84 | 2375.44 | -5.99 % |
| Model forward pass | laya-typed-decisions | 1 | 575.86 | 515.74 | -10.44 % |
| Model forward pass | laya-typed-decisions | 4 | 2199.33 | 2163.51 | -1.63 % |
| Model forward pass | laya-typed-decisions | 10 | 5826.61 | 5700.82 | -2.16 % |
| Model forward pass | von-1.2.0 | 1 | 398.85 | 379.11 | -4.95 % |
| Model forward pass | von-1.2.0 | 4 | 2072.44 | 2083.19 | +0.52 % |
| Model forward pass | von-1.2.0 | 10 | 5571.17 | 5469.18 | -1.83 % |
| Engine end to end | laya-en | 1 | 529.21 | 525.92 | -0.62 % |
| Engine end to end | laya-en | 4 | 2165.70 | 2271.57 | +4.89 % |
| Engine end to end | laya-en | 10 | 5616.91 | 6263.89 | +11.52 % |
| Engine end to end | laya-multilingual | 1 | 233.97 | 208.70 | -10.80 % |
| Engine end to end | laya-multilingual | 4 | 905.84 | 884.03 | -2.41 % |
| Engine end to end | laya-multilingual | 10 | 2527.83 | 2376.32 | -5.99 % |
| Engine end to end | laya-typed-decisions | 1 | 576.39 | 516.25 | -10.43 % |
| Engine end to end | laya-typed-decisions | 4 | 2200.07 | 2164.26 | -1.63 % |
| Engine end to end | laya-typed-decisions | 10 | 5827.77 | 5701.88 | -2.16 % |
| Engine end to end | von-1.2.0 | 1 | 399.72 | 379.85 | -4.97 % |
| Engine end to end | von-1.2.0 | 4 | 2074.73 | 2085.58 | +0.52 % |
| Engine end to end | von-1.2.0 | 10 | 5576.43 | 5473.93 | -1.84 % |

The two timed passes ran back to back on the same process and models. The largest p50 change between them is 11.52 %. SC-007 asks that a rerun of the command reproduces the headline figures within the variation a report records, and this is that variation. A change of more than a few percent means the machine wasn't quiet, and the figures should be re-measured before they're quoted.

## Full distributions (repeat 1)

Standard deviation is the sample standard deviation. Later repeats are in the JSON file next to this one.

### Model forward pass

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 30 | 535.82 | 44.07 | 454.31 | 528.44 | 606.31 | 621.96 | 627.36 |
| laya-en | 4 | 30 | 2187.96 | 113.73 | 2024.70 | 2164.74 | 2377.76 | 2446.10 | 2464.34 |
| laya-en | 10 | 30 | 5606.51 | 173.68 | 5300.22 | 5615.65 | 5911.83 | 5950.76 | 5952.01 |
| laya-multilingual | 1 | 30 | 231.25 | 23.74 | 186.65 | 233.48 | 262.26 | 279.59 | 286.66 |
| laya-multilingual | 4 | 30 | 926.68 | 68.07 | 823.03 | 905.25 | 1034.00 | 1051.87 | 1058.12 |
| laya-multilingual | 10 | 30 | 2503.63 | 126.03 | 2270.94 | 2526.84 | 2640.63 | 2792.36 | 2850.47 |
| laya-typed-decisions | 1 | 30 | 574.21 | 37.02 | 490.48 | 575.86 | 623.23 | 637.22 | 642.31 |
| laya-typed-decisions | 4 | 30 | 2209.89 | 127.07 | 2000.58 | 2199.33 | 2390.74 | 2498.42 | 2541.95 |
| laya-typed-decisions | 10 | 30 | 5864.24 | 168.79 | 5576.19 | 5826.61 | 6170.56 | 6226.70 | 6237.14 |
| von-1.2.0 | 1 | 30 | 405.55 | 34.19 | 345.44 | 398.85 | 453.15 | 464.15 | 465.67 |
| von-1.2.0 | 4 | 30 | 2075.53 | 87.65 | 1934.73 | 2072.44 | 2228.89 | 2261.94 | 2265.14 |
| von-1.2.0 | 10 | 30 | 5587.20 | 160.67 | 5401.74 | 5571.17 | 5900.62 | 5990.65 | 6017.68 |

### Engine end to end

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 30 | 536.54 | 44.08 | 454.90 | 529.21 | 607.11 | 622.72 | 628.11 |
| laya-en | 4 | 30 | 2188.95 | 113.82 | 2025.61 | 2165.70 | 2378.86 | 2447.05 | 2465.19 |
| laya-en | 10 | 30 | 5607.75 | 173.82 | 5301.16 | 5616.91 | 5913.20 | 5952.17 | 5953.36 |
| laya-multilingual | 1 | 30 | 231.66 | 23.77 | 186.96 | 233.97 | 262.69 | 280.01 | 287.06 |
| laya-multilingual | 4 | 30 | 927.32 | 68.09 | 823.64 | 905.84 | 1034.63 | 1052.58 | 1058.83 |
| laya-multilingual | 10 | 30 | 2504.63 | 126.04 | 2271.70 | 2527.83 | 2641.65 | 2793.41 | 2851.47 |
| laya-typed-decisions | 1 | 30 | 574.76 | 37.07 | 490.92 | 576.39 | 623.85 | 637.83 | 642.92 |
| laya-typed-decisions | 4 | 30 | 2210.64 | 127.10 | 2001.46 | 2200.07 | 2391.55 | 2499.13 | 2542.62 |
| laya-typed-decisions | 10 | 30 | 5865.43 | 168.80 | 5577.21 | 5827.77 | 6171.72 | 6227.74 | 6238.16 |
| von-1.2.0 | 1 | 30 | 406.82 | 34.45 | 346.42 | 399.72 | 456.32 | 465.17 | 466.67 |
| von-1.2.0 | 4 | 30 | 2078.34 | 87.61 | 1936.96 | 2074.73 | 2231.95 | 2264.84 | 2268.03 |
| von-1.2.0 | 10 | 30 | 5593.10 | 161.07 | 5407.77 | 5576.43 | 5905.89 | 5998.70 | 6026.38 |

### Model forward pass, varied inputs

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|

### Engine end to end, varied inputs

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|

