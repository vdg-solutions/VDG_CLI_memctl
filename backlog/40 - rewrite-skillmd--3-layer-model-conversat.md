---
id: 40
type: task
title: Rewrite SKILL.md — 3-layer model, conversation terminology, distill
status: In Progress
priority: high
tags:
- docs,skill,memory
created: 2026-05-07
updated: 2026-05-07
---

## Description

Rewrite `plugins/memctl-claude/skills/memctl/SKILL.md` theo **caveman compression style** (ref: github.com/juliusbrussee/caveman). Hiện tại ~880 dòng prose verbose — tốn tokens mỗi lần invoke. Target: ~200-250 dòng, ~70% reduction, zero substance loss.

## Caveman compression rules

Drop: articles, filler (`just/basically/really`), hedging, prose explanations có thể thay bằng table/fragment. Keep: tất cả commands, flags, contracts, examples. Pattern: `[thing] [action] [reason]`.

Thay prose → table wherever possible. Thay paragraph → bullet fragment. Code blocks unchanged.

## Content changes cần làm đồng thời

### 1. Terminology: session → conversation
- Hook Protocol v1 wire format: `"session_id"` → `"conversation_id"`
- Path `sessions/<date>-<id>.md` → `chats/<date>-<id>.md`
- Tag schema: `session,task-{id}` → `conversation,task-{id}`

### 2. Thêm 3-layer memory model
```
L1 Raw       chats/{date}-{id}.md          transcript, decays
L2 Distilled decisions/ patterns/ lessons/  long-term memory, weight ≥1.0
L3 Linked    wikilinks between L2 notes     memory graph → search-links
```

### 3. Thêm `memctl distill`
Sau khi #39 xong. Placeholder nếu làm trước.

### 4. Fix: `delete` MCP note
`(MCP only — no CLI equivalent)` → sai, CLI đã có.

### 5. Fix: Episodic memory row
`chats/ (daily YYYY-MM-DD.md)` → per-conversation `{date}-{id}.md`.

## Trước/sau example

**Trước (prose):**
> The model: You encode what matters → vault stores it → you recall it next session → periodic consolidation keeps it clean → old memories decay unless reinforced. Like human long-term memory.

**Sau (caveman):**
> Encode → vault stores → recall next conversation → consolidate → stale memories decay unless reinforced.

**Trước (command description prose):**
> Add a new note. With `--llm-*` flags, auto-generates tags and wikilinks.

**Sau:**
> Add note. `--llm-*` → auto-tags + wikilinks.

## Acceptance criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | Line count ≤ 250 (was ~880) | `wc -l SKILL.md` |
| AC-2 | All 15+ commands still documented (add, get, search, list, capture, ingest, distill, decay, lint, weight, delete, hook-status, fetch, organize, context-inject) | grep each command |
| AC-3 | All flags documented for each command | review flags section |
| AC-4 | 3-layer memory model table present | grep "Layer 1" |
| AC-5 | Zero "session" terminology — all replaced with "conversation" | grep -i "session" → 0 results |
| AC-6 | `memctl distill` section present (placeholder OK if #39 not done) | grep "distill" |
| AC-7 | `delete` command NOT marked "(MCP only)" | grep "MCP only" → 0 results |
| AC-8 | Episodic memory row shows per-conversation format `{date}-{id}.md` not `YYYY-MM-DD.md` | grep row in table |

## Files
- `plugins/memctl-claude/skills/memctl/SKILL.md`

## Dependency
Làm sau #39 để viết distill section đầy đủ. Hoặc làm trước với `distill` placeholder.