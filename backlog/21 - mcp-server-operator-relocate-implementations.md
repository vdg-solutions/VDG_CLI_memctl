---
id: 21
type: task
title: 'A.D.D V3: McpServerOperator → Implementations/Mcp (Web Adapter pattern)'
status: Done
priority: normal
tags:
- architecture
- refactor
- a.d.d-v3
- mcp
created: 2026-04-30
updated: 2026-04-30
---

## Description

`McpServerOperator.cs` hiện ở `src/memctl/Operators/`. Theo A.D.D V3 doc section "Web/REST API Integration":

> Web framework code (ASP.NET Controllers, Minimal API, Express routes, FastAPI routes) is **technology-specific** and belongs in **Implementations layer** as **Web Adapters**.

MCP stdio JSON-RPC là framework giao tiếp ngoài (giống HTTP framework). Theo A.D.D, McpServerOperator phải ở `Implementations/Mcp/` chứ không phải `Operators/`.

**Hệ quả vi phạm hiện tại:**
- Operators layer chứa technology-specific code (MCP protocol parsing, JSON-RPC envelope).
- Operators không còn framework-agnostic.
- Khó swap MCP transport sang HTTP/SSE mà không đụng Operator.

## Mục tiêu

McpServerOperator move sang `src/memctl/Implementations/Mcp/`:
- Đóng vai trò Web Adapter: nhận MCP request → gọi Operator (qua Port) → map response sang MCP format.
- Operators thực sự (Add, Search, Get, ...) framework-agnostic.

## Implementation

### Files to CREATE

- `src/memctl/Implementations/Mcp/McpServerAdapter.cs` — main entry, replace McpServerOperator.
- `src/memctl/Implementations/Mcp/Protocol/JsonRpcEnvelope.cs` — JSON-RPC types.
- `src/memctl/Implementations/Mcp/Tools/AddToolHandler.cs`, etc. — per-tool handler.
- `src/memctl/CoreAbstractions/Ports/IMcpServer.cs` — port (nếu cần expose interface).

### Files to MODIFY

- `src/memctl/Bootstrap/Program.cs` — `mcp` command resolve adapter từ Implementations thay Operators.

### Files to DELETE

- `src/memctl/Operators/McpServerOperator.cs` — move content sang Implementations/Mcp.

### Mapper reachability

Sau move, McpServerAdapter ở Implementations layer:
- ✅ Ref được CoreAbstractions (Note, SearchHit Entity).
- ✅ Ref được Boundary (NoteDto, SearchResultDto) — đây là system-edge adapter, A.D.D explicit allow.
- ✅ Có thể dùng `Operators/Mapping/MemctlResultMapper` (Adapter ref Operators? — KHÔNG! Implementations không ref Operators).

→ **Mapper phải move khỏi Operators** sau task này, hoặc duplicate logic. Cách giải:
- Option a: Move `MemctlResultMapper` sang `Implementations/Mapping/` — Implementations ref nhau OK.
- Option b: Tạo `Translator Port` ở `CoreAbstractions/Ports/IMemctlResultMapper`, implement ở `Implementations/Mapping/`. McpServerAdapter inject port. Bootstrap CLI cũng inject port.

Option **b** đúng A.D.D nhất.

### Caller chain sau refactor

```
Bootstrap (CLI) → Operator (e.g. AddOperator) → MemctlOutcome
Bootstrap (CLI) → IMemctlResultMapper.ToResult(outcome) → MemctlResult → ResultPrinter

Bootstrap (mcp cmd) → McpServerAdapter (Implementations/Mcp)
McpServerAdapter → Operator (qua port) → MemctlOutcome
McpServerAdapter → IMemctlResultMapper.ToResult(outcome) → MemctlResult → MCP wire
```

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | `Operators/McpServerOperator.cs` không còn tồn tại | `ls src/memctl/Operators/` |
| FR-2 | `Implementations/Mcp/McpServerAdapter.cs` tồn tại + chạy được | Run `memctl mcp`, send tools/list |
| FR-3 | Operators layer không reference MCP protocol type | grep `JsonRpc` trong `src/memctl/Operators/` = 0 hits |
| FR-4 | MCP response shape giữ nguyên (backward compat) | snapshot test pre/post refactor |
| FR-5 | `IMemctlResultMapper` port định nghĩa ở Core Abstractions | grep IMemctlResultMapper interface |
| NFR-1 | Build pass | `dotnet build` |
| NFR-2 | A.D.D dependency rule không vi phạm | manual review imports |

## Out of Scope

- Đổi MCP protocol version.
- Thêm MCP tool mới.

## Dependencies

- **Blocked by task #14** (Boundary DTO + mapper phải có trước).
- Hard refactor — nên làm sau khi task #14, #15 stable.

## Risk

- **High churn**: McpServerOperator file lớn (~500 line), có 13 tool handler. Move + tách thành nhiều file = dễ break.
- **Mitigation**: viết MCP integration test (gọi từng tool, verify response) trước refactor.

## Effort

~6-8h.

## Comments

**2026-04-30 09:29 user:** Phase 6: merged. McpServerAdapter at Implementations/Mcp. Operators layer free of MCP protocol code. Documented A.D.D leak: adapter still uses Operator classes directly (port extraction deferred).
