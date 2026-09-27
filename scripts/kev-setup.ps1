<#
.SYNOPSIS
    Starts or stops the /v1/systemone conformance peer for T047: Kev-0.8B natively on Windows if
    that works, WSL Ubuntu if it doesn't, or von-sdk's own `von serve` as a last resort.

.DESCRIPTION
    `-Action Start` tries, in order:
      1. Kev-0.8B (jaredpalmer/kev, Apache-2.0) cloned into .cache/kev at a pinned commit, run
         natively via uv. Kev's own pyproject does not pin a CUDA wheel index, so a plain
         `uv sync` on Windows installs the CPU-only PyPI build of torch; if CUDA isn't visible
         after sync, torch is reinstalled from the CUDA 12.8 wheel index before giving up on the
         GPU. Only a genuine failure after that (the process won't start, or never opens its
         port) falls through.
      2. The same Kev checkout, run inside WSL Ubuntu (installed separately; this script assumes
         it is already registered), reached back on Windows via WSL2's localhost forwarding.
      3. von-sdk's own `/v1/systemone` server (`von serve`), run from the Tau fine-tune sidecar's
         own uv environment (sidecar/finetune), which already depends on von-sdk.

    Whichever one starts is recorded in .cache/kev-setup/peer.json (kind, revision, url) so
    `-Action Stop` (or scripts/conformance.ps1's `finally` block) can tear down the right one.
    This never runs against production and needs no API key (constitution Principle XVI).

.PARAMETER Action
    'Start' (default) or 'Stop'.

.PARAMETER Port
    Port the peer listens on. Default 8009 (R-13).

.EXAMPLE
    ./scripts/kev-setup.ps1
    ./scripts/kev-setup.ps1 -Action Stop
#>
[CmdletBinding()]
param(
    [ValidateSet('Start', 'Stop')]
    [string]$Action = 'Start',

    [int]$Port = 8009,

    [string]$KevModelId = 'jaredpalmer/kev-0.8b',

    # Pinned at implement time (2026-09-27): https://github.com/jaredpalmer/kev/commit/5920c5f
    [string]$KevCommit = '5920c5fe4ca8e0970ed4209ac2c9b8e18bea5109',

    [string]$CacheDir = '.cache/kev',

    [string]$StateDir = '.cache/kev-setup',

    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

$stateDirFull = Join-Path $repoRoot $StateDir
New-Item -ItemType Directory -Force -Path $stateDirFull | Out-Null
$pidFile = Join-Path $stateDirFull 'peer.json'
$logDir = Join-Path $stateDirFull 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Wait-PortOpen {
    param([string]$ComputerName, [int]$Port, [int]$TimeoutSeconds)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $client = New-Object System.Net.Sockets.TcpClient
            $client.Connect($ComputerName, $Port)
            $client.Close()
            return $true
        } catch {
            Start-Sleep -Seconds 2
        }
    }
    return $false
}

function Stop-Peer {
    if (-not (Test-Path $pidFile)) {
        Write-Host 'no tracked peer process (nothing to stop)'
        return
    }

    # Only the state object: a function's stray pipeline output must never be mistaken for it.
    $peerState = @(Get-Content $pidFile -Raw | ConvertFrom-Json) | Where-Object { $_.kind } | Select-Object -Last 1
    switch ([string]$peerState.kind) {
        'native' {
            # `uv run` spawns the Python server as a child, so stop the process that owns the port too.
            # Both PIDs were recorded by this script when it started them, and the listener's command line was
            # checked to be kev.serve at that point.
            foreach ($id in @($peerState.listenerPid, $peerState.processId) | Where-Object { $_ }) {
                Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
            }
            Write-Host "stopped native Kev (listener $($peerState.listenerPid), launcher $($peerState.processId))"
        }
        'wsl' {
            wsl -d $peerState.distro -- bash -lc "pkill -f 'kev.serve --run $($peerState.modelId)' || true" | Out-Null
            Write-Host "stopped kev.serve inside WSL ($($peerState.distro))"
        }
        'von' {
            Stop-Process -Id $peerState.processId -Force -ErrorAction SilentlyContinue
            Write-Host "stopped von serve process $($peerState.processId)"
        }
        default {
            Write-Host "unrecognised peer kind '$($peerState.kind)' in $pidFile — nothing stopped"
        }
    }

    Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
}

if ($Action -eq 'Stop') {
    Stop-Peer
    return
}

if (Test-Path $pidFile) {
    Write-Error "a peer is already tracked at $pidFile — stop it first: ./scripts/kev-setup.ps1 -Action Stop"
    exit 1
}

