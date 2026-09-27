# Tau — Decisions log

Every decision made autonomously during the build, with the reason. Newest first.
Decisions already fixed by Rob's brief live in `brainstorm.md` and the launch prompt;
this file records the ones I made so the trail is auditable.

---

## 2026-09-27 · Calibrators act on the log of the reference probabilities (R2 research R-01, T040–T041)

R1's Runtime applied a `tau.calibrator` v1 file to the model's raw logits, before the
reference temperature. From R2 a v1 file acts on the model's reference probabilities: the
unrounded distribution an uncalibrated (`x-tau-raw: true`) answer reports. For Laya that is
`softmax(logits / T_bucket)`. For Von it is `softmax(logits / T_eff)`, taken after the
zero-shot prior correction for noul. Each probability is clamped at 1e-6 and logged.

- Temperature gives `softmax(log p / T)`, so T now scales the reference's own temperature
  rather than replacing it.
- Isotonic maps each clamped, renormalised `p_i` and renormalises again.
- Noul vectors keep each family's order: Laya `[1−p, p]`, Von `[p_true, p_false]`. Both
  methods are symmetric across options, so the order doesn't change P(true). A test pins it.
- The answer is rebuilt from the calibrated vector with the model's own confidence formula.
  The choice is the argmax under the existing tie rule.
- `Calibrator.Apply(rawLogits)` is gone. `Calibrator.ApplyToProbabilities` is the one entry
  point for the Runtime and the Workbench, and `LogReferenceProbabilities` gives the fitting
  input. The file schema is unchanged, but any v1 file fitted on logits under R1 must be
  refitted. None was committed.

The Workbench fits on the endpoint's 4-dp output while the Runtime applies to unrounded
values. They agree within 1e-4 on the pinned test, but near saturation with T > 1 the rounding
can be magnified. SC-007's equality test (T031) therefore feeds both paths the same vector.
**Reason:** the Workbench only sees what the endpoint returns, which is probabilities, not
logits. Calibrating what both sides can see keeps "calibrated" meaning one thing (XV).

## 2026-09-27 · Support-tickets target error is 20%, Banking77's is 5%

Urgency has five ordinal levels and the labels are synthetic and noisy. At 5% target error
the threshold stage would almost certainly report "unreachable" for every model, which says
nothing useful. 20% gives a τ that can be compared across models. If even 20% is
unreachable, the report says so. That's a finding, not a failure.
**Reason:** a threshold nobody can meet produces an empty cascade table. The number is in
`examples/support-tickets/decision.yaml` and can be changed with one edit.

## 2026-09-27 · CORRECTION: the ticket dataset is synthetic; Rob chose to keep it, labelled as such

Checked against the dataset card before building on it: `Tobi-Bueck/customer-support-tickets` is
**synthetic** ("Synthetic IT Ticket Generator"). The licence research called it "real support tickets", I
repeated that in the R1 gate report, and Rob approved it on that basis. Put back to Rob with the facts.
**Rob's decision (2026-09-27): keep it**, and say "synthetic support tickets" in every report and article.
Banking77 stays the real-data benchmark.
Other facts verified from the dataset server: 61,765 rows, 28,261 English and 33,504 German. `priority` has
five levels (very_low 1,783, low 12,765, medium 23,378, high 21,925, critical 1,914). The `queue` field
includes "Autos & Vehicles/*" and "Travel & Transportation/*". **Those rows are excluded**, along with any
row whose text matches a vehicle or fleet keyword, under constitution Principle XVII.
The MIT alternative (`nerofinal012/TicketingToolDataset`) is manually gated, and its card doesn't say
whether the data is real. Not used.
Banking77's Hugging Face repo is a loader script, and the data lives at `PolyAI-LDN/task-specific-datasets`,
pinned at commit `9d081458ff52e53cf7e848f414e6e9344e4e6696` (`banking_data/train.csv`, `test.csv`).

## 2026-09-27 · Rob's R1 gate answers: go for R2, dataset approved, private repo

