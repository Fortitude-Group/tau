# Feature Specification: Tau R3: launch articles, APEX R&D page and public readiness

**Feature Branch**: `003-launch-articles`

**Created**: 2026-09-27

**Status**: Draft

**Input**: User description: "Tau R3: launch articles, APEX R&D page, public-readiness and FINISH.md. Everything Rob needs to go public, stopping at drafts. Nothing is published, deployed or made public." (full text in the conversation record and `docs/PROGRESS.md`)

## Context

R1 (the Runtime and ONNX parity) and R2 (the Workbench and two worked datasets) are merged to `master`. This release turns their committed evidence into words people will read, and leaves Rob with a checklist he can follow to go public. It measures nothing new, apart from timing the README walkthrough.

The agreed angle, from the R2 gate: most LLM classification calls can run on a gaming GPU, and the surprise is which model does it best. The failure shape from the brainstorm carries through every piece: **a confidence score that looks certain and means nothing**.

## Clarifications

### Session 2026-09-27

- Q: How should the MiniLM result sit in the headline? → A: As a twist teased in the title. The title leads with the cost cut on a gaming GPU and says "the surprise is which model". MiniLM is revealed in paragraph two.
- Q: How prominent should the negative tickets result be? → A: It gets its own section in the canonical piece and on HN, and one or two lines in DEV.to, Reddit and LinkedIn.
- Q: Which working title? → A: "Three-quarters of my Claude classification calls could run on a gaming GPU. The surprise is which model." The first paragraph must make clear the calls are the Banking77 benchmark's (FR-004).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Rob reviews one canonical write-up he can trust (Priority: P1)

Rob opens the canonical long-form piece. It tells the whole story in the agreed order:
1. The Banking77 cascade result.
2. The classic-encoder twist.
3. Calibration before and after.
4. The misses.
5. How to reproduce it.

Every number links to the committed report it came from. He can publish it on the Fortitude Omnis R&D page after light edits, without re-checking any figure himself.

**Why this priority**: Every channel draft and the APEX page derive from this piece. If its numbers or framing are wrong, everything downstream is wrong.

**Independent Test**: Read the piece and follow every numeric link. Each number matches the value in the linked report file, and each caveat the honesty rules require is present.

**Acceptance Scenarios**:

1. **Given** the committed reports, **When** a reader follows any number's link, **Then** the linked report contains that number, at the precision stated or rounded from it.
2. **Given** the headline cascade figure, **When** it first appears, **Then** the same paragraph names:
   - the hardware (RTX 3080 Ti);
   - the dataset;
   - the measurement date;
   - that the £ figures are list-price estimates;
   - that the frontier answers came from an interactive session answering batched sheets, not the API.
3. **Given** the piece, **When** it is checked against the misses list in both reports, **Then** it includes:
   - the classic-encoder win;
   - the ticket label noise;
   - the tickets calibration miss;
   - the cases where calibration barely helped;
   - the urgency task where no local model stands in for the frontier.

---

### User Story 2 - Rob posts each channel draft without rewriting it (Priority: P1)

Rob picks up the draft for one channel: HN, DEV.to, Reddit or LinkedIn. It is already shaped for that channel's norms:
- **HN:** findings first, no marketing tone, plus a first comment.
- **DEV.to:** a hands-on tutorial with one command to reproduce, and its canonical URL pointing at the R&D page.
- **Reddit:** findings first and the repo link last, with separate variants for r/LocalLLaMA and r/dotnet, and r/MachineLearning only as a [P] post that leads with method.
- **LinkedIn:** a short post with one chart and a link out.

**Why this priority**: Posting is the point of the release. A draft that needs rewriting per channel hasn't done its job.

**Independent Test**: For each channel file, check the channel-specific form rules and the shared honesty rules, and check that its numbers match the canonical piece.

**Acceptance Scenarios**:

1. **Given** the HN draft, **When** it is read, **Then** it leads with a measured finding, links the repo and the canonical write-up, and contains no superlatives or marketing claims.
2. **Given** the Reddit drafts, **When** each is read, **Then** findings come before the repo link, which appears once, at the end.
3. **Given** any draft, **When** it is searched, **Then** it contains no term from the banned list.

