# Tau: finish

Current as of 28 September 2026.

**Where things stand:**
- **Repo:** public at [Fortitude-Group/tau](https://github.com/Fortitude-Group/tau). Its history was rewritten to remove personal planning notes, and the full history stays private in `Fortitude-Group/tau-dev`. Report hashes from before the rewrite map to new ones in [commit-map.txt](commit-map.txt).
- **Exported ONNX models:** on the repo's `models-v1` release.
- **R&D page:** live at https://fortitude-omnis.group/rd/tau/, with both HTML reports hosted beside it.
- **Measured:** the local models, a classic baseline, Claude as the frontier model and TypeSafe's hosted Jev, all on the same items.
- **Not published:** no NuGet packages, no Docker image and no posts. The drafts are ready and waiting for your approval.

## What shipped

- **Tau Runtime.** A .NET 10 server that answers the `/v1/systemone` contract locally with Laya (English, multilingual, typed decisions) and Von 1.2.0 through ONNX Runtime, on CUDA, DirectML or CPU. It has calibrator loading, raw outputs behind `x-tau-raw`, full-precision probabilities behind `x-tau-precision: full`, OpenTelemetry, a single-file publish and a Docker image.
- **Tau.Client** and **Tau.Contract.** Typed C# client packages. They pack locally, and nothing has been pushed.
- **Conformance against Jev.** The suite takes `--peer-api-key-env` for a hosted peer, so the key stays in the environment. Report in [reports/jev](../reports/jev/conformance.md).: label, measure, calibrate, threshold, cascade, report and run.
  - It works against any `/v1/systemone` endpoint, local or hosted, and writes one self-contained HTML report per example.
  - Hosted endpoints go under `external:` in the spec. The key is read from an environment variable at run time, and a budget guard stops a run before it overspends.
  - Calibrators are fitted on full-precision probabilities where the endpoint offers them.
  - Each report has one combined cost table: Claude alone, then each model answering first with the rest escalated.
- **Jev measured.** TypeSafe's hosted `jev-1.13.0` went through both examples on the same items, 2,000 calls per dataset.
  - **Spend:** an estimated [$0.14 for a Banking77 run](../examples/banking77/report.json) and [$0.04 for a tickets run](../examples/support-tickets/report.json). Banking77 was run twice, so the total across both runs and the first smoke call is about $0.32.
  - **Checked against the bill:** Rob confirmed on the TypeSafe console on 28 September that the total spend was $0.32, which matches the estimate. The estimates price input tokens at the published $0.042 per million and output tokens at nothing, and the bill agrees, so output tokens aren't charged.
- **Exported models to download.** `scripts/fetch-onnx.ps1` pulls the hash-pinned ONNX packages from the `models-v1` release, so a newcomer needs no Python export environment. The support-tickets fine-tune isn't on the release, because it was trained on non-commercial data.
- **Fine-tune sidecar** (Python, pinned). Data prep, Laya fine-tune, MiniLM baseline, ONNX export, parity and the frontier answer-sheet tooling.
- **Two worked examples** with committed reports, calibrators, cached frontier answers and per-item results. The reports are hosted as pages: [Banking77](https://fortitude-omnis.group/rd/files/tau/banking77.html) and [support tickets](https://fortitude-omnis.group/rd/files/tau/support-tickets.html).
- **Launch drafts** in `docs/articles/`:
  - the canonical piece, which is also the live R&D page
  - HN and LinkedIn, as plain text with URLs
  - DEV.to and Reddit (three variants), with absolute links
  - the APEX page source
  - five report screenshots, also on the site.
- **Public readiness:**
  - README, THIRD-PARTY-NOTICES.md and an extended NOTICE
  - a secret scan of the rewritten history (clean)
  - `scripts/check-articles.ps1`, which proves every article number against its report.

## Headline numbers

All measured on one RTX 3080 Ti (12 GB) and i9-11900K, FP32, on 27 and 28 September 2026. £ figures are list-price estimates. The frontier answers came from Claude Opus 5.5 in an interactive Claude Code session working through batched answer sheets, not from the API.

- **Cascade, Banking77:** the fine-tuned Laya [keeps 73.0% of decisions local at 93.3% against 94.2% for Claude alone, for an estimated £1,054 against £3,898 per million decisions](../examples/banking77/report.json). Von [keeps 32.6%](../examples/banking77/report.json).
- **Classic baseline:** a fine-tuned MiniLM [keeps 94.7% local at 94.2% blended accuracy](../examples/banking77/report.json) and [scores 91.5% held-out](../examples/banking77/report.json).
- **Calibration:**
  - Banking77: out-of-the-box laya-en [ECE 0.502 to 0.056](../examples/banking77/report.json), Von [0.185 to 0.021](../examples/banking77/report.json).
  - Tickets: laya-typed-decisions [0.270 to 0.064](../examples/support-tickets/report.json).
- **Label noise:**
  - Claude [disagrees with the Banking77 labels on 5.8%](../examples/banking77/report.json).
  - Claude [agrees with the synthetic ticket labels on 23.8%](../examples/support-tickets/report.json), against [41.0% for always picking the most common label](../examples/support-tickets/report.json).
- **Tickets cascade:** scored against Claude, [no local decision model keeps more than 0.1%](../examples/support-tickets/report.json).
- **Jev (hosted):**
  - Banking77: [79.2% held-out out of the box, ECE 0.093, 0.029 after offline calibration](../examples/banking77/report.json). As the first stage it [keeps 51.3% at 93.6% blended, £1,952 against £3,898 per million](../examples/banking77/report.json).
  - Tickets: [52.5% agreement with Claude out of the box](../examples/support-tickets/report.json), and the only non-frontier model with a real share: [39.8% kept at 89.3%, £614 against £997 per million](../examples/support-tickets/report.json).
  - [p50 223 ms](../examples/banking77/report.json) over the network, not comparable with local inference.
- **Latency:** laya-en on CUDA [18.59 ms for one question, 114.11 ms for ten](../reports/r1/latency.md). CPU [529.21 ms for one](../reports/r1/latency.md).
- **ONNX parity:** [pass on every gate](../reports/r1/parity.md) for all four models, and for [both fine-tunes](../examples/banking77/finetune-parity.json).
- **Conformance:** Tau answers [all 45 requests without a contract violation](../reports/r1/conformance.md), diffed against Kev-0.8B, and [all 45 again with Jev as the peer](../reports/jev/conformance.md). Jev passes 35: three misses are Tau-only fixtures, and seven depart from TypeSafe's own published reference (400 where it documents 422, and two invalid requests accepted).

## Brainstorm finish line

| Criterion | Status | Evidence |
| --- | --- | --- |
| Runtime answers the full contract, conformance passes against Kev (and Jev if a key is available) | Met against both | Tau passes 45 of 45 [against Kev-0.8B](../reports/r1/conformance.md) and 45 of 45 [with Jev as the peer](../reports/jev/conformance.md) (28 September). Jev itself passes 35. Three of its misses are Tau-only fixtures (a null state and the `auto` and `laya-en` model names). The other seven are Jev departing from its own published reference: 400 instead of 422 for five invalid requests, and accepting a one-level score and a noul `maybe` key |
| ONNX matches the PyTorch reference within the agreed tolerance on every case | Met | [parity report](../reports/r1/parity.md), plus the fine-tune parity for [Banking77](../examples/banking77/finetune-parity.json) and [tickets](../examples/support-tickets/finetune-parity.json) |
| Latency measured on Rob's hardware, single and batched, with specs | Met | [latency summary](../reports/r1/latency.md) and the per-provider reports |
| ECE before and after on both datasets, after materially lower | Met, one explained miss | Banking77 laya-en 88.7% lower. Tickets laya-typed-decisions 76.3% lower, laya-en 43.4% (explained in the report) |
| Cascade reports share kept local, blended accuracy against Claude-only, £ per million | Met | Both reports' cascade table and combined cost table, Jev included |
| Every number traces to a committed report, one command reproduces each | Met | `scripts/check-articles.ps1`: 0 problems over all six drafts. `scripts/examples.ps1 -Example <name>` rebuilds each report, and re-measuring Jev needs `TYPESAFE_API_KEY` |
| Repo, NuGet package and Docker image ready to go public, README under 10 minutes | Repo public. Packages and image ready but not pushed | README walkthrough: 117 s excluding downloads (PROGRESS, R3 log). The quickstart now downloads the exported models from the release. Packages and image built and tested locally |
| Articles drafted for LinkedIn, DEV.to, Reddit and HN, Rob approves before publishing | Drafted and link-checked, approval pending | `docs/articles/`. Every URL resolves. Approval is yours |

## Known gaps

- **Tau and Jev answer invalid requests with different status codes.** Tau follows TypeSafe's published reference (422 for a failed validation), and Jev returns 400 for five of those cases. It also accepts a one-level score and a noul `maybe` key that the reference rules out. A client written against Jev's real behaviour rather than its docs would see different codes from Tau. Matching Jev instead of the reference is a one-line decision per case, and I've left it as the reference says.
- **VRAM pressure with three models resident.** In the final runs the fine-tuned Laya, measured last in a Runtime holding three FP32 models, slowed to [a 15,219 ms median](../examples/banking77/runs/laya-en-ft-banking77/heldout.raw.summary.json) in its raw phase, against [243 ms](../examples/banking77/runs/laya-en-ft-banking77/heldout.calibrated.summary.json) in a fresh Runtime.
  - Accuracy is unaffected, and the published latency uses the clean phase.
  - A real deployment on a 12 GB card should serve fewer models or use FP16.
  - The cause (VRAM spill) isn't proven.
- **Frontier labels are session-produced.** They came from batched sheets, not independent per-item API calls. Every report says so. A per-item API run would cost real money and was out of scope.
- **The ticket dataset is synthetic and non-commercial** (CC-BY-NC-4.0). Its labels are close to arbitrary. Treat the tickets fine-tune as non-commercial as well.
- **The laya-en tickets calibration missed the 50% target.** It was explained, and the rule wasn't changed after the fact.
- **FP16, WebGPU, numeric interval decoding and the prompt-injection harness** are all out of scope, as the brainstorm planned.
- **Decision models weren't measured where they should shine,** on untrained questions or many questions against one state. The articles say so.
- **JsonSchema.Net** (tests and tools only) ships its binaries under a maintenance-fee EULA for revenue-generating users over US$10k a year. It isn't shipped, but decide whether you're happy using it.
- **The R1 latency reports say "(dirty)".** Uncommitted Workbench edits were in the tree during the run, but none of the measured projects references them (DECISIONS).
- **Session usage for frontier labelling** was about 1.33M tokens, against a 0.85–1.0M estimate. That was Max usage, with no API spend.

## What you need to do to go public

Done already:
- **Repo made public** (27 September), with rewritten history. The drafts' links have been turned into live GitHub and site URLs.
- **Exported ONNX models published** as the `models-v1` release (27 September), with the README quickstart pointing at them.
- **R&D page built and deployed** at `/rd/tau/`, each change after you approved its diff:
  - the first version (27 September)
  - the hosted reports
  - the final numbers with Jev (28 September)
  - the combined cost table (28 September).

Still to do, in order:
1. **Read and edit the drafts** in `docs/articles/`. Approve or change the titles, especially the HN title.
2. **Decide on JsonSchema.Net.** Keep it (tests only) or swap it for another JSON Schema validator.
3. **Push the packages** if you want them on NuGet. First run `dotnet pack` for `src/Tau.Contract`, `src/Tau.Client` and `src/Tau.Workbench.Cli`, then `dotnet nuget push artifacts/packages/*.nupkg --source nuget.org --api-key <key>`. Push Tau.Contract before Tau.Client, and set the key with `setx` as usual, never in chat.
4. **Push the Docker image** if you want it public. Tag `tau-runtime:local` for your registry, then push. It's a CPU image. Mention NVIDIA's terms if you ever ship a CUDA image.
5. **Post, in this order:**
   - HN (Show HN), and stay around to answer comments. The canonical piece is already live on the R&D page.
   - DEV.to: an unpublished draft is already in your dashboard (created 28 September from `docs/articles/devto.md`, canonical URL set to https://fortitude-omnis.group/rd/tau/). Review the preview and press Publish.
   - r/LocalLLaMA, then r/dotnet a day or two later, then r/MachineLearning only if its rules allow a [P] post that day. Check each subreddit's self-promotion rules on the day.
   - LinkedIn, from the Fortitude Omnis page.
