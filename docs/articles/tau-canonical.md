# Three-quarters of my Claude classification calls could run on a gaming GPU. The surprise is which model.

*Rob Hill, Fortitude Omnis. Measured on 27 and 28 September 2026.*

I took a thousand banking support messages that Claude Opus 5.5 would otherwise sort into 77 intents, and ran them through a model on my own RTX 3080 Ti first. When the local model was confident, I kept its answer. When it wasn't, I sent the message to Claude. With a fine-tuned Laya decision model doing the local work, [73.0% of decisions stayed on the GPU, blended accuracy was 93.3% against 94.2% for Claude alone, and the estimated bill fell from £3,898 to £1,054 per million decisions](../../examples/banking77/report.json). Those pounds are estimates from published list prices, not invoices. It's one dataset (Banking77), one card, one night. And the Claude answers came from an interactive Claude Code session working through batched answer sheets, not from the API, so nobody was billed for them.

That's the result I set out to find. It isn't the most interesting one. A plain fine-tuned MiniLM classifier, the sort of thing people were training years ago, [kept 94.7% of decisions local at 94.2% blended accuracy](../../examples/banking77/report.json). It has [22,742,861 parameters](../../examples/banking77/baselines/minilm-l6-banking77.training.json), and it beat every decision model I tested, on accuracy and on calibration. I'll come back to what that means, because it's the useful bit.

I also put Jev, TypeSafe's hosted model, through the same tests on the same items. It lands in the middle on Banking77. On the harder ticket task it's the only thing I tested that takes real work off Claude.

## What I built

Tau is two things, both open source under Apache-2.0.

The **Runtime** is a .NET server that answers the `/v1/systemone` decision contract locally. It runs the open Laya and Von decision models through ONNX Runtime on CUDA, DirectML or a CPU. Anything that already talks to that contract can point at it instead of a hosted endpoint.

The **Workbench** is a command-line tool that asks one question of any `/v1/systemone` endpoint: can I trust this model's confidence enough to gate on it, and what does gating save? It measures accuracy and calibration, fits calibrators, picks a confidence threshold, simulates the local-first cascade and prices it. It measures hosted endpoints over the network the same way, which is how Jev got into the reports. Every stage writes its output to disk, and the last stage writes a single HTML report. Every number in this piece comes from one of those reports.

## A confidence score that looks certain and means nothing

A decision model gives you an answer and a probability. The pitch is that you can gate on the probability: keep the answer when it's high, escalate when it's low. That only works if 0.9 means right nine times in ten.

Out of the box, it doesn't. On Banking77, [laya-en was 37.2% accurate with an expected calibration error (ECE) of 0.502](../../examples/banking77/report.json). Put roughly, its stated confidence sat about 50 points away from its hit rate. Gating on that number is gating on noise dressed as a percentage.

Calibration changes what the number means. It leaves the model alone. Fitting a small calibrator on a separate 1,000-item split took [laya-en's ECE from 0.502 to 0.056, 88.7% lower](../../examples/banking77/report.json), and [Von's from 0.185 to 0.021](../../examples/banking77/report.json). Accuracy didn't move. What changed is that the confidence became something you can threshold. (The first version of this piece had worse figures here, because I'd fitted the calibrators on the wrong input. That's in the misses.)

