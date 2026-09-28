# Tau /v1/systemone conformance report

- **Command**: `dotnet run --project tools/Tau.Conformance -- --tau http://localhost:18095 --peer https://api.typesafe.ai --peer-name jev-1.13 --peer-revision jev-latest --peer-api-key-env TYPESAFE_API_KEY --out reports/jev`
- **Date (UTC)**: 2026-09-28T07:30:23Z
- **Git commit**: 90cbce084da5cb9ca8de794f94d5e40d59a81f4d
- **Contract version**: `systemone/2026-09-27`
- **GPU**: NVIDIA GeForce RTX 3080 Ti, 12288 MiB, 610.47
- **CPU**: Intel64 Family 6 Model 167 Stepping 1, GenuineIntel
- **RAM**: 63.8 GB (reported by the .NET GC as total available memory)
- **OS**: Microsoft Windows 10.0.19045
- **Tau `/v1/models`**: {"models":[{"id":"laya-en","family":"laya","revision":"55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851","onnx_sha256":"866a05b244e47e96820660d18ee050c518c2de0f60c39e2e9a89dfeb056e1eec","loaded":true},{"id":"laya-multilingual","family":"laya","revision":"55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851","onnx_sha256":"62be63b71dd97ed1d2b6582965702d06d9fb387a1c9be103fe365d6d9c82ed0d","loaded":true},{"id":"laya-typed-decisions","family":"laya","revision":"55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851","onnx_sha256":"2b7a961ac37157d1cfa8105e3283106baf1ba2a5cc30fb3a673253f06aa1e1c5","loaded":true},{"id":"von-1.2.0","family":"von","revision":"5df8185a4f2327ad0a7cd117cc4f701ac557b9ae","onnx_sha256":"0777bb988636663b770775ae0b4eb961d6fbee176c9bea1d1da823723b1eb3f3","loaded":true}],"aliases":["auto","tau-auto","jev-latest","jev-*"]}
- **Peer**: jev-1.13 at `jev-latest`

This report is reproducible by re-running the command above from a checkout of the commit named above; every number below comes from that one run.

## Summary

| Requests | Tau pass | Tau fail | Peer pass | Peer fail | Structural failures | Model disagreements | Peer extensions |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 45 | 45 | 0 | 35 | 10 | 10 | 66 | 0 |

- **Requests** is the number of committed fixtures under `tests/conformance/requests/` this run sent to every target: 45.
- **Tau pass/fail** is how many of those 45 requests Tau answered without any contract violation (45 did, 0 did not); any Tau fail makes this run's exit code non-zero, since Tau's own conformance is the merge gate (FR-024).
- **Peer pass/fail** is the same count for the peer server, validated leniently (extra fields never count against it); it is recorded for comparison and never affects the exit code.
- **Structural failures** is the total number of contract violations across both sides (10): a status the contract didn't expect, a required field missing, or a value outside what the question allows — see FR-024's structural/model-disagreement split.
- **Model disagreements** (66) is how many times Tau and the peer both gave a contractually valid answer to the same question that simply differs in value (a different choice, score or noul) — expected because they are different models, and never a failure.
- **Peer extensions** (0) is how many response fields the peer returned beyond the pinned contract, tolerated under lenient validation and listed per request below.

## Per-request detail

### noul question alone (`01_noul_alone.json`)

A single noul question with no other question types in the request.

Expected status: **200**.

- **Tau**: status 200, 410 ms, contract **PASS**.
- **Peer**: status 200, 317.6 ms, contract **PASS**.
Model disagreements:
- `is_urgent` (noul): Tau said `0.2273`, peer said `0.76`.

### choice question alone (`02_choice_alone.json`)

A single choice question with no other question types in the request.

Expected status: **200**.

- **Tau**: status 200, 70.6 ms, contract **PASS**.
- **Peer**: status 200, 264.6 ms, contract **PASS**.
### score question alone (`03_score_alone.json`)

A single score question with no other question types in the request.

Expected status: **200**.

- **Tau**: status 200, 70 ms, contract **PASS**.
- **Peer**: status 200, 256.8 ms, contract **PASS**.
Model disagreements:
- `satisfaction_risk` (score): Tau said `1.3674`, peer said `2.9`.

### all three question types together (`04_all_three_types.json`)

One noul, one choice and one score question in the same request.

Expected status: **200**.

