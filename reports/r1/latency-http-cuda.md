# Tau latency: HTTP, CUDA

These are measurements from the reference machine described below, at the precision stated. They say nothing about other hardware. The headline tables use repeat 1. Every figure is in milliseconds.

## Run

- **Measured (UTC)**: 2026-09-27T17:11:10Z
- **Reproduce with**: `./scripts/bench.ps1 -Http -Models laya-en,laya-multilingual,laya-typed-decisions,von-1.2.0`
- **Bench command**: `Tau.Bench --provider cuda --iterations 500 --http-url http://127.0.0.1:18088 --http-server-log C:\projects\personal\Tau\.cache\bench-run\runtime-18088.log --warmup 50 --out reports/r1 --repeat 2 --questions 1,4,10 --invoked-by "./scripts/bench.ps1 -Http -Models laya-en,laya-multilingual,laya-typed-decisions,von-1.2.0" --models laya-en,laya-multilingual,laya-typed-decisions,von-1.2.0`
- **Git commit**: `c58502cca4793eef16676242aba2696956d8348b` (working tree had uncommitted changes)
- **Contract**: `systemone/2026-09-27`
- **GPU**: NVIDIA GeForce RTX 3080 Ti, 12288 MiB, driver 610.47
- **CPU**: 11th Gen Intel(R) Core(TM) i9-11900K @ 3.50GHz, 8 physical cores, 16 logical processors
- **RAM**: 63.8 GiB
- **OS**: Microsoft Windows 10 Pro 10.0.19045
- **Software**: .NET 10.0.11 (x64), ONNX Runtime 1.24.4 managed, Tau 0.1.0
- **Provider**: requested CUDA, ran on CUDA
  - the Runtime's start-up log (runtime-18088.log) says 'Tau engine ready: provider Cuda', logged from engine.ActualProvider
  - scripts/bench.ps1 starts the Runtime with --Tau:AllowCpuFallback=false, so a GPU that can't start stops the server instead of falling back
- **Precision**: fp32 for every model
- **Session**: graph optimisation ORT_ENABLE_BASIC, sequential execution, intra-op threads 8, inter-op threads 1, deterministic compute on, CUDA: use_tf32=0, cudnn_conv_algo_search=DEFAULT, no Tau calibrators loaded (reference post-processing)
- **Sampling**: 50 warm-up iterations per cell, not timed. Then 500 timed iterations per cell, and the whole timed pass run 2 time(s) back to back. Timer: System.Diagnostics.Stopwatch (high resolution). Percentiles: linear interpolation between closest ranks (Hyndman-Fan type 7, the NumPy and Excel PERCENTILE.INC default).
- **Machine load just before timing (models already loaded)**: CPU 4.56 %, GPU 3.00 %, GPU memory in use 10184 MiB
- **Machine load just after timing**: CPU 5.27 %, GPU 32.00 %, GPU memory in use 11776 MiB
  - CPU: GetSystemTimes busy share over the sampling window; GPU: mean of 3 nvidia-smi utilization.gpu/memory.used readings 0.5 s apart. A snapshot, so it shows whether the machine was busy, not what happened during every iteration.
- **Note**: The working tree had uncommitted changes when this ran, so the commit above doesn't fully describe the code measured.
- **Note**: Server: http://127.0.0.1:18088/. Batch rows aren't visible over HTTP, so they're recorded as -1; input tokens come from the response's usage block.

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
| laya-en | n/a / 188 | n/a / 693 | n/a / 1731 |
| laya-multilingual | n/a / 196 | n/a / 728 | n/a / 1820 |
| laya-typed-decisions | n/a / 188 | n/a / 693 | n/a / 1731 |
| von-1.2.0 | n/a / 163 | n/a / 618 | n/a / 1561 |

Both families build one row per question with the whole state in it, so the tokens grow with the question count. Von also adds a state-free row for each noul question (its zero-shot prior correction), so a Von request sends more rows than it has questions. The models use different tokenisers, so their token counts differ for the same text.

## HTTP end to end (ms)

