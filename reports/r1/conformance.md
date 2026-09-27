# Tau /v1/systemone conformance report

- **Command**: `dotnet run --project tools/Tau.Conformance -- --tau http://localhost:8088 --out reports/r1 --requests tests/conformance/requests --peer http://127.0.0.1:8009 --peer-name kev-0.8b --peer-revision 5920c5fe4ca8e0970ed4209ac2c9b8e18bea5109`
- **Date (UTC)**: 2026-09-27T04:05:24Z
- **Git commit**: c26162e40284fbbff86bc937e482885cbb961b64
- **Contract version**: `systemone/2026-09-27`
- **GPU**: NVIDIA GeForce RTX 3080 Ti, 12288 MiB, 610.47
- **CPU**: Intel64 Family 6 Model 167 Stepping 1, GenuineIntel
- **RAM**: 63.8 GB (reported by the .NET GC as total available memory)
- **OS**: Microsoft Windows 10.0.19045
- **Tau `/v1/models`**: {"models":[{"id":"laya-en","family":"laya","revision":"55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851","onnx_sha256":"866a05b244e47e96820660d18ee050c518c2de0f60c39e2e9a89dfeb056e1eec","loaded":true},{"id":"laya-multilingual","family":"laya","revision":"55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851","onnx_sha256":"62be63b71dd97ed1d2b6582965702d06d9fb387a1c9be103fe365d6d9c82ed0d","loaded":true},{"id":"laya-typed-decisions","family":"laya","revision":"55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851","onnx_sha256":"2b7a961ac37157d1cfa8105e3283106baf1ba2a5cc30fb3a673253f06aa1e1c5","loaded":true},{"id":"von-1.2.0","family":"von","revision":"5df8185a4f2327ad0a7cd117cc4f701ac557b9ae","onnx_sha256":"0777bb988636663b770775ae0b4eb961d6fbee176c9bea1d1da823723b1eb3f3","loaded":true}],"aliases":["auto","tau-auto","jev-latest","jev-*"]}
- **Peer**: kev-0.8b at `5920c5fe4ca8e0970ed4209ac2c9b8e18bea5109`

This report is reproducible by re-running the command above from a checkout of the commit named above; every number below comes from that one run.

## Summary

| Requests | Tau pass | Tau fail | Peer pass | Peer fail | Structural failures | Model disagreements | Peer extensions |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 45 | 45 | 0 | 40 | 5 | 5 | 69 | 33 |

- **Requests** is the number of committed fixtures under `tests/conformance/requests/` this run sent to every target: 45.
- **Tau pass/fail** is how many of those 45 requests Tau answered without any contract violation (45 did, 0 did not); any Tau fail makes this run's exit code non-zero, since Tau's own conformance is the merge gate (FR-024).
- **Peer pass/fail** is the same count for the peer server, validated leniently (extra fields never count against it); it is recorded for comparison and never affects the exit code.
- **Structural failures** is the total number of contract violations across both sides (5): a status the contract didn't expect, a required field missing, or a value outside what the question allows — see FR-024's structural/model-disagreement split.
- **Model disagreements** (69) is how many times Tau and the peer both gave a contractually valid answer to the same question that simply differs in value (a different choice, score or noul) — expected because they are different models, and never a failure.
- **Peer extensions** (33) is how many response fields the peer returned beyond the pinned contract, tolerated under lenient validation and listed per request below.

## Per-request detail

### noul question alone (`01_noul_alone.json`)

A single noul question with no other question types in the request.

Expected status: **200**.

