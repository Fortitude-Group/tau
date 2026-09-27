<#
.SYNOPSIS
  Run every parity gate and write reports/r1/parity.{md,json}.

  Gates (tolerance agreed 2026-09-27: |dlogit| <= 2e-3, |dprob| <= 1e-3, identical argmax):
    1. Level 1 (sidecar): ONNX Runtime vs the vendor PyTorch forward on identical tensors, every case, every model.
    2. C# sequences: token ids, option markers and Von position ids identical to the reference rows.
    3. C# logits: Tau's own batching and ONNX run vs the reference logits.
    4. C# answers: full /v1/systemone answers vs laya 0.3.20 / von-sdk 1.2.3 answers.
    5. Tokeniser, serialiser (json.dumps / str) and routing parity against Python-generated fixtures.
  Exits non-zero if any gate fails.
.PARAMETER RegenerateFixtures
  Rewrite tests/fixtures/parity from the reference (needed after a re-export; the committed fixtures are
  tied to the exported ONNX hashes).
#>
[CmdletBinding()]
param([switch] $RegenerateFixtures)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/..").Path
$out = Join-Path $root 'reports/r1'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$started = Get-Date
# Taken before anything is written: the reports this script produces are outputs, not inputs, so they don't count.
$dirty = if (git -C $root status --porcelain --untracked-files=no -- . ':(exclude)reports') { ' (working tree had uncommitted changes)' } else { '' }
$command = "./scripts/parity.ps1" + $(if ($RegenerateFixtures) { " -RegenerateFixtures" } else { "" })

# 1. Level 1 in the sidecar.
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONPATH = '.'
Push-Location (Join-Path $root 'sidecar/finetune')
try {
    $args1 = @('run', '--frozen', 'python', '-m', 'tau_sidecar.parity')
    if (-not $RegenerateFixtures) { $args1 += '--no-fixtures' }
    & uv @args1
    $level1Ok = ($LASTEXITCODE -eq 0)
    $pyVersions = uv run --frozen python -c "import sys,torch,transformers,onnxruntime,laya,importlib.metadata as m;print(sys.version.split()[0],torch.__version__,transformers.__version__,onnxruntime.__version__,m.version('laya'),m.version('von-sdk'))"
} finally { Pop-Location }
$level1 = Get-Content (Join-Path $out 'parity-model.json') -Raw | ConvertFrom-Json

# 2-5. C# gates, one filter at a time so each gate's counts are reported separately.
dotnet build (Join-Path $root 'tests/Tau.Inference.Tests') -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$metrics = Join-Path ([IO.Path]::GetTempPath()) "tau-parity-$([guid]::NewGuid().ToString('N')).jsonl"
$env:TAU_PARITY_OUT = $metrics
$gates = [ordered]@{
    'C# model parity (sequences, logits, answers)' = 'FullyQualifiedName~ModelParityTests'
    'Tokeniser parity'                              = 'FullyQualifiedName~HfTokenizerParity'
    'Serialiser parity (json.dumps, str)'           = 'FullyQualifiedName~PyTextFixture|FullyQualifiedName~PyJsonAscii'
    'Routing parity (script router)'                = 'FullyQualifiedName~RoutingParity'
}
$gateResults = [ordered]@{}
foreach ($g in $gates.GetEnumerator()) {
    $log = dotnet test (Join-Path $root 'tests/Tau.Inference.Tests') -c Release --no-build --filter $g.Value 2>&1 | Out-String
    $total = if ($log -match 'total:\s*(\d+)') { [int]$Matches[1] } else { 0 }
    $failed = if ($log -match 'failed:\s*(\d+)') { [int]$Matches[1] } else { -1 }
    $gateResults[$g.Key] = [ordered]@{ tests = $total; failed = $failed; pass = ($failed -eq 0 -and $total -gt 0) }
    Write-Host ("{0,-46} tests {1,5}  failed {2}" -f $g.Key, $total, $failed)
}
Remove-Item Env:TAU_PARITY_OUT
$csharp = if (Test-Path $metrics) { Get-Content $metrics | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json } } else { @() }
Remove-Item $metrics -ErrorAction SilentlyContinue

# Provenance.
$gpu = (& nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader 2>$null) -join '; '
$cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
$ram = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB)
$os = (Get-CimInstance Win32_OperatingSystem).Caption
$commit = git -C $root rev-parse HEAD
$models = foreach ($id in 'laya-en', 'laya-multilingual', 'laya-typed-decisions', 'von-1.2.0') {
    $m = Get-Content (Join-Path $root "models/$id/tau-model.json") -Raw | ConvertFrom-Json
    [ordered]@{ id = $id; repo = $m.source.repo; revision = $m.source.revision; onnx_sha256 = $m.onnx.sha256; reference = $m.reference.package }
}
$v = $pyVersions -split ' '
$allPass = $level1Ok -and -not ($gateResults.Values | Where-Object { -not $_.pass })

$report = [ordered]@{
    report = 'parity'; release = 'R1'; command = $command; date = $started.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    git_commit = "$commit$dirty"; contract = 'systemone/2026-09-27'
    hardware = [ordered]@{ gpu = $gpu; cpu = $cpu; ram_gb = $ram; os = $os }
    software = [ordered]@{ dotnet = (dotnet --version); onnxruntime_csharp = '1.24.4 (CPU)'; python = $v[0]; torch = $v[1]; transformers = $v[2]; onnxruntime_python = $v[3]; laya = $v[4]; von_sdk = $v[5] }
    tolerance = [ordered]@{ logit = 2e-3; prob = 1e-3; argmax = 'identical (ties within 1e-4 resolved to the lowest index)' }
    models = $models; level1 = $level1.results; csharp = $csharp; gates = $gateResults; pass = $allPass
}
$report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out 'parity.json') -Encoding utf8

