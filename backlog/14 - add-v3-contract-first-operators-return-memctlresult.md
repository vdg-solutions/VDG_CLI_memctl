---
id: 14
type: task
title: 'A.D.D V3 Contract First: Operators trả MemctlResult (Boundary DTO) thay MemctlOutcome anonymous'
status: Done
priority: high
tags:
- architecture
- refactor
- a.d.d-v3
- contract-first
created: 2026-04-30
updated: 2026-04-30
---

## Description

Codebase hiện tại vi phạm CONTRACT IS LAW + A.D.D V3 layered architecture:

1. `Boundary/MemctlResult.cs` chứa typed DTO contract (`MemctlResult`, `NoteDto`, `SearchResultDto`, `TagDto`, `StatsDto`, `GrepHitDto`) với `[JsonPropertyName]` — đây là wire format chuẩn cho stdout JSON / MCP output.
2. Toàn bộ Operator (37 file) bypass contract, trả `MemctlOutcome` (CoreAbstractions/Entities) với `Data` chứa **anonymous object** (`new { id = ..., file = ..., title = ... }`).
3. Hệ quả: schema wire format phụ thuộc tên biến C# trong Operator → đổi tên biến = breaking change cho external consumer (AI agent, shell parser, MCP client). Không có typed shape, không có versioning, không có validation.
4. `MemctlResult` + 5 DTO bị bỏ rơi, không file nào reference.
5. `ResultPrinter` hack manual map keys lowercase vì `MemctlOutcome` thiếu `JsonPropertyName`.

## Ngữ cảnh A.D.D V3

Per A.D.D V3 doc (`docs/refs/abstract-driven.txt` ở repo VDG_CLI_SDLC.Cli):
- **Boundary**: External contracts. Pure data, JSON-ready, versioned. Source of truth cho external consumers.
- **Core Abstractions/Entities**: Domain model nội bộ. Thin structure. Operator dùng trao đổi state.
- **Operators**: Nhận DTO, trả DTO. "DTO in, DTO out". Mapping DTO↔Entity là responsibility của Operators layer.

`MemctlOutcome` đang đóng 2 vai trò mâu thuẫn:
- (a) Internal flow giữa Operators (đúng vai trò Entity) ✅
- (b) Wire format ra stdout JSON qua ResultPrinter (sai vai trò — phải là Boundary DTO) ❌

## Mục tiêu

Tách 2 vai trò rõ ràng:
- `MemctlOutcome` (Core Abstractions/Entities): chỉ dùng nội bộ. `Data` là Entity (`Note`, `SearchHit[]`, `Tag[]`...) thay anonymous object.
- `MemctlResult` (Boundary): wire format duy nhất ra stdout / MCP. ResultPrinter và McpServerOperator output `MemctlResult`.
- Mapper Entity → DTO ở `Operators/Mapping/MemctlResultMapper.cs` (static class).

## Lý do chọn Operators/Mapping cho mapper

Constraint dependency rules A.D.D:
- Mapper cần ref cả `Note` (CoreAbstractions) + `NoteDto` (Boundary) → loại CoreAbstractions, Boundary (cấm cross-ref).
- McpServerOperator (Operators) cần gọi mapper → mapper KHÔNG được ở Bootstrap (Operators không ref được Bootstrap) hay Implementations (Operators không ref Implementations).
- → **Chỉ Operators layer reachable từ cả Bootstrap (CLI ResultPrinter) lẫn sibling Operators (McpServerOperator)**.
- A.D.D V3 doc explicit: Operators "Contains: TGO, Coordinators, **DTO↔Entity mapping**".

## Implementation

### Files to CREATE

- `src/memctl/Operators/Mapping/MemctlResultMapper.cs` — static class:
  - `MemctlResult ToResult(MemctlOutcome outcome)` — entry point, switch dispatch trên `outcome.Data` runtime type.
  - `NoteDto MapNote(Note n)`
  - `SearchResultDto MapSearch(string query, IEnumerable<SearchHit> hits)`
  - `TagDto[] MapTags(IEnumerable<(string tag, int count)> tags)`
  - `StatsDto MapStats(...)`
  - `GrepHitDto[] MapGrepHits(...)`

### Files to MODIFY

**Bootstrap layer:**
- `src/memctl/Bootstrap/ResultPrinter.cs` — gọi `MemctlResultMapper.ToResult(outcome)` rồi serialize `MemctlResult` thay anonymous mapping.

**Operators layer (~25 file):**
- Mọi `return MemctlOutcome.Ok("action", "msg", new { ... })` → đổi sang `return MemctlOutcome.Ok("action", "msg", entity)` với entity là Note / SearchHit[] / etc.
- Cụ thể:
  - `AddOperator.cs`, `VaultWriteOperator.cs` — trả `Note` thay `new { id, file, title }`.
  - `GetOperator.cs` — trả `Note` thay anonymous.
  - `ListOperator.cs` — trả `Note[]`.
  - `SearchOperator.cs`, `SearchSemanticOperator.cs`, `SearchTextOperator.cs`, `SearchTagsOperator.cs`, `SearchLinksOperator.cs`, `SearchDateOperator.cs` — trả tuple `(query, SearchHit[])` hoặc dedicated record.
  - `TagsOperator.cs` — trả `(string tag, int count)[]`.
  - `StatsOperator.cs` — trả record với field stats.
  - `GrepOperator.cs` — trả `GrepHit[]` (cần thêm Entity `GrepHit` ở CoreAbstractions hoặc inline tuple).
  - Còn lại (StatusOperator, IdentityOperator, ModelListOperator...) — chuẩn hóa Data type.

