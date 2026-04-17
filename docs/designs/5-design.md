# Technical Design: Enable Global CLI Install

**Spec:** docs/specs/5-spec.md
**Task:** #5
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Architecture Overview

Pure build/distribution layer change. No source code modifications. Three new scripts complement the existing `build-portable.sh`. The csproj is already fully configured (`PackAsTool=true`, `ToolCommandName=memctl`, `PackageId=memctl`).

**Key research finding:** The existing `nupkg/memctl.1.0.0.nupkg` already embeds all platform native libs under `runtimes/{rid}/native/` — .NET resolves them automatically at runtime. Framework-dependent packaging is correct; no RID-specific nupkg needed.

**gitignore:** Already has `*.nupkg` pattern — no change needed.

### Two install paths

```
dotnet users:   bash build-tool.sh [ver] → nupkg/ → dotnet tool install -g
non-dotnet:     bash build-portable.sh → dist/{platform}/ → bash install.sh (or ./install.ps1)
```

---

## 2. File Changes

### New Files

| File Path | Purpose |
|-----------|---------|
| `build-tool.sh` | Builds nupkg via `dotnet pack`, version-stamped via MSBuild property |
| `install.sh` | Copies portable binary + native libs to `~/.local/bin/` on Linux/macOS |
| `install.ps1` | Copies portable binary + DLLs to `$HOME\.local\bin\` and adds to user PATH on Windows |

### Modified Files

| File Path | Changes | Reason |
|-----------|---------|--------|
| `README.md` | Add "Install" section with both paths | FR-012 |

### Integration Code Blocks

README.md does not exist yet (empty). The Install section content is in § 6 below.

### Deleted Files

None.

---

## 3. Data Model

N/A — no data model changes.

---

## 4. API Design

N/A — CLI tool, no HTTP API.

---

## 5. UI Components

N/A.

---

## 6. Business Logic

### `build-tool.sh` — Logic

```bash
#!/usr/bin/env bash
# build-tool.sh — builds dotnet tool nupkg
# Usage: bash build-tool.sh [version]
# Output: nupkg/memctl.{VERSION}.nupkg

set -euo pipefail

VERSION="${1:-$(git describe --tags --exact-match 2>/dev/null || git rev-parse --short HEAD 2>/dev/null || echo "dev")}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$SCRIPT_DIR/src/memctl/memctl.csproj"
OUT="$SCRIPT_DIR/nupkg"

mkdir -p "$OUT"
rm -f "$OUT"/memctl.*.nupkg

echo "Building memctl tool package $VERSION"
dotnet pack "$SRC" -c Release -p:Version="$VERSION" -o "$OUT" --nologo -v q 2>&1 | grep -v "^$" || true

PKG="$OUT/memctl.$VERSION.nupkg"
if [[ -f "$PKG" ]]; then
    SIZE=$(du -k "$PKG" | cut -f1)
    echo "  done: memctl.$VERSION.nupkg (${SIZE} KB)"
else
    echo "ERROR: expected $PKG not found" >&2
    exit 1
fi

echo ""
echo "Install with:"
echo "  dotnet tool install -g memctl --add-source $OUT"
echo "  (or: dotnet tool update -g memctl --add-source $OUT)"
```

**Note:** Version is injected via `-p:Version` MSBuild property — csproj is never modified.

---

### `install.sh` — Logic

```bash
#!/usr/bin/env bash
# install.sh — installs memctl portable binary to ~/.local/bin
# Usage: bash install.sh
# Requires: bash build-portable.sh must have been run first

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEST_DIR="$HOME/.local/bin"
DEST="$DEST_DIR/memctl"

# detect platform
OS="$(uname -s)"
ARCH="$(uname -m)"
case "$OS-$ARCH" in
    Linux-x86_64)  RID="linux-x64" ;;
    Linux-aarch64) RID="linux-arm64" ;;
    Darwin-arm64)  RID="osx-arm64" ;;
    Darwin-x86_64) RID="osx-x64" ;;
    *) echo "ERROR: unsupported platform $OS-$ARCH" >&2; exit 1 ;;
esac

SRC_DIR="$SCRIPT_DIR/dist/$RID"
SRC_BIN="$SRC_DIR/memctl"

