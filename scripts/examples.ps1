<#
.SYNOPSIS
    Runs one worked example (T060) end to end: checks its data and model packages, builds Release, starts the Tau
    Runtime on CUDA serving exactly the spec's models, runs `tau run`, then restarts the Runtime with the example's
    calibrators and runs the calibrated phase and the report.

.DESCRIPTION
    1. Checks data/<dataset>/{calibration,heldout,finetune}.jsonl and examples/<name>/dataset.manifest.json exist.
       If not, it prints the sidecar command that prepares them and exits 1. It never prepares data itself.
    2. Checks every model in the spec's `models` list is packaged as models/<id>/model.onnx + tau-model.json. If one
       is missing, it prints the fetch, export or fine-tune command and exits 1. It never trains or exports itself.
       -CheckOnly stops here, exiting 0 when everything is present.
    3. Builds Tau.Runtime and the tau CLI in Release, and starts the built Tau.Runtime.dll (not `dotnet run`, so the
       process it stops is the server itself) with --Tau:Provider=cuda --Tau:AllowCpuFallback=false on the port of
       the spec's `endpoint` (18093 when the spec has none, or -Port). It waits for GET /v1/models and checks every
       needed model is listed. If the port is taken, it stops: it never frees a port by killing anything.
    4. Runs `tau run <spec>`. Exit code 2 means blocked (for example frontier answers pending): the reason is
       printed and the script stops with exit code 2.
    5. Unless -SkipCalibrated: stops the Runtime, restarts it with Tau:CalibratorsDirectory=examples/<name>/calibrators
       (the Runtime loads the per-model subfolders), then runs `tau measure <spec> --phase calibrated` and
       `tau report <spec>`.
    6. In a finally block, stops only the Runtime process this script started. Prints where report.html is.

.PARAMETER Example
    banking77 or support-tickets (a folder under examples/).

.PARAMETER Port
    The Runtime's port. Defaults to the port in the spec's `endpoint`, or 18093. When it differs from the spec's
    endpoint, the tau commands are given --endpoint.

.PARAMETER SkipCalibrated
    Stop after `tau run`, without the calibrated phase and the final report.

.PARAMETER CheckOnly
    Only run the data and model checks (steps 1-2), exiting 0 when everything is present.

.EXAMPLE
    ./scripts/examples.ps1 -Example banking77

.EXAMPLE
    ./scripts/examples.ps1 -Example support-tickets -CheckOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('banking77', 'support-tickets')]
    [string]$Example,
    [int]$Port,
    [switch]$SkipCalibrated,
    [switch]$CheckOnly,
    [int]$RuntimeStartupTimeoutSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

$exampleDir = Join-Path $repoRoot "examples/$Example"
$specPath = Join-Path $exampleDir 'decision.yaml'
if (-not (Test-Path $specPath)) { throw "No spec at $specPath" }
$specRel = "examples/$Example/decision.yaml"

# --- read the three fields this script needs from decision.yaml (flat YAML, no module needed) ---
$specLines = Get-Content $specPath
function Get-TopLevelValue {
    param([string]$Key)
    foreach ($line in $specLines) {
        if ($line -match "^$([regex]::Escape($Key)):\s*(.*?)\s*(#.*)?$") { return $Matches[1].Trim('"', "'") }
    }
    return $null
}

$modelsValue = Get-TopLevelValue 'models'
if (-not $modelsValue -or $modelsValue -notmatch '^\[(.*)\]$') {
    throw "$specRel`: expected an inline list 'models: [a, b]'; found '$modelsValue'"
}
$models = @($Matches[1].Split(',') | ForEach-Object { $_.Trim().Trim('"', "'") } | Where-Object { $_ })
if ($models.Count -eq 0) { throw "$specRel lists no models" }

$dataset = $null
$inData = $false
foreach ($line in $specLines) {
    if ($line -match '^data:\s*$') { $inData = $true; continue }
    if ($inData -and $line -match '^\S') { break }
    if ($inData -and $line -match '^\s+dataset:\s*"?([^"#\s]+)"?') { $dataset = $Matches[1]; break }
}
if (-not $dataset) { throw "$specRel has no data.dataset" }

$specEndpoint = Get-TopLevelValue 'endpoint'
$specPort = 18093
if ($specEndpoint) { $specPort = ([Uri]$specEndpoint).Port }
if (-not $PSBoundParameters.ContainsKey('Port')) { $Port = $specPort }
if ($Port -eq 8080) { throw 'Port 8080 is reserved on this machine; pass another -Port.' }
$endpointArgs = @()
if ($Port -ne $specPort) { $endpointArgs = @('--endpoint', "http://localhost:$Port") }

