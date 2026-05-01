---
id: 30
type: epic
title: 'Vault layout V2 — .memctl/ as vault root container'
status: Todo
priority: high
children: [31, 32, 33]
tags:
  - epic
  - vault
  - layout
  - breaking-change
  - migration
  - isolation
  - skill
  - plugin
created: 2026-05-01
updated: 2026-05-01
---

## Description

Convert vault layout from V1 (`<project>/.obsidian/` + `<project>/.memctl/` siblings, project root pollution) → V2 (`<project>/.memctl/` as vault root container chứa `.obsidian/` + index.db + chats/ + notes). Per-project install becomes natural — project source files outside `.memctl/` never indexed.

Epic split into 3 deliverable tasks. Ship in order — each builds on previous.

```
V1 (current):                                V2 (target):
<project>/                                   <project>/
├── .obsidian/                               ├── .memctl/                ← vault root
├── .memctl/                                 │   ├── .obsidian/
│   ├── index.db                             │   ├── index.db            ← flat
│   └── models/                              │   ├── models/
├── chats/                                   │   ├── chats/
├── claude-memory/                           │   ├── claude-memory/
├── src/        ← INDEXED                    │   └── *.md                ← memory notes
└── README.md   ← INDEXED                    ├── src/                    ← NOT indexed
                                             └── README.md               ← NOT indexed
```

Epic ships v1.3.0 (vault contract change). MEMCTL_SHARED_VAULT (#29) parked — V2 makes per-project natural; revisit env var fallback after epic completes.

## Children

| # | Title | Scope | Effort |
|---|-------|-------|--------|
| #31 | VaultLocator V2 + Init refactor + path updates + legacy warning | Foundation: resolver, init, path computations, StatusOperator legacy warning (no migration yet — legacy vaults still resolve as `legacy v1`) | 4-5h |
| #32 | Migration command — atomic rename + dry-run + idempotent + same-FS guard | Build on #31: `memctl migrate-vault` CLI, V1→V2 file moves with atomic safety, MigrationTests | 3-4h |
| #33 | Skill rewrite + plugin README + isolation runbook + version bump 1.3.0 + sync | docs/memctl.md V2 examples, plugin README, runbook, sync-skill-to-plugin.sh, csproj+plugin version 1.3.0, API sync top-level SKILL.md + plugin source on public memctl-releases, verify workflow sync ordering | 3h |

## Epic-Level Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| EPIC-1 | All 3 children `status: Done` | `for id in 31 32 33; do bl show $id \| grep -q "^status: Done" \|\| exit 1; done` exit 0 |
| EPIC-2 | End-to-end smoke: V2 init in temp project + ingest + list excludes project source | smoke script in #31 §9 + #32 §9 + #33 §9, all pass |
| EPIC-3 | csproj + plugin.json version === 1.3.0 lockstep | workflow #28 verify-versions enforces |
| EPIC-4 | Plugin marketplace.json reflects 1.3.0 after next tag | `gh api repos/vdg-solutions/claude-plugins/contents/.claude-plugin/marketplace.json --jq '.content' \| base64 -d \| jq '.plugins[0].version'` returns `"1.3.0"` |
| EPIC-5 | Public memctl-releases skill + plugin source sync to V2 examples | `curl https://raw.githubusercontent.com/vdg-solutions/memctl-releases/master/SKILL.md \| grep -c "\.memctl/"` ≥3 |
| EPIC-6 | Migration safe on dev vault (61 notes preserved through V1→V2) | dry-run + full migrate on `E:\repos\CLIs\VDG_CLI_memctl\.memctl/` post-epic ship; `memctl list` returns 61 notes after migrate |

## Out of Scope

- Auto-detect cross-vault scan for migration. User runs migrate per vault.
- Vault format version field in index.db. Future task.
- Auto-migration on first `memctl status` after upgrade. User opts in via explicit `migrate-vault` command.

## Dependencies

- **Blocked by #28 (Done)** — workflow verify-versions enforces lockstep.
- **#29 parked** — em set status `Todo` + comment "Defer until #30 epic ships". Revisit env var fallback after V2 lands.

## Risk

| Risk | Mitigation |
|------|-----------|
| Children ship out of order, mid-state breaks dev box | #31 ships first w/o migration → existing legacy vault still resolves as `legacy v1` (warning surfaced, but reads work). #32 adds migration. #33 ships docs + version. Each child ships independently green. |
| Dev vault data loss during testing #32 migration | Mandatory dry-run + backup in #32 user actions. Em backup `E:\repos\CLIs\VDG_CLI_memctl\.memctl/` to `.memctl.backup-pre-v2/` BEFORE running real migration. |
| User on v1.2.x auto-updates plugin → silent memory loss | #31 adds loud `::warning::` SessionStart annotation when legacy v1 detected. User sees warning at session start, not silent. |
| Workflow `sync-marketplace` runs before plugin source synced (between-job race) | #33 verifies job ordering: release job's "Sync plugin source to release repo" step completes BEFORE sync-marketplace job starts (sync-marketplace `needs: release`). Verify in workflow YAML. |

## Effort

~10-12h total across 3 children:
- #31: 4-5h
- #32: 3-4h
- #33: 3h

## User Actions Required

- (in #32) [USER-ACTION-REQUIRED] Backup dev vault `cp -r .memctl/ .memctl.backup-pre-v2/` before em runs full migration on it.
- (in #33) [USER-ACTION-REQUIRED] After v1.3.0 ship, anh chạy `memctl migrate-vault --vault <each-existing-vault>` cho mỗi vault legacy.

## Notes

- Sequential ship recommended: #31 → #32 → #33. Each child gates next via `Blocked by` dep.
- Epic Done when all children Done — `/sdlc` Sprint Epic Check (Step 0) auto-closes if sprint has `epic: 30` field.
- Detailed implementation steps + ACs + tests live in each child backlog.
- Em delete this body's V1/V2 diagram + child summary if it duplicates with child specs — keep epic body short, children carry detail.
