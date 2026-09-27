<#
.SYNOPSIS
  Provider spike (T026): run the Category=Provider tests once per ONNX Runtime flavour (cpu, cuda, directml),
  each in its own process, and print a pass/fail table.

.DESCRIPTION
  A process can load only one native flavour, so each provider needs a separate test run; TAU_ORT_PROVIDER
  tells the tests which one. The tests load tests/Tau.Inference.Tests/Onnx/add.onnx through OrtNativeResolver,
  create the session with no CPU fallback, check the result, and check from ONNX Runtime's own profile that
  the node ran on the requested provider.

  Needs scripts/fetch-natives.ps1 (all flavours) and, for CUDA, scripts/fetch-cuda.ps1.

  The test project is an xUnit v3 executable and is run directly (`dotnet run -- -trait Category=Provider`),
  which is equivalent to `dotnet test --filter Category=Provider` and does not depend on the SDK's test-runner
  mode.

.EXAMPLE
  ./scripts/provider-smoke.ps1
  ./scripts/provider-smoke.ps1 -Provider cuda
#>
[CmdletBinding()]
param(
    [ValidateSet('cpu', 'cuda', 'directml')] [string[]] $Provider = @('cpu', 'cuda', 'directml'),
    [string] $Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/..").Path
$project = Join-Path $root 'tests/Tau.Inference.Tests/Tau.Inference.Tests.csproj'

dotnet build $project -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$previous = $env:TAU_ORT_PROVIDER
$rows = @()
try {
    foreach ($p in $Provider) {
        Write-Host "`n=== provider: $p" -ForegroundColor Cyan
        $env:TAU_ORT_PROVIDER = $p
        $output = & dotnet run --project $project -c $Configuration --no-build -- -trait 'Category=Provider' 2>&1
        $code = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
        $summary = $output | Select-String -Pattern 'Total: (\d+), Errors: (\d+), Failed: (\d+), Skipped: (\d+)' | Select-Object -Last 1
        $total, $errors, $failed, $skipped = if ($summary) { $summary.Matches[0].Groups[1..4].Value } else { '?', '?', '?', '?' }
        $rows += [pscustomobject]@{
            Provider = $p
            Result   = if ($code -eq 0) { 'PASS' } else { 'FAIL' }
            Total    = $total
            Failed   = $failed
            Errors   = $errors
            Skipped  = $skipped
        }
    }
} finally {
    $env:TAU_ORT_PROVIDER = $previous
}

Write-Host "`nProvider smoke results ($(Get-Date -Format 'yyyy-MM-dd HH:mm'), $((Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Name))):"
$rows | Format-Table -AutoSize | Out-String | Write-Host
if ($rows.Result -contains 'FAIL') { exit 1 }
exit 0