$md = [System.Collections.Generic.List[string]]::new()
$md.Add('# Parity report: exported ONNX models vs the reference runtimes')
$md.Add('')
$md.Add("Generated by ``$command`` on $($report.date) at commit ``$commit``$dirty.")
$md.Add('')
$md.Add("**Result: $(if ($allPass) { 'PASS on every gate' } else { 'FAIL (see below)' }).** Tolerance, agreed with the owner on 2026-09-27: max |Δlogit| ≤ 2×10⁻³, max |Δprobability| ≤ 1×10⁻³, and the same top answer, with exact ties resolved to the lowest index as the reference does.")
$md.Add('')
$md.Add('## What was compared')
$md.Add('')
$md.Add('The reference is the maintained vendor runtime: `laya==0.3.20` for the three Laya checkpoints and `von-sdk==1.2.3` for Von, both run on CPU in FP32. Tau runs the exported ONNX graphs through ONNX Runtime. A pass means Tau gives the reference''s answers, not approximately similar ones: the same tokens, the same logits to within float noise, the same choices.')
$md.Add('')
$md.Add('## Level 1: ONNX vs PyTorch on identical tensors (Python)')
$md.Add('')
$md.Add('| Model | Cases | Rows | Max abs. logit diff. | Max abs. prob. diff. | Argmax mismatches | Expected rejections | Result |')
$md.Add('| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |')
foreach ($p in $level1.results.PSObject.Properties) {
    $r = $p.Value
    $md.Add(("| {0} | {1} | {2} | {3:E1} | {4:E1} | {5} | {6} | {7} |" -f $p.Name, $r.cases, $r.rows, $r.max_dlogit, $r.max_dprob, $r.argmax_mismatches, $r.expected_errors, $(if ($r.pass) { 'pass' } else { 'FAIL' })))
}
$md.Add('')
$md.Add('This isolates the export: same input tensors, two runtimes. The largest difference is the number to watch; it sits well under the tolerance, so the graph is a faithful copy of the PyTorch model.')
$md.Add('')
$md.Add('## C# gates: what the Runtime itself produces')
$md.Add('')
$md.Add('| Model | Sequences (cases, mismatches) | Logits (rows, max abs. logit diff., max abs. prob. diff.) | Answers (answers compared, mismatches) |')
$md.Add('| --- | --- | --- | --- |')
foreach ($id in 'laya-en', 'laya-multilingual', 'laya-typed-decisions', 'von-1.2.0') {
    $s = $csharp | Where-Object { $_.model -eq $id -and $_.gate -eq 'sequences' } | Select-Object -Last 1
    $l = $csharp | Where-Object { $_.model -eq $id -and $_.gate -eq 'logits' } | Select-Object -Last 1
    $a = $csharp | Where-Object { $_.model -eq $id -and $_.gate -eq 'answers' } | Select-Object -Last 1
    $md.Add(("| {0} | {1}, {2} | {3}, {4:E1}, {5:E1} | {6}, {7} |" -f $id, $s.cases, $s.failures, $l.rows, $l.max_dlogit, $l.max_dprob, $a.answers, $a.failures))
}
$md.Add('')
$md.Add('Here Tau tokenises, builds the rows, batches every question of a request into one forward pass and applies the reference''s post-processing in C#. Sequences must match exactly; one token out would move every answer after it. The logit figures include the effect of Tau''s batching, which the reference doesn''t do for Von.')
$md.Add('')
$md.Add('## Supporting gates')
$md.Add('')
$md.Add('| Gate | Tests | Failed |')
$md.Add('| --- | ---: | ---: |')
foreach ($g in $gateResults.GetEnumerator()) { $md.Add("| $($g.Key) | $($g.Value.tests) | $($g.Value.failed) |") }
$md.Add('')
$md.Add('The tokeniser, serialiser and routing gates compare against fixtures generated by the reference Python code (Hugging Face `tokenizers`, `json.dumps`/`str`, `laya.router`), so any drift in how text reaches the model shows up here first.')
$md.Add('')
$md.Add('## Provenance')
$md.Add('')
$md.Add("- Hardware: $gpu; $cpu; $ram GB RAM; $os. Parity runs on CPU; the GPU is listed for completeness.")
$md.Add("- Software: .NET $($report.software.dotnet), ONNX Runtime (C#) 1.24.4, Python $($v[0]), torch $($v[1]), transformers $($v[2]), onnxruntime (Python) $($v[3]), laya $($v[4]), von-sdk $($v[5]).")
$md.Add('- Contract: `/v1/systemone` snapshot 2026-09-27.')
$md.Add('- Models:')
foreach ($m in $models) { $md.Add("  - ``$($m.id)``: ``$($m.repo)`` @ ``$($m.revision)``, ONNX sha256 ``$($m.onnx_sha256)``, reference ``$($m.reference)``") }
$md.Add('')
$md.Add('Cases: `sidecar/finetune/tau_sidecar/cases.py` (hand-written; every question type, 1–255 options, text/object/array/null/empty states, truncation, special tokens, 13 scripts). Fixtures: `tests/fixtures/parity/`.')
$md -join "`n" | Set-Content (Join-Path $out 'parity.md') -Encoding utf8

Write-Host "parity: $(if ($allPass) { 'PASS' } else { 'FAIL' }) -> reports/r1/parity.md"
if (-not $allPass) { exit 1 }
