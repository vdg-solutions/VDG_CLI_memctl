# Requirements Spec: Vault Auto-Detect and Skill Rename

**Task:** 8
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Overview

Add vault auto-detection so `--vault` becomes optional for all commands that read from an existing vault. The tool walks up from the current working directory looking for a `.obsidian/` marker directory, the same convention as git finds `.git/`. The `init` command retains required `--vault` (target must be explicit when creating). Additionally rename `docs/SKILL.md` → `docs/memctl.md` to match Claude Code skill naming convention.

## 2. User Stories

- As a developer, I want `memctl mcp` in my MCP config without a `--vault` path so that each project's vault is found automatically when Claude Code spawns the server from the project root.
- As a user, I want `memctl search "query"` to work without `--vault` when I'm inside a project with a vault so that I don't need to remember the path.
- As a user, I want a clear error when no vault is found so that I know how to fix it.
- As a Claude Code user, I want `docs/memctl.md` as the skill file so that it follows the standard naming convention.

## 3. Functional Requirements

### 3.1 Vault Auto-Detection

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|---------------------|
| FR-001 | Add `VaultLocator.FindVault(string? explicitPath)` static method | Must | [unit] | Method walks up from cwd when explicitPath is null/empty; returns first directory containing `.obsidian/`; returns null if none found |
| FR-002 | Walk starts at `Directory.GetCurrentDirectory()` | Must | [unit] | Starting directory is cwd, not binary location or home dir |
| FR-003 | Walk stops at filesystem root | Must | [unit] | Loop terminates when `Directory.GetParent()` returns null |
| FR-004 | Explicit `--vault` overrides auto-detect | Must | [e2e] | When `--vault /path` provided, VaultLocator returns that path without walking |
| FR-005 | `RequireVault` uses VaultLocator for all commands except `init` | Must | [e2e] | `memctl search "x"` without `--vault` auto-detects vault from cwd |
| FR-006 | `init` command retains required `--vault` | Must | [e2e] | `memctl init` without `--vault` still prints error: "--vault is required for this command" |
| FR-007 | Clear error when no vault found | Must | [e2e] | Error message: "No vault found. Create one with 'memctl init --vault <path>' or run from a directory containing a vault." |
| FR-008 | `mcp` command uses auto-detect | Must | [e2e] | `memctl mcp` without `--vault`, run from a vault dir, starts MCP server successfully |
| FR-009 | Auto-detect works for: search, get, list, search-*, grep, tags, stats, status, add, ingest, organize, weight, identity, add-turn | Must | [unit] | All listed commands pass auto-detected vault to their operators |

### 3.2 Skill File Rename

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|---------------------|
| FR-010 | Rename `docs/SKILL.md` → `docs/memctl.md` | Must | [unit] | File `docs/memctl.md` exists; `docs/SKILL.md` does not exist |
| FR-011 | `docs/memctl.md` frontmatter preserved | Must | [unit] | `name: memctl`, `allowed-tools: Bash` present in new file |
| FR-012 | `docs/memctl.md` updated for auto-detect | Must | [unit] | `--vault` marked as optional in Global Options table; MCP config example shows no `--vault` arg |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|------------|---------------------|
| NFR-001 | Performance | Walk must be fast | No I/O beyond Directory.Exists per level; terminates in <5ms for typical project depth |
| NFR-002 | Security | Vault path must be validated | VaultLocator returns absolute canonical path via Path.GetFullPath |
| NFR-003 | Backwards compat | Explicit --vault still works everywhere | All existing usage with --vault continues to work unchanged |
| NFR-004 | Error clarity | Error messages actionable | Error includes exact command to fix the situation |

## 5. Edge Cases & Error Scenarios

1. **No `.obsidian/` anywhere in tree**: VaultLocator returns null → `RequireVault` prints "No vault found..." error, exit 1.
2. **`.obsidian/` exists in current dir**: Walk finds it immediately (first iteration).
3. **`.obsidian/` in parent but not child**: Walk succeeds — returns parent path.
4. **User at filesystem root with no vault**: Loop terminates correctly without infinite loop.
5. **`--vault` points to nonexistent path**: Existing behavior preserved (operators fail with their own errors).
6. **`status` command with no vault**: Currently passes empty string to StatusOperator — auto-detect should apply here too (but status is best-effort, not fatal if no vault).
7. **`model download` / `model list` / `model use`**: These don't use vault at all — no change needed.

## 6. Out of Scope

- Creating `.obsidian/` automatically (user must init first)
- Config file for default vault path
- Multiple vault detection / selection
- Vault discovery via environment variable
- Any changes to VaultWriteOperator or McpServerOperator internals

## 7. Dependencies

- `System.IO` — `Directory.GetCurrentDirectory()`, `Directory.GetParent()`, `Directory.Exists()`
- No new NuGet packages

## 8. Open Questions

- (none — all resolved by task description and context)

## 9. QC Checklist

- [ ] FR-001: VaultLocator.FindVault walks up from cwd and returns first dir with .obsidian/
- [ ] FR-004: Explicit --vault overrides auto-detect
- [ ] FR-005: search/get/list and all reading commands auto-detect vault
- [ ] FR-006: init still requires explicit --vault
- [ ] FR-007: No-vault error message matches spec exactly
- [ ] FR-008: memctl mcp without --vault works from vault dir
- [ ] FR-010: docs/SKILL.md renamed to docs/memctl.md
- [ ] FR-012: docs/memctl.md shows --vault as optional, MCP example without --vault arg
- [ ] NFR-003: Existing --vault usage unaffected