Write-Host "=== $Example`: dataset '$dataset', models $($models -join ', '), port $Port ==="

# --- 1. data ---
$dataDir = Join-Path $repoRoot "data/$dataset"
$missingData = @()
foreach ($split in 'calibration', 'heldout', 'finetune') {
    $f = Join-Path $dataDir "$split.jsonl"
    if (-not (Test-Path $f)) { $missingData += "data/$dataset/$split.jsonl" }
}
if (-not (Test-Path (Join-Path $exampleDir 'dataset.manifest.json'))) { $missingData += "examples/$Example/dataset.manifest.json" }
if ($missingData.Count -gt 0) {
    $module = if ($dataset -eq 'banking77') { 'data_banking77' } else { 'data_tickets' }
    Write-Host "missing: $($missingData -join ', ')" -ForegroundColor Red
    Write-Host 'Prepare the data first (this script does not run it for you):'
    Write-Host "    cd sidecar/finetune; uv run --frozen python -m tau_sidecar.$module"
    exit 1
}
Write-Host "data: ok (data/$dataset/{calibration,heldout,finetune}.jsonl, dataset.manifest.json)"

# --- 2. model packages ---
$vendorIds = @('laya-en', 'laya-multilingual', 'laya-typed-decisions', 'von-1.2.0')
$missingModels = @()
foreach ($id in $models) {
    $dir = Join-Path $repoRoot "models/$id"
    if ((Test-Path (Join-Path $dir 'model.onnx')) -and (Test-Path (Join-Path $dir 'tau-model.json'))) {
        Write-Host "model: ok  $id"
    } else {
        $missingModels += $id
        Write-Host "model: MISSING  models/$id/model.onnx (or tau-model.json)" -ForegroundColor Red
    }
}
if ($missingModels.Count -gt 0) {
    Write-Host 'Package the missing models first (this script does not train or export for you):'
    foreach ($id in $missingModels) {
        if ($id -in $vendorIds) {
            Write-Host "  $id`:"
            Write-Host '    ./scripts/fetch-models.ps1'
            Write-Host "    ./scripts/export.ps1 -Only $id"
        } elseif ($id -like 'laya-en-ft-*') {
            Write-Host "  $id (a local fine-tune; GPU, tens of minutes):"
            Write-Host "    cd sidecar/finetune"
            Write-Host "    uv run --frozen python -m tau_sidecar.finetune_laya --example $Example"
            Write-Host "    uv run --frozen python -m tau_sidecar.export_laya --only $id"
            Write-Host "    uv run --frozen python -m tau_sidecar.parity --only $id --no-fixtures --report ..\..\examples\$Example\finetune-parity.json"
        } else {
            Write-Host "  $id`: no known recipe; package it under models/$id/"
        }
    }
    exit 1
}

if ($CheckOnly) {
    Write-Host "check: everything $Example needs is present."
    exit 0
}

# --- 3. build ---
function Test-PortFree {
    param([int]$P)
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $P)
    try { $listener.Start(); return $true } catch { return $false } finally { $listener.Stop() }
}

Write-Host '=== building Tau.Runtime and the tau CLI (Release) ==='
dotnet build src/Tau.Runtime -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build src/Tau.Runtime -c Release failed with exit code $LASTEXITCODE" }
dotnet build src/Tau.Workbench.Cli -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build src/Tau.Workbench.Cli -c Release failed with exit code $LASTEXITCODE" }
$runtimeDll = Get-ChildItem -Path 'src/Tau.Runtime/bin/Release' -Recurse -Filter 'Tau.Runtime.dll' | Select-Object -First 1
if (-not $runtimeDll) { throw 'Tau.Runtime.dll not found under src/Tau.Runtime/bin/Release after the build' }
$tauDll = Get-ChildItem -Path 'src/Tau.Workbench.Cli/bin/Release' -Recurse -Filter 'tau.dll' | Select-Object -First 1
if (-not $tauDll) { throw 'tau.dll not found under src/Tau.Workbench.Cli/bin/Release after the build' }

$stateDir = Join-Path $repoRoot '.cache/examples-run'
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$url = "http://localhost:$Port"
$calibratorsDir = Join-Path $exampleDir 'calibrators'