function Try-NativeWindows {
    Write-Host '=== attempting Kev-0.8B natively on Windows ==='
    $cacheDirFull = Join-Path $repoRoot $CacheDir

    if (-not (Test-Path (Join-Path $cacheDirFull '.git'))) {
        git clone https://github.com/jaredpalmer/kev.git $cacheDirFull
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'native Windows FAILED: git clone did not succeed'
            return $null
        }
    }

    Push-Location $cacheDirFull
    try {
        git fetch --quiet origin
        git checkout --quiet $KevCommit

        # Kev's own pyproject has no [tool.uv.sources] CUDA index (unlike Tau's own sidecar), so a
        # plain `uv sync` on Windows resolves the default PyPI torch wheel, which is CPU-only —
        # a local environment gap, not a Kev/Windows incompatibility. Patch it in (idempotently,
        # local to this disposable .cache/kev checkout, never upstream) before syncing: doing this
        # before `uv sync` means the lock itself resolves the CUDA build, so it survives every
        # later `uv run` (which otherwise re-syncs the venv back to whatever the lock says, undoing
        # a plain `pip install --reinstall` done after the fact — confirmed by trying that first).
        $pyprojectPath = Join-Path $cacheDirFull 'pyproject.toml'
        $pyprojectText = Get-Content $pyprojectPath -Raw
        if ($pyprojectText -notmatch '\[tool\.uv\.sources\]') {
            $cudaIndexBlock = @"


# Local-only addition (not upstream Kev): point torch at the CUDA 12.8 wheel index Tau's own
# sidecar uses (sidecar/finetune/pyproject.toml), so the conformance peer runs on the GPU.
[tool.uv.sources]
torch = { index = "pytorch-cu128" }

[[tool.uv.index]]
name = "pytorch-cu128"
url = "https://download.pytorch.org/whl/cu128"
explicit = true
"@
            Add-Content -Path $pyprojectPath -Value $cudaIndexBlock
        }

        $syncLog = Join-Path $logDir 'native-uv-sync.log'
        uv sync --extra serve *>&1 | Tee-Object -FilePath $syncLog
        if ($LASTEXITCODE -ne 0) {
            Write-Host "native Windows FAILED: 'uv sync --extra serve' exited $LASTEXITCODE — see $syncLog"
            return $null
        }

        $cudaCheck = uv run --no-sync --extra serve python -c 'import torch; print(torch.cuda.is_available())' 2>&1
        if ($cudaCheck -notmatch 'True') {
            Write-Host "native Windows FAILED: no CUDA device is visible to torch after syncing from the CUDA wheel index ($cudaCheck)"
            return $null
        }

        $serveLog = Join-Path $logDir 'native-serve.log'
        $serveErrLog = Join-Path $logDir 'native-serve.err.log'
        $proc = Start-Process -FilePath 'uv' `
            -ArgumentList @('run', '--no-sync', '--extra', 'serve', 'python', '-m', 'kev.serve', '--run', $KevModelId, '--port', $Port, '--host', '127.0.0.1') `
            -WorkingDirectory $cacheDirFull `
            -RedirectStandardOutput $serveLog `
            -RedirectStandardError $serveErrLog `
            -PassThru -NoNewWindow

        if (Wait-PortOpen -ComputerName '127.0.0.1' -Port $Port -TimeoutSeconds $TimeoutSeconds) {
            # Record the real server process (uv's child) only if it is the kev.serve we just launched.
            $listenerPid = $null
            foreach ($c in @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) {
                $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($c.OwningProcess)").CommandLine
                if ($cmd -match 'kev\.serve') { $listenerPid = $c.OwningProcess }
            }
            Write-Host "Kev-0.8B is up natively on Windows (launcher $($proc.Id), server $listenerPid) on port $Port"
            return [ordered]@{
                kind = 'native'; processId = $proc.Id; listenerPid = $listenerPid; modelId = $KevModelId; commit = $KevCommit
                url = "http://127.0.0.1:$Port"; platform = 'native-windows'
                name = 'kev-0.8b'; revision = $KevCommit
            }
        }

        Write-Host "native Windows FAILED: server never opened port $Port within $TimeoutSeconds s — see $serveLog / $serveErrLog"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        return $null
    } finally {
        Pop-Location
    }
}

function Try-Wsl {
    param([string]$Distro = 'Ubuntu')

    Write-Host "=== attempting Kev-0.8B inside WSL ($Distro) ==="

    $wslList = (wsl -l -q 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0 -or ($wslList -notmatch [regex]::Escape($Distro))) {
        Write-Host "WSL FAILED: distro '$Distro' is not installed/registered (wsl -l -q: $wslList)"
        return $null
    }

    # Same repo checkout, addressed from inside WSL: C:\a\b -> /mnt/c/a/b.
    $winPath = $repoRoot -replace '\\', '/'
    $drive = $winPath.Substring(0, 1).ToLowerInvariant()
    $wslCacheDir = "/mnt/$drive$($winPath.Substring(2))/$CacheDir"

    $setupScript = @"
set -e
if ! command -v uv >/dev/null 2>&1; then
  curl -LsSf https://astral.sh/uv/install.sh | sh
fi
export PATH="`$HOME/.local/bin:`$PATH"
if [ ! -d '$wslCacheDir/.git' ]; then
  git clone https://github.com/jaredpalmer/kev.git '$wslCacheDir'
fi
cd '$wslCacheDir'
git fetch --quiet origin
git checkout --quiet $KevCommit
uv sync --extra serve
"@
    $setupScriptPath = Join-Path $logDir 'wsl-setup.sh'
    Set-Content -Path $setupScriptPath -Value $setupScript -NoNewline

    $setupLog = Join-Path $logDir 'wsl-setup.log'
    Get-Content $setupScriptPath -Raw | wsl -d $Distro -- bash -lc 'cat > /tmp/kev-setup.sh && bash /tmp/kev-setup.sh' 2>&1 |
        Tee-Object -FilePath $setupLog
    if ($LASTEXITCODE -ne 0) {
        Write-Host "WSL FAILED: setup did not complete — see $setupLog"
        return $null
    }

    $serveCmd = "cd '$wslCacheDir' && nohup uv run --extra serve python -m kev.serve --run $KevModelId --port $Port --host 0.0.0.0 > /tmp/kev-serve.log 2>&1 < /dev/null & echo started"
    wsl -d $Distro -- bash -lc $serveCmd 2>&1 | Tee-Object -FilePath (Join-Path $logDir 'wsl-serve-launch.log') | Out-Null

    # WSL2 forwards 127.0.0.1 from Windows into the VM by default (localhostForwarding), so the
    # server is reached exactly like the native path once it is listening.
    if (Wait-PortOpen -ComputerName '127.0.0.1' -Port $Port -TimeoutSeconds $TimeoutSeconds) {
        Write-Host "Kev-0.8B is up inside WSL ($Distro), reached via localhost forwarding on port $Port"
        return [ordered]@{
            kind = 'wsl'; distro = $Distro; modelId = $KevModelId; commit = $KevCommit
            url = "http://127.0.0.1:$Port"; platform = "wsl-$Distro"
            name = 'kev-0.8b'; revision = $KevCommit
        }
    }

    Write-Host "WSL FAILED: server never opened port $Port within $TimeoutSeconds s — see: wsl -d $Distro -- cat /tmp/kev-serve.log"
    wsl -d $Distro -- bash -lc "pkill -f 'kev.serve --run $KevModelId' || true" | Out-Null
    return $null
}

function Try-Von {
    Write-Host "=== falling back to von-sdk's own /v1/systemone server (von serve) ==="
    $sidecarDir = Join-Path $repoRoot 'sidecar/finetune'

    Push-Location $sidecarDir
    try {
        $helpLog = Join-Path $logDir 'von-help.log'
        uv run --frozen von --help *>&1 | Tee-Object -FilePath $helpLog
        if ($LASTEXITCODE -ne 0) {
            Write-Host "von FAILED: 'uv run --frozen von --help' did not run — see $helpLog"
            return $null
        }

        $vonVersion = (uv run --frozen python -c "from importlib.metadata import version; print(version('von-sdk'))" 2>&1 | Select-Object -Last 1)

        $serveLog = Join-Path $logDir 'von-serve.log'
        $serveErrLog = Join-Path $logDir 'von-serve.err.log'
        $proc = Start-Process -FilePath 'uv' `
            -ArgumentList @('run', '--frozen', 'von', 'serve', '--host', '127.0.0.1', '--port', $Port, '--device', 'cuda') `
            -WorkingDirectory $sidecarDir `
            -RedirectStandardOutput $serveLog `
            -RedirectStandardError $serveErrLog `
            -PassThru -NoNewWindow

        if (Wait-PortOpen -ComputerName '127.0.0.1' -Port $Port -TimeoutSeconds $TimeoutSeconds) {
            Write-Host "von serve is up (pid $($proc.Id)) on port $Port"
            return [ordered]@{
                kind = 'von'; processId = $proc.Id; url = "http://127.0.0.1:$Port"; platform = 'native-windows-von'
                name = 'von-sdk'; revision = $vonVersion
            }
        }

        Write-Host "von FAILED: server never opened port $Port within $TimeoutSeconds s — see $serveLog / $serveErrLog"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        return $null
    } finally {
        Pop-Location
    }
}

$peer = Try-NativeWindows
if (-not $peer) { $peer = Try-Wsl }
if (-not $peer) { $peer = Try-Von }

if (-not $peer) {
    Write-Error "every peer option failed (native Windows, WSL, von serve) — see logs under $logDir"
    exit 1
}

# Keep only the state object; anything else a function wrote to the pipeline is noise.
$peer = @($peer) | Where-Object { $_ -is [System.Collections.IDictionary] } | Select-Object -Last 1
$peer | ConvertTo-Json | Set-Content -Path $pidFile
Write-Host "peer ready: $($peer.kind) at $($peer.url)"
