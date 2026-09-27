<!--
Note for Rob, before posting:

- Check each subreddit's self-promotion rules on the day. They change, and moderators enforce them
  differently. r/LocalLLaMA tolerates project posts that are about running models locally, as long
  as the post carries the substance. r/dotnet allows project showcases but has had self-promotion
  weeks and flair rules. The site-wide guideline is still that your own links should be a small
  part of what you post.
- r/MachineLearning only takes this as a [P] post, and its moderators are strict about low-effort
  project plugs. The method-first version below is written for that. If the rules on the day say
  [P] posts need a paper or a novel method, skip it. It's the least important of the three.
- My recommendation: post to r/LocalLLaMA first. Local latency on a gaming card and "can I stop
  paying for this call" is exactly its audience, and the MiniLM twist will get a fair hearing
  there. Then r/dotnet a day or two later, since the C# angle stands on its own. r/MachineLearning
  last, and only if the first two went down well and the rules allow it.
- Reddit renders Markdown, so the report links can stay, pointed at the files on GitHub once the
  repo is public. The repo link appears once, at the end of each post.
-->

# Reddit drafts

## r/LocalLLaMA

**Title:** I measured how many Claude classification calls a 3080 Ti can take. A fine-tuned MiniLM beat the decision models.

**Body:**

I measured how much of a Claude classification workload a local model on my RTX 3080 Ti can take, if you only keep its answer when its calibrated confidence clears a threshold. With a fine-tuned Laya decision model, [73.6% of Banking77 decisions stayed local, blended accuracy was 93.2% against 94.2% for Claude Opus 5.5 alone, and the estimated cost fell from £3,898 to £1,031 per million decisions](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json). Caveats up front: one dataset (Banking77), one card, one date (27 September 2026). The £ figures are list-price estimates. And Claude's answers came from an interactive Claude Code session working through batched answer sheets, not the API.

The surprise was the classic encoder. A fine-tuned all-MiniLM-L6-v2 [kept 94.7% local at the same 94.2% blended accuracy as Claude alone](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json), and beat every decision model on accuracy and calibration.

**Calibration.** Raw laya-en is overconfident to the point of uselessness: [37.2% accurate with an ECE of 0.502](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json). A small calibrator fitted on a separate split took [its ECE from 0.502 to 0.065](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json) without touching accuracy. Gate on the raw number and you're gating on noise. Also, Laya's `confidence` field for choice questions is entropy-derived and isn't a probability, so I calibrate the chosen answer's probability instead.

