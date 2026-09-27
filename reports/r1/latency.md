# Tau latency

A summary of the per-run reports listed below, all measured on the reference machine at the precision stated. The per-run reports hold the full distributions, the provider checks and the run-to-run variation. Every figure is a p50 in milliseconds from repeat 1 unless a column says otherwise.

## Runs

| Report | Kind | Provider | Measured (UTC) | Commit | Timed iterations | Load before (CPU / GPU) | Largest p50 change between repeats |
|---|---|---|---|---|---:|---|---:|
| [latency-cuda.md](latency-cuda.md) | in-process | CUDA | 2026-09-27T16:21:42Z | `c58502cca479` (dirty) | 500 x 2 | 8.70 % / 18.67 % | 21.85 % |
| [latency-cpu.md](latency-cpu.md) | in-process | CPU | 2026-09-27T16:58:00Z | `c58502cca479` (dirty) | 30 x 2 | 9.40 % / 0.33 % | 11.52 % |
| [latency-http-cuda.md](latency-http-cuda.md) | http | CUDA | 2026-09-27T17:11:10Z | `c58502cca479` (dirty) | 500 x 2 | 4.56 % / 3.00 % | 9.22 % |

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
| laya-en | 1 | 18.59 | 529.21 | 28.48x | 18.59 | 529.21 |
| laya-en | 4 | 51.47 | 2165.70 | 42.08x | 12.87 | 541.43 |
| laya-en | 10 | 114.11 | 5616.91 | 49.22x | 11.41 | 561.69 |
| laya-multilingual | 1 | 12.34 | 233.97 | 18.97x | 12.34 | 233.97 |
| laya-multilingual | 4 | 24.39 | 905.84 | 37.14x | 6.10 | 226.46 |
| laya-multilingual | 10 | 53.09 | 2527.83 | 47.61x | 5.31 | 252.78 |
| laya-typed-decisions | 1 | 19.45 | 576.39 | 29.64x | 19.45 | 576.39 |
| laya-typed-decisions | 4 | 52.27 | 2200.07 | 42.09x | 13.07 | 550.02 |
| laya-typed-decisions | 10 | 127.97 | 5827.77 | 45.54x | 12.80 | 582.78 |
| von-1.2.0 | 1 | 17.67 | 399.72 | 22.62x | 17.67 | 399.72 |
| von-1.2.0 | 4 | 53.68 | 2074.73 | 38.65x | 13.42 | 518.68 |
| von-1.2.0 | 10 | 129.36 | 5576.43 | 43.11x | 12.94 | 557.64 |

This is the per-request cost for a .NET program that embeds the engine, on each provider. The ratio column says how many times slower the CPU is than the GPU for the same request. The per-question columns are the batched p50 divided by the number of questions. Compare providers at the question count you'll send most often, since the gap between them changes with q.

## Model forward pass by provider (p50 ms)

| Model | q | CUDA | CPU | CPU / CUDA | CUDA per question | CPU per question |
|---|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 18.26 | 528.44 | 28.94x | 18.26 | 528.44 |
| laya-en | 4 | 50.98 | 2164.74 | 42.46x | 12.75 | 541.19 |
| laya-en | 10 | 113.32 | 5615.65 | 49.56x | 11.33 | 561.56 |
| laya-multilingual | 1 | 12.09 | 233.48 | 19.32x | 12.09 | 233.48 |
| laya-multilingual | 4 | 23.96 | 905.25 | 37.77x | 5.99 | 226.31 |
| laya-multilingual | 10 | 52.45 | 2526.84 | 48.18x | 5.24 | 252.68 |
| laya-typed-decisions | 1 | 19.11 | 575.86 | 30.13x | 19.11 | 575.86 |
| laya-typed-decisions | 4 | 51.78 | 2199.33 | 42.48x | 12.94 | 549.83 |
| laya-typed-decisions | 10 | 127.16 | 5826.61 | 45.82x | 12.72 | 582.66 |
| von-1.2.0 | 1 | 17.06 | 398.85 | 23.38x | 17.06 | 398.85 |
| von-1.2.0 | 4 | 52.19 | 2072.44 | 39.71x | 13.05 | 518.11 |
| von-1.2.0 | 10 | 126.35 | 5571.17 | 44.09x | 12.63 | 557.12 |

This is the model alone, without Tau's tokenising and post-processing. Where the engine figure above is much larger than this, the difference is Tau's own code rather than the model.

## HTTP end to end, CUDA (p50 ms)

| Model | q | HTTP p50 | HTTP p95 | HTTP per question | Engine p50 (in-process) | Added by HTTP |
|---|---:|---:|---:|---:|---:|---:|
| laya-en | 1 | 19.48 | 21.92 | 19.48 | 18.59 | 0.89 |
| laya-en | 4 | 51.84 | 53.87 | 12.96 | 51.47 | 0.37 |
| laya-en | 10 | 113.86 | 117.94 | 11.39 | 114.11 | -0.25 |
| laya-multilingual | 1 | 12.43 | 13.39 | 12.43 | 12.34 | 0.09 |
| laya-multilingual | 4 | 24.07 | 24.90 | 6.02 | 24.39 | -0.31 |
| laya-multilingual | 10 | 54.70 | 57.61 | 5.47 | 53.09 | 1.61 |
| laya-typed-decisions | 1 | 20.14 | 25.87 | 20.14 | 19.45 | 0.69 |
| laya-typed-decisions | 4 | 52.95 | 55.82 | 13.24 | 52.27 | 0.67 |
| laya-typed-decisions | 10 | 123.37 | 156.98 | 12.34 | 127.97 | -4.61 |
| von-1.2.0 | 1 | 18.94 | 25.47 | 18.94 | 17.67 | 1.27 |
| von-1.2.0 | 4 | 55.84 | 75.84 | 13.96 | 53.68 | 2.16 |
| von-1.2.0 | 10 | 126.34 | 151.06 | 12.63 | 129.36 | -3.02 |

This is what a client on the same machine waits for one request. The last column is the HTTP p50 minus the in-process engine p50 from a separate run, so it's an estimate of what HTTP, JSON and ASP.NET add, not a direct measurement. A negative value means the overhead is smaller than the run-to-run noise between the two runs. A remote client adds its network round trip on top.

## Published figures for context (third party, not measured here)

These figures come from other people, on other hardware, with timers we haven't inspected. They aren't in any table above and aren't like-for-like with them.

- Laya's model card (convaiinnovations/laya on Hugging Face) reports 32.8 ms for a single question and 7.2 ms per question batched, on an NVIDIA T4. A T4 is a smaller, older GPU than the one above.
- For the hosted Jev endpoint, third parties report a p50 between 236 and 380 ms per request. That includes a network round trip and the hosted service's own queueing. Tau doesn't call the hosted endpoint, so we haven't measured it.

