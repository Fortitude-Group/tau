<!--
Ready to post. HN renders plain text only, so the numbers carry no inline links. The report URLs are listed at the end of the post text and of the first comment. Paste the title into the title field and the URL into the URL field, then post the first comment straight away.
-->

# Show HN: Tau, measuring when a local model can stand in for a frontier LLM

**URL:** https://github.com/Fortitude-Group/tau

**Text:**

I ran a thousand Banking77 support messages through a local model first and only sent the ones it wasn't sure about to Claude Opus 5.5. With a fine-tuned Laya decision model on my RTX 3080 Ti, 73.0% of decisions stayed local, blended accuracy was 93.3% against 94.2% for Claude alone, and the estimated cost fell from £3,898 to £1,054 per million decisions. That's one dataset (Banking77), one card, one night (27 to 28 September 2026). The £ figures are list-price estimates, not invoices. Claude's answers came from an interactive Claude Code session working through batched answer sheets, not from the API.

The better result wasn't mine. A plain fine-tuned MiniLM classifier kept 94.7% local at the same 94.2% blended accuracy, and beat every decision model I tested on accuracy and calibration, TypeSafe's hosted Jev included.

Tau is two things, both Apache-2.0. A .NET Runtime that serves the `/v1/systemone` decision contract locally through ONNX Runtime. And a command-line Workbench that measures accuracy and calibration against any such endpoint, local or hosted, fits calibrators, picks a threshold, simulates the cascade and prices it. It writes an HTML report with the misses left in.

The second dataset went badly, and I think it's the more useful story. Details in the first comment, along with how Jev did.

Write-up: https://fortitude-omnis.group/rd/tau/

Every number above is in the Banking77 report: https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json

## First comment

Rob here. Some detail on method, the hosted model, the misses, and the dataset that fooled me.

### Method

- One RTX 3080 Ti (12 GB), i9-11900K, Windows 10, .NET 10, ONNX Runtime 1.24.4, FP32.
- Banking77 at a pinned commit, split with seed 42 into 9,003 training, 1,000 calibration and 1,000 held-out items. Calibrators and the threshold are fitted on the calibration split. Every figure above is on held-out.
- The threshold is 0.95, picked on the calibration split for a 5% target error. Above it the local answer stands. Below it the item goes to Claude.
- Frontier labels: Claude Opus 5.5, answered in an interactive Claude Code session working through sheets of up to 200 items, each item judged on its own. No API calls. Every answer is cached and committed, so the reports rebuild without asking again. A model answering 200 items in one context isn't the same as 200 separate calls, and I'd rather say so up front.
- Prices: Anthropic's list prices on the day, converted at the ECB rate. Each Banking77 prompt is about 1,259 input tokens once you list all the intents. Tokens are estimated from characters, not counted.
- Every ONNX export, both fine-tunes included, was checked against the PyTorch reference within a logit tolerance of 0.002.

### Calibration

Out of the box, laya-en was 37.2% accurate with an ECE of 0.502. Its stated confidence sat about 50 points away from its hit rate. That's the failure I built this to catch: a confidence score that looks certain and means nothing. A small calibrator fitted on the calibration split took its ECE from 0.502 to 0.056, and Von's from 0.185 to 0.021. Accuracy didn't move. What changed is that the number became something you can threshold.

One contract detail. Laya's `confidence` field for choice questions is derived from the entropy of the whole distribution, so it isn't a calibrated probability. All my calibration figures use the probability of the chosen answer instead.

### Jev, the hosted model

I measured TypeSafe's hosted Jev (the responses say jev-1.13.0) on the same items with the same code, over the network. Both datasets together cost an estimated $0.18 in API calls: about $0.14 for Banking77 and $0.04 for the tickets, at the published $0.042 per million input tokens.

