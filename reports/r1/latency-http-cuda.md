# Tau latency: HTTP, CUDA

These are measurements from the reference machine described below, at the precision stated. They say nothing about other hardware. The headline tables use repeat 1. Every figure is in milliseconds.

## Run

- **Measured (UTC)**: 2026-09-27T03:06:00Z
- **Reproduce with**: `./scripts/bench.ps1 -Http`
- **Bench command**: `Tau.Bench --provider cuda --iterations 500 --http-url http://127.0.0.1:18088 --http-server-log C:\projects\personal\Tau\.cache\bench-run\runtime-18088.log --warmup 50 --out reports/r1 --repeat 2 --questions 1,4,10 --invoked-by "./scripts/bench.ps1 -Http"`
- **Git commit**: `77a47a7e0eb6da7c0049e4506c3fe1461fd20805` (working tree had uncommitted changes)
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
- **Machine load just before timing (models already loaded)**: CPU 44.61 %, GPU 0.00 %, GPU memory in use 9585 MiB
- **Machine load just after timing**: CPU 43.64 %, GPU 21.33 %, GPU memory in use 11711 MiB
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
| laya-en | 19.92 / 25.10 / 30.25 | 51.93 / 53.14 / 53.75 | 12.98 | 112.37 / 113.73 / 114.50 | 11.24 |
| laya-multilingual | 16.41 / 23.10 / 27.28 | 24.86 / 26.64 / 28.39 | 6.22 | 53.19 / 54.50 / 55.23 | 5.32 |
| laya-typed-decisions | 23.35 / 34.44 / 37.49 | 52.91 / 54.43 / 55.10 | 13.23 | 113.04 / 114.73 / 115.71 | 11.30 |
| von-1.2.0 | 18.85 / 25.00 / 28.87 | 52.41 / 54.34 / 55.94 | 13.10 | 121.46 / 124.03 / 125.81 | 12.15 |

This is what a client on the same machine waits for one request. Set it against the in-process engine figure for the same provider to see what HTTP, JSON and ASP.NET add. A client on another machine adds its network round trip on top.

## Model forward pass as reported by the Runtime (ms)

The x-tau-model-ms response header of the same requests: the forward-pass time measured inside the server. The gap between this and the HTTP figure is what HTTP, JSON and the host add.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 18.97 / 24.06 / 28.63 | 50.85 / 51.65 / 52.23 | 12.71 | 110.78 / 111.58 / 112.01 | 11.08 |
| laya-multilingual | 15.62 / 22.15 / 26.53 | 23.80 / 25.13 / 27.16 | 5.95 | 51.69 / 52.54 / 52.88 | 5.17 |
| laya-typed-decisions | 22.31 / 32.39 / 36.36 | 51.70 / 52.70 / 53.15 | 12.92 | 111.38 / 112.24 / 112.84 | 11.14 |
| von-1.2.0 | 17.75 / 23.82 / 27.62 | 50.18 / 50.97 / 51.61 | 12.55 | 116.84 / 117.34 / 117.63 | 11.68 |

This should match the in-process forward pass on the same provider. If it's clearly higher, something else was using the GPU or CPU during the HTTP run and the HTTP figures above should be re-measured before anyone quotes them.

## Run-to-run variation

| Measurement | Model | q | p50 repeat 1 | p50 repeat 2 | Change |
|---|---|---:|---:|---:|---:|
| HTTP end to end | laya-en | 1 | 19.92 | 21.22 | +6.54 % |
| HTTP end to end | laya-en | 4 | 51.93 | 53.10 | +2.25 % |
| HTTP end to end | laya-en | 10 | 112.37 | 113.58 | +1.08 % |
| HTTP end to end | laya-multilingual | 1 | 16.41 | 17.12 | +4.33 % |
| HTTP end to end | laya-multilingual | 4 | 24.86 | 25.27 | +1.63 % |
| HTTP end to end | laya-multilingual | 10 | 53.19 | 53.52 | +0.62 % |
| HTTP end to end | laya-typed-decisions | 1 | 23.35 | 21.89 | -6.26 % |
| HTTP end to end | laya-typed-decisions | 4 | 52.91 | 53.19 | +0.51 % |
| HTTP end to end | laya-typed-decisions | 10 | 113.04 | 114.47 | +1.26 % |
| HTTP end to end | von-1.2.0 | 1 | 18.85 | 20.01 | +6.14 % |
| HTTP end to end | von-1.2.0 | 4 | 52.41 | 53.05 | +1.22 % |
| HTTP end to end | von-1.2.0 | 10 | 121.46 | 123.35 | +1.55 % |
| Model forward pass as reported by the Runtime | laya-en | 1 | 18.97 | 20.26 | +6.76 % |
| Model forward pass as reported by the Runtime | laya-en | 4 | 50.85 | 51.95 | +2.16 % |
| Model forward pass as reported by the Runtime | laya-en | 10 | 110.78 | 111.88 | +0.99 % |
| Model forward pass as reported by the Runtime | laya-multilingual | 1 | 15.62 | 16.29 | +4.28 % |
| Model forward pass as reported by the Runtime | laya-multilingual | 4 | 23.80 | 24.19 | +1.66 % |
| Model forward pass as reported by the Runtime | laya-multilingual | 10 | 51.69 | 52.01 | +0.62 % |
| Model forward pass as reported by the Runtime | laya-typed-decisions | 1 | 22.31 | 20.89 | -6.38 % |
| Model forward pass as reported by the Runtime | laya-typed-decisions | 4 | 51.70 | 51.94 | +0.47 % |
| Model forward pass as reported by the Runtime | laya-typed-decisions | 10 | 111.38 | 112.64 | +1.13 % |
| Model forward pass as reported by the Runtime | von-1.2.0 | 1 | 17.75 | 18.81 | +5.98 % |
| Model forward pass as reported by the Runtime | von-1.2.0 | 4 | 50.18 | 50.59 | +0.81 % |
| Model forward pass as reported by the Runtime | von-1.2.0 | 10 | 116.84 | 118.32 | +1.27 % |

