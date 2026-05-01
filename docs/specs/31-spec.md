# Requirements Spec: V2 foundation — VaultLocator + Init refactor + path updates + loud legacy warning

**Task:** 31
**Date:** 2026-05-01
**Status:** Approved (extracted from backlog/31)

## 1. Overview

Foundation child of epic #30. Refactor `VaultLocator.Discover()` to walk-up tìm `.memctl/` chứa `.obsidian/` làm vault root container. Refactor `InitVaultStructure` cho V2.1 layout với runtime nested in `.obsidian/memctl/` + 7 semantic top-level subdirs (tasks/, patterns/, lessons/, decisions/, chats/, attachments/, claude-memory/). Update path computations across 6 operators (runtime → `.obsidian/memctl/`). Detect legacy V1 + emit loud `::warning::` annotation. NO migration command (lands in #32).

## 2. User Stories

- As a maintainer, I want `memctl init --vault <project>` to create `<project>/.memctl/` with everything memctl manages inside, so project source files outside never get indexed.
- As a maintainer, I want walk-up resolver to find V2 vault from any subdir (`<project>/src/...`), so hooks resolve vault correctly without `--vault` flag.
- As a maintainer with a legacy V1 vault, I want a loud warning when memctl detects sibling `.obsidian/` + `.memctl/` layout, so I know to run migrate-vault (when #32 ships).

## 3. Functional Requirements

| ID | Requirement | Priority | Test | Acceptance |
|----|------------|----------|------|------------|
| FR-1 | `<root>/.memctl/.obsidian/` resolved as V2 vault, returned path = `<root>/.memctl/`, strategy `walk-up v2 (.memctl/)` | Must | [unit] | `dotnet test --filter VaultLocatorV2Tests.WalkUp_finds_v2_when_memctl_contains_obsidian` exit 0 |
| FR-2 | Walk-up V2 from project subdir resolves to `.memctl/` ancestor | Must | [unit] | `dotnet test --filter VaultLocatorV2Tests.WalkUp_finds_v2_from_subdir` exit 0 |
| FR-4 | `InitVaultStructure(<parent>)` creates V2.1 layout: `.obsidian/memctl/` runtime + 7 semantic dirs | Must | [unit] | smoke `test -d` per dir all pass |
| FR-5 | `InitVaultStructure(<path>/.memctl)` direct path skips nesting (no `.memctl/.memctl/`) | Must | [unit] | `dotnet test --filter InitV2Tests.Init_with_direct_memctl_path_skips_nesting` exit 0 |
| FR-6 | Index.db at `<vault>/.obsidian/memctl/index.db`, hook.log at `<vault>/.obsidian/memctl/hook.log`, models at `<vault>/.obsidian/memctl/models/` | Must | [unit] | smoke: `test -f $TMP/p/.memctl/.obsidian/memctl/index.db` exit 0 post-ingest |
| FR-7 | EnumerateMarkdownFiles excludes `.obsidian/` only (runtime nested inside, auto-excluded) | Must | [unit] | smoke: `<vault>/foo.md` indexed, `<vault>/.obsidian/memctl/x.md` not indexed |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance |
|----|----------|------------|-----------|
| NFR-1 | Build | 0 warning, 0 error | `dotnet build -c Release` clean |
| NFR-2 | Regression | All 42 existing tests pass + 7 new = 49 total | `dotnet test --nologo` "Passed: 49" |
| NFR-3 | No migration | `migrate-vault` command NOT registered (V1 hard-cutover, no back-compat per anh's directive) | `grep -c "migrate-vault" src/memctl/Bootstrap/Program.cs` returns 0 |

## 5. Edge Cases

- Walk-up reaches filesystem root without match → return null
- `.memctl/` exists but no `.obsidian/` inside → not V2 (continue walk-up)
- `.obsidian/` exists but no `.memctl/` (Obsidian-only folder) → not vault (continue walk-up)
- Both `.memctl/.obsidian/` AND legacy sibling layout in same dir → V2 wins (V2 detection runs first)

## 6. Out of Scope

- Migration command (#32)
- Skill rewrite + version bump (#33)
- MEMCTL_SHARED_VAULT env var (#29 deferred)

## 7. Dependencies

- Blocked by #28 (Done) — version lockstep workflow exists
- Touches: VaultLocator.cs, ObsidianVaultReader.cs, IngestOperator.cs, HookLog.cs, MemctlConfig.cs, GemmaEmbeddingEngine.cs, StatusOperator.cs, GrepOperator.cs, Bootstrap/Program.cs

## 8. Open Questions

(none)

## 9. QC Checklist

- [ ] FR-1..9 each via dedicated test + smoke
- [ ] NFR-1 build clean
- [ ] NFR-2 51/51 tests pass
- [ ] NFR-3 no migrate-vault command yet
- [ ] StatusOperator legacy hint phrased "available in v1.3.0+" (not present-tense)