Client-side wall time for one POST /v1/systemone over a kept-alive localhost connection: sending the JSON body, the Runtime's parsing, validation and inference, and reading and deserialising the response into the contract types.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 19.48 / 21.92 / 26.14 | 51.84 / 53.87 / 55.96 | 12.96 | 113.86 / 117.94 / 122.67 | 11.39 |
| laya-multilingual | 12.43 / 13.39 / 20.09 | 24.07 / 24.90 / 25.63 | 6.02 | 54.70 / 57.61 / 60.02 | 5.47 |
| laya-typed-decisions | 20.14 / 25.87 / 29.22 | 52.95 / 55.82 / 58.39 | 13.24 | 123.37 / 156.98 / 186.69 | 12.34 |
| von-1.2.0 | 18.94 / 25.47 / 28.71 | 55.84 / 75.84 / 105.78 | 13.96 | 126.34 / 151.06 / 161.80 | 12.63 |

This is what a client on the same machine waits for one request. Set it against the in-process engine figure for the same provider to see what HTTP, JSON and ASP.NET add. A client on another machine adds its network round trip on top.

## Model forward pass as reported by the Runtime (ms)

The x-tau-model-ms response header of the same requests: the forward-pass time measured inside the server. The gap between this and the HTTP figure is what HTTP, JSON and the host add.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 18.68 / 21.09 / 25.31 | 50.86 / 52.90 / 54.51 | 12.72 | 112.63 / 116.69 / 121.06 | 11.26 |
| laya-multilingual | 11.90 / 12.79 / 19.43 | 23.30 / 24.01 / 24.77 | 5.83 | 53.58 / 56.38 / 58.59 | 5.36 |
| laya-typed-decisions | 19.44 / 25.09 / 28.59 | 52.01 / 54.73 / 57.25 | 13.00 | 121.50 / 154.59 / 184.46 | 12.15 |
| von-1.2.0 | 17.92 / 24.32 / 27.65 | 53.74 / 73.42 / 103.18 | 13.43 | 122.99 / 147.62 / 158.82 | 12.30 |

This should match the in-process forward pass on the same provider. If it's clearly higher, something else was using the GPU or CPU during the HTTP run and the HTTP figures above should be re-measured before anyone quotes them.

## Run-to-run variation

| Measurement | Model | q | p50 repeat 1 | p50 repeat 2 | Change |
|---|---|---:|---:|---:|---:|
| HTTP end to end | laya-en | 1 | 19.48 | 21.18 | +8.75 % |
| HTTP end to end | laya-en | 4 | 51.84 | 54.62 | +5.37 % |
| HTTP end to end | laya-en | 10 | 113.86 | 120.85 | +6.14 % |
| HTTP end to end | laya-multilingual | 1 | 12.43 | 13.00 | +4.62 % |
| HTTP end to end | laya-multilingual | 4 | 24.07 | 24.84 | +3.19 % |
| HTTP end to end | laya-multilingual | 10 | 54.70 | 54.14 | -1.02 % |
| HTTP end to end | laya-typed-decisions | 1 | 20.14 | 19.71 | -2.13 % |
| HTTP end to end | laya-typed-decisions | 4 | 52.95 | 54.25 | +2.46 % |
| HTTP end to end | laya-typed-decisions | 10 | 123.37 | 120.20 | -2.57 % |
| HTTP end to end | von-1.2.0 | 1 | 18.94 | 17.96 | -5.18 % |
| HTTP end to end | von-1.2.0 | 4 | 55.84 | 53.06 | -4.96 % |
| HTTP end to end | von-1.2.0 | 10 | 126.34 | 125.37 | -0.77 % |
| Model forward pass as reported by the Runtime | laya-en | 1 | 18.68 | 20.40 | +9.22 % |
| Model forward pass as reported by the Runtime | laya-en | 4 | 50.86 | 53.75 | +5.68 % |
| Model forward pass as reported by the Runtime | laya-en | 10 | 112.63 | 119.70 | +6.27 % |
| Model forward pass as reported by the Runtime | laya-multilingual | 1 | 11.90 | 12.36 | +3.93 % |
| Model forward pass as reported by the Runtime | laya-multilingual | 4 | 23.30 | 24.01 | +3.02 % |
| Model forward pass as reported by the Runtime | laya-multilingual | 10 | 53.58 | 53.12 | -0.86 % |
| Model forward pass as reported by the Runtime | laya-typed-decisions | 1 | 19.44 | 18.99 | -2.29 % |
| Model forward pass as reported by the Runtime | laya-typed-decisions | 4 | 52.01 | 53.34 | +2.56 % |
| Model forward pass as reported by the Runtime | laya-typed-decisions | 10 | 121.50 | 118.94 | -2.11 % |
| Model forward pass as reported by the Runtime | von-1.2.0 | 1 | 17.92 | 17.09 | -4.61 % |
| Model forward pass as reported by the Runtime | von-1.2.0 | 4 | 53.74 | 51.48 | -4.21 % |
| Model forward pass as reported by the Runtime | von-1.2.0 | 10 | 122.99 | 122.43 | -0.46 % |

