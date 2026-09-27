# Tau latency

A summary of the per-run reports listed below, all measured on the reference machine at the precision stated. The per-run reports hold the full distributions, the provider checks and the run-to-run variation. Every figure is a p50 in milliseconds from repeat 1 unless a column says otherwise.

## Runs

| Report | Kind | Provider | Measured (UTC) | Commit | Timed iterations | Load before (CPU / GPU) | Largest p50 change between repeats |
|---|---|---|---|---|---:|---|---:|
| [latency-cuda.md](latency-cuda.md) | in-process | CUDA | 2026-09-27T02:08:27Z | `77a47a7e0eb6` (dirty) | 500 x 2 | 46.19 % / 0.00 % | 66.89 % |
| [latency-cpu.md](latency-cpu.md) | in-process | CPU | 2026-09-27T02:53:14Z | `77a47a7e0eb6` (dirty) | 30 x 2 | 39.89 % / 0.00 % | 24.67 % |
| [latency-http-cuda.md](latency-http-cuda.md) | http | CUDA | 2026-09-27T03:06:00Z | `77a47a7e0eb6` (dirty) | 500 x 2 | 44.61 % / 0.00 % | 6.76 % |

## Reference machine

- **GPU**: NVIDIA GeForce RTX 3080 Ti, 12288 MiB, driver 610.47
- **CPU**: 11th Gen Intel(R) Core(TM) i9-11900K @ 3.50GHz, 8 physical cores, 16 logical processors
- **RAM**: 63.8 GiB
- **OS**: Microsoft Windows 10 Pro 10.0.19045
- **Software**: .NET 10.0.11, ONNX Runtime 1.24.4, Tau 0.1.0
- **Contract**: `systemone/2026-09-27`
- **Precision**: fp32

## Engine end to end by provider (p50 ms)

| Model | q | CUDA | CPU | CPU / CUDA | CUDA per question | CPU per question |
|---|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 20.26 | 917.29 | 45.28x | 20.26 | 917.29 |
| laya-en | 4 | 51.95 | 3254.86 | 62.66x | 12.99 | 813.71 |
| laya-en | 10 | 112.84 | 7319.57 | 64.87x | 11.28 | 731.96 |
| laya-multilingual | 1 | 16.27 | 258.01 | 15.86x | 16.27 | 258.01 |
| laya-multilingual | 4 | 24.80 | 1046.50 | 42.19x | 6.20 | 261.63 |
| laya-multilingual | 10 | 53.07 | 2704.49 | 50.97x | 5.31 | 270.45 |
| laya-typed-decisions | 1 | 21.04 | 594.85 | 28.27x | 21.04 | 594.85 |
| laya-typed-decisions | 4 | 52.90 | 2492.69 | 47.12x | 13.23 | 623.17 |
| laya-typed-decisions | 10 | 114.45 | 7385.54 | 64.53x | 11.45 | 738.55 |
| von-1.2.0 | 1 | 19.24 | 523.95 | 27.24x | 19.24 | 523.95 |
| von-1.2.0 | 4 | 53.31 | 2615.89 | 49.07x | 13.33 | 653.97 |
| von-1.2.0 | 10 | 124.21 | 6962.20 | 56.05x | 12.42 | 696.22 |

This is the per-request cost for a .NET program that embeds the engine, on each provider. The ratio column says how many times slower the CPU is than the GPU for the same request. The per-question columns are the batched p50 divided by the number of questions. Compare providers at the question count you'll send most often, since the gap between them changes with q.

## Model forward pass by provider (p50 ms)

| Model | q | CUDA | CPU | CPU / CUDA | CUDA per question | CPU per question |
|---|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 19.68 | 916.17 | 46.55x | 19.68 | 916.17 |
| laya-en | 4 | 51.23 | 3253.72 | 63.51x | 12.81 | 813.43 |
| laya-en | 10 | 111.68 | 7317.86 | 65.53x | 11.17 | 731.79 |
| laya-multilingual | 1 | 15.93 | 257.55 | 16.16x | 15.93 | 257.55 |
| laya-multilingual | 4 | 24.20 | 1045.74 | 43.22x | 6.05 | 261.44 |
| laya-multilingual | 10 | 52.14 | 2703.14 | 51.84x | 5.21 | 270.31 |
| laya-typed-decisions | 1 | 20.55 | 594.25 | 28.92x | 20.55 | 594.25 |
| laya-typed-decisions | 4 | 52.18 | 2491.82 | 47.76x | 13.04 | 622.95 |
| laya-typed-decisions | 10 | 113.28 | 7383.67 | 65.18x | 11.33 | 738.37 |
| von-1.2.0 | 1 | 18.35 | 522.60 | 28.48x | 18.35 | 522.60 |
| von-1.2.0 | 4 | 50.85 | 2612.10 | 51.37x | 12.71 | 653.02 |
| von-1.2.0 | 10 | 118.85 | 6954.07 | 58.51x | 11.89 | 695.41 |

This is the model alone, without Tau's tokenising and post-processing. Where the engine figure above is much larger than this, the difference is Tau's own code rather than the model.

## HTTP end to end, CUDA (p50 ms)

| Model | q | HTTP p50 | HTTP p95 | HTTP per question | Engine p50 (in-process) | Added by HTTP |
|---|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 19.92 | 25.10 | 19.92 | 20.26 | -0.34 |
| laya-en | 4 | 51.93 | 53.14 | 12.98 | 51.95 | -0.01 |
| laya-en | 10 | 112.37 | 113.73 | 11.24 | 112.84 | -0.47 |
| laya-multilingual | 1 | 16.41 | 23.10 | 16.41 | 16.27 | 0.14 |
| laya-multilingual | 4 | 24.86 | 26.64 | 6.22 | 24.80 | 0.06 |
| laya-multilingual | 10 | 53.19 | 54.50 | 5.32 | 53.07 | 0.13 |
| laya-typed-decisions | 1 | 23.35 | 34.44 | 23.35 | 21.04 | 2.31 |
| laya-typed-decisions | 4 | 52.91 | 54.43 | 13.23 | 52.90 | 0.01 |
| laya-typed-decisions | 10 | 113.04 | 114.73 | 11.30 | 114.45 | -1.41 |
| von-1.2.0 | 1 | 18.85 | 25.00 | 18.85 | 19.24 | -0.38 |
| von-1.2.0 | 4 | 52.41 | 54.34 | 13.10 | 53.31 | -0.90 |
| von-1.2.0 | 10 | 121.46 | 124.03 | 12.15 | 124.21 | -2.74 |

This is what a client on the same machine waits for one request. The last column is the HTTP p50 minus the in-process engine p50 from a separate run, so it's an estimate of what HTTP, JSON and ASP.NET add, not a direct measurement. A negative value means the overhead is smaller than the run-to-run noise between the two runs. A remote client adds its network round trip on top.

## Published figures for context (third party, not measured here)

These figures come from other people, on other hardware, with timers we haven't inspected. They aren't in any table above and aren't like-for-like with them.

- Laya's model card (convaiinnovations/laya on Hugging Face) reports 32.8 ms for a single question and 7.2 ms per question batched, on an NVIDIA T4. A T4 is a smaller, older GPU than the one above.
- For the hosted Jev endpoint, third parties report a p50 between 236 and 380 ms per request. That includes a network round trip and the hosted service's own queueing. Tau doesn't call the hosted endpoint, so we haven't measured it.

