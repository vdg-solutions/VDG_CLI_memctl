# Requirements Spec: Enable Global CLI Install

**Task:** #5
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Overview

memctl is already configured as a dotnet tool (`PackAsTool=true`) and a nupkg exists. The gap is the absence of automated build scripts for the nupkg, install scripts for end users (both dotnet and non-dotnet workflows), and version synchronization between build artifacts. This task closes that gap so developers can run `memctl` from any shell after a one-line install.

## 2. User Stories

- As a dotnet developer, I want `dotnet tool install -g memctl --add-source ./nupkg` to work reliably after a fresh build, so I don't need to manage PATH manually.
- As a non-dotnet user, I want a one-command install script that copies the portable binary to my PATH, so I can use memctl without installing the .NET SDK.
- As a maintainer, I want a single `build-tool.sh` script that produces the nupkg with the correct version, so releasing a new version is reproducible.

## 3. Functional Requirements

### 3.1 Build Pipeline

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-001 | `build-tool.sh` runs `dotnet pack` and outputs nupkg to `nupkg/` | Must | [unit] | `nupkg/memctl.{VERSION}.nupkg` exists after script runs |
| FR-002 | Script accepts optional `[version]` arg; defaults to git tag or short SHA | Must | [unit] | `bash build-tool.sh 2.0.0` produces `memctl.2.0.0.nupkg` |
| FR-003 | Script updates `<Version>` in csproj before packing | Must | [unit] | nupkg manifest contains the passed version |
| FR-004 | Old nupkg files are cleaned before packing to avoid stale versions | Should | [unit] | Only one `memctl.*.nupkg` in `nupkg/` after build |

### 3.2 Install — Dotnet Tool Path

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-005 | `dotnet tool install -g memctl --add-source ./nupkg` installs successfully | Must | [unit] | Exit code 0; `memctl --help` works from new shell |
| FR-006 | ONNX Runtime native libs are bundled correctly so embedded model loading works after install | Must | [unit] | `memctl model list` returns valid JSON after global install |
| FR-007 | `dotnet tool update -g memctl --add-source ./nupkg` upgrades without error | Should | [unit] | Exit code 0 after running update |

### 3.3 Install — PATH-Based (Non-Dotnet Users)

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-008 | `install.sh` copies platform binary to `~/.local/bin/memctl` and makes it executable | Must | [unit] | `which memctl` returns `~/.local/bin/memctl` after sourcing shell |
| FR-009 | `install.ps1` copies `memctl.exe` to `$HOME\.local\bin\` and adds it to user PATH if not present | Must | [unit] | `memctl --help` works in new PowerShell after script runs |
| FR-010 | Install scripts detect existing installation and prompt before overwriting | Should | [unit] | Script outputs warning if binary already exists at target |
| FR-011 | Install scripts validate that native runtime libs (onnxruntime.dll/.so) are present next to the binary | Must | [unit] | Script aborts with clear error if libs are missing |

### 3.4 Uninstall

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-012 | README documents `dotnet tool uninstall -g memctl` for dotnet path | Should | [unit] | Command documented in README install section |
| FR-013 | Install scripts document how to reverse the PATH install | Could | [unit] | Comment or echo in script shows removal command |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|------------|-------------------|
| NFR-001 | Compatibility | Dotnet tool package works on .NET 10 runtime | `dotnet tool install` succeeds on .NET 10 |
| NFR-002 | Native libs | ONNX Runtime platform libs must be resolvable at runtime after global install | `memctl status` doesn't throw DllNotFoundException |
| NFR-003 | Idempotency | Running install scripts multiple times produces no errors | Second run of install.sh/install.ps1 exits cleanly |
| NFR-004 | Script style | Scripts follow same style as `build-portable.sh` (set -euo pipefail, comments) | Code review pass |

## 5. Edge Cases & Error Scenarios

1. **ONNX native lib resolution after dotnet tool install**: Dotnet tools are framework-dependent by default; native libs from `Microsoft.ML.OnnxRuntime` may not be in the right location. Expected: test `memctl model list` post-install; if it fails, the nupkg must use `--tool-manifest` or RID-specific packaging.
2. **Version already installed**: `dotnet tool install` fails if tool is already installed. Expected: script or README guides user to use `dotnet tool update`.
3. **No .NET SDK on target machine**: PATH install via `install.sh` should work from portable zip without requiring .NET SDK. Expected: install.sh sources from `dist/` portable binary.
4. **PATH not writable**: `~/.local/bin` doesn't exist or isn't in PATH. Expected: install.sh creates directory and prints instructions to add to shell profile.
5. **Windows without PowerShell execution policy**: `install.ps1` may be blocked. Expected: script header includes `Set-ExecutionPolicy` instructions.

## 6. Out of Scope

- Publishing to NuGet.org (public registry) — local nupkg source is sufficient for now
- macOS `.pkg` or Windows `.msi` installer UI
- Auto-update mechanism

## 7. Dependencies

- `build-portable.sh` exists and produces `dist/win-x64/memctl.exe` etc. — install scripts source from there
- .NET 10 SDK available in build environment for nupkg generation
- `nupkg/` directory must be in `.gitignore` or only tracked intentionally

## 8. Open Questions

- [ ] Should `build-tool.sh` also call `build-portable.sh` or remain separate? (Currently separate workflows)
- [ ] Should the dotnet tool nupkg be RID-specific (win-x64 only) or framework-dependent with all native libs embedded?

## 9. QC Checklist

- [ ] FR-001: `nupkg/memctl.{VERSION}.nupkg` exists after `bash build-tool.sh`
- [ ] FR-003: nupkg manifest version matches input version
- [ ] FR-005: `dotnet tool install -g memctl --add-source ./nupkg` exits 0
- [ ] FR-006: `memctl model list` returns valid JSON after global install (ONNX libs resolve)
- [ ] FR-008: `install.sh` produces executable at `~/.local/bin/memctl`
- [ ] FR-009: `install.ps1` adds memctl.exe to user PATH
- [ ] FR-011: Install scripts abort with clear error if native libs are missing
- [ ] NFR-003: Second run of install scripts exits cleanly
