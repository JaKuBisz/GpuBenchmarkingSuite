# setup.ps1 - Dependency checker and installer for GpuBenchmarks on Windows
# Run from the repo root:  .\setup.ps1
# Or as admin for system-wide installs: powershell -ExecutionPolicy Bypass -File setup.ps1

$ErrorActionPreference = "Continue"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Join-Path $ScriptDir "GpuBenchmarks"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  GPU Benchmark Suite - Setup and Dependency Check" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# --- Helper functions ---------------------------------------------------------
function Check-Command($cmd) {
    return $null -ne (Get-Command $cmd -ErrorAction SilentlyContinue)
}

function Print-OK($msg)   { Write-Host "  [OK]   $msg" -ForegroundColor Green }
function Print-WARN($msg) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }
function Print-FAIL($msg) { Write-Host "  [FAIL] $msg" -ForegroundColor Red }
function Print-INFO($msg) { Write-Host "  [INFO] $msg" -ForegroundColor Cyan }

$allGood = $true

# --- 1. Check .NET 8 SDK ------------------------------------------------------
Write-Host "1. Checking .NET SDK..." -ForegroundColor White
if (Check-Command "dotnet") {
    $dotnetVersion = dotnet --version 2>&1
    if ($dotnetVersion -match "^8\.") {
        Print-OK ".NET SDK $dotnetVersion found"
    } elseif ($dotnetVersion -match "^[89]\.|^[1-9][0-9]\.") {
        Print-OK ".NET SDK $dotnetVersion found (compatible)"
    } else {
        Print-WARN ".NET SDK $dotnetVersion found - .NET 8 recommended"
        Print-INFO "Download: https://dotnet.microsoft.com/download/dotnet/8.0"
    }

    # List all installed SDK versions
    $sdks = dotnet --list-sdks 2>&1
    Print-INFO "Installed SDKs:"
    $sdks | ForEach-Object { Write-Host "           $_" }
} else {
    Print-FAIL ".NET SDK not found!"
    Print-INFO "Download .NET 8 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0"
    $allGood = $false

    # Try to install via winget
    if (Check-Command "winget") {
        $answer = Read-Host "  Install .NET 8 SDK via winget? (y/N)"
        if ($answer -eq "y" -or $answer -eq "Y") {
            Write-Host "  Installing .NET 8 SDK..." -ForegroundColor Yellow
            winget install Microsoft.DotNet.SDK.8 --accept-package-agreements --accept-source-agreements
            if ($LASTEXITCODE -eq 0) {
                Print-OK ".NET 8 SDK installed. Please restart this script."
            } else {
                Print-FAIL "Installation failed. Install manually from: https://dotnet.microsoft.com/download"
            }
        }
    }
}

# --- 2. Check GPU drivers -----------------------------------------------------
Write-Host ""
Write-Host "2. Checking GPU drivers..." -ForegroundColor White

# Detect GPUs via WMI
$gpus = Get-WmiObject Win32_VideoController -ErrorAction SilentlyContinue
if ($gpus) {
    foreach ($gpu in $gpus) {
        Print-INFO "Found GPU: $($gpu.Name)  [Driver: $($gpu.DriverVersion)]"
    }
} else {
    Print-WARN "Could not query GPUs via WMI"
}

# Check for NVIDIA CUDA
$nvidiaSmi = Get-Command "nvidia-smi" -ErrorAction SilentlyContinue
if ($nvidiaSmi) {
    Print-OK "NVIDIA driver found (nvidia-smi available)"
    $smiOut = nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader 2>&1
    Write-Host "    $smiOut" -ForegroundColor Gray
} else {
    Print-INFO "nvidia-smi not in PATH - CUDA support requires NVIDIA driver"
    Print-INFO "NVIDIA CUDA drivers: https://www.nvidia.com/Download/index.aspx"
}

