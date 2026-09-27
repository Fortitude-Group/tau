# Implementation Plan: Tau R3: launch articles, APEX R&D page and public readiness

**Branch**: `003-launch-articles` | **Date**: 2026-09-27 | **Spec**: [spec.md](spec.md)

## Summary

Turn the committed R1/R2 evidence into one canonical article, four channel drafts and an APEX page draft. Make the repo ready to go public (README, packaging, container, licences, secrets). Finish with `docs/FINISH.md` and the brainstorm boxes ticked. Nothing is published.

## Technical context

- **Inputs (read-only):**
  - `examples/banking77/report.json|html`
  - `examples/support-tickets/report.json|html`
  - `reports/r1/*.md|json`
  - `examples/*/finetune-parity.json`
  - `examples/*/frontier/label-summary.json`
  - `docs/DECISIONS.md`
- **Writing rules:** the `soup-playbook` and `writing-no-slop` skills. British English, first person, no em dashes or semicolons in prose.
- **Number check:** a small script, `scripts/check-articles.ps1`. It reads each draft and, for every Markdown link to a repo file, checks that the file exists and that the numbers in the same sentence appear in that file (allowing for rounding: a number with d decimals matches any file value that rounds to it). It also fails on banned terms and em dashes. This is the automated check that FR-008 requires.
- **Figures:** headless Edge screenshots of the committed `report.html` sections, saved to `docs/articles/img/`.
- **README walkthrough:** a fresh clone in the scratchpad, following the README literally, with the clock running. Model fetching is timed separately.
- **Packaging:** `dotnet pack` for `Tau.Client` and `Tau.Workbench` (the `tau` tool) into `artifacts/packages`, which is gitignored. `docker build` the Runtime image and answer one request against it. Nothing is pushed.
- **Licences:** walk `Directory.Packages.props`, the sidecar `uv.lock` top-level dependencies, `models.lock.json` and both dataset manifests, and write `THIRD-PARTY-NOTICES.md`. Update `NOTICE` if needed.
- **Secret scan:** `gitleaks` if available, otherwise a regex sweep over `git log -p --all` and the tree for key and token patterns. The result is recorded.

## Constitution check

| Principle | How R3 meets it |
| --- | --- |
| XIII honesty, every number from a committed report | The number-check script gates the drafts. The misses are published. The headline is caveated. |
| X no production change without approval | Nothing is deployed, pushed or made public (FR-015). |
| Security rules | Secret scan. The web playbook's credentials are never copied into the repo. |
| Parallel by default | Lanes below. The article prose stays single-voice by design. |

## Lanes

| Lane | Work | Why it can run in parallel |
| --- | --- | --- |
| A: canonical article | Written by the lead, single voice | Everything downstream derives from it |
| B: public readiness | README, walkthrough, pack, Docker, licences, secret scan | Code and docs only, no article dependency |
| C: number checker and figures | `scripts/check-articles.ps1` with tests, and report screenshots | Independent of prose |
| D: channel drafts | HN, DEV.to, Reddit, LinkedIn | Needs A. Single voice, so one agent |
| E: APEX page draft | Content and structure from the web playbook's style guide | Needs A |
| F: finish | FINISH.md, brainstorm ticks, gate | Needs everything |

## Project structure (new files)

```
README.md
THIRD-PARTY-NOTICES.md
scripts/check-articles.ps1
docs/articles/
  tau-canonical.md
  hn.md
  devto.md
  reddit.md
  linkedin.md
  apex-page.md
  img/*.png
docs/FINISH.md
```