- **Tau**: status 200, 935.6 ms, contract **PASS**.
- **Peer**: status 200, 538.3 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_urgent` (noul): Tau said `0.2273`, peer said `0.5082`.

### choice question alone (`02_choice_alone.json`)

A single choice question with no other question types in the request.

Expected status: **200**.

- **Tau**: status 200, 786.9 ms, contract **PASS**.
- **Peer**: status 200, 1131.8 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
### score question alone (`03_score_alone.json`)

A single score question with no other question types in the request.

Expected status: **200**.

- **Tau**: status 200, 1001.7 ms, contract **PASS**.
- **Peer**: status 200, 1064 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `satisfaction_risk` (score): Tau said `1.3674`, peer said `2.0244`.

### all three question types together (`04_all_three_types.json`)

One noul, one choice and one score question in the same request.

Expected status: **200**.

- **Tau**: status 200, 1083.5 ms, contract **PASS**.
- **Peer**: status 200, 1173.7 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `needs_escalation` (noul): Tau said `0.6642`, peer said `0.5041`.
- `department` (choice): Tau said `fraud`, peer said `cards`.
- `urgency` (score): Tau said `0.9318`, peer said `1.5587`.

### exactly one question (`05_questions_count_1.json`)

A request containing exactly one question.

Expected status: **200**.

- **Tau**: status 200, 1068.4 ms, contract **PASS**.
- **Peer**: status 200, 1171 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_resolved` (noul): Tau said `0.1086`, peer said `0.2653`.

### exactly two questions (`06_questions_count_2.json`)

A request containing exactly two questions.

Expected status: **200**.

