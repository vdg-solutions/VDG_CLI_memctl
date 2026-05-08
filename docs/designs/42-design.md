# Design #42 — Online installer: curl-pipe install from GitHub Releases

## Approach

Four independent file changes, no shared code:

1. **`install.sh`** — full rewrite as online installer
2. **`Install.ps1`** — full rewrite as online installer
3. **`install.ps1`** — delete
4. **`.github/workflows/release.yml`** — 3 targeted patches

## install.sh — full content

```bash
#!/usr/bin/env bash
set -euo pipefail

REPO="vdg-solutions/memctl-releases"
DEFAULT_DIR="$HOME/.local/bin"

detect_rid() {
    local os arch
    os=$(uname -s)
    arch=$(uname -m)
    case "$os" in
        Linux)
            case "$arch" in
                x86_64)  echo "linux-x64" ;;
                aarch64) echo "linux-arm64" ;;
                *) echo "ERROR: unsupported arch: $arch" >&2; exit 1 ;;
            esac ;;
        Darwin)
            case "$arch" in
                arm64)  echo "osx-arm64" ;;
                x86_64) echo "osx-x64" ;;
                *) echo "ERROR: unsupported arch: $arch" >&2; exit 1 ;;
            esac ;;
        *) echo "ERROR: unsupported OS: $os" >&2; exit 1 ;;
    esac
}

fetch_latest_tag() {
    local tag
    tag=$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
        | grep '"tag_name"' | sed 's/.*"tag_name": *"\([^"]*\)".*/\1/')
    if [ -z "$tag" ]; then
        echo "ERROR: could not fetch latest release tag (network error or API rate limit)" >&2
        exit 1
    fi
    echo "$tag"
}

main() {
    local install_dir="$DEFAULT_DIR"

    while [ $# -gt 0 ]; do
        case "$1" in
            --dir) install_dir="$2"; shift 2 ;;
            *) echo "ERROR: unknown option: $1" >&2; exit 1 ;;
        esac
    done

    local rid tag ver asset_url tmpdir tmpfile
    rid=$(detect_rid)
    tag=$(fetch_latest_tag)
    ver="${tag#v}"

    asset_url="https://github.com/$REPO/releases/download/$tag/memctl-$rid-$ver.tar.gz"
    tmpdir=$(mktemp -d)
    tmpfile="$tmpdir/memctl.tar.gz"

    echo "Installing memctl $tag for $rid → $install_dir"

    curl -fsSL "$asset_url" -o "$tmpfile" || {
        echo "ERROR: download failed: $asset_url" >&2
        exit 1
    }
    [ -s "$tmpfile" ] || { echo "ERROR: download empty" >&2; exit 1; }
    tar -tzf "$tmpfile" >/dev/null 2>&1 || { echo "ERROR: archive corrupt" >&2; exit 1; }

    tar -xzf "$tmpfile" -C "$tmpdir"

    mkdir -p "$install_dir"
    cp "$tmpdir/memctl" "$install_dir/memctl"
    # copy native libs (.so / .dylib)
    find "$tmpdir" \( -name "*.so" -o -name "*.dylib" \) -exec cp {} "$install_dir/" \;

    chmod +x "$install_dir/memctl"

    if [ "$(uname -s)" = "Darwin" ]; then
        xattr -d com.apple.quarantine "$install_dir/memctl" 2>/dev/null || true
        codesign --sign - "$install_dir/memctl" 2>/dev/null || true
    fi

    "$install_dir/memctl" --version >/dev/null 2>&1 || {
        echo "ERROR: binary verify failed — memctl --version returned non-zero" >&2
        exit 1
    }

    echo "memctl $tag installed to $install_dir"

    case ":$PATH:" in
        *":$install_dir:"*) ;;
        *) echo "NOTE: add $install_dir to PATH: export PATH=\"$install_dir:\$PATH\"" >&2 ;;
    esac

    rm -rf "$tmpdir"
}

main "$@"
```

## Install.ps1 — full content

