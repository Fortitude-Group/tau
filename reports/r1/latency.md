# Tau latency

A summary of the per-run reports listed below, all measured on the reference machine at the precision stated. The per-run reports hold the full distributions, the provider checks and the run-to-run variation. Every figure is a p50 in milliseconds from repeat 1 unless a column says otherwise.

## Runs

| Report | Kind | Provider | Measured (UTC) | Commit | Timed iterations | Load before (CPU / GPU) | Largest p50 change between repeats |
|---|---|---|---|---|---:|---|---:|
| [latency-cuda.md](latency-cuda.md) | in-process | CUDA | 2026-09-27T04:22:38Z | `c26162e40284` (dirty) | 500 x 2 | 49.94 % / 0.00 % | 19.94 % |
| [latency-cpu.md](latency-cpu.md) | in-process | CPU | 2026-09-27T05:06:43Z | `c26162e40284` (dirty) | 30 x 2 | 48.77 % / 0.00 % | 17.59 % |
| [latency-http-cuda.md](latency-http-cuda.md) | http | CUDA | 2026-09-27T05:19:25Z | `c26162e40284` (dirty) | 500 x 2 | 49.66 % / 0.00 % | 14.44 % |

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
| laya-en | 1 | 21.57 | 811.20 | 37.60x | 21.57 | 811.20 |
| laya-en | 4 | 51.99 | 3013.33 | 57.96x | 13.00 | 753.33 |
| laya-en | 10 | 112.44 | 6841.55 | 60.85x | 11.24 | 684.16 |
| laya-multilingual | 1 | 18.73 | 252.89 | 13.50x | 18.73 | 252.89 |
| laya-multilingual | 4 | 24.62 | 984.23 | 39.97x | 6.16 | 246.06 |
| laya-multilingual | 10 | 53.28 | 2628.01 | 49.33x | 5.33 | 262.80 |
| laya-typed-decisions | 1 | 25.95 | 577.00 | 22.24x | 25.95 | 577.00 |
| laya-typed-decisions | 4 | 53.08 | 2615.48 | 49.28x | 13.27 | 653.87 |
| laya-typed-decisions | 10 | 113.15 | 7302.40 | 64.54x | 11.31 | 730.24 |
| von-1.2.0 | 1 | 19.75 | 512.21 | 25.93x | 19.75 | 512.21 |
| von-1.2.0 | 4 | 52.87 | 2597.83 | 49.13x | 13.22 | 649.46 |
| von-1.2.0 | 10 | 122.97 | 6827.89 | 55.52x | 12.30 | 682.79 |

This is the per-request cost for a .NET program that embeds the engine, on each provider. The ratio column says how many times slower the CPU is than the GPU for the same request. The per-question columns are the batched p50 divided by the number of questions. Compare providers at the question count you'll send most often, since the gap between them changes with q.

## Model forward pass by provider (p50 ms)

| Model | q | CUDA | CPU | CPU / CUDA | CUDA per question | CPU per question |
|---|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 21.09 | 810.20 | 38.42x | 21.09 | 810.20 |
| laya-en | 4 | 51.23 | 3012.14 | 58.80x | 12.81 | 753.03 |
| laya-en | 10 | 111.12 | 6839.91 | 61.55x | 11.11 | 683.99 |
| laya-multilingual | 1 | 18.33 | 252.43 | 13.77x | 18.33 | 252.43 |
| laya-multilingual | 4 | 23.96 | 983.54 | 41.04x | 5.99 | 245.89 |
| laya-multilingual | 10 | 52.24 | 2627.08 | 50.29x | 5.22 | 262.71 |
| laya-typed-decisions | 1 | 25.37 | 576.48 | 22.72x | 25.37 | 576.48 |
| laya-typed-decisions | 4 | 52.24 | 2614.58 | 50.05x | 13.06 | 653.65 |
| laya-typed-decisions | 10 | 111.83 | 7300.85 | 65.28x | 11.18 | 730.08 |
| von-1.2.0 | 1 | 18.84 | 510.79 | 27.11x | 18.84 | 510.79 |
| von-1.2.0 | 4 | 50.38 | 2593.71 | 51.49x | 12.59 | 648.43 |
| von-1.2.0 | 10 | 116.94 | 6820.37 | 58.33x | 11.69 | 682.04 |

This is the model alone, without Tau's tokenising and post-processing. Where the engine figure above is much larger than this, the difference is Tau's own code rather than the model.

## HTTP end to end, CUDA (p50 ms)

| Model | q | HTTP p50 | HTTP p95 | HTTP per question | Engine p50 (in-process) | Added by HTTP |
|---|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 20.86 | 27.69 | 20.86 | 21.57 | -0.72 |
| laya-en | 4 | 52.37 | 53.68 | 13.09 | 51.99 | 0.38 |
| laya-en | 10 | 112.24 | 113.53 | 11.22 | 112.44 | -0.20 |
| laya-multilingual | 1 | 14.90 | 21.03 | 14.90 | 18.73 | -3.83 |
| laya-multilingual | 4 | 24.81 | 26.32 | 6.20 | 24.62 | 0.19 |
| laya-multilingual | 10 | 53.13 | 54.32 | 5.31 | 53.28 | -0.15 |
| laya-typed-decisions | 1 | 21.71 | 29.62 | 21.71 | 25.95 | -4.24 |
| laya-typed-decisions | 4 | 53.00 | 54.70 | 13.25 | 53.08 | -0.08 |
| laya-typed-decisions | 10 | 113.10 | 114.64 | 11.31 | 113.15 | -0.05 |
| von-1.2.0 | 1 | 18.91 | 24.65 | 18.91 | 19.75 | -0.84 |
| von-1.2.0 | 4 | 52.34 | 54.09 | 13.09 | 52.87 | -0.53 |
| von-1.2.0 | 10 | 121.29 | 125.37 | 12.13 | 122.97 | -1.69 |

This is what a client on the same machine waits for one request. The last column is the HTTP p50 minus the in-process engine p50 from a separate run, so it's an estimate of what HTTP, JSON and ASP.NET add, not a direct measurement. A negative value means the overhead is smaller than the run-to-run noise between the two runs. A remote client adds its network round trip on top.

## Published figures for context (third party, not measured here)

These figures come from other people, on other hardware, with timers we haven't inspected. They aren't in any table above and aren't like-for-like with them.

- Laya's model card (convaiinnovations/laya on Hugging Face) reports 32.8 ms for a single question and 7.2 ms per question batched, on an NVIDIA T4. A T4 is a smaller, older GPU than the one above.
- For the hosted Jev endpoint, third parties report a p50 between 236 and 380 ms per request. That includes a network round trip and the hosted service's own queueing. Tau doesn't call the hosted endpoint, so we haven't measured it.