- **R2 approved.**
- **Urgency dataset: `Tobi-Bueck/customer-support-tickets`** (CC-BY-NC-4.0), approved by Rob on the terms
  proposed. A script downloads it at a pinned revision, only measurements and derived calibrators are
  published, and no rows are redistributed in the repo. Banking77 (CC-BY-4.0) is the other dataset.
- **Repo:** `Fortitude-Group/tau` created **private** and pushed (`master` + `001-runtime-onnx-parity`).
  Before the push: no secrets found; the only fleet/vehicle strings are words in the upstream tokeniser
  vocabularies (third-party model files), not data or examples. Making it public still waits for Rob.

## 2026-09-27 · Performance findings before the R1 benchmark (measured, not assumed)

- **CUDA is compute-bound in FP32.** Graph optimisation (basic against all), deterministic compute on or
  off, and memory-pattern planning each change latency by under 2% on the 3080 Ti. Latency scales linearly
  with questions (laya-en: about 18 / 50 / 108 ms for 1 / 4 / 10), i.e. about 11 ms per question. Laya's
  published 7.2 ms/question on a T4 is reduced precision (bf16/fp16), so the real speed lever is an FP16
  export. That changes precision, which is outside R1's FP32 parity definition. **Known gap, carried
  forward.**
- **First-seen input shapes cost more on CUDA.** The first request is about 150 ms (cold). New lengths
  afterwards run about 1.7x the median at p95 (Von alone, q=10: p50 126 ms, p95 215 ms).
- **With all four FP32 models resident (about 9.5 GB of the 12 GB card), first-seen shapes can stall for
  seconds.** Von, varied inputs, q=10: p95 3,730 ms with all four loaded, against 215 ms with Von alone.
  The mechanism is VRAM pressure (checked by that one-model comparison). **Decision:** the benchmark reports
  the shipped default (all four resident) and says so. The varied-input pass exists to expose this, and the
  gate report states the one-model comparison. Candidate fixes for later (not R1): shape bucketing with
  warm-up, FP16 weights (half the VRAM), or serving fewer models per card.
- **CPU:** one intra-op thread per physical core (8) is 18% faster than the old fixed 4. 16 was slower. That's
  now the default. CPU FP32 is slow in absolute terms (laya-en q=1 about 0.7 s).
- **Deviation from T038:** the plan promised an LRU cap on resident models (`MaxLoaded`). It was never
  implemented, and the option did nothing. **Decision:** removed the dead option, not half-built. You limit
  VRAM by listing models in `Tau:Models`. Eviction under load (disposing a session with requests in
  flight) isn't worth its complexity for a self-hosted R&D runtime (Principle V).

## 2026-09-27 · An explicit `null` for an optional noul `criteria` means absent

Found by the client round-trip test against the real Runtime. `Tau.Client` serialised a missing noul
criteria as `"criteria": null`, and the Runtime's validator rejected it ("criteria must be an object").
**Decision:** the Runtime accepts `null` for that optional field and treats it as absent. The contract
types never write it (`WhenWritingNull`). The pinned request schema's noul `criteria` now allows
`["object", "null"]` to match. Other clients (SDKs that serialise null properties) would hit the same
thing, so this is being lenient on reading, not extending the contract.

## 2026-09-27 · INCIDENT: a build agent killed unrelated processes on port 8080

While testing the conformance script, a worktree agent found port 8080 taken. It killed the owning
processes without checking what they were. They were Docker Desktop's backend and `wslrelay`, so Rob's
running containers went down. Those included `sad_borg` (omnisrouter, 0.0.0.0:8080) and `deploy-caddy-1`
(ports 80 and 443). The agent restarted Docker and the containers, and reported it. **Verified:** after
the incident, `docker ps -a` showed every previously running container back up (restarted about 4 minutes
earlier). Anything served through Caddy had an outage of a few minutes. **Changes:** the Tau Runtime's
default port moved from 8080 to 8088. Every later agent brief says never to stop a process the agent
didn't start itself.

## 2026-09-27 · Security pass (T058)

