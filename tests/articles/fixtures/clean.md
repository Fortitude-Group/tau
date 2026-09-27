# Clean fixture

At a 5% target error the fine-tuned model kept [73.6%](report.json) of decisions local, with an ECE of 0.502 before calibration and 0.065 after, on [1,000 items](/tests/articles/fixtures/report.json).

It cost about £1,031 per million decisions for 22.7M tokens, agreed with the frontier [94.2 per cent](tests/articles/fixtures/report.json) of the time and answered in 12 ms (see [the report](report.json)).

Measured on an RTX 3080 Ti on 27 September 2026 and 2026-09-27 with von-1.2.0, all-MiniLM-L6-v2 and Opus 5.5, as recorded in [the report](report.json).

This line has figures but no report link, so it is only noted: 3 models and 77 intents.

- A list item links [the report](../../../tests/articles/fixtures/report.json) and quotes 73.6%.

| Model | Kept local |
| --- | --- |
| fine-tuned | [73.6%](report.json) |

Code is not prose: `a; b — c` and `99.9%` are ignored here, as is an [external page](https://example.com/a;b).

```
x = 42; y = 3.14159 — unchecked
```
