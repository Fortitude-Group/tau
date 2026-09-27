<#
.SYNOPSIS
    Regenerates reports/r1/latency*.{md,json} (T049-T050): builds Tau.Bench in Release, measures every model in-process
    on CUDA and then on CPU (one process per provider), optionally measures HTTP end to end against a Runtime this script
    starts and stops itself, and writes the combined report.

.DESCRIPTION
    Run it on a quiet machine: close anything that uses the GPU or keeps the CPU busy. Each report records the CPU and GPU
    load sampled just before and after its timed passes, so a busy machine shows up in the report rather than hiding in it.

    ONNX Runtime can load only one native flavour per process, so each provider runs in its own Tau.Bench process.
    The in-process CUDA run and the Runtime can't hold the models on the GPU at the same time (about 6 GB of FP32 weights
    each on a 12 GB card), so the HTTP pass runs after the in-process passes, with the Runtime started only for it.

    With -Http the script starts the Runtime (the built Tau.Runtime.dll, not `dotnet run`, so the process it stops is the
    server itself) on a free localhost port with --Tau:Provider=cuda --Tau:AllowCpuFallback=false, waits for /healthz,
    runs the HTTP pass, and stops that one process in a finally block. It never stops anything it didn't start.

.EXAMPLE
    ./scripts/bench.ps1 -Http

.EXAMPLE
    ./scripts/bench.ps1 -Providers cuda -GpuIterations 100 -Repeat 1 -OutDir scratch/bench
#>
[CmdletBinding()]
param(
    [string]$OutDir = 'reports/r1',
    [ValidateSet('cuda', 'cpu', 'directml')]
    [string[]]$Providers = @('cuda', 'cpu'),
    [int]$Warmup = 50,
    # CPU FP32 takes about 0.7-7.5 s per request here, so 50/100 would run for about 4 hours; 5/30 keeps it near 1 hour.
    [int]$CpuWarmup = 5,
    [int]$GpuIterations = 500,
    [int]$CpuIterations = 30,
    [int]$Repeat = 2,
    [string]$Questions = '1,4,10',
    [string]$Models,
    [switch]$Http,
    [int]$Port = 18088,
    [int]$RuntimeStartupTimeoutSeconds = 300,
    [string]$ModelsDirectory,
    [string]$NativeDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

# The command a reader runs to reproduce these reports, recorded in every report header.
$invokedBy = './scripts/bench.ps1' + (($PSBoundParameters.GetEnumerator() | Sort-Object Key | ForEach-Object {
    if ($_.Value -is [switch]) { if ($_.Value.IsPresent) { " -$($_.Key)" } }
    elseif ($_.Value -is [array]) { " -$($_.Key) $($_.Value -join ',')" }
    else { " -$($_.Key) $($_.Value)" }
}) -join '')

$pathArgs = @()
if ($ModelsDirectory) { $pathArgs += @('--models-dir', $ModelsDirectory) }
if ($NativeDirectory) { $pathArgs += @('--native-dir', $NativeDirectory) }
$commonArgs = @('--out', $OutDir, '--repeat', $Repeat, '--questions', $Questions, '--invoked-by', $invokedBy) + $pathArgs
if ($Models) { $commonArgs += @('--models', $Models) }

function Invoke-Bench {
    param([string[]]$BenchArgs)
    & dotnet run --project tools/Tau.Bench -c Release --no-build -- @BenchArgs
    if ($LASTEXITCODE -ne 0) { throw "Tau.Bench $($BenchArgs -join ' ') failed with exit code $LASTEXITCODE" }
}

function Test-PortFree {
    param([int]$P)
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $P)
    try { $listener.Start(); return $true } catch { return $false } finally { $listener.Stop() }
}

Write-Host '=== building Tau.Bench (Release) ==='
dotnet build tools/Tau.Bench -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build tools/Tau.Bench -c Release failed with exit code $LASTEXITCODE" }

