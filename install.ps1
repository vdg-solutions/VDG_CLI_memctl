# install.ps1 — installs memctl to $HOME\.local\bin and adds to user PATH
# Usage: .\install.ps1
# Requires: run build-portable.sh first (produces dist\win-x64\)
#
# If blocked by execution policy, run first:
#   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

$ErrorActionPreference = "Stop"

$ScriptDir  = $PSScriptRoot
$SrcDir     = Join-Path $ScriptDir "dist\win-x64"
$SrcBin     = Join-Path $SrcDir "memctl.exe"
$DestDir    = Join-Path $HOME ".local\bin"
$DestBin    = Join-Path $DestDir "memctl.exe"

$RequiredLibs = @("onnxruntime.dll", "e_sqlite3.dll", "onnxruntime_providers_shared.dll")

# validate binary
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

# warn on overwrite (idempotent — proceed regardless)
if (Test-Path $DestBin) {
    Write-Host "Warning: memctl already installed at $DestBin — overwriting"
}

New-Item -ItemType Directory -Force -Path $DestDir | Out-Null

Copy-Item $SrcBin $DestBin -Force
foreach ($lib in $RequiredLibs) {
    Copy-Item (Join-Path $SrcDir $lib) (Join-Path $DestDir $lib) -Force
}

Write-Host "Installed: $DestBin"
Write-Host ""
Write-Host "To uninstall:"
Write-Host "  Remove-Item $DestBin, $DestDir\onnxruntime*.dll, $DestDir\e_sqlite3.dll, $DestDir\onnxruntime_providers_shared.dll"
Write-Host ""

# add to user PATH if not present
$userPath = [Environment]::GetEnvironmentVariable("PATH", "User") ?? ""
if ($userPath -notlike "*$DestDir*") {
    $newPath = if ($userPath) { "$DestDir;$userPath" } else { $DestDir }
    [Environment]::SetEnvironmentVariable("PATH", $newPath, "User")
    Write-Host "Added $DestDir to user PATH"
    Write-Host "Open a new terminal for the PATH change to take effect."
} else {
    Write-Host "$DestDir already in PATH"
}