One detail I'd skip if I were in a hurry, and shouldn't have been. Laya's contract returns a `confidence` field for choice questions that isn't a calibrated probability at all (it's derived from the entropy of the whole distribution). Every calibration figure here is on the probability of the chosen answer instead, and the reports say so.

## The cascade, and what it costs

A calibrated model is only useful if it's also right often enough. Raw laya-en wasn't: [no threshold got its error below 5%](../../examples/banking77/report.json). So I fine-tuned it on the [9,003-item training split](../../examples/banking77/dataset.manifest.json), on the same card. It took [1,518 seconds](../../examples/banking77/finetune-training.json).

With a threshold of [0.95, picked on the calibration split and judged on the held-out split](../../examples/banking77/report.json), the fine-tuned model kept 73.0% of decisions and sent the rest to Claude. The blend lost about one point of accuracy against Claude alone.

The money is simple arithmetic on published prices. Each Banking77 prompt is about [1,259 input tokens](../../examples/banking77/report.json) once you list every intent, so Claude Opus 5.5 at list price comes to [£3,898 per million decisions, or £1,949 through the Batch API](../../examples/banking77/report.json). Sending only the uncertain quarter [drops that to £1,054](../../examples/banking77/report.json). The local electricity is a rounding error next to either figure. I haven't counted the cost of the GPU itself, which I already owned for less serious reasons.

Two caveats I'd want if I were reading this. Tokens are estimated from characters (four per token, plus 30% for the tokenizer), not counted. And the frontier accuracy is Claude's agreement with the Banking77 labels, which it [disagreed with on 5.8% of items](../../examples/banking77/report.json). Some of those are Claude's mistakes and some are the dataset's.

## The classic encoder won

I put a fine-tuned all-MiniLM-L6-v2 through exactly the same measurement, calibration, threshold and cascade code as the decision models. It [scored 91.5% on the held-out set, with a calibrated ECE of 0.024](../../examples/banking77/report.json), against 87.3% for the fine-tuned Laya. In the cascade it [kept 94.7% local at the same 94.2% blended accuracy as Claude alone, for an estimated £207 per million](../../examples/banking77/report.json). It wasn't served through Tau, so that figure leaves out local latency and energy.

My [first MiniLM run scored 80.8%](../DECISIONS.md), because I'd stopped it after five epochs with the loss still falling. A baseline trained that badly flatters whatever you're comparing it with, so I retrained it properly (early stopping on the calibration split, held-out never touched) before I believed anything. The mistake and the fix are in the [decisions log](../DECISIONS.md).

What the win means: on a fixed task with thousands of labelled examples, a classic small classifier is still the thing to beat. Decision models earn their keep elsewhere, on questions you haven't trained for, on many questions against one state, on tasks where writing the options down is all the training you'll get. The part of Tau I'd defend hardest is the Workbench, because it measured its own models losing and printed it in the report.

## The benchmark that lied

The second dataset was meant to be support-ticket urgency: 5 levels, from very low to critical. I picked a public set with [61,765 tickets](../../examples/support-tickets/dataset.manifest.json) and priority labels. It turned out to be synthetic, generated by a model, and English tickets only use 3 of the 5 levels.

Then Claude labelled 1,000 of them, and [agreed with the dataset's labels on 23.8% of items](../../examples/support-tickets/report.json). Always answering the most common label [would score 41.0%](../../examples/support-tickets/report.json). Asking the question two different ways, Claude [agreed with itself on 76.5%](../../examples/support-tickets/report.json), so urgency is a genuine judgement call. But the labels were the bigger problem. I read the disagreements: a suspected breach exposing medical records was labelled "medium", and a one-line request for information was "high".

The part worth sharing is what the small classifier did with them. MiniLM [agreed with the synthetic labels on 55.6%](../../examples/support-tickets/report.json), well above Claude. It had learned the generator's habits, which have little to do with urgency. If I'd trusted the dataset, the headline would have been "a 22M model beats Claude at triage", and it would have been wrong.

So I scored this example against Claude's answers instead, asking whether a local model can stand in for the frontier call. For the local models the answer is no. At a 20% disagreement target, [laya-en, laya-typed-decisions and Von each kept 0.1% of decisions local, and the fine-tuned Laya found no threshold at all](../../examples/support-tickets/report.json). On judgement calls like this, the small models can't do the frontier model's work. One model did better, and it wasn't local.

## Jev, the hosted model

Jev is TypeSafe's hosted decision model, and it answers the same `/v1/systemone` contract Tau serves. The responses identify it as `jev-1.13.0`. I listed it in the same specs as the local models, so it saw the same items, the same calibration split, the same threshold rule and the same cascade code. The only difference is the network in between. A thousand calls per split cost an estimated [$0.14 for Banking77](../../examples/banking77/report.json) and [$0.04 for the tickets](../../examples/support-tickets/report.json), from the tokens each response reported and the published price of [$0.042 per million input tokens](../../examples/banking77/report.json). Pennies, but it's spend, so it's in the report.

On Banking77 it's a solid generalist. It scored [79.2% held-out out of the box, ahead of laya-en's 37.2% and Von's 77.1%](../../examples/banking77/report.json). Its confidence was more honest than theirs from the start, [an ECE of 0.093 before calibration and 0.029 after](../../examples/banking77/report.json). As the first stage of the cascade it [kept 51.3% of decisions at 93.6% blended accuracy, for an estimated £1,952 per million against £3,898](../../examples/banking77/report.json). [Those £1,952 include Jev's own calls](../../examples/banking77/report.json), since the first stage isn't free here.

But the fine-tuned Laya beat it, [87.3% against 79.2%, and kept 73.0% local against Jev's 51.3%](../../examples/banking77/report.json). And MiniLM beat both. On a fixed task with training data, the model you trained on it wins.

Two things to know before you gate on Jev's numbers. It returns [probabilities rounded to 2 decimal places](../DECISIONS.md), whatever you ask for. And a hosted endpoint can't load your calibrator, so its calibrated figures are the Workbench applying the calibrator offline. In production you'd apply it on your side of the call.

The tickets are where it earns its place. Scored against Claude, Jev [agreed on 52.5% of items out of the box, ahead of every local model, with Von's 43.9% the closest](../../examples/support-tickets/report.json). It needed calibrating here ([ECE 0.288, down to 0.122](../../examples/support-tickets/report.json)). After that it's the only model besides Claude that takes a real share: at the 20% target it [kept 39.8% of decisions, the served answers agreed with Claude on 89.3%, and the estimated bill fell from £997 to £614 per million](../../examples/support-tickets/report.json). It also agreed with the synthetic labels on only [18.9%](../../examples/support-tickets/report.json), below Claude's 23.8%, which I'll take as one more vote against those labels.

Its median response took [223 ms](../../examples/banking77/report.json), measured over the network from my desk. That includes the round trip, so it isn't comparable with the local figures further down.

So the order on Banking77 is MiniLM, then the fine-tuned Laya, then Jev, then the open models out of the box. On urgency, Jev is the one thing I measured that takes work off Claude.

## Other misses

- **I fitted the calibrators on rounded numbers.** The Runtime rounds every probability to 4 decimal places, and the Workbench fitted its calibrators on that rounded output. The Runtime then applied them to the unrounded probabilities, so the fit and the use saw different inputs. With 77 options, most probabilities round to zero, so it mattered. The first version of this piece quoted [laya-en's calibrated ECE as 0.065 and Von's as 0.039](../DECISIONS.md). Fitted on full precision they're [0.056 and 0.021](../../examples/banking77/report.json). I'd also written that the Workbench and the Runtime agree within 2.4e-4, which I'd only checked on inputs that didn't saturate. The rebuilt test includes 77-option items and skips nothing, and the [decisions log](../DECISIONS.md) has the whole story.
- **Three models on one card slowed the last one to seconds per request.** With laya-en, Von and the fine-tuned Laya resident in one Runtime (all FP32, on 12 GB), the fine-tune was measured last and had [a median of 15,219 ms per request](../../examples/banking77/runs/laya-en-ft-banking77/heldout.raw.summary.json) in its raw phase, against [243 ms](../../examples/banking77/runs/laya-en-ft-banking77/heldout.calibrated.summary.json) in the calibrated phase's fresh Runtime. It happened on two runs. VRAM spill is the likely cause, in line with the [all-models-resident stalls from the first release](../DECISIONS.md), but I haven't proven it. Accuracy isn't affected, and the cascade latency in the report comes from the clean calibrated phase.
- **Calibration barely helped the fine-tuned Laya on Banking77:** [ECE went from 0.071 to 0.060](../../examples/banking77/report.json). Fine-tuning had already fitted its temperature, so there was little left to fix.
- **laya-en on the tickets missed the 50% calibration target:** [ECE went from 0.273 to 0.155, 43% lower](../../examples/support-tickets/report.json). The Workbench picks the calibrator with the lower log loss on the calibration split. Here that picked isotonic regression, even though temperature scaling had [a calibration-split ECE of 0.016 against 0.216](../../examples/support-tickets/calibrators/laya-en/calibration-summary.json). I set that rule before seeing any held-out result, so I didn't change it afterwards. The report explains the miss instead.
- **Von's tickets calibration moved [ECE from 0.045 to 0.042](../../examples/support-tickets/report.json).** It was already close to calibrated.
- **My own report misled me.** Its summary first quoted the cascade of the first model in the list [(Von, 4.5% local at the time)](../DECISIONS.md) rather than the best one, and I repeated that to myself before reading the table. The summary now quotes the best cascade, and the fix has a test.

## How fast is it?

On the 3080 Ti with CUDA, laya-en answers [one question in 18.59 ms and ten questions about one state in 114.11 ms](../../reports/r1/latency.md). On the i9-11900K's CPU, the single question takes [529.21 ms](../../reports/r1/latency.md). All of those are FP32 medians with no FP16 tricks. [Jev's 223 ms](../../examples/banking77/report.json) includes the network, so I've kept it out of this comparison.

## How the numbers were made

- **Hardware:** one RTX 3080 Ti (12 GB), i9-11900K, Windows 10, .NET 10, ONNX Runtime 1.24.4, FP32.
- **Data:** Banking77 at a pinned commit, [split with seed 42 into 9,003 training, 1,000 calibration and 1,000 held-out items](../../examples/banking77/dataset.manifest.json). The tickets set at a pinned revision, English only, deduplicated. No ticket text is published.
- **Frontier labels:** Claude Opus 5.5, answered in an interactive Claude Code session by subagents working through sheets of up to 200 items, each item judged on its own. No API calls were made. Every answer is cached with provenance and committed, so the reports rebuild without asking again. A model answering 200 items in one context isn't the same as 200 separate calls, and I'd rather say so than have you find it.
- **Hosted endpoint:** Jev, called over the network from my machine. The key is read from an environment variable at run time and never written anywhere. A budget guard stops the run before spend passes the spec's limit, and neither run came near it.
- **ONNX parity:** every model export was checked against the PyTorch reference, including both fine-tunes, [within a logit tolerance of 0.002](../../examples/banking77/finetune-parity.json).
- **Prices:** Anthropic's list prices on 27 September 2026, converted at the ECB rate of 25 September. Jev at its published input price. Estimates, clearly labelled, in every report.

## Reproduce it

The repo holds both worked examples with their reports, calibrators, cached frontier answers and per-item results. On a machine with the models fetched, one command reruns an example end to end and rewrites its report:

```
./scripts/examples.ps1 -Example banking77
```

Re-measuring Jev needs your own TypeSafe key in `TYPESAFE_API_KEY`, and costs a few pence. Read [the Banking77 report](https://fortitude-omnis.group/rd/files/tau/banking77.html) and [the tickets report](https://fortitude-omnis.group/rd/files/tau/support-tickets.html) first. They carry the misses in more detail than I've managed here, and they were harder on me than I was.
