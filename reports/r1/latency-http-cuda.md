# Tau latency: HTTP, CUDA

These are measurements from the reference machine described below, at the precision stated. They say nothing about other hardware. The headline tables use repeat 1. Every figure is in milliseconds.

## Run

- **Measured (UTC)**: 2026-09-27T05:19:25Z
- **Reproduce with**: `./scripts/bench.ps1 -Http`
- **Bench command**: `Tau.Bench --provider cuda --iterations 500 --http-url http://127.0.0.1:18088 --http-server-log C:\projects\personal\Tau\.cache\bench-run\runtime-18088.log --warmup 50 --out reports/r1 --repeat 2 --questions 1,4,10 --invoked-by "./scripts/bench.ps1 -Http"`
- **Git commit**: `c26162e40284fbbff86bc937e482885cbb961b64` (working tree had uncommitted changes)
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
- **Machine load just before timing (models already loaded)**: CPU 49.66 %, GPU 0.00 %, GPU memory in use 9596 MiB
- **Machine load just after timing**: CPU 48.01 %, GPU 30.67 %, GPU memory in use 11720 MiB
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
| laya-en | 20.86 / 27.69 / 29.94 | 52.37 / 53.68 / 54.18 | 13.09 | 112.24 / 113.53 / 114.19 | 11.22 |
| laya-multilingual | 14.90 / 21.03 / 24.12 | 24.81 / 26.32 / 27.65 | 6.20 | 53.13 / 54.32 / 54.71 | 5.31 |
| laya-typed-decisions | 21.71 / 29.62 / 32.58 | 53.00 / 54.70 / 55.76 | 13.25 | 113.10 / 114.64 / 115.89 | 11.31 |
| von-1.2.0 | 18.91 / 24.65 / 26.17 | 52.34 / 54.09 / 55.18 | 13.09 | 121.29 / 125.37 / 137.95 | 12.13 |

This is what a client on the same machine waits for one request. Set it against the in-process engine figure for the same provider to see what HTTP, JSON and ASP.NET add. A client on another machine adds its network round trip on top.

## Model forward pass as reported by the Runtime (ms)

The x-tau-model-ms response header of the same requests: the forward-pass time measured inside the server. The gap between this and the HTTP figure is what HTTP, JSON and the host add.

| Model | q=1 p50 / p95 / p99 | q=4 p50 / p95 / p99 | q=4 per question (p50) | q=10 p50 / p95 / p99 | q=10 per question (p50) |
|---|---:|---:|---:|---:|---:|
| laya-en | 19.81 / 26.03 / 28.67 | 51.17 / 52.05 / 52.54 | 12.79 | 110.70 / 111.32 / 111.69 | 11.07 |
| laya-multilingual | 14.19 / 20.04 / 23.09 | 23.76 / 24.97 / 26.15 | 5.94 | 51.66 / 52.41 / 52.65 | 5.17 |
| laya-typed-decisions | 20.74 / 28.46 / 31.23 | 51.76 / 52.93 / 53.90 | 12.94 | 111.40 / 112.14 / 112.80 | 11.14 |
| von-1.2.0 | 17.77 / 23.36 / 25.17 | 50.09 / 50.77 / 51.07 | 12.52 | 116.75 / 117.74 / 120.15 | 11.68 |

This should match the in-process forward pass on the same provider. If it's clearly higher, something else was using the GPU or CPU during the HTTP run and the HTTP figures above should be re-measured before anyone quotes them.

## Run-to-run variation

| Measurement | Model | q | p50 repeat 1 | p50 repeat 2 | Change |
|---|---|---:|---:|---:|---:|
| HTTP end to end | laya-en | 1 | 20.86 | 20.94 | +0.38 % |
| HTTP end to end | laya-en | 4 | 52.37 | 53.05 | +1.29 % |
| HTTP end to end | laya-en | 10 | 112.24 | 113.37 | +1.00 % |
| HTTP end to end | laya-multilingual | 1 | 14.90 | 17.05 | +14.44 % |
| HTTP end to end | laya-multilingual | 4 | 24.81 | 25.22 | +1.65 % |
| HTTP end to end | laya-multilingual | 10 | 53.13 | 53.41 | +0.53 % |
| HTTP end to end | laya-typed-decisions | 1 | 21.71 | 21.45 | -1.19 % |
| HTTP end to end | laya-typed-decisions | 4 | 53.00 | 53.19 | +0.36 % |
| HTTP end to end | laya-typed-decisions | 10 | 113.10 | 114.19 | +0.97 % |
| HTTP end to end | von-1.2.0 | 1 | 18.91 | 19.19 | +1.48 % |
| HTTP end to end | von-1.2.0 | 4 | 52.34 | 52.93 | +1.12 % |
| HTTP end to end | von-1.2.0 | 10 | 121.29 | 122.93 | +1.36 % |
| Model forward pass as reported by the Runtime | laya-en | 1 | 19.81 | 20.07 | +1.27 % |
| Model forward pass as reported by the Runtime | laya-en | 4 | 51.17 | 51.89 | +1.39 % |
| Model forward pass as reported by the Runtime | laya-en | 10 | 110.70 | 111.71 | +0.91 % |
| Model forward pass as reported by the Runtime | laya-multilingual | 1 | 14.19 | 16.23 | +14.38 % |
| Model forward pass as reported by the Runtime | laya-multilingual | 4 | 23.76 | 24.10 | +1.45 % |
| Model forward pass as reported by the Runtime | laya-multilingual | 10 | 51.66 | 51.88 | +0.43 % |
| Model forward pass as reported by the Runtime | laya-typed-decisions | 1 | 20.74 | 20.50 | -1.13 % |
| Model forward pass as reported by the Runtime | laya-typed-decisions | 4 | 51.76 | 51.94 | +0.34 % |
| Model forward pass as reported by the Runtime | laya-typed-decisions | 10 | 111.40 | 112.42 | +0.91 % |
| Model forward pass as reported by the Runtime | von-1.2.0 | 1 | 17.77 | 18.02 | +1.41 % |
| Model forward pass as reported by the Runtime | von-1.2.0 | 4 | 50.09 | 50.49 | +0.79 % |
| Model forward pass as reported by the Runtime | von-1.2.0 | 10 | 116.75 | 117.97 | +1.04 % |

