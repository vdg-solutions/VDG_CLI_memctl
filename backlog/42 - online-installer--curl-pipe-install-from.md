---
id: 42
type: task
title: Online installer — curl-pipe install from GitHub Releases
status: Todo
priority: normal
tags:
- install,distribution
created: 2026-05-07
updated: 2026-05-07
---

## Description

End users hiện tại không thể cài memctl mà không clone source + build. Cần một online installer script mà user chỉ cần chạy một lệnh duy nhất — không cần .NET SDK, không cần build.

Target: `curl -fsSL https://raw.githubusercontent.com/vdg-solutions/memctl-releases/main/install.sh | bash`

## In scope

### `install.sh` (Linux/macOS)

- Detect platform RID từ `uname -s / uname -m`
- Fetch latest release tag từ GitHub API (`api.github.com/repos/vdg-solutions/memctl-releases/releases/latest`)
- Download `.tar.gz` artifact cho đúng RID từ release assets
- Verify download không corrupt (file size > 0, tar extractable)
- Extract → copy binary + native libs vào `~/.local/bin` (hoặc `--dir` override)
- `chmod +x`, macOS quarantine fix (`xattr -d`, `codesign --sign -`)
- Verify binary runs: `memctl --version`
- PATH reminder nếu chưa có
- Wrap toàn bộ trong `main()` — pipe-safe

### `Install.ps1` (Windows)

- Fetch latest release tag từ GitHub API
- Download `.zip` artifact cho `win-x64`
- Verify + extract vào temp dir
- Rename-aside existing binary, copy mới, verify `--version`, rollback nếu fail
- Copy native libs (onnxruntime.dll, e_sqlite3.dll, onnxruntime_providers_shared.dll)
- `--Dir` flag override install directory (default: `~\.local\bin`)
- Add to user PATH nếu chưa có

### Release repo wiring

- Hai scripts này sống ở `vdg-solutions/memctl-releases` (public repo)
- CI workflow (`release.yml`) sync scripts vào release repo khi tag push
- README cập nhật one-liner install command

## Out of scope

- SHA256 checksum verify (GitHub HTTPS đủ trust model cho use case này)
- `--version` pinning (luôn lấy latest; nếu cần pin thì user tự download artifact)
- Chocolatey / Homebrew / winget packaging (separate task)
- Windows: `irm ... | iex` style (dùng explicit download thay vì pipe vì PowerShell pipe model khác)

## Implementation notes

GitHub API để lấy latest release:
```bash
LATEST=$(curl -fsSL https://api.github.com/repos/vdg-solutions/memctl-releases/releases/latest \
  | grep '"tag_name"' | sed 's/.*"tag_name": *"\([^"]*\)".*/\1/')
```

Asset URL pattern: `https://github.com/vdg-solutions/memctl-releases/releases/download/$LATEST/memctl-$RID-$LATEST.tar.gz`

`release.yml` cần thêm step copy `install.sh` + `Install.ps1` vào release repo (hiện sync `plugins/` rồi, thêm scripts vào list).

## Acceptance criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `install.sh` chạy được từ pipe: `curl -fsSL .../install.sh \| bash` — không partial execution | pipe test trên Linux/macOS |
| AC-2 | Detect đúng RID cho linux-x64, linux-arm64, osx-arm64, osx-x64 | test trên từng platform hoặc mock uname |
| AC-3 | Download artifact đúng version từ GitHub Releases | check file tải về match latest tag |
| AC-4 | Binary verify sau install: `memctl --version` exit 0 | chạy xong → version in ra |
| AC-5 | `Install.ps1` Windows: rename-aside + rollback nếu verify fail | corrupt binary test |
| AC-6 | `--dir` / `-Dir` override install directory trên cả hai scripts | `bash install.sh --dir /tmp/test` → binary ở `/tmp/test/memctl` |
| AC-7 | `release.yml` sync scripts vào release repo khi tag push | push tag → scripts có trong release repo |
| AC-8 | README trong release repo có one-liner install command | đọc README sau release |

## Files

- `install.sh` (rewrite — replace local-build version)
- `Install.ps1` (rewrite — replace local-build version, rename từ `install.ps1`)
- `.github/workflows/release.yml` (thêm sync step cho scripts)
- `backlog/wiki/release-runbook.md` (cập nhật install instructions)

## Effort

~3h: install.sh online (1.5h) + Install.ps1 online (1h) + release.yml wiring (0.5h)