- **Tau**: status 200, 1120.4 ms, contract **PASS**.
- **Peer**: status 200, 1268.2 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_bug` (noul): Tau said `0.8688`, peer said `0.8079`.

### ten questions (`07_questions_count_10.json`)

A request containing ten questions, cycling through all three question types.

Expected status: **200**.

- **Tau**: status 200, 1130.2 ms, contract **PASS**.
- **Peer**: status 200, 1255.4 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `noul_1` (noul): Tau said `0.2452`, peer said `0.6618`.
- `score_3` (score): Tau said `2.4143`, peer said `1.0726`.
- `noul_4` (noul): Tau said `0.2284`, peer said `0.5914`.
- `score_6` (score): Tau said `2.3642`, peer said `1.1508`.
- `noul_7` (noul): Tau said `0.234`, peer said `0.5906`.
- `score_9` (score): Tau said `2.318`, peer said `1.1635`.
- `noul_10` (noul): Tau said `0.2262`, peer said `0.6126`.

### fifty questions (`08_questions_count_50.json`)

A request containing fifty questions, cycling through all three question types.

Expected status: **200**.

- **Tau**: status 200, 1728.1 ms, contract **PASS**.
- **Peer**: status 200, 3349.8 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `noul_1` (noul): Tau said `0.2452`, peer said `0.6618`.
- `score_3` (score): Tau said `2.4143`, peer said `1.073`.
- `noul_4` (noul): Tau said `0.2284`, peer said `0.5914`.
- `score_6` (score): Tau said `2.3642`, peer said `1.1452`.
- `noul_7` (noul): Tau said `0.234`, peer said `0.5906`.
- `score_9` (score): Tau said `2.318`, peer said `1.1603`.
- `noul_10` (noul): Tau said `0.2262`, peer said `0.6126`.
- `score_12` (score): Tau said `2.3166`, peer said `1.1381`.
- `noul_13` (noul): Tau said `0.2325`, peer said `0.6117`.
- `score_15` (score): Tau said `2.3054`, peer said `1.141`.
- `noul_16` (noul): Tau said `0.2276`, peer said `0.621`.
- `score_18` (score): Tau said `2.3085`, peer said `1.1363`.
- `noul_19` (noul): Tau said `0.2085`, peer said `0.6314`.
- `score_21` (score): Tau said `2.3289`, peer said `1.094`.
- `noul_22` (noul): Tau said `0.221`, peer said `0.6256`.
- `score_24` (score): Tau said `2.3`, peer said `1.0818`.
- `noul_25` (noul): Tau said `0.231`, peer said `0.6242`.
- `score_27` (score): Tau said `2.3332`, peer said `1.1138`.
- `noul_28` (noul): Tau said `0.2242`, peer said `0.6279`.
- `score_30` (score): Tau said `2.318`, peer said `1.065`.
- `noul_31` (noul): Tau said `0.2287`, peer said `0.6072`.
- `score_33` (score): Tau said `2.321`, peer said `1.0801`.
- `noul_34` (noul): Tau said `0.2308`, peer said `0.6326`.
- `score_36` (score): Tau said `2.3401`, peer said `1.0909`.
- `noul_37` (noul): Tau said `0.2363`, peer said `0.6221`.
- `score_39` (score): Tau said `2.3297`, peer said `1.0749`.
- `noul_40` (noul): Tau said `0.2283`, peer said `0.6031`.
- `score_42` (score): Tau said `2.3221`, peer said `1.0787`.
- `noul_43` (noul): Tau said `0.2354`, peer said `0.6307`.
- `score_45` (score): Tau said `2.3095`, peer said `1.0878`.
- `noul_46` (noul): Tau said `0.2407`, peer said `0.6062`.
- `score_48` (score): Tau said `2.2983`, peer said `1.0878`.
- `noul_49` (noul): Tau said `0.2191`, peer said `0.6195`.

### choice with 1 option (`09_choice_options_1.json`)

A choice question with a single option.

Expected status: **200**.

- **Tau**: status 200, 284.5 ms, contract **PASS**.
- **Peer**: status 200, 1475.9 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
### choice with 2 options (`10_choice_options_2.json`)

A choice question with two options.

Expected status: **200**.

- **Tau**: status 200, 1167.5 ms, contract **PASS**.
- **Peer**: status 200, 1393.6 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `binary_choice` (choice): Tau said `option_01`, peer said `option_02`.

### choice with 5 options (`11_choice_options_5.json`)

A choice question with five options.

Expected status: **200**.

- **Tau**: status 200, 1350.7 ms, contract **PASS**.
- **Peer**: status 200, 1470.1 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `category` (choice): Tau said `option_01`, peer said `option_05`.

### choice with 30 options (`12_choice_options_30.json`)

A choice question with thirty options, well under the contract's maximum of 255.

Expected status: **200**.

- **Tau**: status 200, 1315.6 ms, contract **PASS**.
- **Peer**: status 200, 1417.7 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `reason_code` (choice): Tau said `option_09`, peer said `option_30`.

### choice with 77 options (`13_choice_options_77.json`)

A choice question with seventy-seven options (the Banking77-style option count).

Expected status: **200**.

- **Tau**: status 200, 1313.3 ms, contract **PASS**.
- **Peer**: status 200, 1649.7 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `intent` (choice): Tau said `option_05`, peer said `option_02`.

### choice with null descriptions (`14_choice_null_descriptions.json`)

A choice question whose option descriptions are all JSON null, which the contract permits.

Expected status: **200**.

- **Tau**: status 200, 1155.4 ms, contract **PASS**.
- **Peer**: status 200, 1335.6 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
### choice with empty string descriptions (`15_choice_empty_descriptions.json`)

A choice question whose option descriptions are all empty strings.

Expected status: **200**.

- **Tau**: status 200, 1358.6 ms, contract **PASS**.
- **Peer**: status 200, 1355.3 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
### score with 2 levels (`16_score_levels_2.json`)

A score question at the contract's minimum of two levels.

Expected status: **200**.

- **Tau**: status 200, 1362.7 ms, contract **PASS**.
- **Peer**: status 200, 1364.3 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `satisfied` (score): Tau said `0.1691`, peer said `0.3759`.

### score with 10 levels (`17_score_levels_10.json`)

A score question at the contract's maximum of ten levels.

Expected status: **200**.

- **Tau**: status 200, 1312 ms, contract **PASS**.
- **Peer**: status 200, 1343.2 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `satisfaction` (score): Tau said `2.5661`, peer said `4.6596`.

### noul with criteria (`18_noul_with_criteria.json`)

A noul question that supplies true/false criteria descriptions.

Expected status: **200**.

- **Tau**: status 200, 1338.2 ms, contract **PASS**.
- **Peer**: status 200, 1328.7 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_fraud` (noul): Tau said `0.5725`, peer said `0.3239`.

### noul without criteria (`19_noul_without_criteria.json`)

A noul question with no criteria field, which the contract makes optional.

Expected status: **200**.

- **Tau**: status 200, 1309.3 ms, contract **PASS**.
- **Peer**: status 200, 1352.7 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_fraud` (noul): Tau said `0.3557`, peer said `0.4534`.

### plain text state (`20_state_text.json`)

State given as a plain string.

Expected status: **200**.

- **Tau**: status 200, 1339.3 ms, contract **PASS**.
- **Peer**: status 200, 1479 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_urgent` (noul): Tau said `0.1561`, peer said `0.4375`.