- **Tau**: status 200, 113 ms, contract **PASS**.
- **Peer**: status 200, 232.8 ms, contract **PASS**.
Model disagreements:
- `needs_escalation` (noul): Tau said `0.6642`, peer said `0.62`.
- `department` (choice): Tau said `fraud`, peer said `cards`.
- `urgency` (score): Tau said `0.9318`, peer said `1.7`.

### exactly one question (`05_questions_count_1.json`)

A request containing exactly one question.

Expected status: **200**.

- **Tau**: status 200, 61.4 ms, contract **PASS**.
- **Peer**: status 200, 231.8 ms, contract **PASS**.
Model disagreements:
- `is_resolved` (noul): Tau said `0.1086`, peer said `0.06`.

### exactly two questions (`06_questions_count_2.json`)

A request containing exactly two questions.

Expected status: **200**.

- **Tau**: status 200, 78 ms, contract **PASS**.
- **Peer**: status 200, 273.9 ms, contract **PASS**.
Model disagreements:
- `is_bug` (noul): Tau said `0.8688`, peer said `0.43`.
- `priority` (choice): Tau said `medium`, peer said `high`.

### ten questions (`07_questions_count_10.json`)

A request containing ten questions, cycling through all three question types.

Expected status: **200**.

- **Tau**: status 200, 210 ms, contract **PASS**.
- **Peer**: status 200, 242 ms, contract **PASS**.
Model disagreements:
- `noul_1` (noul): Tau said `0.2452`, peer said `0.56`.
- `score_3` (score): Tau said `2.4143`, peer said `0.7`.
- `noul_4` (noul): Tau said `0.2284`, peer said `0.22`.
- `score_6` (score): Tau said `2.3642`, peer said `0.65`.
- `noul_7` (noul): Tau said `0.234`, peer said `0.18`.
- `score_9` (score): Tau said `2.318`, peer said `0.88`.
- `noul_10` (noul): Tau said `0.2262`, peer said `0.14`.

### fifty questions (`08_questions_count_50.json`)

A request containing fifty questions, cycling through all three question types.

Expected status: **200**.

- **Tau**: status 200, 225.4 ms, contract **PASS**.
- **Peer**: status 200, 221.8 ms, contract **PASS**.
Model disagreements:
- `noul_1` (noul): Tau said `0.2452`, peer said `0.59`.
- `score_3` (score): Tau said `2.4143`, peer said `0.83`.
- `noul_4` (noul): Tau said `0.2284`, peer said `0.21`.
- `score_6` (score): Tau said `2.3642`, peer said `0.68`.
- `noul_7` (noul): Tau said `0.234`, peer said `0.18`.
- `score_9` (score): Tau said `2.318`, peer said `1.02`.
- `noul_10` (noul): Tau said `0.2262`, peer said `0.15`.
- `score_12` (score): Tau said `2.3166`, peer said `0.85`.
- `noul_13` (noul): Tau said `0.2325`, peer said `0.18`.
- `score_15` (score): Tau said `2.3054`, peer said `1.11`.
- `noul_16` (noul): Tau said `0.2276`, peer said `0.17`.
- `score_18` (score): Tau said `2.3085`, peer said `1.21`.
- `noul_19` (noul): Tau said `0.2085`, peer said `0.16`.
- `score_21` (score): Tau said `2.3289`, peer said `0.95`.
- `noul_22` (noul): Tau said `0.221`, peer said `0.16`.
- `score_24` (score): Tau said `2.3`, peer said `0.94`.
- `noul_25` (noul): Tau said `0.231`, peer said `0.14`.
- `score_27` (score): Tau said `2.3332`, peer said `0.96`.
- `noul_28` (noul): Tau said `0.2242`, peer said `0.17`.
- `score_30` (score): Tau said `2.318`, peer said `1.11`.
- `noul_31` (noul): Tau said `0.2287`, peer said `0.18`.
- `score_33` (score): Tau said `2.321`, peer said `1.23`.
- `noul_34` (noul): Tau said `0.2308`, peer said `0.19`.
- `score_36` (score): Tau said `2.3401`, peer said `1.06`.
- `noul_37` (noul): Tau said `0.2363`, peer said `0.18`.
- `score_39` (score): Tau said `2.3297`, peer said `1.37`.
- `noul_40` (noul): Tau said `0.2283`, peer said `0.18`.
- `score_42` (score): Tau said `2.3221`, peer said `1.03`.
- `noul_43` (noul): Tau said `0.2354`, peer said `0.16`.
- `score_45` (score): Tau said `2.3095`, peer said `1.1`.
- `noul_46` (noul): Tau said `0.2407`, peer said `0.16`.
- `score_48` (score): Tau said `2.2983`, peer said `1.11`.
- `noul_49` (noul): Tau said `0.2191`, peer said `0.17`.