The two timed passes ran back to back on the same process and models. The largest p50 change between them is 6.76 %. SC-007 asks that a rerun of the command reproduces the headline figures within the variation a report records, and this is that variation. A change of more than a few percent means the machine wasn't quiet, and the figures should be re-measured before they're quoted.

## Full distributions (repeat 1)

Standard deviation is the sample standard deviation. Later repeats are in the JSON file next to this one.

### HTTP end to end

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 500 | 20.78 | 2.24 | 18.43 | 19.92 | 25.10 | 30.25 | 31.64 |
| laya-en | 4 | 500 | 51.97 | 0.97 | 50.52 | 51.93 | 53.14 | 53.75 | 66.00 |
| laya-en | 10 | 500 | 112.54 | 1.27 | 111.27 | 112.37 | 113.73 | 114.50 | 136.87 |
| laya-multilingual | 1 | 500 | 16.95 | 3.60 | 12.22 | 16.41 | 23.10 | 27.28 | 30.07 |
| laya-multilingual | 4 | 500 | 25.05 | 0.87 | 23.67 | 24.86 | 26.64 | 28.39 | 30.33 |
| laya-multilingual | 10 | 500 | 53.29 | 0.68 | 52.03 | 53.19 | 54.50 | 55.23 | 55.82 |
| laya-typed-decisions | 1 | 500 | 24.48 | 4.52 | 19.03 | 23.35 | 34.44 | 37.49 | 42.08 |
| laya-typed-decisions | 4 | 500 | 53.05 | 0.77 | 51.46 | 52.91 | 54.43 | 55.10 | 56.38 |
| laya-typed-decisions | 10 | 500 | 113.26 | 1.06 | 112.07 | 113.04 | 114.73 | 115.71 | 129.74 |
| von-1.2.0 | 1 | 500 | 19.86 | 2.45 | 17.19 | 18.85 | 25.00 | 28.87 | 31.83 |
| von-1.2.0 | 4 | 500 | 52.64 | 1.11 | 50.76 | 52.41 | 54.34 | 55.94 | 62.64 |
| von-1.2.0 | 10 | 500 | 121.68 | 1.67 | 119.06 | 121.46 | 124.03 | 125.81 | 145.19 |

### Model forward pass as reported by the Runtime

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 500 | 19.87 | 2.12 | 17.79 | 18.97 | 24.06 | 28.63 | 30.65 |
| laya-en | 4 | 500 | 50.86 | 0.84 | 49.64 | 50.85 | 51.65 | 52.23 | 64.82 |
| laya-en | 10 | 500 | 110.87 | 1.05 | 110.02 | 110.78 | 111.58 | 112.01 | 132.83 |
| laya-multilingual | 1 | 500 | 16.17 | 3.49 | 11.66 | 15.62 | 22.15 | 26.53 | 28.82 |
| laya-multilingual | 4 | 500 | 23.96 | 0.75 | 22.88 | 23.80 | 25.13 | 27.16 | 28.97 |
| laya-multilingual | 10 | 500 | 51.77 | 0.43 | 50.95 | 51.69 | 52.54 | 52.88 | 53.81 |
| laya-typed-decisions | 1 | 500 | 23.46 | 4.37 | 18.36 | 22.31 | 32.39 | 36.36 | 40.34 |
| laya-typed-decisions | 4 | 500 | 51.78 | 0.54 | 50.59 | 51.70 | 52.70 | 53.15 | 54.33 |
| laya-typed-decisions | 10 | 500 | 111.51 | 0.88 | 110.70 | 111.38 | 112.24 | 112.84 | 127.97 |
| von-1.2.0 | 1 | 500 | 18.72 | 2.29 | 16.45 | 17.75 | 23.82 | 27.62 | 30.29 |
| von-1.2.0 | 4 | 500 | 50.28 | 0.70 | 49.30 | 50.18 | 50.97 | 51.61 | 59.07 |
| von-1.2.0 | 10 | 500 | 116.91 | 1.08 | 116.25 | 116.84 | 117.34 | 117.63 | 140.18 |

