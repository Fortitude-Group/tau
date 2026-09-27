<#
.SYNOPSIS
    Runs the R1 /v1/systemone conformance suite end to end (T045-T048): builds and starts Tau,
    starts the conformance peer via scripts/kev-setup.ps1, waits for both, runs
    tools/Tau.Conformance, and stops both servers in a `finally` block regardless of outcome.

.DESCRIPTION
    `src/Tau.Runtime` is not implemented yet in this worktree, so this script's Tau step is
    written to be correct once it is: it builds it in Release, starts it with
    `--Tau:Provider=cuda`, and fails clearly (naming the log) if `/healthz` doesn't come up
    within `-TauStartupTimeoutSeconds` (120s by default).

    `-SkipPeer` runs Tau alone, useful once Tau exists but before a peer is wanted. There is no
    flag to skip Tau: this script's whole point is proving Tau's own conformance (FR-024); running
    the tool against the peer alone, to exercise the tool before Tau exists, is a separate,
    uncommitted, exploratory run (see the R1 implementation notes), not this script.

.EXAMPLE
    ./scripts/conformance.ps1
#>
[CmdletBinding()]
param(
    [int]$TauPort = 8088,
    [int]$PeerPort = 8009,
    [string]$OutDir = 'reports/r1',
    [string]$RequestsDir = 'tests/conformance/requests',
    [int]$TauStartupTimeoutSeconds = 120,
    [switch]$SkipPeer
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

$stateDir = Join-Path $repoRoot '.cache/conformance-run'
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null

function Wait-Healthz {
    param([string]$Url, [int]$TimeoutSeconds)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $resp = Invoke-WebRequest -Uri $Url -Method Get -TimeoutSec 5 -UseBasicParsing
            if ($resp.StatusCode -eq 200) { return $true }
        } catch {
            Start-Sleep -Seconds 2
        }
    }
    return $false
}

$tauProcess = $null
$peerStarted = $false
$exitCode = 1

try {
    Write-Host '=== building Tau.Runtime (Release, CUDA) ==='
    dotnet build src/Tau.Runtime -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build src/Tau.Runtime -c Release failed with exit code $LASTEXITCODE"
    }

    Write-Host '=== starting Tau.Runtime ==='
    $tauLog = Join-Path $stateDir 'tau-runtime.log'
    $tauErrLog = Join-Path $stateDir 'tau-runtime.err.log'
    $tauProcess = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', 'src/Tau.Runtime', '-c', 'Release', '--no-build', '--', '--Tau:Provider=cuda', "--urls=http://localhost:$TauPort") `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $tauLog `
        -RedirectStandardError $tauErrLog `
        -PassThru -NoNewWindow

    $tauHealthy = Wait-Healthz -Url "http://localhost:$TauPort/healthz" -TimeoutSeconds $TauStartupTimeoutSeconds
    if (-not $tauHealthy) {
        throw "Tau did not answer GET /healthz within $TauStartupTimeoutSeconds s — see $tauLog and $tauErrLog"
    }
    Write-Host "Tau is healthy on http://localhost:$TauPort"

    $peerUrl = $null
    $peerName = $null
    $peerRevision = $null

    if (-not $SkipPeer) {
        Write-Host '=== starting the conformance peer (scripts/kev-setup.ps1) ==='
        & (Join-Path $PSScriptRoot 'kev-setup.ps1') -Action Start -Port $PeerPort
        if ($LASTEXITCODE -ne 0) {
            throw 'scripts/kev-setup.ps1 could not start any peer (native Windows, WSL and von serve all failed — see its own log output above)'
        }

        $peerStarted = $true
        $peerStateFile = Join-Path $repoRoot '.cache/kev-setup/peer.json'
        $peerState = Get-Content $peerStateFile -Raw | ConvertFrom-Json
        $peerUrl = $peerState.url
        $peerName = $peerState.name
        $peerRevision = $peerState.revision
        Write-Host "peer ready: $peerName ($peerRevision) at $peerUrl [$($peerState.kind)]"
    }

    Write-Host '=== running tools/Tau.Conformance ==='
    $conformanceArgs = @(
        'run', '--project', 'tools/Tau.Conformance', '-c', 'Release', '--no-build', '--',
        '--tau', "http://localhost:$TauPort",
        '--out', $OutDir,
        '--requests', $RequestsDir
    )
    if (-not $SkipPeer) {
        $conformanceArgs += @('--peer', $peerUrl, '--peer-name', $peerName, '--peer-revision', $peerRevision)
    }

    dotnet build tools/Tau.Conformance -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build tools/Tau.Conformance -c Release failed with exit code $LASTEXITCODE"
    }

    dotnet @conformanceArgs
    $exitCode = $LASTEXITCODE
} finally {
    Write-Host '=== tearing down ==='
    if ($tauProcess) {
        Stop-Process -Id $tauProcess.Id -Force -ErrorAction SilentlyContinue
        Write-Host "stopped Tau (pid $($tauProcess.Id))"
    }
    if ($peerStarted) {
        & (Join-Path $PSScriptRoot 'kev-setup.ps1') -Action Stop -Port $PeerPort
    }
}

exit $exitCode