### choice with 1 option (`09_choice_options_1.json`)

A choice question with a single option.

Expected status: **200**.

- **Tau**: status 200, 14.9 ms, contract **PASS**.
- **Peer**: status 200, 240.6 ms, contract **PASS**.
### choice with 2 options (`10_choice_options_2.json`)

A choice question with two options.

Expected status: **200**.

- **Tau**: status 200, 16.2 ms, contract **PASS**.
- **Peer**: status 200, 246.7 ms, contract **PASS**.
Model disagreements:
- `binary_choice` (choice): Tau said `option_01`, peer said `option_02`.

### choice with 5 options (`11_choice_options_5.json`)

A choice question with five options.

Expected status: **200**.

- **Tau**: status 200, 15.9 ms, contract **PASS**.
- **Peer**: status 200, 220.7 ms, contract **PASS**.
### choice with 30 options (`12_choice_options_30.json`)

A choice question with thirty options, well under the contract's maximum of 255.

Expected status: **200**.

- **Tau**: status 200, 22.5 ms, contract **PASS**.
- **Peer**: status 200, 246.6 ms, contract **PASS**.
Model disagreements:
- `reason_code` (choice): Tau said `option_09`, peer said `option_01`.

### choice with 77 options (`13_choice_options_77.json`)

A choice question with seventy-seven options (the Banking77-style option count).

Expected status: **200**.

- **Tau**: status 200, 32.3 ms, contract **PASS**.
- **Peer**: status 200, 256 ms, contract **PASS**.
Model disagreements:
- `intent` (choice): Tau said `option_05`, peer said `option_01`.

### choice with null descriptions (`14_choice_null_descriptions.json`)

A choice question whose option descriptions are all JSON null, which the contract permits.

Expected status: **200**.

- **Tau**: status 200, 14.3 ms, contract **PASS**.
- **Peer**: status 200, 213 ms, contract **PASS**.
### choice with empty string descriptions (`15_choice_empty_descriptions.json`)

A choice question whose option descriptions are all empty strings.

Expected status: **200**.

- **Tau**: status 200, 14.8 ms, contract **PASS**.
- **Peer**: status 200, 213.1 ms, contract **PASS**.
### score with 2 levels (`16_score_levels_2.json`)

A score question at the contract's minimum of two levels.

Expected status: **200**.

- **Tau**: status 200, 22.5 ms, contract **PASS**.
- **Peer**: status 200, 205.1 ms, contract **PASS**.
Model disagreements:
- `satisfied` (score): Tau said `0.1691`, peer said `0.01`.

### score with 10 levels (`17_score_levels_10.json`)

A score question at the contract's maximum of ten levels.

Expected status: **200**.

- **Tau**: status 200, 29.4 ms, contract **PASS**.
- **Peer**: status 200, 220.4 ms, contract **PASS**.
Model disagreements:
- `satisfaction` (score): Tau said `2.5661`, peer said `1.76`.

### noul with criteria (`18_noul_with_criteria.json`)

A noul question that supplies true/false criteria descriptions.

Expected status: **200**.

- **Tau**: status 200, 29.2 ms, contract **PASS**.
- **Peer**: status 200, 209.8 ms, contract **PASS**.
Model disagreements:
- `is_fraud` (noul): Tau said `0.5725`, peer said `0.23`.

### noul without criteria (`19_noul_without_criteria.json`)

A noul question with no criteria field, which the contract makes optional.

Expected status: **200**.

- **Tau**: status 200, 32.1 ms, contract **PASS**.
- **Peer**: status 200, 216.1 ms, contract **PASS**.
Model disagreements:
- `is_fraud` (noul): Tau said `0.3557`, peer said `0.48`.

### plain text state (`20_state_text.json`)

State given as a plain string.

Expected status: **200**.