### structured object state (`21_state_object.json`)

State given as a JSON object rather than a string.

Expected status: **200**.

- **Tau**: status 200, 1299.4 ms, contract **PASS**.
- **Peer**: status 200, 1532.5 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_billing_error` (noul): Tau said `0.8686`, peer said `0.8358`.

### array state (`22_state_array.json`)

State given as a JSON array of message turns.

Expected status: **200**.

- **Tau**: status 200, 1213.3 ms, contract **PASS**.
- **Peer**: status 200, 1443.7 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `needs_investigation` (noul): Tau said `0.0942`, peer said `0.633`.

### deeply nested state (`23_state_nested.json`)

State given as a deeply nested object and array structure.

Expected status: **200**.

- **Tau**: status 200, 1301.6 ms, contract **PASS**.
- **Peer**: status 200, 1517.6 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `wants_cancellation` (noul): Tau said `0.904`, peer said `0.9603`.

### null state (`24_state_null.json`)

State explicitly given as JSON null, which the contract permits.

Expected status: **200**.

- **Tau**: status 200, 1284.4 ms, contract **PASS**.
- **Peer**: status 200, 1512.6 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_urgent` (noul): Tau said `0.1758`, peer said `0.4161`.

### empty string state (`25_state_empty_string.json`)

State given as an empty string.

Expected status: **200**.

- **Tau**: status 200, 1258 ms, contract **PASS**.
- **Peer**: status 200, 1318.7 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_urgent` (noul): Tau said `0.1898`, peer said `0.4495`.

### long state around 6000 characters (`26_state_long.json`)

State is a long free-text narrative of 6100 characters.

Expected status: **200**.

- **Tau**: status 200, 1444.3 ms, contract **PASS**.
- **Peer**: status 200, 1630.1 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `root_cause_is_otp_delay` (noul): Tau said `0.9514`, peer said `0.9547`.

### unicode-heavy state (`27_state_unicode.json`)

State containing emoji and accented Latin characters.

Expected status: **200**.

- **Tau**: status 200, 1018.9 ms, contract **PASS**.
- **Peer**: status 200, 1369.4 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_angry` (noul): Tau said `0.6127`, peer said `0.8216`.

### non-Latin script state (`28_state_non_latin.json`)

State written in Japanese and Arabic script rather than Latin characters.

Expected status: **200**.

- **Tau**: status 200, 1364.2 ms, contract **PASS**.
- **Peer**: status 200, 1418.7 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `needs_translation_support` (noul): Tau said `0.6639`, peer said `0.6077`.

### instructions given as an object (`29_instructions_object.json`)

The question's instructions field is a structured object rather than a string.

Expected status: **200**.

- **Tau**: status 200, 1395.9 ms, contract **PASS**.
- **Peer**: status 200, 1401.2 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_bug` (noul): Tau said `0.8168`, peer said `0.906`.

### instructions given as an array (`30_instructions_array.json`)

The question's instructions field is an array of instruction fragments rather than a string.

Expected status: **200**.

- **Tau**: status 200, 1362.4 ms, contract **PASS**.
- **Peer**: status 200, 1399.3 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `urgency` (score): Tau said `1.0639`, peer said `1.1999`.

### model value jev-latest (`31_model_jev_latest.json`)

The request pins the model alias jev-latest.

Expected status: **200**.

- **Tau**: status 200, 1331.1 ms, contract **PASS**.
- **Peer**: status 200, 1416.6 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_urgent` (noul): Tau said `0.2623`, peer said `0.5352`.

### model value auto (`32_model_auto.json`)

The request uses the auto-routing model alias.

Expected status: **200**.

