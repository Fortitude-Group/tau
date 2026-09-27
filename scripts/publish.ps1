<#
.SYNOPSIS
  Publish the Tau Runtime as a self-contained single-file executable plus a sibling native/ folder
  (the ONNX Runtime flavours, which are chosen at start-up so they can't live inside the single file).
  Local only: nothing is pushed anywhere.
.EXAMPLE
  ./scripts/publish.ps1                       # win-x64, cpu + cuda + directml natives
  ./scripts/publish.ps1 -Rid linux-x64
  ./scripts/publish.ps1 -IncludeCudaDeps      # also copy the ~2 GB CUDA 12 / cuDNN 9 libraries
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64')] [string] $Rid = 'win-x64',
    [string] $Out,
    [switch] $IncludeCudaDeps
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/..").Path
if (-not $Out) { $Out = Join-Path $root "artifacts/publish/$Rid" }

dotnet publish (Join-Path $root 'src/Tau.Runtime/Tau.Runtime.csproj') -c Release -r $Rid --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $Out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$flavours = if ($Rid -eq 'win-x64') { 'cpu', 'cuda', 'directml' } else { 'cpu', 'cuda' }
foreach ($f in $flavours) {
    $src = Join-Path $root "native/$f/$Rid"
    if (-not (Test-Path $src)) { throw "$src missing: run scripts/fetch-natives.ps1" }
    $dst = Join-Path $Out "native/$f/$Rid"
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Copy-Item "$src/*" $dst -Recurse -Force
}
if ($IncludeCudaDeps) {
    Copy-Item (Join-Path $root 'native/cuda-deps') (Join-Path $Out 'native/cuda-deps') -Recurse -Force
}

$exe = Get-ChildItem $Out -Filter 'Tau.Runtime*' | Where-Object { $_.Extension -in '.exe', '' } | Select-Object -First 1
Write-Host ("publish: {0} ({1:N0} MB) + native/ ({2}). Point Tau:ModelsDirectory at your models folder." -f `
    $exe.FullName, ($exe.Length / 1MB), ($flavours -join ', '))
