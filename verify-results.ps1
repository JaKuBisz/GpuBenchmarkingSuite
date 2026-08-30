param(
    [string]$ResultsDir = "results"
)

$ErrorActionPreference = "Stop"

function Assert-FileExists {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        throw "Missing required file: $Path"
    }
}

function Assert-HasDataRows {
    param([string]$Path)
    $rows = Import-Csv -Path $Path
    if ($rows.Count -lt 1) {
        throw "File has no data rows: $Path"
    }
}

function Assert-HasHeader {
    param([string]$Path)
    $firstLine = Get-Content -Path $Path -TotalCount 1
    if ([string]::IsNullOrWhiteSpace($firstLine)) {
        throw "File has no header: $Path"
    }
}

function Assert-NoInvalidRows {
    param([string]$Path)
    $rows = Import-Csv -Path $Path
    $invalid = $rows | Where-Object { $_.PSObject.Properties.Name -contains "IsValid" -and $_.IsValid -eq "False" }
    if ($invalid) {
        throw "Found rows with IsValid=False in $Path"
    }
}

function Assert-NoNAInColumns {
    param(
        [string]$Path,
        [string[]]$Columns
    )

    $rows = Import-Csv -Path $Path
    foreach ($column in $Columns) {
        if (-not ($rows[0].PSObject.Properties.Name -contains $column)) {
            continue
        }

        $bad = $rows | Where-Object { $_.$column -eq "N/A" -or [string]::IsNullOrWhiteSpace($_.$column) }
        if ($bad) {
            throw "Column '$column' contains N/A/empty values in $Path"
        }
    }
}

$resultsPath = Resolve-Path -Path $ResultsDir -ErrorAction Stop
Write-Host "Verifying results in: $resultsPath"

$requiredFiles = @(
    "raw_results.csv",
    "summary.csv",
    "parallel1_analysis.csv",
    "amdahl_analysis.csv",
    "weak_raw_results.csv",
    "weak_summary.csv",
    "weak_gustafson_analysis.csv",
    "ilgpu_raw_results.csv",
    "ilgpu_summary.csv",
    "ilgpu_scaling_analysis.csv",
    "weak_ilgpu_raw_results.csv",
    "weak_ilgpu_summary.csv",
    "weak_ilgpu_scaling_analysis.csv"
)

foreach ($file in $requiredFiles) {
    $fullPath = Join-Path $resultsPath $file
    Assert-FileExists -Path $fullPath
    Assert-HasHeader -Path $fullPath
}

$mustHaveRows = @(
    "raw_results.csv",
    "summary.csv",
    "weak_raw_results.csv",
    "weak_summary.csv",
    "ilgpu_raw_results.csv",
    "ilgpu_summary.csv",
    "weak_ilgpu_raw_results.csv",
    "weak_ilgpu_summary.csv"
)

foreach ($file in $mustHaveRows) {
    Assert-HasDataRows -Path (Join-Path $resultsPath $file)
}

Assert-NoInvalidRows -Path (Join-Path $resultsPath "raw_results.csv")
Assert-NoInvalidRows -Path (Join-Path $resultsPath "weak_raw_results.csv")
Assert-NoInvalidRows -Path (Join-Path $resultsPath "ilgpu_raw_results.csv")
Assert-NoInvalidRows -Path (Join-Path $resultsPath "weak_ilgpu_raw_results.csv")

Assert-NoNAInColumns -Path (Join-Path $resultsPath "summary.csv") -Columns @("MedianMs", "MeanMs", "MinMs", "MaxMs", "StdDevMs")
Assert-NoNAInColumns -Path (Join-Path $resultsPath "weak_summary.csv") -Columns @("MedianMs", "MeanMs", "MinMs", "MaxMs", "StdDevMs")
Assert-NoNAInColumns -Path (Join-Path $resultsPath "ilgpu_summary.csv") -Columns @("MedianMs", "MeanMs", "MinMs", "MaxMs", "StdDevMs")
Assert-NoNAInColumns -Path (Join-Path $resultsPath "weak_ilgpu_summary.csv") -Columns @("MedianMs", "MeanMs", "MinMs", "MaxMs", "StdDevMs")

Write-Host "All checks passed."
