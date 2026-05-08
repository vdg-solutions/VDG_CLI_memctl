# Spec #42 — Online installer: curl-pipe install from GitHub Releases

## Context

Users cannot install memctl without cloning source and building locally. memctl ships
native AOT binaries (no .NET SDK required). Install scripts must download the correct
binary from GitHub Releases with zero local toolchain requirements.

Decision: AOT-only. No nupkg. Release ships win-x64, linux-x64, osx-arm64 binaries.

## Functional Requirements

### FR-1: install.sh — detect platform

`install.sh` MUST detect the host RID from `uname -s` + `uname -m` and map to one of:
`linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`.

Unsupported platforms → stderr message + exit 1.

### FR-2: install.sh — fetch latest release

Fetch latest tag from GitHub API:
`https://api.github.com/repos/vdg-solutions/memctl-releases/releases/latest`

Parse `tag_name` field. If curl fails, 404, or `tag_name` is empty → stderr + exit 1.

### FR-3: install.sh — download + verify

Download `.tar.gz` for detected RID from GitHub Releases.
URL pattern: `https://github.com/vdg-solutions/memctl-releases/releases/download/$TAG/memctl-$RID-${TAG#v}.tar.gz`

Verify: file size > 0 AND `tar -tzf` succeeds. Either fails → stderr + exit 1.

### FR-4: install.sh — install binary + native libs

Extract archive → copy binary + all `.so`/`.dylib` native libs to install dir.
Default install dir: `~/.local/bin`. Override via `--dir <path>`.
`chmod +x` the binary. macOS: run `xattr -d com.apple.quarantine` and `codesign --sign -`.
Verify: `memctl --version` exits 0 after install. If not → stderr + exit 1.

### FR-5: install.sh — PATH reminder

If install dir is not in `$PATH` → print reminder to stderr (non-fatal).

### FR-6: install.sh — pipe-safe

Entire script wrapped in `main()` called at bottom. Safe for `curl -fsSL ... | bash`.

### FR-7: Install.ps1 — fetch + download

Fetch latest tag from GitHub API (same endpoint as install.sh).
Download `.zip` for `win-x64`.
URL pattern: `https://github.com/vdg-solutions/memctl-releases/releases/download/$tag/memctl-win-x64-${tag_num}.zip`

### FR-8: Install.ps1 — verify + install with rollback

Verify: zip extractable. Extract to temp dir.
If existing binary: rename-aside. Copy new binary + native libs (.dll).
Verify: `memctl --version` exits 0. If not → rollback (restore aside), stderr + exit 1.
Default install dir: `~\.local\bin`. Override via `-Dir <path>`.

### FR-9: Install.ps1 — PATH

Add install dir to user PATH via registry if not already present.

### FR-10: release.yml — native libs in archives

Package step MUST copy native libs alongside binary:
- Windows: `onnxruntime.dll`, `e_sqlite3.dll`, `onnxruntime_providers_shared.dll`
- Linux/macOS: all `.so` / `.dylib` files from publish output

Archive must NOT include `.lib` linker import files.

### FR-11: release.yml — sync install scripts

After tag push, CI syncs `install.sh` + `Install.ps1` to `vdg-solutions/memctl-releases` repo.

### FR-12: release.yml — remove nupkg

Remove `pack-tool` job. Change `release` job `needs` from `[build, pack-tool]` to `[build]`.

## Non-Functional Requirements

### NFR-1: No .NET SDK required

Install scripts download pre-built AOT binary. Zero managed runtime dependency.

### NFR-2: Pipe safety

`install.sh` must not read stdin (would consume bash input in pipe mode).
Entire logic in `main()` function, called at EOF.

### NFR-3: Deterministic behavior

Given the same platform + latest tag, script always installs to the same path.
No interactive prompts.

### NFR-4: Failure isolation

Each failure path (network, corrupt archive, verify fail) exits immediately with a distinct
message. No silent failures.

## Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | `install.sh` run via `curl -fsSL .../install.sh \| bash` completes without partial execution |
| AC-2 | Detects correct RID for linux-x64, linux-arm64, osx-arm64, osx-x64 |
| AC-3 | Downloads artifact matching the latest GitHub release tag |
| AC-4 | `memctl --version` succeeds after install via `install.sh` |
| AC-5 | `Install.ps1` installs binary + 3 native DLLs, rollback if verify fails |
| AC-6 | `--dir` / `-Dir` overrides install dir on both scripts |
| AC-7 | `release.yml` Package step archives include native libs (not .lib files) |
| AC-8 | `release.yml` syncs `install.sh` + `Install.ps1` to release repo on tag push |
| AC-9 | `install.sh` exits 1 + stderr on network failure or API rate limit |
| AC-10 | `install.sh` exits 1 + stderr on corrupt/empty archive |
| AC-11 | `pack-tool` job removed; `release` job depends only on `build` |
| AC-12 | `install.ps1` (lowercase) deleted from repo |

## Files

- `install.sh` — full rewrite: local-build → online installer
- `Install.ps1` — full rewrite: local-build → online installer
- `install.ps1` — DELETE (duplicate of `Install.ps1`, case confusion)
- `.github/workflows/release.yml` — 3 changes: remove pack-tool, fix Package step, add sync step
- `backlog/wiki/release-runbook.md` — update install instructions + Windows one-liner