**Latency on the 3080 Ti** (CUDA, FP32 medians, ONNX Runtime from .NET): laya-en answers [one question in 18.59 ms and ten questions about one state in 114.11 ms](https://github.com/Fortitude-Group/tau/blob/master/reports/r1/latency.md). On the i9-11900K's CPU the single question takes [529.21 ms](https://github.com/Fortitude-Group/tau/blob/master/reports/r1/latency.md). No FP16 yet.

**Where it fell over.** On a synthetic support-ticket urgency set, Claude [agreed with the labels on 23.8% against a 41.0% majority baseline, and MiniLM "learned" them to 55.6%](https://github.com/Fortitude-Group/tau/blob/master/examples/support-tickets/report.json). Scored against Claude instead, [every local model kept about 0.1% of decisions local](https://github.com/Fortitude-Group/tau/blob/master/examples/support-tickets/report.json). Urgency needs the big model.

Other misses: calibration barely moved the fine-tuned Laya ([ECE 0.071 to 0.061](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json)), and laya-en missed my calibration target on the tickets ([ECE 0.273 to 0.148](https://github.com/Fortitude-Group/tau/blob/master/examples/support-tickets/report.json)) because my calibrator-selection rule picked isotonic over temperature.

Everything's open source (Apache-2.0): the server, the measuring tool, the reports with the misses left in, and one command to rerun it. Repo: https://github.com/Fortitude-Group/tau

## r/dotnet

**Title:** I built a self-hosted classification server in .NET 10 on ONNX Runtime, then measured when to trust it

**Body:**

On one RTX 3080 Ti, a fine-tuned Laya decision model served from .NET [kept 73.6% of Banking77 decisions local, at 93.2% blended accuracy against 94.2% for Claude Opus 5.5 alone, for an estimated £1,031 per million decisions against £3,898](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json). That's one dataset (Banking77), one card, one date (27 September 2026). The £ figures are list-price estimates. Claude's answers came from an interactive Claude Code session working through batched sheets, not the API.

The C# side is what I'd like opinions on.

**The Runtime** is an ASP.NET Core minimal API that answers the `/v1/systemone` decision contract. It loads ONNX exports of the Laya and Von models through the managed ONNX Runtime package, with the native build fetched per provider, and picks the CUDA, DirectML or CPU execution provider from config. It refuses to start if the provider you asked for can't load, unless you opt into a CPU fallback. I'd rather it fail loudly than run [28 times slower](https://github.com/Fortitude-Group/tau/blob/master/reports/r1/latency.md) without telling me.

**Latency**, CUDA, FP32 medians: laya-en answers [one question in 18.59 ms and ten questions about one state in 114.11 ms](https://github.com/Fortitude-Group/tau/blob/master/reports/r1/latency.md), against [529.21 ms for one question on the CPU](https://github.com/Fortitude-Group/tau/blob/master/reports/r1/latency.md).

**The client** is a typed wrapper that maps a choice question onto an enum:

```csharp
using Tau.Client;

using var client = new SystemOneClient(new Uri("http://localhost:8088/"));

var decision = await client.DecideAsync<Category>(
    state: "My card payment failed three times this morning and I need it sorted today.",
    instructions: "Which support category fits this message?");

Console.WriteLine($"{decision.Value} p={decision.Probabilities[decision.Value]:F2}");

enum Category { Billing, Technical, Account }
```

`ScoreAsync` and `NoulAsync` do the same for an ordered scale and a yes/no question.

**The Workbench** is a .NET CLI (`tau`) that measures any `/v1/systemone` endpoint, fits calibrators, picks a threshold and prices a local-first cascade. It's how I found out raw laya-en's confidence is close to meaningless ([ECE 0.502, down to 0.065 after calibration](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json)), and that a plain fine-tuned MiniLM classifier beat my models, [keeping 94.7% local](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json).

It also caught a bad dataset. On synthetic support tickets, Claude [agreed with the labels on 23.8% against a 41.0% majority baseline, and no local model kept more than about 0.1% of decisions](https://github.com/Fortitude-Group/tau/blob/master/examples/support-tickets/report.json) once I scored against Claude instead.

Apache-2.0, .NET 10, tests included. Repo: https://github.com/Fortitude-Group/tau

## r/MachineLearning

**Title:** [P] Calibrate, threshold, cascade: measuring how much of a frontier classification workload a local model can take

**Body:**

**Method.** For a fixed classification question, I measure each local model on a calibration split and a held-out split, fit temperature and isotonic calibrators on the calibration split, and pick the calibrator with the lower calibration-split log loss. I then choose the lowest confidence threshold whose calibration-split error meets a target ([5% for Banking77](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json)), and judge it only on held-out. The cascade keeps the local answer above the threshold and uses the frontier model's answer below it. ECE is on max(p), the probability of the chosen answer, since the contract's own `confidence` field is entropy-derived. Frontier labels are Claude Opus 5.5, answered in an interactive Claude Code session through batched sheets of up to 200 items, each item judged on its own, not through the API. Every answer is cached and committed.

**Setup.** Banking77 at a pinned commit, [split with seed 42 into 9,003 training, 1,000 calibration and 1,000 held-out items](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/dataset.manifest.json). One RTX 3080 Ti, ONNX Runtime, FP32. Local models: laya-en, Von, a fine-tuned laya-en and, as a baseline, a fine-tuned all-MiniLM-L6-v2 through identical code.

**Results.** The fine-tuned Laya [kept 73.6% of decisions local at 93.2% blended accuracy against 94.2% for Claude alone, for an estimated £1,031 per million decisions against £3,898](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json). That's one dataset (Banking77), one card, one date (27 September 2026), £ figures from list prices, and frontier labels from batched interactive sheets, not per-item API calls. The MiniLM baseline did better: [91.5% held-out accuracy, calibrated ECE 0.024, and 94.7% kept local at 94.2% blended](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json). Calibration took raw laya-en from [ECE 0.502 to 0.065](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json). Claude [disagreed with the Banking77 gold labels on 5.8% of items](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json), which bounds all of the above.

**Negative results.**

- On a synthetic support-ticket urgency set, Claude [agreed with the gold labels on 23.8% against a 41.0% majority baseline, while MiniLM fitted them to 55.6%](https://github.com/Fortitude-Group/tau/blob/master/examples/support-tickets/report.json). It had learned the generator's habits. Scored against Claude, [every model kept about 0.1% local at a 20% disagreement target](https://github.com/Fortitude-Group/tau/blob/master/examples/support-tickets/report.json).
- Calibration barely helped the fine-tuned Laya: [ECE 0.071 to 0.061](https://github.com/Fortitude-Group/tau/blob/master/examples/banking77/report.json).
- The log-loss selection rule picked isotonic for laya-en on the tickets, although temperature had [a calibration-split ECE of 0.016 against 0.201](https://github.com/Fortitude-Group/tau/blob/master/examples/support-tickets/calibrators/laya-en/calibration-summary.json), so held-out ECE only fell [from 0.273 to 0.148](https://github.com/Fortitude-Group/tau/blob/master/examples/support-tickets/report.json). The rule was fixed before any held-out result and I left it.

**Open questions** I'd value input on: whether log loss or ECE should select the calibrator when they disagree, and how much batched-context frontier labelling biases the reference against per-item calls.

Code, reports and cached labels: https://github.com/Fortitude-Group/tau
