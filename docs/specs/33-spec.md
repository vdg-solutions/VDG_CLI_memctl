# Requirements Spec: V2 docs ship — skill rewrite + plugin README + version 1.3.0 + sync

**Task:** 33
**Date:** 2026-05-01
**Status:** Approved (extracted from backlog/33)

## 1. Overview

Final child of epic #30. Rewrite skill (docs/memctl.md) + plugin README cho V2.1 layout, bump csproj + plugin.json `1.2.0 → 1.3.0` lockstep, sync public memctl-releases (top-level SKILL.md + plugin source). Verify workflow ordering ensures plugin source push completes BEFORE marketplace bump. **V2.1 is hard cutover** — no migrate-vault (drop migration references; manual upgrade flow only).

## 2. User Stories

- As an end user reading the skill, I want V2.1 layout diagram + manual upgrade instructions (no migrate-vault command exists).
- As a maintainer pushing tag v1.3.0, I want `verify-versions` workflow gate to enforce csproj == plugin.json == tag.
- As a Claude Code user running `claude plugin update memctl@vdg-solutions`, I want the public memctl-releases plugin source already at V2.1 by the time marketplace.json bumps to 1.3.0.

## 3. Functional Requirements

| ID | Requirement | Priority | Test | Acceptance |
|----|------------|----------|------|------------|
| FR-1 | docs/memctl.md contains V2.1 layout diagram + manual upgrade instructions | Must | [unit] | `grep -c "Vault layout (V2" docs/memctl.md` ≥ 1; `grep -c "\.archived-v1-vault" docs/memctl.md` ≥ 1 |
| FR-2 | docs/memctl.md drops legacy `~/my-vault` + `.memctl-vault/` patterns | Must | [unit] | `grep -cE "memctl init ~|\.memctl-vault" docs/memctl.md` returns 0 |
| FR-3 | plugins/memctl-claude/README.md uses V2.1 init examples | Must | [unit] | `grep -c "memctl init --vault \." plugins/memctl-claude/README.md` ≥ 1; `grep -c "\.memctl-vault" plugins/memctl-claude/README.md` returns 0 |
| FR-4 | Skill synced — diff docs/memctl.md and plugin SKILL.md returns empty | Must | [unit] | `diff -q docs/memctl.md plugins/memctl-claude/skills/memctl/SKILL.md` exit 0 |
| FR-5 | csproj `<Version>` === plugin.json `version` === 1.3.0 | Must | [unit] | both grep checks return 1.3.0 |
| FR-6 | Public memctl-releases SKILL.md (top-level) reflects V2.1 examples | Should | [unit] | `curl -sS https://raw.githubusercontent.com/vdg-solutions/memctl-releases/master/SKILL.md \| grep -c "\.memctl/"` ≥ 3 |
| FR-7 | Public memctl-releases plugin README synced | Should | [unit] | `curl -sS https://raw.githubusercontent.com/vdg-solutions/memctl-releases/master/plugins/memctl-claude/README.md \| grep -c "memctl init --vault \."` ≥ 1 |
| FR-8 | Workflow ordering: sync-marketplace `needs: release` AND release job contains "Sync plugin source" step | Must | [unit] | mechanical grep checks |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance |
|----|----------|------------|-----------|
| NFR-1 | Build | 0 warning, 0 error | `dotnet build -c Release` clean |
| NFR-2 | Regression | All 49 tests still pass | `dotnet test --nologo` "Passed: 49" |
| NFR-3 | No new tests added (docs+version only) | `git diff main..HEAD --name-only \| grep -c "tests/.*Tests.cs"` returns 0 |
| NFR-4 | Version bump atomic — both files in same commit | inspect commit history |

## 5. Edge Cases

- Plugin marketplace.json on remote may have version=1.2.0 still — workflow #28 sync-marketplace job will bump on next tag push, no manual marketplace.json edit needed.
- Public memctl-releases plugin source may have stale V1 README — manual API sync required until next tag triggers workflow auto-sync.

## 6. Out of Scope

- Pushing tag `v1.3.0` itself (anh does manually after merge).
- Updating CHANGELOG.md (deferred — wishlist).
- Marketplace.json version bump (workflow auto-handles on tag push).

## 7. Dependencies

- Blocked by #31 (Done) — V2 foundation
- Blocked by #28 (Done) — verify-versions workflow gate
- ~~Blocked by #32~~ — Archived

## 8. Open Questions

(none)

## 9. QC Checklist

- [ ] FR-1..8 each via mechanical grep
- [ ] NFR-1 build clean
- [ ] NFR-2 49/49 tests pass
- [ ] NFR-3 no new tests
- [ ] NFR-4 version atomic