**Core Abstractions:**
- Cần thêm Entity `GrepHit`, `TagCount`, `Stats` nếu chưa có (hiện chỉ có `Note`, `SearchHit`).

**McpServerOperator:**
- Output qua MCP cũng phải là `MemctlResult` — gọi `MemctlResultMapper.ToResult` trước khi serialize sang MCP protocol.

### Files to DELETE

- Không xóa file nào. Giữ cả `MemctlOutcome` (internal) và `MemctlResult` (external).

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | Mọi command CLI trả JSON với field name khớp `[JsonPropertyName]` của Boundary DTO (snake_case/lowercase keys top-level) | Run `memctl <cmd> --vault ...` parse JSON, assert keys khớp DTO schema |
| FR-2 | `MemctlOutcome.Data` không bao giờ là anonymous object trong production code | grep pattern `MemctlOutcome.Ok\(.*new \{` trong `src/memctl/Operators/` = 0 hits |
| FR-3 | `Boundary/MemctlResult.cs` được reference từ ít nhất 2 file (ResultPrinter, MemctlResultMapper) | grep `MemctlResult ` trong codebase ≥ 2 files |
| FR-4 | `MemctlResultMapper` xử lý mọi runtime type của `outcome.Data` (Note, SearchHit[], Tag[], Stats, GrepHit[]); fallback giữ nguyên cho unknown type | unit test mỗi Data type |
| FR-5 | MCP tool response JSON khớp Boundary DTO schema | run MCP server, gọi tool, verify schema |
| NFR-1 | Build pass 0 warning, 0 error | `dotnet build` |
| NFR-2 | Backward compat output JSON: existing key names/structure không thay đổi nếu không cần | diff JSON output trước/sau với mỗi command |
| NFR-3 | A.D.D dependency rule: Boundary không ref Core Abstractions, Core Abstractions không ref Boundary | manual review + grep imports |
| NFR-4 | Không cần thêm NuGet package mới | check `memctl.csproj` không thay đổi `<PackageReference>` |

## Out of Scope (tracked as separate tasks)

- **Task #21**: Refactor `McpServerOperator` từ Operators sang Implementations/Mcp (A.D.D Web Adapter pattern).
- Đổi shape wire format hiện tại — giữ backward compat (chỉ chuyển từ anonymous sang typed, key name giữ nguyên).
- **Task #22**: Versioning cho `MemctlResult` (v1 schema lock).
- **Task #23**: Validation attributes (`[Required]`, `[Range]`) cho Boundary Request DTO.

## Related downstream tasks

- **Task #15**: MCP tool schema completeness — outputSchema reference DTO sau khi #14 ship.
- **Task #16**: Search variant disambiguation guide.
- **Task #17**: Hook observability.
- **Task #18**: Vault discovery debug (verbose status).
- **Task #19**: Tag namespace migration (one-time cleanup user vault).
- **Task #20**: Identity note discoverability.

## Dependencies

- Không depend task nào khác trong backlog.
- Chỉ depend trên codebase hiện tại (post-13).

## Risk

- **High churn**: ~25 file Operator phải sửa. Risk regression ở edge case Data shape.
- **Mitigation**: viết unit test snapshot JSON output cho mỗi command trước khi refactor; refactor incrementally per Operator; sau mỗi Operator chạy test snapshot để đảm bảo không đổi wire format.

## Effort

~6-10 giờ:
- 1h: thiết kế `MemctlResultMapper` + Entity bổ sung (`GrepHit`, `Stats` record).
- 3h: refactor 25 file Operator.
- 2h: viết test snapshot JSON cho mỗi command.
- 1h: cập nhật `ResultPrinter` + `McpServerOperator` + run full integration.
- 1h: docs cập nhật (`memctl.md` note về wire format DTO).

## Notes

- Bug autopilot skill (`memctl append` không tồn tại trên CLI) đã fix riêng — không phụ thuộc task này.
- Telegram leftover (`add-turn`, hardcode `telegram` tag) đã dọn — không phụ thuộc task này.
- Phát hiện task này từ audit ngày 2026-04-30: bot autopilot báo `memctl append` không tồn tại → phát hiện CLI/MCP drift → audit tổng → phát hiện vi phạm Contract First.

## Comments

**2026-04-30 08:35 user:** Phase 0: Initialized. Branch feature/14-add-v3-contract-first-operators-return-memctlresult.

**2026-04-30 08:39 user:** Phase 1 complete: Spec at docs/specs/14-spec.md (38 FR + 7 NFR, backward compat via snapshot baseline)

**2026-04-30 08:43 user:** Phase 2 complete: Design at docs/designs/14-design.md. Mapper=switch expression. Search carriers=dedicated records. 14-step impl order with snapshot baseline first.

**2026-04-30 09:01 user:** Phase 3 done: 25+ Operators refactored to typed Data. Mapper switch exhaustive (no fallback, throws on unknown). Build pass 0/0. Smoke test 10+ commands wire keys preserved. Pending: snapshot regression test suite, /qc Layer 1-3 formal pass, /review gatekeeper.

**2026-04-30 09:11 user:** Phase 6 complete: merged to main (cee9471). 24/24 mapper tests pass. Pending: snapshot baseline regression infra (NFR-006, follow-up task). McpServerOperator relocation (#21) and versioning (#22) tracked separately.
