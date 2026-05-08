#Requires -Version 5.1
# Usage: .\Install.ps1 [-Dir <path>]
# Or:    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/vdg-solutions/memctl-releases/main/Install.ps1" -OutFile "$env:TEMP\memctl-install.ps1"; & "$env:TEMP\memctl-install.ps1"
[CmdletBinding()]
param(
    [string]$Dir = "$env:USERPROFILE\.local\bin"
)

$ErrorActionPreference = 'Stop'

$Repo       = "vdg-solutions/memctl-releases"
$NativeDlls = @('onnxruntime.dll', 'e_sqlite3.dll', 'onnxruntime_providers_shared.dll')

function Get-LatestTag {
    try {
        $resp = Invoke-WebRequest -Uri "https://api.github.com/repos/$Repo/releases/latest" `
            -UseBasicParsing -ErrorAction Stop
        $tag = ($resp.Content | ConvertFrom-Json).tag_name
    } catch {
        Write-Error "Failed to fetch latest release tag: $_"
        exit 1
    }
    if (-not $tag) { Write-Error "Empty tag_name from GitHub API"; exit 1 }
    return $tag
}

$tag      = Get-LatestTag
$ver      = $tag.TrimStart('v')
$assetUrl = "https://github.com/$Repo/releases/download/$tag/memctl-win-x64-$ver.zip"

Write-Host "Installing memctl $tag to $Dir"

$tmpDir = Join-Path $env:TEMP "memctl-install-$ver"
$tmpZip = Join-Path $env:TEMP "memctl-win-x64-$ver.zip"

try {
    Invoke-WebRequest -Uri $assetUrl -OutFile $tmpZip -UseBasicParsing -ErrorAction Stop
} catch {
    Write-Error "Download failed: $assetUrl`n$_"
    exit 1
}

if ((Get-Item $tmpZip).Length -eq 0) {
    Write-Error "Download empty or failed"
    exit 1
}

try {
    Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force -ErrorAction Stop
} catch {
    Write-Error "Archive corrupt or failed to extract: $_"
    exit 1
}

if (-not (Test-Path $Dir)) { New-Item -ItemType Directory -Path $Dir -Force | Out-Null }

$target = Join-Path $Dir 'memctl.exe'
$aside  = "$target.aside"

if (Test-Path $target) { Move-Item $target $aside -Force }

try {
    Copy-Item (Join-Path $tmpDir 'memctl.exe') $target -Force
    foreach ($dll in $NativeDlls) {
        $src = Join-Path $tmpDir $dll
        if (Test-Path $src) { Copy-Item $src (Join-Path $Dir $dll) -Force }
    }

    & $target --version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "memctl --version exited $LASTEXITCODE" }
} catch {
    Write-Warning "Install failed, rolling back: $_"
    if (Test-Path $aside) { Move-Item $aside $target -Force }
    Remove-Item $tmpDir, $tmpZip -Recurse -Force -ErrorAction SilentlyContinue
    exit 1
}

if (Test-Path $aside) { Remove-Item $aside -Force }
Remove-Item $tmpDir, $tmpZip -Recurse -Force -ErrorAction SilentlyContinue

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath -notlike "*$Dir*") {
    [Environment]::SetEnvironmentVariable('Path', "$userPath;$Dir", 'User')
    Write-Host "Added $Dir to user PATH (restart shell to take effect)"
}

# Sync memctl skill doc to ~/.claude/skills/memctl/SKILL.md
$skillDir = Join-Path $env:USERPROFILE ".claude\skills\memctl"
if (Test-Path $skillDir) {
    $skillUrl = "https://raw.githubusercontent.com/$Repo/main/SKILL.md"
    $skillDest = Join-Path $skillDir "SKILL.md"
    try {
        Invoke-WebRequest -Uri $skillUrl -OutFile $skillDest -UseBasicParsing -ErrorAction Stop
        Write-Host "Skill doc synced to $skillDest"
    } catch {
        Write-Warning "Skill doc sync failed (non-fatal): $_"
    }
}

Write-Host "memctl $tag installed to $Dir"
