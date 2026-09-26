<#
.SYNOPSIS
  Download the pinned model checkpoints listed in models.lock.json into models/src/<id>/
  and verify every file's sha256 against the lock. Idempotent: files that already verify are skipped.
.EXAMPLE
  ./scripts/fetch-models.ps1
  ./scripts/fetch-models.ps1 -Only von-1.2.0
#>
[CmdletBinding()]
param(
    [string[]] $Only,
    [string] $Root = (Resolve-Path "$PSScriptRoot/..").Path
)
$ErrorActionPreference = 'Stop'

$lock = Get-Content (Join-Path $Root 'models.lock.json') -Raw | ConvertFrom-Json
if ($lock.format -ne 'tau.models.lock' -or $lock.version -ne 1) { throw "models.lock.json: unexpected format/version" }

$failed = @()
foreach ($m in $lock.models) {
    if ($Only -and $m.id -notin $Only) { continue }
    $dest = Join-Path $Root "models/src/$($m.id)"
    foreach ($f in $m.files.PSObject.Properties) {
        $rel = $f.Name
        $info = $f.Value
        $target = Join-Path $dest $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null

        if ((Test-Path $target) -and ((Get-FileHash $target -Algorithm SHA256).Hash.ToLower() -eq $info.sha256)) {
            Write-Host "ok     $($m.id)/$rel"
            continue
        }
        $url = "https://huggingface.co/$($m.repo)/resolve/$($m.revision)/$($info.path)"
        Write-Host "fetch  $($m.id)/$rel ($([math]::Round($info.size / 1MB, 1)) MB)"
        & curl.exe -sSL --fail --retry 5 --retry-delay 5 -C - -o $target $url
        if ($LASTEXITCODE -ne 0) {
            # A resumed download of a file whose server copy changed can't be resumed; start clean once.
            Remove-Item $target -ErrorAction SilentlyContinue
            & curl.exe -sSL --fail --retry 5 --retry-delay 5 -o $target $url
            if ($LASTEXITCODE -ne 0) { $failed += "$($m.id)/$rel (download)"; continue }
        }
        $hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLower()
        if ($hash -ne $info.sha256) {
            $failed += "$($m.id)/$rel (sha256 $hash, expected $($info.sha256))"
            Remove-Item $target
        } else {
            Write-Host "ok     $($m.id)/$rel"
        }
    }
}

if ($failed) {
    Write-Error ("fetch-models: failures:`n  " + ($failed -join "`n  "))
    exit 1
}
Write-Host "fetch-models: all files verified."
