---
id: 15
type: task
title: MCP tool schema completeness — inputSchema + outputSchema reference Boundary DTO
status: Done
priority: high
tags:
- mcp
- api-contract
- plugin-ux
- claude-code
created: 2026-04-30
updated: 2026-04-30
---

## Description

memctl expose 13 MCP tool cho Claude Code consume. Hiện trạng: nhiều tool có `inputSchema` rời rạc / không đủ; `outputSchema` không khai báo (MCP optional nhưng giúp client validate).

Sau task #14 (Boundary DTO contract), cần update mọi MCP tool để:
- `inputSchema` đầy đủ field, type, required, description.
- `outputSchema` reference shape của `MemctlResult { success, action, message, data: <typed DTO> }`.
- Tool description rõ use case (giúp Claude Code chọn đúng tool).

## Mục tiêu

Claude Code khi đọc tool list từ MCP `tools/list`:
- Hiểu chính xác input shape → không guess field name.
- Hiểu output shape → parse response confident.
- Chọn đúng tool theo use case.

## Implementation

**File modify:** `src/memctl/Operators/McpServerOperator.cs`

Per tool, update:
```json
{
  "name": "add",
  "description": "Create a new note in the vault. Use when capturing a decision, finding, or reusable insight.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "text":   { "type": "string", "description": "Note content (markdown body)" },
      "title":  { "type": "string", "description": "Optional title; auto-extracted from first H1 if omitted" },
      "tags":   { "type": "array", "items": { "type": "string" } },
      "folder": { "type": "string", "description": "Subfolder relative to vault root" }
    },
    "required": ["text"]
  },
  "outputSchema": {
    "$ref": "#/definitions/MemctlResult",
    "properties": {
      "data": { "$ref": "#/definitions/NoteDto" }
    }
  }
}
```

13 tool cần audit + update:
- `add`, `append`, `update`, `delete` (vault writes)
- `get`, `list`
- `search`, `search_semantic`, `search_text`, `search_tags`, `search_links`, `search_date`
- `set_weight`, `tags`, `stats`

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | Mọi MCP tool có `inputSchema` với required + description đầy đủ | Inspect `tools/list` response, assert mỗi tool có description ≥ 30 chars + inputSchema.required ≠ null |
| FR-2 | Mọi MCP tool có `outputSchema` reference DTO Boundary | Inspect `tools/list`, assert outputSchema reference exists |
| FR-3 | Tool description ghi rõ use case (when to use) | Manual review: mỗi description chứa "Use when..." hoặc equivalent |
| NFR-1 | Build pass 0 warning, 0 error | `dotnet build` |
| NFR-2 | MCP `initialize` response không phá MCP protocol spec | Test với MCP Inspector |

## Out of Scope

- Refactor McpServerOperator sang Implementations/Mcp (task #21).
- Versioning schema (task #22).

## Dependencies

- **Blocked by task #14** (Boundary DTO contract phải có trước để outputSchema reference được).

## Effort

~3-4h: audit 13 tool + viết schema + test với MCP Inspector.

## Comments

**2026-04-30 09:13 user:** Phase 6: merged. All 13 MCP tools have outputSchema + Use-when descriptions.
