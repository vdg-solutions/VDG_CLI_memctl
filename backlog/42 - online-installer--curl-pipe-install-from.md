---
id: 42
type: task
title: Online installer — curl-pipe install from GitHub Releases
status: Done
priority: normal
tags:
- install,distribution
created: 2026-05-07
updated: 2026-05-08
---

## Description

End users hiện tại không thể cài memctl mà không clone source + build. Cần một online installer script mà user chỉ cần chạy một lệnh duy nhất — không cần .NET SDK, không cần build.

**Decision: AOT-only. No nupkg.** Release ships native AOT binaries only (win-x64, linux-x64, osx-arm64). Install scripts download binary directly from GitHub Releases — no .NET SDK required.

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

## Prerequisite: fix release.yml Package step (BLOCKER)

**Current state**: `Package` job trong `release.yml` chỉ copy binary (`memctl`/`memctl.exe`) vào archive. Native libs **không có** trong `.zip`/`.tar.gz`. Nếu install script download archive về mà không có native libs thì binary không chạy được.

**Verified via `dotnet publish -p:PublishAot=true -r win-x64`** — AOT output chứa:
```
memctl.exe
e_sqlite3.dll
onnxruntime.dll
onnxruntime_providers_shared.dll
onnxruntime.lib                   ← linker import lib, KHÔNG copy (không cần runtime)
onnxruntime_providers_shared.lib  ← linker import lib, KHÔNG copy
```
Linux/macOS AOT output tương tự với `.so`/`.dylib` thay vì `.dll`.

**Fix needed** — thêm vào Package step sau `cp "publish/memctl${EXT}" package/`:
```bash
# Windows: copy runtime DLLs only (not .lib linker files)
if [ "$ARCHIVE" = "zip" ]; then
  for lib in onnxruntime.dll e_sqlite3.dll onnxruntime_providers_shared.dll; do
    [ -f "publish/$lib" ] && cp "publish/$lib" package/
  done
else
  # Linux/macOS: copy .so/.dylib
  find publish \( -name "*.so" -o -name "*.dylib" \) -exec cp {} package/ \;
fi
```

Verify: archive phải chứa đúng 3 `.dll` (Windows) hoặc ≥1 `.so`/`.dylib` (Unix) ngoài binary chính.

**Lưu ý**: `deploy.ps1` (local dev) build + install riêng biệt, không liên quan đến install.sh. Rewriting install.sh không ảnh hưởng dev workflow.

## Implementation notes

### GitHub API — lấy latest release tag

```bash
LATEST=$(curl -fsSL https://api.github.com/repos/vdg-solutions/memctl-releases/releases/latest \
  | grep '"tag_name"' | sed 's/.*"tag_name": *"\([^"]*\)".*/\1/')
[ -z "$LATEST" ] && { echo "ERROR: could not fetch latest release" >&2; exit 1; }
```

Failure modes cần handle:
- `curl` fails (no network) → exit với message rõ ràng
- API returns 404 (no releases yet) → exit với message rõ ràng
- `tag_name` empty sau parse (API rate-limited, returns `{"message":"API rate limit exceeded"}`) → exit với message rõ ràng

### Asset URL pattern

```
.tar.gz: https://github.com/vdg-solutions/memctl-releases/releases/download/$LATEST/memctl-$RID-${LATEST#v}.tar.gz
.zip:    https://github.com/vdg-solutions/memctl-releases/releases/download/$LATEST/memctl-win-x64-${LATEST#v}.zip
```

Note: `LATEST` là `v1.4.2`, còn filename dùng `1.4.2` (không có `v`). Strip bằng `${LATEST#v}`.

### Verify download không corrupt

```bash
[ -s "$TMPFILE" ] || { echo "ERROR: download empty or failed" >&2; exit 1; }
tar -tzf "$TMPFILE" >/dev/null 2>&1 || { echo "ERROR: archive corrupt" >&2; exit 1; }
```