- Banking77: 79.2% held-out out of the box. That's ahead of raw laya-en (37.2%) and Von (77.1%), behind the fine-tuned Laya (87.3%), MiniLM (91.5%) and Claude (94.2%).
- Its confidence starts out fairly honest: ECE 0.093 raw, 0.029 after an offline calibration.
- As the first stage of the cascade it kept 51.3% at 93.6% blended, £1,952 against £3,898 per million, with its own calls priced in.
- On the tickets, scored against Claude, it agreed on 52.5% out of the box, ahead of every local model (Von's 43.9% was closest). At the 20% target it kept 39.8% of decisions with the served answers agreeing with Claude on 89.3%, £614 against £997 per million. It's the only non-frontier model that took a real share there.
- It returns probabilities rounded to 2 decimal places, and a hosted endpoint can't load your calibrator, so its calibrated figures are the Workbench applying one offline.
- Median latency was 223 ms, measured over the network from my desk. Not comparable with local inference.

So: on a fixed task with training data, the model you trained wins, and the classic classifier wins outright. On a judgement call, the hosted generalist is the only thing that takes work off Claude.

### The benchmark that lied

The second dataset was meant to be support-ticket urgency, five levels from very low to critical. I picked a public set of 61,765 tickets with priority labels. It turned out to be synthetic, generated by a model, and the English tickets only use three of the five levels.

Claude labelled 1,000 of them and agreed with the dataset's labels on 23.8% of items. Always answering the most common label would score 41.0%. Asked two different ways, Claude agreed with itself on 76.5%, so urgency is a judgement call. The labels were the bigger problem. I read the disagreements. A suspected breach exposing medical records was labelled "medium". A one-line request for information was "high".

The fine-tuned MiniLM agreed with those synthetic labels on 55.6%, well above Claude. It had learned the generator's habits, which have little to do with urgency. If I'd trusted the dataset, my headline would have been "small model beats Claude at triage", and it would have been wrong.

So I scored that example against Claude's answers instead and asked whether a local model can stand in for the frontier call. None can. At a 20% disagreement target, three decision models kept 0.1% of decisions local and the fine-tuned Laya found no threshold at all.

### The other misses

- I fitted the calibrators on rounded numbers. The Runtime rounds probabilities to 4 dp, the Workbench fitted on that, and the Runtime then applied the calibrators to unrounded values. With 77 options most probabilities round to zero, so it mattered. My earlier figures (laya-en calibrated ECE 0.065, Von 0.039) came from that. I'd also claimed the Workbench and Runtime agreed within 2.4e-4, measured only on inputs that didn't saturate. Refitted on full precision, the numbers above are the corrected ones.
- With three FP32 models resident on the 12 GB card, the model measured last slowed to seconds per request: the fine-tuned Laya's raw-phase median was 15,219 ms, against 243 ms in a fresh Runtime. It happened on two runs. VRAM spill is my best guess, not proven. Accuracy is unaffected, and the published cascade latency comes from the clean phase.
- Raw laya-en never got there on Banking77. No threshold got its error below 5%, which is why I fine-tuned it.
- The fine-tuned MiniLM beat every decision model: 91.5% held-out accuracy and a calibrated ECE of 0.024, against 87.3% for the fine-tuned Laya. It isn't served through Tau, so its cascade leaves out local latency and energy. My first MiniLM run scored 80.8% because I stopped it too early, and I retrained it before believing anything.
- Calibration barely helped the fine-tuned Laya: ECE went from 0.071 to 0.060. Fine-tuning had already fitted its temperature.
- laya-en on the tickets missed my 50% calibration target: ECE went from 0.273 to 0.155. The Workbench picks the calibrator with the lower log loss on the calibration split, and that picked isotonic even though temperature scaling had a calibration-split ECE of 0.016 against 0.216. I set the rule before seeing held-out results, so I left it alone.
- Von on the tickets went from ECE 0.045 to 0.042. Already close to calibrated.
- My own report summary first quoted the cascade of the first model in the list, not the best one, and I believed it for a while. Fixed, with a test.
- Claude disagreed with the Banking77 labels on 5.8% of items. Some are Claude's mistakes and some are the dataset's, so every accuracy figure carries that much slack.

### Latency

On the 3080 Ti with CUDA, laya-en answers one question in 18.59 ms and ten questions about one state in 114.11 ms. On the CPU it's 529.21 ms for one question. FP32 medians, no FP16.

### What I'd like feedback on

- Whether choosing the calibrator by calibration-split log loss is the right rule, given the tickets case where it picked the worse one by ECE.
- Whether the batched-sheet frontier labels bother you enough that I should pay for proper per-item API runs before anyone relies on these numbers.
- Where you'd expect decision models to beat a fine-tuned encoder. My guess is untrained questions and many questions against one state, and I haven't measured either yet.

Repo: https://github.com/Fortitude-Group/tau. `./scripts/examples.ps1 -Example banking77` reruns the Banking77 example end to end and rewrites its report. Re-measuring Jev needs your own TypeSafe key.

Sources for every number in this comment:

- Banking77 report: https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json
- Tickets report: https://github.com/Fortitude-Group/tau/blob/master/examples/support-tickets/report.json
- Tickets calibrator choice: https://github.com/Fortitude-Group/tau/blob/master/examples/support-tickets/calibrators/laya-en/calibration-summary.json
- The VRAM slowdown: https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/runs/laya-en-ft-banking77/heldout.raw.summary.json
- Latency: https://github.com/Fortitude-Group/tau/blob/master/reports/r1/latency.md
- Fine-tune parity: https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/finetune-parity.json
- Dataset splits: https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/dataset.manifest.json and https://github.com/Fortitude-Group/tau/blob/master/examples/support-tickets/dataset.manifest.json
- Decisions log (the first MiniLM run, the rounding fix, Jev's spend): https://github.com/Fortitude-Group/tau/blob/master/docs/DECISIONS.md
