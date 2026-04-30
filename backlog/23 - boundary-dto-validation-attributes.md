---
id: 23
type: task
title: Boundary DTO validation attributes — input validation tại system edge
status: Done
priority: low
tags:
- boundary
- validation
- api-contract
- safety
created: 2026-04-30
updated: 2026-04-30
---

## Description

A.D.D V3 Boundary doc:
> **Responsibility**: System ingress/egress, external contracts
> **Contains**: DTOs, Boundary Events, **input validation**

Hiện tại `Boundary/MemctlResult.cs` các DTO không có validation attribute. Nếu external consumer gửi input invalid (e.g. weight ngoài [0, 2.0], date format sai, tag rỗng) → Operator phát hiện trễ, error message không nhất quán.

## Mục tiêu

Validation tại Boundary edge — fail fast với error message rõ ràng trước khi vào Operator.

## Phạm vi

Cần xác định những DTO nào là **input** (Claude Code → memctl) vs **output** (memctl → Claude Code). Hiện tại chỉ có output DTO. Input DTO chưa được khai báo (input qua CLI args / MCP tool args trực tiếp).

**Phương án:**

**A. Tạo input DTO riêng trong Boundary**
- `CreateNoteRequest`, `SearchRequest`, `SetWeightRequest`, ...
- Validation attributes: `[Required]`, `[Range]`, `[StringLength]`, `[RegularExpression]`.
- Bootstrap (CLI) hoặc McpServerAdapter map từ args/MCP params sang Request DTO → validate → pass to Operator.

**B. Validation logic ở Operators**
- Operator method check input thủ công.
- Đơn giản hơn, không cần DTO mới.
- Nhưng vi phạm A.D.D ("input validation" thuộc Boundary).

Recommend **A**.

## Implementation

### Files to CREATE in Boundary

```
src/memctl/Boundary/Requests/
  CreateNoteRequest.cs
  AppendNoteRequest.cs
  SearchRequest.cs
  SetWeightRequest.cs
  DecayRequest.cs
  IdentitySetRequest.cs
  ...
```

Example:
```csharp
public sealed class SetWeightRequest {
    [JsonPropertyName("id")]
    [Required, StringLength(256, MinimumLength = 1)]
    public string Id { get; init; } = "";

    [JsonPropertyName("weight")]
    [Range(0.0, 2.0)]
    public float Weight { get; init; }
}
```

### Files to MODIFY

- `src/memctl/Bootstrap/Program.cs` — CLI handler map args → Request → validate → Operator.
- `src/memctl/Implementations/Mcp/McpServerAdapter.cs` (hoặc McpServerOperator hiện tại) — MCP tool handler tương tự.
- Operator signature đổi từ primitive args sang Request DTO (e.g. `WeightOperator.Execute(SetWeightRequest req)`).

### Validation runner

Use `System.ComponentModel.DataAnnotations.Validator.TryValidateObject` — built-in BCL.

```csharp
var ctx     = new ValidationContext(request);
var results = new List<ValidationResult>();
if (!Validator.TryValidateObject(request, ctx, results, validateAllProperties: true))
    return MemctlOutcome.Fail(action, string.Join("; ", results.Select(r => r.ErrorMessage)));
```

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | Mỗi command có Request DTO trong Boundary với validation attribute | grep `Memctl.Boundary.Requests` files |
| FR-2 | Invalid input trả error message với field name + reason | Test với weight=3.0 → error "Weight must be in range [0, 2.0]" |
| FR-3 | Valid input pass validation, Operator nhận Request DTO | unit test |
| FR-4 | MCP tool input args validate trước khi dispatch | MCP test với arg invalid |
| NFR-1 | Validation overhead <5ms per request | benchmark |
| NFR-2 | Build pass | `dotnet build` |
| NFR-3 | A.D.D: Boundary depend on nothing (validation attributes là BCL only) | manual review imports |

## Out of Scope

- Custom validation attribute (chỉ dùng built-in).
- Async validation (e.g. check note exists qua repo).
- Cross-field validation (e.g. from < to).

## Dependencies

- **Blocked by task #14** (Boundary contract phải có trước).
- Soft depend task #15 (MCP schema phải reference Request DTO).

## Effort

~4-5h (audit + viết Request DTO cho 25+ command).

## Comments

**2026-04-30 09:26 user:** Phase 6: merged. Request DTO + validation pattern established. weight/decay/add wired. Other commands follow-up.