---

### User Story 3 - A newcomer makes a first decision from the README in under 10 minutes (Priority: P2)

A developer who has never seen Tau clones the repo and follows the README. They go from nothing to a first answered `/v1/systemone` request in under 10 minutes on the reference machine, not counting the time to download model weights.

**Why this priority**: It's a finish-line criterion in the brainstorm, and the articles send readers to the README.

**Independent Test**: Follow the README step by step on a fresh clone and time it. Record every point where the README was wrong or unclear, and fix the README.

**Acceptance Scenarios**:

1. **Given** a fresh clone, **When** the README quickstart is followed literally, **Then** a first decision is returned and the elapsed time, excluding the model download, is recorded and under 10 minutes.

---

### User Story 4 - Rob knows exactly what is left to go public (Priority: P2)

Rob opens `docs/FINISH.md`. It lists:
- what shipped;
- every headline number, each linked to its report;
- the known gaps;
- the exact, ordered steps he must take himself, none of which have been taken: making the repo public, pushing the NuGet package and Docker image, deploying the APEX page, and posting each article.

**Why this priority**: The launch prompt makes this file part of the finish line. Without it, the irreversible steps have no checklist.

**Independent Test**: Read FINISH.md. Every step is concrete (a command or a click path), every number has a link, and every brainstorm finish-line box is ticked with evidence or marked with the reason it isn't.

**Acceptance Scenarios**:

1. **Given** FINISH.md, **When** each brainstorm finish-line criterion is looked up, **Then** it has either evidence (a file or report link) or a plain statement of why not.

---

### User Story 5 - The APEX R&D page is ready to build (Priority: P3)

Rob, or a later session following the web playbook, takes the APEX page draft and builds it into the Fortitude Omnis site without having to decide content or structure.

**Why this priority**: The page hosts the canonical piece, but building and deploying it is a separate, later step.

**Independent Test**: The draft specifies the page sections, the copy, the images to use (screenshots from the committed reports) and the links. It contains no deploy steps and no credentials.

**Acceptance Scenarios**:

1. **Given** the APEX draft, **When** it is read, **Then** it has a title, a summary, the section copy, the figure list with sources, calls to action (the repo, the canonical piece) and a byline.

### Edge Cases

- **A number in an article doesn't match its report.** The article is wrong: fix the article, never the report.
- **A figure has no committed report behind it,** for example a cost worked out in prose. Either derive it in a committed report or drop it.
- **The README walkthrough fails or takes over 10 minutes.** Fix the README or the code. If the target still can't be met, record the actual time in FINISH.md and leave the finish-line box unticked, with the reason.
- **A draft needs a chart that no report contains.** Use a committed report's own figure (a screenshot of `report.html`) rather than drawing a new chart from uncommitted numbers.
- **A channel's rules forbid self-promotion,** for example some subreddits. The draft says so at the top and recommends whether to post at all.
- **The secret scan finds something.** Stop, remove it from history if it was ever committed, rotate it, and record the incident. Nothing goes public until that is done.

## Requirements *(mandatory)*

### Functional Requirements

**Articles**

- **FR-001**: A canonical long-form article MUST exist in `docs/articles/` under the agreed working title. It tells the story in the agreed order: cascade result, classic-encoder twist (revealed in paragraph two), calibration, the tickets section ("the benchmark that lied"), the other misses, how to reproduce.
- **FR-002**: Channel drafts MUST exist as one file per channel: HN (a Show HN post plus a first comment), DEV.to (a tutorial with a canonical URL pointing at the R&D page), Reddit (r/LocalLLaMA and r/dotnet variants, and an r/MachineLearning [P] variant that leads with method) and LinkedIn (a short post naming one chart).
- **FR-003**: Every number in every draft MUST link to the committed report file that contains it, and MUST match that report at the stated precision.
- **FR-004**: The first mention of any headline number in each draft MUST carry its caveats in the same paragraph:
  - the hardware;
  - the dataset;
  - the measurement date;
  - that £ figures are list-price estimates;
  - that the frontier labels came from an interactive session answering batched sheets, not the API.