# Check for OpenCL (AMD/Intel/any GPU)
$openclDlls = @(
    "C:\Windows\System32\OpenCL.dll",
    "C:\Windows\SysWOW64\OpenCL.dll"
)
$openclFound = $false
foreach ($dll in $openclDlls) {
    if (Test-Path $dll) {
        Print-OK "OpenCL runtime found: $dll"
        $openclFound = $true
        break
    }
}
if (-not $openclFound) {
    Print-WARN "OpenCL.dll not found in System32/SysWOW64"
    Print-INFO "Install GPU drivers to get OpenCL support:"
    Print-INFO "  AMD:   https://www.amd.com/support"
    Print-INFO "  Intel: https://www.intel.com/content/www/us/en/download-center/home.html"
    Print-INFO "  NVIDIA: https://www.nvidia.com/Download/index.aspx"
    Print-INFO "NOTE: ILGPU also has a CPU emulator fallback - GPU tests will use it if no GPU found."
}

# --- 3. Restore NuGet packages ------------------------------------------------
Write-Host ""
Write-Host "3. Restoring NuGet packages (ILGPU, System.Management)..." -ForegroundColor White
if (Test-Path $ProjectDir) {
    Push-Location $ProjectDir
    dotnet restore 2>&1 | ForEach-Object {
        if ($_ -match "error") { Print-FAIL $_ }
        elseif ($_ -match "warn") { Print-WARN $_ }
        else { Write-Host "    $_" -ForegroundColor Gray }
    }
    if ($LASTEXITCODE -eq 0) {
        Print-OK "NuGet packages restored successfully"
    } else {
        Print-FAIL "NuGet restore failed"
        $allGood = $false
    }
    Pop-Location
} else {
    Print-FAIL "Project directory not found: $ProjectDir"
    $allGood = $false
}

# --- 4. Build (Release) -------------------------------------------------------
Write-Host ""
Write-Host "4. Building project (Release)..." -ForegroundColor White
if (Test-Path $ProjectDir) {
    Push-Location $ProjectDir
    $buildOut = dotnet build -c Release 2>&1
    $buildOut | ForEach-Object {
        if ($_ -match " error ") { Print-FAIL $_; $allGood = $false }
        elseif ($_ -match "warning") { Print-WARN $_ }
    }
    if ($LASTEXITCODE -eq 0) {
        Print-OK "Build succeeded"
    } else {
        Print-FAIL "Build failed - check errors above"
        $allGood = $false
    }
    Pop-Location
}

# --- 5. List ILGPU devices (quick test) ---------------------------------------
Write-Host ""
Write-Host "5. Listing available ILGPU accelerator devices..." -ForegroundColor White
if (Test-Path $ProjectDir) {
    Push-Location $ProjectDir
    # We pass --list-devices flag (Program.cs handles it gracefully; if not, will just run)
    # Instead, run a small inline C# snippet via dotnet-script or just inform user
    Print-INFO "Run the benchmark with --list-devices to see all ILGPU-detected devices:"
    Print-INFO "  cd $ProjectDir"
    Print-INFO "  dotnet run -c Release -- --list-devices"
    Pop-Location
}

# --- Summary ------------------------------------------------------------------
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
if ($allGood) {
    Write-Host "  All checks passed! Ready to run:" -ForegroundColor Green
    Write-Host ""
    Write-Host "  cd $ProjectDir" -ForegroundColor White
    Write-Host "  dotnet run -c Release                   # auto-select GPU" -ForegroundColor White
    Write-Host "  dotnet run -c Release -- --gpu 0        # force GPU index 0" -ForegroundColor White
    Write-Host "  dotnet run -c Release -- --list-devices # list available GPUs" -ForegroundColor White
} else {
    Write-Host "  Some checks FAILED - fix the issues above before running." -ForegroundColor Red
}
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "GPU Support Summary:" -ForegroundColor White
Write-Host "  NVIDIA  -> Install CUDA drivers (any version >= 10)"  -ForegroundColor Gray
Write-Host "  AMD     -> Install ROCm or AMDGPU-PRO with OpenCL"    -ForegroundColor Gray
Write-Host "  Intel   -> Install Intel GPU driver (includes OpenCL)" -ForegroundColor Gray
Write-Host "  No GPU  -> ILGPU CPU emulator is used automatically"   -ForegroundColor Gray
Write-Host ""

