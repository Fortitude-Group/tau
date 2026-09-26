# Tau — Decisions log

Every decision made autonomously during the build, with the reason. Newest first.
Decisions already fixed by Rob's brief live in `brainstorm.md` and the launch prompt;
this file records the ones I made so the trail is auditable.

---

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
