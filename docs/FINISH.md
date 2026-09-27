# Tau: finish

Written 27 September 2026 at the end of R3. Update, 27 September 2026 (evening): the repo is **public** at `Fortitude-Group/tau` with rewritten history (the full history stays private in `Fortitude-Group/tau-dev`). The ONNX packages are on the `models-v1` release. The R&D page is live at https://fortitude-omnis.group/rd/tau/. Nothing else is published: no NuGet packages, no Docker image and no posts.

## What shipped

- **Tau Runtime.** A .NET 10 server that answers the `/v1/systemone` contract locally with Laya (English, multilingual, typed decisions) and Von 1.2.0 through ONNX Runtime, on CUDA, DirectML or CPU. It has calibrator loading, raw outputs behind `x-tau-raw`, OpenTelemetry, a single-file publish and a Docker image.
- **Tau.Client** and **Tau.Contract.** Typed C# client packages. They pack locally, and nothing has been pushed.
- **Tau Workbench** (`tau` global tool). Label, measure, calibrate, threshold, cascade, report and run. It works against any `/v1/systemone` endpoint and writes one self-contained HTML report per example.
- **Fine-tune sidecar** (Python, pinned). Data prep, Laya fine-tune, MiniLM baseline, ONNX export, parity and the frontier answer-sheet tooling.
- **Two worked examples** with committed reports, calibrators, cached frontier answers and per-item results: [Banking77](../examples/banking77/report.html) and [support tickets](../examples/support-tickets/report.html).
- **Launch drafts** in `docs/articles/`:
  - the canonical piece
  - HN, DEV.to, Reddit (three variants) and LinkedIn
  - the APEX R&D page
  - five report screenshots.
- **Public readiness:**
  - README, THIRD-PARTY-NOTICES.md and an extended NOTICE
  - a secret scan (clean)
  - `scripts/check-articles.ps1`, which proves every article number against its report.

## Headline numbers

All measured on one RTX 3080 Ti (12 GB) and i9-11900K, FP32, on 27 September 2026. £ figures are list-price estimates. The frontier answers came from Claude Opus 5.5 in an interactive Claude Code session working through batched answer sheets, not from the API.

- **Cascade, Banking77:** the fine-tuned Laya [keeps 73.6% of decisions local at 93.2% against 94.2% for Claude alone, for an estimated £1,031 against £3,898 per million decisions](../examples/banking77/report.json).
- **Classic baseline:** a fine-tuned MiniLM [keeps 94.7% local at 94.2% blended accuracy](../examples/banking77/report.json) and [scores 91.5% held-out](../examples/banking77/report.json).
- **Calibration:**
  - Banking77: out-of-the-box laya-en [ECE 0.502 to 0.065](../examples/banking77/report.json), Von [0.185 to 0.039](../examples/banking77/report.json).
  - Tickets: laya-typed-decisions [0.269 to 0.067](../examples/support-tickets/report.json).
- **Label noise:**
  - Claude [disagrees with the Banking77 labels on 5.8%](../examples/banking77/report.json).
  - Claude [agrees with the synthetic ticket labels on 23.8%](../examples/support-tickets/report.json), against [41.0% for always picking the most common label](../examples/support-tickets/report.json).
- **Tickets cascade:** [every model keeps about 0.1% local](../examples/support-tickets/report.json) when scored against Claude.
- **Latency:** laya-en on CUDA [18.59 ms for one question, 114.11 ms for ten](../reports/r1/latency.md). CPU [529.21 ms for one](../reports/r1/latency.md).
- **ONNX parity:** [pass on every gate](../reports/r1/parity.md) for all four models, and for [both fine-tunes](../examples/banking77/finetune-parity.json).
- **Conformance:** Tau answers [all 45 requests without a contract violation](../reports/r1/conformance.md), diffed against Kev-0.8B.

## Brainstorm finish line

