<#
.SYNOPSIS
  Single entry point: build the solution (gate: 0 errors), run every .NET test, run the sidecar tests.
.PARAMETER NoModels
  Exclude tests tagged Category=Models (for a machine without the fetched checkpoints).
#>
[CmdletBinding()]
param([switch] $NoModels)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/..").Path
Push-Location $root
try {
    dotnet build Tau.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw "build failed" }

    $filter = if ($NoModels) { @('--filter', 'Category!=Models') } else { @() }
    dotnet test Tau.slnx -c Release --no-build @filter
    if ($LASTEXITCODE -ne 0) { throw ".NET tests failed" }

    Push-Location sidecar/finetune
    try {
        uv run --frozen pytest -q
        if ($LASTEXITCODE -ne 0) { throw "sidecar tests failed" }
    } finally { Pop-Location }
    Write-Host "build-test: all green."
} finally { Pop-Location }
