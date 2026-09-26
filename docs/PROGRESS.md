# Tau — Progress

Updated after every completed task. On session restart, resume from here without asking.

**Current release:** R0 (setup) → moving into constitution, then R1.
**Current gate status:** none reached yet. First stop is the **R1 gate** (wait for Rob's "go").

---

## Done

- **2026-09-26 — Grounding.** Verified toolchain: .NET 10.0.400, Python 3.14 + 3.12,
  git 2.55, gh active as `fortitude-omnis`, GPU **RTX 3080 Ti 12 GB** (driver 610.47).
  *Evidence:* command output in session; recorded in `DECISIONS.md`.
- **2026-09-26 — Codenames resolved** to real artifacts (Laya=Convai 421M ModernBERT,
  Von, TypeSafe/Jev, `/v1/systemone`). *Evidence:* `DECISIONS.md` mapping table + sources.
- **2026-09-26 — Repo initialised.** `git init` on `master`, Fortitude identity,
  `.gitignore`. *Evidence:* `git config` output; files on disk. Not yet pushed
  (push to Fortitude-Group org is an R1 action, and public/remote steps wait for Rob).

- **2026-09-26 — Constitution v1.6.0 (Tau fork).** Replaced the symlink to the
  global base with a tracked fork: base I–XII plus Tau XIII–XVII and Gate #8.
  *Evidence:* `.specify/memory/constitution.md`; placeholder check came back clean.

## Next

1. R1: `/speckit.specify` → `clarify` → `plan` → `tasks` → `analyze` → `implement`,
   starting with the **ONNX parity spike** (export Laya + Von, prove parity).
3. Stop at the **R1 gate** with evidence; wait for "go".

## Open items to confirm at R1 (non-blocking, defaulted)

- Exact "Kev" conformance server (default: a real open wire-compatible impl, pinned at implement).
- Second dataset final pick (default: an open urgency-scored support-ticket set; `triage-bench` lead).
- PyTorch/ONNX on Python 3.12 — verify wheels install in the spike.