- **Tau**: status 200, 1391.8 ms, contract **PASS**.
- **Peer**: status 200, 1406.6 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_urgent` (noul): Tau said `0.2623`, peer said `0.5352`.

### model value pinned laya-en (`33_model_pinned_laya_en.json`)

The request pins a specific model id, laya-en, rather than an alias.

Expected status: **200**.

- **Tau**: status 200, 1384.8 ms, contract **PASS**.
- **Peer**: status 200, 1323.1 ms, contract **PASS**.
  - peer extension: top-level field 'latency_ms'
Model disagreements:
- `is_urgent` (noul): Tau said `0.2623`, peer said `0.5352`.

### invalid: missing model field (`34_invalid_missing_model.json`)

The request omits the required top-level model field.

Expected status: **422**.

- **Tau**: status 422, 3.8 ms, contract **PASS**.
- **Peer**: status 200, 19.5 ms, contract **FAIL**.
  - error: status mismatch: expected 422, got 200
### invalid: missing state field (`35_invalid_missing_state.json`)

The request omits the required top-level state field entirely (distinct from state: null).

Expected status: **422**.

- **Tau**: status 422, 0.4 ms, contract **PASS**.
- **Peer**: status 422, 3.7 ms, contract **PASS**.
### invalid: missing questions field (`36_invalid_missing_questions.json`)

The request omits the required top-level questions field.

Expected status: **422**.

- **Tau**: status 422, 0.3 ms, contract **PASS**.
- **Peer**: status 422, 1.3 ms, contract **PASS**.
### invalid: empty questions map (`37_invalid_empty_questions.json`)

The request supplies questions as an empty object, violating the minimum of one question.

Expected status: **422**.

- **Tau**: status 422, 0.2 ms, contract **PASS**.
- **Peer**: status 422, 2.1 ms, contract **PASS**.
### invalid: unknown question type (`38_invalid_unknown_type.json`)

The question's type field is a value outside noul, choice and score.

Expected status: **422**.

- **Tau**: status 422, 0.3 ms, contract **PASS**.
- **Peer**: status 422, 2.8 ms, contract **PASS**.
### invalid: choice with 0 options (`39_invalid_choice_0_options.json`)

A choice question whose criteria map is empty, violating the minimum of one option.

Expected status: **422**.

- **Tau**: status 422, 0.3 ms, contract **PASS**.
- **Peer**: status 422, 2.3 ms, contract **PASS**.
### invalid: choice with 256 options (`40_invalid_choice_256_options.json`)

A choice question with 256 options, one more than the contract's maximum of 255.

Expected status: **422**.

- **Tau**: status 422, 0.5 ms, contract **PASS**.
- **Peer**: status 422, 2.3 ms, contract **PASS**.
### invalid: score with 1 level (`41_invalid_score_1_level.json`)

A score question with only one level, below the contract's minimum of two.

Expected status: **422**.

- **Tau**: status 422, 0.3 ms, contract **PASS**.
- **Peer**: status 200, 21.7 ms, contract **FAIL**.
  - error: status mismatch: expected 422, got 200
### invalid: score with 11 levels (`42_invalid_score_11_levels.json`)

A score question with eleven levels, one more than the contract's maximum of ten.

Expected status: **422**.

- **Tau**: status 422, 0.4 ms, contract **PASS**.
- **Peer**: status 200, 21.1 ms, contract **FAIL**.
  - error: status mismatch: expected 422, got 200
### invalid: noul criteria with a maybe key (`43_invalid_noul_maybe_key.json`)

A noul question's criteria object includes a maybe key, which the contract does not allow alongside true/false.

Expected status: **422**.

- **Tau**: status 422, 0.4 ms, contract **PASS**.
- **Peer**: status 200, 19.7 ms, contract **FAIL**.
  - error: status mismatch: expected 422, got 200
### invalid: unknown top-level field (`44_invalid_unknown_top_level_field.json`)

The request body includes an extra top-level field not permitted by the contract and not x-tau- prefixed.

Expected status: **422**.

- **Tau**: status 422, 0.3 ms, contract **PASS**.
- **Peer**: status 200, 18.8 ms, contract **FAIL**.
  - error: status mismatch: expected 422, got 200
### invalid: malformed JSON body (`45_invalid_malformed_json.json`)

The raw request body is not valid JSON at all (a trailing comma and an unterminated string).

Expected status: **422**.

- **Tau**: status 422, 5 ms, contract **PASS**.
- **Peer**: status 422, 1.1 ms, contract **PASS**.
