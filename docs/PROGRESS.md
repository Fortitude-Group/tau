# Tau — Progress

Updated after every completed task. On session restart, resume from here without asking.

**Current release:** R1 (Runtime core + ONNX parity), branch `001-runtime-onnx-parity`.
**Gate status:** R1 in progress. The R1 gate stops for Rob's "go".
**Task list:** `specs/001-runtime-onnx-parity/tasks.md` (44+/63 ticked).

---

## Done

### Setup and planning
- **Grounding, constitution v1.6.0, R1 spec/clarify/plan/tasks/analyze.** *Evidence:* `docs/DECISIONS.md`,
  `.specify/memory/constitution.md`, `specs/001-runtime-onnx-parity/`.

### Models and parity
- **T004 · Models fetched and verified.** 22/22 files match the sha256 in `models.lock.json`, taken from
  Hugging Face's LFS metadata at the pinned revisions.
- **T010–T017 · ONNX export spike.** TorchScript export generalises wrongly (a baked-in sequence length).
  The dynamo export works for all four models. Level-1 parity: 293 rows, max |Δlogit| 5.5e-5, 0 argmax
  mismatches. *Evidence:* `reports/r1/parity-model.json`.
- **T027–T034 · C# parity.** The Laya and Von sequence builders reproduce the reference token rows exactly
  (tokens, markers, Von position ids) for all four models. Tau's own batched logits and its complete
  `/v1/systemone` answers match laya 0.3.20 and von-sdk 1.2.3 within tolerance. *Evidence:*
  `tests/Tau.Inference.Tests/Parity/ModelParityTests.cs` (12/12).
- **T023–T026, T006 · Serialisers, tokeniser, providers.** 3,157-case byte-exact `json.dumps`/`str`
  parity; tokeniser parity on about 740 texts over 3 tokenisers; providers **cpu, cuda and directml all pass**
  on the 3080 Ti.
- **T037 · Routing.** The C# port of Laya's script router matches the reference on 684/684 states, reason
  strings included, using Unicode tables generated from Python 3.12.

### Runtime
- **T018–T022 · Contract (113 tests) and calibration library (95 tests).**
- **T038–T043 · Engine and HTTP host.** Real-model HTTP tests, 41/41: contract-strict answers from all four
  models, routing, 422s, truncation, identical bytes over 100 concurrent requests.
- **T053–T054 · Calibrator hook.** Temperature and isotonic calibrators apply, and `x-tau-raw` bypasses them.
  A bad or stale calibrator stops start-up.
- **T051 · Tau.Client** (41 tests, packs to `artifacts/packages/Tau.Client.0.1.0.nupkg`, not pushed).
- **T045–T047 · Conformance tool and Kev.** Kev-0.8B (`jaredpalmer/kev` @ `5920c5fe`) runs natively on
  Windows once its setup is pointed at the CUDA wheel index. The tool's self-test passes 12/12.
- **Smoke run, CUDA, end to end:** an English ticket goes to `laya-en`, German goes to `laya-multilingual`, a
  pinned Von request answers, and an invalid score gets 422.

## In flight

- T035/T036: `scripts/parity.ps1` running (writes `reports/r1/parity.md`).
- T049/T050: latency benchmark tool (agent). The measured run needs a quiet machine.

## Next

- T048 conformance run against Kev. T050 latency run. T052 client in-process test. T055 publish and Docker.
  T060–T062 clean reproduction. T063 merge. **T064 R1 gate.**

## Gate items for Rob (collected so far)

1. **Incident.** A build agent killed Docker Desktop's backend while clearing port 8080. Your containers
   restarted within minutes (verified), but anything behind `deploy-caddy-1` (80/443) had a short outage.
   Tau now defaults to port 8088.
2. **Dataset licence decision:** the best urgency set (`Tobi-Bueck/customer-support-tickets`, 61,765 real
   tickets) is CC-BY-NC-4.0. Banking77 is CC-BY-4.0.
3. The brainstorm's "encode the state once" is wrong about the mechanism. Batching per request was agreed
   in clarify.
4. The GPU is an RTX 3080 **Ti** (the brief said 3080). Reports state the Ti.
5. Laya's own numbers are worse than the brainstorm implies in places (Banking77 0.425 vs Jev 0.870, per
   its model card). These go in the articles as misses.