- **Tau**: status 200, 27.6 ms, contract **PASS**.
- **Peer**: status 200, 220.7 ms, contract **PASS**.
Model disagreements:
- `is_urgent` (noul): Tau said `0.1561`, peer said `0.27`.

### structured object state (`21_state_object.json`)

State given as a JSON object rather than a string.

Expected status: **200**.

- **Tau**: status 200, 34.9 ms, contract **PASS**.
- **Peer**: status 200, 205.1 ms, contract **PASS**.
Model disagreements:
- `is_billing_error` (noul): Tau said `0.8686`, peer said `0.94`.

### array state (`22_state_array.json`)

State given as a JSON array of message turns.

Expected status: **200**.

- **Tau**: status 200, 35.3 ms, contract **PASS**.
- **Peer**: status 200, 219.6 ms, contract **PASS**.
Model disagreements:
- `needs_investigation` (noul): Tau said `0.0942`, peer said `0.74`.

### deeply nested state (`23_state_nested.json`)

State given as a deeply nested object and array structure.

Expected status: **200**.

- **Tau**: status 200, 54.8 ms, contract **PASS**.
- **Peer**: status 200, 208.5 ms, contract **PASS**.
Model disagreements:
- `wants_cancellation` (noul): Tau said `0.904`, peer said `0.99`.

### null state (`24_state_null.json`)

State explicitly given as JSON null, which the contract permits.

Expected status: **200**.

- **Tau**: status 200, 33.7 ms, contract **PASS**.
- **Peer**: status 422, 171.6 ms, contract **FAIL**.
  - error: status mismatch: expected 200, got 422
### empty string state (`25_state_empty_string.json`)

State given as an empty string.

Expected status: **200**.

- **Tau**: status 200, 35.3 ms, contract **PASS**.
- **Peer**: status 200, 242.7 ms, contract **PASS**.
Model disagreements:
- `is_urgent` (noul): Tau said `0.1898`, peer said `0.7`.

### long state around 6000 characters (`26_state_long.json`)

State is a long free-text narrative of 6100 characters.

Expected status: **200**.

- **Tau**: status 200, 144.5 ms, contract **PASS**.
- **Peer**: status 200, 228 ms, contract **PASS**.
Model disagreements:
- `root_cause_is_otp_delay` (noul): Tau said `0.9514`, peer said `0.91`.

### unicode-heavy state (`27_state_unicode.json`)

State containing emoji and accented Latin characters.

Expected status: **200**.

- **Tau**: status 200, 25 ms, contract **PASS**.
- **Peer**: status 200, 279.3 ms, contract **PASS**.
Model disagreements:
- `is_angry` (noul): Tau said `0.6127`, peer said `0.98`.

### non-Latin script state (`28_state_non_latin.json`)

State written in Japanese and Arabic script rather than Latin characters.

Expected status: **200**.

- **Tau**: status 200, 22.8 ms, contract **PASS**.
- **Peer**: status 200, 243.5 ms, contract **PASS**.
Model disagreements:
- `needs_translation_support` (noul): Tau said `0.6639`, peer said `0.92`.

### instructions given as an object (`29_instructions_object.json`)

The question's instructions field is a structured object rather than a string.

Expected status: **200**.

- **Tau**: status 200, 38.3 ms, contract **PASS**.
- **Peer**: status 200, 273.1 ms, contract **PASS**.
Model disagreements:
- `is_bug` (noul): Tau said `0.8168`, peer said `0.82`.

### instructions given as an array (`30_instructions_array.json`)

The question's instructions field is an array of instruction fragments rather than a string.

Expected status: **200**.

- **Tau**: status 200, 38.5 ms, contract **PASS**.
- **Peer**: status 200, 247.1 ms, contract **PASS**.
Model disagreements:
- `urgency` (score): Tau said `1.0639`, peer said `1.73`.

### model value jev-latest (`31_model_jev_latest.json`)

The request pins the model alias jev-latest.

Expected status: **200**.

- **Tau**: status 200, 36.8 ms, contract **PASS**.
- **Peer**: status 200, 208.9 ms, contract **PASS**.
Model disagreements:
- `is_urgent` (noul): Tau said `0.2623`, peer said `0.67`.

### model value auto (`32_model_auto.json`)

The request uses the auto-routing model alias.

Expected status: **200**.

- **Tau**: status 200, 40 ms, contract **PASS**.
- **Peer**: status 400, 185.5 ms, contract **FAIL**.
  - error: status mismatch: expected 200, got 400