The two timed passes ran back to back on the same process and models. The largest p50 change between them is 9.22 %. SC-007 asks that a rerun of the command reproduces the headline figures within the variation a report records, and this is that variation. A change of more than a few percent means the machine wasn't quiet, and the figures should be re-measured before they're quoted.

## Full distributions (repeat 1)

Standard deviation is the sample standard deviation. Later repeats are in the JSON file next to this one.

### HTTP end to end

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 500 | 19.79 | 1.31 | 18.37 | 19.48 | 21.92 | 26.14 | 27.74 |
| laya-en | 4 | 500 | 52.02 | 1.13 | 50.19 | 51.84 | 53.87 | 55.96 | 58.35 |
| laya-en | 10 | 500 | 114.33 | 2.38 | 111.33 | 113.86 | 117.94 | 122.67 | 131.71 |
| laya-multilingual | 1 | 500 | 12.68 | 1.27 | 12.01 | 12.43 | 13.39 | 20.09 | 25.15 |
| laya-multilingual | 4 | 500 | 24.13 | 0.46 | 23.46 | 24.07 | 24.90 | 25.63 | 28.44 |
| laya-multilingual | 10 | 500 | 54.79 | 1.72 | 52.01 | 54.70 | 57.61 | 60.02 | 61.15 |
| laya-typed-decisions | 1 | 500 | 20.88 | 2.21 | 18.64 | 20.14 | 25.87 | 29.22 | 34.63 |
| laya-typed-decisions | 4 | 500 | 53.22 | 1.51 | 51.40 | 52.95 | 55.82 | 58.39 | 65.83 |
| laya-typed-decisions | 10 | 500 | 127.82 | 18.24 | 112.16 | 123.37 | 156.98 | 186.69 | 346.69 |
| von-1.2.0 | 1 | 500 | 19.83 | 2.50 | 17.17 | 18.94 | 25.47 | 28.71 | 32.22 |
| von-1.2.0 | 4 | 500 | 59.34 | 15.80 | 51.07 | 55.84 | 75.84 | 105.78 | 249.33 |
| von-1.2.0 | 10 | 500 | 129.93 | 11.13 | 119.24 | 126.34 | 151.06 | 161.80 | 186.69 |

### Model forward pass as reported by the Runtime

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 500 | 18.99 | 1.24 | 17.76 | 18.68 | 21.09 | 25.31 | 27.04 |
| laya-en | 4 | 500 | 51.07 | 1.07 | 49.38 | 50.86 | 52.90 | 54.51 | 56.86 |
| laya-en | 10 | 500 | 113.07 | 2.32 | 110.19 | 112.63 | 116.69 | 121.06 | 129.62 |
| laya-multilingual | 1 | 500 | 12.12 | 1.25 | 11.59 | 11.90 | 12.79 | 19.43 | 24.57 |
| laya-multilingual | 4 | 500 | 23.34 | 0.40 | 22.76 | 23.30 | 24.01 | 24.77 | 27.22 |
| laya-multilingual | 10 | 500 | 53.62 | 1.62 | 51.05 | 53.58 | 56.38 | 58.59 | 59.83 |
| laya-typed-decisions | 1 | 500 | 20.14 | 2.19 | 18.01 | 19.44 | 25.09 | 28.59 | 33.86 |
| laya-typed-decisions | 4 | 500 | 52.28 | 1.35 | 50.58 | 52.01 | 54.73 | 57.25 | 62.57 |
| laya-typed-decisions | 10 | 500 | 126.07 | 18.12 | 110.75 | 121.50 | 154.59 | 184.46 | 344.64 |
| von-1.2.0 | 1 | 500 | 18.78 | 2.43 | 16.25 | 17.92 | 24.32 | 27.65 | 31.00 |
| von-1.2.0 | 4 | 500 | 57.31 | 15.73 | 49.37 | 53.74 | 73.42 | 103.18 | 247.26 |
| von-1.2.0 | 10 | 500 | 126.58 | 11.03 | 116.34 | 122.99 | 147.62 | 158.82 | 179.70 |