```powershell
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Dir = "$env:USERPROFILE\.local\bin"
)

$ErrorActionPreference = 'Stop'
$Repo = "vdg-solutions/memctl-releases"
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
    if (-not $tag) { Write-Error "Empty tag_name from API"; exit 1 }
    return $tag
}

$tag = Get-LatestTag
$ver = $tag.TrimStart('v')
$assetUrl = "https://github.com/$Repo/releases/download/$tag/memctl-win-x64-$ver.zip"

Write-Host "Installing memctl $tag → $Dir"

$tmpDir = Join-Path $env:TEMP "memctl-install-$ver"
$tmpZip = Join-Path $env:TEMP "memctl-win-x64-$ver.zip"

try {
    Invoke-WebRequest -Uri $assetUrl -OutFile $tmpZip -UseBasicParsing -ErrorAction Stop
} catch {
    Write-Error "Download failed: $assetUrl`n$_"
    exit 1
}

if ((Get-Item $tmpZip).Length -eq 0) { Write-Error "Download empty"; exit 1 }

try {
    Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force -ErrorAction Stop
} catch {
    Write-Error "Archive corrupt or failed to extract: $_"
    exit 1
}

if (-not (Test-Path $Dir)) { New-Item -ItemType Directory -Path $Dir -Force | Out-Null }

$target = Join-Path $Dir 'memctl.exe'
$aside  = "$target.aside"

# rename-aside existing binary for rollback
if (Test-Path $target) { Move-Item $target $aside -Force }

try {
    Copy-Item (Join-Path $tmpDir 'memctl.exe') $target -Force
    foreach ($dll in $NativeDlls) {
        $src = Join-Path $tmpDir $dll
        if (Test-Path $src) { Copy-Item $src (Join-Path $Dir $dll) -Force }
    }

    & $target --version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "memctl --version failed" }
} catch {
    Write-Warning "Install failed, rolling back: $_"
    if (Test-Path $aside) { Move-Item $aside $target -Force }
    Remove-Item $tmpDir, $tmpZip -Recurse -Force -ErrorAction SilentlyContinue
    exit 1
}

if (Test-Path $aside) { Remove-Item $aside -Force }
Remove-Item $tmpDir, $tmpZip -Recurse -Force -ErrorAction SilentlyContinue

# add to user PATH if not present
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath -notlike "*$Dir*") {
    [Environment]::SetEnvironmentVariable('Path', "$userPath;$Dir", 'User')
    Write-Host "Added $Dir to user PATH (restart shell to take effect)"
}

Write-Host "memctl $tag installed to $Dir"
```

## release.yml — changes

### Change 1: Remove pack-tool job

Delete the entire `pack-tool` job block (lines that define the nupkg build step).

### Change 2: release job needs

```yaml
# Before
needs: [build, pack-tool]

# After
needs: [build]
```

### Change 3: Package step — add native libs

After the existing `cp "publish/memctl${EXT}" package/` line:

```bash
if [ "$ARCHIVE" = "zip" ]; then
  for lib in onnxruntime.dll e_sqlite3.dll onnxruntime_providers_shared.dll; do
    [ -f "publish/$lib" ] && cp "publish/$lib" package/
  done
else
  find publish \( -name "*.so" -o -name "*.dylib" \) -exec cp {} package/ \;
fi
```

### Change 4: Sync step — add install scripts

In the `Sync plugin source + top-level SKILL.md to release repo` step, after `cp docs/memctl.md "$REL/SKILL.md"`:

```bash
cp install.sh "$REL/install.sh"
cp Install.ps1 "$REL/Install.ps1"
```

Update `git add` line to include scripts:
```bash
git add plugins/memctl-claude SKILL.md install.sh Install.ps1
```

## Test plan (manual, pre-commit)

```bash
# AC-9: network failure simulation
# Temporarily point to bad URL → check stderr message + exit 1

# AC-10: corrupt archive
# Truncate downloaded file → check stderr "archive corrupt" + exit 1

# AC-6: dir override
bash install.sh --dir /tmp/memctl-test
# expect: binary at /tmp/memctl-test/memctl

# AC-7: verify release archive contents
# unzip/untar a release artifact → expect .dll/.so files present

# AC-11: release.yml — pack-tool job gone
grep "pack-tool" .github/workflows/release.yml | wc -l  # expect 0 or only in comments

# AC-12: install.ps1 deleted
test ! -f install.ps1 && echo "OK"
```
