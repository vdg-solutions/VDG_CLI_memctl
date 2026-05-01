# Requirements Spec: Vault MEMCTL_SHARED_VAULT env var — shared-mode opt-in

**Task:** 29
**Date:** 2026-05-01
**Status:** Draft

---

## 1. Overview

Wire `MEMCTL_SHARED_VAULT` environment variable into `VaultLocator.Discover()` as lowest-priority fallback after the V2.1 walk-up loop. Sync existing docs (`backlog/wiki/memory-pipeline.md`, `plugins/memctl-claude/README.md`) that already reference the env var as if implemented. Bump csproj + plugin version 1.3.0 → 1.3.1 lockstep.

## 2. User Stories

- As a user with multiple sensitive projects, I want per-project `.memctl/` to ALWAYS win over a globally-set env var so leaks across projects are impossible.
- As a user with a personal scratchpad vault outside any repo, I want to set `MEMCTL_SHARED_VAULT=/path/to/personal-vault` and have memctl use it when cwd has no project vault.
- As a developer reading the docs, I want the documented behavior to match actual code so my expectations don't break.

## 3. Functional Requirements

| ID | Requirement | Priority | Test | Acceptance Criteria |
|----|-------------|----------|------|---------------------|
| FR-1 | `MEMCTL_SHARED_VAULT` pointing at V2.1 vault (`<path>/.obsidian/` exists) resolves to that path when walk-up fails | Must | [unit] | `dotnet test --filter EnvVar_used_when_no_walk_up_match` exit 0 |
| FR-2 | V2.1 walk-up (finds `.memctl/.obsidian/`) priorities over env var | Must | [unit] | `dotnet test --filter Walk_up_wins_over_env_var` exit 0 |
| FR-3 | `--vault` explicit flag priorities over both env var and walk-up | Must | [unit] | `dotnet test --filter Explicit_flag_wins_over_env_var` exit 0 |
| FR-4 | Env var pointing at non-existent dir falls through to null (no throw) | Must | [unit] | `dotnet test --filter Env_var_pointing_at_invalid_dir_falls_through_to_null` exit 0 |
| FR-5 | `Discover()` Strategy field reports `"MEMCTL_SHARED_VAULT env (shared)"` when env var hit | Must | [unit] | `Strategy = "MEMCTL_SHARED_VAULT env (shared)"` in test assertion + `memctl status --json` `data.discovery.strategy` |
| FR-6 | Tests can override env reading via injected `EnvReader` hook | Must | [unit] | `internal static Func<string, string?> EnvReader` exists; tests assign and restore in Dispose |
| FR-7 | Docs (`memory-pipeline.md`, plugin README) updated to match actual behavior + version note | Must | [unit] | grep "v1.3.1+" in both files |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|-------------|---------------------|
| NFR-1 | Build | 0 warning, 0 error | `dotnet build src/memctl/memctl.csproj -c Release --nologo -v q` clean |
| NFR-2 | Regression | Existing test suite green | `dotnet test --nologo` exit 0 |
| NFR-3 | Version lockstep | csproj version === plugin.json version === target tag (workflow #28 enforces) | Both files contain `1.3.1` |
| NFR-4 | Cross-platform | `Environment.GetEnvironmentVariable` works Win/Linux/macOS | Default `EnvReader` uses framework API |
| NFR-5 | Security | Per-project vault always wins over env var | FR-2 covers |

## 5. Edge Cases & Error Scenarios

1. **Env var unset** → resolver returns `null` Vault (current behavior unchanged).
2. **Env var set, points at empty string** → treated as unset (NullOrWhiteSpace check).
3. **Env var set, dir exists but missing `.obsidian/`** → does NOT resolve, falls through to null. Prevents accidental random-dir vault.
4. **Env var set, walk-up also matches** → walk-up wins (FR-2).
5. **Explicit `--vault` + env var both set** → explicit wins (FR-3).
6. **Env var changed mid-process** → next `Discover()` call re-reads (no caching).
7. **Symlinked path in env var** → `Path.GetFullPath` normalizes; symlinks NOT resolved (out of scope).

## 6. Out of Scope

- Multi-path env var (`MEMCTL_SHARED_VAULT=p1:p2`).
- Auto-`memctl init` if env var points at empty dir.
- Symlink canonicalization beyond `Path.GetFullPath`.
- Removing env var support if walk-up succeeds (already short-circuits).

## 7. Dependencies

- Task #28 Done (workflow `verify-versions` enforces lockstep)
- Task #31 Done (V2.1 VaultLocator already in place)

## 8. Open Questions

- (none — task body resolved all)

## 9. QC Checklist (auto)

- [ ] FR-1: env var hit returns vault when walk-up empty
- [ ] FR-2: walk-up wins over env var
- [ ] FR-3: explicit wins over both
- [ ] FR-4: bad env var path returns null, no throw
- [ ] FR-5: strategy string correct
- [ ] FR-6: EnvReader hook exists and is `internal static`
- [ ] FR-7: docs reference v1.3.1+
- [ ] NFR-1: build clean
- [ ] NFR-2: full test suite green
- [ ] NFR-3: version lockstep 1.3.1