# validate source
if [[ ! -f "$SRC_BIN" ]]; then
    echo "ERROR: $SRC_BIN not found. Run: bash build-portable.sh" >&2
    exit 1
fi

# validate native libs present
NATIVE_LIB_COUNT=$(find "$SRC_DIR" -name "*.so" -o -name "*.dylib" 2>/dev/null | wc -l)
if [[ "$NATIVE_LIB_COUNT" -eq 0 ]]; then
    echo "ERROR: native runtime libs missing in $SRC_DIR. Re-run build-portable.sh" >&2
    exit 1
fi

# warn if already installed
if [[ -f "$DEST" ]]; then
    echo "Warning: memctl already installed at $DEST — overwriting"
fi

# install
mkdir -p "$DEST_DIR"
cp "$SRC_BIN" "$DEST"
chmod +x "$DEST"
# copy native libs alongside binary
find "$SRC_DIR" \( -name "*.so" -o -name "*.dylib" \) -exec cp {} "$DEST_DIR/" \;

echo "Installed: $DEST"
echo "To uninstall: rm $DEST $DEST_DIR/libonnxruntime* $DEST_DIR/libe_sqlite3*"
echo ""

# PATH check
if ! echo "$PATH" | grep -q "$DEST_DIR"; then
    echo "Note: add to your shell profile:"
    echo "  export PATH=\"\$HOME/.local/bin:\$PATH\""
fi
```

---

### `install.ps1` — Logic

```powershell
# install.ps1 — installs memctl to $HOME\.local\bin and adds to user PATH
# Usage: .\install.ps1
# Requires: build-portable.sh must have been run first (produces dist\win-x64\)
# Note: if blocked by execution policy, run:
#   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

$ErrorActionPreference = "Stop"

$ScriptDir  = $PSScriptRoot
$SrcDir     = Join-Path $ScriptDir "dist\win-x64"
$SrcBin     = Join-Path $SrcDir "memctl.exe"
$DestDir    = Join-Path $HOME ".local\bin"
$DestBin    = Join-Path $DestDir "memctl.exe"

$RequiredLibs = @("onnxruntime.dll", "e_sqlite3.dll", "onnxruntime_providers_shared.dll")

# validate source binary
if (-not (Test-Path $SrcBin)) {
    Write-Error "memctl.exe not found at $SrcBin. Run build-portable.sh first."
    exit 1
}

# validate native libs
foreach ($lib in $RequiredLibs) {
    $libPath = Join-Path $SrcDir $lib
    if (-not (Test-Path $libPath)) {
        Write-Error "Required native lib missing: $libPath. Re-run build-portable.sh."
        exit 1
    }
}

# warn if already installed
if (Test-Path $DestBin) {
    Write-Host "Warning: memctl already installed at $DestBin — overwriting"
}

# create dest dir
New-Item -ItemType Directory -Force -Path $DestDir | Out-Null

# copy binary + native libs
Copy-Item $SrcBin $DestBin -Force
foreach ($lib in $RequiredLibs) {
    Copy-Item (Join-Path $SrcDir $lib) (Join-Path $DestDir $lib) -Force
}

Write-Host "Installed: $DestBin"
Write-Host "To uninstall: Remove-Item $DestDir\memctl.exe, $DestDir\onnxruntime*.dll, $DestDir\e_sqlite3.dll"

# add to user PATH if not present
$userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($userPath -notlike "*$DestDir*") {
    [Environment]::SetEnvironmentVariable("PATH", "$DestDir;$userPath", "User")
    Write-Host "Added $DestDir to user PATH"
    Write-Host "Open a new terminal for changes to take effect."
} else {
    Write-Host "$DestDir already in PATH"
}
```

---

### README.md Install Section

```markdown
## Installation

### Option A — dotnet global tool (requires .NET 10 SDK)

Build the package:
```bash
bash build-tool.sh
```

Install globally:
```bash
dotnet tool install -g memctl --add-source ./nupkg
```

Upgrade:
```bash
dotnet tool update -g memctl --add-source ./nupkg
```

Uninstall:
```bash
dotnet tool uninstall -g memctl
```

### Option B — portable binary (no .NET SDK required)

Build portables:
```bash
bash build-portable.sh
```

**Linux / macOS:**
```bash
bash install.sh
```