- **FR-005**: Each draft MUST include the misses, at a length that fits the channel:
  - the classic encoder beats every Tau model on Banking77;
  - the synthetic ticket labels are close to noise;
  - on urgency, no local model stands in for the frontier;
  - the laya-en calibration miss on tickets and why;
  - the models calibration barely helped.
- **FR-006**: The drafts MUST follow the writing rules: plain British English, first person, short sentences, dry asides allowed, no hype, and no em dashes or semicolons in prose.
- **FR-007**: No draft may contain a term from the banned list. The byline is Rob and Fortitude Omnis.
- **FR-008**: The drafts MUST be checked by an automated pass that confirms:
  - every linked report path exists;
  - every number that sits beside a link appears in the linked file;
  - no banned words or em dashes appear.

**APEX page**

- **FR-009**: An APEX R&D page draft MUST specify the title, summary, sections with copy, figures with their source files, links and byline, following the house style guide where one exists. It MUST contain no deploy steps or credentials.

**Public readiness**

- **FR-010**: The README quickstart MUST be followed literally on a fresh clone and timed. The README MUST be corrected wherever it was wrong or unclear, and the timing recorded.
- **FR-011**:
  - The NuGet packages MUST pack locally.
  - The Docker image MUST build locally and answer one request.
  - Nothing may be pushed to any registry.
- **FR-012**:
  - The licence, NOTICE and third-party attributions MUST be checked for every shipped dependency, model and dataset, with the result recorded.
  - A secret scan MUST run over the working tree and the full git history, with the result recorded.

**Finish**

- **FR-013**: `docs/FINISH.md` MUST list:
  - what shipped;
  - every headline number with its report link;
  - the known gaps;
  - the exact, ordered steps Rob must take himself to go public.
- **FR-014**: Every success-criteria box in `docs/brainstorm.md` MUST be ticked with evidence, or left unticked with the reason stated, in both brainstorm.md and FINISH.md.
- **FR-015**: Nothing may be published, posted, deployed, pushed to a registry or made public.

### Key Entities

- **Canonical article**: the single source of wording and numbers for every channel.
- **Channel draft**: one file per channel, derived from the canonical article and shaped for that channel.
- **Number reference**: a number in a draft, paired with the committed report file (and field) it comes from.
- **Finish record**: FINISH.md plus the ticked brainstorm boxes. It is the audit of what is done and what Rob still has to do.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All six files exist (the canonical article, HN, DEV.to, Reddit, LinkedIn and the APEX page), with the Reddit variants inside the Reddit file.
- **SC-002**: 100% of the numbers in the drafts link to a committed report and match it. The automated check reports zero mismatches and zero dead links.
- **SC-003**: Zero banned terms and zero em dashes appear in any draft, confirmed by the automated check.
- **SC-004**: A newcomer following the README reaches a first decision in under 10 minutes on the reference machine, excluding the model download. The timing is recorded.
- **SC-005**: The packages pack, the image builds and answers a request, and the secret scan finds nothing. All three results are recorded.
- **SC-006**: Every brainstorm success-criteria box is ticked with evidence, or has a stated reason.
- **SC-007**: Nothing has been published, deployed, pushed to a registry or made public by the end of the release.

## Assumptions

- The committed R1 and R2 reports are the only source of numbers. No new benchmark runs happen in R3, apart from the README timing.
- The canonical URL for DEV.to is a placeholder on the Fortitude Omnis R&D path until the APEX page is deployed. FINISH.md tells Rob to fill it in.
- Charts are screenshots of the committed `report.html` files, captured locally. No new chart code is written.
- The web playbook's house style guide (`design-system/<brand>/MASTER.md`), if present, shapes the APEX draft's structure. The playbook's credentials are never read into, or referenced by, any repo file.
- Posting times, subreddit rule checks at posting time and replies to comments are Rob's job and are out of scope.
- Rob's time downloading model weights is excluded from the 10-minute README target, because it depends on his connection.
