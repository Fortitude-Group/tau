<#
.SYNOPSIS
  Export all four pinned checkpoints to ONNX model packages (models/<id>/model.onnx + tau-model.json).
  Needs scripts/fetch-models.ps1 first. Uses the dynamo exporter (approach B; see docs/DECISIONS.md).
.EXAMPLE
  ./scripts/export.ps1
  ./scripts/export.ps1 -Only laya-en
#>
[CmdletBinding()]
param([string[]] $Only)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/..").Path
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONPATH = '.'

Push-Location (Join-Path $root 'sidecar/finetune')
try {
    $laya = @('laya-en', 'laya-multilingual', 'laya-typed-decisions') | Where-Object { -not $Only -or $_ -in $Only }
    if ($laya) {
        uv run --frozen python -m tau_sidecar.export_laya --only @laya
        if ($LASTEXITCODE -ne 0) { throw "Laya export failed" }
    }
    if (-not $Only -or 'von-1.2.0' -in $Only) {
        uv run --frozen python -m tau_sidecar.export_von
        if ($LASTEXITCODE -ne 0) { throw "Von export failed" }
    }
} finally { Pop-Location }

foreach ($id in 'laya-en', 'laya-multilingual', 'laya-typed-decisions', 'von-1.2.0') {
    if ($Only -and $id -notin $Only) { continue }
    $m = Get-Content (Join-Path $root "models/$id/tau-model.json") -Raw | ConvertFrom-Json
    Write-Host ("{0,-22} onnx {1}  opset {2}  exporter {3}" -f $id, $m.onnx.sha256.Substring(0, 12), $m.onnx.opset, $m.onnx.exporter)
}
Write-Host "export: done. Fixtures are tied to these hashes; if any changed, run scripts/parity.ps1 -RegenerateFixtures."