The two timed passes ran back to back on the same process and models. The largest p50 change between them is 14.44 %. SC-007 asks that a rerun of the command reproduces the headline figures within the variation a report records, and this is that variation. A change of more than a few percent means the machine wasn't quiet, and the figures should be re-measured before they're quoted.

## Full distributions (repeat 1)

Standard deviation is the sample standard deviation. Later repeats are in the JSON file next to this one.

### HTTP end to end

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 500 | 21.88 | 2.69 | 19.03 | 20.86 | 27.69 | 29.94 | 32.61 |
| laya-en | 4 | 500 | 52.45 | 0.66 | 51.10 | 52.37 | 53.68 | 54.18 | 55.33 |
| laya-en | 10 | 500 | 112.38 | 1.08 | 111.10 | 112.24 | 113.53 | 114.19 | 132.58 |
| laya-multilingual | 1 | 500 | 15.55 | 2.64 | 12.33 | 14.90 | 21.03 | 24.12 | 25.04 |
| laya-multilingual | 4 | 500 | 24.97 | 0.80 | 23.76 | 24.81 | 26.32 | 27.65 | 30.25 |
| laya-multilingual | 10 | 500 | 53.21 | 0.59 | 52.06 | 53.13 | 54.32 | 54.71 | 55.49 |
| laya-typed-decisions | 1 | 500 | 23.10 | 3.38 | 18.97 | 21.71 | 29.62 | 32.58 | 38.76 |
| laya-typed-decisions | 4 | 500 | 53.19 | 0.99 | 51.59 | 53.00 | 54.70 | 55.76 | 60.21 |
| laya-typed-decisions | 10 | 500 | 113.34 | 1.09 | 112.20 | 113.10 | 114.64 | 115.89 | 130.56 |
| von-1.2.0 | 1 | 500 | 19.83 | 2.12 | 17.43 | 18.91 | 24.65 | 26.17 | 29.11 |
| von-1.2.0 | 4 | 500 | 52.47 | 0.90 | 50.76 | 52.34 | 54.09 | 55.18 | 56.93 |
| von-1.2.0 | 10 | 500 | 122.17 | 4.11 | 119.40 | 121.29 | 125.37 | 137.95 | 172.65 |

### Model forward pass as reported by the Runtime

| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 500 | 20.83 | 2.50 | 18.27 | 19.81 | 26.03 | 28.67 | 30.97 |
| laya-en | 4 | 500 | 51.21 | 0.45 | 50.14 | 51.17 | 52.05 | 52.54 | 53.24 |
| laya-en | 10 | 500 | 110.77 | 0.98 | 109.57 | 110.70 | 111.32 | 111.69 | 131.19 |
| laya-multilingual | 1 | 500 | 14.80 | 2.57 | 11.82 | 14.19 | 20.04 | 23.09 | 24.21 |
| laya-multilingual | 4 | 500 | 23.89 | 0.65 | 22.96 | 23.76 | 24.97 | 26.15 | 28.79 |
| laya-multilingual | 10 | 500 | 51.70 | 0.37 | 51.06 | 51.66 | 52.41 | 52.65 | 52.95 |
| laya-typed-decisions | 1 | 500 | 22.13 | 3.25 | 18.24 | 20.74 | 28.46 | 31.23 | 35.91 |
| laya-typed-decisions | 4 | 500 | 51.88 | 0.77 | 50.66 | 51.76 | 52.93 | 53.90 | 58.44 |
| laya-typed-decisions | 10 | 500 | 111.52 | 0.85 | 110.86 | 111.40 | 112.14 | 112.80 | 127.59 |
| von-1.2.0 | 1 | 500 | 18.67 | 2.01 | 16.49 | 17.77 | 23.36 | 25.17 | 27.16 |
| von-1.2.0 | 4 | 500 | 50.13 | 0.37 | 49.27 | 50.09 | 50.77 | 51.07 | 51.38 |
| von-1.2.0 | 10 | 500 | 116.92 | 1.00 | 116.00 | 116.75 | 117.74 | 120.15 | 129.82 |

