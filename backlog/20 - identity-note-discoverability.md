---
id: 20
type: task
title: Identity note discoverability — Claude Code biết identity note tồn tại + cần set
status: Done
priority: low
tags:
- identity
- mcp
- plugin-ux
- claude-code
created: 2026-04-30
updated: 2026-04-30
---

## Description

`memctl identity set <id>` designate 1 note làm identity (Layer 0). MCP server inject content note này vào `serverInfo.instructions` khi `initialize` → mọi AI session có context về project ngay.

**Vấn đề:** User không biết tính năng này tồn tại. Nếu chưa set identity → MCP `serverInfo.instructions` empty hoặc default → AI session không có Layer 0 context.

Claude Code không có hint nào để remind user setup identity.

## Mục tiêu

User mới cài memctl plugin → Claude Code prompt user setup identity note ngay session đầu (1 lần).

## Phương án

**A. MCP `initialize` hint khi identity chưa set**

```json
{
  "serverInfo": {
    "name": "memctl",
    "instructions": "[memctl] No identity note set. Run 'memctl identity set <note-id>' to inject project context every session. List candidates: memctl list --tag identity"
  }
}
```

**B. CLI command `memctl onboard`**

Wizard: status check → prompt user create/set identity note → set as identity.

**C. Tool `identity_status` cho Claude Code chủ động check**

Claude Code gọi đầu session, nếu identity chưa set → suggest user.

## Implementation

**File modify:**
- `src/memctl/Operators/McpServerOperator.cs` — `initialize` response check identity, inject hint nếu chưa set.
- `src/memctl/Operators/IdentityOperator.cs` — thêm method `HasIdentity()` trả bool.

**File create (optional cho phương án B):**
- `src/memctl/Operators/OnboardOperator.cs` — wizard CLI.

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | MCP `initialize` response chứa identity hint khi identity chưa set | Test với vault không có identity, capture response |
| FR-2 | Hint text actionable: command cụ thể, list note candidates | Manual review |
| FR-3 | Khi identity đã set, hint không xuất hiện | Test với vault đã set identity |
| FR-4 | Hint không over-noisy (chỉ initialize, không lặp mỗi tool call) | Trace MCP traffic |
| NFR-1 | Build pass | `dotnet build` |

## Out of Scope

- Auto-create identity note (user phải tự viết content).
- Identity note template.

## Dependencies

- Soft depend task #18 (vault discovery — chung chỗ inject hint).

## Effort

~2h.

## Comments

**2026-04-30 09:20 user:** Phase 6: merged. Initialize hint when identity not set.