| Criterion | Status | Evidence |
| --- | --- | --- |
| Runtime answers the full contract, conformance passes against Kev (and Jev if a key is available) | Met, Jev not run | [conformance report](../reports/r1/conformance.md): Tau 45 of 45. No Jev key was bought, by decision |
| ONNX matches the PyTorch reference within the agreed tolerance on every case | Met | [parity report](../reports/r1/parity.md), plus the fine-tune parity for [Banking77](../examples/banking77/finetune-parity.json) and [tickets](../examples/support-tickets/finetune-parity.json) |
| Latency measured on Rob's hardware, single and batched, with specs | Met | [latency summary](../reports/r1/latency.md) and the per-provider reports |
| ECE before and after on both datasets, after materially lower | Met, one explained miss | Banking77 laya-en 87% lower. Tickets laya-typed-decisions 75% lower, laya-en 46% (explained in the report) |
| Cascade reports share kept local, blended accuracy against Claude-only, £ per million | Met | Both reports' cascade and cost tables |
| Every number traces to a committed report, one command reproduces each | Met | `scripts/check-articles.ps1`: 0 problems over all six drafts. `scripts/examples.ps1 -Example <name>` rebuilds each report |
| Repo, NuGet package and Docker image ready to go public, README under 10 minutes | Met, with caveats | README walkthrough: 117 s excluding downloads (PROGRESS, R3 log). Packages and image built and tested locally. Caveats under Known gaps |
| Articles drafted for LinkedIn, DEV.to, Reddit and HN, Rob approves before publishing | Drafted, approval pending | `docs/articles/`. Approval is yours |

## Known gaps

- **Jev never measured.** No key was bought, so there's no conformance or head-to-head run against the hosted service.
- **Newcomers now download the exported models** from the `models-v1` release with `scripts/fetch-onnx.ps1` (hash-pinned). The support-tickets fine-tune isn't on the release, because it was trained on non-commercial data.
- **Frontier labels are session-produced.** They came from batched sheets, not independent per-item API calls. Every report says so. A per-item API run would cost real money and was out of scope.
- **The ticket dataset is synthetic and non-commercial** (CC-BY-NC-4.0). Its labels are close to arbitrary. Treat the tickets fine-tune as non-commercial as well.
- **The laya-en tickets calibration missed the 50% target.** It was explained, and the rule wasn't changed after the fact.
- **FP16, WebGPU, numeric interval decoding and the prompt-injection harness** are all out of scope, as the brainstorm planned.
- **Decision models weren't measured where they should shine,** on untrained questions or many questions against one state. The articles say so.
- **JsonSchema.Net** (tests and tools only) ships its binaries under a maintenance-fee EULA for revenue-generating users over US$10k a year. It isn't shipped, but decide whether you're happy using it.
- **The R1 latency reports say "(dirty)".** Uncommitted Workbench edits were in the tree during the run, but none of the measured projects references them (DECISIONS).
- **Session usage for frontier labelling** was about 1.33M tokens, against a 0.85–1.0M estimate. That was Max usage, with no API spend.

## What you need to do to go public, in order

1. **Read and edit the drafts** in `docs/articles/`. Approve or change the titles, especially the canonical title and the HN title.
2. **Decide on JsonSchema.Net.** Keep it (tests only) or swap it for another JSON Schema validator before going public.
3. **Done 2026-09-27. Make the repo public:** `gh repo edit Fortitude-Group/tau --visibility public --accept-visibility-change-consequences`, run as `fortitude-omnis`. After that, the relative links in the drafts need turning into `https://github.com/Fortitude-Group/tau/blob/master/...` URLs (a search and replace on `../../`).
4. **Done 2026-09-27. Optionally publish the exported ONNX models** as a GitHub release, and point the README's quickstart at it.
5. **Push the packages** if you want them on NuGet. First run `dotnet pack` for `src/Tau.Contract`, `src/Tau.Client` and `src/Tau.Workbench.Cli`, then `dotnet nuget push artifacts/packages/*.nupkg --source nuget.org --api-key <key>`. Push Tau.Contract before Tau.Client, and set the key with `setx` as usual, never in chat.
6. **Push the Docker image** if you want it public. Tag `tau-runtime:local` for your registry, then push. It's a CPU image. Mention NVIDIA's terms if you ever ship a CUDA image.
7. **Done 2026-09-27. Build the R&D page** from `docs/articles/apex-page.md`, using the web playbook's R&D generator at `/rd/tau/`:
   - copy `docs/articles/img/*.png` to `/images/rd/tau-*.png`
   - add the `rd/projects.json` entry from the draft's front matter
   - deploy with the playbook's process.
8. **Post, in this order:**
   - the canonical piece on the R&D page
   - HN (Show HN), and stay around to answer comments
   - DEV.to with the canonical URL set
   - r/LocalLLaMA, then r/dotnet a day or two later, then r/MachineLearning only if its rules allow a [P] post that day
   - LinkedIn from the Fortitude Omnis page.
   - Check each subreddit's self-promotion rules on the day.
