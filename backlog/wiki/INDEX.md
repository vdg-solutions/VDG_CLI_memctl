# Backlog Wiki — Index

Reference docs for working with the memctl backlog + memory system. Future bots: read these before designing new features or running /sdlc.

These are **reference docs**, NOT `/sdlc` intake. `/sdlc` only picks up `backlog/wiki/*.md` files with `status: pending` frontmatter — files in this index have no status field, so they're skipped by the intake scanner.

---

## Memory & Vault

| Doc | Topic |
|-----|-------|
| [memory-pipeline.md](memory-pipeline.md) | How vault data flows to/from Claude Code (read/write paths via hooks) |
| [vault-layout.md](vault-layout.md) | V2.1 directory structure + writer ownership matrix |

## Operations

| Doc | Topic |
|-----|-------|
| [release-runbook.md](release-runbook.md) | End-to-end release SOP — version bump, tag push, workflow, PAT rotation |
| [plugin-publish.md](plugin-publish.md) | Two-repo plugin publish flow + marketplace.json source object format |
| [backlog-conventions.md](backlog-conventions.md) | Status semantics, when to edit Done items, archive vs fix decision tree |

## Process

| Doc | Topic |
|-----|-------|
| [../wishlist.md](../wishlist.md) | Cosmetic deferrals + future ideas — promote to backlog when demand surfaces |
| [../TEMPLATE.md](../TEMPLATE.md) | Backlog item structure (hard rules for /sdlc readiness) |

---

## What lives where

| Location | Content |
|----------|---------|
| `backlog/wiki/` (this dir) | Reference docs about backlog + memory + ops |
| `backlog/<N> - <slug>.md` | Active SDLC task files — picked up by `/sdlc` |
| `backlog/wishlist.md` | Cosmetic deferrals not yet promoted to backlog |
| `backlog/TEMPLATE.md` | Canonical task template |
| `docs/specs/<id>-spec.md` | Active task spec (during /sdlc pipeline) |
| `docs/designs/<id>-design.md` | Active task design (during /sdlc pipeline) |
| `docs/retros/<id>-retro.md` | Active task retro (during /sdlc Phase 6) |
| `docs/archives/{specs,designs,retros}/` | **Frozen** historical SDLC artifacts (post-Done) — see [archive README](../../docs/archives/README.md) |
| `docs/memctl.md` | Skill source of truth (synced to plugin via `scripts/sync-skill-to-plugin.sh`) |
| `docs/refs/articles/` | External research references |

`docs/specs|designs|retros/` are **frozen historical artifacts** — they document what was true at task ship time. They may contain stale paths/refs; don't bulk-update unless task is reopened. Per [backlog-conventions.md](backlog-conventions.md), Done items can be edited for wrong snippets that would mislead future bots, but per-task SDLC artifacts are append-only history.

---

## How `/sdlc` reads this dir

`/sdlc` Step 0 wiki scan logic:
- Looks at `backlog/wiki/*.md` for files with `status: pending` frontmatter
- Skips files matching `*-spec.md` or `*-design.md` (pipeline artifacts pattern)
- Files without `status:` field (like everything in this index) → ignored

Net effect: this dir is reference-only. `/sdlc` doesn't try to convert these into tasks.

---

## Contributing to this wiki

When you write a new reference doc:
1. Place in `backlog/wiki/<slug>.md`
2. NO `status:` frontmatter (otherwise /sdlc may try to process it)
3. Add entry to this INDEX.md
4. Update cross-references in other wiki files if they should link to it
5. Commit `docs(wiki): <topic>` directly to main — no /sdlc needed for doc work

When a reference doc becomes stale:
1. Mark as deprecated at top: `> **DEPRECATED** — superseded by [other-doc.md](other-doc.md). Will be deleted on next cleanup.`
2. Or delete directly if confident no external refs remain (`grep -rn "doc-slug" backlog/ docs/ README*` to verify)