function Start-TauRuntime {
    param([string]$Label, [string]$CalibratorsDirectory)
    if (-not (Test-PortFree $Port)) {
        throw "Port $Port is already in use by another process. This script won't stop it: free the port yourself or pass -Port."
    }
    $log = Join-Path $stateDir "runtime-$Example-$Label.log"
    $errLog = Join-Path $stateDir "runtime-$Example-$Label.err.log"
    $runtimeArgs = @($runtimeDll.FullName, '--Tau:Provider=cuda', '--Tau:AllowCpuFallback=false', '--Tau:Preload=true', "--urls=$url")
    for ($i = 0; $i -lt $models.Count; $i++) { $runtimeArgs += "--Tau:Models:$i=$($models[$i])" }
    if ($CalibratorsDirectory) { $runtimeArgs += "--Tau:CalibratorsDirectory=$CalibratorsDirectory" }

    Write-Host "=== starting Tau.Runtime ($Label, CUDA) on $url ==="
    $proc = Start-Process -FilePath 'dotnet' -ArgumentList $runtimeArgs -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $log -RedirectStandardError $errLog -PassThru -NoNewWindow
    $script:runtime = $proc
    Write-Host "Runtime pid $($proc.Id), log $log"

    $deadline = (Get-Date).AddSeconds($RuntimeStartupTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($proc.HasExited) { throw "Tau.Runtime exited during start-up (exit code $($proc.ExitCode)); see $log and $errLog" }
        try {
            $listing = Invoke-RestMethod -Uri "$url/v1/models" -TimeoutSec 5
        } catch {
            Start-Sleep -Seconds 2
            continue
        }
        $listed = @($listing.models | ForEach-Object { $_.id })
        $absent = @($models | Where-Object { $_ -notin $listed })
        if ($absent.Count -gt 0) { throw "Tau.Runtime is up but doesn't list $($absent -join ', '); see $log" }
        Write-Host "Runtime ready: $($listed -join ', ')"
        return
    }
    throw "Tau.Runtime didn't answer GET /v1/models within $RuntimeStartupTimeoutSeconds s; see $log and $errLog"
}

function Stop-TauRuntime {
    if ($script:runtime -and -not $script:runtime.HasExited) {
        Stop-Process -Id $script:runtime.Id -Force
        $script:runtime.WaitForExit()
        Write-Host "stopped Tau.Runtime (pid $($script:runtime.Id), started by this script)"
    }
    $script:runtime = $null
}

function Invoke-Tau {
    param([string[]]$TauArgs)
    Write-Host "=== tau $($TauArgs -join ' ') ==="
    & dotnet $tauDll.FullName @TauArgs | Out-Host
    return $LASTEXITCODE
}

$script:runtime = $null
$exitCode = 1
try {
    # --- 4. raw run ---
    Start-TauRuntime -Label 'raw'
    $code = Invoke-Tau (@('run', $specRel) + $endpointArgs)
    if ($code -eq 2) {
        Write-Host "tau run is blocked (reason above). If frontier answers are pending, answer the batches under examples/$Example/frontier/pending/ and run this again." -ForegroundColor Yellow
        $exitCode = 2
    } elseif ($code -ne 0) {
        throw "tau run failed with exit code $code"
    } elseif ($SkipCalibrated) {
        $exitCode = 0
    } else {
        # --- 5. calibrated phase ---
        if (-not (Test-Path $calibratorsDir)) { throw "tau run finished but wrote no calibrators under $calibratorsDir" }
        Stop-TauRuntime
        Start-TauRuntime -Label 'calibrated' -CalibratorsDirectory $calibratorsDir
        $code = Invoke-Tau (@('measure', $specRel, '--phase', 'calibrated') + $endpointArgs)
        if ($code -eq 2) {
            Write-Host 'The calibrated phase is blocked (reason above).' -ForegroundColor Yellow
            $exitCode = 2
        } elseif ($code -ne 0) {
            throw "tau measure --phase calibrated failed with exit code $code"
        } else {
            $code = Invoke-Tau @('report', $specRel)
            if ($code -ne 0) { throw "tau report failed with exit code $code" }
            $exitCode = 0
        }
    }
} finally {
    # --- 6. stop only the Runtime this script started ---
    Stop-TauRuntime
}

if ($exitCode -eq 0) { Write-Host "done: $(Join-Path $exampleDir 'report.html')" }
exit $exitCode
