---
id: 12
type: task
title: 'G2: Proactive injection — memctl context-inject + UserPromptSubmit hook'
status: Todo
priority: high
created: 2026-04-19
updated: 2026-04-19
---

## Dependencies

- `INoteIndex.Search()` + `INoteIndex.GetAll()` đã có — không cần thêm port
- Standalone — không depends on G1/G5

## Description

Bot phải chủ động gọi `list`/`search` để load context. Nó có thể skip, nhất là khi task không liên quan rõ ràng đến memory. G2 loại bỏ hoàn toàn dependency này bằng cách dùng Claude Code `UserPromptSubmit` hook: trước khi bot process prompt, hook tự động inject relevant memory context vào conversation.

New command `memctl context-inject`:

**Files to create/modify:**
- NEW: `src/memctl/Operators/ContextInjectOperator.cs`
- MODIFY: `src/memctl/Bootstrap/Program.cs` — register `context-inject` subcommand

**Algorithm:**
1. Đọc stdin: if JSON → extract first string field; else treat as plain text prompt
2. Vault auto-detect từ process cwd
3. Nếu vault không tồn tại: write empty string to stdout, exit 0
4. Extract keywords: split on whitespace+punctuation, lowercase, deduplicate, filter stop words
5. If keywords non-empty: `index.Search(keywords, limit=6)` → search_results
6. `index.GetAll(limit=3)` → list_results (sorted by weight desc)
7. Dedup: merge search_results + [list_results not already in search_results by id]
8. If total == 0: write empty string to stdout, exit 0
9. Format: `## Memory Context\n\n{for each note: ### {title}\n{content truncated at 500 chars}\n\n}`
10. Write to stdout, exit 0

Hook config (user docs):
```json
// ~/.claude/settings.json
{
  "hooks": {
    "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": "memctl context-inject" }] }]
  }
}
```

## Acceptance Criteria

- `memctl context-inject` đọc user prompt từ stdin theo Hook Protocol v1 `before-prompt` event: plain UTF-8 text
- Nếu detect stdin là JSON: extract string field đầu tiên làm prompt text (extensibility)
- stdout là markdown context block hoặc empty string — client prepend vào prompt
- Extract keywords: lowercase, deduplicate, filter stop words (a, the, is, and, ...)
- Chạy `search <keywords> --limit 6` (primary) + `list --limit 3` (secondary) internally
- Search-first: search results fill đầu tiên; list chỉ append nếu chưa trùng (dedup by note id)
- Nếu keywords rỗng hoặc search trả về 0 kết quả: fallback `list --limit 6`
- Output là markdown context block: `## Memory Context\n{notes}`
- Exit 0 khi vault không tồn tại — output empty string, không block hook
- Exit 0 khi stdin rỗng — output empty string
- Exit 0 khi cả search lẫn list đều rỗng — output empty string (không inject gì)
- Format mỗi note: `### {title}\n{content}\n` (truncate content > 500 chars)
- `memctl context-inject --dry-run` → print context block to stdout, no actual injection (same behavior, for testing)
- Docs: ví dụ UserPromptSubmit hook config trong docs/memctl.md