### model value pinned laya-en (`33_model_pinned_laya_en.json`)

The request pins a specific model id, laya-en, rather than an alias.

Expected status: **200**.

- **Tau**: status 200, 55.1 ms, contract **PASS**.
- **Peer**: status 400, 170.2 ms, contract **FAIL**.
  - error: status mismatch: expected 200, got 400
### invalid: missing model field (`34_invalid_missing_model.json`)

The request omits the required top-level model field.

Expected status: **422**.

- **Tau**: status 422, 2.8 ms, contract **PASS**.
- **Peer**: status 422, 173 ms, contract **PASS**.
### invalid: missing state field (`35_invalid_missing_state.json`)

The request omits the required top-level state field entirely (distinct from state: null).

Expected status: **422**.

- **Tau**: status 422, 0.3 ms, contract **PASS**.
- **Peer**: status 422, 172.1 ms, contract **PASS**.
### invalid: missing questions field (`36_invalid_missing_questions.json`)

The request omits the required top-level questions field.

Expected status: **422**.

- **Tau**: status 422, 0.3 ms, contract **PASS**.
- **Peer**: status 422, 172.6 ms, contract **PASS**.
### invalid: empty questions map (`37_invalid_empty_questions.json`)

The request supplies questions as an empty object, violating the minimum of one question.

Expected status: **422**.

- **Tau**: status 422, 0.3 ms, contract **PASS**.
- **Peer**: status 422, 173.5 ms, contract **PASS**.
### invalid: unknown question type (`38_invalid_unknown_type.json`)

The question's type field is a value outside noul, choice and score.

Expected status: **422**.

- **Tau**: status 422, 0.4 ms, contract **PASS**.
- **Peer**: status 400, 171.6 ms, contract **FAIL**.
  - error: status mismatch: expected 422, got 400
### invalid: choice with 0 options (`39_invalid_choice_0_options.json`)

A choice question whose criteria map is empty, violating the minimum of one option.

Expected status: **422**.

- **Tau**: status 422, 0.3 ms, contract **PASS**.
- **Peer**: status 400, 323.2 ms, contract **FAIL**.
  - error: status mismatch: expected 422, got 400
### invalid: choice with 256 options (`40_invalid_choice_256_options.json`)

A choice question with 256 options, one more than the contract's maximum of 255.

Expected status: **422**.

- **Tau**: status 422, 0.4 ms, contract **PASS**.
- **Peer**: status 400, 174.8 ms, contract **FAIL**.
  - error: status mismatch: expected 422, got 400
### invalid: score with 1 level (`41_invalid_score_1_level.json`)

A score question with only one level, below the contract's minimum of two.

Expected status: **422**.

- **Tau**: status 422, 0.3 ms, contract **PASS**.
- **Peer**: status 200, 221.4 ms, contract **FAIL**.
  - error: status mismatch: expected 422, got 200
### invalid: score with 11 levels (`42_invalid_score_11_levels.json`)

A score question with eleven levels, one more than the contract's maximum of ten.

Expected status: **422**.

- **Tau**: status 422, 0.5 ms, contract **PASS**.
- **Peer**: status 400, 170.4 ms, contract **FAIL**.
  - error: status mismatch: expected 422, got 400
### invalid: noul criteria with a maybe key (`43_invalid_noul_maybe_key.json`)

A noul question's criteria object includes a maybe key, which the contract does not allow alongside true/false.

Expected status: **422**.

- **Tau**: status 422, 0.4 ms, contract **PASS**.
- **Peer**: status 200, 233.3 ms, contract **FAIL**.
  - error: status mismatch: expected 422, got 200
### invalid: unknown top-level field (`44_invalid_unknown_top_level_field.json`)

The request body includes an extra top-level field not permitted by the contract and not x-tau- prefixed.

Expected status: **422**.

- **Tau**: status 422, 0.4 ms, contract **PASS**.
- **Peer**: status 400, 172.7 ms, contract **FAIL**.
  - error: status mismatch: expected 422, got 400
### invalid: malformed JSON body (`45_invalid_malformed_json.json`)

The raw request body is not valid JSON at all (a trailing comma and an unterminated string).

Expected status: **422**.

- **Tau**: status 422, 1.6 ms, contract **PASS**.
- **Peer**: status 422, 169.3 ms, contract **PASS**.
