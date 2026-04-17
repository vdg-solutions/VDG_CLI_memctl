# deploy.ps1 — build win-x64 self-contained binary and install to user PATH
# Usage: .\deploy.ps1
# If blocked: Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

$ErrorActionPreference = "Stop"

$ScriptDir   = $PSScriptRoot
$Project     = Join-Path $ScriptDir "src\memctl\memctl.csproj"
$PublishOut  = Join-Path $ScriptDir "dist\win-x64"
$SrcBin      = Join-Path $PublishOut "memctl.exe"
$DestDir     = Join-Path $HOME ".local\bin"
$DestBin     = Join-Path $DestDir "memctl.exe"
$RequiredLibs = @("onnxruntime.dll", "e_sqlite3.dll", "onnxruntime_providers_shared.dll")

# --- build ---

Write-Host "Building memctl (win-x64, self-contained)..."
dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $PublishOut `
    --nologo -v q

if (-not (Test-Path $SrcBin)) {
    Write-Error "Build succeeded but memctl.exe not found at $SrcBin"
    exit 1
}

# --- validate native libs ---

foreach ($lib in $RequiredLibs) {
    $libPath = Join-Path $PublishOut $lib
    if (-not (Test-Path $libPath)) {
        Write-Error "Required native lib missing after publish: $libPath"
        exit 1
    }
}

Write-Host "Build OK"

# --- deploy ---

if (Test-Path $DestBin) {
    Write-Host "Updating existing install at $DestBin"
}

New-Item -ItemType Directory -Force -Path $DestDir | Out-Null

Copy-Item $SrcBin $DestBin -Force
foreach ($lib in $RequiredLibs) {
    Copy-Item (Join-Path $PublishOut $lib) (Join-Path $DestDir $lib) -Force
}

Write-Host "Deployed: $DestBin"

# --- PATH ---

$userPathRaw = [Environment]::GetEnvironmentVariable("PATH", "User")
$userPath = if ($null -ne $userPathRaw) { $userPathRaw } else { "" }
if ($userPath -notlike "*$DestDir*") {
    $newPath = if ($userPath) { "$DestDir;$userPath" } else { $DestDir }
    [Environment]::SetEnvironmentVariable("PATH", $newPath, "User")
    Write-Host "Added $DestDir to user PATH — open a new terminal to apply"
} else {
    Write-Host "PATH OK"
}

# --- smoke check ---

Write-Host ""
& $DestBin --version
