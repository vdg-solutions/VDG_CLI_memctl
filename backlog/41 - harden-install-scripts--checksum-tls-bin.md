---
id: 41
type: task
title: Harden install scripts — binary verify, rename-aside, macOS signing
status: Done
priority: normal
tags:
- install,security
created: 2026-05-07
updated: 2026-05-07
---

## Description

`install.ps1` và `install.sh` của memctl cần thêm robustness cho install-from-local workflow. memctl install từ local `dist/` artifacts (không download từ internet), nên checksum/TLS/URL validation **không áp dụng** — những thứ đó chỉ relevant khi có online download mode.

Tập trung vào những thứ thực sự có giá trị cho local install: binary verify sau copy, rename-aside để rollback dễ, macOS quarantine fix, partial-download guard cho pipe-install.

## In scope

| Feature | Applies to |
|---------|-----------|
| Rename-aside existing binary trước khi overwrite (`memctl.exe` → `memctl.exe.old`) | `install.ps1` |
| Verify binary sau install: chạy `memctl --version` → fail install nếu không chạy được | cả hai |
| `--dir <path>` flag — override install directory | cả hai |
| Wrap toàn bộ trong `main()` function — pipe-safe, guard partial download | `install.sh` |
| `xattr -d com.apple.quarantine` + `codesign --sign - --force` sau copy (macOS only) | `install.sh` |
| Cleaner PATH check: `tr ':' '\n' | grep -qx` | `install.sh` |

## Out of scope

- SHA256 checksum verify — không có download mode
- TLS 1.2+ enforcement — không có download mode
- Non-HTTPS URL rejection — không có download mode
- `--skip-config` flag — config hiện tại đơn giản, không cần skip

## Changes

### install.ps1

```powershell
# 1. Rename-aside before overwrite
if (Test-Path $DestPath) {
    Move-Item $DestPath "$DestPath.old" -Force
}

# 2. Copy binary
Copy-Item $SrcPath $DestPath -Force

# 3. Verify binary runs
$ver = & $DestPath --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Binary verification failed: $ver"
    # Rollback
    if (Test-Path "$DestPath.old") { Move-Item "$DestPath.old" $DestPath -Force }
    exit 1
}

# 4. --dir flag
param([string]$Dir = "$env:LOCALAPPDATA\memctl")
```

### install.sh

```bash
main() {
    # ... all install logic inside main()

    # macOS quarantine fix
    if [[ "$OSTYPE" == "darwin"* ]]; then
        xattr -d com.apple.quarantine "$dest" 2>/dev/null || true
        codesign --sign - --force "$dest" 2>/dev/null || true
    fi

    # Verify binary runs
    if ! "$dest" --version >/dev/null 2>&1; then
        echo "Error: binary verification failed" >&2
        exit 1
    fi
}
main "$@"
```

## Acceptance criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `install.ps1` renames existing binary to `.old` before overwrite | run on machine with existing memctl → `.old` file created |
| AC-2 | `install.ps1` rollbacks to `.old` if new binary fails `--version` | corrupt binary → install fails, old binary restored |
| AC-3 | `install.sh` wrapped in `main()` — safe to pipe from `curl \| bash` | pipe install works, no partial execution |
| AC-4 | `install.sh` runs `xattr`/`codesign` on macOS | test on mac: binary runs without quarantine dialog |
| AC-5 | Both scripts verify binary runs after install | verify `--version` called and exit 1 on failure |
| AC-6 | `--dir` flag overrides install location | `install.ps1 -Dir C:\tools` → binary at `C:\tools\memctl.exe` |

## Files

- `install.ps1`
- `install.sh`

## Effort

~2h: ps1 rename+verify+dir (0.75h) + sh main wrapper+codesign+verify+dir (0.75h) + test (0.5h)

## Comments

**2026-05-07 10:40 user:** Pipeline complete. Renamed-aside, binary verify+rollback, macOS signing, --dir flag — all 6 ACs. Merged to main.
