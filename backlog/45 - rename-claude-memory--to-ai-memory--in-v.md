---
id: 45
type: task
title: Rename claude-memory/ to ai-memory/ in vault init
status: Todo
priority: low
created: 2026-05-08
updated: 2026-05-08
---

## Description

Remove Claude-specific naming from vault layout by renaming `claude-memory/` → `ai-memory/` in `ObsidianVaultReader.InitVaultStructure`. v2 breaking change — batch with other vault layout changes.

**Decision:** final name is `ai-memory/` (not `memory/`).

## Files

- `src/memctl/Implementations/Vault/ObsidianVaultReader.cs:65` — directory name in `foreach` array
- `src/memctl/Implementations/Vault/ObsidianVaultReader.cs:77` — `MEMORY.md` write path (`claude-memory/MEMORY.md` → `ai-memory/MEMORY.md`)
- `src/memctl/Implementations/Vault/ObsidianVaultReader.cs:81` — README.md content string referencing `claude-memory/MEMORY.md`
- `plugins/memctl-claude/skills/memctl/SKILL.md` — vault layout table (golden-rule: code change → SKILL.md update)
- Any test asserting `claude-memory/` directory exists after init

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `memctl init` creates `ai-memory/` subdirectory, not `claude-memory/` | `ls .memctl/ai-memory/` after init |
| AC-2 | `ai-memory/MEMORY.md` is written on init with correct default content | file exists with `# Memory index` |
| AC-3 | Existing vault with old `claude-memory/` dir loads without crash — no auto-migration | `memctl status` on old vault exits 0 |
| AC-4 | SKILL.md vault layout section shows `ai-memory/` not `claude-memory/` | read SKILL.md |
| AC-5 | All init tests updated to assert `ai-memory/` directory | `dotnet test` passes |

## Out of scope

- Auto-renaming `claude-memory/` → `ai-memory/` in existing vaults (no data migration)
- Adding `memctl migrate-vault` command

## Performance

No runtime impact — directory name change only, no hot path affected.

## Comments

**2026-05-08 06:46 user:** Cosmetic coupling fix: ObsidianVaultReader.cs:65 creates 'claude-memory/' subdir; rename to 'ai-memory/' (or 'memory/') to remove Claude-specific naming from vault structure. Needs migration note for existing vaults. v2 breaking change — batch with other vault layout changes.
