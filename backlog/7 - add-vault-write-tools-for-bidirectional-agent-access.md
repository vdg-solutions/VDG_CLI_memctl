---
id: 7
type: task
title: Add vault write tools for bidirectional agent access
status: Done
priority: high
created: 2026-04-17
updated: 2026-04-19
---

## Description

Extend memctl MCP server with write-side tools so agents can read AND write vault notes during SDLC sessions. Currently agents can only consume vault knowledge; they cannot persist decisions, spec fragments, or insights back to the vault.

## Context

After task #6, memctl covers the full read stack (L0 identity, L1 list, L2 search, L3 semantic). The write side is completely missing — agents cannot create, update, or annotate notes via MCP.

Use cases:
- Agent saves spec decisions or design rationale as vault notes
- Agent appends session insights to existing notes
- Agent tags notes with session metadata (e.g. #sdlc-used, #reviewed)
- Agent pins important notes via weight from MCP (not just CLI)

## Requirements (to be fleshed out in /spec)

- MCP tool: `create` — create a new note in the vault
- MCP tool: `update` — overwrite content of an existing note
- MCP tool: `append` — append text to an existing note (non-destructive)
- MCP tool: `set_weight` — set note importance weight from MCP (currently CLI-only)
- All write tools must re-ingest the affected note into the index after write
- Atomic writes (no partial state if process is interrupted)
- Vault path safety — writes must stay within vault root (no path traversal)

## Open Questions (for autoresearch)

1. File format: write raw markdown or support YAML frontmatter upsert?
2. Re-ingest strategy: full vault re-ingest vs single-note upsert into index?
3. Conflict handling: what if a note is modified externally between read and write?
4. ID assignment for new notes: use title-slug, uuid, or timestamp-based?


## Pipeline Complete

- Phase 1: Spec — docs/specs/7-spec.md (19 FRs, 4 NFRs)
- Phase 2: Design — docs/designs/7-design.md (autoresearch 8/8 baseline)
- Phase 3: Build — 0 errors, 0 warnings
- Phase 4: QC — 5.0/5, 12/12 smoke scenarios, 0 retry loops
- Phase 5: Review — APPROVE 4.9/5, 0 critical/high issues
- Phase 6: Merged to main (2026-04-17)

## Comments

**2026-04-19 10:19 user:** ## Description
Bot phải chủ động gọi list/search để load context — có thể skip khi task không liên quan rõ ràng đến memory. Proactive injection bơm context vào mọi conversation turn mà không cần bot làm gì.

## Implementation
New command `memctl context-inject`:
1. Đọc user prompt từ stdin
2. Extract keywords: bỏ stopwords, lấy N từ có trọng lượng nhất (position-weighted, không cần LLM)
3. Chạy `list --limit 5` (top weighted notes)
4. Chạy `search <keywords> --limit 3`
5. Format thành context block → stdout (Claude Code inject vào conversation trước khi bot process)
6. Nếu vault chưa tồn tại: exit 0, output rỗng (không block)

## Acceptance Criteria
- `echo 'how does vault auto-detect work?' | memctl context-inject` → stdout chứa formatted context block với top notes liên quan
- Context block format: markdown, bắt đầu bằng `## Vault Context`, có list notes + snippets
- Khi vault trống hoặc không tồn tại: exit 0, stdout rỗng
- Latency < 500ms (không block user prompt)
- Docs cập nhật: ví dụ UserPromptSubmit hook config
