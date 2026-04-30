---
id: 16
type: task
title: Search variant disambiguation — guide Claude Code chọn đúng search tool
status: Done
priority: normal
tags:
- mcp
- plugin-ux
- claude-code
- dx
created: 2026-04-30
updated: 2026-04-30
---

## Description

memctl expose 6 search variant: `search`, `search-semantic`, `search-text`, `search-tags`, `search-links`, `search-date`. Claude Code phải tự chọn variant theo intent — không có guide → có thể chọn sai (vd. dùng `search-text` cho query mơ hồ thay vì `search-semantic`).

## Mục tiêu

Claude Code chọn đúng search tool ngay lần đầu, không cần thử-sai.

## Hai phương án

### Phương án A — Decision tree trong tool description (preferred)

Mỗi MCP tool description chứa hint use case rõ ràng:

| Tool | Description |
|---|---|
| `search` | Default hybrid (BM25 + semantic, RRF fusion). Use when query is general or you don't know the exact terms. |
| `search_semantic` | Pure vector similarity. Use when query is conceptual ("find notes about distributed consensus") and exact words may not appear in notes. |
| `search_text` | BM25 full-text. Use when you know exact phrase or proper noun ("Raft", "VAULT_PATH"). |
| `search_tags` | Filter by tag. Use when user mentions a tag explicitly ("notes tagged 'crypto'"). |
| `search_links` | Wikilink graph traversal. Use to find notes linked from/to a specific note. |
| `search_date` | Time range filter. Use when user asks "what did I work on last week". |

### Phương án B — Unify dưới `search` với `--mode`

Ẩn variant cũ ở MCP layer, chỉ expose `search` với param `mode: hybrid|semantic|text|tag|link|date`. Internal vẫn route tới các Operator riêng. CLI giữ variant riêng cho power user.

Pro: ít tool hơn, dễ chọn (1 quyết định 2 chiều: search có/không + mode nào).
Con: breaking change MCP API.

## Implementation

**Phương án A (đề xuất):**
- Modify `src/memctl/Operators/McpServerOperator.cs` — update tool description per spec table.
- Optional: thêm tool `search_help` trả markdown bảng trên (Claude Code gọi khi unsure).

**Phương án B:**
- Modify McpServerOperator: gộp 6 tool thành 1 + dispatcher.
- Update README doc breaking change.

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | Tool description mỗi search variant chứa "Use when..." kèm ví dụ cụ thể | Manual review |
| FR-2 | (Nếu phương án B) Tool `search` accept `mode` enum 6 giá trị | MCP `tools/list` schema check |
| FR-3 | Test scenarios: query exact phrase → Claude Code chọn `search_text`; query concept → chọn `search_semantic` | LLM judge với 5 query mẫu |
| NFR-1 | Build pass | `dotnet build` |
| NFR-2 | CLI giữ variant riêng (backward compat) | `memctl search-text ...` vẫn hoạt động |

## Out of Scope

- Đổi RRF fusion algorithm.
- Multi-vault search.

## Dependencies

- Soft depend task #15 (MCP schema cleanup) — nên làm cùng.

## Effort

~2h cho phương án A, ~5h cho phương án B.

Recommend **A** (low risk, đủ giải quyết confusion).

## Comments

**2026-04-30 09:14 user:** Phase 6: merged. search_help MCP tool added; per-tool 'Use when' descriptions also live in tool list (from #15).
