<#
.SYNOPSIS
  Install the CUDA 12 runtime libraries ONNX Runtime 1.24.4's CUDA provider needs into native/cuda-deps/,
  from NVIDIA's free pip wheels, so no CUDA toolkit install is required.

.DESCRIPTION
  ORT 1.21-1.26 CUDA builds are built against CUDA 12.8 and cuDNN 9.x (onnxruntime.ai CUDA EP requirements
  table, checked 2026-09-27). The import table of native/cuda/win-x64/onnxruntime_providers_cuda.dll names
  cudart64_12, cublas64_12, cublasLt64_12, cufft64_11 and cudnn64_9; cuRAND is installed as well because
  ORT's documentation lists it for the CUDA provider.

  The versions below are the CUDA 12.8 set (the same pins PyTorch's cu128 wheels use) and cuDNN 9.10, all
  exact, so a re-run installs exactly what was tested. The wheels are installed with
  `uv pip install --target`, which touches no Python environment; only their DLLs are used.

  After installing, the script prints the directories that hold the DLLs. The Runtime (and the provider
  tests) pass native/cuda-deps to OrtNativeResolver, which finds these directories itself.

.EXAMPLE
  ./scripts/fetch-cuda.ps1
#>
[CmdletBinding()]
param(
    [string] $Root = (Resolve-Path "$PSScriptRoot/..").Path,
    [switch] $Force
)
$ErrorActionPreference = 'Stop'

$Wheels = @(
    'nvidia-cuda-runtime-cu12==12.8.90',
    'nvidia-cublas-cu12==12.8.4.1',
    'nvidia-cufft-cu12==11.3.3.83',
    'nvidia-curand-cu12==10.3.9.90',
    'nvidia-cudnn-cu12==9.10.2.21'
)

$target = Join-Path $Root 'native/cuda-deps'
if ((Test-Path $target) -and $Force) { Remove-Item -Recurse -Force $target }
New-Item -ItemType Directory -Force -Path $target | Out-Null

& uv pip install --python 3.12 --target $target --no-deps @Wheels
if ($LASTEXITCODE -ne 0) { throw "uv pip install failed" }

$libPattern = if ($IsWindows) { '*.dll' } else { '*.so*' }
$dirs = Get-ChildItem -Recurse -File -Path (Join-Path $target 'nvidia') -Filter $libPattern |
    Select-Object -ExpandProperty DirectoryName -Unique | Sort-Object

$required = if ($IsWindows) {
    @('cudart64_12.dll', 'cublas64_12.dll', 'cublasLt64_12.dll', 'cufft64_11.dll', 'curand64_10.dll', 'cudnn64_9.dll')
} else {
    @('libcudart.so.12', 'libcublas.so.12', 'libcublasLt.so.12', 'libcufft.so.11', 'libcurand.so.10', 'libcudnn.so.9')
}
$missing = $required | Where-Object { -not (Get-ChildItem -Recurse -File -Path $target -Filter $_ -ErrorAction SilentlyContinue) }
if ($missing) { throw "fetch-cuda: installed wheels are missing $($missing -join ', ')" }

Write-Host "fetch-cuda: CUDA 12.8 / cuDNN 9 libraries installed. Library directories:"
$dirs | ForEach-Object { Write-Host "  $_" }
exit 0
