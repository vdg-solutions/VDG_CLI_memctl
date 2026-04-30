---
id: 22
type: task
title: MemctlResult versioning — lock v1 schema, breaking change qua v2
status: In Progress
priority: low
tags:
- api-contract
- versioning
- boundary
- long-term
created: 2026-04-30
updated: 2026-04-30
---

## Description

Sau task #14, `MemctlResult` (Boundary DTO) là wire format external. External consumer (Claude Code, AI agent, CI scripts, MCP clients) phụ thuộc field name + structure.

Hiện tại không có version trong response → upgrade memctl thay đổi DTO = breaking change silent. Consumer không biết schema đổi cho tới khi parse fail.

## Mục tiêu

Lock schema v1, expose version trong response, breaking change phải qua v2 explicit.

## Implementation

### MemctlResult v1

```csharp
public sealed class MemctlResult {
    [JsonPropertyName("schema_version")] public int     SchemaVersion { get; init; } = 1;
    [JsonPropertyName("success")]        public bool    Success       { get; init; }
    [JsonPropertyName("action")]         public string  Action        { get; init; } = "";
    [JsonPropertyName("message")]        public string  Message       { get; init; } = "";
    [JsonPropertyName("data")]           public object? Data          { get; init; }
}
```

### Per-DTO version

DTO con (NoteDto, SearchResultDto, ...) cũng có thể có `schema_version` field nếu evolve độc lập. Optional cho v1, mandatory từ v2.

### Migration strategy

- v1 → v2: DTO mới namespace `Memctl.Boundary.V2`. Operator mặc định trả v1 trừ khi `--api-version 2` flag.
- MCP: `serverInfo.protocolVersion` reflect schema version; tool description note version.
- Deprecation period: 6 tháng giữ v1 song song.

### Spec compliance

- Document v1 schema trong `docs/specs/wire-format-v1.md`.
- JSON Schema file: `docs/schemas/memctl-result-v1.json`.
- Test: snapshot per command, parse với JSON Schema validator.

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | Mọi response có top-level `schema_version: 1` | parse JSON, check field |
| FR-2 | Doc `docs/specs/wire-format-v1.md` mô tả full schema | manual review |
| FR-3 | JSON Schema file validate được mọi response actual | run validator with corpus of actual outputs |
| FR-4 | CHANGELOG.md ghi schema v1 lock date | grep CHANGELOG |
| NFR-1 | Adding new optional field không bump version (additive only) | manual review process doc |
| NFR-2 | Build pass | `dotnet build` |

## Out of Scope

- Implement v2 (chưa có nhu cầu).
- API gateway / proxy version translation.

## Dependencies

- **Blocked by task #14** (DTO contract phải stable trước khi lock).
- Soft depend task #15 (MCP schema).

## Effort

~3-4h (chỉ thêm field + doc; không thay logic).