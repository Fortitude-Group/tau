# Tau — Decisions log

Every decision made autonomously during the build, with the reason. Newest first.
Decisions already fixed by Rob's brief live in `brainstorm.md` and the launch prompt;
this file records the ones I made so the trail is auditable.

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