foreach ($provider in $Providers) {
    $iterations = if ($provider -eq 'cpu') { $CpuIterations } else { $GpuIterations }
    Write-Host "=== in-process, $provider, $iterations iterations x $Repeat repeats ==="
    $warm = if ($provider -eq 'cpu') { $CpuWarmup } else { $Warmup }
    Invoke-Bench (@('--provider', $provider, '--iterations', $iterations, '--warmup', $warm) + $commonArgs)
}

if ($Http) {
    Write-Host '=== building Tau.Runtime (Release) ==='
    dotnet build src/Tau.Runtime -c Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet build src/Tau.Runtime -c Release failed with exit code $LASTEXITCODE" }
    $runtimeDll = Get-ChildItem -Path 'src/Tau.Runtime/bin/Release' -Recurse -Filter 'Tau.Runtime.dll' | Select-Object -First 1
    if (-not $runtimeDll) { throw 'Tau.Runtime.dll not found under src/Tau.Runtime/bin/Release after the build' }

    while (-not (Test-PortFree $Port)) { $Port++ }
    $url = "http://127.0.0.1:$Port"
    $stateDir = Join-Path $repoRoot '.cache/bench-run'
    New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
    $log = Join-Path $stateDir "runtime-$Port.log"
    $errLog = Join-Path $stateDir "runtime-$Port.err.log"

    $runtimeArgs = @($runtimeDll.FullName, '--Tau:Provider=cuda', '--Tau:AllowCpuFallback=false', '--Tau:Preload=true', "--urls=$url")
    if ($ModelsDirectory) { $runtimeArgs += "--Tau:ModelsDirectory=$((Resolve-Path $ModelsDirectory).Path)" }
    if ($NativeDirectory) {
        $native = (Resolve-Path $NativeDirectory).Path
        $runtimeArgs += @("--Tau:NativeDirectory=$native", "--Tau:CudaDepsDirectory=$(Join-Path $native 'cuda-deps')")
    }
    if ($Models) {
        $i = 0
        foreach ($m in $Models.Split(',')) { $runtimeArgs += "--Tau:Models:$i=$($m.Trim())"; $i++ }
    }

    $runtime = $null
    try {
        Write-Host "=== starting Tau.Runtime (CUDA) on $url ==="
        $runtime = Start-Process -FilePath 'dotnet' -ArgumentList $runtimeArgs -WorkingDirectory $repoRoot `
            -RedirectStandardOutput $log -RedirectStandardError $errLog -PassThru -NoNewWindow
        Write-Host "Runtime pid $($runtime.Id), log $log"

        $deadline = (Get-Date).AddSeconds($RuntimeStartupTimeoutSeconds)
        $healthy = $false
        while ((Get-Date) -lt $deadline) {
            if ($runtime.HasExited) { throw "Tau.Runtime exited during start-up (exit code $($runtime.ExitCode)); see $log and $errLog" }
            try {
                if ((Invoke-WebRequest -Uri "$url/healthz" -TimeoutSec 5 -UseBasicParsing).StatusCode -eq 200) { $healthy = $true; break }
            } catch { Start-Sleep -Seconds 2 }
        }
        if (-not $healthy) { throw "Tau.Runtime didn't answer /healthz within $RuntimeStartupTimeoutSeconds s; see $log and $errLog" }

        Write-Host "=== HTTP, cuda, $GpuIterations iterations x $Repeat repeats ==="
        Invoke-Bench (@('--provider', 'cuda', '--iterations', $GpuIterations, '--http-url', $url, '--http-server-log', $log, '--warmup', $Warmup) + $commonArgs)
    } finally {
        if ($runtime -and -not $runtime.HasExited) {
            Stop-Process -Id $runtime.Id -Force
            Write-Host "stopped Tau.Runtime (pid $($runtime.Id), started by this script)"
        }
    }
}

Write-Host '=== combining ==='
Invoke-Bench @('--combine', $OutDir)
Write-Host "done: $OutDir/latency.md"
