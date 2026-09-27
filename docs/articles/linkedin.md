<!--
Ready to post. Attach https://fortitude-omnis.group/images/rd/tau-banking77-cascade.png (also docs/articles/img/banking77-cascade.png) as the one image, with this alt text: "The cascade table from the Banking77 report: for each local model, the confidence threshold, the share of decisions kept local, blended accuracy, and estimated cost per million decisions against Claude alone". The write-up URL is the only link.
-->

# How many AI decisions need a frontier model?

*Rob Hill, Fortitude Omnis*

Fewer than I'd assumed. I ran a thousand banking support messages from the Banking77 benchmark through a model on my own RTX 3080 Ti first, and only sent the uncertain ones to Claude Opus 5.5. With a fine-tuned Laya model doing the local work, 73.6% of decisions stayed local, accuracy was 93.2% against 94.2% for Claude alone, and the estimated cost fell from £3,898 to £1,031 per million decisions. One dataset, one card, measured on 27 September 2026. The £ figures are list-price estimates, not invoices. Claude's answers came from an interactive session working through batched sheets, not the API.

The best local model wasn't mine. A plain fine-tuned MiniLM classifier kept 94.7% local at the same accuracy as Claude alone.

And it doesn't always work. On support-ticket urgency, no local model kept more than about 0.1% of decisions. Judgement calls still need the big model.

The server, the measuring tool and every report are open source. Write-up, misses included: https://fortitude-omnis.group/rd/tau/


#MachineLearning #DotNet #LLM
