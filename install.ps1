# install.ps1 — installs memctl to ~/.local/bin (or -Dir override) and adds to user PATH
# Usage: .\install.ps1 [-Dir <path>]
# Requires: run build-portable.sh first (produces dist\win-x64\)
#
# If blocked by execution policy, run first:
#   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

param(
    [string]$Dir = (Join-Path $HOME ".local\bin")
)

$ErrorActionPreference = "Stop"

$ScriptDir  = $PSScriptRoot
$SrcDir     = Join-Path $ScriptDir "dist\win-x64"
$SrcBin     = Join-Path $SrcDir "memctl.exe"
$DestDir    = $Dir
$DestBin    = Join-Path $DestDir "memctl.exe"
$DestBinOld = "$DestBin.old"

$RequiredLibs = @("onnxruntime.dll", "e_sqlite3.dll", "onnxruntime_providers_shared.dll")

# validate source binary
if (-not (Test-Path $SrcBin)) {
    Write-Error "memctl.exe not found at $SrcBin`nRun build-portable.sh first."
    exit 1
}

# validate native libs
foreach ($lib in $RequiredLibs) {
    $libPath = Join-Path $SrcDir $lib
    if (-not (Test-Path $libPath)) {
        Write-Error "Required native lib missing: $libPath`nRe-run build-portable.sh."
        exit 1
    }
}

New-Item -ItemType Directory -Force -Path $DestDir | Out-Null

# rename-aside existing binary for rollback
if (Test-Path $DestBin) {
    Move-Item $DestBin $DestBinOld -Force
    Write-Host "Renamed existing binary → $DestBinOld"
}

Copy-Item $SrcBin $DestBin -Force
foreach ($lib in $RequiredLibs) {
    Copy-Item (Join-Path $SrcDir $lib) (Join-Path $DestDir $lib) -Force
}

# verify new binary runs
$ver = & $DestBin --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Binary verification failed: $ver"
    if (Test-Path $DestBinOld) {
        Move-Item $DestBinOld $DestBin -Force
        Write-Host "Rolled back to previous binary."
    }
    exit 1
}
Write-Host "Verified: $ver"

# clean up .old on success
if (Test-Path $DestBinOld) {
    Remove-Item $DestBinOld -Force
}

Write-Host "Installed: $DestBin"
Write-Host ""
Write-Host "To uninstall:"
Write-Host "  Remove-Item $DestBin, $DestDir\onnxruntime*.dll, $DestDir\e_sqlite3.dll, $DestDir\onnxruntime_providers_shared.dll"
Write-Host ""

# add to user PATH if not present
$userPathRaw = [Environment]::GetEnvironmentVariable("PATH", "User")
$userPath = if ($null -ne $userPathRaw) { $userPathRaw } else { "" }
if ($userPath -notlike "*$DestDir*") {
    $newPath = if ($userPath) { "$DestDir;$userPath" } else { $DestDir }
    [Environment]::SetEnvironmentVariable("PATH", $newPath, "User")
    Write-Host "Added $DestDir to user PATH"
    Write-Host "Open a new terminal for the PATH change to take effect."
} else {
    Write-Host "$DestDir already in PATH"
}