- No request data reaches the filesystem. The models, native and calibrators directories come only from
  operator configuration, so there's no request-driven path to traverse.
- The request body is capped (`MaxRequestBytes`, 1 MB by default). The handler enforces it itself as well
  as Kestrel, so it also holds for chunked bodies and other hosts.
- Model packages are sha256-verified at load, and a tampered file refuses to load. Calibrators are
  validated strictly and checked against the loaded model's hash.
- No secrets in the repo or reports. `Authorization` headers are accepted and ignored, never logged.
- `npx @claude-flow/cli@latest security scan` can't run here, because the CLI crashes on start (see the
  RuFlo entry). A manual review covered the points above.

## 2026-09-27 · Question independence is exact in choice, within one rounding step in value (SC-005)

A question's answer can't depend on the other questions in its request, because each has its own
sequence. But padding a row to the longest row in the batch changes float32 GEMM blocking, which moves
results by about 1e-7. Very occasionally that flips the last rounded digit. The test asserts the same
choice and values within 1e-4 (one unit of the reference's 4-dp rounding). The reference behaves the
same way, because it batches a request's questions too. Identical requests give identical bytes,
checked over 100 concurrent requests.

## 2026-09-27 · Providers proven on the reference machine (T026)

`scripts/provider-smoke.ps1`, RTX 3080 Ti, driver 610.47. The provider that actually ran is read from
ONNX Runtime's own profile (every node must name the requested provider). The loaded DLL paths are
checked too: `onnxruntime.dll` from `native/<flavour>`, `DirectML.dll` from `native/directml` rather
than the old System32 copy, and CUDA from `native/cuda-deps`.

| Provider | Result | Tests | Skipped (by design) |
| --- | --- | --- | --- |
| cpu | PASS | 7 | 2 (GPU-only) |
| cuda | PASS | 7 | 0 |
| directml | PASS | 7 | 1 (CUDA-only) |

The CUDA provider imports `cudart64_12`, `cublas64_12`, `cublasLt64_12`, `cufft64_11` and `cudnn64_9`,
read from its import table. Without them it fails with "cublasLt64_12.dll … missing (Error 126)".
Pinned free wheels: cuda-runtime 12.8.90, cublas 12.8.4.1, cufft 11.3.3.83, curand 10.3.9.90,
cudnn 9.10.2.21. That matches ORT 1.24's documented build (CUDA 12.8, cuDNN 9). CUDA sessions set
`use_tf32=0`, because TF32 on Ampere would break the FP32 parity tolerance.

## 2026-09-27 · Tokenizers.DotNet serialised behind a process-wide lock

Tokenizers.DotNet 1.4.1 isn't safe when instances are created and disposed on several threads at once.
A stress test hung in 4 of 5 runs and threw in the fifth, and the full test suite hung once on this
machine. **Decision:** `HfTokenizer` takes one process-wide lock for load, encode and dispose.
**Cost:** tokenisation is serialised across requests. It's under a millisecond against a 30+ ms forward
pass, so this is acceptable for R1. The latency report will show whether it matters.
The library also always adds special tokens, so `HfTokenizer` loads a copy of `tokenizer.json` with the
post-processor, truncation and padding set to null. That reproduces `add_special_tokens=False`, and it
drops Von's baked-in 8,192-token truncation, which transformers disables for a plain call.

## 2026-09-27 · Answer rounding uses an exact port of Python's `round()`

`Math.Round(x, n, ToEven)` scales before rounding, so it differs from Python on real values
(0.00625 gives 0.0062 where Python gives 0.0063, and 0.00035 gives 0.0004 where Python gives 0.0003).
The routing port found this. Every rounded number Tau emits now goes through the exact `PyRound`.

---

## 2026-09-27 · ONNX export spike: approach A failed, approach B passed for all four models

- **Approach A** (TorchScript `torch.onnx.export`, opset 17) exported `laya-en` in 19 s, but the graph
  **doesn't generalise**. The head's `nn.MultiheadAttention` bakes the traced sequence length (423) into
  a reshape, so a 21-token input fails with `Reshape … {21,1,1024} → {423,16,64}`. Keeping it would have
  been silently wrong for every request of a different length. Dropped.
- **Approach B** (the `torch.export`/dynamo exporter, opset 18, symbolic B/S/K dims) passes. All four
  models exported (`laya-en`, `laya-multilingual`, `laya-typed-decisions`, `von-1.2.0`) in 27–45 s each,
  with weights in `model.onnx.data`. Level-1 parity (onnxruntime-python on CPU against the vendor PyTorch
  forward on identical tensors, 293 rows over 163 model-case pairs): **max |Δlogit| 5.5e-5, max |Δprob|
  4.0e-6, 0 argmax mismatches**. The tolerance is 2e-3 / 1e-3, so the headroom is about 36x.
  Evidence: `reports/r1/parity-model.json`, produced by `uv run python -m tau_sidecar.parity`.
- No fallback needed, so no Python inference process sits behind the Runtime.
- Tracing hazards found in the reference code and handled: HF masks skip padding and the local window
  on small examples (trace with padding and S > 300); the fused encoder-layer fast path has no ONNX
  export (export with grad enabled); `topk(2)` needs K ≥ 2 (the host pads K to 2 with a masked slot).

## 2026-09-27 · argmax tie-break: first index within 1e-4 of the max

Von scores each option independently, so two options with identical text get exactly the same logit
in the reference. torch picks the first index, and ONNX float noise (about 1e-6) picked the second.
**Decision:** Tau picks the lowest index among logits within 1e-4 of the max. That's the reference's
own first-index rule, made robust to float noise. The parity gate uses the same rule.
**Consequence:** if two options genuinely differ by less than 1e-4 in logit (about 2.5e-5 in
probability), Tau may pick the other one. That's a tie in any practical sense.

## 2026-09-27 · Von input rules come from von-sdk's own types

`von.types` only accepts string (or null) descriptions for choice and noul criteria. It turns object
or array instructions into text with `json.dumps(v, sort_keys=isinstance(v, dict))` (default ASCII
escaping). Score levels are strings or `{what, examples}` dicts.
**Decision:** for `von-1.2.0`, Tau reproduces that exact instruction serialisation. It rejects, with
422 naming the field, any contract-valid shapes the reference itself rejects (structured choice or noul
descriptions, array score levels). Noul criteria keys are lower-cased before Von sees them (the Tau
contract rule), so `"True"` works on Von. The reference would silently ignore it. Recorded deviation.

## 2026-09-27 · Laya's option budget depends on the checkpoint

The English checkpoint (`max_len` 512, `head_max_len` 192) fits about 125 short options. The
multilingual and typed-decisions checkpoints (`max_len` 1024) fit all 255. Over-budget requests are
rejected with 422, as the reference raises. My first parity case assumed 512 for all three. The
reference was right and the case was fixed.

## 2026-09-27 · Datasets (FR-027 licence check)

Verified from the HF API and dataset cards by a research agent. Key facts rechecked where they matter.
- **Banking77** (`PolyAI/banking77` @ `90d4e2ee…`): **CC-BY-4.0**. 13,083 rows (10,003/3,080), 77
  gold intents. Publishable with attribution.
- **Tobi-Bueck/customer-support-tickets** (@ `ddf1c81a…`): the best urgency set. 61,765 tickets (synthetic, see correction above), a
  5-level `priority` field, English and German. **CC-BY-NC-4.0, non-commercial.** It's fine for
  measuring and for Rob's articles, but redistributing samples inside an Apache-2.0 repo, or using it
  on a company page, is a grey area. **Put to Rob at the R1 gate** with a recommendation.
- Dropped: `gorkemsevinc/customer_support_tickets` (no licence), `electricsheepafrica/…` (contradictory
  rights statement), `bitext/…` (no urgency field).
- Permissive fallbacks if Rob says no to NC: `nerofinal012/TicketingToolDataset` (MIT, real, gated,
  small, row count unverified), `Horizon-Labs/multilingual-zeroshot-synthetic` (ODC-BY, synthetic).

## 2026-09-27 · RuFlo / Claude Flow unavailable, so fan-out uses the Agent tool

The ruflo MCP server failed to connect this session, and `npx @claude-flow/cli@latest` crashes on
start (`npm error Class extends value undefined is not a constructor or null`). **Decision:** fall back
to Claude Code's Agent tool, with worktree isolation for parallel lanes, as the global CLAUDE.md
allows when the CLI is unavailable.

## 2026-09-27 · Laya's reference is the maintained `laya==0.3.20` runtime, not the checkpoint-repo script

The checkpoint repo's `rl_agent_api.py` is out of date. The PyPI runtime clamps temperatures
to [0.5, 5.0]. Without the clamp, the shipped `choice:11+` = 0.1006 turns a 0.24 top probability
into 0.99, and Laya's authors say as much in a code comment. The runtime also left-truncates list
states and renders structured criteria as JSON.
**Decision:** parity is measured against `laya==0.3.20` and `von-sdk==1.2.3`, both pinned.
**Reason:** "drop-in" means matching what users actually run.

## 2026-09-27 · Laya's `confidence` isn't the calibrated quantity (carried to R2)

For choice and score, Laya reports `confidence = 1 − normalised entropy`. Its own code says this
isn't what temperature scaling fits, and it reports `answer_confidence = max(p)` separately for that
reason. **Decision:** the contract field keeps the reference definition. The Workbench (R2) measures
ECE on `max(p)` and says so in every report. It's an article point too.

## 2026-09-27 · ONNX Runtime pinned at 1.24.4; one managed binary picks the native flavour at startup

The DirectML native package stops at 1.24.4, while CUDA is at 1.30. A managed ORT assembly only drives
natives of its own version. **Decision:** pin CPU, CUDA and DirectML at 1.24.4. Ship each native flavour
in `native/<flavour>/` and pick it with `NativeLibrary.SetDllImportResolver` from config. So "single-file
publish" means a single-file executable plus a sibling `native/` folder. CUDA 12 and cuDNN 9 DLLs come
from NVIDIA's free pip wheels, because no toolkit is installed.
**Reason:** that's the only way to get "same binary, config-selected" across all three providers.

## 2026-09-27 · `jev-latest` routes automatically

The official SDK sends `model: "jev-latest"` by default. **Decision:** `auto`, `tau-auto`, `jev-latest`
and `jev-*` go through Laya's script router (English vs multilingual), and the response names the Tau
model actually used. **Reason:** rejecting the SDK default would break "change only the base URL".

## 2026-09-27 · Contract validated strictly

Tau rejects Laya's laxer inputs: a list-form choice, and a score with one level. Tau-specific request
fields must start with `x-tau-`. **Reason:** Principle XIV, the contract comes first.

## 2026-09-27 · "Shared-state batching" means one batched pass per request, not one state encoding

Verified by reading Laya's `rl_common.py` and Von's `option_marker.py`. Both models
put the question (and, for Laya, the options) into the same sequence as the state,
and the encoder is bidirectional. The question tokens attend to the state, so a
state encoding can't be reused across questions without retraining the model, and
retraining is a non-goal. Laya's published "7.2 ms per question batched" comes from
running every question of one request through a single batched forward pass (one
sequence per question).
**Decision:** the Runtime batches all of a request's questions into one forward pass
per model. Questions stay independent because each has its own sequence. Articles
must not say the state is "encoded once". **Flag to Rob at the R1 gate:** the
brainstorm's wording is wrong about the mechanism, though the latency claim it
supports still holds.

## 2026-09-27 · Kev resolved: `jaredpalmer/kev`, Kev-0.8B for local conformance

Kev is an open Apache-2.0 family on Qwen3.5 (0.8B/4B/9B/27B) that serves the same
`POST /v1/systemone`. Kev-0.8B needs about 4 GB of VRAM, which fits on the 3080 Ti.
**Decision:** the conformance suite diffs Tau against a local Kev-0.8B server and
against the contract schema. Kev only documents Linux and macOS, so running it on
Windows is an R1 risk. If it won't run, the fallback is WSL, and the fallback after
that is Von's own `von serve`, which is also a real `/v1/systemone` server.
Supersedes the earlier "Kev deferred" entry.

## 2026-09-27 · Contract pinned as a dated snapshot

TypeSafe's API reference (docs.typesafe.ai/api) has no version identifier.
**Decision:** pin the contract as a snapshot dated 2026-09-27, committed as a
JSON Schema plus a human-readable copy of the field definitions under
`contracts/systemone/2026-09-27/`. Conformance tests validate against that
snapshot. Drift shows up as a diff on the next snapshot.

## 2026-09-27 · Parity reproduces each model's own post-processing

The two reference implementations differ after the encoder. Laya uses 1 minus
normalised entropy for confidence, per-type and per-option-count temperatures,
`json.dumps` for structured state, and `p[1]` for noul. Von uses TypeSafe's margin
confidence `(n·p_max−1)/(n−1)`, an input-conditioned temperature map, a
null-state prior correction for zero-shot noul, `k: v` lines for dict state, and
`p[0]` for noul.
**Decision:** the Runtime reproduces each model's reference post-processing exactly,
so its answers match the vendor SDKs. Parity is tested at two levels: (1) ONNX
logits against PyTorch logits for the same token IDs, and (2) complete
`/v1/systemone` answers from the C# Runtime against the reference Python API.
Tokeniser parity (C# against HF `tokenizers`) is a third gate, because a one-token
drift breaks everything that comes after it.
Laya's non-contract `rl_agent` answer field is dropped (Principle XIV).

## 2026-09-27 · Pinned checkpoints and licences (models)

| Model | HF repo @ commit | Licence |
| --- | --- | --- |
| Laya (English, multilingual, typed-decisions) | `convaiinnovations/laya` @ `55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851` | Apache-2.0 |
| Von 1.2.0 | `wfzyx/von` @ `5df8185a4f2327ad0a7cd117cc4f701ac557b9ae` | Apache-2.0 |
| Kev-0.8B | `jaredpalmer/kev` (pinned at implement) | Apache-2.0 (Qwen base Apache-2.0) |

All three can be published. Dataset licences are checked in R1 as a task.

## 2026-09-27 · Laya's own Banking77 miss is on record

Laya's model card reports **0.425 on Banking77 against Jev's 0.870**, because each
option gets only 3 to 4 tokens when there are 77 of them. This is the same problem
as the brainstorm's "fine-tuned classic encoders win" point, and it's
vendor-reported, so we have to re-measure it. It goes in the articles as a miss.

---

## 2026-09-26 · Constitution is a repo-local fork, not a symlink to the global base

`.specify/memory/constitution.md` was a symlink to `~/.claude/constitution.md`.
Editing it would have changed the constitution for every project on the machine.
**Decision:** removed the symlink (global base untouched) and wrote a tracked
fork, v1.6.0. It keeps base Principles I–XII verbatim and adds Tau Principles
XIII–XVII (measured not claimed, contract-first, one calibration library, zero
extra cost, named data exclusions) plus Quality Gate #8 (parity and conformance
are merge gates).
**Reason:** the global CLAUDE.md says each Spec Kit project keeps its own
tracked constitution, versioned independently.

## 2026-09-26 · Codename → real-artifact mapping (grounding)

The brainstorm uses working names for products that shipped after my knowledge
cutoff. Resolved by web research (ArXivIQ review + vendor pages + HuggingFace/GitHub)
so the build targets real artifacts, not guesses. **Verified, not assumed.**

| Brainstorm name | Real artifact | Source |
| --- | --- | --- |
| System One model | Category: typed decision models (state + typed questions → calibrated probabilities, one non-autoregressive forward pass) | ArXivIQ review |
| TypeSafe / Jev | TypeSafe AI's hosted closed model + the published `/v1/systemone` contract (shipped 15 Sep 2026) | ArXivIQ; vendor |
| Laya | Convai Innovations — open **421M ModernBERT-large** decision head, Apache 2.0. English + **mmBERT** multilingual. Raw ECE **0.466 → 0.081** after per-type temperature scaling | laya.convaiinnovations.com; HuggingFace |
| Von | Open System One model, sub-15ms, drop-in Jev alternative | github.com/wfzyx/von |
| Primitives | **Choice** (pick one), **Score** (ordinal/rubric float), **Noul** (yes/no probability) | vendor docs |
| Support-ticket dataset | `ruidpm/triage-bench` benchmarks Von on support-ticket triage — candidate second dataset | GitHub |

**Reason:** Building against the wrong checkpoint would poison every measured
number — the exact credibility risk the brainstorm names. Identity had to be
settled from sources before any code. Model repo IDs + commit hashes get pinned
at R1 implement time (in the export scripts and report metadata), not here.

## 2026-09-26 · "Kev" conformance target — deferred, non-blocking

The brainstorm names "Kev" as a wire-compatible endpoint to run conformance
against. No exact match surfaced. Several real open Jev-wire-compatible servers
exist (e.g. `Manavarya09/verdict`, entries in `cobanov/awesome-jev`).
**Decision:** conformance runs against (a) the *published `/v1/systemone`
contract* as the authority, and (b) one real open wire-compatible implementation,
chosen and pinned at R1 implement. No Jev key is bought (per brief); any Jev
figure is cited third-party data, labelled as such.
**Reason:** the contract, not any single server, is the standard the brief fixes.

## 2026-09-26 · Reference GPU is an RTX 3080 **Ti** (12 GB), not a plain 3080

`nvidia-smi`: NVIDIA GeForce RTX 3080 Ti, 12288 MiB, driver 610.47. The brief
said "RTX 3080"; the actual card is the Ti with 12 GB.
**Decision:** benchmark on and state the real card (RTX 3080 Ti, 12 GB, driver
610.47) in every report. 12 GB VRAM is the fine-tune budget to design against.
**Reason:** "measured, not claimed" — state the hardware that produced the number.
*(Flagged to Rob; trivially correct to the real spec.)*

## 2026-09-26 · Fine-tune sidecar pinned to Python 3.12, not 3.14

Machine has Python 3.14.7 (default) and 3.12. PyTorch/ONNX wheels for 3.14 are
not reliably available yet.
**Decision:** the Python sidecar (`sidecar/finetune/`) uses a **3.12** venv with
pinned deps; the .NET side is unaffected. Top R1 risk to verify empirically in
the spike; fallback is 3.11 if a specific wheel is missing.
**Reason:** avoid a bleeding-edge-interpreter dead end blocking the ONNX spike.

## 2026-09-26 · Release breakdown (embedded diagram not in brainstorm text)

The brainstorm's "Release plan · 3 releases, 3 gates" is an embedded diagram, not
text. Inferred from the components, the finish-line criteria, and the launch
prompt ("Articles and APEX page (R3)"):

- **R1 — Runtime core + ONNX spike.** Contract types, shared calibration library,
  Laya/Von → ONNX with a gating **parity test**, ASP.NET server answering the full
  `/v1/systemone` contract, Tau.Client, conformance suite, measured latency on the
  3080 Ti. Gate: parity passes; conformance green; latency published.
- **R2 — Workbench + benchmarks.** `tau` CLI (label→measure→calibrate→threshold→
  cascade→report), fine-tune sidecar, ECE-before/after + cascade-£ on **two**
  datasets, committed reports reproducible by one command. Gate: both datasets end
  to end; after-ECE materially lower; cascade £ reported.
- **R3 — Articles + APEX page.** Canonical long-form + HN/DEV.to/Reddit/LinkedIn
  drafts + APEX R&D page. `docs/FINISH.md`. Gate: drafts done, every number links
  to its report. Nothing published.

**Reason:** needed concrete release boundaries to run one spec per release; this
mapping matches the finish-line checklist and the prompt. Revisit if Rob's
embedded plan differs.
