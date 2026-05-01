# Backlog Wishlist — deferred / cosmetic ideas

Items here are not yet promoted to full backlog tasks. They live here until pulled in (or abandoned). Bot reading backlog: skip this file for `/sdlc` picks. Use as inspiration when scoping new work.

Format: short paragraph + trigger condition. No frontmatter, no full backlog template.

---

## Vault organization (post-#30 epic)

### chats/ archival by year
- **Trigger:** chats/ accumulates > 365 files (one year of daily rollups), OR start of new calendar year
- **Action:** introduce `memctl archive-chats --year YYYY` command — moves `chats/YYYY-MM-DD.md` → `chats/YYYY/` subdir
- **Update:** Obsidian daily-notes plugin config — point at `chats/` (current year) but make older accessible via folder browse
- **Rationale:** flat `chats/` works fine for first year; sharding by year prevents file explorer fatigue afterward
- **Effort:** ~2-3h (operator + test + Obsidian config update + Daily-notes plugin retains current-year semantics)

### attachments/ sharding by month
- **Trigger:** attachments/ accumulates > ~100 files (heuristic — empirical when navigation slows)
- **Action:** introduce `memctl archive-attachments` or auto-shard at write time — files routed to `attachments/YYYY-MM/<filename>`
- **Rationale:** flat `attachments/` with `YYYY-MM-DD-` filename prefix sorts correctly alphabetically; sharding only needed at scale
- **Effort:** ~1-2h (auto-shard at capture write time + test)

### Obsidian theme + plugin config opinionated defaults
- **Trigger:** user feedback on default Obsidian UX being bare
- **Action:** init writes opinionated `.obsidian/themes/` + community-plugins.json prefilling `dataview`, `calendar`, `templater`, `omnisearch`
- **Risk:** opinionated defaults divisive — wait for explicit user demand
- **Effort:** ~1h

### tasks/ sharding by epic
- **Trigger:** tasks/ has > 100 files
- **Action:** group `task-{id}-{phase}.md` by parent epic — `tasks/epic-{N}/task-{id}-{phase}.md`
- **Rationale:** epic-grouped navigation matches /sdlc Sprint Epic Check semantics
- **Effort:** ~1h

---

## Memory consolidation

### patterns/ → lessons/ promotion CLI
- **Trigger:** /qc-dream auto-promotion not granular enough; user wants explicit "promote this pattern now" override
- **Action:** `memctl promote-pattern <pattern-slug>` command
- **Rationale:** automation handles 80%; manual override for edge cases
- **Effort:** ~1h

### Cross-vault lesson sync
- **Trigger:** user has multiple vaults (per-project) and wants `lessons/` to flow across
- **Action:** `memctl sync-lessons --from <other-vault>` reads other vault's lessons/, dedupes against current
- **Rationale:** lessons are universal wisdom — should NOT be locked per-project
- **Effort:** ~3h (dedupe logic + tests)
- **Note:** requires careful design — could conflict with vault isolation goal of #30 epic

---

## CLI ergonomics

### memctl backup-vault
- **Trigger:** users running production migrations want one-command backup
- **Action:** `memctl backup-vault [--dest <path>]` creates timestamped tarball of vault (excluding `.obsidian/memctl/` runtime)
- **Effort:** ~1h

### memctl import-from <other-tool>
- **Trigger:** users coming from Obsidian-only or Notion or markdown notes folder
- **Action:** import existing markdown collection as memctl notes — frontmatter normalize, weight default 1.0
- **Effort:** ~3-4h (depending on supported tools)

---

## How to graduate from wishlist

When demand surfaces (user asks, hit_count rises, blocking issue), promote a wishlist item to a full backlog task:

1. Copy paragraph to new `backlog/{N+1} - <slug>.md`
2. Apply TEMPLATE.md structure (Description, Implementation Steps, ACs, Risk, Effort, etc.)
3. Run `bash scripts/lint-backlog.sh` until SDLC-ready
4. Remove wishlist entry (or mark as promoted with task id reference)
