---
id: 18
type: task
title: Vault discovery debug — Claude Code biết tại sao vault auto-detect fail
status: Done
priority: low
tags:
- vault-locator
- plugin-ux
- claude-code
- dx
created: 2026-04-30
updated: 2026-04-30
---

## Description

`VaultLocator.FindVault` walk up từ cwd tìm folder chứa `.obsidian/`. Nếu cwd lạ (e.g. `/tmp`, home dir, project chưa có vault) → return null → command silent fail / fallback.

Claude Code không biết tại sao memory "biến mất" — do vault không tồn tại? Path sai? Permission?

## Mục tiêu

Cho Claude Code chủ động biết vault state khi memory empty.

## Phương án

**Existing:** `memctl status` đã trả vault path + readiness. Nhưng:
- Trả "no vault" thay vì giải thích why (cwd path? walk-up failed? permission?).
- MCP không expose verbose vault info.

**Proposal:**

1. Mở rộng `status` output:
```json
{
  "success": true,
  "action": "status",
  "data": {
    "vault": {
      "found": false,
      "search_path": "/tmp/some/cwd",
      "search_strategy": "walk-up from cwd",
      "checked_paths": ["/tmp/some/cwd", "/tmp/some", "/tmp", "/"],
      "hint": "Run 'memctl init --vault <path>' to create one, or cd to a folder containing .obsidian/"
    },
    "model": { ... },
    "index": { ... }
  }
}
```

2. MCP tool `status` expose verbose vault info luôn.

3. Khi MCP server start (initialize), return `serverInfo.instructions` chứa vault state hint nếu vault không thấy.

## Implementation

**File modify:**
- `src/memctl/Operators/StatusOperator.cs` — trả verbose vault info.
- `src/memctl/Implementations/Vault/VaultLocator.cs` — expose `FindVaultVerbose` trả paths checked.
- `src/memctl/Operators/McpServerOperator.cs` — `serverInfo.instructions` inject vault hint khi vault missing.

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | `memctl status` trả vault.found, vault.search_path, vault.checked_paths, vault.hint | Run từ cwd không có vault, parse JSON |
| FR-2 | MCP `initialize` response include vault hint trong `serverInfo.instructions` khi vault missing | Test MCP với cwd lạ |
| FR-3 | Hint text actionable: command cụ thể để tạo vault | Manual review |
| NFR-1 | Verbose info không leak path nhạy cảm (only show paths under home dir) | Manual review |
| NFR-2 | Build pass | `dotnet build` |

## Out of Scope

- Multi-vault routing.
- Auto-init vault khi không tìm thấy (đã có cho MCP via `RequireVaultOrInit`).

## Dependencies

- Soft depend task #14 (typed DTO cho status response).

## Effort

~2h.

## Comments

**2026-04-30 09:19 user:** Phase 6: merged. status verbose. MCP initialize hint skipped — RequireVaultOrInit makes MCP path always have a vault.
