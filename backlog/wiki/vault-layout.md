# Vault Layout (V2.1, as of v1.3.0)

Authoritative reference for `<project>/.memctl/` directory structure. Single source of truth — runbooks reference this.

---

## Directory tree

```
<project>/.memctl/                          ← vault root (Obsidian opens here)
│
├── .obsidian/                              ← Obsidian config (auto-hidden by Obsidian app)
│   ├── app.json
│   ├── appearance.json
│   ├── community-plugins.json              (default: dataview, calendar)
│   ├── core-plugins.json                   (daily-notes, templates, backlink, outline, word-count)
│   ├── daily-notes.json                    (folder=chats, format=YYYY-MM-DD)
│   ├── workspace.json                      (Obsidian session UI state — tabs, last-open)
│   │
│   └── memctl/                             ← memctl runtime (nested inside .obsidian/, hidden)
│       ├── index.db                        ← SQLite — embeddings + BM25 + metadata
│       ├── models/embeddinggemma-300m/     ← embedding model files (~295 MB)
│       └── hook.log                        ← Stop/UserPromptSubmit/SessionStart diagnostic log
│
├── tasks/                                  ← /sdlc per-phase artifacts
│   └── task-{id}-{phase}.md                    (append-only per phase)
│
├── patterns/                               ← /retro: error patterns + hit_count mutate
│   └── pattern-{slug}.md                       (frontmatter: hit_count, severity, triggers)
│
├── lessons/                                ← /qc-dream: cross-task wisdom (dedupe + merge)
│   └── lesson-{slug}.md
│
├── decisions/                              ← /design: ADR-style design records
│   └── adr-{NNNN}-{slug}.md                    (sequential numbering)
│
├── chats/                                  ← Stop hook: daily-rollup
│   └── YYYY-MM-DD.md                           (1 file/day, all sessions appended)
│
├── attachments/                            ← images, binaries (flat with date prefix)
│   └── YYYY-MM-DD-<filename>.<ext>
│
├── claude-memory/                          ← hierarchical memory namespace
│   ├── MEMORY.md                               (top-level index — bot reads first)
│   ├── projects/<project-name>.md
│   └── topics/<topic>.md
│
├── README.md                               ← vault explainer (init-generated)
└── *.md                                    ← ad-hoc bot saves at root (encourage subdirs)
```

---

## Writer ownership matrix

| Subdir | Writer (skill / hook) | Frequency | Mutate? |
|--------|----------------------|-----------|---------|
| `.obsidian/` | Obsidian app + `InitVaultStructure` | once at init + Obsidian session | rewrite (Obsidian-managed) |
| `.obsidian/memctl/index.db` | `IngestOperator`, `AddOperator`, `CaptureOperator` | every write | reindex incremental |
| `.obsidian/memctl/models/` | `GemmaEmbeddingEngine` (auto-download once) | first use | rare (only re-download) |
| `.obsidian/memctl/hook.log` | `HookLog.Append()` | every hook trigger | append-only |
| `tasks/` | /sdlc orchestrator + each phase skill | per phase transition | append-only (file per phase) |
| `patterns/` | /retro post-merge | per task | mutate `hit_count` field |
| `lessons/` | /qc-dream | per-task mini-dream + per-sprint full-dream | dedupe + merge body |
| `decisions/` | /design when phase 2 produces design doc | per task | append-only (ADR numbering) |
| `chats/` | Stop hook (`memctl capture`) | every assistant response | append into daily file |
| `attachments/` | tool/hook output | as-needed | append-only |
| `claude-memory/MEMORY.md` | /qc-dream consolidation | per-sprint | rewrite (compress) |
| `<vault>/*.md` | `AddOperator` (`/memctl-save`), `CaptureOperator` | bot save | append/create |

---

## Read scan rules

`EnumerateMarkdownFiles(vaultPath)` (in `ObsidianVaultReader`) returns ALL `.md` recursively, EXCLUDING `.obsidian/`. Runtime files inside `.obsidian/memctl/` are auto-excluded (parent excluded).

`GrepOperator` follows same exclude rule.

This means everything in `tasks/`, `patterns/`, `lessons/`, `decisions/`, `chats/`, `attachments/` (only `.md` ones), `claude-memory/`, AND root `*.md` are indexed and searchable.

---

## Promotion ladder

```
patterns/<slug>.md  (hit_count >= 3)  →  lessons/<slug>.md
                                              ↓
                                  (validated 3+ projects, manual or auto)
                                              ↓
                              ~/.claude/memory/long/golden_rules.md
                                  (cross-vault, machine-wide)
```

`/qc-dream` runs the patterns→lessons promotion automatically based on `hit_count`. golden_rules promotion is currently manual review.

---

## Note frontmatter format

```markdown
---
title: Decision: use Postgres
weight: 1.5            # higher = surfaces first in recall (default 1.0)
tags: [architecture, decision]
created: 2026-05-01
hit_count: 1           # only for patterns/
---

Body content here. Wikilinks [[other-note]] supported. Obsidian renders.
```

`memctl ingest` parses frontmatter via hand-parsed YAML (not YamlDotNet — AOT compat, see #24 retro).

---

## Migration from V1

V1 placed `.obsidian/` and `.memctl/` as siblings at vault root, polluting index with non-memory `.md` files. V2.1 fixes this. See:

- See `docs/memctl.md` "Upgrading from V1" section — manual `mv .obsidian + .memctl → .archived-v1-vault/` then fresh `memctl init`.
- Backlog #32 ARCHIVED — V2.1 is hard cutover, no automated migration.

---

## Cosmetic deferrals (see `backlog/wishlist.md`)

- `chats/` archival by year (trigger: > 365 daily files)
- `attachments/` sharding by month (trigger: ~100 files)
- `tasks/` sharding by epic (trigger: > 100 task files)

All flat-for-now. Promote to backlog tasks when threshold actually hit.
