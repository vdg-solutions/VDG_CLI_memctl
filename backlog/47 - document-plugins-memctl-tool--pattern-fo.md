---
id: 47
type: task
title: Document plugins/memctl-{tool}/ pattern for hook-based integrations
status: Done
priority: low
created: 2026-05-08
updated: 2026-05-08
---

## Description

Write a developer guide (`docs/plugin-guide.md`) for creating per-tool hook plugins. Uses `plugins/memctl-claude/` as the canonical reference. Docs-only, no code changes. Linked from #46 (MCP is primary; plugins are fallback).

**Constraints:** plugin must be thin — hook wiring only, no business logic. Any logic belongs in the `memctl` binary itself.

## Files

- `docs/plugin-guide.md` — new file, plugin authoring guide
- `plugins/memctl-claude/README.md` — add link to `docs/plugin-guide.md`

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `docs/plugin-guide.md` exists with structure: Overview, Directory layout, Hook wiring, Version pin, Version drift risk | ls + read file |
| AC-2 | Guide references `plugins/memctl-claude/` as canonical example | grep "memctl-claude" plugin-guide.md |
| AC-3 | Guide specifies minimum memctl version pin format: `"minVersion": "x.y.z"` in plugin manifest | read file |
| AC-4 | Guide explicitly states: no business logic in plugin — hook commands must only call `memctl <cmd>` | read file |
| AC-5 | Guide documents version drift risk and mitigation: keep plugins thin so hook signature changes don't break them | read file |
| AC-6 | `plugins/memctl-claude/README.md` links to `docs/plugin-guide.md` | grep README |

## Out of scope

- Creating any new plugin (e.g. memctl-cursor, memctl-cline)
- Code changes to the binary
- SKILL.md update (no command changes)

## Performance

Docs-only — no runtime impact.

## Comments

**2026-05-08 06:46 user:** Write a guide for creating per-tool hook plugins: thin adapter only (JSON config + optional JS/shell hook, no logic), minimum memctl version pin, pattern follows plugins/memctl-claude/ as reference. ~1 day per tool. Mention version drift risk — keep plugins thin.
