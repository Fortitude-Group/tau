<#
.SYNOPSIS
  Download the exported ONNX model packages from the models-v1 GitHub release and unpack them to
  models/<id>/, so the Runtime can start without the Python export environment.

.DESCRIPTION
  These are the exact files every committed report was measured with. The calibrators in
  examples/*/calibrators/ are bound to their model.onnx hashes, and a fresh export with
  scripts/export.ps1 isn't byte-identical, so use this script if you want the reports to reproduce.

  Each zip is checked against the sha256 pinned below before it's unpacked. After unpacking, model.onnx
  and model.onnx.data are checked against the hashes in the package's own tau-model.json.

  Downloads use `gh release download` when the GitHub CLI is installed and signed in (this also works
  while the repository is private), and a plain HTTPS download otherwise.

  Idempotent: a package already present with matching hashes is skipped unless -Force.

.EXAMPLE
  ./scripts/fetch-onnx.ps1 -Only laya-en
  ./scripts/fetch-onnx.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('laya-en', 'laya-multilingual', 'laya-typed-decisions', 'von-1.2.0', 'laya-en-ft-banking77')]
    [string[]] $Only = @('laya-en', 'laya-multilingual', 'laya-typed-decisions', 'von-1.2.0', 'laya-en-ft-banking77'),
    [string] $Root = (Resolve-Path "$PSScriptRoot/..").Path,
    [string] $Repo = 'Fortitude-Group/tau',
    [string] $Tag = 'models-v1',
    [switch] $Force
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# sha256 of each release zip as uploaded on 2026-09-27; a mismatch means the file isn't the one we published.
$Pinned = @{
    'laya-en'              = '0d8716dd88da8674fbe7daafd8a66151988e535a38c9654b931568689fd7f37b'
    'laya-multilingual'    = '15714babab093bdee3a93488ab6436540f06e0340e883892a64aca9a4518b931'
    'laya-typed-decisions' = 'ff7a17b9b30d29cc88359791b08257c092f50fdacff786759808655cbf835391'
    'von-1.2.0'            = 'b8e7ff905167f9da6bd5149aaac96117dc97b859c235b64af3b59f40f612c050'
    'laya-en-ft-banking77' = 'da50cbcbb4dfcac0b5a6ae252b13d35ed30ad1c8925576c1e7eb13e1f81992fb'
}

function Test-Package([string] $dir) {
    $manifest = Join-Path $dir 'tau-model.json'
    if (-not (Test-Path $manifest)) { return $false }
    $onnx = (Get-Content $manifest -Raw | ConvertFrom-Json).onnx
    foreach ($pair in @(@('model.onnx', $onnx.sha256), @('model.onnx.data', $onnx.data_sha256))) {
        $file = Join-Path $dir $pair[0]
        if (-not $pair[1]) { continue }
        if (-not (Test-Path $file)) { return $false }
        if ((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pair[1]) { return $false }
    }
    return $true
}

$useGh = [bool](Get-Command gh -ErrorAction SilentlyContinue)
if ($useGh) {
    gh auth status *> $null
    $useGh = $LASTEXITCODE -eq 0
}

$modelsDir = Join-Path $Root 'models'
$tmp = Join-Path $Root '.cache/fetch-onnx'
New-Item -ItemType Directory -Force $modelsDir, $tmp | Out-Null

foreach ($id in $Only) {
    $target = Join-Path $modelsDir $id
    if (-not $Force -and (Test-Package $target)) {
        Write-Host "$id`: already present and verified, skipping"
        continue
    }

    $zip = Join-Path $tmp "$id.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Write-Host "$id`: downloading $id.zip from $Repo release $Tag"
    if ($useGh) {
        gh release download $Tag -R $Repo -p "$id.zip" -D $tmp --clobber
        if ($LASTEXITCODE -ne 0) { throw "gh release download failed for $id.zip" }
    } else {
        Invoke-WebRequest -Uri "https://github.com/$Repo/releases/download/$Tag/$id.zip" -OutFile $zip
    }

    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Pinned[$id] -ne $hash) {
        Remove-Item $zip
        throw "$id.zip has sha256 $hash, expected $($Pinned[$id]). Not unpacking it."
    }

    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $modelsDir)
    Remove-Item $zip
    if (-not (Test-Package $target)) { throw "$id unpacked, but its files don't match the hashes in tau-model.json" }
    Write-Host "$id`: unpacked to models/$id and verified"
}