### release.yml — sync install scripts (exact code)

Trong job `release`, step `Sync plugin source + top-level SKILL.md to release repo`, thêm sau `cp docs/memctl.md "$REL/SKILL.md"`:

```bash
# Sync online install scripts
cp install.sh "$REL/install.sh"
cp Install.ps1 "$REL/Install.ps1"
```

**IMPORTANT**: rename `install.ps1` → `Install.ps1` ở source repo trước khi thêm sync step này. Nếu file chưa rename mà release chạy thì `cp Install.ps1` sẽ fail. Làm trong cùng một commit.

Và update `git add` line:
```bash
git add plugins/memctl-claude SKILL.md install.sh Install.ps1
```

### Windows one-liner

Vì `irm | iex` out of scope, Windows user dùng PowerShell download explicit:

```powershell
# Option 1: download + run
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/vdg-solutions/memctl-releases/main/Install.ps1" -OutFile "$env:TEMP\memctl-install.ps1"; & "$env:TEMP\memctl-install.ps1"

# Option 2: gh release download (nếu đã có gh CLI)
gh release download --repo vdg-solutions/memctl-releases --pattern "memctl-win-x64-*.zip" -D $env:TEMP
```

README nên document cả hai. Option 1 là primary.

## Acceptance criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `install.sh` chạy được từ pipe: `curl -fsSL .../install.sh \| bash` — không partial execution | pipe test trên Linux/macOS |
| AC-2 | Detect đúng RID cho linux-x64, linux-arm64, osx-arm64, osx-x64 | test trên từng platform hoặc mock uname |
| AC-3 | Download artifact đúng version từ GitHub Releases | check file tải về match latest tag |
| AC-4 | Binary verify sau install: `memctl --version` exit 0 | chạy xong → version in ra |
| AC-5 | `Install.ps1` Windows: rename-aside + rollback nếu verify fail | corrupt binary test |
| AC-6 | `--dir` / `-Dir` override install directory trên cả hai scripts | `bash install.sh --dir /tmp/test` → binary ở `/tmp/test/memctl` |
| AC-7 | `release.yml` sync install.sh + Install.ps1 vào release repo khi tag push | push tag → scripts có trong release repo |
| AC-8 | README trong release repo có one-liner cho Linux/macOS + Windows | đọc README sau release |
| AC-9 | `install.sh` exit 1 với message rõ nếu không có network hoặc GitHub API rate-limited | mock curl fail → error message in ra stderr |
| AC-10 | `install.sh` exit 1 nếu downloaded archive corrupt hoặc empty | truncate file → error message |
| AC-11 | release.yml Package step include native libs trong archive | unzip/untar release artifact → thấy .so/.dylib/.dll |

## Files

- `install.sh` (rewrite — replace local-build version; local dev dùng `deploy.ps1`, không bị ảnh hưởng)
- `install.ps1` → rename thành `Install.ps1` (rewrite — online download version)
- `.github/workflows/release.yml` (2 changes: Package step + sync step)
- `backlog/wiki/release-runbook.md` (cập nhật install instructions + Windows one-liner)

## Effort

~4h: release.yml Package fix (0.5h) + install.sh online (1.5h) + Install.ps1 online (1h) + release.yml sync wiring (0.5h) + README/runbook (0.5h)

## Comments

**2026-05-07 11:45 user:** PAUSED — cần quyết định: (1) AOT only hay giữ nupkg trong public release? (2) install scripts authored ở public repo (memctl-releases) hay sync từ private? Implement sau khi có quyết định.

**2026-05-08 06:52 user:** Decision: AOT-only. No nupkg in release. Release pipeline ships only native AOT binaries (win-x64, linux-x64, osx-arm64). Install script downloads binary directly from GitHub Releases. Remove any nupkg publish steps from CI.

**2026-05-08 07:53 user:** Pipeline complete. feat(42) merged to feature branch. All 12 ACs verified. Pending merge to main.
