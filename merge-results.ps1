[CmdletBinding()]
param(
    [string]$RawPath,
    [string]$CompiledPath,
    [double]$CvThreshold = 10.0,
    [switch]$PreviewOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if ([string]::IsNullOrWhiteSpace($RawPath)) {
    $RawPath = Join-Path $scriptRoot 'GpuBenchmarks\results\raw_results.csv'
}
if ([string]::IsNullOrWhiteSpace($CompiledPath)) {
    $CompiledPath = Join-Path $scriptRoot 'GpuBenchmarks\results\raw_results_compiled.csv'
}

function Get-Key {
    param([psobject]$Row)
    return '{0}|{1}|{2}' -f $Row.TaskName, $Row.Variant, $Row.InputSize
}

function Get-Stats {
    param([System.Collections.Generic.List[psobject]]$Rows)

    $times = @($Rows | ForEach-Object { [double]$_.TimeMs }) | Sort-Object
    if ($times.Count -eq 0) {
        throw 'Cannot compute statistics for an empty group.'
    }

    $mean = ($times | Measure-Object -Average).Average
    $median = if ($times.Count % 2 -eq 0) {
        ($times[$times.Count / 2 - 1] + $times[$times.Count / 2]) / 2.0
    }
    else {
        $times[$times.Count / 2]
    }

    $sumSquares = 0.0
    foreach ($time in $times) {
        $delta = $time - $mean
        $sumSquares += $delta * $delta
    }

    $variance = $sumSquares / $times.Count
    $stddev = [Math]::Sqrt($variance)
    $cvPercent = if ($mean -gt 0) { $stddev / $mean * 100.0 } else { [double]::NaN }

    [pscustomobject]@{
        Count = $times.Count
        MedianMs = $median
        MeanMs = $mean
        MinMs = ($times | Measure-Object -Minimum).Minimum
        MaxMs = ($times | Measure-Object -Maximum).Maximum
        StdDevMs = $stddev
        CvPercent = $cvPercent
        AllValid = @($Rows | Where-Object { -not [bool]$_.IsValid }).Count -eq 0
    }
}

function Group-ByKey {
    param([psobject[]]$Rows)

    $groups = @{}
    foreach ($row in $Rows) {
        $key = Get-Key -Row $row
        if (-not $groups.ContainsKey($key)) {
            $groups[$key] = New-Object 'System.Collections.Generic.List[psobject]'
        }

        $groups[$key].Add($row)
    }

    return $groups
}

function Format-Double {
    param([double]$Value)

    if ([double]::IsNaN($Value)) {
        return 'N/A'
    }

    return $Value.ToString('F4', [System.Globalization.CultureInfo]::InvariantCulture)
}

if (-not (Test-Path -LiteralPath $RawPath)) {
    throw "Raw results not found: $RawPath"
}

$rawRows = @(Import-Csv -LiteralPath $RawPath)
if ($rawRows.Count -eq 0) {
    throw "Raw results file is empty: $RawPath"
}

$compiledRows = @()
if (Test-Path -LiteralPath $CompiledPath) {
    $compiledRows = @(Import-Csv -LiteralPath $CompiledPath)
}

$rawGroups = Group-ByKey -Rows $rawRows
$compiledGroups = Group-ByKey -Rows $compiledRows

$rawStatsByKey = @{}
foreach ($entry in $rawGroups.GetEnumerator()) {
    $rawStatsByKey[$entry.Key] = Get-Stats -Rows $entry.Value
}

$compiledStatsByKey = @{}
foreach ($entry in $compiledGroups.GetEnumerator()) {
    $compiledStatsByKey[$entry.Key] = Get-Stats -Rows $entry.Value
}

$replaceKeys = New-Object 'System.Collections.Generic.List[string]'
$retryKeys = New-Object 'System.Collections.Generic.List[string]'
$skipKeys = New-Object 'System.Collections.Generic.List[string]'

foreach ($entry in $rawStatsByKey.GetEnumerator() | Sort-Object Key) {
    $key = $entry.Key
    $stats = $entry.Value
    $hasCompiled = $compiledStatsByKey.ContainsKey($key)
    $compiledStats = if ($hasCompiled) { $compiledStatsByKey[$key] } else { $null }

    $rawCv = $stats.CvPercent
    $compiledCv = if ($hasCompiled) { $compiledStats.CvPercent } else { [double]::PositiveInfinity }
    $meetsThreshold = -not [double]::IsNaN($rawCv) -and $rawCv -le $CvThreshold
    $improved = (-not $hasCompiled) -or ($rawCv -lt $compiledCv)

    if (-not $stats.AllValid) {
        $skipKeys.Add($key)
        continue
    }

    if ($improved) {
        $replaceKeys.Add($key)
    }

    if (-not $meetsThreshold) {
        $retryKeys.Add($key)
    }
}

$replaceSet = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($key in $replaceKeys) {
    [void]$replaceSet.Add($key)
}

$keptCompiledRows = @()
foreach ($row in $compiledRows) {
    if (-not $replaceSet.Contains((Get-Key -Row $row))) {
        $keptCompiledRows += $row
    }
}

# Only include raw rows that belong to the replace set (do not append all raw rows)
$replacedRawRows = @()
foreach ($entry in $rawGroups.GetEnumerator()) {
    if ($replaceSet.Contains($entry.Key)) {
        foreach ($r in $entry.Value) { $replacedRawRows += $r }
    }
}

$mergedRows = @($keptCompiledRows + $replacedRawRows)
$mergedRows = $mergedRows | Sort-Object `
    @{ Expression = { [string]$_.TaskName } }, `
    @{ Expression = { [string]$_.Variant } }, `
    @{ Expression = { [int]$_.InputSize } }, `
    @{ Expression = { [int]$_.RunNumber } }

if (-not $PreviewOnly) {
    $mergedRows | Export-Csv -LiteralPath $CompiledPath -NoTypeInformation
}

Write-Host "Raw rows: $($rawRows.Count)"
Write-Host "Compiled rows before merge: $($compiledRows.Count)"
Write-Host "Compiled rows after merge: $($mergedRows.Count)"
Write-Host "Replaced keys: $($replaceKeys.Count)"
Write-Host "Retry keys: $($retryKeys.Count)"
Write-Host "Skipped keys: $($skipKeys.Count)"

if ($replaceKeys.Count -gt 0) {
    Write-Host ''
    Write-Host 'Replaced configurations:'
    foreach ($key in $replaceKeys) {
        $stats = $rawStatsByKey[$key]
        Write-Host ('  {0} | CV={1}% | runs={2}' -f $key, (Format-Double $stats.CvPercent), $stats.Count)
    }
}

if ($retryKeys.Count -gt 0) {
    Write-Host ''
    Write-Host 'Retry configurations:'
    foreach ($key in $retryKeys) {
        $stats = $rawStatsByKey[$key]
        $compiledStats = if ($compiledStatsByKey.ContainsKey($key)) { $compiledStatsByKey[$key] } else { $null }
        $compiledCvText = if ($null -ne $compiledStats) { Format-Double $compiledStats.CvPercent } else { 'N/A' }
        Write-Host ('  {0} | raw CV={1}% | compiled CV={2}% | runs={3}' -f $key, (Format-Double $stats.CvPercent), $compiledCvText, $stats.Count)
    }
}

if ($skipKeys.Count -gt 0) {
    Write-Host ''
    Write-Host 'Skipped configurations:'
    foreach ($key in $skipKeys) {
        $stats = $rawStatsByKey[$key]
        Write-Host ('  {0} | CV={1}% | runs={2}' -f $key, (Format-Double $stats.CvPercent), $stats.Count)
    }
}
