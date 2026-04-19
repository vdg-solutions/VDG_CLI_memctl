---
id: 6
type: task
title: Add Layer 0 identity note for MCP context bootstrapping
status: Done
priority: high
created: 2026-04-17
updated: 2026-04-19
---

## Description

Implement a Layer 0 "identity note" concept — a designated note that MCP clients load first in every session, before any tool call or search. Analogous to MemPalace's `~/.mempalace/identity.txt` but integrated natively into the Obsidian vault and weight system.

## Context

MemPalace's 4-layer memory stack:
- L0: Identity file (~50–100 tokens) — loads every session unconditionally
- L1: Top-N important (weight/access_count) — `list` tool already handles this (task #4+3)
- L2: Topic-scoped — `search` + `--folder` already handles this (task #2+3)
- L3: Deep semantic — `search`/`search_semantic` already handles this

memctl already has L1, L2, L3. Only L0 is missing.

## Requirements (to be fleshed out in /spec)

- Convention for designating a note as the "identity" note (e.g. a special tag `#memctl-identity`, or a special filename like `_identity.md`, or a metadata entry in the SQLite index)
- `memctl mcp` server should expose identity note retrieval as a dedicated MCP resource or as a `get_identity` tool
- `memctl identity set <id-or-path>` command to designate a note as identity
- `memctl identity get` to retrieve the current identity note
- Identity note should be returned in `tools/list` response or `initialize` response so MCP clients can load it automatically on connection
- Weight system integration: identity note gets pinned weight (auto-set to 1.0 or special sentinel)
- Single vault = single identity note (not per-topic)

## Open Questions (for autoresearch)

1. Best injection point: MCP `initialize` response (serverInfo.instructions), dedicated `get_identity` tool, or MCP resources (resource://identity)?
2. Convention for designation: special tag vs special filename vs SQLite metadata entry?
3. Should identity note be excluded from `list`/`GetAll()` sort (always first) or treated as weight=1.0 note (naturally first)?
4. What happens when no identity note is set — silent skip or error?

## Comments

**2026-04-19 10:19 user:** ## Description
Bot phải chủ động gọi create/append để lưu memory — nó thường quên. Auto-capture loại bỏ dependency này hoàn toàn.

## Implementation
1. Thêm `--auto` flag vào `AddTurnOperator`: đọc conversation context từ Claude Code hook env vars (CLAUDE_HOOK_SESSION_ID, CLAUDE_HOOK_TRANSCRIPT_PATH hoặc stdin JSON)
2. Filter signal khỏi noise: chỉ capture khi transcript có decision/finding/pattern keywords — không capture mọi exchange
3. Document Claude Code Stop hook config trong docs/

## Acceptance Criteria
- `memctl add-turn --auto` đọc hook payload từ stdin (JSON format mà Claude Code inject vào Stop hook)
- Tự động tạo note hoặc append vào session note hiện tại
- Filter: không lưu exchanges rỗng, chỉ lưu khi content có nghĩa (>50 chars hoặc chứa decision markers)
- `memctl add-turn --auto` exit 0 ngay cả khi vault chưa tồn tại (silent fail, không block hook)
- Docs cập nhật: ví dụ Stop hook config cho ~/.claude/settings.json
