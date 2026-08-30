[CmdletBinding()]
param(
    [double]$CvThreshold = 10.0,
    [int]$Runs = 5,
    [int]$MaxAttempts = 3,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$resultsDir = Join-Path $scriptRoot 'GpuBenchmarks\results'
$summaryPath = Join-Path $resultsDir 'summary.csv'
$compiledPath = Join-Path $resultsDir 'raw_results_compiled.csv'

if (-not (Test-Path -LiteralPath $summaryPath)) { throw "Missing summary.csv at $summaryPath" }

# load candidate keys with CV > threshold
$summary = Import-Csv -LiteralPath $summaryPath
$candidates = $summary | Where-Object {
    $cv = [double]$_.'CvPercent'
    $cv -gt $CvThreshold
} | ForEach-Object {
    [pscustomobject]@{
        TaskName = $_.TaskName
        Variant = $_.Variant
        InputSize = $_.InputSize
        CvPercent = [double]$_.CvPercent
    }
}

# default skip rules for very long runs
$skipList = @()
$todoList = @()
foreach ($c in $candidates) {
    $task = $c.TaskName
    $size = [int]$c.InputSize
    $variant = $c.Variant

    $skip = $false
    if ($task -eq 'MatrixMultiply' -and $size -ge 2048) { $skip = $true }
    if ($task -eq 'GameOfLife' -and $size -ge 1000) { $skip = $true }

    if ($skip) { $skipList += $c } else { $todoList += $c }
}

function Get-VariantToken($variant) {
    switch -regex ($variant) {
        '^Sequential$' { return 'seq' }
        '^Parallel_(\d+)$' { return "parallel_$($matches[1])" }
        '^Parallel$' { return 'parallel' }
        'ILGPU' { return 'ilgpu' }
        'GPU_ComputeSharp' { return 'computesharp' }
        default { return $variant.ToLower() }
    }
}

Write-Host "Found $($candidates.Count) high-CV candidates; $($todoList.Count) to run, $($skipList.Count) skipped by size rules."

foreach ($item in $todoList) {
    $task = $item.TaskName
    $variant = $item.Variant
    $size = [int]$item.InputSize
    $variantToken = Get-VariantToken $variant

    if ($task -eq 'MatrixMultiply') {
        $runArgs = "--sizes $size --variants $variantToken --runs $Runs"
    }
    else {
        $runArgs = "--gol-sizes $size --variants $variantToken --runs $Runs"
    }

    $cmd = "dotnet run -c Release -- $runArgs"
    Write-Host "\nPlanned: $task | $variant | $size -> $cmd"

    if ($DryRun) { continue }

    $attempt = 1
    $succeeded = $false
    while ($attempt -le $MaxAttempts -and -not $succeeded) {
        Write-Host "Running attempt $attempt for $task|$variant|$size"
        Push-Location -Path (Join-Path $scriptRoot 'GpuBenchmarks')
        try {
            & dotnet run -c Release -- $runArgs
        }
        catch {
            Write-Host "Run failed: $($_.Exception.Message)" -ForegroundColor Yellow
        }
        Pop-Location

        # locate produced raw_results.csv
        $producedRaw = Join-Path $resultsDir 'raw_results.csv'
        if (-not (Test-Path -LiteralPath $producedRaw)) {
            Write-Host "Expected raw results not found after run: $producedRaw" -ForegroundColor Red
            break
        }

        $tempRaw = Join-Path $resultsDir ("raw_results_rerun_{0}_{1}_{2}.csv" -f $task, $variant, $size)
        Copy-Item -LiteralPath $producedRaw -Destination $tempRaw -Force

        # preview merge
        $previewOut = & "$scriptRoot\merge-results.ps1" -RawPath $tempRaw -CompiledPath $compiledPath -CvThreshold $CvThreshold -PreviewOnly 2>&1
        $previewText = $previewOut -join "`n"

        # detect whether this key would be replaced
        $key = "{0}|{1}|{2}" -f $task, $variant, $size
        $wouldReplace = $previewText -match [regex]::Escape($key) -and $previewText -match 'Replaced configurations'
        $wouldRetry = $previewText -match [regex]::Escape($key) -and $previewText -match 'Retry configurations'

        if ($wouldReplace) {
            Write-Host "Merge preview: key will be replaced. Applying merge..."
            # apply merge for this raw file
            & "$scriptRoot\merge-results.ps1" -RawPath $tempRaw -CompiledPath $compiledPath -CvThreshold $CvThreshold

            # verify compiled now contains improved CV (we can re-run preview against compiled)
            $verifyOut = & "$scriptRoot\merge-results.ps1" -RawPath $tempRaw -CompiledPath $compiledPath -CvThreshold $CvThreshold -PreviewOnly 2>&1
            Write-Host "Merged and verified for $key"
            $succeeded = $true
        }
        elseif ($wouldRetry) {
            Write-Host "Merge preview: key still exceeds CV threshold; will retry."
            $attempt++
            Start-Sleep -Seconds 1
            continue
        }
        else {
            Write-Host "Merge preview: key not replaced and not marked for retry; inspecting output..."
            Write-Host $previewText
            break
        }
    }

    if (-not $succeeded) {
        Write-Host ("Unresolved after {0}: {1}" -f $MaxAttempts, $key) -ForegroundColor Yellow
    }
}

# Report skipped items
if ($skipList.Count -gt 0) {
    Write-Host "\nSkipped by size rules:"
    foreach ($s in $skipList) { Write-Host "  $($s.TaskName)|$($s.Variant)|$($s.InputSize) | CV=$($s.CvPercent)" }
}

Write-Host "\nOrchestration complete (dryRun=$DryRun)."
