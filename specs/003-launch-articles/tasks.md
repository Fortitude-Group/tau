# Tasks: Tau R3: launch articles, APEX R&D page and public readiness

**Standing rule**: tick each task here and add a line to `docs/PROGRESS.md` in the same commit.
**Hard rule**: nothing is published, posted, deployed, pushed to a registry or made public.

## Lanes

| Lane | Tasks | Runs |
| --- | --- | --- |
| A: canonical article | T010–T011 | Lead, now |
| B: public readiness | T020–T025 | Agent, now, in parallel |
| C: checker and figures | T030–T031 | Agent, now, in parallel |
| D: channel drafts | T040 | Agent, after A |
| E: APEX page | T050 | Agent, after A |
| F: finish | T060–T063 | Lead, last |

## Lane A: canonical article

- [x] T010 [US1] Write `docs/articles/tau-canonical.md` under the agreed title, in the agreed order, with every number linked to its report and the caveats at the first mention (FR-001, FR-003–FR-007).
- [x] T011 [US1] Run the checker (T030) on it. Zero dead links, zero mismatches, zero banned terms.

## Lane B: public readiness

- [ ] T020 [P] [US3] Write `README.md`: what Tau is, a quickstart to a first decision (fetch models, build or run the Runtime, one curl/`Tau.Client` call), the Workbench in one example, links to the reports, licence.
- [ ] T021 [US3] Follow the README literally on a fresh clone and time it, excluding the model download. Fix every step that failed or confused, and record the timing in `docs/PROGRESS.md`.
- [ ] T022 [P] [US4] `dotnet pack` `Tau.Client` and `Tau.Workbench`, then install the tool locally from the nupkg and run `tau --help`. Nothing is pushed.
- [ ] T023 [P] [US4] `docker build` the Runtime image, run it with the models mounted, answer one request, and stop only that container.
- [ ] T024 [P] [US4] Licence audit: write `THIRD-PARTY-NOTICES.md` (NuGet, Python and model dependencies and datasets, with licences and links) and update `NOTICE` if needed.
- [ ] T025 [P] [US4] Secret scan over the tree and the full history. Record the result.

## Lane C: checker and figures

- [x] T030 [P] `scripts/check-articles.ps1`: link existence, number-in-file matching with rounding, banned terms, and em dashes and semicolons in prose. Self-test against a fixture with one deliberate error of each kind (FR-008).
- [x] T031 [P] Screenshots of the committed reports into `docs/articles/img/`: the Banking77 reliability diagrams, cascade table, trade-off curve and misses list, and the tickets summary and gold-view table.

## Lane D and E: derived drafts (after T011)

- [x] T040 [US2] `hn.md` (a Show HN post and first comment), `devto.md` (tutorial, canonical URL placeholder), `reddit.md` (r/LocalLLaMA, r/dotnet and r/MachineLearning [P] variants, with a subreddit-rules note) and `linkedin.md` (short post, one image). All derive from the canonical piece, and the checker passes on each.
- [x] T050 [US5] `apex-page.md`: title, summary, sections with copy, figures with their sources, calls to action and byline, following the web playbook's style guide. No deploy steps and no credentials. The checker passes.

## Lane F: finish

- [ ] T060 [US4] `docs/FINISH.md`: what shipped, the headline numbers with links, known gaps, and Rob's ordered go-public steps.
- [ ] T061 Tick the brainstorm finish-line boxes with evidence, or give the reason.
- [ ] T062 Checker over every draft, full test suite green, merge to `master`, push to the private repo.
- [ ] T063 **FINISH: stop.** Report to Rob.

## Dependencies

A → D, E. A, B, C, D, E → F. B and C run alongside A.