**Windows (PowerShell):**
```powershell
.\install.ps1
```
```

---

## 7. Error Handling Strategy

| Error Scenario | Handling | Output |
|---------------|----------|--------|
| Source binary missing | `exit 1` with message pointing to build script | `ERROR: ... not found. Run: bash build-portable.sh` |
| Native libs missing | `exit 1` with message | `ERROR: native runtime libs missing` |
| Wrong platform on install.sh | `exit 1` | `ERROR: unsupported platform` |
| dotnet pack fails | Script exits via `set -euo pipefail` | dotnet's own error output |
| Expected nupkg not produced | Explicit file check + `exit 1` | `ERROR: expected .nupkg not found` |

---

## 8. Security Considerations

- `install.ps1` notes execution policy requirement — does not bypass it silently
- No credentials, no network access in any script
- PATH modification is user-scope only (`"User"` scope in SetEnvironmentVariable)

---

## 9. Performance Considerations

- `dotnet pack` is the heaviest operation (~5-10s) — acceptable for a one-time build
- Install scripts are pure file copy — instant

---

## 10. Testing Strategy

| Level | What to Test | How |
|-------|-------------|-----|
| Smoke | build-tool.sh produces nupkg | bash script + file existence check |
| Smoke | dotnet tool install succeeds | dotnet CLI exit code |
| Smoke | memctl --help works after install | process stdout |
| Manual | install.sh on Linux/macOS | run in clean environment |
| Manual | install.ps1 on Windows | run in fresh PowerShell |

---

## 10.5 E2E Scenarios

**Project Type:** cli_tool

### Smoke Scenarios (Layer 2.5)

| Scenario | Command | Expected Output | FR |
|----------|---------|-----------------|-----|
| build-tool.sh produces nupkg | `bash build-tool.sh 1.0.0-test` | `nupkg/memctl.1.0.0-test.nupkg` exists, exit 0 | FR-001, FR-002 |
| nupkg has correct version | inspect nuspec in nupkg | version field = `1.0.0-test` | FR-003 |
| single nupkg in dir | `ls nupkg/*.nupkg \| wc -l` | output: `1` | FR-004 |
| install.sh validates missing src | remove dist/linux-x64/, run install.sh | exit non-zero, stderr contains "not found" | FR-011 |
| install.ps1 validates missing lib | remove onnxruntime.dll, run install.ps1 | error contains "missing" | FR-011 |

---

## 11. Dependencies

| Dependency | Purpose | New? |
|-----------|---------|------|
| .NET 10 SDK | `dotnet pack` for build-tool.sh | No |
| bash | install.sh | No |
| PowerShell 5+ | install.ps1 | No |

---

## 12. Implementation Order

1. Create `build-tool.sh`
2. Create `install.sh`
3. Create `install.ps1`
4. Update `README.md` with Install section
5. Smoke test: `bash build-tool.sh 1.0.0-test` → verify nupkg exists and version is correct

---

## 13. Assumptions & Open Design Decisions

- **Resolved:** Framework-dependent nupkg is correct — native libs already embedded in existing nupkg via `runtimes/{rid}/native/` NuGet convention.
- **Resolved:** `build-tool.sh` stays separate from `build-portable.sh` — independent workflows.
- **Resolved:** `.gitignore` already has `*.nupkg` — no change needed.
- **Assumption:** `dist/{platform}/` artifacts are produced before running install scripts. Scripts fail fast with clear error if not.

---

## 14. Traceability Matrix

| Requirement | File | Test |
|-------------|------|------|
| FR-001, FR-002, FR-003, FR-004 | `build-tool.sh` | Smoke: nupkg exists, version correct, single file |
| FR-005, FR-006, FR-007 | `build-tool.sh` + nupkg | Smoke: dotnet tool install, memctl --help |
| FR-008, FR-010, FR-011, FR-013 | `install.sh` | Smoke: missing src abort, idempotent |
| FR-009, FR-010, FR-011, FR-013 | `install.ps1` | Smoke: missing lib abort, PATH set |
| FR-012 | `README.md` | Code review |
| NFR-001, NFR-002 | nupkg (no change needed) | `memctl model list` post-install |
| NFR-003 | `install.sh`, `install.ps1` | Second run exits cleanly |
| NFR-004 | `build-tool.sh`, `install.sh` | Code review |
