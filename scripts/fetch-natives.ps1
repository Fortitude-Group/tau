<#
.SYNOPSIS
  Download the pinned ONNX Runtime 1.24.4 native libraries for every execution-provider flavour and lay
  them out as native/<flavour>/<rid>/, where the Runtime's native resolver loads them from.

.DESCRIPTION
  One managed ORT assembly (Microsoft.ML.OnnxRuntime.Managed 1.24.4) drives whichever native flavour the
  config picks at startup (see docs/DECISIONS.md, "ONNX Runtime pinned at 1.24.4"). The natives come from
  the official nupkgs on nuget.org; each nupkg is checked against the sha512 nuget.org publishes for it
  and against the sha256 pinned below, and only runtimes/<rid>/native/* is extracted (plus DirectML.dll
  from Microsoft.AI.DirectML, which the DirectML flavour needs next to onnxruntime.dll).

    flavour   rid          package(s)
    cpu       win-x64      Microsoft.ML.OnnxRuntime 1.24.4
    cpu       linux-x64    Microsoft.ML.OnnxRuntime 1.24.4
    cuda      win-x64      Microsoft.ML.OnnxRuntime.Gpu.Windows 1.24.4   (CUDA 12 / cuDNN 9: scripts/fetch-cuda.ps1)
    cuda      linux-x64    Microsoft.ML.OnnxRuntime.Gpu.Linux 1.24.4
    directml  win-x64      Microsoft.ML.OnnxRuntime.DirectML 1.24.4 + Microsoft.AI.DirectML 1.15.4

  Idempotent: a flavour/rid whose files are already present is skipped unless -Force.

.EXAMPLE
  ./scripts/fetch-natives.ps1
  ./scripts/fetch-natives.ps1 -Flavour cuda -Rid win-x64
#>
[CmdletBinding()]
param(
    [ValidateSet('cpu', 'cuda', 'directml')] [string[]] $Flavour = @('cpu', 'cuda', 'directml'),
    [ValidateSet('win-x64', 'linux-x64')] [string[]] $Rid = @('win-x64', 'linux-x64'),
    [string] $Root = (Resolve-Path "$PSScriptRoot/..").Path,
    [switch] $Force
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$OrtVersion = '1.24.4'

# sha256 of each nupkg as downloaded on 2026-09-27; a mismatch means the file is not the one we tested.
$Pinned = @{
    'microsoft.ml.onnxruntime.1.24.4.nupkg'             = '4b978d5065b85e7004b6c6f60ca494bd978fbe6836cbf0a0b52d82b61ab99638'
    'microsoft.ml.onnxruntime.gpu.windows.1.24.4.nupkg' = 'e897a13d318483e71e1eef91005634846201ab50bc6a582ae913dc5a6ccc0240'
    'microsoft.ml.onnxruntime.gpu.linux.1.24.4.nupkg'   = '06540847b4f83cc5fd92562263122659452cf62783e9a1477b6000b2d947b542'
    'microsoft.ml.onnxruntime.directml.1.24.4.nupkg'    = '57e9f11b73437bef7a309496135d4c1f96b1a8e9ddba60013fa27bfc1d788681'
    'microsoft.ai.directml.1.15.4.nupkg'                = '4e7cb7ddce8cf837a7a75dc029209b520ca0101470fcdf275c1f49736a3615b9'
}

# flavour -> rid -> list of (package id, version, entry prefix inside the nupkg)
$Layout = @{
    'cpu'      = @{
        'win-x64'   = @(, @('Microsoft.ML.OnnxRuntime', $OrtVersion, 'runtimes/win-x64/native/'))
        'linux-x64' = @(, @('Microsoft.ML.OnnxRuntime', $OrtVersion, 'runtimes/linux-x64/native/'))
    }
    'cuda'     = @{
        'win-x64'   = @(, @('Microsoft.ML.OnnxRuntime.Gpu.Windows', $OrtVersion, 'runtimes/win-x64/native/'))
        'linux-x64' = @(, @('Microsoft.ML.OnnxRuntime.Gpu.Linux', $OrtVersion, 'runtimes/linux-x64/native/'))
    }
    'directml' = @{
        'win-x64' = @(
            @('Microsoft.ML.OnnxRuntime.DirectML', $OrtVersion, 'runtimes/win-x64/native/'),
            @('Microsoft.AI.DirectML', '1.15.4', 'bin/x64-win/')
        )
    }
}

$cache = Join-Path $Root 'native/.nupkg'
New-Item -ItemType Directory -Force -Path $cache | Out-Null

function Get-Nupkg([string] $id, [string] $version) {
    $lower = $id.ToLowerInvariant()
    $file = "$lower.$version.nupkg"
    $path = Join-Path $cache $file
    if (-not (Test-Path $path)) {
        $url = "https://api.nuget.org/v3-flatcontainer/$lower/$version/$file"
        Write-Host "fetch  $file"
        if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
            & curl.exe -sSL --fail --retry 5 --retry-delay 5 -o "$path.part" $url
            if ($LASTEXITCODE -ne 0) { throw "download failed: $url" }
        } else {
            # Linux build containers have no curl.exe; PowerShell's own client works everywhere.
            Invoke-WebRequest -Uri $url -OutFile "$path.part" -MaximumRetryCount 5 -RetryIntervalSec 5
        }
        Move-Item -Force "$path.part" $path
    }

    $sha = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $pin = $Pinned[$file]
    if (-not $pin) { throw "$file has no pinned sha256 (got $sha); add it to `$Pinned after checking the package" }
    if ($pin -ne $sha) { Remove-Item $path; throw "$file sha256 $sha does not match the pinned $pin" }
    return $path
}

$results = @()
foreach ($f in $Flavour) {
    foreach ($r in $Rid) {
        if (-not $Layout[$f].ContainsKey($r)) { continue }
        $dest = Join-Path $Root "native/$f/$r"
        $lib = if ($r -like 'win-*') { 'onnxruntime.dll' } else { 'libonnxruntime.so' }
        if ((Test-Path (Join-Path $dest $lib)) -and -not $Force) {
            Write-Host "ok     native/$f/$r (present)"
            $results += [pscustomobject]@{ Flavour = $f; Rid = $r; Files = (Get-ChildItem $dest -File).Name -join ', ' }
            continue
        }
        if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
        New-Item -ItemType Directory -Force -Path $dest | Out-Null

        foreach ($pkg in $Layout[$f][$r]) {
            $nupkg = Get-Nupkg $pkg[0] $pkg[1]
            $prefix = $pkg[2]
            $zip = [IO.Compression.ZipFile]::OpenRead($nupkg)
            try {
                $entries = $zip.Entries | Where-Object { $_.FullName.StartsWith($prefix) -and $_.Name -and $_.FullName.Substring($prefix.Length) -notmatch '/' }
                if (-not $entries) { throw "$($pkg[0]) $($pkg[1]) has no files under $prefix" }
                foreach ($e in $entries) {
                    # Import libraries, debug symbols and DirectML's debug-layer build are not needed at run time.
                    if ($e.Name -match '\.(lib|pdb)$' -or $e.Name -eq 'DirectML.Debug.dll') { continue }
                    [IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $dest $e.Name), $true)
                }
            } finally { $zip.Dispose() }
        }

        if (-not (Test-Path (Join-Path $dest $lib))) { throw "native/$f/$r has no $lib after extraction" }
        Write-Host "ok     native/$f/$r"
        $results += [pscustomobject]@{ Flavour = $f; Rid = $r; Files = (Get-ChildItem $dest -File).Name -join ', ' }
    }
}

$results | Format-Table -AutoSize -Wrap
Write-Host "fetch-natives: done. The Runtime loads native/<flavour>/<rid>/$('{onnxruntime.dll|libonnxruntime.so}')."
exit 0
